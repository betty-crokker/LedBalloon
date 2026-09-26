using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The five effects that draw on a random number generator, captured on the south controller's
/// 285 LED roofline with the color slots set to red, blue and green.
/// <para>
/// These cannot be matched pixel for pixel. The controller's generator has its own state and
/// nothing can line it up with ours, so what is checked is how they behave in aggregate: how many
/// LEDs light at once, how often, how deep the variation runs. Where an effect's randomness only
/// picks a seed — Twinkle — the pattern that follows is ported exactly and only the seed differs.
/// </para>
/// </summary>
public class SparkleEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Roofline = 285;
    private const int MeasuredFrameMs = 9;

    private static EffectSegment Run() => new(Roofline)
    {
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = MeasuredFrameMs,
        PaletteId = 0,
        Palette = null,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    private static (double Mean, int Max, int ZeroFrames, int Frames) Count(
        IWledEffect effect, uint milliseconds, Func<RgbColor, bool> matches)
    {
        EffectSegment segment = Run();
        var counts = new List<int>();

        for (uint t = 0; t < milliseconds; t += MeasuredFrameMs)
        {
            effect.Render(segment, t);

            if (t >= 1500)
            {
                counts.Add(segment.Pixels.Count(matches));
            }
        }

        return (counts.Average(), counts.Max(), counts.Count(c => c == 0), counts.Count);
    }

    private static bool IsRed(RgbColor p) => p.R > p.G && p.R > p.B;

    private static bool IsBlue(RgbColor p) => p.B > p.R && p.B > p.G;

    /// <summary>
    /// One LED, whatever the length of the run. Speed sets how often it moves and nothing sets how
    /// many there are, which is the whole character of it.
    /// </summary>
    [Fact]
    public void Sparkle_lights_exactly_one_led_at_a_time()
    {
        (double mean, int max, _, _) = Count(new SparkleEffect(), 14_000, IsRed);

        output.WriteLine($"simulated: {mean:F2} lit per frame, max {max}");
        output.WriteLine("measured : 1.00 lit per frame, max 1");

        Assert.Equal(1.0, mean, 2);
        Assert.Equal(1, max);
    }

    /// <summary>
    /// Genuinely sparse: a throw of the dice every 127 ms with a one in seven chance, so most
    /// frames show nothing at all. The strip caught a flash in 7 frames of 310.
    /// </summary>
    [Fact]
    public void Sparkle_Dark_flashes_a_single_led_now_and_then()
    {
        (double mean, int max, int zero, int frames) = Count(new FlashSparkleEffect(), 18_000, IsBlue);

        output.WriteLine($"simulated: {mean:F2} lit per frame, max {max}, {zero} of {frames} frames dark");
        output.WriteLine("measured : 0.02 lit per frame, max 1, 303 of 310 frames dark");

        Assert.Equal(1, max);
        Assert.InRange(mean, 0.001, 0.15);
        Assert.InRange((double)zero / frames, 0.85, 0.9999);
    }

    /// <summary>
    /// The same dice as Sparkle Dark, but a hit scatters a third of the run's worth of draws.
    /// <para>
    /// Drawn with replacement, so far fewer than a third actually light: 95 draws over 285 LEDs
    /// leaves about 81 distinct, and the strip peaked at 83. Reproducing that is the point — an
    /// implementation that lit exactly 95 would read as a block rather than a scatter.
    /// </para>
    /// </summary>
    [Fact]
    public void Sparkle_Plus_scatters_about_a_quarter_of_the_run_when_it_hits()
    {
        (double mean, int max, int zero, int frames) = Count(new HyperSparkleEffect(), 18_000, IsBlue);

        output.WriteLine($"simulated: {mean:F2} lit per frame, max {max}, {zero} of {frames} frames dark");
        output.WriteLine("measured : 0.76 lit per frame, max 83, 313 of 316 frames dark");

        // 95 draws with replacement over 285 positions: about 81 distinct.
        Assert.InRange(max, 65, 95);
        Assert.InRange((double)zero / frames, 0.85, 0.9999);
    }

    /// <summary>
    /// LEDs filling in one at a time over a long round, then clearing and starting again. One
    /// round takes 93 seconds at these settings, so this watches a hundred.
    /// <para>
    /// The count is held down by the same collisions as Sparkle Plus: the round ends at 143 draws,
    /// which over 285 positions leaves about 113 distinct. The strip peaked at 118.
    /// </para>
    /// </summary>
    [Fact]
    public void Twinkle_fills_the_run_in_over_a_long_round()
    {
        (double mean, int max, _, _) = Count(new TwinkleEffect(), 100_000, IsRed);

        output.WriteLine($"simulated: {mean:F2} lit per frame, max {max}");
        output.WriteLine("measured : 57.53 lit per frame, max 118");

        Assert.InRange(mean, 45, 72);
        Assert.InRange(max, 95, 135);
    }

    /// <summary>
    /// Every LED dimmed by its own random amount, which is the one of these five that can be
    /// checked against arithmetic as well as against the strip: at these settings the subtraction
    /// is uniform over 0 to 27, so the run should average 255 - 13.5 and vary by 28/sqrt(12).
    /// </summary>
    [Fact]
    public void Fire_Flicker_dims_each_led_by_its_own_amount()
    {
        var effect = new FireFlickerEffect();
        EffectSegment segment = Run();

        List<double> means = [], sds = [];

        for (uint t = 0; t < 16_000; t += MeasuredFrameMs)
        {
            effect.Render(segment, t);

            if (t < 1500)
            {
                continue;
            }

            double[] v = [.. segment.Pixels.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];
            double m = v.Average();
            means.Add(m);
            sds.Add(Math.Sqrt(v.Average(x => (x - m) * (x - m))));

            // It subtracts the same amount from every channel, so a red stays red.
            Assert.All(segment.Pixels, p => Assert.Equal(0, p.G));
        }

        output.WriteLine($"simulated: bri {means.Average():F2} ({means.Min():F2}-{means.Max():F2}) sd {sds.Average():F2}");
        output.WriteLine("measured : bri 241.48 (240.36-242.71) sd 8.06");
        output.WriteLine("arithmetic: mean 241.5, sd 8.08");

        Assert.InRange(means.Average(), 239, 244);
        Assert.InRange(sds.Average(), 7.0, 9.2);
    }
}
