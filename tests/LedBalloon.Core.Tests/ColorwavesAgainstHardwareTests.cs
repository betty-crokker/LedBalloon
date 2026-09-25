using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Colorwaves driven onto the south controller's 285 LED roofline at speed 128, intensity 128,
/// palette Default with a red primary, and its LED buffer captured through WLED's own live
/// preview. The numbers asserted here are what the strip did.
/// <para>
/// Captured over 140 seconds, 2154 frames, and not a second less. Colorwaves is driven by five
/// sines at tempos between 0.44 and 1.6 beats a minute, and the slowest takes 136 seconds to come
/// round. A ten second sample catches whatever phase it happens to catch: the first attempt at
/// this measured a mean brightness of 176 against a true cycle mean of 151, and called a correct
/// port broken.
/// </para>
/// </summary>
public class ColorwavesAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Roofline = 285;
    private const int FrameMs = EffectSimulation.DefaultFrameMilliseconds;

    private static EffectSegment Run() => new(Roofline)
    {
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = FrameMs,
        PaletteId = 0,
        Palette = null,
        Colors = [new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black],
    };

    private static double[] Brightness(EffectSegment segment) =>
        [.. segment.Pixels.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];

    private static double Sd(double[] values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    /// <summary>Local maxima standing at least 12 above the trough before them, as the capture counted them.</summary>
    private static int Bands(double[] v)
    {
        int count = 0;
        bool rising = false;
        double lastMin = v[0];

        for (int i = 1; i < v.Length; i++)
        {
            if (v[i] > v[i - 1])
            {
                if (!rising)
                {
                    lastMin = v[i - 1];
                }

                rising = true;
            }
            else if (rising && v[i - 1] - lastMin > 12)
            {
                count++;
                rising = false;
            }
        }

        return count;
    }

    private (List<double> Means, List<double> Sds, List<int> Bands) Simulate(uint milliseconds = 140_000)
    {
        var effect = new ColorwavesEffect();
        EffectSegment segment = Run();

        List<double> means = [], sds = [];
        List<int> bands = [];

        for (uint t = 0; t < milliseconds; t += FrameMs)
        {
            effect.Render(segment, t);

            // The first frames are the pattern building from black, which the capture never saw.
            if (t < 1000)
            {
                continue;
            }

            double[] b = Brightness(segment);
            means.Add(b.Average());
            sds.Add(Sd(b));
            bands.Add(Bands(b));
        }

        return (means, sds, bands);
    }

    [Fact]
    public void It_sits_at_the_brightness_the_strip_sat_at()
    {
        (List<double> means, _, _) = Simulate();

        output.WriteLine($"simulated: mean {means.Average():F1}, range {means.Min():F1}-{means.Max():F1}");
        output.WriteLine("measured : mean 150.9, range 111.4-192.4");

        Assert.InRange(means.Average(), 138, 164);
        Assert.InRange(means.Min(), 100, 125);
        Assert.InRange(means.Max(), 180, 205);
    }

    /// <summary>
    /// The thing that makes it Colorwaves rather than a pulse: at any instant the run is not one
    /// brightness, it is bands. A flat effect would score near zero here.
    /// </summary>
    [Fact]
    public void It_is_banded_across_the_run_the_way_the_strip_was()
    {
        (_, List<double> sds, _) = Simulate();

        output.WriteLine($"simulated: spatial sd mean {sds.Average():F1}, range {sds.Min():F1}-{sds.Max():F1}");
        output.WriteLine("measured : spatial sd mean 46.3, range 24.7-73.6");

        Assert.InRange(sds.Average(), 39, 54);
    }

    [Fact]
    public void It_fits_the_same_number_of_bands_along_the_run()
    {
        (_, _, List<int> bands) = Simulate();

        output.WriteLine($"simulated: {bands.Average():F1} bands (min {bands.Min()}, max {bands.Max()})");
        output.WriteLine("measured : 35.8 bands (max 45)");

        Assert.InRange(bands.Average(), 30, 42);
    }

    /// <summary>
    /// On palette Default the hue walk has nowhere to go: every lookup falls back to color slot 1,
    /// so the run is one hue at varying brightness. The capture measured g=0 and b=0 throughout.
    /// </summary>
    [Fact]
    public void On_the_default_palette_it_stays_the_one_color()
    {
        var effect = new ColorwavesEffect();
        EffectSegment segment = Run();

        for (uint t = 0; t < 40_000; t += FrameMs)
        {
            effect.Render(segment, t);

            Assert.All(segment.Pixels, p => Assert.Equal(0, p.G));
            Assert.All(segment.Pixels, p => Assert.Equal(0, p.B));
        }

        Assert.InRange(segment.Pixels.Average(p => (double)p.R), 100, 205);
    }
}
