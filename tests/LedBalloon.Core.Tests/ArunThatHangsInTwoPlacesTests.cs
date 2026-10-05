using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// One electrical run drawn as two separate stretches on the photo.
/// <para>
/// A house does not always hang a run in one line. Forty-eight LEDs under a set of stairs can be
/// two rails with a landing between them: one segment as far as the controller is concerned, two
/// separate stretches as far as anybody looking at the house is concerned. Drawn as a single
/// polyline, the LEDs were spread evenly along the jump as well, so a share of them appeared
/// hanging in the air between the two rails — and the dashed outline drew a line through it.
/// </para>
/// </summary>
public class ArunThatHangsInTwoPlacesTests
{
    /// <summary>
    /// Two stretches of equal length with a long jump between them. Four points: 0-1 is the first
    /// rail, 1-2 is the jump, 2-3 is the second.
    /// </summary>
    private static Segment Stairs() => new()
    {
        Id = "stairs", Name = "Under stairs", ControllerKey = "north",
        Output = 2, Count = 48, SegmentId = 1,
        Path =
        [
            new LayoutPoint(0.0, 0.5),
            new LayoutPoint(0.1, 0.5),
            new LayoutPoint(0.9, 0.5),
            new LayoutPoint(1.0, 0.5),
        ],
        Breaks = [1],
    };

    [Fact]
    public void No_led_is_placed_in_the_gap()
    {
        Segment stairs = Stairs();

        for (int led = 0; led < stairs.Count; led++)
        {
            double x = stairs.PositionOf(led).X;

            // Everything lands on one rail or the other, never between them.
            Assert.True(
                x <= 0.1000001 || x >= 0.8999999,
                $"LED {led} landed at {x}, which is in the gap.");
        }
    }

    [Fact]
    public void The_leds_split_evenly_between_the_two_stretches()
    {
        Segment stairs = Stairs();

        int first = 0;
        int second = 0;

        for (int led = 0; led < stairs.Count; led++)
        {
            if (stairs.PositionOf(led).X <= 0.1000001)
            {
                first++;
            }
            else
            {
                second++;
            }
        }

        // The two stretches are the same length, so they take the same share - which is what laying
        // them end to end would give, and that is electrically what they are.
        Assert.InRange(first, 23, 25);
        Assert.InRange(second, 23, 25);
    }

    /// <summary>
    /// Without the break the same path spreads a sixth of the run across the jump, which is the
    /// behaviour this replaced.
    /// </summary>
    [Fact]
    public void Without_a_break_the_same_path_fills_the_gap()
    {
        Segment joined = Stairs();
        joined.Breaks = [];

        int between = 0;

        for (int led = 0; led < joined.Count; led++)
        {
            double x = joined.PositionOf(led).X;

            if (x is > 0.1000001 and < 0.8999999)
            {
                between++;
            }
        }

        Assert.True(between > 0, "the gap should be filled when nothing says it is a gap");
    }

    /// <summary>
    /// A file written before breaks existed has none, which is the same as a run in one piece.
    /// </summary>
    [Fact]
    public void Apath_with_no_breaks_behaves_exactly_as_before()
    {
        var plain = new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = "south",
            Output = 1, Count = 5, SegmentId = 0,
            Path = [new LayoutPoint(0, 0), new LayoutPoint(1, 0)],
        };

        Assert.Empty(plain.Breaks);
        Assert.Equal(0, plain.PositionOf(0).X);
        Assert.Equal(1, plain.PositionOf(4).X);
    }
}
