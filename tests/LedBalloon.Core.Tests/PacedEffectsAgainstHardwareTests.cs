using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The effects that ask not to be drawn every frame, captured on the south controller's 285 LED
/// roofline with the color slots set to red, blue and green.
/// <para>
/// WLED's effects each return how long they want before the next frame, and almost all of them
/// return "as soon as you can". These do not, and for them it is the effect rather than a detail:
/// ICU sits still for a second or three between movements, and Chase Flash holds a flash for 20 ms
/// against a 30 ms gap.
/// </para>
/// </summary>
public class PacedEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor Background = new(0, 0, 255);

    /// <summary>
    /// The one measurement that settles whether the delays are honored at all.
    /// <para>
    /// Chase Flash's pair advances one LED per nine-phase cycle, so how far it travels in a given
    /// time reads the cycle length directly - and unlike counting the flashes themselves, it is slow
    /// enough for the live preview to sample honestly. The requested delays make a cycle 223 ms and
    /// put the pair 4.5 LEDs along a second; drawing every frame regardless would make it 85 ms and
    /// 11.7 LEDs a second.
    /// </para>
    /// <para>
    /// The strip travelled 3.73 LEDs a second. That is the delay-honoring regime and nowhere near
    /// the other one, but it is also 17% slower than the requested delays alone predict - the
    /// controller evidently spends a few milliseconds per frame over and above what the effect asked
    /// for. This simulation uses the requested delays as written, so it runs that much fast.
    /// Recorded rather than fudged: one data point is not enough to model an overhead, and the
    /// assertion below is on the regime rather than on the rate.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_Flash_advances_at_the_rate_its_own_frame_delays_ask_for()
    {
        List<int> spots = [];

        Strip.Sample(new ChaseFlashEffect(), Strip.Run(), 30_000, (frame, _) =>
        {
            int at = Array.FindIndex(frame, p => p.B > p.R && p.B > p.G);

            if (at >= 0)
            {
                spots.Add(at);
            }
        });

        int advance = 0;

        for (int i = 1; i < spots.Count; i++)
        {
            int step = spots[i] - spots[i - 1];

            if (step < -Strip.Roofline / 2)
            {
                step += Strip.Roofline;
            }
            else if (step > Strip.Roofline / 2)
            {
                step -= Strip.Roofline;
            }

            advance += step;
        }

        double perSecond = Math.Abs(advance) / 30.0;

        output.WriteLine($"simulated: {Math.Abs(advance)} LEDs in 30 s, {perSecond:F2} a second");
        output.WriteLine("measured : 112 LEDs in 30 s, 3.73 a second");
        output.WriteLine("arithmetic: 4 * 20 + 4 * 30 + 23 = 223 ms a cycle, so 4.48 a second");
        output.WriteLine("            ignoring the delays would give 85 ms and 11.7 a second");

        // The delay-honoring regime, with room for the overhead the strip shows and none for the
        // three-times-faster alternative.
        Assert.InRange(perSecond, 3.0, 6.0);
    }

    /// <summary>
    /// The flash itself: two background-colored LEDs, and never any other number of them.
    /// <para>
    /// The strip showed a flash on 42.8% of its frames and this shows 36.3%, which is what the
    /// requested delays predict - equal frames would give 44.4%. At 784 samples the standard error is
    /// 1.8 points, so the strip is three of them above the arithmetic and one and a half below the
    /// alternative: this measurement leans the wrong way and does <em>not</em> confirm the flash
    /// timing on its own.
    /// </para>
    /// <para>
    /// The likely reason is the same overhead the travel rate shows. A few milliseconds added to
    /// every frame regardless of what was asked for pushes a 20 ms flash and a 30 ms gap closer
    /// together, and so pushes the share of time the flash is up from 36% toward 44%. That would
    /// explain both numbers at once, but it is one fitted quantity from two measurements, so it is
    /// written down rather than built in.
    /// </para>
    /// <para>
    /// What this test does pin is the shape: exactly two LEDs flash and never any other number, and
    /// they are the secondary color over a run of palette.
    /// </para>
    /// </summary>
    [Fact]
    public void Chase_Flash_flashes_exactly_two_leds_over_a_palette_run()
    {
        List<int> flashing = [];

        Strip.Sample(new ChaseFlashEffect(), Strip.Run(), 16_000, (frame, _) =>
            flashing.Add(frame.Count(p => p.B > p.R && p.B > p.G)));

        double share = (double)flashing.Count(c => c > 0) / flashing.Count;

        output.WriteLine($"simulated: {share:F3} of frames flashing, " +
            $"counts seen {string.Join(", ", flashing.Distinct().Order())}");
        output.WriteLine("measured : 0.428 of frames flashing, counts seen 0, 2");

        Assert.All(flashing, c => Assert.True(c is 0 or 2, $"{c} LEDs flashing, expected 0 or 2"));
        Assert.InRange(share, 0.25, 0.55);
    }

    /// <summary>
    /// ICU lights two LEDs while it sits still and four while it moves, because a moving frame draws
    /// the pair it is leaving as well as the pair it is arriving at.
    /// <para>
    /// The strip showed 0, 2 or 4 and never 1 or 3: 1.1% dark, which are the blinks, 38% still, 60%
    /// moving. The two eyes are always exactly 15 LEDs apart, which is
    /// <c>length / ((intensity &gt;&gt; 3) + 2)</c>, and they roam between 23 and 269 where the
    /// random draw allows 0 to 269.
    /// </para>
    /// <para>
    /// No blinks here, and that is not a mismatch: a blink is one chance in six per arrival, an
    /// arrival takes about seven seconds, and the draws come from this run's own seeded generator
    /// rather than the controller's. Thirty seconds is four arrivals. The blink is checked over two
    /// minutes in the test below instead.
    /// </para>
    /// </summary>
    [Fact]
    public void ICU_lights_two_while_it_waits_and_four_while_it_moves()
    {
        List<int> counts = [];
        List<int> apart = [];
        List<int> places = [];

        Strip.Sample(new IcuEffect(), Strip.Run(), 30_000, (frame, _) =>
        {
            int[] on = [.. Enumerable.Range(0, frame.Length).Where(i => frame[i] != Background)];

            counts.Add(on.Length);

            if (on.Length == 2)
            {
                apart.Add(on[1] - on[0]);
                places.Add(on[0]);
            }
        });

        output.WriteLine($"simulated: counts {string.Join(", ", counts.Distinct().Order())}; " +
            $"dark {counts.Count(c => c == 0) * 100.0 / counts.Count:F1}%, " +
            $"still {counts.Count(c => c == 2) * 100.0 / counts.Count:F1}%, " +
            $"moving {counts.Count(c => c == 4) * 100.0 / counts.Count:F1}%");
        output.WriteLine("measured : counts 0, 2, 4; dark 1.1%, still 38.4%, moving 60.4%");
        output.WriteLine($"simulated: eyes {apart.Min()}-{apart.Max()} apart, " +
            $"roaming {places.Min()}-{places.Max()}");
        output.WriteLine("measured : eyes 15-15 apart, roaming 23-269");

        Assert.All(counts, c => Assert.True(c is 0 or 2 or 4, $"{c} lit, expected 0, 2 or 4"));

        // 285 / ((128 >> 3) + 2) = 15, and it never varies.
        Assert.Equal(15, apart.Min());
        Assert.Equal(15, apart.Max());

        // It spends real time standing still, which only the frame delay can produce.
        Assert.InRange(counts.Count(c => c == 2) / (double)counts.Count, 0.2, 0.6);
    }

    /// <summary>
    /// The pauses are the point, so this checks they are pauses rather than slow movement: ICU asks
    /// for a second to three seconds between movements, and 200 ms for a blink.
    /// <para>
    /// Watched over two minutes rather than twenty seconds, because a blink is one chance in six each
    /// time the eyes arrive somewhere and an arrival takes seven seconds. Twenty seconds is three
    /// arrivals, which is even odds of seeing no blink at all - and the first version of this test
    /// duly saw none and failed.
    /// </para>
    /// </summary>
    [Fact]
    public void ICU_asks_for_seconds_at_a_time_between_movements()
    {
        EffectSegment segment = Strip.Run();
        var effect = new IcuEffect();

        List<int> asked = [];

        for (uint t = 0; t < 120_000; t += (uint)Math.Max(Strip.FrameMs, segment.FrameDelay))
        {
            segment.Draw(effect, t);
            asked.Add(segment.FrameDelay);
        }

        output.WriteLine($"delays asked for: {asked.Min()} to {asked.Max()} ms over {asked.Count} frames");
        output.WriteLine($"{asked.Count(d => d >= 1000)} of them a second or more, " +
            $"{asked.Count(d => d == 200)} of them the 200 ms blink");

        // The step delay is SPEED_FORMULA_L, which is 27 ms on this run.
        Assert.Equal(5 + (50 * 127 / Strip.Roofline), asked.Min());

        // And the long ones are the reposes: 1000 plus up to 2000 more.
        Assert.InRange(asked.Max(), 1000, 3000);
        Assert.True(asked.Any(d => d == 200), "it should blink now and then");
    }

    /// <summary>
    /// Android grows a block from one end, slides it round and shrinks it again, moving on the frame
    /// count rather than on the clock.
    /// <para>
    /// The block reaches <c>intensity * length / 255</c> = 143, and the strip peaked at 144 and got
    /// down to 1, averaging 26.4% of the run. It is the one effect here whose frame delay makes no
    /// difference on this roofline: <c>3 + 8 * 127 / 285</c> is 6 ms, shorter than a frame. On a ten
    /// LED run the same formula gives 104 ms, which is the point of it.
    /// </para>
    /// </summary>
    [Fact]
    public void Android_grows_and_shrinks_a_block_on_the_frame_count()
    {
        List<int> lit = [];

        Strip.Sample(new AndroidEffect(), Strip.Run(), 14_000, (frame, _) =>
            lit.Add(frame.Count(p => p != Background)));

        var wide = new EffectSegment(Strip.Roofline) { Speed = 128 };
        var narrow = new EffectSegment(10) { Speed = 128 };

        wide.Draw(new AndroidEffect(), 0);
        narrow.Draw(new AndroidEffect(), 0);

        output.WriteLine($"simulated: lit {lit.Average() / Strip.Roofline:F3} " +
            $"({lit.Min()}-{lit.Max()} of {Strip.Roofline})");
        output.WriteLine("measured : lit 0.264 (1-144 of 285)");
        output.WriteLine($"asks for {wide.FrameDelay} ms on 285 LEDs and {narrow.FrameDelay} ms on 10");

        Assert.InRange(lit.Max(), 135, 150);
        Assert.InRange(lit.Average() / Strip.Roofline, 0.20, 0.32);

        // Short runs are slowed right down so the block does not race round them.
        Assert.InRange(wide.FrameDelay, 4, 8);
        Assert.InRange(narrow.FrameDelay, 90, 120);
    }

    /// <summary>
    /// Solid asks for a third of a second between frames, which changes nothing on the wall and
    /// everything about what the controller has time for.
    /// </summary>
    [Fact]
    public void Solid_asks_to_be_drawn_three_times_a_second()
    {
        var segment = new EffectSegment(10);
        segment.Draw(new SolidEffect(), 0);

        Assert.Equal(SolidEffect.RefreshMilliseconds, segment.FrameDelay);

        // And an effect that asked once does not keep it: the next frame starts from the default.
        segment.Draw(new BreatheEffect(), 350);
        Assert.Equal(segment.FrameTime, segment.FrameDelay);
    }
}
