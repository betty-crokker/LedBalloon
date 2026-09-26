using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Four effects driven onto the south controller's 285 LED roofline and captured through WLED's
/// live preview, at speed 128, intensity 128, palette Default, with the three color slots set to
/// red, blue and green so each one can be told apart in the capture.
/// <para>
/// Each capture is long enough to cover the effect's own slowest component, which for Lake is a
/// 14 beat a minute sine and for Heartbeat is a 56 beat a minute one. Getting that wrong measures
/// the phase rather than the effect.
/// </para>
/// </summary>
public class PatternEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Roofline = 285;
    private const int FrameMs = EffectSimulation.DefaultFrameMilliseconds;

    /// <summary>
    /// What the controller actually runs at, which is not what it is configured for. hw.led.fps
    /// says 42; the strip reports 107 and a fit of Heartbeat's own decay curve says 108. A 285 LED
    /// WS2812 run takes 8.55 ms to clock out, so ~9.2 ms a frame is the wire speed and the
    /// configured figure is not the effect rate at all.
    /// <para>
    /// Only matters to effects that accumulate per frame rather than reading the clock. Most read
    /// the clock and do not care; Heartbeat decays by a fixed ratio every call and cares a lot.
    /// </para>
    /// </summary>
    private const int MeasuredFrameMs = 9;

    private static EffectSegment Run(int frameMs = FrameMs) => new(Roofline)
    {
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = frameMs,
        PaletteId = 0,
        Palette = null,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    /// <summary>The same numbers the capture harness computed, from the same definitions.</summary>
    private sealed record Fingerprint(
        double Brightness, double BrightnessMin, double BrightnessMax,
        double SpatialSd, double Bands, double LitFraction,
        double Red, double Green, double Blue);

    private static Fingerprint Watch(
        IWledEffect effect, uint milliseconds, uint settle = 1200, int frameMs = FrameMs)
    {
        EffectSegment segment = Run(frameMs);
        List<double> means = [], sds = [], bands = [], lit = [];
        double r = 0, g = 0, b = 0;
        int frames = 0;

        for (uint t = 0; t < settle + milliseconds; t += (uint)frameMs)
        {
            effect.Render(segment, t);

            if (t < settle)
            {
                continue;
            }

            double[] v = [.. segment.Pixels.Select(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)))];
            means.Add(v.Average());
            sds.Add(Sd(v));
            bands.Add(Bands(v));
            lit.Add(v.Count(x => x > 16) / (double)Roofline);

            r += segment.Pixels.Sum(p => (double)p.R);
            g += segment.Pixels.Sum(p => (double)p.G);
            b += segment.Pixels.Sum(p => (double)p.B);
            frames++;
        }

        double total = (double)frames * Roofline;

        return new Fingerprint(
            means.Average(), means.Min(), means.Max(),
            sds.Average(), bands.Average(), lit.Average(),
            r / total, g / total, b / total);
    }

    private static double Sd(double[] v)
    {
        double m = v.Average();
        return Math.Sqrt(v.Average(x => (x - m) * (x - m)));
    }

    private static int Bands(double[] v)
    {
        int n = 0;
        bool rising = false;
        double low = v[0];

        for (int i = 1; i < v.Length; i++)
        {
            if (v[i] > v[i - 1])
            {
                if (!rising)
                {
                    low = v[i - 1];
                }

                rising = true;
            }
            else if (rising && v[i - 1] - low > 12)
            {
                n++;
                rising = false;
            }
        }

        return n;
    }

    private void Report(string measured, Fingerprint f) =>
        output.WriteLine(
            $"simulated: bri {f.Brightness:F1} ({f.BrightnessMin:F0}-{f.BrightnessMax:F0}) " +
            $"sd {f.SpatialSd:F1} bands {f.Bands:F1} lit {f.LitFraction:F3} " +
            $"rgb {f.Red:F0}/{f.Green:F0}/{f.Blue:F0}\nmeasured : {measured}");

    /// <summary>
    /// 129 lit then 129 unlit then the remainder, on a 285 LED run: 156 red and 129 blue. Nothing
    /// moves, so every frame is the same and the capture agrees to the unit.
    /// </summary>
    [Fact]
    public void Solid_Pattern_lays_down_the_blocks_the_strip_laid_down()
    {
        Fingerprint f = Watch(new StaticPatternEffect(), 8000);
        Report("bri 255 (255-255) sd 0.0 bands 0 lit 1.000 rgb 140/0/115", f);

        Assert.Equal(255, f.Brightness, 0);
        Assert.Equal(0, f.SpatialSd, 1);
        Assert.Equal(140, f.Red, 0);
        Assert.Equal(0, f.Green, 0);
        Assert.Equal(115, f.Blue, 0);
    }

    /// <summary>Equal thirds of the three color slots, so each channel averages a third of full.</summary>
    [Fact]
    public void Solid_Pattern_Tri_splits_the_run_between_the_three_colors()
    {
        Fingerprint f = Watch(new TriStaticPatternEffect(), 8000);
        Report("bri 255 (255-255) sd 0.0 bands 0 lit 1.000 rgb 85/85/85", f);

        Assert.Equal(255, f.Brightness, 0);
        Assert.Equal(0, f.SpatialSd, 1);
        Assert.Equal(85, f.Red, 0);
        Assert.Equal(85, f.Green, 0);
        Assert.Equal(85, f.Blue, 0);
    }

    [Fact]
    public void Lake_ripples_the_way_the_strip_rippled()
    {
        Fingerprint f = Watch(new LakeEffect(), 33_000);
        Report("bri 89.9 (55-129) sd 58.6 bands 25.3 lit 0.827 rgb 90/0/0", f);

        Assert.InRange(f.Brightness, 80, 100);
        Assert.InRange(f.SpatialSd, 52, 65);
        Assert.InRange(f.Bands, 22, 29);
        Assert.InRange(f.LitFraction, 0.75, 0.90);

        // Palette Default falls back to color slot 1, so the whole thing is the primary.
        Assert.Equal(0, f.Green, 0);
        Assert.Equal(0, f.Blue, 0);
    }

    /// <summary>
    /// The run pulses together rather than in bands, which is why the spread across it is zero at
    /// every instant. It swings between the primary and the background, so both channels show.
    /// </summary>
    [Fact]
    public void Heartbeat_beats_the_way_the_strip_beat()
    {
        Fingerprint f = Watch(new HeartbeatEffect(), 22_000, frameMs: MeasuredFrameMs);
        Report("bri 208.9 (127-255) sd 0.0 bands 0 lit 1.000 rgb 68/0/185", f);

        Assert.Equal(0, f.SpatialSd, 1);
        Assert.InRange(f.Brightness, 195, 222);
        Assert.InRange(f.BrightnessMin, 115, 140);
        Assert.Equal(255, f.BrightnessMax, 0);
        Assert.InRange(f.Red, 58, 78);
        Assert.InRange(f.Blue, 175, 196);
    }
}
