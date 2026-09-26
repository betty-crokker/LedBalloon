using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Running, Saw and Running Dual, captured on the south controller's 285 LED roofline with the
/// color slots set to red, blue and green.
/// <para>
/// These are waves rather than blocks, so they are measured on one channel rather than on
/// brightness. Brightness is the wrong signal here and misled the first capture: the wave blends
/// blue into red, so the brightest pixels are the ones at <em>both</em> ends of its swing and the
/// dimmest are the ones in the middle. Measured that way Running showed 71 bands where it has 35,
/// and its period came out at half what it is. The red channel rises and falls with the wave
/// exactly once, so that is what these read.
/// </para>
/// </summary>
public class RunningEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <param name="Bands">Peaks along the run, which is how many waves fit on it.</param>
    /// <param name="Rising">
    /// What share of neighboring pairs get brighter, which is the wave's shape: a half for anything
    /// symmetrical, far less for one that ramps up and coasts down.
    /// </param>
    /// <param name="PeriodMs">How long one LED takes to go round its cycle.</param>
    private sealed record Wave(double Mean, double Deviation, double Bands, double Rising, double PeriodMs);

    private static Wave Measure(IWledEffect effect, Func<RgbColor, byte> channel, uint milliseconds)
    {
        List<double> means = [], deviations = [], bands = [], rising = [];
        List<byte> first = [];

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            byte[] v = [.. frame.Select(channel)];

            double mean = v.Average(x => (double)x);
            means.Add(mean);
            deviations.Add(Math.Sqrt(v.Average(x => (x - mean) * (x - mean))));

            int peaks = 0, ups = 0;
            bool climbing = false;

            for (int i = 1; i < v.Length; i++)
            {
                if (v[i] > v[i - 1])
                {
                    climbing = true;
                    ups++;
                }
                else if (climbing && v[i] < v[i - 1])
                {
                    peaks++;
                    climbing = false;
                }
            }

            bands.Add(peaks);
            rising.Add((double)ups / (v.Length - 1));
            first.Add(v[0]);
        });

        // One LED's own swing gives the period without having to follow a wave along the run.
        double level = first.Average(x => (double)x);
        int cycles = first.Zip(first.Skip(1)).Count(p => p.First <= level && p.Second > level);

        return new Wave(
            means.Average(), deviations.Average(), bands.Average(), rising.Average(),
            cycles > 0 ? (double)milliseconds / cycles : 0);
    }

    /// <summary>
    /// Intensity is the wavelength, not the brightness: it divides the spacing between LEDs, so at
    /// the middle of the slider a wave is eight LEDs long and a 285 LED roofline carries 35 of them.
    /// <para>
    /// A symmetrical sine, so half of each wave is on the way up. The strip gave 35.4 bands, half of
    /// the neighboring pairs rising, and a mean and spread of 127 and 90 - which is what a sine
    /// swinging over the whole byte comes to.
    /// </para>
    /// </summary>
    [Fact]
    public void Running_carries_thirty_five_symmetrical_waves_along_the_roofline()
    {
        Wave wave = Measure(new RunningLightsEffect(), p => p.R, 14_000);

        output.WriteLine($"simulated: mean {wave.Mean:F1} sd {wave.Deviation:F1} " +
            $"bands {wave.Bands:F2} rising {wave.Rising:F3} period {wave.PeriodMs:F0} ms");
        output.WriteLine("measured : mean 127.0 sd 90.3 bands 35.37 rising 0.493 period 1000 ms");
        output.WriteLine("arithmetic: 285 LEDs / (256 / (128 >> 2)) = 35.6 waves; 1024 ms per cycle");

        Assert.InRange(wave.Mean, 120, 135);
        Assert.InRange(wave.Deviation, 85, 95);
        Assert.InRange(wave.Bands, 34, 37);
        Assert.InRange(wave.Rising, 0.45, 0.55);
        Assert.InRange(wave.PeriodMs, 900, 1150);
    }

    /// <summary>
    /// The same wave with a sawtooth profile: the same 35 bands, the same mean and the same spread,
    /// and the one number that tells them apart is the shape.
    /// <para>
    /// Running has half its pairs rising and Saw has a sixth of them - 0.493 against 0.173 on the
    /// strip. That is the whole difference between the two effects and nothing else in the capture
    /// shows it, which is why this is the statistic worth asserting on.
    /// </para>
    /// </summary>
    [Fact]
    public void Saw_ramps_up_and_coasts_down_where_Running_is_symmetrical()
    {
        Wave saw = Measure(new SawEffect(), p => p.R, 14_000);
        Wave running = Measure(new RunningLightsEffect(), p => p.R, 14_000);

        output.WriteLine($"simulated: mean {saw.Mean:F1} sd {saw.Deviation:F1} " +
            $"bands {saw.Bands:F2} rising {saw.Rising:F3} period {saw.PeriodMs:F0} ms");
        output.WriteLine("measured : mean 128.3 sd 90.4 bands 35.37 rising 0.173 period 1077 ms");
        output.WriteLine($"simulated Running for comparison: rising {running.Rising:F3}");

        Assert.InRange(saw.Mean, 120, 138);
        Assert.InRange(saw.Deviation, 85, 95);
        Assert.InRange(saw.Bands, 34, 37);
        Assert.InRange(saw.Rising, 0.12, 0.24);
        Assert.InRange(saw.PeriodMs, 900, 1200);

        Assert.True(saw.Rising < running.Rising / 2,
            "a sawtooth spends far less of the run climbing than a sine does");
    }

    /// <summary>
    /// Two waves crossing, each half strength, with gaps between the pulses.
    /// <para>
    /// The gapped sine is what makes this a different effect rather than Running in two colors: it
    /// sits flat at zero for half its period, so there are half as many pulses over the same run and
    /// they take twice as long to come round. The strip bore out both - 17.7 bands against Running's
    /// 35.4, and 2000 ms against 1000.
    /// </para>
    /// <para>
    /// And each wave is blended half and half with the other, so neither reaches full strength: a
    /// mean of 32 in each channel where Running's is 127.
    /// </para>
    /// </summary>
    [Fact]
    public void Running_Dual_crosses_two_half_strength_waves_with_gaps_between_the_pulses()
    {
        Wave red = Measure(new RunningDualEffect(), p => p.R, 14_000);
        Wave green = Measure(new RunningDualEffect(), p => p.G, 14_000);

        output.WriteLine($"simulated red  : mean {red.Mean:F1} sd {red.Deviation:F1} " +
            $"bands {red.Bands:F2} period {red.PeriodMs:F0} ms");
        output.WriteLine("measured red   : mean 31.6 sd 44.9 bands 17.67 period 2000 ms");
        output.WriteLine($"simulated green: mean {green.Mean:F1} sd {green.Deviation:F1} bands {green.Bands:F2}");
        output.WriteLine("measured green : mean 31.3 sd 44.5 bands 17.67");

        Assert.InRange(red.Mean, 26, 38);
        Assert.InRange(red.Deviation, 39, 51);
        Assert.InRange(red.Bands, 16.5, 19);
        Assert.InRange(red.PeriodMs, 1800, 2400);

        // The second wave is the third color slot's, and it behaves the same as the first.
        Assert.InRange(green.Mean, 26, 38);
        Assert.InRange(green.Bands, 16.5, 19);
    }
}
