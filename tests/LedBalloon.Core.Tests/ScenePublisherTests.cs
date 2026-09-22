using System.Text.Json.Nodes;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Publishing is what makes a scene reachable without a PC — the timer, the wall button and the
/// phone app all recall presets and nothing else. These pin down that each controller's preset is
/// derived from the scene rather than translated from another controller's preset, which is the
/// defect this replaced.
/// </summary>
public class ScenePublisherTests
{
    private const string North = "20e7c86a1be8";
    private const string South = "704bca414644";

    /// <summary>The real house: the stairs are on North, the garage and porch on South.</summary>
    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers =
            [
                new ControllerRef { Key = North, Name = "North" },
                new ControllerRef { Key = South, Name = "South" },
            ],
            Segments =
            [
                new Segment { Id = "porchline", Name = "Porchline", ControllerKey = North, Output = 1, Count = 308 },
                new Segment { Id = "stairs", Name = "Under stairs", ControllerKey = North, Output = 1, Count = 48 },
                new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Output = 1, Count = 20 },
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    private static Scene StairsWhite() => new()
    {
        Name = "Stairs white",
        Segments =
        {
            ["porchline"] = new SceneEntry { On = false },
            ["stairs"] = new SceneEntry { Effect = 0, Primary = new RgbColor(255, 212, 172) },
        },
    };

    [Fact]
    public void A_controller_is_published_only_its_own_runs_with_its_own_bounds()
    {
        WledPreset north = ScenePublisher.BuildFor(House(), StairsWhite(), North);

        Assert.Equal("Stairs white", north.Name);
        Assert.Equal(2, north.Segments!.Count);
        Assert.Equal(0, north.Segments[0].Start);
        Assert.Equal(308, north.Segments[0].Stop);
        Assert.Equal(308, north.Segments[1].Start);
        Assert.Equal(356, north.Segments[1].Stop);
    }

    /// <summary>
    /// The defect. Applying North's "Stairs white" to South used to pair runs by position, so the
    /// garage took what the porchline was doing and the porch took the stairs' warm white — a
    /// preset called <i>Stairs white</i> lighting the porch, because position 1 on one box happens
    /// to be the stairs and position 1 on the other happens to be the porch.
    /// </summary>
    [Fact]
    public void A_south_run_never_inherits_what_a_north_run_is_doing()
    {
        WledPreset south = ScenePublisher.BuildFor(House(), StairsWhite(), South);

        Assert.Equal(2, south.Segments!.Count);

        // The scene says nothing about either South run, so both are off rather than wearing
        // whatever the run at the same position on North happens to wear.
        Assert.All(south.Segments, segment => Assert.False(segment.On));
        Assert.All(south.Segments, segment => Assert.Null(segment.Effect));
        Assert.All(south.Segments, segment => Assert.Null(segment.Colors));
    }

    [Fact]
    public void One_scene_is_one_name_however_many_controllers_store_it()
    {
        LedBalloonProject house = House();
        Scene scene = StairsWhite();

        Assert.Equal(
            ScenePublisher.BuildFor(house, scene, North).Name,
            ScenePublisher.BuildFor(house, scene, South).Name);
    }

    [Fact]
    public void A_scene_reuses_the_slot_its_name_already_occupies()
    {
        JsonObject presets = OnTheBox(("1", "Bpm"), ("2", "Off"), ("3", "Stairs white"));

        PublishPlan plan = ScenePublisher.PlanFor(presets, ScenePublisher.BuildFor(House(), StairsWhite(), North));

        // Stable across republishes, which is what lets a timer point at it once and go on working.
        Assert.Equal(3, plan.Slot);
    }

    [Fact]
    public void A_scene_the_box_has_never_heard_of_takes_the_first_free_slot()
    {
        JsonObject presets = OnTheBox(("1", "Bpm"), ("2", "Off"));

        PublishPlan plan = ScenePublisher.PlanFor(presets, ScenePublisher.BuildFor(House(), StairsWhite(), North));

        // Never slot 0, which is WLED's scratch slot rather than a real preset.
        Assert.Equal(3, plan.Slot);
        Assert.True(plan.Changed);
    }

    /// <summary>
    /// Flash has a finite write budget and saving a layout is not a reason to spend one.
    /// </summary>
    [Fact]
    public void Republishing_a_scene_nothing_has_changed_about_writes_nothing()
    {
        LedBalloonProject house = House();
        WledPreset built = ScenePublisher.BuildFor(house, StairsWhite(), North);

        JsonObject presets = OnTheBox(("1", "Bpm"));
        presets["2"] = Stored(built);

        PublishPlan plan = ScenePublisher.PlanFor(presets, ScenePublisher.BuildFor(house, StairsWhite(), North));

        Assert.Equal(2, plan.Slot);
        Assert.False(plan.Changed);
    }

    [Fact]
    public void A_corrected_run_length_makes_the_published_copy_stale_again()
    {
        LedBalloonProject house = House();

        var presets = new JsonObject { ["2"] = Stored(ScenePublisher.BuildFor(house, StairsWhite(), North)) };

        // The porchline turns out to be 310, not 308 — the very thing that makes a baked-in preset
        // wrong and a scene right.
        house.FindSegment("porchline")!.Count = 310;
        house.Reflow(North);

        Assert.True(ScenePublisher.PlanFor(presets, ScenePublisher.BuildFor(house, StairsWhite(), North)).Changed);
    }

    [Fact]
    public void Changing_what_one_segment_wears_makes_the_published_copy_stale()
    {
        LedBalloonProject house = House();

        var presets = new JsonObject { ["2"] = Stored(ScenePublisher.BuildFor(house, StairsWhite(), North)) };

        Scene warmer = StairsWhite();
        warmer.Segments["stairs"].Primary = new RgbColor(255, 240, 220);

        Assert.True(ScenePublisher.PlanFor(presets, ScenePublisher.BuildFor(house, warmer, North)).Changed);
    }

    [Fact]
    public void A_scenes_crossfade_survives_being_published()
    {
        Scene scene = StairsWhite();
        scene.Transition = 20;

        Assert.Equal(20, ScenePublisher.BuildFor(House(), scene, North).Transition);
    }

    /// <summary>A controller with no runs drawn on it has nothing to publish.</summary>
    [Fact]
    public void A_controller_the_house_does_not_reach_gets_an_empty_preset()
    {
        Assert.Empty(ScenePublisher.BuildFor(House(), StairsWhite(), "aabbccddeeff").Segments!);
    }

    /// <summary>
    /// A rename has to replace its old self. Anything else orphans the old preset, and a timer
    /// pointing at its slot would go on firing the previous version of the scene forever.
    /// </summary>
    [Fact]
    public void A_renamed_scene_lands_in_the_slot_its_old_name_holds()
    {
        JsonObject presets = OnTheBox(("1", "Bpm"), ("2", "Off"), ("4", "Evening white"));

        Scene renamed = StairsWhite();
        renamed.Name = "Evening warm";

        PublishPlan plan = ScenePublisher.PlanFor(
            presets,
            ScenePublisher.BuildFor(House(), renamed, North),
            previousName: "Evening white");

        Assert.Equal(4, plan.Slot);
        Assert.True(plan.Changed);
    }

    [Fact]
    public void The_name_it_has_now_wins_over_the_one_it_used_to_have()
    {
        JsonObject presets = OnTheBox(("3", "Evening warm"), ("4", "Evening white"));

        Scene renamed = StairsWhite();
        renamed.Name = "Evening warm";

        PublishPlan plan = ScenePublisher.PlanFor(
            presets,
            ScenePublisher.BuildFor(House(), renamed, North),
            previousName: "Evening white");

        Assert.Equal(3, plan.Slot);
    }

    /// <summary>
    /// Two scenes of one name would fight over a single slot on every box, each overwriting the
    /// other on alternate saves.
    /// </summary>
    [Fact]
    public void A_scene_cannot_take_a_name_another_scene_already_has()
    {
        LedBalloonProject house = House();
        house.Scenes.Add(new Scene { Id = "a", Name = "Christmas" });

        Assert.Equal("Christmas 2", house.UniqueSceneName("Christmas"));

        // The clash is spotted whatever the casing, and the answer keeps what was typed.
        Assert.Equal("christmas 2", house.UniqueSceneName("  christmas  "));

        // A scene keeps its own name when it is the one being renamed.
        Assert.Equal("Christmas", house.UniqueSceneName("Christmas", ignoringId: "a"));
    }

    [Fact]
    public void A_third_scene_of_the_same_name_counts_on_past_the_second()
    {
        LedBalloonProject house = House();
        house.Scenes.Add(new Scene { Name = "Winter" });
        house.Scenes.Add(new Scene { Name = "Winter 2" });

        Assert.Equal("Winter 3", house.UniqueSceneName("Winter"));
    }

    /// <summary>A presets.json holding named slots, the way a real box does.</summary>
    private static JsonObject OnTheBox(params (string Slot, string Name)[] entries)
    {
        var presets = new JsonObject { ["0"] = new JsonObject() };

        foreach ((string slot, string name) in entries)
        {
            presets[slot] = new JsonObject { ["n"] = name };
        }

        return presets;
    }

    /// <summary>What the box would hold after this preset was published to it.</summary>
    private static JsonNode Stored(WledPreset preset) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
            preset, LedBalloon.Core.Json.WledJson.Default.WledPreset))!;
}
