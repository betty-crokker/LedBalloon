using System.Text.Json.Nodes;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A scene and its published copy are two renderings of one fact, and they can come apart — someone
/// edits the preset in the WLED app, and the next save quietly writes over it. These pin down that
/// it is noticed, and that the ordinary case of the scene itself having changed is not mistaken
/// for it.
/// </summary>
public class DriftTests
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

    private static Scene Christmas() => new()
    {
        Name = "Christmas",
        Segments = { ["garage"] = new SceneEntry { Effect = 74, Speed = 128 } },
    };

    /// <summary>A preset file holding this scene exactly as LedBalloon would have written it.</summary>
    private static (JsonObject Presets, string Hash) AsPublished(LedBalloonProject house, Scene scene)
    {
        WledPreset built = ScenePublisher.BuildFor(house, scene, South);
        var presets = new JsonObject { ["0"] = new JsonObject(), ["3"] = Stored(built) };
        return (presets, ScenePublisher.Hash(built));
    }

    [Fact]
    public void A_copy_still_holding_what_we_wrote_is_not_drift()
    {
        LedBalloonProject house = House();
        (JsonObject presets, string hash) = AsPublished(house, Christmas());

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, Christmas(), South), expectedHash: hash);

        Assert.False(plan.Changed);
        Assert.False(plan.Drifted);
    }

    /// <summary>
    /// The ordinary case, and the one that must not be mistaken for drift: the scene was edited
    /// here, the controller still holds the previous version, and republishing is the point.
    /// </summary>
    [Fact]
    public void Editing_the_scene_is_a_change_but_not_drift()
    {
        LedBalloonProject house = House();
        (JsonObject presets, string hash) = AsPublished(house, Christmas());

        Scene edited = Christmas();
        edited.Segments["garage"].Effect = 9;

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, edited, South), expectedHash: hash);

        Assert.True(plan.Changed);
        Assert.False(plan.Drifted);
    }

    /// <summary>Somebody changed it in the WLED app. Overwriting would throw that away.</summary>
    [Fact]
    public void A_copy_someone_else_changed_is_drift()
    {
        LedBalloonProject house = House();
        (JsonObject presets, string hash) = AsPublished(house, Christmas());

        presets["3"]!["seg"]![0]!["fx"] = 33;

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, Christmas(), South), expectedHash: hash);

        Assert.True(plan.Changed);
        Assert.True(plan.Drifted);
    }

    /// <summary>
    /// A field LedBalloon does not model counts too. Editing a preset in the WLED app changes such
    /// fields, and noticing is the whole point — comparing parsed presets would miss it.
    /// </summary>
    [Fact]
    public void An_edit_to_a_field_we_do_not_model_is_still_drift()
    {
        LedBalloonProject house = House();
        (JsonObject presets, string hash) = AsPublished(house, Christmas());

        presets["3"]!["seg"]![0]!["mi"] = true;

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, Christmas(), South), expectedHash: hash);

        Assert.True(plan.Drifted);
    }

    /// <summary>
    /// With nothing to compare against there is no edit to be throwing away. A scene published for
    /// the first time, or one adopted from a preset made by hand, must not be reported as drifted.
    /// </summary>
    [Fact]
    public void A_slot_we_have_never_written_is_not_drift()
    {
        LedBalloonProject house = House();

        var presets = new JsonObject
        {
            ["0"] = new JsonObject(),
            ["3"] = new JsonObject { ["n"] = "Christmas", ["on"] = true },
        };

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, Christmas(), South), expectedHash: null);

        Assert.True(plan.Changed);
        Assert.False(plan.Drifted);
    }

    [Fact]
    public void An_empty_slot_is_not_drift()
    {
        LedBalloonProject house = House();

        var presets = new JsonObject { ["0"] = new JsonObject() };

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, Christmas(), South), expectedHash: "deadbeefdeadbeef");

        Assert.True(plan.Changed);
        Assert.False(plan.Drifted);
        Assert.Null(plan.StoredHash);
    }

    /// <summary>
    /// A scene edited here *and* a copy edited there is still drift. Both moved, so the disagreement
    /// is real whichever way it is settled.
    /// </summary>
    [Fact]
    public void Both_sides_moving_is_drift()
    {
        LedBalloonProject house = House();
        (JsonObject presets, string hash) = AsPublished(house, Christmas());

        presets["3"]!["seg"]![0]!["fx"] = 33;

        Scene edited = Christmas();
        edited.Segments["garage"].Effect = 9;

        PublishPlan plan = ScenePublisher.PlanFor(
            presets, ScenePublisher.BuildFor(house, edited, South), expectedHash: hash);

        Assert.True(plan.Drifted);
    }

    /// <summary>
    /// A controller that drifted into agreeing with the scene is not a disagreement. Whatever
    /// happened, the box now holds what the scene says, and there is nothing to write or ask.
    /// </summary>
    [Fact]
    public void A_copy_edited_into_agreeing_with_the_scene_is_left_alone()
    {
        LedBalloonProject house = House();
        (JsonObject presets, _) = AsPublished(house, Christmas());

        PublishPlan plan = ScenePublisher.PlanFor(
            presets,
            ScenePublisher.BuildFor(house, Christmas(), South),
            expectedHash: "0123456789abcdef");

        Assert.False(plan.Changed);
        Assert.False(plan.Drifted);
    }

    [Fact]
    public void A_scene_records_what_it_published_to_each_controller()
    {
        var scene = Christmas();

        Assert.Empty(scene.Published);

        scene.Published[South] = "0123456789abcdef";

        Assert.Equal("0123456789abcdef", scene.Published[South]);
    }

    private static JsonNode Stored(WledPreset preset) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
            preset, LedBalloon.Core.Json.WledJson.Default.WledPreset))!;
}
