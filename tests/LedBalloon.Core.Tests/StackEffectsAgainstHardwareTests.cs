using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Tetrix and Aurora, captured on the south controller's 285 LED roofline on palette Rainbow.
/// <para>
/// Both were captured with the secondary color set to black. Tetrix uses the secondary as its
/// background and fades a full run into it, so a blue one makes "has this LED anything on it" mean
/// nothing - and the stack height is the whole measurement. Aurora does not read the secondary as a
/// background at all, but it counts the color slots that are set to decide how dim its backlight is,
/// so this capture was taken with all three set and the backlight at its brightest.
/// </para>
/// </summary>
public class StackEffectsAgainstHardwareTests(ITestOutputHelper output)
{
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

    private static EffectSegment Run(byte speed, RgbColor secondary) => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = speed,
        Intensity = 128,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 11,
        Palette = Rainbow(),
        Colors = [new RgbColor(255, 0, 0), secondary, new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// Tetrix stacks bricks of exactly 25 LEDs, and the stack only ever stands at a multiple of 25.
    /// <para>
    /// The brick size is arithmetic, not a setting: the intensity slider gives <c>(128 &gt;&gt; 5) + 1</c>,
    /// which is 5, and that is multiplied by <c>1 + (285 &gt;&gt; 6)</c>, another 5, so bricks grow with
    /// the run. The strip's stack stood at 0, 25, 50 and so on to 275 and at no other height below full
    /// over 397 frames - which is a stronger statement than any average, because a single brick of the
    /// wrong size would put an odd number in that list.
    /// </para>
    /// <para>
    /// It is also the one effect here whose picture is genuinely accumulated: the falling brick repaints
    /// everything above the stack every frame and never touches the stack, so the stack persists because
    /// nothing overwrites it rather than because anything remembers it.
    /// </para>
    /// </summary>
    [Fact]
    public void Tetrix_stacks_bricks_of_exactly_twenty_five_LEDs()
    {
        Stack stack = Measure(Run(speed: 255, secondary: RgbColor.Black), 25_000);

        output.WriteLine($"simulated: brightness {stack.Brightness:F2} (up to {stack.Brightest:F2}), " +
            $"stack {stack.MeanHeight:F1} of {stack.TallestStack}, {stack.Colors} colors, " +
            $"{stack.DarkFrames} frames clear");
        output.WriteLine($"heights below full: {string.Join(", ", stack.Heights)}");
        output.WriteLine("measured : brightness 60.78 (up to 204.81), stack 72.8 of 285, 79 colors, " +
            "56 frames clear");
        output.WriteLine("measured heights below full: 0, 25, 50, 75, 100, 125, 150, 175, 200, 225, " +
            "250, 275");
        output.WriteLine("arithmetic: ((128 >> 5) + 1) * (1 + (285 >> 6)) = 5 * 5 = 25 LEDs a brick");

        // Every multiple of 25 and nothing else, which pins the brick size exactly.
        Assert.Equal<IEnumerable<int>>(
            [0, 25, 50, 75, 100, 125, 150, 175, 200, 225, 250, 275], stack.Heights);

        Assert.Equal(Strip.Roofline, stack.TallestStack);

        // It fills and then clears, so there are frames with nothing on the run at all.
        Assert.True(stack.DarkFrames > 10, $"the run should clear: {stack.DarkFrames} frames");

        Assert.InRange(stack.Brightness, 45, 78);
    }

    /// <summary>
    /// Tetrix takes about ten seconds to fill a 285 LED run at full speed, not the quarter second the
    /// slider claims.
    /// <para>
    /// A brick's speed is <c>SEGLEN * FRAMETIME / milliseconds</c>, in LEDs per frame - and the comment
    /// beside it says a full drop should take five seconds at the bottom of the slider and a quarter of
    /// a second at the top. That only holds if a frame lasts FRAMETIME. On these controllers the frame
    /// rate is uncapped, so FRAMETIME is the 2 ms floor while the wire actually takes 9, and every brick
    /// falls about four and a half times slower than advertised.
    /// </para>
    /// <para>
    /// Which makes this a measurement of the timing constant rather than of Tetrix. Eleven bricks over
    /// a shrinking distance plus a two second fade comes to about ten seconds, and the strip took 10.1.
    /// Had FRAMETIME been the 23 ms a 42 frame cap would give, the same run would have taken under a
    /// second - so this is the same confirmation Blink and Strobe gave, from a completely different
    /// direction.
    /// </para>
    /// </summary>
    [Fact]
    public void Tetrix_fills_the_run_at_the_rate_the_frame_time_implies()
    {
        Stack stack = Measure(Run(speed: 255, secondary: RgbColor.Black), 40_000);

        output.WriteLine($"simulated: {stack.Cycles} clears in 40 s, one every {stack.CycleSeconds:F1} s");
        output.WriteLine("measured : one every 10.1 s");
        output.WriteLine("arithmetic: 285 * 2 / 250 = 2.28 LEDs a frame, 1760 LEDs of falling over " +
            "eleven bricks, and a two second fade");
        output.WriteLine("with a 23 ms frame time it would be 0.9 s");

        Assert.True(stack.Cycles >= 2, $"40 s should cover several clears: {stack.Cycles}");
        Assert.InRange(stack.CycleSeconds, 7.5, 13.5);
    }

    /// <summary>
    /// Aurora never lets the run go darker than 4, which is its backlight and a count of the color slots.
    /// <para>
    /// The backlight is one unit plus one for each color slot that is not black, so with all three set it
    /// is exactly 4 - and the dimmest LED anywhere was exactly 4 in all three captures taken. Not
    /// approximately: the floor is a hard one, because it is where every LED starts before any wave is
    /// added to it.
    /// </para>
    /// <para>
    /// Everything else about this effect is a distribution rather than a number, and it took three
    /// captures to see that. Eleven waves over twelve seconds, each with its own random width, lifetime
    /// and alpha, is a small sample: the strip gave mean brightnesses of 48.19, 46.75 and 50.23, and the
    /// port over eight seeds gives 42.35 to 55.14 around a mean of 50.0. One capture against one seed
    /// would have looked like a fourteen percent error.
    /// </para>
    /// <para>
    /// Saturation is the clearest case of that. Waves add saturating rather than replacing, so a deep
    /// enough overlap clips - and it is rare enough that the first capture had none at all, which nearly
    /// went into this test as "never saturates". The second had 0.62% of samples clipped. So what is
    /// checked is that it is rare, not that it is absent.
    /// </para>
    /// </summary>
    [Fact]
    public void Aurora_holds_a_hard_floor_of_four_and_only_rarely_saturates()
    {
        EffectSegment segment = Run(speed: 128, secondary: new RgbColor(0, 0, 255));

        List<double> brightness = [], spread = [], peaks = [], colors = [];
        int dimmest = 255, atFloor = 0, saturated = 0, samples = 0;

        Strip.Sample(new AuroraEffect(), segment, 12_000, (frame, _) =>
        {
            Strip.Shape profile = Strip.Profile(frame, Brightness);

            brightness.Add(profile.Mean);
            spread.Add(profile.Deviation);
            peaks.Add(profile.Bands);
            colors.Add(frame.Distinct().Count());

            foreach (RgbColor pixel in frame)
            {
                byte value = Brightness(pixel);

                dimmest = Math.Min(dimmest, value);
                samples++;

                if (value <= 4)
                {
                    atFloor++;
                }

                if (value == 255)
                {
                    saturated++;
                }
            }
        });

        output.WriteLine($"simulated: brightness {brightness.Average():F2}, spread {spread.Average():F2}, " +
            $"{peaks.Average():F2} peaks, {colors.Average():F1} colors, dimmest {dimmest}, " +
            $"at the floor {(double)atFloor / samples:F4}, saturated {(double)saturated / samples:F4}");
        output.WriteLine("measured, three captures:");
        output.WriteLine("  brightness 48.19 / 46.75 / 50.23, spread 38.23 / 41.63 / 42.96");
        output.WriteLine("  dimmest 4 / 4 / 4, at the floor 0.1626 / 0.1561 / 0.1352");
        output.WriteLine("  saturated 0.0000 / 0.0062 / 0.0002");
        output.WriteLine("arithmetic: 1 + one per color slot that is set = 4");

        // A hard floor, and exactly where the slot count puts it. This is the only exact number here.
        Assert.Equal(4, dimmest);

        // Rare rather than absent, which took three captures to establish.
        Assert.InRange((double)saturated / samples, 0, 0.02);

        Assert.InRange((double)atFloor / samples, 0.06, 0.22);
        Assert.InRange(brightness.Average(), 40, 60);
        Assert.InRange(spread.Average(), 32, 47);
        Assert.InRange(peaks.Average(), 3, 10);
    }

    /// <summary>
    /// Aurora's backlight follows the color slots, which is the only thing the slots do for it.
    /// <para>
    /// One unit, plus one for each slot that is not black. Not measured against the strip - it would
    /// mean three more captures to read three numbers off - but worth pinning, because it is the reason
    /// this effect looks switched on between waves and the reason the capture above was taken with all
    /// three slots set.
    /// </para>
    /// </summary>
    [Fact]
    public void Aurora_s_backlight_counts_the_color_slots_that_are_set()
    {
        foreach ((RgbColor[] slots, int expected) in new[]
        {
            (new[] { RgbColor.Black, RgbColor.Black, RgbColor.Black }, 1),
            (new[] { new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black }, 2),
            (new[] { new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), RgbColor.Black }, 3),
            (new[] { new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0) }, 4),
        })
        {
            EffectSegment segment = Run(speed: 128, secondary: RgbColor.Black);
            segment.Colors = slots;

            int dimmest = 255;

            // A few hundred frames, so that somewhere on the run there is an LED no wave is covering.
            for (uint t = 0; t < 2_000; t += (uint)Strip.FrameMs)
            {
                segment.Draw(new AuroraEffect(), t);
                dimmest = Math.Min(dimmest, segment.Pixels.Min(Brightness));
            }

            output.WriteLine($"{slots.Count(s => s != RgbColor.Black)} slots set: floor {dimmest}");

            Assert.Equal(expected, dimmest);
        }
    }

    /// <summary>What a capture of Tetrix looks like, reduced.</summary>
    private readonly record struct Stack(
        double Brightness,
        double Brightest,
        double MeanHeight,
        int TallestStack,
        int[] Heights,
        int Colors,
        int DarkFrames,
        int Cycles,
        double CycleSeconds);

    private static Stack Measure(EffectSegment segment, uint milliseconds)
    {
        List<double> brightness = [];
        List<int> heights = [];
        HashSet<RgbColor> colors = [];
        int darkFrames = 0;
        List<uint> clears = [];
        int previous = 0;

        Strip.Sample(new TetrixEffect(), segment, milliseconds, (frame, when) =>
        {
            // Frames arrive reversed and the stack builds from the start of the segment, so put them
            // back the right way round before walking up from the bottom of the stack.
            RgbColor[] run = [.. frame.Reverse()];

            brightness.Add(run.Average(p => (double)Brightness(p)));

            int height = 0;

            while (height < run.Length && Brightness(run[height]) > 0)
            {
                height++;
            }

            heights.Add(height);

            if (height == 0)
            {
                darkFrames++;
            }

            // A stack that has just lost most of its height is a run that filled and cleared.
            if (height < previous - 20)
            {
                clears.Add(when);
            }

            previous = height;

            foreach (RgbColor pixel in run)
            {
                colors.Add(pixel);
            }
        });

        // Only the gaps between clears, since the first cycle started before the capture did.
        double cycleSeconds = clears.Count > 1
            ? clears.Zip(clears.Skip(1), (a, b) => (b - a) / 1000.0).Where(g => g > 1).Average()
            : 0;

        return new Stack(
            brightness.Average(),
            brightness.Max(),
            heights.Average(),
            heights.Max(),
            [.. heights.Where(h => h < Strip.Roofline).Distinct().Order()],
            colors.Count,
            darkFrames,
            clears.Count,
            cycleSeconds);
    }

    private static byte Brightness(RgbColor color) =>
        Math.Max(color.R, Math.Max(color.G, color.B));
}
