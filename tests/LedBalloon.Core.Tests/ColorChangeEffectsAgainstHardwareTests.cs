using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The effects that change the whole run's color rather than drawing a pattern on it: Fade, Blink
/// Rainbow, Strobe, Strobe Rainbow, Random Colors, Dynamic, Dynamic Smooth and Colorful. Captured on
/// the south controller's 285 LED roofline with the color slots set to red, blue and green.
/// </summary>
[Collection(EngineCollection.Name)]
public class ColorChangeEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <param name="Colors">Distinct colors on the run at once - one means the whole run agrees.</param>
    /// <param name="Changes">Sampled frames that differ from the one before, which times the effect.</param>
    private sealed record Wash(
        double Brightness, double Deviation, double Colors, int MostColors, int Changes, int Frames);

    private static Wash Measure(IWledEffect effect, byte speed, uint milliseconds)
    {
        List<double> bright = [], spread = [], colors = [];
        int most = 0, changes = 0;
        RgbColor[]? previous = null;

        Strip.Sample(effect, Strip.Run(speed), milliseconds, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            bright.Add(shape.Mean);
            spread.Add(shape.Deviation);

            int distinct = frame.Distinct().Count();
            colors.Add(distinct);
            most = Math.Max(most, distinct);

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                changes++;
            }

            previous = [.. frame];
        });

        return new Wash(
            bright.Average(), spread.Average(), colors.Average(), most, changes, bright.Count);
    }

    /// <summary>
    /// Fade washes the whole run between two colors on a triangle, so it is always moving and always
    /// one color.
    /// <para>
    /// A triangle rather than a sine is the difference from Breathe: the level is uniform over its
    /// range rather than lingering at the ends, which puts the average brightness at
    /// <c>255 - 255 / 4</c> once the blend is taken into account. The strip gave 189.6 against that
    /// arithmetic's 191.3.
    /// </para>
    /// </summary>
    [Fact]
    public void Fade_washes_the_whole_run_on_a_triangle()
    {
        Wash measured = Measure(EffectLibrary.Find("Fade")!, 128, 14_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, spread {measured.Deviation:F2}, " +
            $"{measured.MostColors} color at most, changing on {measured.Changes} of {measured.Frames} frames");
        output.WriteLine("measured : brightness 189.59, spread 0.00, 1 color at most, changing on 223 of 224");

        Assert.Equal(1, measured.MostColors);
        Assert.Equal(0, measured.Deviation, 3);
        Assert.InRange(measured.Brightness, 178, 202);

        // Never still: a triangle has no flat parts.
        Assert.True(measured.Changes > measured.Frames - 5, "Fade should be moving in every frame");
    }

    /// <summary>
    /// A strobe is one frame of light per cycle, and that is a measurement the live preview can only
    /// just make: at sixteen samples a second it takes half a minute to count enough of them.
    /// <para>
    /// At speed 245 the cycle is 204 ms and the lit part is one frame of 9, which is 4.6% of the time.
    /// The strip gave 5.1% over 514 samples for Strobe and 4.3% over 423 for Strobe Rainbow. Blink at
    /// the same speed sits at 49.8%, which is what the duty cycle control does and a strobe has none
    /// of.
    /// </para>
    /// <para>
    /// This is also the third independent reading of WLED's <c>FRAMETIME</c> being 2 ms rather than the
    /// frame interval: at 23 ms the lit share would be three frames of 246, or 9.3%.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Strobe", 0.0506)]
    [InlineData("Strobe Rainbow", 0.0426)]
    public void A_strobe_lights_for_one_frame_a_cycle(string name, double measured)
    {
        IWledEffect effect = EffectLibrary.Find(name)!;
        var background = new RgbColor(0, 0, 255);

        List<bool> lit = [];

        Strip.Sample(effect, Strip.Run(245), 30_000, (frame, _) => lit.Add(frame[0] != background));

        double share = (double)lit.Count(x => x) / lit.Count;

        output.WriteLine($"simulated: lit on {share:F4} of {lit.Count} frames");
        output.WriteLine($"measured : {measured:F4}");
        output.WriteLine("arithmetic: one frame of 9 ms in a 204 ms cycle is 0.046; " +
            "with the frame interval instead it would be 0.093");

        Assert.InRange(share, 0.02, 0.085);
    }

    /// <summary>
    /// Blink Rainbow keeps the blink and takes its lit color off the wheel a step per frame, so the
    /// flashes are never the same color twice running.
    /// <para>
    /// Brighter than a plain blink would be, and for a reason worth stating: a wheel color has its two
    /// lit channels adding to 255, so its brightest channel averages 191 rather than sitting at 255.
    /// Half the cycle is the secondary color at full, which puts the average at 223. The strip gave
    /// 226.6.
    /// </para>
    /// </summary>
    [Fact]
    public void Blink_Rainbow_takes_a_new_color_off_the_wheel_each_frame()
    {
        Wash measured = Measure(EffectLibrary.Find("Blink Rainbow")!, 128, 14_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, " +
            $"{measured.MostColors} color at most, a change every " +
            $"{14_000.0 / measured.Changes:F0} ms");
        output.WriteLine("measured : brightness 226.64, 1 color at most, a change every 116 ms");

        // One color across the run, whatever that color is.
        Assert.Equal(1, measured.MostColors);
        Assert.InRange(measured.Brightness, 205, 245);
    }

    /// <summary>
    /// Random Colors crossfades the whole run from one wheel color to the next, and intensity is how
    /// much of each cycle is spent on the way rather than how far it goes.
    /// </summary>
    [Fact]
    public void Random_Colors_crossfades_the_whole_run()
    {
        Wash measured = Measure(EffectLibrary.Find("Random Colors")!, 128, 16_000);

        output.WriteLine($"simulated: brightness {measured.Brightness:F2}, " +
            $"{measured.MostColors} color at most, a change every " +
            $"{16_000.0 / measured.Changes:F0} ms");
        output.WriteLine("measured : brightness 190.00, 1 color at most, a change every 137 ms");

        Assert.Equal(1, measured.MostColors);
        Assert.InRange(measured.Brightness, 170, 215);
    }

    /// <summary>
    /// Dynamic and Dynamic Smooth are the same scatter of random colors, and what separates them is
    /// whether it cuts or creeps.
    /// <para>
    /// Dynamic holds still between ticks - the strip changed picture 7 times in fourteen seconds,
    /// which is a tick every 2000 ms against the 1955 the formula gives. Dynamic Smooth creeps toward
    /// its colors a sixteenth at a time, so it changed 61 times, and carries more distinct colors as a
    /// result: 226 against 172 on a 285 LED run.
    /// </para>
    /// </summary>
    [Fact]
    public void Dynamic_cuts_to_its_colors_where_Dynamic_Smooth_creeps()
    {
        Wash cut = Measure(EffectLibrary.Find("Dynamic")!, 128, 14_000);
        Wash creep = Measure(EffectLibrary.Find("Dynamic Smooth")!, 128, 14_000);

        output.WriteLine($"simulated cut   : {cut.Colors:F1} colors, a change every " +
            $"{14_000.0 / cut.Changes:F0} ms, brightness {cut.Brightness:F1}");
        output.WriteLine("measured cut    : 172.3 colors, a change every 2000 ms, brightness 193.4");
        output.WriteLine($"simulated creep : {creep.Colors:F1} colors, a change every " +
            $"{14_000.0 / creep.Changes:F0} ms, brightness {creep.Brightness:F1}");
        output.WriteLine("measured creep  : 226.0 colors, a change every 230 ms, brightness 166.4");

        // A tick every two seconds, and still in between.
        Assert.InRange(14_000.0 / cut.Changes, 1700, 2400);

        // Where the smooth one is on the move constantly.
        Assert.True(creep.Changes > cut.Changes * 4,
            $"the smooth variant should be always moving: {creep.Changes} against {cut.Changes}");

        Assert.InRange(cut.Colors, 140, 205);
        Assert.InRange(creep.Colors, 190, 265);
    }

    /// <summary>
    /// Colorful repeats four hard-coded colors along the run and steps them round one place a second.
    /// <para>
    /// Exactly four colors on the strip and exactly four here - the slots and the palette are both
    /// ignored at the middle of the intensity slider, which is a three-way switch rather than a scale.
    /// A step every 1067 ms against the 1066 the formula gives.
    /// </para>
    /// </summary>
    [Fact]
    public void Colorful_repeats_four_fixed_colors_and_steps_them_round()
    {
        Wash measured = Measure(EffectLibrary.Find("Colorful")!, 128, 16_000);

        output.WriteLine($"simulated: {measured.MostColors} colors, spread {measured.Deviation:F2}, " +
            $"brightness {measured.Brightness:F2}, a step every {16_000.0 / measured.Changes:F0} ms");
        output.WriteLine("measured : 4 colors, spread 18.53, brightness 233.75, a step every 1067 ms");
        output.WriteLine("arithmetic: 50 + 8 * (255 - 128) = 1066 ms a step");

        Assert.Equal(4, measured.MostColors);
        Assert.InRange(measured.Brightness, 220, 248);
        Assert.InRange(measured.Deviation, 12, 26);
        Assert.InRange(16_000.0 / measured.Changes, 950, 1200);
    }
}
