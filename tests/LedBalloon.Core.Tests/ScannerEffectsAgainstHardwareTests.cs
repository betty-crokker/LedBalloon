using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Scanner, Scanner Dual and Lighthouse, captured on the south controller's 285 LED roofline with
/// the color slots set to red, blue and green.
/// <para>
/// All three are a point with a fading trail, and what distinguishes them is the path: Scanner
/// paces back and forth, Scanner Dual adds a mirror image, and Lighthouse only ever runs one way and
/// reappears at the far end rather than coming back.
/// </para>
/// </summary>
public class ScannerEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>
    /// Where the point is, and which way it is going. Counted as jumps from one end back to the
    /// other against changes of direction, which says which of the three paths it is on without
    /// caring which way round the run is wired.
    /// </summary>
    private static (int Wraps, int Turns, double Red, double Green, int MostFills) Track(
        IWledEffect effect, EffectSegment segment, uint milliseconds)
    {
        List<int> brightest = [];
        List<double> red = [], green = [];
        int most = 0;

        Strip.Sample(effect, segment, milliseconds, (frame, _) =>
        {
            // Followed by the red channel, not by brightness. The point is the palette color and
            // the trail fades toward the secondary, and here those are pure red and pure blue -
            // equally bright, so a brightness metric cannot see the point move at all. It reported
            // the point standing still for fourteen seconds.
            int at = 0;

            for (int i = 1; i < frame.Length; i++)
            {
                if (frame[i].R > frame[at].R)
                {
                    at = i;
                }
            }

            brightest.Add(at);
            red.Add((double)frame.Count(p => Strip.Dominant(p) == 0) / frame.Length);
            green.Add((double)frame.Count(p => Strip.Dominant(p) == 1) / frame.Length);
            most = Math.Max(most, Strip.Fills(frame));
        });

        int wraps = 0, turns = 0, last = 0;

        for (int i = 1; i < brightest.Count; i++)
        {
            int step = brightest[i] - brightest[i - 1];

            if (Math.Abs(step) > Strip.Roofline / 2)
            {
                wraps++;
                continue;
            }

            int direction = Math.Sign(step);

            if (direction == 0)
            {
                continue;
            }

            if (last != 0 && direction != last)
            {
                turns++;
            }

            last = direction;
        }

        return (wraps, turns, red.Average(), green.Average(), most);
    }

    /// <summary>The run as Scanner is actually set up, with no pause at the ends.</summary>
    private static EffectSegment Sweeping()
    {
        EffectSegment segment = Strip.Run();

        // WLED's metadata sets Custom 1 to zero for this effect, and a preset stores what the
        // firmware chose. Left at the generic default it would pause for three seconds at each end.
        segment.Custom1 = 0;
        return segment;
    }

    /// <summary>
    /// A point pacing the length of the run and back, taking about 1.4 seconds each way.
    /// <para>
    /// This is the effect that measures WLED's <c>FRAMETIME</c> by how fast it moves. The point
    /// advances <c>length / (FRAMETIME * map(speed, 0, 255, 96, 2))</c> LEDs a frame, and on this
    /// roofline the two candidate constants are not close: with 2 ms it advances 2 LEDs a frame and
    /// crosses in 1.4 seconds, and with the frame interval it advances none and has to count frames
    /// per LED instead, taking around ten. The strip crossed in 1.4.
    /// </para>
    /// </summary>
    [Fact]
    public void Scanner_crosses_the_roofline_in_about_a_second_and_a_half()
    {
        (int wraps, int turns, double red, _, int most) =
            Track(new ScannerEffect(), Sweeping(), 14_000);

        output.WriteLine($"simulated: {turns} turns, {wraps} wraps, " +
            $"{14_000.0 / Math.Max(1, turns):F0} ms per crossing, red {red:F3}, {most} fills");
        output.WriteLine("measured : 9 turns, 0 wraps, 1556 ms per crossing, red 0.500, 14 fills");
        output.WriteLine("arithmetic: 285 / (2 * 49) = 2 LEDs a frame, so 143 frames a crossing");

        Assert.Equal(0, wraps);
        Assert.InRange(turns, 7, 13);
        Assert.InRange(red, 0.42, 0.58);
    }

    /// <summary>
    /// The mirrored point is drawn in the third color slot rather than in the palette, so the two
    /// halves of the sweep are different colors.
    /// <para>
    /// The strip held 41.6% red and 41.8% green where Scanner alone held 50% red and no green at
    /// all, which is what says the mirror takes the slot rather than copying the color.
    /// </para>
    /// </summary>
    [Fact]
    public void Scanner_Dual_draws_its_mirror_in_the_third_color_slot()
    {
        (_, _, double red, double green, int most) =
            Track(new DualScannerEffect(), Sweeping(), 14_000);

        output.WriteLine($"simulated: red {red:F3} green {green:F3}, {most} fills");
        output.WriteLine("measured : red 0.416 green 0.418, 18 fills");

        Assert.InRange(green, 0.33, 0.50);
        Assert.InRange(red, 0.33, 0.50);

        // The two trails are the same length, being the same sweep drawn twice.
        Assert.InRange(Math.Abs(red - green), 0, 0.08);
    }

    /// <summary>
    /// A beam running one way only, leaving at one end and reappearing at the other.
    /// <para>
    /// Where Scanner turns round, this jumps: the strip showed seven jumps and not a single change
    /// of direction over fourteen seconds, which is a lap every two seconds - the 1986 ms a
    /// sixteen-bit counter takes to wrap at this speed.
    /// </para>
    /// <para>
    /// And the beam has a front and a tail rather than being symmetrical. The steepest step in the
    /// red channel was 232 one way and 4 the other.
    /// </para>
    /// </summary>
    [Fact]
    public void Lighthouse_runs_one_way_and_reappears_rather_than_turning_round()
    {
        (int wraps, int turns, _, _, int most) =
            Track(new LighthouseEffect(), Strip.Run(), 14_000);

        List<int> rises = [], falls = [];

        Strip.Sample(new LighthouseEffect(), Strip.Run(), 14_000, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => p.R);
            rises.Add(shape.SteepestRise);
            falls.Add(shape.SteepestFall);
        });

        output.WriteLine($"simulated: {wraps} wraps, {turns} turns, {most} fills, " +
            $"steepest rise {rises.Average():F0} fall {falls.Average():F0}");
        output.WriteLine("measured : 7 wraps, 0 turns, 16 fills, steepest rise 232 fall 4");

        Assert.Equal(0, turns);
        Assert.InRange(wraps, 5, 9);

        // A sharp front and a long tail, not a symmetrical blob.
        Assert.True(rises.Average() > falls.Average() * 10,
            "the beam should have one hard edge and one that fades");
    }
}
