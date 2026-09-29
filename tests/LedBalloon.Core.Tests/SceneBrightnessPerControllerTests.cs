using System.Text;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A scene is one WLED preset per controller, and each of those carries its own brightness — so the
/// scene has to as well. It used to hold a single number for the house, filled in by whichever
/// controller was read first, which made two things impossible: describing a house that really is
/// brighter on one side than the other, and reading back a pair of presets that already were.
/// <para>
/// The real house is exactly that case. South sits at 255 and North at 38.
/// </para>
/// </summary>
public class SceneBrightnessPerControllerTests
{
    private const string South = "020000000002";
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
                new Segment { Id = "eaves", Name = "Eaves", ControllerKey = North, Output = 1, Count = 40 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    private static WledState Lit(byte brightness, int segmentId) => new()
    {
        On = true,
        Brightness = brightness,
        Segments = [new WledSegment { Id = segmentId, On = true, Effect = 1 }],
    };

    [Fact]
    public void Each_controller_is_sent_its_own_brightness()
    {
        var scene = new Scene { Name = "Uneven", Brightness = { [South] = 255, [North] = 38 } };

        LedBalloonProject house = House();

        Assert.Equal((byte)255, SceneResolver.ResolveFor(house, scene, South).Brightness);
        Assert.Equal((byte)38, SceneResolver.ResolveFor(house, scene, North).Brightness);
    }

    [Fact]
    public void A_controller_the_scene_says_nothing_about_is_left_alone()
    {
        // Null on the wire means "no bri field", which WLED leaves as it is. A scene that only
        // mentions South must not decide anything for North.
        var scene = new Scene { Name = "South only", Brightness = { [South] = 255 } };

        Assert.Null(SceneResolver.ResolveFor(House(), scene, North).Brightness);
    }

    [Fact]
    public void Capturing_the_house_keeps_the_two_apart()
    {
        // The bug this replaces: whichever controller was enumerated first set the brightness of
        // both, so capturing this house wrote 255 everywhere or 38 everywhere depending on the
        // order of a dictionary.
        LedBalloonProject house = House();

        Scene scene = SceneResolver.Capture(
            house,
            new Dictionary<string, WledState>
            {
                [South] = Lit(255, 0),
                [North] = Lit(38, 0),
            },
            "As it is");

        Assert.Equal((byte)255, scene.BrightnessOn(South));
        Assert.Equal((byte)38, scene.BrightnessOn(North));
    }

    [Fact]
    public void And_what_it_captures_goes_back_out_the_way_it_came_in()
    {
        LedBalloonProject house = House();

        Scene scene = SceneResolver.Capture(
            house,
            new Dictionary<string, WledState> { [South] = Lit(255, 0), [North] = Lit(38, 0) },
            "As it is");

        Assert.Equal((byte)255, ScenePublisher.BuildFor(house, scene, South).Brightness);
        Assert.Equal((byte)38, ScenePublisher.BuildFor(house, scene, North).Brightness);
    }

    [Fact]
    public void Adopting_a_pair_of_presets_that_disagree_keeps_the_disagreement()
    {
        // A WLED preset of the same name on two controllers is two presets, and nothing makes them
        // agree about brightness. Adoption used to take the first and discard the other.
        var preset = new HousePreset("Evening",
        [
            new PresetPlacement(South, 1, new WledPreset
            {
                Id = 1,
                Name = "Evening",
                On = true,
                Brightness = 255,
                Segments = [new WledSegment { Id = 0, Start = 0, Stop = 5, On = true, Effect = 1 }],
            }),
            new PresetPlacement(North, 4, new WledPreset
            {
                Id = 4,
                Name = "Evening",
                On = true,
                Brightness = 38,
                Segments = [new WledSegment { Id = 0, Start = 0, Stop = 40, On = true, Effect = 1 }],
            }),
        ]);

        AdoptionReport report = PresetAdoption.Plan(House(), preset);

        Assert.Equal((byte)255, report.Scene.BrightnessOn(South));
        Assert.Equal((byte)38, report.Scene.BrightnessOn(North));
    }

    [Fact]
    public async Task A_scene_saved_before_the_split_means_the_same_everywhere()
    {
        // Schema 2 wrote one number for the house. That is what it meant, so that is what it
        // becomes: the same value on every controller, rather than a value on one and silence on
        // the rest - which would leave the others at whatever they happened to be showing.
        string path = Path.Combine(Path.GetTempPath(), $"lb-schema2-{Guid.NewGuid():N}.json");

        await File.WriteAllTextAsync(path, Schema2With("""{"id":"s1","name":"Evening","brightness":90}"""));

        try
        {
            LedBalloonProject loaded = await ProjectStore.LoadAsync(path);
            Scene scene = Assert.Single(loaded.Scenes);

            Assert.Equal((byte)90, scene.BrightnessOn(South));
            Assert.Equal((byte)90, scene.BrightnessOn(North));
            Assert.Null(scene.SharedBrightness);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_scene_saved_after_the_split_is_read_as_it_was_written()
    {
        LedBalloonProject house = House();
        house.Scenes.Add(new Scene { Name = "Uneven", Brightness = { [South] = 255, [North] = 38 } });

        string path = Path.Combine(Path.GetTempPath(), $"lb-roundtrip-{Guid.NewGuid():N}.json");

        try
        {
            await ProjectStore.SaveAsync(house, path);
            LedBalloonProject read = await ProjectStore.LoadAsync(path);
            Scene scene = Assert.Single(read.Scenes);

            Assert.Equal((byte)255, scene.BrightnessOn(South));
            Assert.Equal((byte)38, scene.BrightnessOn(North));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Migrating_twice_does_not_undo_an_edit_made_in_between()
    {
        // Loading happens on every start and on every revert, so it has to be safe to repeat. If
        // the old number were kept, a second pass would put it back over whatever had since been
        // set per controller.
        var scene = new Scene { Name = "Evening", SharedBrightness = 90 };

        scene.SplitSharedBrightness([South, North]);
        scene.SetBrightnessOn(North, 10);
        scene.SplitSharedBrightness([South, North]);

        Assert.Equal((byte)90, scene.BrightnessOn(South));
        Assert.Equal((byte)10, scene.BrightnessOn(North));
    }

    [Fact]
    public void A_scene_that_already_says_it_per_controller_ignores_the_old_field()
    {
        var scene = new Scene
        {
            Name = "Evening",
            SharedBrightness = 90,
            Brightness = { [South] = 200 },
        };

        scene.SplitSharedBrightness([South, North]);

        Assert.Equal((byte)200, scene.BrightnessOn(South));
        Assert.Null(scene.BrightnessOn(North));
        Assert.Null(scene.SharedBrightness);
    }

    [Fact]
    public void Setting_a_controller_to_nothing_takes_it_out_rather_than_storing_a_zero()
    {
        // Zero is a brightness — it is how the "Off" preset on the real South works — so "no
        // opinion" cannot be spelled as a number.
        var scene = new Scene { Name = "Evening", Brightness = { [South] = 255 } };

        scene.SetBrightnessOn(South, null);

        Assert.Null(scene.BrightnessOn(South));
        Assert.Empty(scene.Brightness);

        scene.SetBrightnessOn(South, 0);

        Assert.Equal((byte)0, scene.BrightnessOn(South));
    }

    private static string Schema2With(string scene) =>
        $$"""
        {
          "schema": 2,
          "revision": 4,
          "controllers": [
            { "key": "{{South}}", "name": "South" },
            { "key": "{{North}}", "name": "North" }
          ],
          "segments": [],
          "scenes": [{{scene}}]
        }
        """;
}
