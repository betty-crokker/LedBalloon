using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Brightness is one value for a whole controller, so a scene that sends it reaches every segment on
/// that box — including the ones it has just promised to leave alone. Silence about all of them has
/// to be silence about the brightness too, or "not included" is honoured for a segment's effect and
/// colour and quietly broken for how bright it is.
/// </summary>
public class SilenceAboutAControllerTests
{
    private const string South = TestHouse.South;
    private const string North = "a04bca414699";

    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers =
            [
                new ControllerRef { Key = South, Name = "South" },
                new ControllerRef { Key = North, Name = "North" },
            ],
            Segments =
            [
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
                new Segment { Id = "roof", Name = "Roofline", ControllerKey = South, Output = 1, Count = 285 },
                new Segment { Id = "eaves", Name = "Eaves", ControllerKey = North, Output = 1, Count = 40 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    [Fact]
    public void A_controller_the_scene_says_nothing_about_is_sent_no_brightness()
    {
        // The whole point. The scene lights the porch on South and has a brightness for North left
        // over from wherever it came from; North's segment is not in it, so North must not be dimmed
        // on its way past.
        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            Brightness = { [South] = 200, [North] = 30 },
            Segments = { ["porch"] = new SceneEntry { Effect = 74 } },
        };

        Assert.Equal((byte)200, SceneResolver.ResolveFor(House(), scene, South).Brightness);
        Assert.Null(SceneResolver.ResolveFor(House(), scene, North).Brightness);
    }

    [Fact]
    public void And_is_not_switched_on_either()
    {
        // Found by pressing the button. The real "Off" scene carries on:true with the brightness at
        // zero - which is how WLED writes an off preset - and names only South's segments. Applying
        // it switched North on, because power was sent to every controller whatever the scene said.
        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            On = true,
            Brightness = { [South] = 0, [North] = 0 },
            Segments = { ["porch"] = new SceneEntry { On = true, Effect = 1 } },
        };

        Assert.True(SceneResolver.ResolveFor(House(), scene, South).On);
        Assert.Null(SceneResolver.ResolveFor(House(), scene, North).On);
    }

    [Fact]
    public void A_controller_it_does_speak_about_still_gets_one()
    {
        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            Brightness = { [North] = 30 },
            Segments = { ["eaves"] = new SceneEntry { Effect = 74 } },
        };

        Assert.Equal((byte)30, SceneResolver.ResolveFor(House(), scene, North).Brightness);
    }

    [Fact]
    public void A_scene_that_accounts_for_everything_speaks_about_every_controller()
    {
        // UnlistedSegmentsOff means "anything I do not mention is off", which is a thing said about
        // every segment on every controller - so there is no silence anywhere to respect.
        var scene = new Scene
        {
            UnlistedSegmentsOff = true,
            Brightness = { [South] = 200, [North] = 30 },
        };

        Assert.Equal((byte)200, SceneResolver.ResolveFor(House(), scene, South).Brightness);
        Assert.Equal((byte)30, SceneResolver.ResolveFor(House(), scene, North).Brightness);
    }

    [Fact]
    public void Switching_a_segment_off_is_speaking_about_its_controller()
    {
        // Off is a decision, not a silence. A scene that turns North's eaves off is about North, and
        // the brightness it carries for North is part of what it is asking for.
        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            Brightness = { [North] = 30 },
        };

        scene.TurnOff("eaves");

        Assert.Equal((byte)30, SceneResolver.ResolveFor(House(), scene, North).Brightness);
    }

    [Fact]
    public void Excluding_the_last_segment_on_a_controller_takes_its_brightness_with_it()
    {
        LedBalloonProject house = House();

        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            Brightness = { [North] = 30 },
            Segments = { ["eaves"] = new SceneEntry { Effect = 74 } },
        };

        Assert.Equal((byte)30, SceneResolver.ResolveFor(house, scene, North).Brightness);

        scene.Exclude("eaves", house.Segments.Select(s => s.Id));

        Assert.Null(SceneResolver.ResolveFor(house, scene, North).Brightness);
    }

    [Fact]
    public void And_the_segments_on_a_controller_it_ignores_are_still_sent_nothing_but_their_bounds()
    {
        // Belt and braces: the reason the brightness mattered is that the segment patch itself is
        // empty, so the brightness was the one thing still reaching a segment left alone.
        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            Brightness = { [North] = 30 },
            Segments = { ["porch"] = new SceneEntry { Effect = 74 } },
        };

        WledSegment eaves = Assert.Single(SceneResolver.ResolveFor(House(), scene, North).Segments!);

        Assert.Null(eaves.On);
        Assert.Null(eaves.Effect);
        Assert.Null(eaves.Brightness);
    }
}
