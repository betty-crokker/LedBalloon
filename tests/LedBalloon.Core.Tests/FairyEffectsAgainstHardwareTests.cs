using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The two fairy-light effects, captured on the south controller's 285 LED roofline.
/// <para>
/// Both are checkable pixel for pixel, and for once without a search. Their colors come from a
/// sequence seeded on the segment number and nothing else, so the colors are a pure function of where
/// the effect is running - Fairy at intensity zero has no flashers at all and is therefore one still
/// picture, and Fairytwinkle's colors sit behind a twinkle that can be divided out.
/// </para>
/// <para>
/// These were captured with the secondary color set to black rather than the blue these tests
/// normally use. Both effects blend up from the secondary, so a bright secondary means an unlit LED
/// reads as full brightness and every brightness statistic measures the background instead of the
/// effect. That is the same trap that made Running look like it had twice as many bands as it has.
/// </para>
/// </summary>
public class FairyEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>Palette 11, Rainbow - sixteen stops, which is what both captures ran on.</summary>
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

    /// <summary>The run as both captures had it: segment two, black background, Rainbow.</summary>
    private static EffectSegment Run(byte intensity = 128) => new(Strip.Roofline)
    {
        // Segment two, because both effects fold the segment number into their seed and the roofline
        // is segment two on this controller. Get it wrong and every LED is a different color.
        SegmentId = 2,
        Speed = 128,
        Intensity = intensity,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 11,
        Palette = Rainbow(),
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 0), new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// Fairy at intensity zero reproduces the strip pixel for pixel, with nothing to search over.
    /// <para>
    /// Intensity is how many flashers there are, and zero means none - so the effect returns after
    /// doing nothing but paint every LED a color from its sequence. Every one of the 60 captured
    /// frames came back byte-identical, which is the check that there is no clock in it at all.
    /// </para>
    /// <para>
    /// So this is the tightest kind of evidence available for an effect: no phase, no accumulated
    /// state, no statistics. It confirms the seed, the generator's odd addend, and the palette lookup
    /// in one shot, and it is the reason the segment number had to be modelled at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Fairy_at_intensity_zero_reproduces_the_strip_pixel_for_pixel()
    {
        RgbColor[] captured = Captured("fairy.txt");

        EffectSegment segment = Run(intensity: 0);
        var effect = new FairyEffect();

        effect.Render(segment, 0);

        double error = Enumerable.Range(0, Strip.Roofline)
            .Sum(i => Math.Abs(segment.Pixels[i].R - captured[i].R)
                + Math.Abs(segment.Pixels[i].G - captured[i].G)
                + Math.Abs(segment.Pixels[i].B - captured[i].B))
            / (Strip.Roofline * 3d);

        // And it really is still: a later frame has to come out the same or the comparison above was
        // luck.
        effect.Render(segment, 7_000);

        double drift = Enumerable.Range(0, Strip.Roofline)
            .Sum(i => Math.Abs(segment.Pixels[i].R - captured[i].R)
                + Math.Abs(segment.Pixels[i].G - captured[i].G)
                + Math.Abs(segment.Pixels[i].B - captured[i].B))
            / (Strip.Roofline * 3d);

        output.WriteLine($"first frame: {error:F2} per channel over {Strip.Roofline} LEDs");
        output.WriteLine($"seven seconds later: {drift:F2} per channel");
        output.WriteLine($"first four LEDs: {string.Join(" ", segment.Pixels.Take(4).Select(Hex))}");
        output.WriteLine($"the strip's    : {string.Join(" ", captured.Take(4).Select(Hex))}");

        Assert.InRange(error, 0, 1);
        Assert.Equal(error, drift, 6);

        // The wrong seed would give a different color for every LED, so the fit has to be checked
        // against being an accident of the palette's range rather than only against zero.
        EffectSegment wrong = Run(intensity: 0);
        wrong.SegmentId = 0;
        effect.Render(wrong, 0);

        double elsewhere = Enumerable.Range(0, Strip.Roofline)
            .Sum(i => Math.Abs(wrong.Pixels[i].R - captured[i].R)
                + Math.Abs(wrong.Pixels[i].G - captured[i].G)
                + Math.Abs(wrong.Pixels[i].B - captured[i].B))
            / (Strip.Roofline * 3d);

        output.WriteLine($"the same effect on segment zero: {elsewhere:F2} per channel");

        Assert.True(elsewhere > 40,
            $"another segment's pattern should be nothing like this one: {elsewhere:F2} per channel");
    }

    /// <summary>
    /// Fairy pulses every fifth LED and lets the rest sag while it does.
    /// <para>
    /// At intensity 128 the flashers stand five LEDs apart, and that is what the strip shows: 57 LEDs
    /// whose brightness moves a lot over twelve seconds, spaced exactly five apart, out of 285. The
    /// count follows from the arithmetic - 285 over 5 is 57 flashers on the run and a 58th just past
    /// the end, which the firmware draws off the end and loses.
    /// </para>
    /// <para>
    /// The other 228 move a little and not at all randomly. Every LED is redrawn each frame scaled by
    /// its zone's peak brightness, which falls as the flashers in that zone light - the supply sag
    /// that gives the effect its name. Without it those LEDs would be perfectly still, so a standard
    /// deviation of about ten units is the whole signature: small, but nowhere near zero.
    /// </para>
    /// </summary>
    [Fact]
    public void Fairy_pulses_every_fifth_LED_and_lets_the_rest_sag()
    {
        (double brightness, int pulsing, double spacing, int spacingMode, double steady, double rising) =
            Measure(new FairyEffect(), Run());

        output.WriteLine($"simulated: brightness {brightness:F2}, {pulsing} LEDs moving a lot, " +
            $"spaced {spacing:F2} (mode {spacingMode}), the rest moving {steady:F2}, rising {rising:F3}");
        output.WriteLine("measured : brightness 145.80, 57 LEDs moving a lot, " +
            "spaced 5.00 (mode 5), the rest moving 10.39, rising 0.438");
        output.WriteLine("arithmetic: ((255 - 128) / 28) + 1 = 5 apart, 285 / 5 = 57 on the run");

        Assert.InRange(brightness, 125, 168);

        // The count and the spacing both come straight from the intensity slider.
        Assert.InRange(pulsing, 46, 68);
        Assert.Equal(5, spacingMode);
        Assert.InRange(spacing, 4.5, 6.5);

        // The sag: small, and the point is that it is not zero.
        Assert.InRange(steady, 4, 18);
    }

    /// <summary>
    /// Fairytwinkle's colors reproduce the strip pixel for pixel once the twinkling is divided out.
    /// <para>
    /// Each LED keeps one color for as long as the effect runs, because the sequence that picks it is
    /// redrawn from the same seed every frame. Only the brightness moves. So taking each LED's
    /// brightest sample over twelve seconds recovers its color undimmed - every LED comes up lit and
    /// then holds at full for seconds before fading, so every LED has such a sample.
    /// </para>
    /// <para>
    /// What that confirms is the loop nobody would guess at: the sequence is drawn again and again
    /// until this LED's value differs from the previous one by at least a quarter of its range, which
    /// takes 1.63 turns on average and as many as seven. Each extra turn shifts the color of every LED
    /// after it, so being out by one anywhere ruins the rest of the run - and the fit is 0.15 of a
    /// unit per channel across all 285. Neighbors end up 125 units apart per channel, about half the
    /// palette's range.
    /// </para>
    /// </summary>
    [Fact]
    public void Fairytwinkle_reproduces_the_strip_s_colors_pixel_for_pixel()
    {
        RgbColor[] captured = Captured("fairytwinkle-colors.txt");

        EffectSegment segment = Run();
        RgbColor[] recovered = BrightestPerLed(new FairyTwinkleEffect(), segment);

        double error = Enumerable.Range(0, Strip.Roofline)
            .Sum(i => Math.Abs(recovered[i].R - captured[i].R)
                + Math.Abs(recovered[i].G - captured[i].G)
                + Math.Abs(recovered[i].B - captured[i].B))
            / (Strip.Roofline * 3d);

        // How different neighbors come out, which is what the repeat loop is for. Measured on both
        // sides rather than quoted, because with the colors matching to a sixth of a unit the strip's
        // own figure is not independent evidence - it is here to show what the loop buys.
        double apart = Apart(recovered);
        double onTheStrip = Apart(captured);

        output.WriteLine($"colors: {error:F2} per channel over {Strip.Roofline} LEDs");
        output.WriteLine($"neighbors differ by {apart:F1} per channel, the strip's by {onTheStrip:F1}");
        output.WriteLine($"first four LEDs: {string.Join(" ", recovered.Take(4).Select(Hex))}");
        output.WriteLine($"the strip's    : {string.Join(" ", captured.Take(4).Select(Hex))}");

        Assert.InRange(error, 0, 2);

        // Half the palette's range apart on average, which is what "differ enough" comes to.
        Assert.InRange(apart, 100, 160);
        Assert.Equal(onTheStrip, apart, 0);
    }

    /// <summary>
    /// Fairytwinkle comes up lit and settles into a twinkle, rather than building out of the dark.
    /// <para>
    /// Every LED's first frame backdates its own start by a whole rise time, so the run is fully lit
    /// immediately and the twinkle emerges from it. And each LED is on its own timer with no
    /// synchronization at all, so what the strip shows is a slow uncoordinated breathing: nine percent
    /// of LEDs brighter than they were a sample ago, which is a long way from the fifty percent a
    /// synchronized wave would give and a long way from the near-zero of something that only fades.
    /// </para>
    /// </summary>
    [Fact]
    public void Fairytwinkle_comes_up_lit_and_settles_into_a_twinkle()
    {
        EffectSegment segment = Run();
        var effect = new FairyTwinkleEffect();

        // The first frame is the one that matters for "comes up lit": the second frame is where every
        // LED initializes, and it should find them all at or near full.
        segment.Draw(effect, 0);
        segment.Draw(effect, Strip.FrameMs);

        double atOnce = segment.Pixels.Average(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)));

        (double brightness, _, _, _, _, double rising) = Measure(effect, Run());

        output.WriteLine($"simulated: lit on the first frame {atOnce:F1}, " +
            $"brightness {brightness:F2}, rising {rising:F3}");
        output.WriteLine("measured : brightness 171.22, rising 0.090");

        // Lit from the start, which is the whole difference from Colortwinkles.
        Assert.InRange(atOnce, 180, 255);

        Assert.InRange(brightness, 150, 195);

        // Breathing, uncoordinated: not a wave, and not a one-way fade either.
        Assert.InRange(rising, 0.03, 0.20);
    }

    /// <summary>
    /// Brightness over time, how many LEDs move a lot and how far apart those sit, how much the rest
    /// move, and how often an LED is brighter than it was a sample ago.
    /// </summary>
    private static (double Brightness, int Pulsing, double Spacing, int SpacingMode, double Steady,
        double Rising) Measure(IWledEffect effect, EffectSegment segment)
    {
        List<byte[]> samples = [];

        Strip.Sample(effect, segment, 12_000,
            (frame, _) => samples.Add([.. frame.Select(p => Math.Max(p.R, Math.Max(p.G, p.B)))]));

        int n = segment.Length;

        double[] deviation = Enumerable.Range(0, n)
            .Select(i => Deviation(samples.Select(s => (double)s[i])))
            .ToArray();

        // Thirty units of movement is well clear of the sag and well below a flasher's swing, so the
        // split is not sensitive to where exactly it is put.
        int[] pulsing = [.. Enumerable.Range(0, n).Where(i => deviation[i] > 30)];

        int[] gaps = [.. pulsing.Zip(pulsing.Skip(1), (a, b) => b - a)];

        double steady = Enumerable.Range(0, n).Where(i => deviation[i] <= 30)
            .Select(i => deviation[i]).DefaultIfEmpty(0).Average();

        double rising = samples.Zip(samples.Skip(1),
            (before, now) => (double)Enumerable.Range(0, n).Count(i => now[i] > before[i]) / n)
            .Average();

        return (
            samples.Average(s => s.Average(b => (double)b)),
            pulsing.Length,
            gaps.Length > 0 ? gaps.Average() : 0,
            gaps.Length > 0 ? gaps.GroupBy(g => g).OrderByDescending(g => g.Count()).First().Key : 0,
            steady,
            rising);
    }

    private static double Deviation(IEnumerable<double> values)
    {
        double[] all = [.. values];
        double mean = all.Average();

        return Math.Sqrt(all.Average(v => (v - mean) * (v - mean)));
    }

    /// <summary>
    /// Each LED's brightest sample over twelve seconds, which for Fairytwinkle is its color undimmed.
    /// </summary>
    private static RgbColor[] BrightestPerLed(IWledEffect effect, EffectSegment segment)
    {
        var best = new RgbColor[segment.Length];
        var peak = new int[segment.Length];

        Strip.Sample(effect, segment, 12_000, (frame, _) =>
        {
            for (int i = 0; i < frame.Length; i++)
            {
                int bright = Math.Max(frame[i].R, Math.Max(frame[i].G, frame[i].B));

                if (bright > peak[i])
                {
                    peak[i] = bright;
                    best[i] = frame[i];
                }
            }
        });

        // Sampled frames arrive reversed, because the roofline is wired back to front, so put them
        // back into segment order to compare with a fixture that is read the same way round.
        return [.. best.Reverse()];
    }

    /// <summary>How far apart neighboring LEDs' colors are, per channel.</summary>
    private static double Apart(RgbColor[] colors) => Enumerable.Range(0, colors.Length - 1)
        .Average(i => (Math.Abs(colors[i].R - colors[i + 1].R)
            + Math.Abs(colors[i].G - colors[i + 1].G)
            + Math.Abs(colors[i].B - colors[i + 1].B)) / 3d);

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
}
