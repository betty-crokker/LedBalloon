using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Candle, Candle Multi and Phased, captured on the south controller's 285 LED roofline with the
/// color slots set to red, blue and green.
/// <para>
/// The two candles were captured at the settings WLED's own metadata gives them - speed 96, intensity
/// 224 - rather than at the middle of both sliders, because those defaults are what makes a candle
/// look like a candle and what the step arithmetic is tuned for.
/// </para>
/// </summary>
public class CandleEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <param name="LevelSd">How much the run's average brightness wanders over time.</param>
    /// <param name="SpatialSd">
    /// How much LEDs differ from each other within a frame, which is the whole difference between
    /// one candle and many.
    /// </param>
    private sealed record Flicker(
        double Level, double Lowest, double Highest, double LevelSd, double SpatialSd, int Flickers);

    private static Flicker Measure(IWledEffect effect, byte speed, byte intensity, uint milliseconds)
    {
        EffectSegment segment = Strip.Run(speed, intensity);

        List<double> level = [], spread = [];

        Strip.Sample(effect, segment, milliseconds, (frame, _) =>
        {
            double[] v = [.. frame.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];
            double mean = v.Average();

            level.Add(mean);
            spread.Add(Math.Sqrt(v.Average(x => (x - mean) * (x - mean))));
        });

        double overall = level.Average();

        int flickers = level.Zip(level.Skip(1)).Count(p => p.First <= overall && p.Second > overall);

        return new Flicker(
            overall, level.Min(), level.Max(),
            Math.Sqrt(level.Average(x => (x - overall) * (x - overall))),
            spread.Average(), flickers);
    }

    /// <summary>
    /// One flame: every LED shares the same brightness, and it wanders.
    /// <para>
    /// The strip's spatial spread was exactly zero in every frame - all 285 LEDs the same - while its
    /// brightness over time had a spread of 22.6 around a mean of 158, wandering between 127 and 236.
    /// That pair of numbers is the effect: flat in space, restless in time.
    /// </para>
    /// </summary>
    [Fact]
    public void Candle_flickers_the_whole_run_together()
    {
        Flicker measured = Measure(new CandleEffect(), 96, 224, 20_000);

        output.WriteLine($"simulated: level {measured.Level:F1} ({measured.Lowest:F0}-{measured.Highest:F0}), " +
            $"over time {measured.LevelSd:F1}, across the run {measured.SpatialSd:F2}, " +
            $"{measured.Flickers} flickers");
        output.WriteLine("measured : level 157.7 (127-236), over time 22.6, across the run 0.00, " +
            "21 flickers");

        // Not one LED out of step with the others.
        Assert.Equal(0, measured.SpatialSd, 5);

        Assert.InRange(measured.Level, 135, 185);
        Assert.InRange(measured.LevelSd, 14, 32);
    }

    /// <summary>
    /// A flame per LED, which is the exact mirror image of the single candle: the spread moves from
    /// time into space.
    /// <para>
    /// The strip's spatial spread went from 0 to 24.4 and its variation over time from 22.6 down to
    /// 1.3 - because 285 independent flames average out to a steady total. The mean brightness is the
    /// same for both, 158, since it is the same distribution either way.
    /// </para>
    /// </summary>
    [Fact]
    public void Candle_Multi_moves_the_flicker_from_time_into_space()
    {
        Flicker multi = Measure(new CandleMultiEffect(), 96, 224, 20_000);
        Flicker single = Measure(new CandleEffect(), 96, 224, 20_000);

        output.WriteLine($"simulated multi : level {multi.Level:F1}, over time {multi.LevelSd:F2}, " +
            $"across the run {multi.SpatialSd:F1}");
        output.WriteLine("measured multi  : level 158.2, over time 1.34, across the run 24.4");
        output.WriteLine($"simulated single: over time {single.LevelSd:F1}, across the run {single.SpatialSd:F2}");
        output.WriteLine("measured single : over time 22.6, across the run 0.00");

        Assert.InRange(multi.SpatialSd, 17, 32);
        Assert.InRange(multi.LevelSd, 0.4, 4);

        // Same mean as one candle, since each flame draws from the same distribution.
        Assert.InRange(Math.Abs(multi.Level - single.Level), 0, 20);

        // And the two are opposites, which is the claim worth pinning.
        Assert.True(multi.SpatialSd > single.SpatialSd * 5, "many flames should vary along the run");
        Assert.True(multi.LevelSd < single.LevelSd / 5, "many flames should average out over time");
    }

    /// <summary>
    /// Candle asks to be drawn at a fixed 23 ms, whatever the controller could manage.
    /// <para>
    /// A third frame time, and the only place it is used: WLED's <c>FRAMETIME_FIXED</c>, the interval
    /// the firmware would have at its nominal 42 frames a second. Candle's step divisors assume it -
    /// the source comment says "mode called every ~25 ms" - and this strip draws four times faster
    /// than that, so without the request the flame would gutter four times too fast.
    /// </para>
    /// </summary>
    [Fact]
    public void Candle_asks_for_a_fixed_frame_time_rather_than_the_strips_own()
    {
        var segment = new EffectSegment(10) { FrameMilliseconds = 9, FrameTime = 2 };
        segment.Draw(new CandleEffect(), 0);

        output.WriteLine($"asks for {segment.FrameDelay} ms, where the strip draws every " +
            $"{segment.FrameMilliseconds} and FRAMETIME is {segment.FrameTime}");

        Assert.Equal(FrameTime.FixedFrameDelay, segment.FrameDelay);
        Assert.Equal(23, segment.FrameDelay);
    }

    /// <summary>
    /// Phased: waves whose wavelength varies along the run, clipped at the bottom so only the peaks
    /// light.
    /// <para>
    /// Measured on the red channel rather than on brightness, for the reason that keeps coming up -
    /// the effect blends blue into red, so brightness dips in the middle of its own swing and reports
    /// twice as many bands as there are. On red the strip gave 73.9 peaks, a mean of 39.5 and half the
    /// run sitting below the cutoff.
    /// </para>
    /// <para>
    /// Intensity is that cutoff and it works backwards: 128 leaves half the run dark.
    /// </para>
    /// </summary>
    [Fact]
    public void Phased_clips_the_bottom_off_its_waves()
    {
        List<double> mean = [], sd = [], rising = [], dark = [];
        List<int> bands = [];

        Strip.Sample(new PhasedEffect(), Strip.Run(), 16_000, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => p.R);

            bands.Add(shape.Bands);
            mean.Add(shape.Mean);
            sd.Add(shape.Deviation);
            rising.Add(shape.Rising);
            dark.Add((double)frame.Count(p => p.R < 4) / frame.Length);
        });

        output.WriteLine($"simulated: mean {mean.Average():F1} sd {sd.Average():F1} " +
            $"bands {bands.Average():F1} rising {rising.Average():F3} dark {dark.Average():F3}");
        output.WriteLine("measured : mean 39.5 sd 48.5 bands 73.9 rising 0.363 dark 0.513");

        Assert.InRange(mean.Average(), 28, 52);
        Assert.InRange(sd.Average(), 38, 58);
        Assert.InRange(bands.Average(), 60, 88);

        // Half the run below the cutoff, which is what intensity 128 asks for.
        Assert.InRange(dark.Average(), 0.42, 0.60);
    }
}
