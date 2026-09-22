using LedBalloon.Core;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Each of these was driven onto the north controller's 308 LED porchline and its LED buffer
/// captured over several seconds, so the numbers asserted here are what the strip did rather than
/// what the code does.
/// </summary>
public class SimpleEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Porchline = 308;
    private const int FrameMs = EffectSimulation.DefaultFrameMilliseconds;

    /// <summary>
    /// Palette 254 on the north controller: the red, white and blue one, exactly as
    /// <c>/json/palx</c> serves it - sixteen stops, gamma already applied.
    /// </summary>
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

    private static EffectSegment Run(byte speed, byte intensity, int paletteId = 0) =>
        new(Porchline)
        {
            Speed = speed,
            Intensity = intensity,
            FrameMilliseconds = FrameMs,
            PaletteId = paletteId,
            Palette = paletteId == 0 ? null : RedWhiteAndBlue(),
            Colors = [new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black],
        };

    private static double MeanBrightness(EffectSegment segment) =>
        segment.Pixels.Average(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)));

    // ---------------------------------------------------------------------------------- blink

    [Fact]
    public void Blink_runs_the_cycle_the_strip_ran_and_spends_half_of_it_lit()
    {
        // Measured: 2612 ms a cycle, lit in 53% of frames, at speed 128.
        var effect = new BlinkEffect();
        EffectSegment segment = Run(speed: 128, intensity: 128);

        var lit = new List<bool>();
        var edges = new List<uint>();
        bool? previous = null;

        for (uint t = 0; t < 8000; t += FrameMs)
        {
            effect.Render(segment, t);
            bool on = MeanBrightness(segment) > 8;
            lit.Add(on);

            if (previous is { } was && was != on)
            {
                edges.Add(t);
            }

            previous = on;
        }

        double duty = 100.0 * lit.Count(x => x) / lit.Count;
        var halves = edges.Zip(edges.Skip(1), (a, b) => (double)(b - a)).ToList();
        double cycle = halves.Count > 0 ? 2 * halves.Average() : 0;

        output.WriteLine($"simulated: {duty:F0}% duty, cycle {cycle:F0} ms");
        output.WriteLine("measured : 53% duty, cycle 2612 ms");

        Assert.InRange(cycle, 2612 * 0.95, 2612 * 1.05);
        Assert.InRange(duty, 45, 60);
    }

    // -------------------------------------------------------------------------------- breathe

    [Fact]
    public void Breathe_swings_between_the_levels_the_strip_swung_between()
    {
        // Measured: 29 at the bottom, 239 at the top, and every LED the same at any instant.
        var effect = new BreatheEffect();
        EffectSegment segment = Run(speed: 128, intensity: 128);

        double low = double.MaxValue;
        double high = 0;

        for (uint t = 0; t < 6000; t += FrameMs)
        {
            effect.Render(segment, t);
            double level = MeanBrightness(segment);
            low = Math.Min(low, level);
            high = Math.Max(high, level);
        }

        output.WriteLine($"simulated: low={low:F0} high={high:F0}");
        output.WriteLine("measured : low=29 high=239");

        Assert.InRange(low, 24, 36);
        Assert.InRange(high, 225, 255);
    }

    [Fact]
    public void Breathe_lights_the_whole_run_evenly_the_way_the_strip_did()
    {
        var effect = new BreatheEffect();
        EffectSegment segment = Run(speed: 128, intensity: 128);

        effect.Render(segment, 1500);

        // The strip measured a spread of exactly zero across all 308 LEDs.
        int brightest = segment.Pixels.Max(p => Math.Max(p.R, Math.Max(p.G, p.B)));
        int dimmest = segment.Pixels.Min(p => Math.Max(p.R, Math.Max(p.G, p.B)));

        output.WriteLine($"simulated spread: {brightest - dimmest}");
        Assert.Equal(0, brightest - dimmest);
    }

    // ------------------------------------------------------------------------------------ bpm

    [Fact]
    public void Bpm_repeats_along_the_run_as_often_as_the_strip_did()
    {
        // Measured at speed 64 on palette 254: brightness ramps then drops back every 25.6 LEDs,
        // steadily, across three separate frames of the capture.
        //
        // Read off the whole run, which matters. Brightness climbs by ten a LED and wraps at 256,
        // so where the first wrap falls depends on the phase - eyeballing the first two dozen LEDs
        // put it at 18 and made the port look wrong when it was not.
        var effect = new BpmEffect();
        EffectSegment segment = Run(speed: 64, intensity: 128, paletteId: 254);

        effect.Render(segment, 3000);

        int[] row = [.. segment.Pixels.Select(p => (int)Math.Max(p.R, Math.Max(p.G, p.B)))];

        // Where the ramp falls away, which is the sawtooth's edge.
        var drops = new List<int>();
        for (int i = 1; i < row.Length; i++)
        {
            if (row[i] < row[i - 1] - 40)
            {
                drops.Add(i);
            }
        }

        var gaps = drops.Zip(drops.Skip(1), (a, b) => b - a).ToList();
        double spacing = gaps.Count > 0 ? gaps.Average() : 0;

        output.WriteLine($"simulated: {drops.Count} drops, about every {spacing:F1} LEDs");
        output.WriteLine("measured : 12 drops, about every 25.6 LEDs");

        Assert.InRange(drops.Count, 10, 14);
        Assert.InRange(spacing, 23, 28);
    }

    [Fact]
    public void Bpm_stays_in_the_brightness_band_the_strip_stayed_in()
    {
        // Measured: mean brightness sat between 76 and 120, averaging 100.
        //
        // Once the crossfade out of the previous effect had finished. WLED blends between effects
        // over the best part of a second, and counting those frames stretched the measured range
        // to 43-143, which is the previous effect fading out rather than this one running.
        var effect = new BpmEffect();
        EffectSegment segment = Run(speed: 64, intensity: 128, paletteId: 254);

        double low = double.MaxValue;
        double high = 0;

        for (uint t = 0; t < 6000; t += FrameMs)
        {
            effect.Render(segment, t);
            double level = MeanBrightness(segment);
            low = Math.Min(low, level);
            high = Math.Max(high, level);
        }

        output.WriteLine($"simulated: low={low:F0} high={high:F0}");
        output.WriteLine("measured : low=76 high=120, mean 100");

        Assert.InRange(low, 60, 110);
        Assert.InRange(high, 100, 140);
    }

    // ----------------------------------------------------------------------------------- flow

    [Fact]
    public void Flow_divides_the_run_into_the_zones_the_strip_showed()
    {
        // Measured at intensity 128 on a 308 LED run: seams about every 4.2 LEDs, and every LED
        // lit. Zones come out as (intensity * (length / 6)) / 256, forced even.
        var effect = new FlowEffect();
        EffectSegment segment = Run(speed: 128, intensity: 128, paletteId: 254);

        effect.Render(segment, 3000);

        int[] row = [.. segment.Pixels.Select(p => (int)Math.Max(p.R, Math.Max(p.G, p.B)))];

        var seams = new List<int>();
        for (int i = 1; i < row.Length - 1; i++)
        {
            if (row[i] <= row[i - 1] && row[i] < row[i + 1])
            {
                seams.Add(i);
            }
        }

        var gaps = seams.Zip(seams.Skip(1), (a, b) => b - a).ToList();
        double spacing = gaps.Count > 0 ? gaps.Average() : 0;

        output.WriteLine($"simulated: {seams.Count} seams, about every {spacing:F1} LEDs");
        output.WriteLine("measured : 70 seams, about every 4.2 LEDs");

        Assert.InRange(seams.Count, 50, 90);
        Assert.InRange(spacing, 3.2, 5.5);
    }

    [Fact]
    public void Flow_leaves_no_led_dark_the_way_the_strip_did_not()
    {
        var effect = new FlowEffect();
        EffectSegment segment = Run(speed: 128, intensity: 128, paletteId: 254);

        effect.Render(segment, 3000);

        int dark = segment.Pixels.Count(p => p.R + p.G + p.B == 0);
        output.WriteLine($"simulated dark LEDs: {dark} (the strip had none)");

        Assert.Equal(0, dark);
    }
}
