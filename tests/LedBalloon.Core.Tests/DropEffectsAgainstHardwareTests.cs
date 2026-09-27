using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The two effects built on WLED's <c>spark</c> struct - things thrown up and things falling down -
/// captured on the south controller's 285 LED roofline.
/// <para>
/// Both are ballistics rather than waveforms, so what can be checked is the shape of the motion:
/// single pixels for Popcorn against streaks for Drip, how much of the run each covers, and where
/// along it they spend their time. A projectile is slowest at the top of its arc, so where the lit
/// pixels sit is a direct measurement of the arithmetic that launched them.
/// </para>
/// </summary>
public class DropEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>
    /// Popcorn throws single pixels up the run, and they linger at the top of the arc.
    /// <para>
    /// Every kernel is one pixel and never a streak, which the strip shows plainly: 844 runs of
    /// consecutive lit pixels over 194 frames, averaging 1.01 LEDs long. The two- and three-long runs
    /// are two kernels that happen to be next to each other, not a trail.
    /// </para>
    /// <para>
    /// How many are in the air at once is the interesting number, because it is not set anywhere - it
    /// falls out of the one-in-128 chance an idle kernel has of popping each frame against how long
    /// the flight lasts. At this speed the arithmetic gives a flight of about 90 frames against a wait
    /// of about 128, so roughly four of the ten kernels should be up at any moment. The strip has
    /// 4.41.
    /// </para>
    /// <para>
    /// And the highest lit pixel averages 227 of 285 rather than half way, which is gravity: a kernel
    /// launched to reach three quarters of the run spends most of its flight in the top third of it.
    /// </para>
    /// </summary>
    [Fact]
    public void Popcorn_throws_single_pixels_that_linger_at_the_top_of_the_arc()
    {
        EffectSegment segment = Run();
        segment.Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)];

        Shape shape = Measure(new PopcornEffect(), segment);

        output.WriteLine($"simulated: {shape.Lit:F2} lit per frame, runs {shape.RunLength:F2} long " +
            $"(longest {shape.LongestRun}), highest lit {shape.Highest:F1}, " +
            $"{shape.Colors} colors, brightness {shape.Brightness:F2}");
        output.WriteLine("measured : 4.41 lit per frame, runs 1.01 long (longest 3), " +
            "highest lit 227.1, 3 colors, brightness 3.95");
        output.WriteLine("arithmetic: 128 * 21 / 255 = 10 kernels, a flight of ~90 frames against a " +
            "wait of ~128");

        Assert.InRange(shape.Lit, 3.0, 6.0);

        // Dots, not trails.
        Assert.InRange(shape.RunLength, 1.0, 1.2);

        // On palette Default a kernel takes one of the three color slots and nothing between them.
        Assert.Equal(3, shape.Colors);

        // Well above half way, which is the arc rather than an even spread.
        Assert.InRange(shape.Highest, 200, 260);
    }

    /// <summary>
    /// Popcorn's kernels are all the primary when the third color slot is left black.
    /// <para>
    /// A kernel picks one of the three slots at random, then throws the choice away unless all three
    /// are set - a run of black kernels would read as a run of missing ones, so the firmware would
    /// rather have them all one color than have some of them invisible. The same check decides what
    /// the background is: with a third color the kernels fly on black, without one they fly on the
    /// secondary.
    /// </para>
    /// </summary>
    [Fact]
    public void Popcorn_falls_back_to_one_color_when_the_third_slot_is_black()
    {
        EffectSegment segment = Run();
        segment.Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), RgbColor.Black];

        Shape shape = Measure(new PopcornEffect(), segment);

        output.WriteLine($"with no third color: {shape.Colors} colors, background {Hex(shape.Background)}");

        // The primary on the secondary, and nothing else.
        Assert.Equal(1, shape.Colors);
        Assert.Equal(new RgbColor(0, 0, 255), shape.Background);
    }

    /// <summary>
    /// Drip keeps the tap lit, drops streaks down the run, and splashes at the bottom.
    /// <para>
    /// The LED at the far end is lit in every single captured frame, dimly, because the tap is redrawn
    /// every frame whether a drop is forming there or not. That is the one thing about this effect that is
    /// true of every frame, so it is the first thing to check.
    /// </para>
    /// <para>
    /// The rest is streaks rather than dots - runs averaging 1.98 LEDs and reaching 6, against
    /// Popcorn's 1.01 - because a falling drop draws four pixels behind itself, each divided down by
    /// its distance. A bouncing one draws a single pixel, since the same expression gives
    /// <c>7 - state</c> and the bounce state is five, so the splash is narrower than the fall by
    /// construction rather than by choice.
    /// </para>
    /// <para>
    /// Dividing a brightness rather than fading it is also why the strip shows 36 distinct shades of
    /// the primary over twelve seconds: a trail pixel is the drop's brightness over one, two, three or
    /// four, and the drop's own brightness changes as it swells.
    /// </para>
    /// </summary>
    [Fact]
    public void Drip_keeps_the_tap_lit_and_falls_in_streaks()
    {
        EffectSegment segment = Run();
        segment.Colors = [new RgbColor(255, 0, 0), RgbColor.Black, new RgbColor(0, 255, 0)];

        Shape shape = Measure(new DripEffect(), segment);

        output.WriteLine($"simulated: {shape.Lit:F2} lit per frame, runs {shape.RunLength:F2} long " +
            $"(longest {shape.LongestRun}), tap lit in {shape.TapLit:P0} of frames, " +
            $"{shape.Colors} shades, brightness {shape.Brightness:F2}");
        output.WriteLine("measured : 6.12 lit per frame, runs 1.98 long (longest 6), " +
            "tap lit in 100% of frames, 36 shades, brightness 2.45");

        // The tap never goes out.
        Assert.Equal(1.0, shape.TapLit);

        Assert.InRange(shape.Lit, 4.0, 9.0);

        // Streaks, which is what separates this from Popcorn.
        Assert.InRange(shape.RunLength, 1.5, 2.6);
        Assert.InRange(shape.LongestRun, 4, 8);

        // Many shades, because the trail divides the brightness rather than fading it.
        Assert.InRange(shape.Colors, 20, 60);
    }

    /// <summary>What a capture of one of these looks like, reduced.</summary>
    private readonly record struct Shape(
        double Lit,
        double RunLength,
        int LongestRun,
        double Highest,
        int Colors,
        double Brightness,
        double TapLit,
        RgbColor Background);

    private static Shape Measure(IWledEffect effect, EffectSegment segment)
    {
        List<int> lit = [], runs = [], tops = [];
        List<double> bright = [];
        HashSet<RgbColor> colors = [];
        int tapLit = 0, frames = 0;
        RgbColor background = RgbColor.Black;

        Strip.Sample(effect, segment, 12_000, (sampled, _) =>
        {
            // Sampled frames arrive reversed, because the roofline is wired back to front, and
            // "highest" and "the tap" both mean a particular end of the run.
            RgbColor[] frame = [.. sampled.Reverse()];

            // Whatever the most common color is, which for both of these is the background.
            background = frame.GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key;

            int[] on = [.. Enumerable.Range(0, frame.Length)
                .Where(i => frame[i] != background && Brightness(frame[i]) > 0)];

            lit.Add(on.Length);
            frames++;

            if (on.Length > 0)
            {
                tops.Add(on[^1]);
            }

            if (Brightness(frame[^1]) > 0)
            {
                tapLit++;
            }

            foreach (int i in on)
            {
                colors.Add(frame[i]);
            }

            bright.Add(frame.Average(p => (double)Brightness(p)));

            int run = 0;

            for (int i = 0; i < on.Length; i++)
            {
                run++;

                if (i == on.Length - 1 || on[i + 1] != on[i] + 1)
                {
                    runs.Add(run);
                    run = 0;
                }
            }
        });

        return new Shape(
            lit.Average(),
            runs.Count > 0 ? runs.Average() : 0,
            runs.Count > 0 ? runs.Max() : 0,
            tops.Count > 0 ? tops.Average() : 0,
            colors.Count,
            bright.Average(),
            (double)tapLit / frames,
            background);
    }

    private static byte Brightness(RgbColor color) =>
        Math.Max(color.R, Math.Max(color.G, color.B));

    private static string Hex(RgbColor color) => $"{color.R:x2}{color.G:x2}{color.B:x2}";

    /// <summary>The run as both captures had it - palette Default, since neither reads a palette.</summary>
    private static EffectSegment Run() => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 0,
    };
}
