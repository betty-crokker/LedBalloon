using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The four chases, captured on the south controller's 285 LED roofline with the color slots set to
/// red, blue and green.
/// <para>
/// Two of the four draw their background off the color wheel, which produces plenty of reddish and
/// bluish pixels of its own. So a band set to a color slot is counted by matching that slot exactly
/// rather than by which channel leads, and that is what the strip was measured on too.
/// </para>
/// </summary>
public class ChaseEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor Blue = new(0, 0, 255);
    private static readonly RgbColor Green = new(0, 255, 0);

    /// <summary>The exact width of a band at the middle of the intensity slider.</summary>
    private const int BandWidth = 36;

    private static (double Red, double Blue, double Green, int MostFills, int Visits) Measure(
        IWledEffect effect, uint milliseconds)
    {
        List<double> red = [], blue = [], green = [];
        int most = 0, visits = 0;
        bool wasBand = false;

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            red.Add((double)frame.Count(p => p == Red) / frame.Length);
            blue.Add((double)frame.Count(p => p == Blue) / frame.Length);
            green.Add((double)frame.Count(p => p == Green) / frame.Length);
            most = Math.Max(most, Strip.Fills(frame));

            // One LED is taken by a band once a lap, which counts laps without having to follow a
            // position that jumps between the two band edges.
            bool band = frame[0] == Green;

            if (band && !wasBand)
            {
                visits++;
            }

            wasBand = band;
        });

        return (red.Average(), blue.Average(), green.Average(), most, visits);
    }

    /// <summary>
    /// Two bands of 36 over a background of 213, and a lap every two seconds.
    /// <para>
    /// The width is <c>1 + ((intensity * length) &gt;&gt; 10)</c> - half what Scan's slider gives
    /// for the same setting, from the same arithmetic shifted one place further. The strip held
    /// 12.6% red and 12.6% green against 74.7% blue, which is 36, 36 and 213 exactly.
    /// </para>
    /// <para>
    /// Speed is a counter that wraps at sixteen bits rather than a cycle time, so a lap takes
    /// 65536 / ((speed &gt;&gt; 2) + 1) ms: 1986 at the middle of the slider, and the strip passed
    /// one LED seven times in fourteen seconds.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_runs_two_bands_of_thirty_six_round_the_roofline()
    {
        (double red, double blue, double green, int most, int visits) =
            Measure(new ChaseColorEffect(), 14_000);

        output.WriteLine($"simulated: red {red:F3} blue {blue:F3} green {green:F3}, " +
            $"{most} fills, {visits} laps");
        output.WriteLine("measured : red 0.126 blue 0.747 green 0.126, 3 fills, 7 laps");
        output.WriteLine($"arithmetic: 1 + ((128 * 285) >> 10) = {BandWidth} of 285 = " +
            $"{BandWidth / 285.0:F3}; 65536 / 33 = 1986 ms per lap");

        Assert.Equal(3, most);
        Assert.InRange(red, 0.11, 0.14);
        Assert.InRange(green, 0.11, 0.14);
        Assert.InRange(blue, 0.73, 0.76);
        Assert.InRange(visits, 6, 8);
    }

    /// <summary>
    /// A new background color each lap, arriving behind the bands rather than everywhere at once.
    /// <para>
    /// That is what makes four colors possible at a time where every other chase shows three: the
    /// stretch the bands have already passed keeps the previous lap's color while the stretch ahead
    /// of them has the new one, so old background, new background and two bands are all on the run
    /// together. The strip reached four and no more.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_Random_lays_the_new_background_down_behind_the_bands()
    {
        (_, _, double green, int most, _) = Measure(new ChaseRandomEffect(), 14_000);

        output.WriteLine($"simulated: {most} fills at most, green band {green:F3}");
        output.WriteLine("measured : 4 fills at most");

        Assert.Equal(4, most);

        // The bands are still the color slots, whatever the background is doing.
        Assert.InRange(green, 0.11, 0.14);
    }

    /// <summary>
    /// The bands in the two color slots over a background walking the color wheel.
    /// <para>
    /// The strip held 13.9% exactly red and 13.5% exactly blue, a little over the 12.6% the bands
    /// account for because the background passes through pure red and pure blue on its way round.
    /// </para>
    /// <para>
    /// It showed no green at all, but that is a small sample rather than a rule: this effect does
    /// not use the third slot, yet its background walks the wheel a step a frame and the wheel
    /// passes through pure green once every 256 steps. Over the 170 frames captured that was odds
    /// of two to one against seeing it, and it was not seen. So what is asserted is that green is
    /// rare, not that it is impossible - the earlier reading of zero was luck, and pinning it would
    /// have been pinning the luck.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_Rainbow_puts_the_color_slots_on_a_background_that_walks_the_wheel()
    {
        (double red, double blue, double green, int most, _) =
            Measure(new ChaseRainbowEffect(), 12_000);

        output.WriteLine($"simulated: red {red:F3} blue {blue:F3} green {green:F3}, {most} fills");
        output.WriteLine("measured : red 0.139 blue 0.135 green 0.000, 3 fills");

        Assert.Equal(3, most);
        Assert.InRange(red, 0.11, 0.17);
        Assert.InRange(blue, 0.11, 0.17);

        // Only ever the background passing through it, never a band: a band would put it level
        // with the other two.
        Assert.InRange(green, 0, 0.02);
    }

    /// <summary>
    /// The other way round: the background is the primary color and the bands walk the wheel.
    /// <para>
    /// Which makes the background measurable to the LED. The strip held 74.7% exactly red, and
    /// 285 less two bands of 36 is 213, which is 74.74%.
    /// </para>
    /// </summary>
    [Fact]
    public void Rainbow_Runner_leaves_exactly_the_background_in_the_primary_color()
    {
        (double red, double blue, _, int most, _) = Measure(new RainbowRunnerEffect(), 12_000);

        output.WriteLine($"simulated: red {red:F3} blue {blue:F3}, {most} fills");
        output.WriteLine("measured : red 0.747 blue 0.000, 3 fills");
        output.WriteLine($"arithmetic: (285 - 2 * {BandWidth}) / 285 = " +
            $"{(285 - (2 * BandWidth)) / 285.0:F3}");

        Assert.Equal(3, most);
        Assert.InRange(red, 0.73, 0.76);
    }

    /// <summary>
    /// Chase 3 is not a chase at all despite the name: equal stripes of three colors all the way
    /// along, marching.
    /// <para>
    /// Every color gets exactly a third of the run - the strip held 0.333, 0.332 and 0.336 - and
    /// the stripes are nine LEDs each, so a full pattern is 27 and a 285 LED roofline carries 10.6
    /// of them. Counting peaks in the red channel found 10.21, the shortfall being the two ends.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_3_lays_equal_stripes_of_three_colors_along_the_run()
    {
        List<double> red = [], blue = [], green = [];
        List<int> bands = [];
        int most = 0;

        Strip.Sample(new TricolorChaseEffect(), Strip.Run(), 14_000, (frame, _) =>
        {
            red.Add((double)frame.Count(p => p == Red) / frame.Length);
            blue.Add((double)frame.Count(p => p == Blue) / frame.Length);
            green.Add((double)frame.Count(p => p == Green) / frame.Length);
            bands.Add(Strip.Profile(frame, p => p.R).Bands);
            most = Math.Max(most, Strip.Fills(frame));
        });

        output.WriteLine($"simulated: red {red.Average():F3} blue {blue.Average():F3} " +
            $"green {green.Average():F3}, {bands.Average():F2} bands, {most} fills");
        output.WriteLine("measured : red 0.333 blue 0.336 green 0.332, 10.21 bands, 3 fills");
        output.WriteLine($"arithmetic: 1 + (128 >> 4) = 9 per stripe, so 285 / 27 = {285 / 27.0:F2}");

        Assert.Equal(3, most);
        Assert.InRange(red.Average(), 0.31, 0.35);
        Assert.InRange(blue.Average(), 0.31, 0.35);
        Assert.InRange(green.Average(), 0.31, 0.35);
        Assert.InRange(bands.Average(), 9.5, 11);
    }

    /// <summary>
    /// The stripes march one LED per tick, so a whole pattern comes round every 27 ticks: at full
    /// speed that is 50 ms a tick and 1350 ms a pattern, and the strip gave 1273.
    /// </summary>
    [Fact]
    public void Chase_3_marches_a_whole_pattern_every_twenty_seven_ticks()
    {
        List<byte> first = [];

        Strip.Sample(new TricolorChaseEffect(), Strip.Run(255), 14_000, (frame, _) =>
            first.Add(frame[0].R));

        double level = first.Average(x => (double)x);
        int cycles = first.Zip(first.Skip(1)).Count(p => p.First <= level && p.Second > level);
        double period = cycles > 0 ? 14_000.0 / cycles : 0;

        output.WriteLine($"simulated: {cycles} patterns in 14 s, {period:F0} ms each");
        output.WriteLine("measured : 11 patterns in 14 s, 1273 ms each");
        output.WriteLine("arithmetic: 50 + (255 - 255) * 2 = 50 ms a tick, 27 ticks = 1350 ms");

        Assert.InRange(period, 1150, 1500);
    }

    /// <summary>
    /// The two rainbow chases walk the wheel once per frame drawn rather than once per millisecond,
    /// which makes them two of the few effects whose speed on screen depends on how fast the
    /// controller is managing to draw.
    /// <para>
    /// Worth a test of its own because the frame counter is kept by the strip's service loop and not
    /// by the effect: get that wrong and both of these sit on one color forever, which is a failure
    /// that looks like a working effect with a badly chosen palette.
    /// </para>
    /// </summary>
    [Fact]
    public void The_rainbow_chases_walk_the_wheel_once_per_frame_drawn()
    {
        EffectSegment segment = Strip.Run();
        List<RgbColor> backgrounds = [];

        // Two hundred frames without advancing the clock at all: anything that moves here is moving
        // on the frame count and nothing else.
        for (int i = 0; i < 200; i++)
        {
            segment.Draw(new ChaseRainbowEffect(), 0);
            backgrounds.Add(segment.Pixels[^1]);
        }

        output.WriteLine($"{backgrounds.Distinct().Count()} distinct backgrounds over 200 frames " +
            $"with the clock held still");

        Assert.True(backgrounds.Distinct().Count() > 50,
            "the background should walk the wheel on the frame count alone");
    }
}
