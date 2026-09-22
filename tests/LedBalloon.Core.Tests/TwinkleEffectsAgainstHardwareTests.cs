using LedBalloon.Core;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The three effects that keep state per LED, measured against the strip running them on the 308
/// LED porchline at speed 128, intensity 128, on palette 254.
/// <para>
/// Compared as distributions rather than frame by frame, and for two of them that is the only
/// option there is: Colortwinkles and Ripple choose where to light up at random, so a preview of
/// one can never agree with the house pixel for pixel. What it can agree on is how much of the run
/// is lit and how brightly, which is what someone looking at a photo of their house actually reads.
/// </para>
/// <para>
/// Twinklecat is the exception and worth knowing about: it looks random and is not. It restarts the
/// same pseudo-random sequence from the same seed every frame, so each LED gets the same offset and
/// speed without anything being remembered - which means it can be reproduced exactly.
/// </para>
/// </summary>
public class TwinkleEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Porchline = 308;
    private const int FrameMs = EffectSimulation.DefaultFrameMilliseconds;

    private static WledPalette RedWhiteAndBlue() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(0, 0, 0)),
            new PaletteStop(16, new RgbColor(0, 0, 255)),
            new PaletteStop(32, new RgbColor(5, 21, 214)),
            new PaletteStop(48, new RgbColor(10, 42, 173)),
            new PaletteStop(64, new RgbColor(10, 42, 173)),
            new PaletteStop(80, new RgbColor(71, 95, 193)),
            new PaletteStop(96, new RgbColor(132, 148, 214)),
            new PaletteStop(112, new RgbColor(193, 201, 234)),
            new PaletteStop(128, new RgbColor(255, 255, 255)),
            new PaletteStop(144, new RgbColor(255, 255, 255)),
            new PaletteStop(160, new RgbColor(255, 170, 170)),
            new PaletteStop(176, new RgbColor(255, 85, 86)),
            new PaletteStop(192, new RgbColor(255, 0, 2)),
            new PaletteStop(208, new RgbColor(255, 0, 2)),
            new PaletteStop(224, new RgbColor(149, 0, 1)),
            new PaletteStop(240, new RgbColor(43, 0, 0)),
        ],
    };

    private static EffectSegment Run() => new(Porchline)
    {
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = FrameMs,
        PaletteId = 254,
        Palette = RedWhiteAndBlue(),
        Colors = [new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black],
    };

    /// <summary>Runs an effect for a few seconds and reports what it lit, once it has settled.</summary>
    private (double Lit, double Bright, double Brightness) Measure(IWledEffect effect, string name)
    {
        EffectSegment segment = Run();

        var lit = new List<int>();
        var bright = new List<int>();
        var level = new List<double>();

        for (uint t = 0; t < 6000; t += FrameMs)
        {
            effect.Render(segment, t);

            if (t <= 1500)
            {
                continue;
            }

            lit.Add(segment.Pixels.Count(p => p.R + p.G + p.B > 0));
            bright.Add(segment.Pixels.Count(p => p.R + p.G + p.B > 200));
            level.Add(segment.Pixels.Average(p => (double)Math.Max(p.R, Math.Max(p.G, p.B))));
        }

        output.WriteLine(
            $"{name}: lit {lit.Min()}-{lit.Max()} mean {lit.Average():F1}, " +
            $"bright mean {bright.Average():F1}, brightness {level.Average():F1}");

        return (lit.Average(), bright.Average(), level.Average());
    }

    [Fact]
    public void Colortwinkles_lights_about_as_much_of_the_run_as_the_strip_did()
    {
        // Strip: lit 142-180 averaging 162, about 40 bright at a time, mean brightness 41.
        (double lit, double bright, double brightness) = Measure(new ColorTwinklesEffect(), "simulated");
        output.WriteLine("measured : lit 142-180 mean 162.1, bright mean 39.9, brightness 41.4");

        Assert.InRange(lit, 162 * 0.7, 162 * 1.3);
        Assert.InRange(bright, 39.9 * 0.5, 39.9 * 1.8);
        Assert.InRange(brightness, 41.4 * 0.6, 41.4 * 1.6);
    }

    [Fact]
    public void Twinklecat_lights_about_as_much_of_the_run_as_the_strip_did()
    {
        // Strip: lit 161-183 averaging 172, about 62 bright at a time, mean brightness 61.
        (double lit, double bright, double brightness) = Measure(new TwinkleCatEffect(), "simulated");
        output.WriteLine("measured : lit 161-183 mean 172.1, bright mean 61.8, brightness 60.6");

        Assert.InRange(lit, 172 * 0.7, 172 * 1.3);
        Assert.InRange(bright, 61.8 * 0.5, 61.8 * 1.8);
        Assert.InRange(brightness, 60.6 * 0.6, 60.6 * 1.6);
    }

    [Fact]
    public void Ripple_leaves_most_of_the_run_dark_the_way_the_strip_did()
    {
        // Strip: lit 47-118 averaging 84 - far less than the twinkles, because a ripple is a few
        // rings rather than a scattering, and most of the run is between them.
        (double lit, double bright, double brightness) = Measure(new RippleEffect(), "simulated");
        output.WriteLine("measured : lit 47-118 mean 83.7, bright mean 18.1, brightness 19.5");

        Assert.InRange(lit, 83.7 * 0.5, 83.7 * 1.6);
        Assert.InRange(brightness, 19.5 * 0.4, 19.5 * 2.0);
    }

    [Fact]
    public void Twinklecat_draws_the_same_frame_twice_because_its_randomness_is_not_random()
    {
        // The sequence restarts from a fixed seed every frame, so the same clock gives the same
        // picture. This is what makes it the one twinkle a preview can reproduce exactly.
        var effect = new TwinkleCatEffect();

        EffectSegment first = Run();
        EffectSegment second = Run();

        effect.Render(first, 4000);
        effect.Render(second, 4000);

        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void Colortwinkles_does_not_draw_the_same_frame_twice()
    {
        // Where the next one lights is genuinely chosen at random, so two runs diverge. Seeded, so
        // they diverge the same way every time and a measurement of them means something.
        var effect = new ColorTwinklesEffect();

        EffectSegment segment = Run();
        segment.Random = new Random(1);

        EffectSegment other = Run();
        other.Random = new Random(2);

        for (uint t = 0; t < 2000; t += FrameMs)
        {
            effect.Render(segment, t);
            effect.Render(other, t);
        }

        Assert.NotEqual(segment.Pixels, other.Pixels);
    }

    [Fact]
    public void A_ripple_spreads_outward_from_where_it_started()
    {
        // One ripple on a bare run, followed for a while: it should reach further as it goes.
        var effect = new RippleEffect();
        EffectSegment segment = Run();
        segment.Random = new Random(7);

        var spans = new List<int>();

        for (uint t = 0; t < 1500; t += FrameMs)
        {
            effect.Render(segment, t);

            int[] lit = [.. Enumerable.Range(0, Porchline)
                .Where(i => segment.Pixels[i].R + segment.Pixels[i].G + segment.Pixels[i].B > 0)];

            if (lit.Length > 1)
            {
                spans.Add(lit[^1] - lit[0]);
            }
        }

        output.WriteLine($"widest span reached: {spans.Max()} LEDs");
        Assert.True(spans.Max() > spans.First(), "the rings should travel outward, not sit still");
    }
}
