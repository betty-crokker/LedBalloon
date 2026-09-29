using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Picking a scene and then changing a run means changing that scene. These pin down that a hand
/// edit changes only what it touched, that a run wearing a named look keeps what it looked like
/// while ceasing to follow the name, and that branching leaves the original alone.
/// </summary>
public class SceneEditTests
{
    private const string South = "020000000002";

    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = South, Name = "South" }],
            Segments =
            [
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
                new Segment { Id = "roof", Name = "Roofline", ControllerKey = South, Output = 1, Count = 285 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    [Fact]
    public void A_color_edit_leaves_the_effect_and_palette_alone()
    {
        var entry = new SceneEntry { Effect = 74, Palette = 12, Speed = 200 };

        SceneResolver.Absorb(entry, new WledSegment { Colors = [[0, 0, 255]] });

        Assert.Equal(new RgbColor(0, 0, 255), entry.Primary);
        Assert.Equal(74, entry.Effect);
        Assert.Equal(12, entry.Palette);
        Assert.Equal((byte)200, entry.Speed);
    }

    [Fact]
    public void An_effect_edit_leaves_the_color_alone()
    {
        var entry = new SceneEntry { Effect = 74, Primary = RgbColor.White };

        SceneResolver.Absorb(entry, new WledSegment { Effect = 9 });

        Assert.Equal(9, entry.Effect);
        Assert.Equal(RgbColor.White, entry.Primary);
    }

    /// <summary>
    /// The edit is about this scene and this run. Changing the look itself is a different
    /// intention with its own button, and doing it here would reach into every other scene using
    /// it without anyone asking.
    /// </summary>
    [Fact]
    public void Editing_a_run_that_wears_a_look_stops_it_following_that_look()
    {
        LedBalloonProject house = House();
        var red = new Look { Id = "red", Name = "Nice red", Effect = 0, Primary = new RgbColor(255, 0, 0) };
        house.Looks.Add(red);

        var entry = new SceneEntry { LookId = "red" };

        SceneResolver.Absorb(entry, new WledSegment { Colors = [[0, 255, 0]] }, house.Wearing(entry));

        Assert.Null(entry.LookId);
        Assert.Equal(new RgbColor(0, 255, 0), entry.Primary);

        // What it looked like apart from the edit came across, rather than being lost with the
        // reference that used to carry it.
        Assert.Equal(0, entry.Effect);

        // And the look itself is untouched, so the other scenes wearing it do not move.
        Assert.Equal(new RgbColor(255, 0, 0), red.Primary);
    }

    /// <summary>
    /// The scenario this was built for: "all red" and "red and blue" share a look, so changing the
    /// look changes both, and the run that was overridden keeps its override.
    /// </summary>
    [Fact]
    public void Changing_a_shared_look_reaches_both_scenes_but_not_the_overridden_run()
    {
        LedBalloonProject house = House();
        var red = new Look { Id = "red", Name = "Nice red", Primary = new RgbColor(255, 0, 0) };
        house.Looks.Add(red);

        var allRed = new Scene
        {
            Name = "All red",
            Segments =
            {
                ["porch"] = new SceneEntry { LookId = "red" },
                ["roof"] = new SceneEntry { LookId = "red" },
            },
        };

        var mixed = new Scene
        {
            Name = "Red and blue",
            Segments =
            {
                ["porch"] = new SceneEntry { Primary = new RgbColor(0, 0, 255) },
                ["roof"] = new SceneEntry { LookId = "red" },
            },
        };

        house.Scenes.Add(allRed);
        house.Scenes.Add(mixed);

        // One edit, in one place.
        red.Primary = new RgbColor(0, 0, 255);

        Assert.Equal(
            new RgbColor(0, 0, 255),
            SceneResolver.ResolveFor(house, allRed, South).Segments!.Single(s => s.Start == 0).PrimaryColor);

        Assert.Equal(
            new RgbColor(0, 0, 255),
            SceneResolver.ResolveFor(house, mixed, South).Segments!.Single(s => s.Start == 5).PrimaryColor);

        Assert.Equal(2, house.ScenesWearing("red"));
    }

    [Fact]
    public void A_copy_of_a_scene_is_independent_of_it()
    {
        var scene = new Scene
        {
            Name = "All red",
            Brightness = { [South] = 200 },
            Segments = { ["porch"] = new SceneEntry { Effect = 74, Primary = RgbColor.White } },
        };

        Scene copy = scene.Clone();
        copy.Segments["porch"].Effect = 9;

        // The brightness dictionary is its own, not the same one under two names. It used to be a
        // single number, where copying it was nothing to get wrong.
        copy.SetBrightnessOn(South, 10);

        Assert.Equal(74, scene.Segments["porch"].Effect);
        Assert.Equal((byte)200, scene.BrightnessOn(South));
    }

    /// <summary>
    /// Branching has to leave the scene it was started from exactly as it was, including what it
    /// has published and where — otherwise the next save would think that had changed too.
    /// </summary>
    [Fact]
    public void Putting_a_scene_back_restores_what_it_said()
    {
        var scene = new Scene
        {
            Name = "All red",
            PublishedAs = "All red",
            Published = { [South] = "0123456789abcdef" },
            Segments = { ["porch"] = new SceneEntry { Primary = new RgbColor(255, 0, 0) } },
        };

        Scene before = scene.Clone();

        scene.Segments["porch"].Primary = new RgbColor(0, 0, 255);
        scene.Segments["roof"] = new SceneEntry { Effect = 9 };

        scene.CopyFrom(before);

        Assert.Equal(new RgbColor(255, 0, 0), scene.Segments["porch"].Primary);
        Assert.Single(scene.Segments);

        // Its own identity is not part of what gets put back.
        Assert.Equal("All red", scene.Name);
        Assert.Equal("All red", scene.PublishedAs);
        Assert.Equal("0123456789abcdef", scene.Published[South]);
    }
}
