using Ledwright.Core.Layout;
using Ledwright.Core.Models;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// The layout tests are the important ones: they pin down the behaviour Ledwright exists to provide,
/// which is that correcting a segment's length fixes every saved look at once.
/// </summary>
public class LayoutTests
{
    private static LedwrightProject TwoSegmentHouse() => new()
    {
        Name = "Test House",
        Segments =
        [
            new Segment { Id = "gable", Name = "Front gable", Start = 0, Count = 308 },
            new Segment { Id = "porch", Name = "Porch rail", Start = 308, Count = 48 },
        ],
    };

    [Fact]
    public void Resolving_a_look_takes_bounds_from_the_current_layout()
    {
        LedwrightProject project = TwoSegmentHouse();
        var look = new Look
        {
            Name = "Warm",
            Segments = { ["gable"] = new SegmentLook { Primary = new RgbColor(255, 120, 0) } },
        };

        WledState before = LookResolver.Resolve(project, look);
        Assert.Equal(308, before.Segments![0].Stop);

        // The run turns out to be five LEDs longer than anyone thought.
        project.Segments[0].Count = 313;
        project.Repack();

        WledState after = LookResolver.Resolve(project, look);

        // The same unchanged look now covers the longer run, and the porch has shifted to follow it.
        Assert.Equal(313, after.Segments![0].Stop);
        Assert.Equal(313, after.Segments[1].Start);
        Assert.Equal(361, after.Segments[1].Stop);
    }

    [Fact]
    public void A_look_stores_no_led_indices_at_all()
    {
        LedwrightProject project = TwoSegmentHouse();
        var state = new WledState
        {
            On = true,
            Brightness = 200,
            Segments =
            [
                new WledSegment { Id = 0, Start = 0, Stop = 308, Effect = 74, Colors = [[255, 0, 0]] },
            ],
        };

        Look captured = LookResolver.Capture(project, state, "Captured");

        // Whatever the capture kept, it is not geometry: that belongs to the project.
        Assert.Equal(74, captured.Segments["gable"].Effect);
        Assert.Equal(new RgbColor(255, 0, 0), captured.Segments["gable"].Primary);
    }

    [Fact]
    public void Unlisted_segments_are_switched_off_so_a_look_is_a_complete_scene()
    {
        LedwrightProject project = TwoSegmentHouse();
        var look = new Look
        {
            Segments = { ["gable"] = new SegmentLook { Primary = RgbColor.White } },
            UnlistedSegmentsOff = true,
        };

        WledState state = LookResolver.Resolve(project, look);

        Assert.True(state.Segments![0].On);
        Assert.False(state.Segments[1].On);
    }

    [Fact]
    public void Repack_closes_gaps_left_by_a_corrected_length()
    {
        LedwrightProject project = TwoSegmentHouse();
        project.Segments[0].Count = 100;
        project.Repack();

        Assert.Equal(0, project.Segments[0].Start);
        Assert.Equal(100, project.Segments[1].Start);
        Assert.Equal(148, project.TotalLeds);
    }

    [Fact]
    public void Validate_reports_overlaps_and_runs_past_the_end_of_the_strip()
    {
        var project = new LedwrightProject
        {
            Segments =
            [
                new Segment { Id = "a", Name = "A", Start = 0, Count = 100 },
                new Segment { Id = "b", Name = "B", Start = 50, Count = 100 },
            ],
        };

        IReadOnlyList<string> problems = project.Validate(deviceLedCount: 120);

        Assert.Contains(problems, p => p.Contains("overlaps"));
        Assert.Contains(problems, p => p.Contains("controller reports 120"));
    }

    [Fact]
    public void A_segment_spreads_its_leds_evenly_along_the_drawn_line()
    {
        var segment = new Segment
        {
            Count = 3,
            Path = [new LayoutPoint(0, 0), new LayoutPoint(1, 0)],
        };

        Assert.Equal(0.0, segment.PositionOf(0).X, 3);
        Assert.Equal(0.5, segment.PositionOf(1).X, 3);
        Assert.Equal(1.0, segment.PositionOf(2).X, 3);
    }

    [Fact]
    public void A_reversed_segment_runs_the_other_way_along_the_same_line()
    {
        var segment = new Segment
        {
            Count = 3,
            Reverse = true,
            Path = [new LayoutPoint(0, 0), new LayoutPoint(1, 0)],
        };

        Assert.Equal(1.0, segment.PositionOf(0).X, 3);
        Assert.Equal(0.0, segment.PositionOf(2).X, 3);
    }
}
