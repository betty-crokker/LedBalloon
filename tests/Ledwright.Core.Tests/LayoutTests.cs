using Ledwright.Core.Layout;
using Ledwright.Core.Models;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// The layout tests are the important ones: they pin down the behaviour Ledwright exists to provide,
/// which is that correcting a run's length fixes every saved look at once.
/// </summary>
public class LayoutTests
{
    private const string Front = "20e7c86a1be8";
    private const string Garage = "704bca414644";

    private static LedwrightProject TwoSegmentHouse() => new()
    {
        Name = "Test House",
        Controllers = [new ControllerRef { Key = Front, Name = "Front of house" }],
        Segments =
        [
            new Segment { Id = "gable", Name = "Front gable", ControllerKey = Front, Start = 0, Count = 308 },
            new Segment { Id = "porch", Name = "Porch rail", ControllerKey = Front, Start = 308, Count = 48 },
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

        WledState before = LookResolver.Resolve(project, look)[Front];
        Assert.Equal(308, before.Segments![0].Stop);

        // The run turns out to be five LEDs longer than anyone thought.
        project.Segments[0].Count = 313;
        project.Repack(Front);

        WledState after = LookResolver.Resolve(project, look)[Front];

        // The same unchanged look now covers the longer run, and the porch shifted to follow it.
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
            Segments = [new WledSegment { Id = 0, Start = 0, Stop = 308, Effect = 74, Colors = [[255, 0, 0]] }],
        };

        Look captured = LookResolver.Capture(
            project,
            new Dictionary<string, WledState> { [Front] = state },
            "Captured");

        Assert.Equal(74, captured.Segments["gable"].Effect);
        Assert.Equal(new RgbColor(255, 0, 0), captured.Segments["gable"].Primary);
    }

    [Fact]
    public void A_look_spans_every_controller_the_house_is_wired_to()
    {
        var project = new LedwrightProject
        {
            Controllers =
            [
                new ControllerRef { Key = Front, Name = "Front of house" },
                new ControllerRef { Key = Garage, Name = "Garage" },
            ],
            Segments =
            [
                new Segment { Id = "gable", ControllerKey = Front, Start = 0, Count = 308 },
                new Segment { Id = "eaves", ControllerKey = Garage, Start = 0, Count = 120 },
            ],
        };

        var look = new Look
        {
            Segments =
            {
                ["gable"] = new SegmentLook { Primary = RgbColor.White },
                ["eaves"] = new SegmentLook { Primary = RgbColor.White },
            },
        };

        IReadOnlyDictionary<string, WledState> states = LookResolver.Resolve(project, look);

        // One patch per controller, each numbered in its own segment-id space.
        Assert.Equal(2, states.Count);
        Assert.Equal(0, states[Front].Segments![0].Id);
        Assert.Equal(0, states[Garage].Segments![0].Id);
        Assert.Equal(308, states[Front].Segments[0].Stop);
        Assert.Equal(120, states[Garage].Segments[0].Stop);
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

        WledState state = LookResolver.Resolve(project, look)[Front];

        Assert.True(state.Segments![0].On);
        Assert.False(state.Segments[1].On);
    }

    [Fact]
    public void Repack_closes_gaps_left_by_a_corrected_length()
    {
        LedwrightProject project = TwoSegmentHouse();
        project.Segments[0].Count = 100;
        project.Repack(Front);

        Assert.Equal(0, project.Segments[0].Start);
        Assert.Equal(100, project.Segments[1].Start);
        Assert.Equal(148, project.TotalLeds);
    }

    [Fact]
    public void Each_controller_has_its_own_address_space()
    {
        var project = new LedwrightProject
        {
            Segments =
            [
                new Segment { Id = "a", ControllerKey = Front, Start = 0, Count = 100 },
                new Segment { Id = "b", ControllerKey = Garage, Start = 0, Count = 100 },
            ],
        };

        // Two runs both starting at LED 0 is correct when they are on different boxes.
        Assert.Empty(project.Validate());
        Assert.Equal(200, project.TotalLeds);
    }

    [Fact]
    public void Validate_reports_overlaps_and_runs_past_the_end_of_the_strip()
    {
        var project = new LedwrightProject
        {
            Controllers = [new ControllerRef { Key = Front, Name = "Front" }],
            Segments =
            [
                new Segment { Id = "a", Name = "A", ControllerKey = Front, Start = 0, Count = 100 },
                new Segment { Id = "b", Name = "B", ControllerKey = Front, Start = 50, Count = 100 },
            ],
        };

        IReadOnlyList<string> problems = project.Validate(new Dictionary<string, int> { [Front] = 120 });

        Assert.Contains(problems, p => p.Contains("overlaps"));
        Assert.Contains(problems, p => p.Contains("drives 120"));
    }

    [Fact]
    public void A_segment_with_no_controller_is_reported()
    {
        var project = new LedwrightProject
        {
            Segments = [new Segment { Id = "orphan", Name = "Nowhere", Start = 0, Count = 10 }],
        };

        Assert.Contains(project.Validate(), p => p.Contains("not assigned to a controller"));
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

    [Fact]
    public void A_controller_keeps_the_name_you_gave_it()
    {
        var project = new LedwrightProject();

        project.RegisterController(Front, "192.168.0.102", "wled-6a1be8.local", "WLED-Gledopto");
        project.FindController(Front)!.Name = "Front of house";

        // Rediscovery must not undo the rename, even though the device still calls itself Gledopto.
        project.RegisterController(Front, "192.168.0.150", "wled-6a1be8.local", "WLED-Gledopto");

        ControllerRef stored = project.FindController(Front)!;
        Assert.Equal("Front of house", stored.Name);
        Assert.Equal("192.168.0.150", stored.LastHost);
    }
}
