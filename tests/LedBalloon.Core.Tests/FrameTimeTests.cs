using LedBalloon.Core.Effects;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The frame time is what anything fading, trailing or decaying is measured in, and the configured
/// frame rate is not it. Both controllers here are set to 42 fps and neither runs at it.
/// </summary>
public class FrameTimeTests
{
    /// <summary>
    /// Measured with an animating effect on each strip: South reports 107 fps at 310 LEDs, North
    /// 96 at 356. The estimate has to land on those from the LED count alone, because the lights
    /// are off exactly when a preview is being looked at.
    /// </summary>
    [Theory]
    [InlineData(310, 9.35)]     // South, reported 107 fps
    [InlineData(356, 10.36)]    // North, reported 96 fps
    public void The_estimate_lands_on_what_the_strips_actually_do(int leds, double measuredMs)
    {
        int estimate = FrameTime.EstimateMilliseconds(leds);

        Assert.InRange(estimate, measuredMs * 0.9, measuredMs * 1.1);
    }

    /// <summary>
    /// If the outputs were clocked in parallel the longest alone would set the pace. North's
    /// longest is 308, which would predict 108 fps against the 96 it reports, so they are not.
    /// </summary>
    [Fact]
    public void The_whole_strip_is_paid_for_not_the_longest_output()
    {
        int whole = FrameTime.EstimateMilliseconds(356);
        int longestOutputOnly = FrameTime.EstimateMilliseconds(308);

        Assert.Equal(11, whole);
        Assert.Equal(9, longestOutputOnly);
    }

    /// <summary>
    /// WLED does not re-clock an unchanged frame, so a static effect reports a rate that says
    /// nothing about the strip. North sat at 15 fps showing Solid and jumped to 96 when something
    /// moved; believing the 15 would make every trail four times too long.
    /// </summary>
    [Theory]
    [InlineData(107, true)]
    [InlineData(96, true)]
    [InlineData(42, true)]
    [InlineData(30, true)]
    [InlineData(15, false)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(null, false)]
    public void A_rate_is_only_believed_when_something_is_moving(int? reported, bool believable)
    {
        Assert.Equal(believable, FrameTime.IsAnimating(reported));
    }

    [Fact]
    public void A_controller_that_reports_nothing_still_gets_a_workable_number()
    {
        Assert.Equal(1, FrameTime.EstimateMilliseconds(0));
        Assert.Equal(1, FrameTime.EstimateMilliseconds(-5));
        Assert.Equal(23, FrameTime.FromFps(42));
        Assert.Equal(9, FrameTime.FromFps(107));

        // Only ever asked about a rate that passed IsAnimating, so zero is a guard rather than a
        // case: it clamps the divisor instead of dividing by it.
        Assert.Equal(1000, FrameTime.FromFps(0));
    }
}
