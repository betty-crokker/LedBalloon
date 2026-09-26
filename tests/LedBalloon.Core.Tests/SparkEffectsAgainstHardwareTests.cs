using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Glitter, Solid Glitter, Oscillate, Fireworks, Rain, Traffic Light and Percent, captured on the
/// south controller's 285 LED roofline with the color slots set to red, blue and green - except for
/// the two glitters, whose third slot was set to white so a spark could be told from the background.
/// </summary>
public class SparkEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>The rainbow the strip showed behind Glitter, which is what palette Default resolves to.</summary>
    private static WledPalette Rainbow() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(255, 0, 0)),
            new PaletteStop(43, new RgbColor(213, 85, 0)),
            new PaletteStop(85, new RgbColor(128, 128, 0)),
            new PaletteStop(128, new RgbColor(0, 255, 0)),
            new PaletteStop(170, new RgbColor(0, 128, 128)),
            new PaletteStop(213, new RgbColor(0, 0, 255)),
            new PaletteStop(255, new RgbColor(255, 0, 0)),
        ],
    };

    /// <summary>
    /// A spark is one LED at most, and whether there is one at all is a throw of the dice at odds the
    /// intensity slider sets.
    /// <para>
    /// The odds are <c>intensity &gt; random8()</c>, so 128 is a coin toss - and the strip agreed to
    /// within a percent on both effects, 50.6% and 52.8% of frames carrying a spark. Never two.
    /// </para>
    /// <para>
    /// The count being one rather than proportional is the whole character: turning intensity up makes
    /// sparks more frequent, not more numerous.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Glitter", 0.506)]
    [InlineData("Solid Glitter", 0.528)]
    public void The_glitters_spark_one_led_at_a_time_at_odds_the_slider_sets(string name, double measured)
    {
        IWledEffect effect = EffectLibrary.Find(name)!;

        EffectSegment segment = Strip.Run();
        segment.Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), RgbColor.White];
        segment.PaletteId = 11;
        segment.Palette = Rainbow();

        List<int> sparks = [];

        Strip.Sample(effect, segment, 16_000, (frame, _) =>
            sparks.Add(frame.Count(p => p.R > 200 && p.G > 200 && p.B > 200)));

        double share = (double)sparks.Count(c => c > 0) / sparks.Count;

        output.WriteLine($"simulated: {share:F3} of frames sparking, counts seen " +
            $"{string.Join(", ", sparks.Distinct().Order())}");
        output.WriteLine($"measured : {measured:F3}, counts seen 0, 1");
        output.WriteLine("arithmetic: intensity 128 of 256, so half of frames");

        Assert.All(sparks, c => Assert.True(c is 0 or 1, $"{c} sparks, expected 0 or 1"));
        Assert.InRange(share, measured - 0.10, measured + 0.10);
    }

    /// <summary>
    /// Glitter asks for the gradient even on palette Default, by passing a color slot that does not
    /// exist.
    /// <para>
    /// WLED's own test is <c>palette == 0 &amp;&amp; mcol &lt; 3</c>, so a slot of 255 falls through to
    /// the palette instead of returning a flat color. Checked here rather than on the strip because
    /// what palette Default resolves to for this effect is the firmware's business - the strip showed
    /// a full rainbow behind it, not anything built from the color slots.
    /// </para>
    /// </summary>
    [Fact]
    public void A_color_slot_that_does_not_exist_asks_for_the_gradient_anyway()
    {
        var segment = new EffectSegment(16)
        {
            PaletteId = 0,
            Palette = Rainbow(),
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), RgbColor.Black],
        };

        RgbColor slot = segment.ColorFromPalette(128, colorSlot: 0);
        RgbColor gradient = segment.ColorFromPalette(128, colorSlot: 255);

        output.WriteLine($"slot 0 gives {slot}, slot 255 gives {gradient}");

        // Slot 0 short-circuits to the primary color, as every other effect expects.
        Assert.Equal(segment.Colors[0], slot);

        // Slot 255 does not, and lands in the green of the gradient's middle.
        Assert.NotEqual(segment.Colors[0], gradient);
        Assert.True(gradient.G > gradient.R, "halfway along a rainbow should be green");
    }

    /// <summary>
    /// Three bars of the three color slots, each 31 LEDs across, bouncing and crossing.
    /// <para>
    /// Size is <c>length / (3 + intensity / 8)</c> either side of the center, so 15 either way and 31
    /// in all; three of those is 93, and that is exactly what the strip peaked at. It averaged 82.8,
    /// the shortfall being the frames where two bars are on top of each other.
    /// </para>
    /// <para>
    /// Those crossings are why there are five colors on the run and not three: an overlap blends half
    /// and half rather than one bar winning. And the bars step every 274 ms, which is
    /// <c>20 + 2 * (255 - speed)</c> - the strip changed picture every 276.
    /// </para>
    /// </summary>
    [Fact]
    public void Oscillate_bounces_three_bars_of_thirty_one_and_blends_them_where_they_cross()
    {
        List<int> lit = [];
        List<int> colors = [];
        int changes = 0;
        RgbColor[]? previous = null;

        Strip.Sample(new OscillateEffect(), Strip.Run(), 16_000, (frame, _) =>
        {
            lit.Add(frame.Count(p => p != RgbColor.Black));
            colors.Add(frame.Where(p => p != RgbColor.Black).Distinct().Count());

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                changes++;
            }

            previous = [.. frame];
        });

        output.WriteLine($"simulated: lit {lit.Average():F1} ({lit.Min()}-{lit.Max()}), " +
            $"{colors.Max()} colors at most, a step every {16_000.0 / changes:F0} ms");
        output.WriteLine("measured : lit 82.8 (48-93), 5 colors at most, a step every 276 ms");
        output.WriteLine("arithmetic: 285 / (3 + 128 / 8) = 15 either side, so 31 a bar and 93 in all");

        Assert.Equal(93, lit.Max());
        Assert.InRange(lit.Average(), 70, 93);
        Assert.InRange(16_000.0 / changes, 250, 300);

        // Three bars and the blends where they meet.
        Assert.InRange(colors.Max(), 4, 6);
    }

    /// <summary>
    /// A traffic light every three LEDs, four phases of 150 ms each at the top of the speed slider.
    /// <para>
    /// The colors are hard-coded rather than taken from the slots, and the time-weighted shares fall
    /// straight out of the sequence: 95 lights of 285 is a third of the run, red is on for two of the
    /// four phases counting the amber that reads as red, and green for one. That gives 0.333 red,
    /// 0.083 green and 0.583 blue background, and the strip gave 0.349, 0.077 and 0.574.
    /// </para>
    /// </summary>
    [Fact]
    public void Traffic_Light_cycles_four_phases_of_the_same_length_at_full_speed()
    {
        List<double> red = [], green = [], blue = [];
        int changes = 0;
        RgbColor[]? previous = null;

        Strip.Sample(new TrafficLightEffect(), Strip.Run(255), 16_000, (frame, _) =>
        {
            red.Add((double)frame.Count(p => Strip.Dominant(p) == 0) / frame.Length);
            green.Add((double)frame.Count(p => Strip.Dominant(p) == 1) / frame.Length);
            blue.Add((double)frame.Count(p => Strip.Dominant(p) == 2) / frame.Length);

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                changes++;
            }

            previous = [.. frame];
        });

        output.WriteLine($"simulated: red {red.Average():F3} green {green.Average():F3} " +
            $"blue {blue.Average():F3}, a phase every {16_000.0 / changes:F0} ms");
        output.WriteLine("measured : red 0.349 green 0.077 blue 0.574, a phase every 155 ms");

        Assert.InRange(red.Average(), 0.28, 0.40);
        Assert.InRange(green.Average(), 0.05, 0.11);
        Assert.InRange(blue.Average(), 0.52, 0.63);
        Assert.InRange(16_000.0 / changes, 135, 180);
    }

    /// <summary>
    /// Percent fills 205 of the 285 LEDs at the middle of its slider, and fills from the far end.
    /// <para>
    /// The slider runs to 200 rather than 100. At 128 it is past halfway, so the reading counts back
    /// down - <c>200 - 128</c> is 72 percent, which is 205 LEDs - and it fills from the other end. The
    /// strip held 71.9% of the run in the palette color, which is 205 exactly.
    /// </para>
    /// </summary>
    [Fact]
    public void Percent_reads_its_slider_backwards_past_the_halfway_mark()
    {
        List<double> filled = [];

        Strip.Sample(new PercentEffect(), Strip.Run(), 14_000, (frame, _) =>
            filled.Add((double)frame.Count(p => Strip.Dominant(p) == 0) / frame.Length));

        output.WriteLine($"simulated: {filled.Average():F3} filled " +
            $"({filled.Min():F3}-{filled.Max():F3})");
        output.WriteLine("measured : 0.719 filled (0.719-0.719)");
        output.WriteLine($"arithmetic: 285 * (200 - 128) / 100 = 205, which is {205 / 285.0:F3}");

        Assert.InRange(filled.Average(), 0.70, 0.74);

        // Settled, not still moving toward its target.
        Assert.Equal(filled.Min(), filled.Max(), 3);
    }

    /// <summary>
    /// Fireworks and Rain: sparks that fade slowly and are blurred into their neighbors.
    /// <para>
    /// On palette Default every spark is the primary color, so the red channel is the whole picture.
    /// Fireworks held a mean of 9.8 with 16.9 peaks along the run; Rain, which shifts the whole run
    /// along underneath the sparks, held 17.8 with 37.0 peaks and four times as many sharp steps -
    /// because a shifting spark is drawn as a streak rather than a point.
    /// </para>
    /// <para>
    /// Rain spawns at half Fireworks' rate here, since these were captured at the intensities the two
    /// effects actually ship with: 192 for Fireworks and 128 for Rain.
    /// </para>
    /// <para>
    /// The sharp-step count is the measurement worth having, and it found a bug that the other two
    /// hid. Whether the shared function blurs at all is decided by the step field - Fireworks never
    /// touches it so it always blurs, and Rain uses it as a timer so it never does. Blurring Rain as
    /// well left 5.4 sharp steps against the strip's 43.5; not blurring it gives 42.9.
    /// </para>
    /// <para>
    /// Fireworks still holds about 30% more red than the strip does, which is not explained. The
    /// spawn odds and the fade rate are both read straight from the source and the sharp-step count
    /// agrees to a tenth, so it is something about how long a spark survives rather than how often
    /// one appears. Left recorded.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Fireworks", 192, 9.795, 16.9, 11.0)]
    [InlineData("Rain", 128, 17.767, 37.0, 43.5)]
    public void The_spark_effects_leave_soft_edged_trails(
        string name, byte intensity, double meanRed, double peaks, double hardEdges)
    {
        IWledEffect effect = EffectLibrary.Find(name)!;

        List<double> means = [], counts = [], hard = [];

        Strip.Sample(effect, Strip.Run(128, intensity), 16_000, (frame, _) =>
        {
            means.Add(frame.Average(p => (double)p.R));

            int found = 0, steps = 0;
            bool climbing = false;

            for (int i = 1; i < frame.Length; i++)
            {
                int step = frame[i].R - frame[i - 1].R;

                if (Math.Abs(step) > 100)
                {
                    steps++;
                }

                if (step > 0)
                {
                    climbing = true;
                }
                else if (climbing && step < 0)
                {
                    found++;
                    climbing = false;
                }
            }

            counts.Add(found);
            hard.Add(steps);
        });

        output.WriteLine($"simulated: mean red {means.Average():F1}, {counts.Average():F1} peaks, " +
            $"{hard.Average():F1} sharp steps");
        output.WriteLine($"measured : mean red {meanRed:F1}, {peaks:F1} peaks, {hardEdges:F1} sharp steps");

        // Sparse and soft rather than a run full of light.
        Assert.InRange(means.Average(), meanRed * 0.7, meanRed * 1.45);
        Assert.InRange(counts.Average(), peaks * 0.7, peaks * 1.45);

        // The one that tells a blurred streak from a sharp one, and the one that caught the bug.
        Assert.InRange(hard.Average(), hardEdges * 0.7, hardEdges * 1.45);
    }
}
