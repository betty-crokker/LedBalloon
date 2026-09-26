using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Pride 2015 and Tri Wipe on the south controller's 285 LED roofline, captured through WLED's
/// live preview, with the color slots set to red, blue and green.
/// </summary>
public class PrideEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Roofline = 285;
    private const int MeasuredFrameMs = 9;

    private static EffectSegment Run(byte speed = 128) => new(Roofline)
    {
        Speed = speed,
        Intensity = 128,
        FrameMilliseconds = MeasuredFrameMs,
        PaletteId = 0,
        Palette = null,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// The same five slow sines as Colorwaves, so it needs the same long look: the slowest comes
    /// round every 136 seconds and a short sample measures the phase instead of the effect.
    /// </summary>
    /// <remarks>
    /// <b>Only partly matched.</b> The average brightness, the number of bands and all three color
    /// channels land on the strip's numbers. The contrast does not: the strip swings from 39 to 83
    /// with a spread across the run of 32.6, and this swings from 55 to 94 with a spread of 25.2.
    /// So the shape and the color are right and the dark end is not dark enough.
    /// <para>
    /// Ruled out: the frame rate, which barely moves it — 9 ms and 23 ms give a spread of 25.2 and
    /// 24.7 — and output gamma, which this controller has switched off. The remaining suspect is
    /// the value handling inside the HSV conversion, where FastLED squares the value through
    /// scale8_video before scaling the channels. Left recorded rather than papered over: the
    /// preview is much closer than the generic approximation either way, and the assertion below
    /// says what is actually verified.
    /// </para>
    /// </remarks>
    [Fact]
    public void Pride_2015_builds_its_colors_from_hue_rather_than_the_palette()
    {
        var effect = new Pride2015Effect();
        EffectSegment segment = Run();

        List<double> means = [], sds = [], bands = [], lit = [];
        double r = 0, g = 0, b = 0;
        int frames = 0;

        for (uint t = 0; t < 26_500; t += MeasuredFrameMs)
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
            bands.Add(Bands(v));
            lit.Add(v.Count(x => x > 16) / (double)Roofline);

            r += segment.Pixels.Sum(p => (double)p.R);
            g += segment.Pixels.Sum(p => (double)p.G);
            b += segment.Pixels.Sum(p => (double)p.B);
            frames++;
        }

        double total = (double)frames * Roofline;

        output.WriteLine(
            $"simulated: bri {means.Average():F1} ({means.Min():F0}-{means.Max():F0}) " +
            $"sd {sds.Average():F1} bands {bands.Average():F1} lit {lit.Average():F3} " +
            $"rgb {r / total:F0}/{g / total:F0}/{b / total:F0}");
        output.WriteLine("measured : bri 64.5 (39-83) sd 32.6 bands 41.5 lit 0.982 rgb 35/28/24");

        Assert.InRange(means.Average(), 55, 75);

        // Banded rather than flat, which is the structural claim. The strip reaches 32.6 and this
        // does not; see the remarks. Tightening this is the open item, not a licence to widen it.
        Assert.InRange(sds.Average(), 22, 36);
        Assert.InRange(bands.Average(), 35, 46);
        Assert.InRange(lit.Average(), 0.93, 1.0);

        // A rainbow, so no channel runs away with it — unlike Colorwaves on palette Default, which
        // is one color at varying brightness.
        Assert.InRange(r / total, 28, 44);
        Assert.InRange(g / total, 21, 36);
        Assert.InRange(b / total, 18, 32);
    }

    /// <summary>
    /// Three wipes in a loop, each color taking about a third of the run's time. Measured at the
    /// top of the speed slider so a full cycle is one second; at the middle it is twenty-six.
    /// <para>
    /// Brightness says nothing useful here — red and blue both read 255 — so this counts which
    /// color slot each LED is showing instead. The first capture used brightness and reported a
    /// spread of zero across the run, which is true and entirely uninformative about a wipe.
    /// </para>
    /// </summary>
    [Fact]
    public void Tri_Wipe_sweeps_the_three_colors_in_turn()
    {
        var effect = new TriWipeEffect();
        EffectSegment segment = Run(speed: 255);

        List<double> red = [], blue = [], green = [];
        var redPeaks = new List<uint>();
        bool wasFull = false;

        for (uint t = 0; t < 14_000; t += MeasuredFrameMs)
        {
            effect.Render(segment, t);

            double r = segment.Pixels.Count(p => p.R > p.G && p.R > p.B) / (double)Roofline;
            double b = segment.Pixels.Count(p => p.B > p.R && p.B > p.G) / (double)Roofline;
            double g = segment.Pixels.Count(p => p.G > p.R && p.G > p.B) / (double)Roofline;

            red.Add(r);
            blue.Add(b);
            green.Add(g);

            bool full = r > 0.95;

            if (full && !wasFull)
            {
                redPeaks.Add(t);
            }

            wasFull = full;
        }

        var periods = redPeaks.Zip(redPeaks.Skip(1), (a, c) => (double)(c - a)).ToList();
        double cycle = periods.Count > 0 ? periods.Average() : 0;
        double mixed = red.Count(x => x > 0.05 && x < 0.95) / (double)red.Count;

        output.WriteLine(
            $"simulated: red {red.Average():F3} blue {blue.Average():F3} green {green.Average():F3}, " +
            $"cycle {cycle:F0} ms, mixed {mixed:P0}");
        output.WriteLine("measured : red 0.340 blue 0.336 green 0.325, cycle ~1000 ms, mixed 60%");

        Assert.InRange(red.Average(), 0.28, 0.40);
        Assert.InRange(blue.Average(), 0.28, 0.40);
        Assert.InRange(green.Average(), 0.27, 0.39);

        Assert.InRange(cycle, 900, 1100);

        // Two colors at once for most of the time is what makes it a wipe rather than a cut.
        Assert.InRange(mixed, 0.45, 0.75);
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
}
