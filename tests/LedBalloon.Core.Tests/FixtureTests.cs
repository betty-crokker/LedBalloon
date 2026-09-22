using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A line on a photo says where a run is, not which way it points or which way it throws light.
/// These pin down both, because getting either wrong makes the preview confidently misleading.
/// </summary>
public class FixtureTests
{
    private static Segment Horizontal(int count = 3, bool reverse = false) => new()
    {
        Count = count,
        Reverse = reverse,
        Path = [new LayoutPoint(0.2, 0.5), new LayoutPoint(0.8, 0.5)],
    };

    [Fact]
    public void A_downlight_aims_down_the_photo_whichever_way_the_run_travels()
    {
        var fixture = new Fixture { Style = FixtureStyle.Downlight };

        // Left to right, then right to left. Both hang under the same eave.
        LayoutPoint rightwards = fixture.AimFrom(new LayoutPoint(1, 0));
        LayoutPoint leftwards = fixture.AimFrom(new LayoutPoint(-1, 0));

        Assert.True(rightwards.Y > 0);
        Assert.True(leftwards.Y > 0);
    }

    [Fact]
    public void An_uplight_aims_up_the_photo_whichever_way_the_run_travels()
    {
        var fixture = new Fixture { Style = FixtureStyle.Uplight };

        Assert.True(fixture.AimFrom(new LayoutPoint(1, 0)).Y < 0);
        Assert.True(fixture.AimFrom(new LayoutPoint(-1, 0)).Y < 0);
    }

    [Fact]
    public void Flipping_the_aim_points_a_downlight_the_other_way()
    {
        var fixture = new Fixture { Style = FixtureStyle.Downlight, FlipAim = true };

        // For the cases where the wall is not below the run, such as a soffit over a porch.
        Assert.True(fixture.AimFrom(new LayoutPoint(1, 0)).Y < 0);
    }

    /// <summary>
    /// The reason "Aim the other way" could be dropped: it never reached anywhere the pair of
    /// styles did not already reach. If this ever stops holding, the checkbox has to come back.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0.6, -0.8)]
    public void Flipping_a_downlight_lands_exactly_on_an_uplight(double dx, double dy)
    {
        var direction = new LayoutPoint(dx, dy);

        LayoutPoint flipped = new Fixture { Style = FixtureStyle.Downlight, FlipAim = true }.AimFrom(direction);
        LayoutPoint uplight = new Fixture { Style = FixtureStyle.Uplight }.AimFrom(direction);

        Assert.Equal(uplight.X, flipped.X, 9);
        Assert.Equal(uplight.Y, flipped.Y, 9);
    }

    [Fact]
    public void An_old_flipped_layout_keeps_aiming_where_it_did()
    {
        var fixture = new Fixture { Style = FixtureStyle.Downlight, FlipAim = true };
        LayoutPoint before = fixture.AimFrom(new LayoutPoint(1, 0));

        fixture.RetireFlipAim();

        LayoutPoint after = fixture.AimFrom(new LayoutPoint(1, 0));

        Assert.Equal(FixtureStyle.Uplight, fixture.Style);
        Assert.False(fixture.FlipAim);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
    }

    [Fact]
    public void Retiring_the_flip_leaves_a_fixture_that_does_not_aim_alone()
    {
        // Nothing ever read it for these, so turning a strip into an uplight would be an invention.
        var fixture = new Fixture { Style = FixtureStyle.DiffusedStrip, FlipAim = true };

        fixture.RetireFlipAim();

        Assert.Equal(FixtureStyle.DiffusedStrip, fixture.Style);
        Assert.False(fixture.FlipAim);
    }

    [Fact]
    public void Only_the_aimed_styles_throw_a_cone()
    {
        Assert.False(new Fixture { Style = FixtureStyle.PointSource }.IsAimed);
        Assert.False(new Fixture { Style = FixtureStyle.DiffusedStrip }.IsAimed);
        Assert.True(new Fixture { Style = FixtureStyle.Downlight }.IsAimed);
        Assert.True(new Fixture { Style = FixtureStyle.Uplight }.IsAimed);
    }

    [Fact]
    public void Led_one_sits_at_the_end_you_drew_first()
    {
        Segment segment = Horizontal();

        Assert.Equal(0.2, segment.PositionOf(0).X, 3);
        Assert.Equal(0.8, segment.PositionOf(2).X, 3);
    }

    [Fact]
    public void Reversing_moves_led_one_to_the_other_end()
    {
        Segment segment = Horizontal(reverse: true);

        Assert.Equal(0.8, segment.PositionOf(0).X, 3);
        Assert.Equal(0.2, segment.PositionOf(2).X, 3);
    }

    [Fact]
    public void Direction_follows_the_drawn_path()
    {
        Segment segment = Horizontal();

        LayoutPoint direction = segment.DirectionAt(0.5);

        Assert.Equal(1.0, direction.X, 3);
        Assert.Equal(0.0, direction.Y, 3);
    }

    [Fact]
    public void Changing_the_fixture_tells_the_segment_something_changed()
    {
        Segment segment = Horizontal();
        var changed = new List<string?>();
        segment.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        segment.Fixture.Style = FixtureStyle.Downlight;

        // The canvas watches segments, not fixtures, so the change has to surface here.
        Assert.Contains(nameof(Segment.Fixture), changed);
    }

    [Fact]
    public void Beam_and_throw_stay_within_something_drawable()
    {
        var fixture = new Fixture
        {
            BeamAngleDegrees = 400,
            ThrowLength = 12,
        };

        Assert.InRange(fixture.BeamAngleDegrees, 5, 170);
        Assert.InRange(fixture.ThrowLength, 0.01, 1.0);
    }
}
