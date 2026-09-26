using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Spots, Spots Fade and Two Dots, captured on the south controller's 285 LED roofline with the
/// color slots set to red, blue and green.
/// </summary>
public class SpotEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor Background = new(0, 0, 255);

    private static (double Mean, double Most, double Least, int Bands, int MostFills) Measure(
        IWledEffect effect, uint milliseconds)
    {
        List<double> lit = [];
        List<int> bands = [];
        int most = 0;

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            lit.Add((double)frame.Count(p => p != Background) / frame.Length);
            bands.Add(Strip.Profile(frame, p => p.R).Bands);
            most = Math.Max(most, Strip.Fills(frame));
        });

        return (lit.Average(), lit.Max(), lit.Min(), (int)Math.Round(bands.Average()), most);
    }

    /// <summary>
    /// Thirty-six pools of light, each lighting four LEDs of the seven in its zone.
    /// <para>
    /// Every number here follows from the arithmetic and the strip matched all of them. Intensity
    /// gives <c>1 + ((128 * (285 >> 2)) >> 8)</c> = 36 zones, so a zone is 7 LEDs; a triangle wave
    /// across those seven clears the threshold at four of them; 36 times 4 is 144 of 285, which is
    /// 50.5%. The strip held 50.5%, and held it steady - this one does not move at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Spots_lights_thirty_six_pools_and_holds_them_still()
    {
        (double mean, double most, double least, int bands, int fills) =
            Measure(new SpotsEffect(), 12_000);

        output.WriteLine($"simulated: lit {mean:F3} ({least:F3}-{most:F3}), {bands} pools, {fills} fills");
        output.WriteLine("measured : lit 0.505 (0.505-0.505), 36 pools, 3 fills");
        output.WriteLine("arithmetic: 36 zones of 7, 4 of each lit, so 144 of 285 = 0.505");

        Assert.Equal(36, bands);
        Assert.InRange(mean, 0.48, 0.53);

        // Fixed, not breathing: that is the whole difference from Spots Fade.
        Assert.Equal(most, least, 3);
    }

    /// <summary>
    /// The same thirty-six pools breathing in and out together.
    /// <para>
    /// Same zones, same spacing, and the one thing that changes is how much of each zone is lit:
    /// the strip swung between 25.3% and 75.8% of the run where Spots sat flat at 50.5%. Driven by
    /// a triangle wave scaled to three quarters, so the pools narrow almost to nothing without ever
    /// going out.
    /// </para>
    /// </summary>
    [Fact]
    public void Spots_Fade_breathes_the_same_pools_in_and_out()
    {
        (double mean, double most, double least, int bands, _) =
            Measure(new SpotsFadeEffect(), 14_000);

        output.WriteLine($"simulated: lit {mean:F3} ({least:F3}-{most:F3}), {bands} pools");
        output.WriteLine("measured : lit 0.555 (0.253-0.758), 36 pools");

        Assert.Equal(36, bands);
        Assert.InRange(mean, 0.50, 0.62);

        // Swinging over most of the range rather than sitting at one size.
        Assert.InRange(least, 0.15, 0.32);
        Assert.InRange(most, 0.68, 0.85);
    }

    /// <summary>
    /// Two dots half the run apart, each 71 LEDs wide, on the third color slot.
    /// <para>
    /// The strip held 24.9% red and 24.9% blue against 50.2% green, which is 71, 71 and 143. The
    /// background being the <em>third</em> slot rather than the second is worth pinning: it is the
    /// one effect so far that uses the slots that way round, and getting it wrong would leave the
    /// dots invisible against their own color.
    /// </para>
    /// </summary>
    [Fact]
    public void Two_Dots_runs_two_bands_of_seventy_one_on_the_third_color_slot()
    {
        List<double> red = [], blue = [], green = [];
        int most = 0;

        Strip.Sample(new TwoDotsEffect(), Strip.Run(), 14_000, (frame, _) =>
        {
            red.Add((double)frame.Count(p => Strip.Dominant(p) == 0) / frame.Length);
            green.Add((double)frame.Count(p => Strip.Dominant(p) == 1) / frame.Length);
            blue.Add((double)frame.Count(p => Strip.Dominant(p) == 2) / frame.Length);
            most = Math.Max(most, Strip.Fills(frame));
        });

        output.WriteLine($"simulated: red {red.Average():F3} blue {blue.Average():F3} " +
            $"green {green.Average():F3}, {most} fills");
        output.WriteLine("measured : red 0.249 blue 0.249 green 0.502, 3 fills");
        output.WriteLine($"arithmetic: (285 * 129) >> 9 = 71 of 285 = {71 / 285.0:F3}");

        Assert.Equal(3, most);
        Assert.InRange(red.Average(), 0.23, 0.27);
        Assert.InRange(blue.Average(), 0.23, 0.27);
        Assert.InRange(green.Average(), 0.47, 0.53);
    }
}
