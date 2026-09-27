using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Dancing Shadows and Rolling Balls, captured on the south controller's 285 LED roofline on palette
/// Rainbow.
/// <para>
/// Two crowds of independent movers, and neither can be checked frame for frame because where each one
/// starts is random. What can be checked is how the crowd behaves: how much of the run it covers, how
/// evenly it spreads over it, and - for Rolling Balls - whether the balls keep their order, which is
/// the only thing a collision actually does.
/// </para>
/// </summary>
public class ShadowEffectsAgainstHardwareTests(ITestOutputHelper output)
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

    private static EffectSegment Run() => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 11,
        Palette = Rainbow(),
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// Dancing Shadows covers a fifth of the run in overlapping patches of light, spread evenly.
    /// <para>
    /// Twenty-five spotlights of one to nine LEDs each, which is 125 LEDs' worth on a 285 LED run - and
    /// the strip shows 62.6 lit. Half, because each is blended half and half onto black rather than
    /// written over it, and because they overlap. The patches average 2.5 LEDs of contiguous light and
    /// reach 18, which is several crossing at once rather than any one spotlight being that wide.
    /// </para>
    /// <para>
    /// The 608 distinct colors over twelve seconds are the blending. Twenty-five colors drawn on black
    /// at half strength would give 25; crossing each other at half strength again gives hundreds.
    /// </para>
    /// <para>
    /// And the light is spread evenly: every tenth of the run carries between 8.7% and 11.2% of it.
    /// Each spotlight moves at a constant speed, so it spends equal time everywhere, and there are
    /// enough of them that the average comes out flat.
    /// </para>
    /// </summary>
    [Fact]
    public void Dancing_shadows_spreads_overlapping_patches_evenly_along_the_run()
    {
        Crowd crowd = Measure(new DancingShadowsEffect(), Run());

        output.WriteLine($"simulated: brightness {crowd.Brightness:F2}, spread {crowd.Spread:F2}, " +
            $"{crowd.Lit:F2} lit per frame, runs {crowd.RunLength:F2} long (longest {crowd.LongestRun}), " +
            $"{crowd.Colors} colors, occupancy {crowd.Flattest:F3} to {crowd.Steepest:F3}");
        output.WriteLine("measured : brightness 24.05, spread 49.27, 62.55 lit per frame, " +
            "runs 2.47 long (longest 18), 608 colors, occupancy 0.087 to 0.112");
        output.WriteLine("arithmetic: 2 + 128 * 47 / 255 = 25 spotlights of 1 to 9 LEDs");

        Assert.InRange(crowd.Lit, 48, 78);
        Assert.InRange(crowd.Brightness, 17, 32);

        // Patches rather than dots, and wider than any one spotlight where several cross.
        Assert.InRange(crowd.RunLength, 2.0, 3.1);
        Assert.InRange(crowd.LongestRun, 10, 30);

        // Hundreds of colors, which is the half-and-half blending rather than the palette.
        Assert.InRange(crowd.Colors, 300, 1200);

        // Evenly spread, because constant speed means equal time everywhere.
        Assert.InRange(crowd.Steepest - crowd.Flattest, 0, 0.06);
    }

    /// <summary>
    /// Rolling Balls puts one dot per ball on the run and spreads them evenly over it.
    /// <para>
    /// Nine balls at this intensity, and exactly nine colors over the whole capture - one per ball,
    /// read off the palette at <c>i * 255 / 9</c>. That the count is exactly nine rather than
    /// approximately nine is the check: a ball keeps its color for as long as the effect runs, so any
    /// tenth color would mean something was being recolored that should not be.
    /// </para>
    /// <para>
    /// 8.89 of those nine are visible in an average frame, the missing tenth of a ball being two that
    /// happen to be on the same LED. Every one is a single pixel - runs averaging 1.03 - because
    /// nothing here draws a trail unless the trails option is set.
    /// </para>
    /// <para>
    /// The occupancy is roughly even along the run, which is what separates rolling from falling.
    /// Nothing accelerates here: a ball crosses at constant speed and so spends equal time everywhere,
    /// where Bouncing Balls lingers at the top of its arc and piles up there.
    /// </para>
    /// </summary>
    [Fact]
    public void Rolling_balls_puts_one_dot_per_ball_and_spreads_them_evenly()
    {
        Crowd crowd = Measure(new RollingBallsEffect(), Run());

        output.WriteLine($"simulated: brightness {crowd.Brightness:F2}, {crowd.Lit:F2} lit per frame " +
            $"(most {crowd.MostLit}), runs {crowd.RunLength:F2} long, {crowd.Colors} colors, " +
            $"occupancy {crowd.Flattest:F3} to {crowd.Steepest:F3}");
        output.WriteLine("measured : brightness 6.16, 8.89 lit per frame (most 9), runs 1.03 long, " +
            "9 colors, occupancy 0.059 to 0.128");
        output.WriteLine("arithmetic: 128 / 16 + 1 = 9 balls");

        // One color per ball and no more, over twelve seconds.
        Assert.Equal(9, crowd.Colors);

        Assert.Equal(9, crowd.MostLit);
        Assert.InRange(crowd.Lit, 8.3, 9.0);

        // Dots, because trails are off.
        Assert.InRange(crowd.RunLength, 1.0, 1.15);

        // Constant speed, so no part of the run is favored much over another. Loose, because nine
        // balls over twelve seconds is a small sample of an even spread.
        Assert.InRange(crowd.Steepest - crowd.Flattest, 0, 0.13);
    }

    /// <summary>
    /// With collisions on the balls keep their order along the run; without, they walk through each
    /// other.
    /// <para>
    /// This is the only thing a collision does that can be seen from outside, and it can be seen
    /// because every ball has its own color: read the colors off in position order and you have a
    /// permutation of the nine balls. Balls that bounce off each other can never exchange places, so
    /// that permutation should hold; balls that ignore each other reshuffle it constantly.
    /// </para>
    /// <para>
    /// The strip gives 34 distinct orderings over twelve seconds without collisions and 11 with, and
    /// 18.73 adjacent swaps away from where it started against 3.88 - a five-fold separation. Not zero
    /// with collisions on, and it should not be: the collision test ignores anything within two
    /// milliseconds of a previous bounce, so a pair that has only just met can still slip past.
    /// </para>
    /// </summary>
    [Fact]
    public void Rolling_balls_keep_their_order_only_when_collisions_are_on()
    {
        EffectSegment free = Run();
        EffectSegment colliding = Run();
        colliding.Option1 = true;

        (int freeOrders, double freeSwaps) = Ordering(free);
        (int collidingOrders, double collidingSwaps) = Ordering(colliding);

        output.WriteLine($"simulated without collisions: {freeOrders} orderings, " +
            $"{freeSwaps:F2} swaps from the first");
        output.WriteLine($"simulated with collisions   : {collidingOrders} orderings, " +
            $"{collidingSwaps:F2} swaps from the first");
        output.WriteLine("measured without collisions : 34 orderings, 18.73 swaps");
        output.WriteLine("measured with collisions    : 11 orderings, 3.88 swaps");

        // Balls that bounce off each other stay in order; balls that do not, do not.
        Assert.True(collidingSwaps * 2 < freeSwaps,
            $"collisions should hold the order: {collidingSwaps:F2} against {freeSwaps:F2}");

        Assert.True(collidingOrders < freeOrders,
            $"and should give fewer distinct orderings: {collidingOrders} against {freeOrders}");
    }

    /// <summary>
    /// How many orderings of the balls appear, and how far from the first one they get, measured in
    /// adjacent swaps.
    /// </summary>
    private static (int Orderings, double Swaps) Ordering(EffectSegment segment)
    {
        // Ball i takes the palette at i * 255 / 9, so the nine colors are known in advance and in ball
        // order - which is better than discovering them, since it also checks that the balls are
        // colored the way they should be.
        int count = (segment.Intensity / 16) + 1;

        RgbColor[] colors = [.. Enumerable.Range(0, count)
            .Select(i => segment.ColorFromPalette(i * 255 / count, wrap: segment.SolidWrap))];

        List<int[]> orders = [];
        int strangers = 0;

        Strip.Sample(new RollingBallsEffect(), segment, 12_000, (frame, _) =>
        {
            List<int> seen = [];

            foreach (RgbColor pixel in frame)
            {
                if (Brightness(pixel) == 0)
                {
                    continue;
                }

                int ball = Array.IndexOf(colors, pixel);

                if (ball < 0)
                {
                    strangers++;
                }
                else if (!seen.Contains(ball))
                {
                    seen.Add(ball);
                }
            }

            // Only frames where every ball is somewhere of its own say anything about the order.
            if (seen.Count == count)
            {
                orders.Add([.. seen]);
            }
        });

        Assert.Equal(0, strangers);

        int[] first = orders[0];

        double swaps = orders.Average(order =>
        {
            int[] against = [.. order.Select(b => Array.IndexOf(first, b))];

            return (double)Enumerable.Range(0, against.Length)
                .Sum(i => Enumerable.Range(i + 1, against.Length - i - 1)
                    .Count(j => against[i] > against[j]));
        });

        return (orders.Select(o => string.Join(",", o)).Distinct().Count(), swaps);
    }

    /// <summary>What a capture of a crowd of movers looks like, reduced.</summary>
    private readonly record struct Crowd(
        double Brightness,
        double Spread,
        double Lit,
        int MostLit,
        double RunLength,
        int LongestRun,
        int Colors,
        double Flattest,
        double Steepest);

    private static Crowd Measure(IWledEffect effect, EffectSegment segment)
    {
        List<double> brightness = [], spread = [], lit = [];
        List<int> runs = [];
        HashSet<RgbColor> colors = [];
        var buckets = new int[10];
        int mostLit = 0;

        Strip.Sample(effect, segment, 12_000, (frame, _) =>
        {
            Strip.Shape profile = Strip.Profile(frame, Brightness);

            brightness.Add(profile.Mean);
            spread.Add(profile.Deviation);

            int[] on = [.. Enumerable.Range(0, frame.Length).Where(i => Brightness(frame[i]) > 0)];

            lit.Add(on.Length);
            mostLit = Math.Max(mostLit, on.Length);

            foreach (int i in on)
            {
                colors.Add(frame[i]);
                buckets[Math.Min(9, i * 10 / frame.Length)]++;
            }

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

        double total = buckets.Sum();
        double[] share = [.. buckets.Select(b => b / total)];

        return new Crowd(
            brightness.Average(),
            spread.Average(),
            lit.Average(),
            mostLit,
            runs.Count > 0 ? runs.Average() : 0,
            runs.Count > 0 ? runs.Max() : 0,
            colors.Count,
            share.Min(),
            share.Max());
    }

    private static byte Brightness(RgbColor color) =>
        Math.Max(color.R, Math.Max(color.G, color.B));
}
