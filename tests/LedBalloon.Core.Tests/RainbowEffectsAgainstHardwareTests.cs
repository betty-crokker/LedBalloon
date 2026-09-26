using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Four more effects captured on the south controller's 285 LED roofline at speed 128, intensity
/// 128, palette Default, colors red, blue and green.
/// <para>
/// Simulated at the frame time the strip actually runs, 9 ms, rather than the 23 ms its settings
/// claim. Three of these read the clock and would not care either way; Strobe Mega counts flashes
/// and does.
/// </para>
/// </summary>
public class RainbowEffectsAgainstHardwareTests(ITestOutputHelper output)
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

    private sealed record Fingerprint(
        double Brightness, double BrightnessMin, double BrightnessMax,
        double SpatialSd, double Red, double Green, double Blue);

    private static Fingerprint Watch(IWledEffect effect, uint milliseconds, uint settle = 1200)
    {
        EffectSegment segment = Run();
        List<double> means = [], sds = [];
        double r = 0, g = 0, b = 0;
        int frames = 0;

        for (uint t = 0; t < settle + milliseconds; t += MeasuredFrameMs)
        {
            effect.Render(segment, t);

            if (t < settle)
            {
                continue;
            }

            double[] v = [.. segment.Pixels.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];
            means.Add(v.Average());

            double m = v.Average();
            sds.Add(Math.Sqrt(v.Average(x => (x - m) * (x - m))));

            r += segment.Pixels.Sum(p => (double)p.R);
            g += segment.Pixels.Sum(p => (double)p.G);
            b += segment.Pixels.Sum(p => (double)p.B);
            frames++;
        }

        double total = (double)frames * Roofline;

        return new Fingerprint(
            means.Average(), means.Min(), means.Max(), sds.Average(),
            r / total, g / total, b / total);
    }

    private void Report(string measured, Fingerprint f) =>
        output.WriteLine(
            $"simulated: bri {f.Brightness:F1} ({f.BrightnessMin:F0}-{f.BrightnessMax:F0}) " +
            $"sd {f.SpatialSd:F1} rgb {f.Red:F0}/{f.Green:F0}/{f.Blue:F0}\nmeasured : {measured}");

    /// <summary>
    /// The whole run is one color at any instant, so the spread across it is zero — and that is
    /// what separates it from Rainbow, which is the same walk spread along the run.
    /// </summary>
    [Fact]
    public void Colorloop_walks_the_whole_run_through_the_wheel_together()
    {
        Fingerprint f = Watch(new ColorloopEffect(), 16_000);
        Report("bri 191.0 (129-255) sd 0.0 rgb 85/81/89", f);

        Assert.Equal(0, f.SpatialSd, 1);
        Assert.InRange(f.Brightness, 180, 202);
        Assert.InRange(f.BrightnessMin, 120, 140);
        Assert.Equal(255, f.BrightnessMax, 0);

        // A full turn of the wheel spends equal time in each third, so no channel dominates.
        Assert.InRange(f.Red, 74, 96);
        Assert.InRange(f.Green, 70, 92);
        Assert.InRange(f.Blue, 78, 100);
    }

    /// <summary>
    /// The same wheel laid along the run instead of applied to all of it, so the brightness is
    /// dead flat over time while the spread across the run is not.
    /// </summary>
    [Fact]
    public void Rainbow_lays_the_wheel_along_the_run()
    {
        Fingerprint f = Watch(new RainbowEffect(), 16_000);
        Report("bri 191.5 (191.5-191.5) sd 37.0 rgb 86/85/85", f);

        Assert.InRange(f.Brightness, 182, 201);
        Assert.InRange(f.SpatialSd, 32, 42);

        // Flat over time: every frame is the same wheel, only rotated.
        Assert.InRange(f.BrightnessMax - f.BrightnessMin, 0, 3);

        Assert.InRange(f.Red, 76, 96);
        Assert.InRange(f.Green, 75, 95);
        Assert.InRange(f.Blue, 75, 95);
    }

    /// <summary>
    /// Bursts of brief flashes over a background: 13 flashes about 65 ms apart, then a long rest,
    /// repeating every 3.6 seconds. The run is uniform and never dark, flashing in color 1 over a
    /// background of color 2.
    /// <para>
    /// The flashes are 15 ms and the live preview samples every 60, so one capture cannot time
    /// them. The first caught red on 5 of 205 samples and looked like a failure; a second caught
    /// 49 of 503. Together, 54 of 708 is 7.6% against 7.3% simulated. What a single capture can
    /// measure honestly is the rhythm, because the rest between bursts is far longer than the
    /// sampling interval.
    /// </para>
    /// </summary>
    [Fact]
    public void Strobe_Mega_flashes_in_bursts_at_the_rhythm_the_strip_kept()
    {
        EffectSegment segment = Run();
        var effect = new StrobeMegaEffect();

        var flashes = new List<uint>();
        var sds = new List<double>();
        int frames = 0;

        for (uint t = 0; t < 31_200; t += MeasuredFrameMs)
        {
            effect.Render(segment, t);

            if (t < 1200)
            {
                continue;
            }

            double[] v = [.. segment.Pixels.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];
            double m = v.Average();
            sds.Add(Math.Sqrt(v.Average(x => (x - m) * (x - m))));

            if (segment.Pixels[150].R > segment.Pixels[150].B)
            {
                flashes.Add(t);
            }

            // Uniform, and never dark: the background is color 2 rather than off.
            Assert.All(segment.Pixels, p => Assert.True(Math.Max(p.R, p.B) > 16));
            frames++;
        }

        // A gap longer than the spacing within a burst starts a new burst.
        var starts = new List<uint>();
        uint previous = 0;

        foreach (uint t in flashes)
        {
            if (starts.Count == 0 || t - previous > 600)
            {
                starts.Add(t);
            }

            previous = t;
        }

        var periods = starts.Zip(starts.Skip(1), (a, b) => (double)(b - a)).Order().ToList();
        double median = periods.Count > 0 ? periods[periods.Count / 2] : 0;
        double duty = (double)flashes.Count / frames;

        output.WriteLine($"simulated: {starts.Count} bursts, period {median:F0} ms, duty {duty:P1}");
        output.WriteLine("measured : 9 bursts, period 3681 ms, duty 7.6% (54 of 708 samples)");

        Assert.Equal(0, sds.Average(), 1);
        Assert.InRange(median, 3300, 4050);
        Assert.InRange(duty, 0.05, 0.11);
    }

    /// <summary>
    /// Crossfades between the three color slots in a loop, the whole run together. Only the middle
    /// leg goes through the palette, which on Default is color slot 3.
    /// </summary>
    [Fact]
    public void Tri_Fade_cycles_the_three_colors()
    {
        Fingerprint f = Watch(new TriFadeEffect(), 20_000);
        Report("bri 192.1 (127-255) sd 0.0 rgb 83/77/93", f);

        Assert.Equal(0, f.SpatialSd, 1);
        Assert.InRange(f.Brightness, 181, 203);
        Assert.InRange(f.BrightnessMin, 118, 138);
        Assert.Equal(255, f.BrightnessMax, 0);

        Assert.InRange(f.Red, 72, 94);
        Assert.InRange(f.Green, 66, 88);
        Assert.InRange(f.Blue, 82, 104);
    }
}
