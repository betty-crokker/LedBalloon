using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LedBalloon.Core;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;

namespace LedBalloon.App.Controls;

/// <summary>
/// One segment's run of LEDs, laid out straight and lit the way the effect lights it.
/// <para>
/// The photo answers "what will the house look like"; this answers "what is this segment doing",
/// which the photo is bad at. A run is drawn on the photo where it actually is - short, foreshortened,
/// behind a downpipe, or diffused into scallops that deliberately hide the individual LEDs - and a
/// dozen of them are competing for the same few hundred pixels. Straightened out and given the width
/// of the panel, the same effect is legible: you can see that Chase has a gap in it, that Twinklecat
/// twinkles rather than chases, and how fast the slider you are holding is making it go.
/// </para>
/// <para>
/// Deliberately ignores both brightnesses and the power switch. Those are answered elsewhere - by the
/// controller's slider with its percentage, by the switch in the top bar, and by the photo, which
/// goes dark when the house does. Folding them in here would mean the one control showing what you
/// are building goes blank exactly when you have no other way to see it, which is the whole reason
/// this exists.
/// </para>
/// </summary>
public sealed class StripPreview : Control
{
    /// <summary>
    /// Cap on LEDs drawn, since past this they are thinner than a pixel anyway.
    /// </summary>
    /// <remarks>
    /// A run longer than this is sampled rather than truncated, so the preview still covers the
    /// whole run - South's roofline is 300 and showing the first 400 of it would be showing all of
    /// it, but the house has had longer runs than that on it.
    /// </remarks>
    private const int MaxLeds = 400;

    /// <summary>What the segment is set to: effect, palette, colors and the sliders.</summary>
    public static readonly StyledProperty<WledSegment?> SegmentProperty =
        AvaloniaProperty.Register<StripPreview, WledSegment?>(nameof(Segment));

    /// <summary>
    /// How many LEDs the run has, from the layout rather than from the controller.
    /// </summary>
    /// <remarks>
    /// The length changes what an effect looks like and not just how wide it is drawn: Chase fits a
    /// fixed number of groups into whatever it is given, and a comet's tail is a proportion of the
    /// run. Previewing a 300-LED roofline at some convenient round number would be previewing a
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

    private string _signature = string.Empty;
    private TimeSpan _lastTick;

    public StripPreview() =>
        // Faster than the photo's 50ms, because this is a few hundred rectangles rather than a
        // photograph with every fixture drawn over it, and because the thing being watched here is
        // usually the speed slider.
        _clock = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnTick);

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

        // Nothing is watching it, so let the buffer go rather than stepping a strip nobody can see
        // for the rest of the session.
        _running = null;
        _signature = string.Empty;
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

        // The unlit strip: what is behind the LEDs, and what shows through between them on a run
        // sparse enough to have gaps.
        context.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x16)),
            new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x35))),
            new RoundedRect(bounds, 4));

        int leds = LedCount;

        if (leds < 1 || Segment is not { } wled)
        {
            return;
        }

        Refresh(wled, leds);

        // Inside the frame, so the drawn LEDs read as sitting in the strip rather than as a bar
        // with a line round it.
        Rect inside = bounds.Deflate(new Thickness(2));
        int cells = Math.Min(leds, MaxLeds);
        double width = inside.Width / cells;

        // A gap only where there is room for one. Below this the gap is most of the LED, which makes
        // a fully lit run look like a dotted one - so instead the LEDs overlap by half a pixel and
        // meet. Drawn exactly edge to edge they do not: the boundaries fall between pixels and get
        // antialiased, and a run of one color comes out finely striped with banding that is not
        // there.
        double gap = width >= 5 ? 1 : 0;
        double drawn = Math.Max(gap > 0 ? width - gap : width + 0.5, 0.5);
        double span = 1d / cells;

        for (int i = 0; i < cells; i++)
        {
            RgbColor color = ColorAt((i + 0.5) * span, span, wled);

            if (color.R + color.G + color.B == 0)
            {
                // Left as the unlit strip rather than painted black over it, which is the same
                // picture and one rectangle cheaper per dark LED - and most effects are mostly dark.
                continue;
            }

            context.FillRectangle(
                new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                new Rect(inside.X + (i * width), inside.Y, drawn, inside.Height));
        }
    }

    /// <summary>
    /// What one LED is showing, averaged over the stretch of the run it stands for.
    /// </summary>
    /// <param name="span">
    /// How much of the run this cell covers. Only wider than one LED on a run longer than
    /// <see cref="MaxLeds"/>, and averaging then matters: reading one LED in three would lose an
    /// effect that lights a few at a time, because most of its dots would land between the samples.
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
                r += pixels[i].R;
                g += pixels[i].G;
                b += pixels[i].B;
            }

            int count = to - from + 1;

            return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }

        // An effect nobody has ported. Its colors are known and its movement is not, so the palette
        // is slid along the run at the pace the speed slider asks for - the same family resemblance
        // the photo draws, and the panel says in words that this is not the effect itself.
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

    /// <summary>
    /// Starts the effect, or restarts it when anything it reads has changed.
    /// </summary>
    private void Refresh(WledSegment wled, int leds)
    {
        string signature = string.Join(
            '/',
            wled.Effect, wled.Palette, wled.Speed, wled.Intensity, wled.Reverse, leds,
            Palette?.Stops.Count ?? -1,
            wled.Colors is { Length: > 0 } ? wled.PrimaryColor.ToHex() : "-",
            wled.Colors is { Length: > 1 } ? wled.SecondaryColor.ToHex() : "-",
            wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]).ToHex() : "-");

        if (_signature == signature)
        {
            return;
        }

        _signature = signature;

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
