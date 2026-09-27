using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Ripple Rainbow, Multi Comet, Meteor, Meteor Smooth, Perlin Move, Wavesins, Sine and Flow Stripe,
/// captured on the south controller's 285 LED roofline with the color slots set to red, blue and
/// green and the custom sliders left where WLED leaves them.
/// </summary>
public class WaveWalkEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <param name="Colors">Distinct exact colors on the run, averaged - a shape measure in itself.</param>
    /// <param name="Peaks">Local maxima in brightness along the run.</param>
    private sealed record Look(
        double Brightness, double Deviation, double Colors, int MostColors,
        double Lit, double Peaks, int Changes, int Frames);

    /// <summary>Palette 11, "Rainbow", for the effects that read a gradient rather than a color slot.</summary>
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

    private static Look Measure(IWledEffect effect, uint milliseconds, bool onPalette = false)
    {
        List<double> bright = [], spread = [], colors = [], lit = [], peaks = [];
        int most = 0, changes = 0;
        RgbColor[]? previous = null;

        EffectSegment segment = Strip.Run();

        if (onPalette)
        {
            segment.PaletteId = 11;
            segment.Palette = Rainbow();
        }

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
    /// Flow Stripe is meant to roll hues along the run and in this release does not: one LED is out of
    /// step with the other 284, and that is the whole of its spatial structure.
    /// <para>
    /// The position term is <c>(abs(i - hl) / hl) * 127</c> with an integer division, and
    /// <c>abs(i - hl)</c> only reaches <c>hl</c> at the very first LED. So the run is one color with a
    /// single odd pixel in it - the strip showed at most two colors, exactly one color on 22% of
    /// frames, a spread of 0.53 across the run and not one local maximum in brightness anywhere.
    /// </para>
    /// <para>
    /// Reproduced rather than corrected. Those numbers are the evidence that it is reproduced: a
    /// working gradient could not have any of them.
    /// </para>
    /// </summary>
    [Fact]
    public void Flow_Stripe_lights_the_whole_run_one_color_bar_a_single_led()
    {
        Look measured = Measure(new FlowStripeEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.MostColors} colors at most, {measured.Peaks:F2} peaks");
        output.WriteLine("measured : brightness 193.07, spread 0.53, 2 colors at most, 0.00 peaks");

        Assert.Equal(2, measured.MostColors);
        Assert.InRange(measured.Deviation, 0, 4);
        Assert.Equal(0, measured.Peaks, 2);
        Assert.InRange(measured.Brightness, 175, 212);
    }

    /// <summary>
    /// Wavesins draws exactly two colors on this roofline, because of how its sliders happen to fall.
    /// <para>
    /// Brightness is <c>sin8(now / 4 + i * intensity)</c>, and intensity 128 makes
    /// <c>i * 128</c> alternate between 0 and 128 as a byte - so odd and even LEDs get two values half
    /// a cycle apart and nothing in between. The strip held exactly two colors, 141.5 local maxima on
    /// a 285 LED run, which is every other LED, and a mean of 128 because the two halves of a sine
    /// add to 255.
    /// </para>
    /// <para>
    /// The spread of 81 is what says the two are far apart rather than near each other, and the whole
    /// thing is a reminder that a slider's <em>value</em> can change an effect's character and not
    /// just its degree.
    /// </para>
    /// </summary>
    [Fact]
    public void Wavesins_alternates_between_two_colors_on_every_other_led()
    {
        Look measured = Measure(new WavesinsEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.MostColors} colors at most, {measured.Peaks:F2} peaks, lit {measured.Lit:F3}");
        output.WriteLine("measured : brightness 127.97, spread 81.09, 2 colors at most, 141.52 peaks, lit 0.885");
        output.WriteLine($"arithmetic: every other LED, so {(Strip.Roofline - 1) / 2.0:F1} peaks");

        Assert.Equal(2, measured.MostColors);
        Assert.InRange(measured.Peaks, 135, 145);
        Assert.InRange(measured.Brightness, 115, 142);
        Assert.InRange(measured.Deviation, 70, 92);
    }

    /// <summary>
    /// Sine slides a cubic wave along the run while the palette walks on its own clock.
    /// </summary>
    [Fact]
    public void Sine_slides_a_cubic_wave_along_the_run()
    {
        Look measured = Measure(new SineEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.MostColors} colors, {measured.Peaks:F2} peaks");
        output.WriteLine("measured : brightness 206.18, spread 39.72, 8 colors, 70.76 peaks");
        output.WriteLine("arithmetic: intensity 128 gives a frequency of 32, so 285 * 32 / 256 = 35.6 waves");

        Assert.InRange(measured.Brightness, 185, 228);
        Assert.InRange(measured.Deviation, 30, 50);
        Assert.InRange(measured.Peaks, 60, 82);
    }

    /// <summary>
    /// Perlin Move scatters a few points whose positions come from noise rather than a sine, on a
    /// slowly fading background.
    /// <para>
    /// Fewer points on screen than the slider asks for, and that is the effect rather than a shortfall:
    /// the noise is mapped from the band 50 to 192 of 256 onto the run without clamping, so a point
    /// whose noise strays outside that band is simply not drawn. The strip carried 49 distinct colors
    /// and 8.3 local maxima where nine points were asked for.
    /// </para>
    /// </summary>
    [Fact]
    public void Perlin_Move_draws_fewer_points_than_the_slider_asks_for()
    {
        Look measured = Measure(new PerlinMoveEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, {measured.Colors:F1} colors " +
            $"(max {measured.MostColors}), {measured.Peaks:F2} peaks, lit {measured.Lit:F3}");
        output.WriteLine("measured : brightness 240.59, 49.0 colors (max 65), 8.30 peaks, lit 1.000");
        output.WriteLine("the slider asks for 128 / 16 + 1 = 9 points");

        Assert.InRange(measured.Peaks, 5, 13);
        Assert.InRange(measured.Colors, 34, 66);
        Assert.InRange(measured.Brightness, 215, 255);
    }

    /// <summary>
    /// Multi Comet runs eight points at once and moves on its own tick, not per frame - so the trails
    /// are the same length however fast the controller is drawing. The strip changed picture every
    /// 137 ms against the 137 the cycle formula gives.
    /// <para>
    /// Its <em>appearance</em> depends on what ran before it, which took two captures to work out. The
    /// eight comet positions live in the segment's scratch buffer, and WLED reuses that buffer between
    /// effects without clearing it when the size happens to match - so the comets start wherever the
    /// previous effect left its own data. Switched to from the middle of a capture run it began with
    /// eight scattered comets and showed 15.9 peaks along the run; switched to from Solid it began with
    /// all eight at zero, which is what a clean start looks like, and showed 1.0.
    /// </para>
    /// <para>
    /// This simulation always starts clean, so it is compared against the clean measurement - 1.00
    /// peaks and 56.6 colors - and the scattered one is recorded here as the reason the first reading
    /// disagreed. Reproducing it would mean modelling whatever effect happened to run before, which is
    /// not a property of this effect.
    /// </para>
    /// </summary>
    [Fact]
    public void Multi_Comet_starts_all_eight_comets_together_from_a_clean_slate()
    {
        Look measured = Measure(new MultiCometEffect(), 12_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, {measured.Colors:F1} colors, " +
            $"{measured.Peaks:F2} peaks, a change every {12_000.0 / measured.Changes:F0} ms");
        output.WriteLine("measured after Solid: brightness 180.35, 56.6 colors, 1.00 peaks");
        output.WriteLine("measured mid-run    : brightness 181.73, 100.9 colors, 15.94 peaks");
        output.WriteLine("arithmetic: 10 + (255 - 128) = 137 ms a tick");

        Assert.InRange(12_000.0 / measured.Changes, 115, 165);
        Assert.InRange(measured.Brightness, 165, 198);

        // All eight together, so the run is one ramp rather than eight.
        Assert.InRange(measured.Peaks, 0, 3);
        Assert.InRange(measured.Colors, 40, 80);
    }

    /// <summary>
    /// The two Meteors differ in how the trail dies, and it changes the picture completely.
    /// <para>
    /// Meteor scales each LED's temperature down by a random amount, which knocks most of the run dark
    /// - the strip had 17.4% of it lit and a mean brightness of 23 with a spread of 65, which is a
    /// bright head on nothing. Meteor Smooth lets the temperature wander by a few units instead, up as
    /// well as down, so the whole run stays lit: 100% lit, a mean of 184, and 68 local maxima where
    /// the decaying one has 20.
    /// </para>
    /// <para>
    /// Same effect name, same head size, same slider - and one is a comet on black while the other is
    /// a shimmer over the whole roofline.
    /// </para>
    /// <para>
    /// Meteor Smooth is measured on a palette rather than on the color slots, because without the
    /// Gradient option it reads the palette <em>by temperature</em> through an out-of-range color slot -
    /// WLED's way of saying "use the gradient whatever the palette is set to". On palette Default there
    /// is no gradient here to read, and the first capture of it was taken there and could not be
    /// matched for that reason rather than for any fault in the port.
    /// </para>
    /// </summary>
    [Fact]
    public void Meteor_decays_onto_black_where_Meteor_Smooth_shimmers()
    {
        Look decaying = Measure(new MeteorEffect(), 14_000);
        Look wandering = Measure(new MeteorSmoothEffect(), 12_000, onPalette: true);

        output.WriteLine($"simulated decaying  : brightness {decaying.Brightness:F2}, spread {decaying.Deviation:F2}, " +
            $"lit {decaying.Lit:F3}, {decaying.Peaks:F2} peaks");
        output.WriteLine("measured decaying   : brightness 23.04, spread 65.05, lit 0.174, 19.87 peaks");
        output.WriteLine($"simulated wandering : brightness {wandering.Brightness:F2}, spread {wandering.Deviation:F2}, " +
            $"lit {wandering.Lit:F3}, {wandering.Peaks:F2} peaks");
        output.WriteLine("measured wandering  : brightness 220.41, spread 37.72, lit 1.000, 70.73 peaks");

        // Mostly dark against entirely lit, which is the difference.
        Assert.InRange(decaying.Lit, 0.08, 0.32);
        Assert.InRange(wandering.Lit, 0.9, 1.0);

        Assert.InRange(decaying.Brightness, 12, 45);
        Assert.InRange(wandering.Brightness, 195, 245);

        // A trail that breaks up against one that shimmers: 20 peaks against 70.
        Assert.InRange(decaying.Peaks, 14, 27);
        Assert.InRange(wandering.Peaks, 58, 84);

        // And the decaying one is the higher contrast of the two despite being darker.
        Assert.True(decaying.Deviation > wandering.Deviation,
            "a head on black has more contrast than a shimmer");
    }

    /// <summary>
    /// Ripple Rainbow is the same rings as Ripple over a background that wanders round the wheel, and
    /// the background is knocked down to a twentieth so the rings show over it.
    /// <para>
    /// The strip's mean brightness was 28 - dark, which is the dimmed background - with a spread of 33
    /// from the rings on top of it, and 53 distinct colors as rings at different ages overlap.
    /// </para>
    /// </summary>
    [Fact]
    public void Ripple_Rainbow_puts_its_rings_over_a_heavily_dimmed_wandering_background()
    {
        Look measured = Measure(new RippleRainbowEffect(), 14_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.Colors:F1} colors (max {measured.MostColors}), {measured.Peaks:F2} peaks");
        output.WriteLine("measured : brightness 28.10, spread 33.14, 53.5 colors (max 86), 21.08 peaks");

        // Dark background, and the rings are what stands out from it.
        Assert.InRange(measured.Brightness, 12, 48);
        Assert.True(measured.Deviation > measured.Brightness,
            "the rings should stand out further than the background is bright");

        Assert.InRange(measured.Colors, 30, 80);
    }
}
