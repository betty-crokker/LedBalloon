using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A controller has two frame times and they are not the same number, which this pins against the
/// strip because using one where the other belongs is a quiet and expensive mistake.
/// <para>
/// One is how often a frame actually gets drawn - 9 ms on the south roofline, measured three
/// separate ways. The other is WLED's <c>FRAMETIME</c> macro, which comes from the configured frame
/// rate and is 2 ms here, because both controllers have that rate set to unlimited. A handful of
/// effects use the second as a constant in their own arithmetic, and at 9 ms they run four and a
/// half times too slow.
/// </para>
/// <para>
/// Worth pinning to hardware rather than to the source, because the source alone does not say which
/// number it is: <c>FRAMETIME</c> is a macro that reads a field, and the field depends on a setting
/// on the controller.
/// </para>
/// </summary>
public class FrameTimeAgainstHardwareTests(ITestOutputHelper output)
{
    private static (double OnFraction, double BlinksPerSecond) Blinking(byte speed, uint milliseconds)
    {
        List<bool> on = [];

        Strip.Sample(new BlinkEffect(), Strip.Run(speed), milliseconds, (frame, _) =>
            on.Add(frame[0].R > frame[0].B));

        int flips = on.Zip(on.Skip(1)).Count(p => p.First != p.Second);

        return (
            (double)on.Count(x => x) / on.Count,
            flips / 2.0 / (milliseconds / 1000.0));
    }

    /// <summary>
    /// At the top of the speed slider Blink's whole cycle is <c>FRAMETIME * 2</c>, which makes it
    /// the sharpest test there is of which constant is right.
    /// <para>
    /// Two milliseconds gives a four millisecond cycle, far shorter than the nine a frame takes -
    /// so the rule that forces it on for one frame at the start of every cycle fires every single
    /// frame and it never goes dark at all. The frame interval instead would give a 46 ms cycle,
    /// which is about 60% duty blinking at ten hertz.
    /// </para>
    /// <para>
    /// The strip stayed solidly on: 180 frames over ten seconds without one dark frame between them.
    /// </para>
    /// </summary>
    [Fact]
    public void Blink_at_full_speed_never_goes_dark()
    {
        (double onFraction, double rate) = Blinking(255, 10_000);

        output.WriteLine($"simulated: on {onFraction:F3} of frames, {rate:F2} blinks a second");
        output.WriteLine("measured : on 1.000 of frames, 0.00 blinks a second");
        output.WriteLine("the frame interval instead would give about 0.6 and 10 blinks a second");

        Assert.Equal(1.0, onFraction, 3);
        Assert.Equal(0, rate, 2);
    }

    /// <summary>
    /// A speed the live preview can actually resolve, where the two constants still differ by a
    /// fifth: a 204 ms cycle against a 246 ms one.
    /// <para>
    /// The first attempt at this used speed 250, where the cycle is 104 ms - and the preview sends a
    /// frame every 56 ms, so the measurement was under Nyquist and came back at 7.1 blinks a second
    /// where the truth was 9.6. It agreed with the wrong constant. Dropping to a rate the sampling
    /// could carry gave 4.88, against 4.90 for two milliseconds and 4.07 for the interval.
    /// </para>
    /// </summary>
    [Fact]
    public void Blink_near_full_speed_runs_at_the_rate_a_two_millisecond_constant_gives()
    {
        (double onFraction, double rate) = Blinking(245, 24_000);

        output.WriteLine($"simulated: on {onFraction:F3} of frames, {rate:F2} blinks a second");
        output.WriteLine("measured : on 0.498 of frames, 4.88 blinks a second");
        output.WriteLine("arithmetic: (255 - 245) * 20 + 2 * 2 = 204 ms, so 4.90 a second");
        output.WriteLine("            the frame interval would give 246 ms, so 4.07 a second");

        Assert.InRange(onFraction, 0.45, 0.55);
        Assert.InRange(rate, 4.5, 5.3);
    }

    /// <summary>
    /// The constant is a setting rather than a property of the hardware, so it is read from the
    /// controller. Unlimited is what both of these are on and what the 2 ms comes from.
    /// </summary>
    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 2)]
    [InlineData(42, 23)]
    [InlineData(60, 16)]
    public void The_constant_comes_from_the_configured_frame_rate(int? configured, int expected) =>
        Assert.Equal(expected, FrameTime.Constant(configured));

    /// <summary>
    /// And it is a different number from the interval, which is what the two properties exist to
    /// keep apart.
    /// </summary>
    [Fact]
    public void The_two_frame_times_are_separate_settings_on_a_run()
    {
        var segment = new EffectSegment(10) { FrameMilliseconds = 9, FrameTime = 2 };

        Assert.Equal(9, segment.FrameMilliseconds);
        Assert.Equal(2, segment.FrameTime);

        // The default is the unlimited case, which is what both controllers here report.
        Assert.Equal(FrameTime.MinimumFrameDelay, new EffectSegment(10).FrameTime);
    }
}
