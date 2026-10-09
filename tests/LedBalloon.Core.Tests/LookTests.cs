using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A look is a named appearance, written down once and worn wherever you like. Naming one is only
/// worth doing if editing it afterwards reaches the scenes using it, so that is what these pin.
/// </summary>
public class LookTests
{
    private const string South = TestHouse.South;

    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = South, Name = "South" }],
            Segments =
            [
                new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Output = 1, Count = 20 },
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    private static Look Warm() => new()
    {
        Id = "warm",
        Name = "Warm white",
        On = true,
        Effect = 0,
        Palette = 0,
        Primary = new RgbColor(255, 212, 172),
    };

    [Fact]
    public void Two_appearances_saying_the_same_thing_match()
    {
        var entry = new SceneEntry { On = true, Effect = 0, Palette = 0, Primary = new RgbColor(255, 212, 172) };

        Assert.True(Warm().Matches(entry));
    }

    [Fact]
    public void One_field_apart_is_not_a_match()
    {
        var entry = new SceneEntry { On = true, Effect = 0, Palette = 0, Primary = new RgbColor(255, 212, 172) };

        entry.Custom1 = 9;

        Assert.False(Warm().Matches(entry));
    }

    [Fact]
    public void Colors_match_whatever_case_the_hex_was_written_in()
    {
        var a = new Appearance { PrimaryHex = "ffd4ac" };
        var b = new Appearance { PrimaryHex = "FFD4AC" };

        Assert.True(a.Matches(b));
    }

    /// <summary>
    /// The point of binding. A segment already showing a named look is written down as wearing it,
    /// so that changing the look later reaches this scene too.
    /// </summary>
    [Fact]
    public void Capturing_a_segment_already_showing_a_look_records_it_as_wearing_that_look()
    {
        LedBalloonProject project = House();
        project.Looks.Add(Warm());

        var scene = new Scene
        {
            Segments =
            {
                ["garage"] = new SceneEntry { On = true, Effect = 0, Palette = 0, Primary = new RgbColor(255, 212, 172) },
                ["porch"] = new SceneEntry { On = true, Effect = 74, Speed = 128 },
            },
        };

        Assert.Equal(1, project.BindLooks(scene));

        Assert.Equal("warm", scene.Segments["garage"].LookId);
        Assert.Null(scene.Segments["porch"].LookId);
    }

    /// <summary>
    /// The reference has to be the only description left, or there would be two answers to one
    /// question and the stale one would win the day the look changed.
    /// </summary>
    [Fact]
    public void Binding_clears_the_fields_it_replaces()
    {
        LedBalloonProject project = House();
        project.Looks.Add(Warm());

        var scene = new Scene
        {
            Segments = { ["garage"] = new SceneEntry { On = true, Effect = 0, Palette = 0, Primary = new RgbColor(255, 212, 172) } },
        };

        project.BindLooks(scene);

        SceneEntry entry = scene.Segments["garage"];
        Assert.Null(entry.Effect);
        Assert.Null(entry.PrimaryHex);
        Assert.Null(entry.On);
    }

    [Fact]
    public void Binding_leaves_an_entry_that_already_names_a_look_alone()
    {
        LedBalloonProject project = House();
        project.Looks.Add(Warm());
        project.Looks.Add(new Look { Id = "other", Name = "Other", Effect = 74 });

        var scene = new Scene { Segments = { ["garage"] = new SceneEntry { LookId = "other" } } };

        Assert.Equal(0, project.BindLooks(scene));
        Assert.Equal("other", scene.Segments["garage"].LookId);
    }

    /// <summary>The reach that makes naming worth it, end to end through the resolver.</summary>
    [Fact]
    public void Editing_a_bound_look_changes_what_the_captured_scene_puts_on_the_wall()
    {
        LedBalloonProject project = House();
        Look warm = Warm();
        project.Looks.Add(warm);

        var scene = new Scene
        {
            Segments = { ["garage"] = new SceneEntry { On = true, Effect = 0, Palette = 0, Primary = new RgbColor(255, 212, 172) } },
        };

        project.BindLooks(scene);
        project.Scenes.Add(scene);

        // Too orange. Fixed in one place, after the scene was captured.
        warm.Primary = new RgbColor(255, 240, 220);

        WledSegment garage = SceneResolver.ResolveFor(project, scene, South).Segments!.Single(s => s.Start == 0);

        Assert.Equal(new RgbColor(255, 240, 220), garage.PrimaryColor);
        Assert.Equal(1, project.ScenesWearing("warm"));
    }

    [Fact]
    public void A_look_cannot_take_a_name_another_look_already_has()
    {
        LedBalloonProject project = House();
        project.Looks.Add(new Look { Id = "warm", Name = "Warm white" });

        Assert.Equal("Warm white 2", project.UniqueLookName("Warm white"));
        Assert.Equal("Warm white", project.UniqueLookName("Warm white", ignoringId: "warm"));
    }

    [Fact]
    public void A_look_nothing_matches_leaves_the_scene_as_it_was()
    {
        LedBalloonProject project = House();
        project.Looks.Add(Warm());

        var scene = new Scene { Segments = { ["garage"] = new SceneEntry { Effect = 74 } } };

        Assert.Equal(0, project.BindLooks(scene));
        Assert.Equal(74, scene.Segments["garage"].Effect);
    }
}
