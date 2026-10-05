using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The four Noise effects, captured on the south controller's 285 LED roofline on palette 11,
/// "Rainbow", with both sliders at the middle.
/// <para>
/// A palette rather than the color slots, because these effects are nothing but a walk through a
/// noise field read through a palette - on palette Default they would all be one flat color and
/// there would be nothing to measure.
/// </para>
/// <para>
/// Noise 4 is checked exactly elsewhere, since at speed zero it is a pure function of position. These
/// four are checked on what separates them from each other, which the strip makes plain: two of them
/// use the noise as a brightness as well as a color and come out half as bright, and Noise 4 skips
/// the tripled sine the other three pass the noise through, so it walks the palette smoothly and has
/// four times as many bands.
/// </para>
/// </summary>
[Collection(EngineCollection.Name)]
public class Noise16EffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor[] Rainbow =
    [
        new(255, 0, 0), new(213, 42, 0), new(171, 85, 0), new(171, 127, 0),
        new(171, 171, 0), new(86, 213, 0), new(0, 255, 0), new(0, 213, 42),
        new(0, 171, 85), new(0, 86, 170), new(0, 0, 255), new(42, 0, 213),
        new(85, 0, 171), new(127, 0, 129), new(171, 0, 85), new(213, 0, 43),
    ];

    private static EffectSegment Run() => new(Strip.Roofline)
    {
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 11,
        Palette = new WledPalette
        {
            Stops = [.. Rainbow.Select((c, i) => new PaletteStop((byte)(i * 16), c))],
        },
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    private sealed record Field(double Brightness, double Deviation, double Bands, double Colors);

    private static Field Measure(IWledEffect effect)
    {
        List<double> bright = [], spread = [], colors = [];
        List<int> bands = [];

        Strip.Sample(effect, Run(), 14_000, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            bright.Add(shape.Mean);
            spread.Add(shape.Deviation);
            bands.Add(shape.Bands);
            colors.Add(frame.DistinctBy(p => ((p.R >> 4) << 8) | ((p.G >> 4) << 4) | (p.B >> 4)).Count());
        });

        return new Field(bright.Average(), spread.Average(), bands.Average(), colors.Average());
    }

    /// <summary>
    /// Noise 1 and Noise 4 read only the color from the noise, so the run stays near full brightness.
    /// Noise 2 and Noise 3 read the brightness from it as well, which halves them.
    /// <para>
    /// The strip: 199.7 and 192.9 against 100.6 and 100.8. That factor of two is the whole of the
    /// difference between reading a noise value once and reading it twice, and it is the thing worth
    /// pinning - it would be easy to port all four the same way and have them all look right on their
    /// own.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Noise 1", 199.73, 33.45)]
    [InlineData("Noise 2", 100.55, 25.10)]
    [InlineData("Noise 3", 100.79, 28.59)]
    [InlineData("Noise 4", 192.86, 33.14)]
    public void The_noise_effects_differ_in_whether_the_noise_dims_them(
        string name, double brightness, double deviation)
    {
        Field measured = Measure(EffectLibrary.Find(name)!);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.Bands:F2} bands, {measured.Colors:F1} colors");
        output.WriteLine($"measured : brightness {brightness:F2}, spread {deviation:F2}");

        Assert.InRange(measured.Brightness, brightness * 0.8, brightness * 1.2);
        Assert.InRange(measured.Deviation, deviation * 0.6, deviation * 1.6);
    }

    /// <summary>
    /// Three of the four pass the noise through <c>sin8(noise * 3)</c> before using it as a palette
    /// index, and Noise 4 does not.
    /// <para>
    /// Tripling before a sine is what turns a smooth field into ribbons: the sine wraps three times
    /// over the noise's range, so neighboring noise values can land at opposite ends of the palette.
    /// Noise 4 walks the palette smoothly instead, and the strip counted 93.6 bands along the run for
    /// it against 11.1, 23.2 and 22.2 for the other three.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_Noise_4_walks_the_palette_without_a_sine()
    {
        Field one = Measure(EffectLibrary.Find("Noise 1")!);
        Field two = Measure(EffectLibrary.Find("Noise 2")!);
        Field three = Measure(EffectLibrary.Find("Noise 3")!);
        Field four = Measure(EffectLibrary.Find("Noise 4")!);

        output.WriteLine($"simulated bands: {one.Bands:F1}, {two.Bands:F1}, {three.Bands:F1}, {four.Bands:F1}");
        output.WriteLine("measured bands : 11.1, 23.2, 22.2, 93.6");

        Assert.InRange(four.Bands, 70, 115);
        Assert.InRange(one.Bands, 6, 18);
        Assert.InRange(two.Bands, 16, 32);
        Assert.InRange(three.Bands, 15, 31);

        // Smooth against ribboned, which is the claim.
        Assert.True(four.Bands > three.Bands * 2.5,
            $"Noise 4 should have far more bands: {four.Bands:F1} against {three.Bands:F1}");
    }

    /// <summary>
    /// Noise 4 stands still at speed zero and moves at any other speed, which is what says it reads
    /// the clock rather than accumulating - the only one of the four that does.
    /// </summary>
    [Fact]
    public void Noise_4_is_the_only_one_that_stands_still_at_speed_zero()
    {
        foreach (IWledEffect effect in (IWledEffect[])[
            EffectLibrary.Find("Noise 1")!, EffectLibrary.Find("Noise 2")!,
            EffectLibrary.Find("Noise 3")!, EffectLibrary.Find("Noise 4")!])
        {
            EffectSegment segment = Run();
            segment.Speed = 0;

            List<RgbColor[]> seen = [];
            Strip.Sample(effect, segment, 3_000, (frame, _) => seen.Add([.. frame]));

            bool still = seen.All(f => f.SequenceEqual(seen[0]));

            output.WriteLine($"{effect.Name} at speed zero: {(still ? "still" : "moving")}");

            Assert.Equal(effect.Name == "Noise 4", still);
        }
    }
}
