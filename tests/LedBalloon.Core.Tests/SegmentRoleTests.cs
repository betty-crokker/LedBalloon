using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A scene does one of three things about a segment: shows something on it, switches it off, or
/// says nothing at all. Off and silence look identical on the wall whenever the segment happens to
/// be dark already, and are completely different the rest of the time — one is a decision the scene
/// makes and the other is a decision it declines to make.
/// <para>
/// The model could always hold all three. What it could not do was let you choose between them: a
/// scene that accounts for every segment says "anything I do not mention is off", so there was no
/// way to say "and leave the garage alone" without saying it about everything.
/// </para>
/// </summary>
public class SegmentRoleTests
{
    private const string South = TestHouse.South;

    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = South, Name = "South" }],
            Segments =
            [
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
                new Segment { Id = "roof", Name = "Roofline", ControllerKey = South, Output = 1, Count = 285 },
                new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Output = 1, Count = 40 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    private static string[] Ids(LedBalloonProject house) => [.. house.Segments.Select(s => s.Id)];

    [Fact]
    public void A_segment_with_an_appearance_is_shown()
    {
        var scene = new Scene { Segments = { ["porch"] = new SceneEntry { Effect = 74 } } };

        Assert.Equal(Scene.SegmentRole.Shown, scene.RoleOf("porch"));
    }

    [Fact]
    public void A_segment_wearing_a_look_is_shown_too()
    {
        var scene = new Scene { Segments = { ["porch"] = new SceneEntry { LookId = "warm" } } };

        Assert.Equal(Scene.SegmentRole.Shown, scene.RoleOf("porch"));
    }

    [Fact]
    public void An_entry_that_says_off_is_off()
    {
        var scene = new Scene { Segments = { ["porch"] = new SceneEntry { On = false } } };

        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("porch"));
    }

    [Fact]
    public void Silence_in_a_scene_that_accounts_for_everything_is_off()
    {
        var scene = new Scene { UnlistedSegmentsOff = true };

        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("porch"));
    }

    [Fact]
    public void Silence_in_a_scene_that_does_not_is_not_included()
    {
        var scene = new Scene { UnlistedSegmentsOff = false };

        Assert.Equal(Scene.SegmentRole.NotIncluded, scene.RoleOf("porch"));
    }

    [Fact]
    public void Excluding_one_segment_from_a_whole_house_scene_leaves_the_others_off()
    {
        // The case the method exists for. Removing the entry alone would have excluded the garage
        // and, in the same stroke, excluded every other segment nobody had mentioned - because what
        // "unmentioned" meant for the whole scene would have changed underneath them.
        LedBalloonProject house = House();

        var scene = new Scene
        {
            UnlistedSegmentsOff = true,
            Segments = { ["porch"] = new SceneEntry { Effect = 74 } },
        };

        scene.Exclude("garage", Ids(house));

        Assert.Equal(Scene.SegmentRole.NotIncluded, scene.RoleOf("garage"));
        Assert.Equal(Scene.SegmentRole.Shown, scene.RoleOf("porch"));
        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("roof"));
    }

    [Fact]
    public void And_what_it_puts_on_the_wire_does_not_change_for_them()
    {
        // The stronger version of the test above: the scene is rewritten, so prove the rewrite is
        // equivalent where it should be. The roofline was being switched off by omission and must
        // still be switched off, by an entry this time.
        LedBalloonProject house = House();

        var scene = new Scene
        {
            UnlistedSegmentsOff = true,
            Segments = { ["porch"] = new SceneEntry { Effect = 74, On = true } },
        };

        WledState before = SceneResolver.ResolveFor(house, scene, South);

        scene.Exclude("garage", Ids(house));

        WledState after = SceneResolver.ResolveFor(house, scene, South);

        static WledSegment Of(WledState state, int id) => state.Segments!.Single(s => s.Id == id);

        Assert.Equal(Of(before, 0).On, Of(after, 0).On);   // porch, shown
        Assert.Equal(Of(before, 1).On, Of(after, 1).On);   // roofline, off either way
    }

    [Fact]
    public void An_excluded_segment_is_sent_nothing_that_would_change_it()
    {
        LedBalloonProject house = House();

        var scene = new Scene { UnlistedSegmentsOff = true };
        scene.Exclude("garage", Ids(house));

        WledSegment garage = SceneResolver
            .ResolveFor(house, scene, South).Segments!
            .Single(s => s.Id == house.WledSegmentIdFor(house.Segments.Single(x => x.Id == "garage")));

        // Geometry only: no power, no effect, no color. WLED merges that into what is there.
        Assert.Null(garage.On);
        Assert.Null(garage.Effect);
        Assert.Null(garage.Colors);
    }

    [Fact]
    public void Excluding_from_a_scene_that_already_leaves_things_alone_touches_nothing_else()
    {
        var scene = new Scene
        {
            UnlistedSegmentsOff = false,
            Segments = { ["porch"] = new SceneEntry { Effect = 74 }, ["roof"] = new SceneEntry { Effect = 9 } },
        };

        scene.Exclude("porch", ["porch", "roof", "garage"]);

        Assert.Equal(Scene.SegmentRole.NotIncluded, scene.RoleOf("porch"));
        Assert.Equal(Scene.SegmentRole.Shown, scene.RoleOf("roof"));
        Assert.Equal(Scene.SegmentRole.NotIncluded, scene.RoleOf("garage"));
        Assert.False(scene.UnlistedSegmentsOff);
    }

    [Fact]
    public void Switching_one_off_does_not_leave_what_it_was_showing_underneath()
    {
        // A fresh entry, not the old one with its power cleared. Leaving the effect and colors in
        // place would store an answer nothing reads, and hand it back if the segment were ever
        // switched on again.
        var scene = new Scene
        {
            Segments = { ["porch"] = new SceneEntry { Effect = 74, Palette = 12, LookId = "warm" } },
        };

        scene.TurnOff("porch");

        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("porch"));
        Assert.Null(scene.Segments["porch"].Effect);
        Assert.Null(scene.Segments["porch"].Palette);
        Assert.Null(scene.Segments["porch"].LookId);
    }

    [Fact]
    public void Putting_a_look_on_a_segment_keeps_only_the_reference()
    {
        // An entry carries either a look or its own fields, never both. Editing the look afterwards
        // is meant to reach this scene, and it cannot if a stale copy is sitting beside the name.
        var scene = new Scene
        {
            Segments = { ["porch"] = new SceneEntry { Effect = 74, Palette = 12 } },
        };

        scene.Wear("porch", "warm");

        Assert.Equal("warm", scene.Segments["porch"].LookId);
        Assert.Null(scene.Segments["porch"].Effect);
        Assert.Null(scene.Segments["porch"].Palette);
        Assert.Equal(Scene.SegmentRole.Shown, scene.RoleOf("porch"));
    }

    [Fact]
    public void A_segment_can_be_moved_through_all_three_and_back()
    {
        LedBalloonProject house = House();
        var scene = new Scene { UnlistedSegmentsOff = true, Segments = { ["porch"] = new SceneEntry { Effect = 74 } } };

        scene.Exclude("porch", Ids(house));
        Assert.Equal(Scene.SegmentRole.NotIncluded, scene.RoleOf("porch"));

        scene.TurnOff("porch");
        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("porch"));

        scene.Wear("porch", "warm");
        Assert.Equal(Scene.SegmentRole.Shown, scene.RoleOf("porch"));

        scene.Exclude("porch", Ids(house));
        Assert.Equal(Scene.SegmentRole.NotIncluded, scene.RoleOf("porch"));

        // And the rest of the house is where the first exclude left it, not somewhere the
        // round trip wandered to.
        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("roof"));
        Assert.Equal(Scene.SegmentRole.Off, scene.RoleOf("garage"));
    }
}
