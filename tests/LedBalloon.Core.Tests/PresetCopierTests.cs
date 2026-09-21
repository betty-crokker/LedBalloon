using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Copying a preset to another controller is the whole point of the merged list: the house should
/// be able to show one look even though a preset physically lives on one box. These pin down that
/// the copy takes the appearance from the source and the LED bounds from the target, because
/// getting that backwards would light the wrong part of the house.
/// </summary>
public class PresetCopierTests
{
    private const string North = "20e7c86a1be8";
    private const string South = "704bca414644";

    private static LedBalloonProject TwoControllerHouse() => new()
    {
        Segments =
        [
            new Segment { Name = "North roofline", ControllerKey = North, Start = 0, Count = 200 },
            new Segment { Name = "North eave", ControllerKey = North, Start = 200, Count = 156 },
            new Segment { Name = "South roofline", ControllerKey = South, Start = 0, Count = 180 },
            new Segment { Name = "South eave", ControllerKey = South, Start = 180, Count = 130 },
        ],
    };

    private static WledPreset NorthJuly4th() => new()
    {
        Id = 8,
        Name = "July 4th",
        On = true,
        Brightness = 200,
        Segments =
        [
            new WledSegment
            {
                Id = 0, Start = 0, Stop = 200,
                Effect = 111, Palette = 254, Speed = 128, Intensity = 64,
                Colors = [[255, 0, 0]],
            },
            new WledSegment
            {
                Id = 1, Start = 200, Stop = 356,
                Effect = 0, Palette = 0,
                Colors = [[0, 0, 255]],
            },
        ],
    };

    [Fact]
    public void The_copy_uses_the_target_controllers_own_led_bounds()
    {
        WledPreset copy = PresetCopier.BuildFor(NorthJuly4th(), TwoControllerHouse(), South);

        Assert.Equal(2, copy.Segments!.Count);

        // South's runs, not the 0..200 / 200..356 the preset was saved with on North.
        Assert.Equal(0, copy.Segments[0].Start);
        Assert.Equal(180, copy.Segments[0].Stop);
        Assert.Equal(180, copy.Segments[1].Start);
        Assert.Equal(310, copy.Segments[1].Stop);
    }

    [Fact]
    public void The_copy_keeps_the_effect_palette_and_colors_that_make_it_look_like_itself()
    {
        WledPreset copy = PresetCopier.BuildFor(NorthJuly4th(), TwoControllerHouse(), South);

        Assert.Equal(111, copy.Segments![0].Effect);
        Assert.Equal(254, copy.Segments[0].Palette);
        Assert.Equal((byte)128, copy.Segments[0].Speed);
        Assert.Equal((byte)64, copy.Segments[0].Intensity);
        Assert.Equal(255, copy.Segments[0].Colors![0][0]);

        // Run two keeps its own appearance rather than inheriting run one's.
        Assert.Equal(0, copy.Segments[1].Effect);
        Assert.Equal(255, copy.Segments[1].Colors![0][2]);

        Assert.Equal("July 4th", copy.Name);
        Assert.Equal((byte)200, copy.Brightness);
    }

    [Fact]
    public void A_target_with_more_runs_repeats_the_last_appearance_rather_than_going_dark()
    {
        LedBalloonProject house = TwoControllerHouse();
        house.Segments.Add(new Segment
        {
            Name = "South porch", ControllerKey = South, Start = 310, Count = 40,
        });

        WledPreset copy = PresetCopier.BuildFor(NorthJuly4th(), house, South);

        Assert.Equal(3, copy.Segments!.Count);

        // The third run has no counterpart on North, so it takes the last one described there.
        Assert.Equal(copy.Segments[1].Effect, copy.Segments[2].Effect);
        Assert.Equal(copy.Segments[1].Colors![0][2], copy.Segments[2].Colors![0][2]);
        Assert.Equal(310, copy.Segments[2].Start);
        Assert.Equal(350, copy.Segments[2].Stop);
    }

    [Fact]
    public void Placeholder_segments_in_the_source_are_not_treated_as_appearances()
    {
        WledPreset source = NorthJuly4th();

        // WLED pads the segment array out to the maximum with empty entries.
        source.Segments!.Add(new WledSegment { Id = 2, Stop = 0 });
        source.Segments.Add(new WledSegment { Id = 3, Stop = 0 });

        WledPreset copy = PresetCopier.BuildFor(source, TwoControllerHouse(), South);

        Assert.Equal(2, copy.Segments!.Count);
        Assert.Equal(111, copy.Segments[0].Effect);
        Assert.Equal(0, copy.Segments[1].Effect);
    }

    [Fact]
    public void Copying_to_a_controller_with_no_runs_produces_nothing_to_store()
    {
        WledPreset copy = PresetCopier.BuildFor(NorthJuly4th(), TwoControllerHouse(), "aabbccddeeff");

        Assert.Empty(copy.Segments!);
    }
}
