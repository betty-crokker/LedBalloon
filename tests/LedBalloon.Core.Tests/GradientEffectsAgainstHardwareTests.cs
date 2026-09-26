using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Gradient and Loading, captured on the south controller's 285 LED roofline with the color slots
/// set to red, blue and green.
/// <para>
/// These two share a function and differ by one flag, and the first statistic that came to hand
/// could not tell them apart: both spend the same share of the run climbing, 0.225 of neighboring
/// pairs, because both have a 64 LED ramp. What separates them is what happens at the far end of
/// that ramp - Gradient comes back down over another 64 LEDs and Loading falls off a cliff.
/// </para>
/// </summary>
public class GradientEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor Background = new(0, 0, 255);

    private static (Strip.Shape Shape, double Lit, int MostFills, double PeriodMs) Measure(
        IWledEffect effect, uint milliseconds)
    {
        List<double> lit = [], rising = [];
        List<int> bands = [], rises = [], falls = [];
        List<byte> first = [];
        int most = 0;

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => p.R);

            bands.Add(shape.Bands);
            rising.Add(shape.Rising);
            rises.Add(shape.SteepestRise);
            falls.Add(shape.SteepestFall);

            lit.Add((double)frame.Count(p => p != Background) / frame.Length);
            most = Math.Max(most, Strip.Fills(frame));
            first.Add(frame[0].R);
        });

        double level = first.Average(x => (double)x);
        int cycles = first.Zip(first.Skip(1)).Count(p => p.First <= level && p.Second > level);

        var shaped = new Strip.Shape(
            0, 0, (int)Math.Round(bands.Average()), rising.Average(),
            (int)Math.Round(rises.Average()), (int)Math.Round(falls.Average()));

        return (shaped, lit.Average(), most, cycles > 0 ? (double)milliseconds / cycles : 0);
    }

    /// <summary>
    /// One soft blob sliding round the run, fading off over 64 LEDs on each side.
    /// <para>
    /// The blob is 128 LEDs across in all, which is 45% of the roofline - the strip showed 44.6%
    /// of it lit at any moment, and never more or less, because the blob keeps its shape as it
    /// travels. Both its edges are gentle: the steepest step in the red channel was 4 in either
    /// direction, which is 255 spread over 64 LEDs.
    /// </para>
    /// </summary>
    [Fact]
    public void Gradient_slides_a_blob_that_fades_off_at_both_edges()
    {
        (Strip.Shape shape, double lit, int most, double period) = Measure(new GradientEffect(), 12_000);

        output.WriteLine($"simulated: lit {lit:F3}, {shape.Bands} band, rising {shape.Rising:F3}, " +
            $"steepest rise {shape.SteepestRise} fall {shape.SteepestFall}, " +
            $"{most} fills, period {period:F0} ms");
        output.WriteLine("measured : lit 0.446, 1 band, rising 0.225, steepest rise 4 fall 4, " +
            "16 fills, period 2000 ms");
        output.WriteLine("arithmetic: spread = intensity / 2 = 64, so 128 of 285 = 0.449");

        Assert.Equal(1, shape.Bands);
        Assert.InRange(lit, 0.42, 0.47);
        Assert.InRange(shape.Rising, 0.20, 0.25);

        // Both edges gentle, which is the whole of what makes this one a gradient.
        Assert.InRange(shape.SteepestRise, 3, 6);
        Assert.InRange(shape.SteepestFall, 3, 6);
        Assert.InRange(period, 1800, 2200);
    }

    /// <summary>
    /// The same blob with its far edge cut off square, so it reads as a bar filling rather than as
    /// a wave passing.
    /// <para>
    /// Half as much of the run is lit - 22.5% against 44.6%, which is the 64 LED ramp without its
    /// mirror image - and the edge where the ramp ends is a single step of 252 out of 255. The
    /// gentle side still steps by 4, so the two together say which way round the shape is.
    /// </para>
    /// </summary>
    [Fact]
    public void Loading_cuts_the_far_edge_of_the_blob_off_square()
    {
        (Strip.Shape shape, double lit, int most, double period) = Measure(new LoadingEffect(), 12_000);

        output.WriteLine($"simulated: lit {lit:F3}, rising {shape.Rising:F3}, " +
            $"steepest rise {shape.SteepestRise} fall {shape.SteepestFall}, " +
            $"{most} fills, period {period:F0} ms");
        output.WriteLine("measured : lit 0.225, rising 0.003, steepest rise 252 fall 4, " +
            "2 fills, period 2000 ms");

        Assert.InRange(lit, 0.20, 0.25);

        // One side a cliff and the other a ramp. The run arrives reversed, so the cliff shows up as
        // the rise.
        Assert.InRange(shape.SteepestRise, 200, 255);
        Assert.InRange(shape.SteepestFall, 3, 6);
        Assert.InRange(period, 1800, 2200);
    }

    /// <summary>
    /// Both effects take the same spread, which is a bug in WLED reproduced on purpose.
    /// <para>
    /// The source reads <c>int brd = 1 + loading ? intensity/2 : intensity/4;</c>, which C parses as
    /// <c>(1 + loading) ? ... : ...</c> - always true, so the quarter-width branch is dead code and
    /// Loading gets the same half-width spread Gradient does. The strip agrees: Loading lights
    /// 22.5% of the run, which is exactly half of Gradient's 44.6% and not half of half.
    /// </para>
    /// <para>
    /// Reproduced rather than corrected. Loading looks the way it looks on the wall because of this,
    /// and a preview that quietly fixed it would be the one that disagreed with the house.
    /// </para>
    /// </summary>
    [Fact]
    public void Loading_gets_the_same_spread_as_Gradient_because_of_a_precedence_bug()
    {
        (_, double gradient, _, _) = Measure(new GradientEffect(), 8_000);
        (_, double loading, _, _) = Measure(new LoadingEffect(), 8_000);

        output.WriteLine($"simulated: gradient lights {gradient:F3}, loading {loading:F3}, " +
            $"ratio {loading / gradient:F3}");
        output.WriteLine("measured : gradient 0.446, loading 0.225, ratio 0.504");
        output.WriteLine("a quarter-width Loading would give a ratio of about 0.25");

        // Half, because the ramp is the same width and there is only one of it. A quarter would
        // mean the dead branch had come back to life.
        Assert.InRange(loading / gradient, 0.45, 0.56);
    }
}
