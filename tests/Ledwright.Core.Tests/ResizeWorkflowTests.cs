using Ledwright.Core.Layout;
using Ledwright.Core.Models;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// The workflow the app exists for: a run turns out to be longer than you thought, and the runs
/// after it have to move. These pin down that correcting one number is enough.
/// </summary>
public class ResizeWorkflowTests
{
    private const string South = "704bca414644";

    /// <summary>The real layout this was written against: four runs across 310 LEDs.</summary>
    private static LedwrightProject House() => new()
    {
        Controllers = [new ControllerRef { Key = South, Name = "South" }],
        Segments =
        [
            new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Start = 0, Count = 20 },
            new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Start = 20, Count = 5 },
            new Segment { Id = "roof", Name = "Roofline", ControllerKey = South, Start = 25, Count = 280 },
            new Segment { Id = "spare", Name = "South 4", ControllerKey = South, Start = 305, Count = 5 },
        ],
    };

    [Fact]
    public void Growing_a_run_reports_the_overlap_it_creates()
    {
        LedwrightProject project = House();

        // The roofline is really 285, not 280.
        project.FindSegment("roof")!.Count = 285;

        IReadOnlyList<string> problems = project.Validate(new Dictionary<string, int> { [South] = 310 });

        // It now runs into the spare, and that has to be said before anything is pushed to a device.
        Assert.Contains(problems, p => p.Contains("overlaps") && p.Contains("South 4"));
    }

    [Fact]
    public void Removing_the_spare_and_re_laying_settles_it()
    {
        LedwrightProject project = House();

        project.FindSegment("roof")!.Count = 285;
        project.Segments.Remove(project.FindSegment("spare")!);
        project.Repack(South);

        Assert.Empty(project.Validate(new Dictionary<string, int> { [South] = 310 }));

        Assert.Equal(310, project.TotalLeds);
        Assert.Equal(25, project.FindSegment("roof")!.Start);
        Assert.Equal(310, project.FindSegment("roof")!.StopExclusive);
    }

    [Fact]
    public void Re_laying_shifts_everything_downstream_of_the_correction()
    {
        LedwrightProject project = House();

        // Correct the porch instead, and the two runs after it should move up.
        project.FindSegment("porch")!.Count = 10;
        project.Repack(South);

        Assert.Equal(20, project.FindSegment("porch")!.Start);
        Assert.Equal(30, project.FindSegment("roof")!.Start);
        Assert.Equal(310, project.FindSegment("spare")!.Start);
    }

    [Fact]
    public void A_look_saved_before_the_correction_still_covers_the_longer_run()
    {
        LedwrightProject project = House();

        var look = new Look
        {
            Name = "Warm",
            Segments = { ["roof"] = new SegmentLook { PrimaryHex = "#FF7700" } },
            UnlistedSegmentsOff = false,
        };

        project.FindSegment("roof")!.Count = 285;
        project.Segments.Remove(project.FindSegment("spare")!);
        project.Repack(South);

        WledState state = LookResolver.Resolve(project, look)[South];
        WledSegment roof = state.Segments!.Single(s => s.Start == 25);

        // The look was written before anyone knew the run was 285 long, and it covers it anyway.
        Assert.Equal(310, roof.Stop);
    }

    [Fact]
    public void Each_controller_re_lays_on_its_own()
    {
        const string North = "20e7c86a1be8";

        var project = new LedwrightProject
        {
            Segments =
            [
                new Segment { Id = "a", ControllerKey = South, Start = 0, Count = 100 },
                new Segment { Id = "b", ControllerKey = North, Start = 0, Count = 200 },
            ],
        };

        project.FindSegment("a")!.Count = 150;
        project.Repack(South);

        // The north controller has its own address space and must not shift.
        Assert.Equal(0, project.FindSegment("b")!.Start);
        Assert.Equal(200, project.FindSegment("b")!.Count);
    }
}
