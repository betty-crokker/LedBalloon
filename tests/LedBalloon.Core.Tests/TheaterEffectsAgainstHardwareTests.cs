using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Theater, Theater Rainbow, Chase 2 and Stream, captured on the south controller's 285 LED roofline
/// with the color slots set to red, blue and green.
/// <para>
/// The first three step rather than slide - the pattern moves a whole LED at a time on a tick - so
/// how often the picture changes is a direct read of the tick, and the strip gave 177 ms for all
/// three where <c>50 + (255 - speed)</c> predicts 177.
/// </para>
/// </summary>
public class TheaterEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor Background = new(0, 0, 255);

    /// <param name="Changes">Frames whose picture differs from the one before, which counts ticks.</param>
    private sealed record Marching(
        double Lit, int LitMost, int LitLeast, double Colors, int MostColors, int Changes, int Frames);

    private static Marching Measure(IWledEffect effect, uint milliseconds)
    {
        List<int> lit = [];
        List<int> colors = [];
        int changes = 0;
        RgbColor[]? previous = null;

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            lit.Add(frame.Count(p => p != Background));
            colors.Add(frame.Distinct().Count());

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                changes++;
            }

            previous = [.. frame];
        });

        return new Marching(
            lit.Average() / Strip.Roofline, lit.Max(), lit.Min(),
            colors.Average(), colors.Max(), changes, lit.Count);
    }

    /// <summary>
    /// One LED in every eleven, stepping along the run every 177 ms.
    /// <para>
    /// Intensity sets the spacing rather than a brightness: <c>3 + (128 &gt;&gt; 4)</c> is 11, so a
    /// 285 LED roofline carries 26 lit LEDs. The strip held 25 or 26 and nothing else, which is 9.1%
    /// of the run, and changed picture 79 times in fourteen seconds.
    /// </para>
    /// </summary>
    [Fact]
    public void Theater_lights_one_led_in_eleven_and_steps_every_177_ms()
    {
        Marching measured = Measure(new TheaterChaseEffect(), 14_000);

        output.WriteLine($"simulated: lit {measured.Lit:F3} ({measured.LitLeast}-{measured.LitMost}), " +
            $"{measured.MostColors} colors, {measured.Changes} steps, " +
            $"{14_000.0 / measured.Changes:F0} ms a step");
        output.WriteLine("measured : lit 0.091 (25-26), 2 colors, 79 steps, 177 ms a step");
        output.WriteLine("arithmetic: 3 + (128 >> 4) = 11, so 26 of 285 = 0.091; 50 + 127 = 177 ms");

        Assert.Equal(2, measured.MostColors);
        Assert.InRange(measured.Lit, 0.085, 0.098);
        Assert.InRange(measured.LitMost, 25, 27);
        Assert.InRange(14_000.0 / measured.Changes, 165, 195);
    }

    /// <summary>
    /// A block and a gap the same width, so half the run is lit - which is the same function with
    /// its spacing arithmetic turned into a block instead of a dot.
    /// <para>
    /// Width is <c>1 + (128 &gt;&gt; 4)</c> = 9 here and the pattern repeats over 18, so the strip
    /// held 141 to 144 lit - half of 285, give or take where the pattern falls at the ends.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_2_lights_a_block_and_leaves_a_gap_the_same_size()
    {
        Marching measured = Measure(new RunningColorEffect(), 14_000);

        output.WriteLine($"simulated: lit {measured.Lit:F3} ({measured.LitLeast}-{measured.LitMost}), " +
            $"{measured.Changes} steps, {14_000.0 / measured.Changes:F0} ms a step");
        output.WriteLine("measured : lit 0.500 (141-144), 79 steps, 177 ms a step");

        Assert.InRange(measured.Lit, 0.47, 0.53);
        Assert.InRange(measured.LitMost, 138, 148);
        Assert.InRange(14_000.0 / measured.Changes, 165, 195);
    }

    /// <summary>
    /// The same 26 lit LEDs and the same 177 ms step, with the lit color walking the wheel.
    /// <para>
    /// A step per tick rather than a step per frame, so unlike the rainbow chases this one keeps the
    /// same time whatever rate the controller manages. The strip split its 9.1% between hues -
    /// 3.5% reading red and 5.6% green - where plain Theater put all of it in red.
    /// </para>
    /// </summary>
    [Fact]
    public void Theater_Rainbow_keeps_the_spacing_and_walks_the_color()
    {
        Marching measured = Measure(new TheaterRainbowEffect(), 14_000);

        List<int> hues = [];

        Strip.Sample(new TheaterRainbowEffect(), Strip.Run(), 14_000, (frame, _) =>
            hues.Add(frame.Where(p => p != Background).Distinct().Count()));

        output.WriteLine($"simulated: lit {measured.Lit:F3} ({measured.LitLeast}-{measured.LitMost}), " +
            $"{measured.Changes} steps, at most {hues.Max()} lit color at a time");
        output.WriteLine("measured : lit 0.091 (25-26), 79 steps, 2 colors on the run in all");

        Assert.InRange(measured.Lit, 0.085, 0.098);
        Assert.InRange(14_000.0 / measured.Changes, 165, 195);

        // One color lit at a time, which is what says the wheel walks the whole pattern rather than
        // coloring each LED separately.
        Assert.Equal(1, hues.Max());
    }

    /// <summary>
    /// Bands of eight LEDs, each a different color off the wheel, drifting along the run.
    /// <para>
    /// Intensity is the band width - <c>((255 - 128) &gt;&gt; 4) + 1</c> = 8 - so a 285 LED roofline
    /// carries 35.6 of them, and counting distinct colors per frame on the strip gave 35.5 with a
    /// maximum of 36. A new band is introduced every 406 ms, and the strip changed picture 35 times
    /// in fourteen seconds, which is 400.
    /// </para>
    /// <para>
    /// The colors are generated from a seed carried between frames rather than drawn fresh, which is
    /// what lets the bands keep their colors while they drift. A preview cannot match the strip's
    /// seed, so what is checked is the band structure rather than the colors themselves.
    /// </para>
    /// </summary>
    [Fact]
    public void Stream_carries_thirty_six_bands_of_eight_along_the_run()
    {
        Marching measured = Measure(new RunningRandomEffect(), 14_000);

        output.WriteLine($"simulated: {measured.Colors:F2} colors a frame, {measured.MostColors} at most, " +
            $"lit {measured.Lit:F3}, {measured.Changes} changes, " +
            $"{14_000.0 / measured.Changes:F0} ms between");
        output.WriteLine("measured : 35.51 colors a frame, 36 at most, lit 1.000, 35 changes, 400 ms between");
        output.WriteLine($"arithmetic: 285 / 8 = {285 / 8.0:F1} bands; 25 + 3 * 127 = 406 ms");

        Assert.Equal(1.0, measured.Lit, 2);
        Assert.InRange(measured.Colors, 33, 37);
        Assert.InRange(measured.MostColors, 34, 38);
        Assert.InRange(14_000.0 / measured.Changes, 360, 450);
    }
}
