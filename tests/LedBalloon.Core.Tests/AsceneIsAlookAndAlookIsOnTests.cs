using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Whether a scene carries the house switch, and what happens on a timer when it does.
/// <para>
/// It did, and the answer was found outside at a quarter past five in the morning. "Stairs white"
/// was arranged while the house was dark — which is when anybody sits down to arrange one — so it
/// recorded the switch as off along with everything else. Published, it became a preset beginning
/// <c>"on": false</c>. The 23:00 timer fired it, WLED set every segment exactly right and then
/// switched the house off, and the stairs stayed dark all night with nothing anywhere reporting a
/// fault. The controller held <c>ps: 3</c> and <c>on: false</c>: it had done as it was told.
/// </para>
/// <para>
/// So a scene is a look, and a look is on. Making the house go dark on a timetable is the switch,
/// which is a line in the timetable and not a scene.
/// </para>
/// </summary>
public class AsceneIsAlookAndAlookIsOnTests
{
    private const string North = TestHouse.North;

    private static LedBalloonProject House() => new()
    {
        Controllers = [new ControllerRef { Key = North, Name = "North" }],
        Segments =
        [
            new Segment
            {
                Id = "stairs", Name = "Under stairs", ControllerKey = North,
                Output = 1, Count = 48, SegmentId = 0,
            },
        ],
    };

    /// <summary>The house as it is when somebody arranges a scene: dark.</summary>
    private static WledState DarkHouse() => new()
    {
        On = false,
        Brightness = 248,
        Segments =
        [
            new WledSegment { Id = 0, Start = 0, Stop = 48, On = true, Effect = 0 },
        ],
    };

    [Fact]
    public void Ascene_captured_with_the_lights_off_is_still_on()
    {
        LedBalloonProject project = House();

        Scene scene = SceneResolver.Capture(
            project,
            new Dictionary<string, WledState> { [North] = DarkHouse() },
            "Stairs white");

        Assert.True(scene.On);
    }

    /// <summary>
    /// And the preset a timer fires says so, which is the half that was actually observed.
    /// </summary>
    [Fact]
    public void The_published_preset_turns_the_house_on()
    {
        LedBalloonProject project = House();

        var captured = new Scene { Name = "Stairs white", On = false };
        captured.Segments["stairs"] = new SceneEntry
        {
            On = true,
            Effect = 0,
            PrimaryHex = "#FFFFFF",
        };

        WledPreset preset = ScenePublisher.BuildFor(project, captured, North);

        Assert.True(preset.On);
    }

    /// <summary>
    /// And applying one from the app does too, so the app and the timer cannot disagree about it.
    /// </summary>
    [Fact]
    public void Applying_one_from_the_app_turns_the_house_on()
    {
        LedBalloonProject project = House();

        var captured = new Scene { Name = "Stairs white", On = false };
        captured.Segments["stairs"] = new SceneEntry { On = true, Effect = 0, PrimaryHex = "#FFFFFF" };

        WledState sent = SceneResolver.ResolveFor(project, captured, North);

        Assert.True(sent.On);
    }

    /// <summary>
    /// A controller the scene says nothing about still keeps its switch, which is the thing this
    /// must not break. South's "Off" preset carries on:true with the brightness at zero, and
    /// applying it used to switch North on.
    /// </summary>
    [Fact]
    public void Acontroller_the_scene_says_nothing_about_keeps_its_switch()
    {
        const string South = TestHouse.South;

        var project = new LedBalloonProject
        {
            Controllers =
            [
                new ControllerRef { Key = North, Name = "North" },
                new ControllerRef { Key = South, Name = "South" },
            ],
            Segments =
            [
                new Segment
                {
                    Id = "stairs", Name = "Under stairs", ControllerKey = North,
                    Output = 1, Count = 48, SegmentId = 0,
                },
                new Segment
                {
                    Id = "garage", Name = "Garage", ControllerKey = South,
                    Output = 1, Count = 20, SegmentId = 0,
                },
            ],
        };

        // Not accounting for everything, so it genuinely says nothing about South.
        var scene = new Scene { Name = "Stairs white", On = true, UnlistedSegmentsOff = false };
        scene.Segments["stairs"] = new SceneEntry { On = true, Effect = 0, PrimaryHex = "#FFFFFF" };

        Assert.Null(SceneResolver.ResolveFor(project, scene, South).On);
    }

    /// <summary>
    /// A segment the scene turns off still goes off: that is a fact about the segment, and it is
    /// the whole point of a scene that lights the stairs and leaves the porch dark.
    /// </summary>
    [Fact]
    public void Asegment_the_scene_leaves_dark_is_still_dark()
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = North, Name = "North" }],
            Segments =
            [
                new Segment
                {
                    Id = "stairs", Name = "Under stairs", ControllerKey = North,
                    Output = 1, Count = 48, SegmentId = 0,
                },
                new Segment
                {
                    Id = "porchline", Name = "Porchline", ControllerKey = North,
                    Output = 2, Count = 308, SegmentId = 1,
                },
            ],
        };

        var scene = new Scene { Name = "Stairs white", On = true, UnlistedSegmentsOff = true };
        scene.Segments["stairs"] = new SceneEntry { On = true, Effect = 0, PrimaryHex = "#FFFFFF" };

        WledPreset preset = ScenePublisher.BuildFor(project, scene, North);

        Assert.True(preset.On);
        Assert.Contains(preset.Segments ?? [], s => s.Id == 1 && s.On == false);
    }
}
