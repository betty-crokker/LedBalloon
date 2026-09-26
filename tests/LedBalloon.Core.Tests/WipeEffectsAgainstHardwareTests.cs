using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The four wipes, captured on the south controller's 285 LED roofline with the color slots set to
/// red and blue.
/// <para>
/// All four share one function, so what separates them is worth measuring precisely rather than
/// eyeballing: the boundary's travel is read off the strip frame by frame, and a wipe shows up as a
/// ramp that always runs the same way while a sweep shows up as one that turns round at each end.
/// Both were counted over eighteen seconds and neither showed a single instance of the other's
/// signature.
/// </para>
/// <para>
/// The strip's live preview arrives about every 71 ms, far slower than the 9 ms the effect is drawn
/// at, so the simulation is drawn at 9 ms and sampled at 71 ms to be compared on equal terms.
/// </para>
/// </summary>
public class WipeEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Roofline = 285;
    private const int MeasuredFrameMs = 9;

    /// <summary>How often the controller's live preview sends a frame, which is what was sampled.</summary>
    private const int PreviewSampleMs = 71;

    private static EffectSegment Run(byte speed, byte intensity) => new(Roofline)
    {
        Speed = speed,
        Intensity = intensity,
        FrameMilliseconds = MeasuredFrameMs,
        PaletteId = 0,
        Palette = null,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// One run of an effect, sampled as the strip was and reduced to the handful of numbers the
    /// strip could be measured on.
    /// </summary>
    /// <param name="Edges">Where the sharpest color change sat in each sampled frame.</param>
    /// <param name="Times">When each of those samples was taken, in the controller's milliseconds.</param>
    /// <param name="RedFraction">How much of the run showed the primary color, averaged.</param>
    /// <param name="PartialFrames">Frames whose boundary LED was a blend of neither fill.</param>
    /// <param name="CrowdedFrames">Frames with more than one such LED, which should never happen.</param>
    /// <param name="MostFills">The most distinct fill colors seen at once.</param>
    /// <param name="Fills">Each distinct fill color, in the order it first appeared.</param>
    private sealed record Sampled(
        IReadOnlyList<int> Edges,
        IReadOnlyList<uint> Times,
        double RedFraction,
        int PartialFrames,
        int CrowdedFrames,
        int MostFills,
        IReadOnlyList<RgbColor> Fills)
    {
        public int Frames => Edges.Count;
    }

    private static Sampled Capture(IWledEffect effect, byte speed, byte intensity, uint milliseconds)
    {
        EffectSegment segment = Run(speed, intensity);

        List<int> edges = [];
        List<uint> times = [];
        List<double> reds = [];
        List<RgbColor> fills = [];
        int partial = 0, crowded = 0, most = 0;
        uint nextSample = 0;

        for (uint t = 0; t < milliseconds; t += MeasuredFrameMs)
        {
            effect.Render(segment, t);

            if (t < nextSample)
            {
                continue;
            }

            nextSample = t + PreviewSampleMs;

            RgbColor[] pixels = segment.Pixels;

            edges.Add(SharpestChange(pixels));
            times.Add(t);
            reds.Add((double)pixels.Count(p => p.R > p.B) / pixels.Length);

            // The two colors covering the most LEDs are the fills; anything far from both is the
            // anti-aliased boundary.
            RgbColor[] dominant = [.. pixels.GroupBy(p => p)
                .OrderByDescending(g => g.Count())
                .Take(2)
                .Select(g => g.Key)];

            int odd = pixels.Count(p => dominant.All(c => Apart(p, c) > 30));

            if (odd >= 1)
            {
                partial++;
            }

            if (odd > 1)
            {
                crowded++;
            }

            RgbColor[] covering = [.. pixels.GroupBy(p => p).Where(g => g.Count() > 3).Select(g => g.Key)];
            most = Math.Max(most, covering.Length);

            foreach (RgbColor color in covering.Where(c => !fills.Contains(c)))
            {
                fills.Add(color);
            }
        }

        return new Sampled(edges, times, reds.Average(), partial, crowded, most, fills);
    }

    private static int Apart(RgbColor first, RgbColor second) =>
        Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);

    /// <summary>Where the boundary is: the one place along the run where the color jumps.</summary>
    private static int SharpestChange(RgbColor[] pixels)
    {
        int at = -1, biggest = 0;

        for (int i = 1; i < pixels.Length; i++)
        {
            int jump = Apart(pixels[i], pixels[i - 1]);

            if (jump > biggest)
            {
                biggest = jump;
                at = i;
            }
        }

        return at;
    }

    /// <summary>
    /// How the boundary travels, which is the whole of what separates a wipe from a sweep.
    /// <para>
    /// A wipe's boundary always runs the same way, so every half cycle ends in a jump from one end
    /// of the run back to the other. A sweep's turns round instead, so every half cycle ends in a
    /// change of direction and there are no jumps at all. Counting both tells the two apart without
    /// caring which way round the run happens to be wired.
    /// </para>
    /// </summary>
    /// <param name="HalfCycleMs">
    /// How long a leg lasts, read off the spacing of those events rather than off how many of them
    /// fitted in the window. A count over a duration is only right when the window happens to start
    /// at the top of a cycle: a 27 second look at a 4500 ms cycle holds twelve legs, but starting
    /// mid-leg means eleven boundaries fall inside it and starting on one means twelve do. Spacing
    /// does not care where the window opened.
    /// </param>
    private static (int Wraps, int Turns, double HalfCycleMs) Travel(
        IReadOnlyList<int> edges, IReadOnlyList<uint> times)
    {
        List<int> directions = [];
        List<uint> events = [];
        int wraps = 0;

        for (int i = 1; i < edges.Count; i++)
        {
            int step = edges[i] - edges[i - 1];

            if (Math.Abs(step) > Roofline / 2)
            {
                wraps++;
                events.Add(times[i]);
                directions.Add(-Math.Sign(step));
            }
            else
            {
                directions.Add(Math.Sign(step));
            }
        }

        int turns = 0, last = 0;

        for (int i = 0; i < directions.Count; i++)
        {
            int direction = directions[i];

            if (direction == 0)
            {
                continue;
            }

            if (last != 0 && direction != last)
            {
                turns++;
                events.Add(times[i + 1]);
            }

            last = direction;
        }

        events.Sort();

        double span = events.Count > 1 ? events[^1] - events[0] : 0;

        return (wraps, turns, events.Count > 1 ? span / (events.Count - 1) : 0);
    }

    /// <summary>
    /// The boundary running the same way every time, half the run in each color, and a cycle of
    /// 750 ms at full speed.
    /// </summary>
    [Fact]
    public void Wipe_runs_its_boundary_the_same_way_every_time()
    {
        Sampled capture = Capture(new WipeEffect(), speed: 255, intensity: 128, 18_000);
        (int wraps, int turns, double half) = Travel(capture.Edges, capture.Times);

        output.WriteLine($"simulated: {capture.Frames} frames, {wraps} wraps, {turns} turns, " +
            $"{half:F0} ms per half cycle, red {capture.RedFraction:F3}");
        output.WriteLine("measured : 253 frames, 48 wraps, 0 turns, 375 ms per half cycle, red 0.501");

        Assert.Equal(0, turns);
        Assert.InRange(wraps, 46, 50);
        Assert.InRange(half, 365, 385);
        Assert.InRange(capture.RedFraction, 0.47, 0.53);
    }

    /// <summary>
    /// The same fill with the return leg reversed, so the boundary turns round at each end instead
    /// of jumping back. The strip showed 47 turns and not one jump.
    /// </summary>
    [Fact]
    public void Sweep_turns_its_boundary_round_at_each_end()
    {
        Sampled capture = Capture(new SweepEffect(), speed: 255, intensity: 128, 18_000);
        (int wraps, int turns, double half) = Travel(capture.Edges, capture.Times);

        output.WriteLine($"simulated: {capture.Frames} frames, {wraps} wraps, {turns} turns, " +
            $"{half:F0} ms per half cycle, red {capture.RedFraction:F3}");
        output.WriteLine("measured : 253 frames, 0 wraps, 47 turns, 383 ms per half cycle, red 0.495");

        Assert.Equal(0, wraps);
        Assert.InRange(turns, 45, 50);
        Assert.InRange(half, 360, 400);
        Assert.InRange(capture.RedFraction, 0.47, 0.53);
    }

    /// <summary>
    /// Speed's second data point, so the cycle formula is pinned by its slope and not only by where
    /// it starts: speed 230 should give 4500 ms, and the strip gave half cycles of exactly 2250 ms.
    /// </summary>
    [Fact]
    public void Speed_sets_the_cycle_to_750_ms_plus_150_per_step_below_full()
    {
        Sampled capture = Capture(new WipeEffect(), speed: 230, intensity: 128, 27_000);
        (int wraps, int turns, double half) = Travel(capture.Edges, capture.Times);

        output.WriteLine($"simulated: {wraps} wraps, {turns} turns, {half:F0} ms per half cycle");
        output.WriteLine("measured : 12 wraps, 0 turns, 2250 ms per half cycle");
        output.WriteLine("formula  : 750 + (255 - 230) * 150 = 4500 ms, so 2250 per half");

        Assert.Equal(0, turns);
        Assert.InRange(half, 2200, 2300);
    }

    /// <summary>
    /// The boundary LED is one LED and it is anti-aliased.
    /// <para>
    /// This is the measurement the port turned on. The remainder that positions the front between
    /// two LEDs is written to truncate to sixteen bits, and the source says so in a comment rather
    /// than in the types - so it was worth checking, because read as a full-width value it would
    /// saturate for the whole leg and the edge would be hard in every single frame.
    /// </para>
    /// <para>
    /// It is not. The share of frames caught mid-blend tracks intensity exactly as truncation
    /// predicts, over a range that spans more than an order of magnitude: 5.5%, 43.2% and 84.9% of
    /// frames at intensity 20, 128 and 250. The full-width reading predicts zero at all three.
    /// </para>
    /// <para>
    /// Each measurement lands a little under the arithmetic - 0.082, 0.504, 0.980 - because a blend
    /// within a few percent of either end is counted as that end by the tolerance used here. The
    /// simulation is measured with the same tolerance, so it carries the same bias.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(20, 0.055)]
    [InlineData(128, 0.432)]
    [InlineData(250, 0.849)]
    public void The_boundary_led_is_blended_between_the_two_fills(byte intensity, double measured)
    {
        Sampled capture = Capture(new WipeEffect(), speed: 255, intensity: intensity, 16_000);

        double share = (double)capture.PartialFrames / capture.Frames;

        output.WriteLine($"simulated: {share:F3} of {capture.Frames} frames part blended");
        output.WriteLine($"measured : {measured:F3}");
        output.WriteLine($"arithmetic: {(intensity + 1) / 256.0:F3} before the tolerance bias");

        Assert.Equal(0, capture.CrowdedFrames);
        Assert.InRange(share, measured - 0.08, measured + 0.08);
    }

    /// <summary>
    /// The Random variants pick their colors off the wheel, so a preview cannot show the same colors
    /// the strip does - the draws are the controller's own. What it can show is the behavior, and
    /// that is what the strip was measured on.
    /// <para>
    /// Two fills on screen at a time and never a third: over 1163 frames the strip averaged 1.98
    /// distinct fills and never once reached three, which is what says a new color replaces the
    /// older of the two rather than joining them.
    /// </para>
    /// <para>
    /// And never two alike in a row. WLED draws each new color at least 42 of 255 around the wheel
    /// from the last, so a change always reads as a change; across 33 fills the closest consecutive
    /// pair measured 35 apart in hue, the shortfall being the wheel's own uneven spacing rather than
    /// a shorter distance.
    /// </para>
    /// </summary>
    [Fact]
    public void Wipe_Random_shows_two_colors_at_a_time_and_never_two_alike_in_a_row()
    {
        Sampled capture = Capture(new WipeRandomEffect(), speed: 230, intensity: 128, 27_000);
        (int wraps, int turns, double half) = Travel(capture.Edges, capture.Times);

        output.WriteLine($"simulated: {capture.MostFills} fills at most, " +
            $"{capture.Fills.Count} distinct over {capture.Frames} frames, {wraps} wraps, {turns} turns");
        output.WriteLine("measured : 2 fills at most, 33 distinct over 1163 frames, 48 wraps, 0 turns");

        Assert.Equal(2, capture.MostFills);
        Assert.Equal(0, turns);

        // Enough colors to have had the chance to repeat itself.
        Assert.InRange(capture.Fills.Count, 8, 40);

        Assert.All(capture.Fills.Zip(capture.Fills.Skip(1)),
            pair => Assert.True(Apart(pair.First, pair.Second) > 60,
                $"{pair.First} then {pair.Second} are too close to read as a change"));
    }

    /// <summary>
    /// Sweep Random turns round at each end like Sweep does, so each new color is introduced from
    /// the end the last one finished at. Its effect id is 36 rather than the 7 that sitting beside
    /// Wipe Random in the source would suggest, which is why these are matched by name.
    /// </summary>
    [Fact]
    public void Sweep_Random_turns_round_like_Sweep_does()
    {
        Sampled capture = Capture(new SweepRandomEffect(), speed: 255, intensity: 128, 18_000);
        (int wraps, int turns, double half) = Travel(capture.Edges, capture.Times);

        output.WriteLine($"simulated: {wraps} wraps, {turns} turns, {capture.MostFills} fills at most");
        output.WriteLine("measured : 0 wraps, 48 turns, 2 fills at most");

        Assert.Equal(0, wraps);
        Assert.InRange(turns, 45, 50);
        Assert.Equal(2, capture.MostFills);
    }
}
