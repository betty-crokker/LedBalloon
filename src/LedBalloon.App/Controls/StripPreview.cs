using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// One segment's run of LEDs, straightened out.
/// <para>
/// The photo answers "what will the house look like" and is bad at "what is this segment doing". A
/// run is drawn on the photo where it actually is — foreshortened, behind a downpipe, or diffused
/// into scallops that deliberately hide the individual LEDs — with a dozen others competing for the
/// same few hundred pixels.
/// </para>
/// <para>
/// This had a second band under it for a while, stacking the recent frames so that a blink too short
/// to catch left a mark you could see - Halloween Eyes lights 8 LEDs out of 308 on about an eighth
/// of frames - and so that movement showed as a slope. It was dropped: a second picture of the same
/// run, under a heading about time, was read as a second thing the effect was doing. What it bought
/// is really worth having for about a dozen effects and cost confusion on all of them, and the house
/// is the honest answer for the rest.
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

    static StripPreview() =>
        AffectsRender<StripPreview>(
            SegmentProperty, LedCountProperty, EffectNamesProperty, PaletteProperty, TimingProperty);

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

    /// <summary>The run as it is this instant, one entry per LED sampled.</summary>
    private int[] _row = [];

    private int _columns;

    /// <summary>Everything the effect reads, so a change of any of it starts the effect again.</summary>
    private string _run = string.Empty;

    private TimeSpan _lastTick;

    /// <summary>
    /// Whether to draw the counters over the strip. Off unless LEDBALLOON_PREVIEW_DIAGNOSTICS=1.
    /// <para>
    /// Here because "the preview is not moving" has three quite different causes and they are
    /// indistinguishable by looking: the clock is not firing, the simulation is being thrown away
    /// and rebuilt, or it is running and the effect really is that slow. One reading tells them
    /// apart - ticks climbing with frames stuck is the first, restarts climbing is the second,
    /// and everything climbing is the third.
    /// </para>
    /// </summary>
    private static readonly bool Diagnostics =
        Environment.GetEnvironmentVariable("LEDBALLOON_PREVIEW_DIAGNOSTICS") == "1";

    /// <summary>How many times the clock has fired since this control was built.</summary>
    private long _ticks;

    /// <summary>How many times the simulation has been thrown away and started again.</summary>
    private int _restarts;

    /// <summary>
    /// Where the readings go. The app is a WinExe, so Console.WriteLine reaches nothing on Windows
    /// even when it was started from a terminal; a file can be read back afterwards.
    /// </summary>
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "ledballoon-preview.log");

    private TimeSpan _lastSaid = TimeSpan.FromSeconds(-10);

    public StripPreview()
    {
        // Faster than the photo's 50ms: this is a bitmap blit rather than a photograph with every
        // fixture drawn over it, and one row of history per tick means the tick rate decides how
        // much time the panel covers.
        _clock = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnTick);

        // The bitmap is one pixel per LED and stretched across the control, and smoothing it would
        // blend neighbouring LEDs into each other - which is the one thing a strip must not do.
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
        // rest of the session.
        _running = null;
        _run = string.Empty;

        _image?.Dispose();
        _image = null;

        _row = [];
        _columns = 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        TimeSpan elapsed = _since.Elapsed;
        double delta = (elapsed - _lastTick).TotalMilliseconds;
        _lastTick = elapsed;

        _ticks++;
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

        var unlit = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x16));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x35)));

        context.DrawRectangle(unlit, edge, new RoundedRect(bounds, 4));

        if (LedCount < 1 || Segment is not { } wled)
        {
            return;
        }

        Restart(wled, LedCount);

        // Inside the frame, so it reads as sitting in the strip rather than as a bar with a line
        // round it.
        Rect inside = bounds.Deflate(new Thickness(2));
        int columns = Math.Clamp(Math.Min(LedCount, (int)inside.Width), 1, MaxLeds);

        Resize(columns);
        Sample(wled);
        Draw(context, inside);

        if (Diagnostics)
        {
            DrawCounters(context, inside, wled);
            Record(wled);
        }
    }

    /// <summary>
    /// Writes one reading a second to <see cref="LogPath"/>.
    /// <para>
    /// The palette samples are the useful part. They are what the segment hands an effect for four
    /// spread-out palette indices, through the same call the effects make - so four equal values is
    /// a run that will be one flat colour whatever the palette holds, and they say so without
    /// anybody having to guess where a private field sits. Four equal values matching color 0 is the
    /// colour slot being substituted; four different ones mean the palette is reaching the strip and
    /// the fault is elsewhere.
    /// </para>
    /// </summary>
    private void Record(WledSegment wled)
    {
        TimeSpan now = _since.Elapsed;
        if (now - _lastSaid < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastSaid = now;

        try
        {
            (int drawnPalette, int capabilities) = NativeEngine.LastDrawn;

            var line = new System.Text.StringBuilder();
            line.Append(CultureInfo.InvariantCulture, $"[{now.TotalSeconds,6:F1}s] ");
            line.Append(CultureInfo.InvariantCulture,
                $"fx {wled.Effect} pal {wled.Palette}->{drawnPalette} caps 0x{capabilities:x2}  ");
            line.Append(CultureInfo.InvariantCulture,
                $"ticks {_ticks} restarts {_restarts} ");

            if (_running is { } running)
            {
                line.Append(CultureInfo.InvariantCulture,
                    $"frames {running.Frames} t {running.ElapsedMilliseconds}ms ");
            }

            line.Append("samples");
            foreach (uint sample in NativeEngine.PaletteSamples())
            {
                line.Append(CultureInfo.InvariantCulture, $" {sample & 0xFFFFFF:x6}");
            }

            line.Append("  col");
            if (wled.Colors is { } colors)
            {
                foreach (int[]? slot in colors)
                {
                    line.Append(CultureInfo.InvariantCulture,
                        $" {RgbColor.FromWledArray(slot).ToHex()}");
                }
            }

            line.Append("  trace");
            foreach (uint value in NativeEngine.CapabilityTrace())
            {
                line.Append(CultureInfo.InvariantCulture, $" {value}");
            }

            line.Append("  probe");
            foreach (uint value in NativeEngine.Probe())
            {
                line.Append(CultureInfo.InvariantCulture, $" {value}");
            }

            File.AppendAllText(LogPath, line.ToString() + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A reading nobody can write down is not worth bringing the preview down for.
        }
    }

    /// <summary>Writes the counters over the strip, for the one question they answer.</summary>
    private void DrawCounters(DrawingContext context, Rect inside, WledSegment wled)
    {
        ControllerTiming timing = Timing is { IntervalMilliseconds: > 0 } known
            ? known
            : new ControllerTiming(
                EffectSimulation.DefaultFrameMilliseconds, FrameTime.MinimumFrameDelay);

        string said = _running is { } running
            ? $"ticks {_ticks}  restarts {_restarts}  frames {running.Frames}  " +
              $"t {running.ElapsedMilliseconds} ms  step {timing.IntervalMilliseconds}/" +
              $"{timing.FrameTimeMilliseconds} ms"
            : $"ticks {_ticks}  restarts {_restarts}  nothing running";

        // What the segment asked for, what the engine was given, and what the engine says the run
        // can show. Bit 0 of the last is RGB, and without it color_from_palette returns the color
        // slot for every pixel - a flat run that no palette and no effect can move.
        (int drawnPalette, int capabilities) = NativeEngine.LastDrawn;

        said += $"  |  pal {wled.Palette} -> {drawnPalette}  caps 0x{capabilities:x2}";

        // And, when the capability is the thing that is wrong, everything that decided it. A caps
        // of exactly zero is assigned rather than merely missing a bit: either the segment was not
        // active when wled_begin ran, or no bus covered it.
        if (NativeEngine.Probe() is { Length: 8 } probe)
        {
            said += $"{Environment.NewLine}begins {probe[0]}  busses {probe[1]}  " +
                    $"seg {probe[2]}..{probe[3]}  maxWidth {probe[5]}  " +
                    $"strip {probe[6]}  bus {probe[7]}";
        }

        var text = new FormattedText(
            said,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            11,
            Brushes.White);

        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
            null,
            new Rect(inside.X, inside.Y, text.Width + 8, text.Height + 4));

        context.DrawText(text, new Point(inside.X + 4, inside.Y + 2));
    }

    /// <summary>Rebuilds the bitmap when the control is a different width in LEDs.</summary>
    private void Resize(int columns)
    {
        if (_columns == columns && _image is not null)
        {
            return;
        }

        _columns = columns;
        _row = new int[columns];

        Array.Fill(_row, unchecked((int)Unlit));

        _image?.Dispose();
        _image = new WriteableBitmap(
            new PixelSize(columns, 1),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
    }

    /// <summary>Reads the run as it is this instant.</summary>
    private void Sample(WledSegment wled)
    {
        double span = 1d / _columns;

        for (int i = 0; i < _columns; i++)
        {
            RgbColor color = ColorAt((i + 0.5) * span, span, wled);

            _row[i] = color.R + color.G + color.B == 0
                // The unlit strip rather than black, so a dark LED matches the frame around it.
                ? unchecked((int)Unlit)
                : unchecked((int)(0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));
        }
    }

    private void Draw(DrawingContext context, Rect inside)
    {
        if (_image is not { } image)
        {
            return;
        }

        using (ILockedFramebuffer buffer = image.Lock())
        {
            Marshal.Copy(_row, 0, buffer.Address, _columns);
        }

        // One pixel tall, stretched over the band. Nothing is interpolated across it, so an LED is
        // a column of one color however tall the control is.
        context.DrawImage(image, new Rect(0, 0, _columns, 1), inside);
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

        // An effect the engine does not have. Its colors are known and its movement is not, so the palette
        // is laid across the run and left there.
        //
        // It used to slide, at the pace the speed slider asked for. That was a mistake, and an
        // instructive one: this strip teaches that a diagonal band means movement and that its slope
        // is the speed, so an effect the app cannot run came out carrying the exact signature of one
        // it can - directly beneath a line saying it could not run it. Held still, the rows all
        // match, which is this control's own way of saying nothing here is moving.
        if (Palette is { } palette && wled.Palette is > 0)
        {
            return palette.ColorAt(
                t,
                wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White,
                wled.Colors is { Length: > 1 } ? wled.SecondaryColor : RgbColor.Black,
                wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]) : RgbColor.Black);
        }

        // Not even the colors: no palette to lay out, and the color slots belong to effects that
        // read them. Freqwave reads neither - it builds a hue out of whatever frequency it is
        // hearing - so a run of color 1 would be invented rather than approximated. Dark, and the
        // panel says why.
        return RgbColor.Black;
    }


    /// <summary>Starts the effect, or restarts it when anything it reads has changed.</summary>
    private void Restart(WledSegment wled, int leds)
    {
        string run = string.Join(
            '/',
            wled.Effect, wled.Palette, Palette?.Stops.Count ?? -1,
            wled.Colors is { Length: > 0 } ? wled.PrimaryColor.ToHex() : "-",
            wled.Colors is { Length: > 1 } ? wled.SecondaryColor.ToHex() : "-",
            wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]).ToHex() : "-",
            wled.Speed, wled.Intensity, wled.Reverse, leds);

        if (_run == run)
        {
            return;
        }

        _run = run;
        _restarts++;

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
