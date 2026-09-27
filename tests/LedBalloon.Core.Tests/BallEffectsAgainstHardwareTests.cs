using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Bouncing Balls, Railway, Washing Machine and Blends, captured on the south controller's 285 LED
/// roofline on palette 11, "Rainbow".
/// <para>
/// A palette rather than the color slots, because three of the four insist on the gradient: they pass
/// a color slot that does not exist, which is WLED's way of saying "the palette, whatever it is set
/// to". On palette Default there would be nothing for them to read.
/// </para>
/// <para>
/// These were the first captures taken with <c>tools/wled.cs</c> rather than through a browser pane.
/// </para>
/// </summary>
public class BallEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private sealed record Look(
        double Brightness, double Deviation, double Colors, int MostColors,
        double Lit, double Peaks, int Changes, int Frames);

    private static WledPalette Rainbow() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(255, 0, 0)), new PaletteStop(16, new RgbColor(213, 42, 0)),
            new PaletteStop(32, new RgbColor(171, 85, 0)), new PaletteStop(48, new RgbColor(171, 127, 0)),
            new PaletteStop(64, new RgbColor(171, 171, 0)), new PaletteStop(80, new RgbColor(86, 213, 0)),
            new PaletteStop(96, new RgbColor(0, 255, 0)), new PaletteStop(112, new RgbColor(0, 213, 42)),
            new PaletteStop(128, new RgbColor(0, 171, 85)), new PaletteStop(144, new RgbColor(0, 86, 170)),
            new PaletteStop(160, new RgbColor(0, 0, 255)), new PaletteStop(176, new RgbColor(42, 0, 213)),
            new PaletteStop(192, new RgbColor(85, 0, 171)), new PaletteStop(208, new RgbColor(127, 0, 129)),
            new PaletteStop(224, new RgbColor(171, 0, 85)), new PaletteStop(240, new RgbColor(213, 0, 43)),
        ],
    };

    private static Look Measure(IWledEffect effect, uint milliseconds)
    {
        EffectSegment segment = Strip.Run();
        segment.PaletteId = 11;
        segment.Palette = Rainbow();

        List<double> bright = [], spread = [], colors = [], lit = [], peaks = [];
        int most = 0, changes = 0;
        RgbColor[]? previous = null;

        Strip.Sample(effect, segment, milliseconds, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            bright.Add(shape.Mean);
            spread.Add(shape.Deviation);
            peaks.Add(shape.Bands);
            lit.Add((double)frame.Count(p => Math.Max(p.R, Math.Max(p.G, p.B)) > 8) / frame.Length);

            int distinct = frame.Distinct().Count();
            colors.Add(distinct);
            most = Math.Max(most, distinct);

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                changes++;
            }

            previous = [.. frame];
        });

        return new Look(
            bright.Average(), spread.Average(), colors.Average(), most,
            lit.Average(), peaks.Average(), changes, bright.Count);
    }

    /// <summary>
    /// Eight balls, and eight LEDs lit - the only effect here that is a physical simulation rather
    /// than a waveform, and the only one in floating point.
    /// <para>
    /// Intensity gives <c>(128 * 15) / 255 + 1</c> = 8 balls, and the strip had 2.6% of the run lit,
    /// which is 7.4 LEDs: eight balls with two occasionally landing on the same LED. Nine colors at
    /// most - eight balls off the wheel plus the black they sit on - and a mean brightness of 5, because
    /// eight lit LEDs in 285 is almost nothing.
    /// </para>
    /// <para>
    /// A simulation cannot be matched frame for frame here: the relaunch speed of a stopped ball is a
    /// random draw, so the balls fall out of step with the strip's within seconds. What is checked is
    /// the count, which is not random, and the spread of heights.
    /// </para>
    /// </summary>
    [Fact]
    public void Bouncing_Balls_lights_one_led_per_ball_and_nothing_else()
    {
        Look measured = Measure(new BouncingBallsEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.Colors:F1} colors (max {measured.MostColors}), lit {measured.Lit:F3}, " +
            $"{measured.Peaks:F2} peaks");
        output.WriteLine("measured : brightness 5.27, spread 32.86, 8.4 colors (max 9), lit 0.026, 6.25 peaks");
        output.WriteLine($"arithmetic: (128 * 15) / 255 + 1 = 8 balls, which is {8 / 285.0:F3} of the run");

        // Eight balls, so eight LEDs give or take two landing together.
        Assert.InRange(measured.Lit * Strip.Roofline, 5.5, 9.5);

        // Eight ball colors and the black behind them.
        Assert.InRange(measured.MostColors, 7, 10);
        Assert.InRange(measured.Brightness, 3.5, 8);
    }

    /// <summary>
    /// Railway crossfades alternate LEDs in opposite directions, so the run is always exactly two
    /// colors and every LED is a local maximum or minimum.
    /// <para>
    /// The strip held exactly two colors and 140.7 peaks on a 285 LED run, which is every other LED,
    /// and every LED lit. The two are opposite ends of the palette read forwards and backwards, so
    /// they are never the same color except at the crossing point.
    /// </para>
    /// </summary>
    [Fact]
    public void Railway_alternates_two_opposite_palette_positions()
    {
        Look measured = Measure(new RailwayEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.MostColors} colors at most, {measured.Peaks:F2} peaks, lit {measured.Lit:F3}");
        output.WriteLine("measured : brightness 196.38, spread 20.58, 2 colors at most, 140.68 peaks, lit 1.000");
        output.WriteLine($"arithmetic: every other LED, so {(Strip.Roofline - 1) / 2.0:F1} peaks");

        Assert.Equal(2, measured.MostColors);
        Assert.InRange(measured.Peaks, 134, 144);
        Assert.Equal(1.0, measured.Lit, 2);
        Assert.InRange(measured.Brightness, 178, 215);
    }

    /// <summary>
    /// Washing Machine rolls waves one way, pauses, and rolls them back.
    /// <para>
    /// Six waves along the run at these settings, but the strip counted 35.6 peaks in brightness and
    /// not 6 - because the palette's own structure is in there too. A rainbow's brightest channel dips
    /// between its primaries, so each sine cycle crosses several of those dips. Reading brightness
    /// counts the palette as well as the wave, and here that is the honest thing to compare since it
    /// is what the strip shows.
    /// </para>
    /// </summary>
    [Fact]
    public void Washing_Machine_rolls_waves_forward_and_back()
    {
        Look measured = Measure(new WashingMachineEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.Colors:F1} colors, {measured.Peaks:F2} peaks");
        output.WriteLine("measured : brightness 198.51, spread 34.11, 149.8 colors, 35.62 peaks");

        Assert.InRange(measured.Brightness, 180, 218);
        Assert.InRange(measured.Deviation, 26, 42);
        Assert.InRange(measured.Peaks, 28, 44);
        Assert.InRange(measured.Colors, 120, 180);
    }

    /// <summary>
    /// Blends creeps every LED toward a target rather than drawing it, so what is on the run is never
    /// quite the palette's own colors.
    /// <para>
    /// The strip carried 161 distinct colors on a 285 LED run - fewer than the run is long, because the
    /// buffer it keeps is capped at 255 and read back round - and 96.3 peaks in brightness.
    /// </para>
    /// </summary>
    [Fact]
    public void Blends_creeps_toward_its_targets_rather_than_drawing_them()
    {
        Look measured = Measure(new BlendsEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.Colors:F1} colors (max {measured.MostColors}), {measured.Peaks:F2} peaks");
        output.WriteLine("measured : brightness 188.09, spread 34.25, 161.0 colors (max 161), 96.30 peaks");

        Assert.InRange(measured.Brightness, 170, 208);
        Assert.InRange(measured.Deviation, 26, 42);
        Assert.InRange(measured.Peaks, 80, 112);
        Assert.InRange(measured.Colors, 135, 190);
    }
}
