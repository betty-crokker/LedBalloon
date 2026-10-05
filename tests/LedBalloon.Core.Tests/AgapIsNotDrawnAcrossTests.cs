using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Where a run jumps, said in the terms a drawn line needs.
/// <para>
/// A rope light is drawn by sampling the path at even fractions and joining consecutive samples,
/// rather than by walking its points — the whole point of diffusion is that you cannot see where
/// one LED ends and the next begins. So skipping the gap when walking points was not enough: the
/// one piece whose two samples sat either side of a gap still drew a glowing line across it, and
/// two rails under a set of stairs looked like one rail through the landing.
/// </para>
/// </summary>
public class AgapIsNotDrawnAcrossTests
{
    /// <summary>Two equal stretches, 0.0-0.1 and 0.9-1.0, with the jump between them.</summary>
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
    public void The_jump_falls_halfway_along_two_equal_stretches()
    {
        double jump = Assert.Single(Stairs().BreakFractions());

        // The gap has no length, so it is a single fraction rather than a span - and with the two
        // stretches the same length it lands exactly in the middle.
        Assert.Equal(0.5, jump, 6);
    }

    [Fact]
    public void Aline_in_one_piece_has_nowhere_to_stop()
    {
        Segment joined = Stairs();
        joined.Breaks = [];

        Assert.Empty(joined.BreakFractions());
    }

    /// <summary>
    /// Uneven stretches put the jump where the shorter one ends, not at the midpoint.
    /// </summary>
    [Fact]
    public void The_jump_falls_where_the_first_stretch_ends()
    {
        var uneven = new Segment
        {
            Id = "stairs", Name = "Under stairs", ControllerKey = "north",
            Output = 2, Count = 48, SegmentId = 1,
            Path =
            [
                new LayoutPoint(0.0, 0.0),
                new LayoutPoint(0.1, 0.0),
                new LayoutPoint(0.5, 0.0),
                new LayoutPoint(0.8, 0.0),
            ],
            Breaks = [1],
        };

        // Lit lengths are 0.1 and 0.3, so the first stretch is a quarter of the run.
        Assert.Equal(0.25, Assert.Single(uneven.BreakFractions()), 6);
    }

    /// <summary>
    /// And two gaps give two places to stop, in the order they occur along the run.
    /// </summary>
    [Fact]
    public void Two_gaps_give_two_places_to_stop()
    {
        var threeWay = new Segment
        {
            Id = "rail", Name = "Rail", ControllerKey = "north",
            Output = 1, Count = 30, SegmentId = 0,
            Path =
            [
                new LayoutPoint(0.0, 0.0),
                new LayoutPoint(0.1, 0.0),
                new LayoutPoint(0.4, 0.0),
                new LayoutPoint(0.5, 0.0),
                new LayoutPoint(0.8, 0.0),
                new LayoutPoint(0.9, 0.0),
            ],
            Breaks = [1, 3],
        };

        IReadOnlyList<double> jumps = threeWay.BreakFractions();

        Assert.Equal(2, jumps.Count);
        Assert.Equal(1d / 3d, jumps[0], 6);
        Assert.Equal(2d / 3d, jumps[1], 6);
    }
}
