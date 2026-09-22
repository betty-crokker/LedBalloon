using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A look is one segment's appearance; a scene is what each segment wears. A look can be named and
/// shared, which is the point — "Under stairs warm white" is written down once, and changing it
/// reaches every scene using it.
/// </summary>
public class SceneTests
{
    private const string South = "704bca414644";

    private static LedBalloonProject House() => new()
    {
        Controllers = [new ControllerRef { Key = South, Name = "South" }],
        Segments =
        [
            new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Output = 1, Count = 20 },
            new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
        ],
    };

    [Fact]
    public void A_segment_wearing_a_named_look_gets_that_looks_appearance()
    {
        LedBalloonProject project = House();
        project.Reflow(South);

        var warm = new Look { Id = "warm", Name = "Warm white", Effect = 0, Primary = new RgbColor(255, 212, 172) };
        project.Looks.Add(warm);

        var scene = new Scene
        {
            Name = "Stairs white",
            Segments = { ["porch"] = new SceneEntry { LookId = "warm" } },
        };

        WledState state = SceneResolver.ResolveFor(project, scene, South);
        WledSegment porch = state.Segments!.Single(s => s.Start == 20);

        Assert.Equal(new RgbColor(255, 212, 172), porch.PrimaryColor);
        Assert.Equal(0, porch.Effect);
    }

    [Fact]
    public void A_one_off_entry_needs_no_name()
    {
        LedBalloonProject project = House();
        project.Reflow(South);

        var scene = new Scene
        {
            Segments = { ["porch"] = new SceneEntry { Effect = 74, Speed = 128, Primary = RgbColor.White } },
        };

        WledSegment porch = SceneResolver.ResolveFor(project, scene, South).Segments!
            .Single(s => s.Start == 20);

        Assert.Equal(74, porch.Effect);
        Assert.Equal((byte)128, porch.Speed);
    }

    /// <summary>The reason for naming a look at all.</summary>
    [Fact]
    public void Changing_a_named_look_changes_every_scene_wearing_it()
    {
        LedBalloonProject project = House();
        project.Reflow(South);

        var warm = new Look { Id = "warm", Name = "Warm white", Primary = new RgbColor(255, 212, 172) };
        project.Looks.Add(warm);

        var christmas = new Scene { Segments = { ["porch"] = new SceneEntry { LookId = "warm" } } };
        var winter = new Scene { Segments = { ["garage"] = new SceneEntry { LookId = "warm" } } };
        project.Scenes.Add(christmas);
        project.Scenes.Add(winter);

        // Too orange. Fixed in one place.
        warm.Primary = new RgbColor(255, 240, 220);

        Assert.Equal(
            new RgbColor(255, 240, 220),
            SceneResolver.ResolveFor(project, christmas, South).Segments!.Single(s => s.Start == 20).PrimaryColor);

        Assert.Equal(
            new RgbColor(255, 240, 220),
            SceneResolver.ResolveFor(project, winter, South).Segments!.Single(s => s.Start == 0).PrimaryColor);
    }

    [Fact]
    public void A_look_knows_how_many_scenes_wear_it()
    {
        LedBalloonProject project = House();
        project.Looks.Add(new Look { Id = "warm", Name = "Warm white" });

        project.Scenes.Add(new Scene { Segments = { ["porch"] = new SceneEntry { LookId = "warm" } } });
        project.Scenes.Add(new Scene { Segments = { ["garage"] = new SceneEntry { LookId = "warm" } } });
        project.Scenes.Add(new Scene { Segments = { ["garage"] = new SceneEntry { Effect = 1 } } });

        Assert.Equal(2, project.ScenesWearing("warm"));
    }

    /// <summary>
    /// A scene wearing a look that has since been deleted goes dark rather than inheriting whatever
    /// happens to be nearby. Silence is better than a wrong guess on the side of a house.
    /// </summary>
    [Fact]
    public void A_reference_to_a_deleted_look_falls_back_to_nothing()
    {
        LedBalloonProject project = House();
        project.Reflow(South);

        var scene = new Scene { Segments = { ["porch"] = new SceneEntry { LookId = "gone" } } };

        Appearance worn = project.Wearing(scene.Segments["porch"]);

        Assert.Null(worn.Effect);
        Assert.Null(worn.Primary);
    }

    [Fact]
    public void Promoting_a_one_off_into_a_named_look_keeps_what_it_looked_like()
    {
        var entry = new SceneEntry { Effect = 74, Speed = 200, Primary = RgbColor.White, Custom1 = 9 };

        var promoted = new Look { Name = "Twinkle" };
        entry.CopyTo(promoted);
        entry.LookId = promoted.Id;

        Assert.Equal(74, promoted.Effect);
        Assert.Equal((byte)200, promoted.Speed);
        Assert.Equal((byte)9, promoted.Custom1);
        Assert.Equal(RgbColor.White, promoted.Primary);
    }

    /// <summary>
    /// The sliders some effects have nothing else to say with. They were missing from the old
    /// per-segment look, so capturing a house and putting it back changed how it looked.
    /// </summary>
    [Fact]
    public void Capturing_a_house_keeps_the_custom_sliders()
    {
        LedBalloonProject project = House();
        project.Reflow(South);

        var state = new WledState
        {
            Segments =
            [
                new WledSegment { Id = 0, Start = 0, Stop = 20, Effect = 111, Custom1 = 3, Custom2 = 5, Custom3 = 7 },
            ],
        };

        Scene captured = SceneResolver.Capture(
            project, new Dictionary<string, WledState> { [South] = state }, "Captured");

        Assert.Equal((byte)3, captured.Segments["garage"].Custom1);
        Assert.Equal((byte)5, captured.Segments["garage"].Custom2);
        Assert.Equal((byte)7, captured.Segments["garage"].Custom3);
    }

    [Fact]
    public void The_custom_sliders_reach_the_wire_again()
    {
        LedBalloonProject project = House();
        project.Reflow(South);

        var scene = new Scene
        {
            Segments = { ["garage"] = new SceneEntry { Effect = 111, Custom1 = 3, Custom2 = 5, Custom3 = 7 } },
        };

        WledSegment garage = SceneResolver.ResolveFor(project, scene, South).Segments!
            .Single(s => s.Start == 0);

        Assert.Equal((byte)3, garage.Custom1);
        Assert.Equal((byte)5, garage.Custom2);
        Assert.Equal((byte)7, garage.Custom3);
    }
}
