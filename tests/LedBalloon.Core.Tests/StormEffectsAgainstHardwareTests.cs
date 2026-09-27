using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Lightning and Halloween Eyes, captured on the south controller's 285 LED roofline.
/// <para>
/// Both of these run to their own clock rather than to the frame, and both spend most of their time
/// showing nothing, so they are the two effects here that needed a longer capture than twelve seconds
/// and a setting other than the middle of the sliders. Lightning at a middling speed waits up to
/// thirteen seconds between strikes, and Halloween Eyes at a middling speed keeps its eyes shut for
/// half a minute at a time - so both were captured at the settings that fit several cycles into a
/// capture rather than at the settings every other test here uses.
/// </para>
/// </summary>
public class StormEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>
    /// Lightning flashes at three brightnesses and nothing in between, over a quarter of the run.
    /// <para>
    /// The brightness is <c>255 / random8(1, 3)</c>, which reads like a range and is not: the divisor
    /// is one or two, so a flash is 255 or 127 and never anything else. The leader that opens a strike
    /// is a third value, fixed at 52. The strip showed exactly those three over thirty seconds - 255
    /// eight times, 127 nine times, 52 twice - and no fourth value, which is the whole check.
    /// </para>
    /// <para>
    /// Each flash is one contiguous stretch from a random start to a random length, and the strip
    /// showed exactly one stretch in every lit frame, averaging 72.4 of 285 LEDs. A start uniform over
    /// the run and a length uniform over what is left of it average a quarter of the run, which is
    /// 71.25 - so the coverage is not a fitted number but an arithmetic one.
    /// </para>
    /// <para>
    /// Only 19 of 516 sampled frames were lit at all, because a flash is drawn for one frame of nine
    /// milliseconds and the next frame paints it out, while the preview only sends one frame in sixty.
    /// That makes the count itself a weak measurement and the brightness and the coverage strong ones,
    /// since neither depends on how many were caught.
    /// </para>
    /// </summary>
    [Fact]
    public void Lightning_flashes_at_three_brightnesses_over_a_quarter_of_the_run()
    {
        // The speed slider is the wait between strikes and the intensity is how many flashes are in
        // one, so this is as many strikes as the effect will give.
        EffectSegment segment = Run(speed: 230, intensity: 255);

        List<int> lengths = [], runs = [];
        List<double> coverage = [];
        HashSet<int> levels = [];
        int frames = 0, litFrames = 0;

        Strip.Sample(new LightningEffect(), segment, 30_000, (frame, _) =>
        {
            frames++;

            int[] on = [.. Enumerable.Range(0, frame.Length).Where(i => Brightness(frame[i]) > 0)];

            if (on.Length == 0)
            {
                return;
            }

            litFrames++;
            coverage.Add((double)on.Length / frame.Length);

            foreach (int i in on)
            {
                levels.Add(Brightness(frame[i]));
            }

            // How many separate stretches, which should always be one.
            int stretches = 1;

            for (int i = 1; i < on.Length; i++)
            {
                if (on[i] != on[i - 1] + 1)
                {
                    stretches++;
                }
            }

            runs.Add(stretches);
            lengths.Add(on.Length);
        });

        output.WriteLine($"simulated: {litFrames} of {frames} frames lit " +
            $"({(double)litFrames / frames:F3}), brightnesses {string.Join(", ", levels.Order())}, " +
            $"coverage {coverage.Average():F3}, {lengths.Average():F1} LEDs in " +
            $"{runs.Average():F2} stretches");
        output.WriteLine("measured : 19 of 516 frames lit (0.037), brightnesses 52, 127, 255, " +
            "coverage 0.254, 72.4 LEDs in 1.00 stretches");
        output.WriteLine("arithmetic: a random start over 285 and a random length over what is left " +
            "averages 71.25");

        // Three brightnesses and no fourth.
        Assert.Equal<IEnumerable<int>>([52, 127, 255], levels.Order());

        // One stretch, every time.
        Assert.Equal(1.0, runs.Average());

        // Loose on purpose. A flash covers anywhere from one LED to the whole run, so the spread on a
        // single flash is about as big as the mean, and eighteen of them leave a standard error near
        // 0.06. Anything tighter than this would be pinning noise.
        Assert.InRange(coverage.Average(), 0.17, 0.38);
        Assert.InRange((double)litFrames / frames, 0.01, 0.09);
    }

    /// <summary>
    /// Halloween Eyes draws two blocks four LEDs wide with eight dark LEDs between them.
    /// <para>
    /// The geometry is derived from the length of the run rather than fixed: the gap is a
    /// thirty-second of it and each eye is half the gap, so on 285 LEDs that is two blocks of four
    /// with eight between. The strip showed exactly two blocks in every lit frame, every block exactly
    /// four wide, and every gap exactly eight - no averages to argue about.
    /// </para>
    /// <para>
    /// The eyes fade in over an eighth of their time on, which showed up as 12.7% of lit frames being
    /// dimmer than full against the eighth the arithmetic gives. And they open in a new place each
    /// time and never move while open: nine separate openings over twenty seconds, nine distinct
    /// positions.
    /// </para>
    /// </summary>
    [Fact]
    public void Halloween_eyes_opens_two_four_LED_blocks_eight_apart()
    {
        // Speed is how long the eyes stay shut, so a low speed is what gives several openings in a
        // capture. Intensity is how long they stay open.
        EffectSegment segment = Run(speed: 8, intensity: 40);

        List<int> blocks = [], widths = [], gaps = [];
        List<int> positions = [];
        int frames = 0, litFrames = 0, fadingIn = 0, openings = 0;
        bool wasLit = false;

        Strip.Sample(new HalloweenEyesEffect(), segment, 20_000, (frame, _) =>
        {
            frames++;

            int[] on = [.. Enumerable.Range(0, frame.Length).Where(i => Brightness(frame[i]) > 0)];

            if (on.Length == 0)
            {
                wasLit = false;
                return;
            }

            litFrames++;

            if (!wasLit)
            {
                openings++;
            }

            wasLit = true;

            List<List<int>> grouped = [[on[0]]];

            for (int i = 1; i < on.Length; i++)
            {
                if (on[i] == on[i - 1] + 1)
                {
                    grouped[^1].Add(on[i]);
                }
                else
                {
                    grouped.Add([on[i]]);
                }
            }

            blocks.Add(grouped.Count);
            widths.AddRange(grouped.Select(g => g.Count));

            if (grouped.Count == 2)
            {
                gaps.Add(grouped[1][0] - grouped[0][^1] - 1);
            }

            positions.Add(on[0]);

            if (on.Select(i => Brightness(frame[i])).Max() < 255)
            {
                fadingIn++;
            }
        });

        output.WriteLine($"simulated: {litFrames} of {frames} frames lit " +
            $"({(double)litFrames / frames:F3}), {blocks.Average():F2} blocks of " +
            $"{widths.Average():F2} with a gap of {gaps.Average():F2}, " +
            $"{openings} openings at {positions.Distinct().Count()} positions, " +
            $"fading in {(double)fadingIn / litFrames:F3}");
        output.WriteLine("measured : 134 of 344 frames lit (0.390), 2.00 blocks of 4.00 with a gap " +
            "of 8.00, 9 openings at 9 positions, fading in 0.127");
        output.WriteLine("arithmetic: 285 >> 5 = 8 apart, 8 / 2 = 4 wide, a fade over an eighth of " +
            "the time on");

        // Exact, because the geometry is arithmetic and not a shape anyone measured.
        Assert.Equal(2.0, blocks.Average());
        Assert.Equal(4.0, widths.Average());
        Assert.Equal(8.0, gaps.Average());

        // A new place every time and never a move while open.
        Assert.Equal(openings, positions.Distinct().Count());
        Assert.InRange(openings, 5, 14);

        Assert.InRange((double)litFrames / frames, 0.25, 0.55);
        Assert.InRange((double)fadingIn / litFrames, 0.05, 0.25);
    }

    private static byte Brightness(RgbColor color) =>
        Math.Max(color.R, Math.Max(color.G, color.B));

    /// <summary>
    /// The run as both captures had it: palette Default with a black background, since both of these
    /// paint the background over the whole run every frame and a bright one would hide the effect.
    /// </summary>
    private static EffectSegment Run(byte speed, byte intensity) => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = speed,
        Intensity = intensity,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 0,
        Colors = [new RgbColor(255, 0, 0), RgbColor.Black, new RgbColor(0, 255, 0)],
    };
}
