using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LedBalloon.Core;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;

namespace LedBalloon.App.Controls;

/// <summary>
/// One segment's run of LEDs across, and the last couple of seconds of it downwards.
/// <para>
/// The photo answers "what will the house look like" and is bad at "what is this segment doing". A
/// run is drawn on the photo where it actually is — foreshortened, behind a downpipe, or diffused
/// into scallops that deliberately hide the individual LEDs — with a dozen others competing for the
/// same few hundred pixels.
/// </para>
/// <para>
/// The first version of this was one row: the run as it is this instant. That is faithful and, for a
/// good half of the effects, unreadable. Halloween Eyes on Porchline lights 8 LEDs out of 308 on 12%
/// of frames, so the honest single row is black nearly all the time and reads as a control that is
/// broken. Stacking the recent frames fixes it without inventing anything: a blink that lasted a
/// second becomes a mark you can see, instead of one you had to be looking at the right moment to
/// catch.
/// </para>
/// <para>
/// It also shows the one thing a single row cannot, which is movement. Chase comes out as diagonal
/// bands and their slope is its speed, so the slider being dragged has something to answer to. An
/// effect that does not move is rows that all match, which is the plain strip back again.
/// </para>
/// <para>
/// Deliberately ignores both brightnesses and the power switch. Those are answered by the
/// controller's slider with its percentage, by the switch in the top bar, and by the photo, which
/// goes dark when the house does. Folding them in would blank the one control showing what is being
/// built at exactly the moment there is no other way to see it.
/// </para>
/// </summary>
public sealed class StripPreview : Control
{
    /// <summary>
    /// Cap on LEDs sampled, since past this they are thinner than a pixel anyway.
    /// </summary>
    /// <remarks>
    /// A longer run is sampled rather than truncated, so the preview still covers all of it.
    /// </remarks>
    private const int MaxLeds = 400;

    /// <summary>Cap on rows of history, which is also the cap on work done per frame.</summary>
    private const int MaxRows = 160;

    /// <summary>The space between the two frames, so they read as two things rather than one.</summary>
    private const double Gap = 5;

    /// <summary>The unlit strip, which is also what shows between the lit LEDs.</summary>
    private const uint Unlit = 0xFF101116;

    /// <summary>What the segment is set to: effect, palette, colors and the sliders.</summary>
    public static readonly StyledProperty<WledSegment?> SegmentProperty =
        AvaloniaProperty.Register<StripPreview, WledSegment?>(nameof(Segment));

    /// <summary>
    /// How many LEDs the run has, from the layout rather than from the controller.
    /// </summary>
    /// <remarks>
    /// The length changes what an effect looks like and not just how wide it is drawn: Chase fits a
    /// fixed number of groups into whatever it is given, and a comet's tail is a proportion of the
    /// run. Previewing a 308-LED porch line at some convenient round number would be previewing a
    /// different effect.
    /// </remarks>
    public static readonly StyledProperty<int> LedCountProperty =
        AvaloniaProperty.Register<StripPreview, int>(nameof(LedCount));

    /// <summary>
    /// The controller's effect list, so the effect number resolves to the right effect.
    /// </summary>
    /// <remarks>
    /// By name rather than by number, for the same reason the photo does it: the numbers have moved
    /// between WLED versions, and the two boxes need not be running the same build.
    /// </remarks>
    public static readonly StyledProperty<IReadOnlyList<string>?> EffectNamesProperty =
        AvaloniaProperty.Register<StripPreview, IReadOnlyList<string>?>(nameof(EffectNames));

    /// <summary>The gradient the segment's palette resolves to, read from the controller.</summary>
    public static readonly StyledProperty<WledPalette?> PaletteProperty =
        AvaloniaProperty.Register<StripPreview, WledPalette?>(nameof(Palette));

    /// <summary>
    /// The controller's frame rate, since anything that trails or fades does so per frame.
    /// </summary>
    public static readonly StyledProperty<ControllerTiming?> TimingProperty =
        AvaloniaProperty.Register<StripPreview, ControllerTiming?>(nameof(Timing));

    /// <summary>
    /// How tall a band to give the run as it is this instant, above the history. Zero for none.
    /// </summary>
    /// <remarks>
    /// Drawn by this control rather than by a second one beside it, because two controls would mean
    /// two simulations - and two runs of an effect that uses randomness are two different pictures.
    /// A strip claiming to be the same run as the panel under it has to actually be the same run.
    /// </remarks>
    public static readonly StyledProperty<double> NowHeightProperty =
        AvaloniaProperty.Register<StripPreview, double>(nameof(NowHeight));

    static StripPreview() =>
        AffectsRender<StripPreview>(
            SegmentProperty, LedCountProperty, EffectNamesProperty, PaletteProperty, TimingProperty,
            NowHeightProperty);

    private readonly DispatcherTimer _clock;
    private readonly Stopwatch _since = Stopwatch.StartNew();

    /// <summary>
    /// The running effect, kept between frames.
    /// <para>
    /// Kept rather than rebuilt, because an effect that trails is a function of the frames before
    /// it and throwing the buffer away each repaint would throw away the trail. Restarted when the
    /// signature changes, so moving a slider shows the new setting rather than carrying on under
    /// the old one.
    /// </para>
    /// </summary>
    private EffectSimulation? _running;

    private WriteableBitmap? _image;

    /// <summary>The history, newest row first, one entry per LED sampled.</summary>
    private int[] _rows = [];

    private int _columns;
    private int _depth;

    /// <summary>
    /// What the run is showing: the effect, the palette and the colors.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="_run"/> because only this one makes the history wrong. Change the
    /// palette and the rows above are a picture of a segment that no longer exists, sitting under a
    /// heading that says they are this one's recent past - so they go. Change the speed and they are
    /// still this segment, running slower a moment ago, which is worth keeping: the slope changing
    /// down the panel is the clearest thing the slider does.
    /// </remarks>
    private string _look = string.Empty;

    /// <summary>Everything the effect reads, so a change of any of it starts the effect again.</summary>
    private string _run = string.Empty;

    private TimeSpan _lastTick;

    public StripPreview()
    {
        // Faster than the photo's 50ms: this is a bitmap blit rather than a photograph with every
        // fixture drawn over it, and one row of history per tick means the tick rate decides how
        // much time the panel covers.
        _clock = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnTick);

        // The bitmap is built at exactly the size it is drawn, so smoothing would only blur rows
        // that are meant to be one pixel each.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public WledSegment? Segment
    {
        get => GetValue(SegmentProperty);
        set => SetValue(SegmentProperty, value);
    }

    public int LedCount
    {
        get => GetValue(LedCountProperty);
        set => SetValue(LedCountProperty, value);
    }

    public IReadOnlyList<string>? EffectNames
    {
        get => GetValue(EffectNamesProperty);
        set => SetValue(EffectNamesProperty, value);
    }

    public WledPalette? Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public ControllerTiming? Timing
    {
        get => GetValue(TimingProperty);
        set => SetValue(TimingProperty, value);
    }

    public double NowHeight
    {
        get => GetValue(NowHeightProperty);
        set => SetValue(NowHeightProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clock.Stop();

        // Nothing is watching, so let it all go rather than stepping a strip nobody can see for the
        // rest of the session. The history is stale by the time anyone looks again anyway.
        _running = null;
        _look = string.Empty;
        _run = string.Empty;

        _image?.Dispose();
        _image = null;

        _rows = [];
        _columns = 0;
        _depth = 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        TimeSpan elapsed = _since.Elapsed;
        double delta = (elapsed - _lastTick).TotalMilliseconds;
        _lastTick = elapsed;

        _running?.Advance(delta);

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = new(Bounds.Size);

        if (bounds.Width < 4 || bounds.Height < 4)
        {
            return;
        }

        // Two frames when there is room for one: the run as it is this instant on top, and the
        // last few seconds under it. Separate rather than the top row of one picture, because the
        // top row of a waterfall is a line and what somebody wants to see is the strip.
        double band = NowHeight;
        bool split = band >= 6 && bounds.Height > band + 14;

        Rect top = split ? bounds.WithHeight(band) : default;
        Rect history = split
            ? new Rect(0, band + Gap, bounds.Width, bounds.Height - band - Gap)
            : bounds;

        var unlit = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x16));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x35)));

        if (split)
        {
            context.DrawRectangle(unlit, edge, new RoundedRect(top, 4));
        }

        context.DrawRectangle(unlit, edge, new RoundedRect(history, 4));

        if (LedCount < 1 || Segment is not { } wled)
        {
            return;
        }

        Restart(wled, LedCount);

        // Inside the frame, so it reads as sitting in the strip rather than as a bar with a line
        // round it. One bitmap pixel per screen pixel, so nothing is scaled.
        Rect inside = history.Deflate(new Thickness(2));
        int columns = Math.Clamp(Math.Min(LedCount, (int)inside.Width), 1, MaxLeds);
        int depth = Math.Clamp((int)inside.Height, 1, MaxRows);

        Resize(columns, depth);
        Scroll(wled);
        Draw(context, inside, top.Height > 0 ? top.Deflate(new Thickness(2)) : null);
    }

    /// <summary>Throws the history away when it would no longer line up with what is drawn.</summary>
    private void Resize(int columns, int depth)
    {
        if (_columns == columns && _depth == depth && _image is not null)
        {
            return;
        }

        _columns = columns;
        _depth = depth;
        _rows = new int[columns * depth];

        Array.Fill(_rows, unchecked((int)Unlit));

        _image?.Dispose();
        _image = new WriteableBitmap(
            new PixelSize(columns, depth),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
    }

    /// <summary>Pushes the history down a row and reads the run into the top of it.</summary>
    private void Scroll(WledSegment wled)
    {
        Array.Copy(_rows, 0, _rows, _columns, (_depth - 1) * _columns);

        double span = 1d / _columns;

        for (int i = 0; i < _columns; i++)
        {
            RgbColor color = ColorAt((i + 0.5) * span, span, wled);

            _rows[i] = color.R + color.G + color.B == 0
                // The unlit strip rather than black, so a dark LED matches the frame around it.
                ? unchecked((int)Unlit)
                : unchecked((int)(0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));
        }
    }

    private void Draw(DrawingContext context, Rect inside, Rect? now)
    {
        if (_image is not { } image)
        {
            return;
        }

        using (ILockedFramebuffer buffer = image.Lock())
        {
            // Row by row rather than in one copy, because a locked framebuffer may pad its rows.
            for (int row = 0; row < _depth; row++)
            {
                Marshal.Copy(_rows, row * _columns, buffer.Address + (row * buffer.RowBytes), _columns);
            }
        }

        context.DrawImage(image, new Rect(0, 0, _columns, _depth), inside);

        if (now is { } band)
        {
            // The newest row of the same picture, stretched up the band. Taken from the one bitmap
            // rather than drawn again, so the two can never disagree about what the run is doing.
            context.DrawImage(image, new Rect(0, 0, _columns, 1), band);
        }
    }

    /// <summary>
    /// What one LED is showing, averaged over the stretch of the run it stands for.
    /// </summary>
    /// <param name="span">
    /// How much of the run this sample covers. Wider than one LED whenever the run is longer than
    /// the strip is wide, and averaging then matters: reading one LED in three would lose an effect
    /// that lights a few at a time, because most of its dots would fall between the samples.
    /// </param>
    private RgbColor ColorAt(double t, double span, WledSegment wled)
    {
        // An effect the app can run has already decided what every LED is showing, so there is
        // nothing to approximate: read the pixels.
        if (_running is { } running)
        {
            RgbColor[] pixels = running.Segment.Pixels;
            int last = pixels.Length - 1;

            int from = Math.Clamp((int)((t - (span / 2)) * last), 0, last);
            int to = Math.Clamp((int)Math.Ceiling((t + (span / 2)) * last), from, last);

            int r = 0, g = 0, b = 0;
            for (int i = from; i <= to; i++)
            {
                // Read the way the run is wired, so a reversed segment is drawn the way
                // round the house shows it rather than mirrored.
                RgbColor pixel = running.Segment.AsWired(i);
            
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
            }

            int count = to - from + 1;

            return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }

        // An effect nobody has ported. Its colors are known and its movement is not, so the palette
        // slides along the run at the pace the speed slider asks for - a family resemblance to most
        // WLED effects and an impersonation of none of them. The panel says so in words.
        if (Palette is { } palette && wled.Palette is > 0)
        {
            double along = t + PhaseFor(wled);
            along -= Math.Floor(along);

            return palette.ColorAt(
                along,
                wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White,
                wled.Colors is { Length: > 1 } ? wled.SecondaryColor : RgbColor.Black,
                wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]) : RgbColor.Black);
        }

        return wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White;
    }

    /// <summary>How far the palette has slid along the run by now, in palette widths.</summary>
    private double PhaseFor(WledSegment wled)
    {
        if (wled.Effect is not > 0)
        {
            // Solid does not move, and cycling it would be inventing motion that is not there.
            return 0;
        }

        double cyclesPerSecond = 0.06 + ((wled.Speed ?? 128) / 255d * 0.8);
        double phase = _since.Elapsed.TotalSeconds * cyclesPerSecond;

        return wled.Reverse == true ? -phase : phase;
    }

    /// <summary>Starts the effect, or restarts it when anything it reads has changed.</summary>
    private void Restart(WledSegment wled, int leds)
    {
        string look = string.Join(
            '/',
            wled.Effect, wled.Palette, Palette?.Stops.Count ?? -1,
            wled.Colors is { Length: > 0 } ? wled.PrimaryColor.ToHex() : "-",
            wled.Colors is { Length: > 1 } ? wled.SecondaryColor.ToHex() : "-",
            wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]).ToHex() : "-");

        if (_look != look)
        {
            _look = look;

            // The rows above are a picture of a segment that no longer exists, under a heading
            // saying they are this one's recent past. Better blank than wrong.
            Array.Fill(_rows, unchecked((int)Unlit));
        }

        string run = string.Join('/', look, wled.Speed, wled.Intensity, wled.Reverse, leds);

        if (_run == run)
        {
            return;
        }

        _run = run;

        ControllerTiming timing = Timing is { IntervalMilliseconds: > 0 } known
            ? known
            : new ControllerTiming(
                EffectSimulation.DefaultFrameMilliseconds, FrameTime.MinimumFrameDelay);

        _running = EffectLibrary.Simulate(
            wled,
            Math.Min(leds, MaxLeds),
            EffectNames,
            Palette,
            timing.IntervalMilliseconds,
            timing.FrameTimeMilliseconds);
    }
}
