using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Plasma and Sunrise, captured on the south controller's 285 LED roofline on palette Rainbow.
/// <para>
/// Both fill every LED every frame from a smooth function of position, which makes them the opposite
/// sort of measurement from the effects that draw a few dots: what matters is the shape of the whole
/// run at once. And they differ in exactly the way that shape can show - Sunrise is a perfect mirror
/// about the middle and Plasma is not symmetric at all.
/// </para>
/// </summary>
public class SmoothEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static WledPalette Rainbow() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(255, 0, 0)), new PaletteStop(16, new RgbColor(213, 42, 0)),
            new PaletteStop(32, new RgbColor(171, 85, 0)), new PaletteStop(48, new RgbColor(171, 127, 0)),
            new PaletteStop(64, new RgbColor(171, 171, 0)), new PaletteStop(80, new RgbColor(86, 213, 0)),
            new PaletteStop(96, new RgbColor(0, 255, 0)), new PaletteStop(112, new RgbColor(0, 213, 42)),
            new PaletteStop(128, new RgbColor(0, 171, 85)), new PaletteStop(144, new RgbColor(0, 86, 170)),
            new PaletteStop(160, new RgbColor(0, 0, 255)), new PaletteStop(176, new RgbColor(42, 0, 213)),
            new PaletteStop(192, new RgbColor(85, 0, 171)), new PaletteStop(208, new RgbColor(127, 0, 129)),
            new PaletteStop(224, new RgbColor(171, 0, 85)), new PaletteStop(240, new RgbColor(213, 0, 43)),
        ],
    };

    private static EffectSegment Run(byte speed = 128, byte intensity = 128) => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = speed,
        Intensity = intensity,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 11,
        Palette = Rainbow(),
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// Plasma covers the run in a moving texture, with about a tenth of it fully dark at any moment.
    /// <para>
    /// The dark part is the interesting measurement, because it is not a fade. A third slow wave is
    /// subtracted from the brightness with a floor, so where it wins the LED goes to nothing and stays
    /// there rather than dipping and recovering - the strip had 29.7 of 285 LEDs at exactly zero in an
    /// average frame. A plain multiply would have given a dim run and no zeroes at all.
    /// </para>
    /// <para>
    /// And it is emphatically not symmetric: 38.94 units per channel between one end and the other,
    /// where Sunrise measures zero on the same statistic. Two waves of unrelated frequency walking in
    /// the same direction cannot be, which is what stops it looking like a pattern.
    /// </para>
    /// </summary>
    [Fact]
    public void Plasma_covers_the_run_and_takes_a_tenth_of_it_to_nothing()
    {
        Shape shape = Measure(new PlasmaEffect(), Run());

        output.WriteLine($"simulated: brightness {shape.Brightness:F2}, spread {shape.Spread:F2}, " +
            $"{shape.Peaks:F2} peaks, {shape.Dark:F1} dark, {shape.Colors:F1} colors, " +
            $"mirror mismatch {shape.Mirror:F2}, rising {shape.Rising:F3}");
        output.WriteLine("measured : brightness 69.43, spread 43.41, 46.68 peaks, 29.7 dark, " +
            "136.0 colors, mirror mismatch 38.94, rising 0.391");
        output.WriteLine("arithmetic: 2 + 3 * (128 >> 5) = 14 and 1 + 2 * (128 >> 5) = 9 LEDs a cycle");

        Assert.InRange(shape.Brightness, 55, 85);
        Assert.InRange(shape.Spread, 35, 52);
        Assert.InRange(shape.Peaks, 38, 56);

        // Really zero, not merely dim, which is what the floored subtraction gives.
        Assert.InRange(shape.Dark, 18, 44);

        // Nothing like a mirror.
        Assert.InRange(shape.Mirror, 25, 55);
    }

    /// <summary>
    /// Sunrise is a perfect mirror about the middle of the run, to the last unit.
    /// <para>
    /// Every LED is drawn twice, once counted from each end, so the two halves cannot differ - and the
    /// strip agrees exactly: 0.00 units per channel between one half and the other, over 185 frames.
    /// That is the rare case where the right answer is a hard zero rather than a small number, and it
    /// is worth having because it pins the loop bound as well as the symmetry. Running to
    /// <c>SEGLEN / 2</c> inclusive is what covers the middle LED on an odd-length run; stopping one
    /// short would leave a dark pixel in the centre and this measurement would find it.
    /// </para>
    /// <para>
    /// Nothing is ever dark, because the darkest the palette is read at is its own first entry rather
    /// than black - the background is drawn from the palette too.
    /// </para>
    /// </summary>
    [Fact]
    public void Sunrise_is_a_perfect_mirror_about_the_middle()
    {
        // Above 120 the slider stops being a duration in minutes and becomes a rate, which is the
        // only setting that does anything visible inside a twelve second capture.
        Shape shape = Measure(new SunriseEffect(), Run(speed: 240));

        output.WriteLine($"simulated: brightness {shape.Brightness:F2}, spread {shape.Spread:F2}, " +
            $"{shape.Peaks:F2} peaks, {shape.Dark:F1} dark, {shape.Colors:F1} colors, " +
            $"mirror mismatch {shape.Mirror:F2}, rising {shape.Rising:F3}");
        output.WriteLine("measured : brightness 201.70, spread 28.44, 8.27 peaks, 0.0 dark, " +
            "78.8 colors, mirror mismatch 0.00, rising 0.365");

        // Exact, both here and on the strip.
        Assert.Equal(0.0, shape.Mirror);

        Assert.Equal(0.0, shape.Dark);

        Assert.InRange(shape.Brightness, 180, 220);
        Assert.InRange(shape.Spread, 22, 35);

        // Peaks are reported but barely asserted on. The profile here is smooth enough that counting
        // direction changes counts rounding: the firmware blends sixteen palette entries in integers
        // and the small steps that leaves register as extra turns, where this port interpolates and
        // does not. 3.59 against 8.27 is that rather than a difference in the wave - the pixel for
        // pixel fit below settles it, and every other statistic here agrees to a fraction of a unit.
        Assert.InRange(shape.Peaks, 1, 14);
    }

    /// <summary>
    /// Sunrise reproduces the strip pixel for pixel, with one clock to search over.
    /// <para>
    /// Above a speed of 120 the slider is a rate rather than a duration, and then the whole picture is
    /// a pure function of the clock - one triangle wave drives it, nothing is accumulated, and the
    /// palette is read straight off the result. So a captured frame has exactly one unknown, and
    /// stepping the clock two milliseconds at a time covers every picture the effect can draw in about
    /// eleven hundred tries.
    /// </para>
    /// <para>
    /// This is what tells the peak count above to be quiet. A fit of well under a unit per channel
    /// across all 285 LEDs says the wave is right; a peak count that disagrees while the mean agrees
    /// to two decimal places is measuring the palette blend's rounding and nothing else.
    /// </para>
    /// </summary>
    [Fact]
    public void Sunrise_reproduces_the_strip_pixel_for_pixel()
    {
        RgbColor[] captured = Captured("sunrise.txt");

        EffectSegment segment = Run(speed: 240);
        var effect = new SunriseEffect();

        double best = double.MaxValue;
        uint bestClock = 0;

        // The counter is (now >> 1) * 61, so two milliseconds is one step of it and 65536 / 61 of
        // those cover a whole triangle.
        for (uint clock = 0; clock < 2_200; clock += 2)
        {
            effect.Render(segment, clock);

            double error = Enumerable.Range(0, Strip.Roofline)
                .Sum(i => Math.Abs(segment.Pixels[i].R - captured[i].R)
                    + Math.Abs(segment.Pixels[i].G - captured[i].G)
                    + Math.Abs(segment.Pixels[i].B - captured[i].B))
                / (Strip.Roofline * 3d);

            if (error < best)
            {
                best = error;
                bestClock = clock;
            }
        }

        // Draw the winner again, or what gets printed is whatever the last try left behind.
        effect.Render(segment, bestClock);

        output.WriteLine($"best clock {bestClock} ms: {best:F2} per channel over {Strip.Roofline} LEDs");
        output.WriteLine($"first four LEDs: {string.Join(" ", segment.Pixels.Take(4).Select(Hex))}");
        output.WriteLine($"the strip's    : {string.Join(" ", captured.Take(4).Select(Hex))}");

        // Neighbouring clocks draw nearly the same picture, so there is no point asking for the runner
        // up to be far behind. What carries the evidence is how small the best fit is.
        Assert.InRange(best, 0, 2);
    }

    private static string Hex(RgbColor color) => $"{color.R:x2}{color.G:x2}{color.B:x2}";

    /// <summary>One captured line, in segment order - the roofline is wired back to front.</summary>
    private static RgbColor[] Captured(string fixture)
    {
        string line = File.ReadAllLines(Path.Combine("Fixtures", fixture))
            .First(l => !l.StartsWith('#') && l.Length >= Strip.Roofline * 6);

        var frame = new RgbColor[Strip.Roofline];

        for (int i = 0; i < Strip.Roofline; i++)
        {
            int at = i * 6;

            frame[Strip.Roofline - 1 - i] = new RgbColor(
                Convert.ToByte(line.Substring(at, 2), 16),
                Convert.ToByte(line.Substring(at + 2, 2), 16),
                Convert.ToByte(line.Substring(at + 4, 2), 16));
        }

        return frame;
    }

    /// <summary>
    /// Sunrise's speed slider really is a duration in minutes, and it restarts when it moves.
    /// <para>
    /// Not measured against the strip, because measuring it would mean watching a roofline for an hour.
    /// It is arithmetic: a speed of one is a one minute sunrise, so at thirty seconds it should be half
    /// risen and at sixty fully risen and no further. A speed of 61 is the same sunrise backwards.
    /// </para>
    /// <para>
    /// The restart is the part worth pinning, since it is the only place in any of these effects where
    /// an effect notices a slider moving. Half way through a one minute rise, moving the slider to ten
    /// minutes has to start again from dark rather than stay half risen, because half of one minute is
    /// not half of ten.
    /// </para>
    /// </summary>
    [Fact]
    public void Sunrise_measures_its_rise_in_minutes_and_restarts_when_the_slider_moves()
    {
        EffectSegment segment = Run(speed: 1);
        var effect = new SunriseEffect();

        segment.Draw(effect, 0);
        RgbColor[] start = [.. segment.Pixels];

        segment.Draw(effect, 30_000);
        RgbColor[] half = [.. segment.Pixels];

        segment.Draw(effect, 60_000);
        RgbColor[] risen = [.. segment.Pixels];

        segment.Draw(effect, 120_000);
        RgbColor[] later = [.. segment.Pixels];

        output.WriteLine($"a one minute rise: spread {Spread(start):F1} at the start, " +
            $"{Spread(half):F1} at thirty seconds, {Spread(risen):F1} at a minute, " +
            $"{Spread(later):F1} at two");

        // Nothing has risen yet, so the whole run is one color. Measured as spread rather than as
        // brightness, because brightness is the wrong signal here: this palette's first entry is full
        // red, so a run that has not risen at all still reads as bright. And the spread is not
        // monotone either - what rising does is move the gradient along the palette, and how far apart
        // that leaves the ends of the run goes up and down as it goes. So what is checked past the
        // start is that the picture moves and then stops, not that some number climbs.
        Assert.Equal(0.0, Spread(start));

        Assert.True(Spread(half) > 5, $"it should have spread out by thirty seconds: {Spread(half):F1}");
        Assert.False(half.SequenceEqual(risen), "it should still be moving at a minute");

        // And then it stops, because the elapsed time is clamped to the duration.
        Assert.True(risen.SequenceEqual(later), "and it should have stopped by two minutes");

        // Now move the slider, which has to start the rise over from one flat color.
        segment.Speed = 10;
        segment.Draw(effect, 120_000);

        output.WriteLine($"after moving the slider to ten minutes: spread {Spread(segment.Pixels):F1}");

        Assert.Equal(0.0, Spread(segment.Pixels));
        Assert.True(start.SequenceEqual(segment.Pixels), "and from the same picture it started from");
    }

    /// <summary>How far the run is from being one flat color, which is what rising undoes.</summary>
    private static double Spread(IReadOnlyList<RgbColor> pixels)
    {
        double[] all = [.. pixels.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];
        double mean = all.Average();

        return Math.Sqrt(all.Average(v => (v - mean) * (v - mean)));
    }

    /// <summary>What a capture of one of these looks like, reduced.</summary>
    private readonly record struct Shape(
        double Brightness,
        double Spread,
        double Peaks,
        double Dark,
        double Colors,
        double Mirror,
        double Rising);

    private static Shape Measure(IWledEffect effect, EffectSegment segment)
    {
        List<double> brightness = [], spread = [], peaks = [], dark = [], colors = [], mirror = [],
            rising = [];

        byte[]? before = null;

        Strip.Sample(effect, segment, 12_000, (frame, _) =>
        {
            byte[] now = [.. frame.Select(p => Math.Max(p.R, Math.Max(p.G, p.B)))];
            int n = now.Length;

            Strip.Shape profile = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            brightness.Add(profile.Mean);
            spread.Add(profile.Deviation);
            peaks.Add(profile.Bands);
            dark.Add(now.Count(v => v == 0));
            colors.Add(frame.Distinct().Count());

            // How far the run is from being its own reflection, which is the one statistic that tells
            // these two apart outright.
            mirror.Add(Enumerable.Range(0, n / 2)
                .Average(i => (Math.Abs(frame[i].R - frame[n - 1 - i].R)
                    + Math.Abs(frame[i].G - frame[n - 1 - i].G)
                    + Math.Abs(frame[i].B - frame[n - 1 - i].B)) / 3d));

            if (before is not null)
            {
                rising.Add((double)Enumerable.Range(0, n).Count(i => now[i] > before[i]) / n);
            }

            before = now;
        });

        return new Shape(
            brightness.Average(), spread.Average(), peaks.Average(), dark.Average(),
            colors.Average(), mirror.Average(), rising.Average());
    }
}
