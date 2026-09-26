using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Scan and Scan Dual, captured on the south controller's 285 LED roofline with the color slots set
/// to red, blue and green.
/// <para>
/// These two are flat colors with hard edges, which makes them easy to measure precisely: every
/// LED is one of two or three exact colors, so counting them measures the block's width directly
/// rather than by inference.
/// </para>
/// </summary>
public class ScanEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>
    /// What share of the run each color slot holds, averaged over the capture, plus the most colors
    /// seen at once.
    /// </summary>
    private static (double Red, double Green, double Blue, int MostFills) Shares(
        IWledEffect effect, byte speed, uint milliseconds)
    {
        List<double> red = [], green = [], blue = [];
        int most = 0;

        Strip.Sample(effect, Strip.Run(speed), milliseconds, (frame, _) =>
        {
            red.Add((double)frame.Count(p => Strip.Dominant(p) == 0) / frame.Length);
            green.Add((double)frame.Count(p => Strip.Dominant(p) == 1) / frame.Length);
            blue.Add((double)frame.Count(p => Strip.Dominant(p) == 2) / frame.Length);
            most = Math.Max(most, Strip.Fills(frame));
        });

        return (red.Average(), green.Average(), blue.Average(), most);
    }

    /// <summary>
    /// A block a quarter of the run wide, not a dot.
    /// <para>
    /// The slider WLED labels "# of dots" sets <c>1 + ((intensity * length) &gt;&gt; 9)</c>, so at
    /// the middle of the slider a 285 LED run gets a block of 72 - a quarter of the roofline, lit at
    /// once. The strip held 25.3% red against a 74.7% blue background, which is 72 and 213 exactly.
    /// </para>
    /// </summary>
    [Fact]
    public void Scan_lights_a_quarter_of_the_run_at_a_time()
    {
        (double red, double green, double blue, int most) = Shares(new ScanEffect(), 128, 14_000);

        output.WriteLine($"simulated: red {red:F3} green {green:F3} blue {blue:F3}, {most} fills");
        output.WriteLine("measured : red 0.253 green 0.000 blue 0.747, 2 fills");
        output.WriteLine($"arithmetic: 1 + ((128 * 285) >> 9) = 72 of 285 = {72 / 285.0:F3}");

        Assert.Equal(2, most);
        Assert.Equal(0, green, 3);
        Assert.InRange(red, 0.24, 0.27);
        Assert.InRange(blue, 0.73, 0.76);
    }

    /// <summary>
    /// The mirrored block is drawn first and the primary block over the top of it, so where the two
    /// cross the primary wins.
    /// <para>
    /// That is why the two do not come out equal: the strip held 25.3% red but only 19.8% green,
    /// where two blocks of 72 would be 25.3% each. The 5.5% shortfall is the overlap, which happens
    /// as the two blocks pass through the middle of the run. Getting this right is the difference
    /// between the two blocks merging as they cross and one of them blinking out.
    /// </para>
    /// </summary>
    [Fact]
    public void Scan_Dual_lets_the_primary_block_cover_the_mirrored_one()
    {
        (double red, double green, double blue, int most) = Shares(new DualScanEffect(), 128, 14_000);

        output.WriteLine($"simulated: red {red:F3} green {green:F3} blue {blue:F3}, {most} fills");
        output.WriteLine("measured : red 0.253 green 0.198 blue 0.549, 3 fills");

        Assert.Equal(3, most);
        Assert.InRange(red, 0.24, 0.27);
        Assert.InRange(blue, 0.53, 0.57);

        // Short of the red block by the overlap, and not by much more or much less.
        Assert.InRange(green, 0.17, 0.22);
        Assert.True(green < red, "the mirrored block is the one that gets covered");
    }

    /// <summary>
    /// The block folds at the ends rather than wrapping round them, so it paces back and forth.
    /// <para>
    /// Timed at full speed, where a cycle is 750 ms and a single traverse 375: over fourteen seconds
    /// the strip turned round 39 times, which is a traverse every 359 ms. Sampled at 71 ms that
    /// count is good to about one, so the two agree.
    /// </para>
    /// </summary>
    [Fact]
    public void Scan_paces_back_and_forth_rather_than_wrapping()
    {
        List<int> lefts = [];

        Strip.Sample(new ScanEffect(), Strip.Run(255), 14_000, (frame, _) =>
            lefts.Add(Array.FindIndex(frame, p => Strip.Dominant(p) == 0)));

        int turns = 0, wraps = 0, last = 0;

        for (int i = 1; i < lefts.Count; i++)
        {
            int step = lefts[i] - lefts[i - 1];

            if (Math.Abs(step) > Strip.Roofline / 2)
            {
                wraps++;
                continue;
            }

            int direction = Math.Sign(step);

            if (direction == 0)
            {
                continue;
            }

            if (last != 0 && direction != last)
            {
                turns++;
            }

            last = direction;
        }

        output.WriteLine($"simulated: {turns} turns, {wraps} wraps, {14_000.0 / turns:F0} ms per traverse");
        output.WriteLine("measured : 39 turns, 0 wraps, 359 ms per traverse");
        output.WriteLine("formula  : 750 ms per cycle at full speed, so 375 per traverse");

        Assert.Equal(0, wraps);
        Assert.InRange(turns, 34, 42);
    }

    /// <summary>
    /// The block takes its color from where it has reached rather than carrying one along, which
    /// only shows on a palette - so it is checked against a palette rather than against the strip,
    /// which was captured on the color slots.
    /// </summary>
    [Fact]
    public void Scan_takes_its_color_from_where_the_block_has_reached()
    {
        EffectSegment segment = Strip.Run(255);
        segment.PaletteId = 6;
        segment.Palette = new WledPalette
        {
            Stops = [.. Enumerable.Range(0, 16).Select(i => new PaletteStop(
                (byte)(i * 17), new RgbColor((byte)(i * 17), 0, (byte)(255 - (i * 17)))))],
        };

        List<RgbColor> blocks = [];

        Strip.Sample(new ScanEffect(), segment, 4_000, (frame, _) =>
        {
            int at = Array.FindIndex(frame, p => p != segment.Colors[1]);

            if (at >= 0)
            {
                blocks.Add(frame[at]);
            }
        });

        output.WriteLine($"{blocks.Distinct().Count()} distinct leading colors over {blocks.Count} frames");

        // A block carrying one color would show one; a block reading the palette shows many.
        Assert.True(blocks.Distinct().Count() > 8,
            "the block should change color as it travels along the palette");
    }
}
