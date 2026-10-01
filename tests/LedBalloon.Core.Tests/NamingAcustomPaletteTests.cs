using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Custom palettes get names here, and editing one says what it reaches.
/// <para>
/// WLED knows a custom palette only as a position in a file list counted down from 255, so its own
/// UI calls them "Custom 0". The name has to live in the project because there is nowhere on the
/// controller to keep it — an extra key in the palette file does survive the firmware, but WLED's
/// own editor rewrites that file and would drop it.
/// </para>
/// <para>
/// And the reach is the surprise, the same way it is for a look but with less to go on: a look has a
/// name and sits in a list, while a custom palette is a file several scenes may quietly be built on.
/// </para>
/// </summary>
public class NamingAcustomPaletteTests
{
    private const string North = "aa:bb:cc:dd:ee:01";
    private const string South = "aa:bb:cc:dd:ee:02";

    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject();

        project.Segments.Add(new Segment { Id = "porchline", Name = "Porchline", ControllerKey = North, Count = 10 });
        project.Segments.Add(new Segment { Id = "stairs", Name = "Under stairs", ControllerKey = North, Count = 10 });
        project.Segments.Add(new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Count = 10 });

        return project;
    }

    private static Scene Scene(string name, params (string Segment, SceneEntry Entry)[] worn)
    {
        var scene = new Scene { Name = name };

        foreach ((string segment, SceneEntry entry) in worn)
        {
            scene.Segments[segment] = entry;
        }

        return scene;
    }

    [Fact]
    public void A_palette_keeps_its_name_per_controller()
    {
        LedBalloonProject project = House();

        // The same id on two boxes is two different gradients: 254 is North's palette1.json and
        // South's, and nothing says they match.
        project.NamePalette(North, 254, "July 4th");
        project.NamePalette(South, 254, "Icicles");

        Assert.Equal("July 4th", project.NameForPalette(North, 254));
        Assert.Equal("Icicles", project.NameForPalette(South, 254));
        Assert.Null(project.NameForPalette(North, 255));
    }

    [Fact]
    public void Naming_it_again_replaces_rather_than_doubles_up()
    {
        LedBalloonProject project = House();

        project.NamePalette(North, 254, "July 4th");
        project.NamePalette(North, 254, "Independence");

        Assert.Equal("Independence", project.NameForPalette(North, 254));
        Assert.Single(project.PaletteNames);
    }

    [Fact]
    public void Clearing_the_name_forgets_it()
    {
        LedBalloonProject project = House();

        project.NamePalette(North, 254, "July 4th");
        project.NamePalette(North, 254, null);

        Assert.Null(project.NameForPalette(North, 254));
        Assert.Empty(project.PaletteNames);
    }

    [Fact]
    public void A_palette_nothing_uses_is_used_nowhere()
    {
        Assert.Empty(CustomPaletteUsage.Find(House(), North, 254));
    }

    [Fact]
    public void A_built_in_palette_is_not_this_kind_of_thing_at_all()
    {
        LedBalloonProject project = House();
        project.Scenes.Add(Scene("Winter", ("porchline", new SceneEntry { Palette = 11 })));

        // Ocean is firmware data, the same on every box, and nobody can edit it. Only the uploads
        // are worth counting.
        Assert.Empty(CustomPaletteUsage.Find(project, North, 11));
    }

    [Fact]
    public void One_scene_using_it_directly_is_not_worth_a_warning()
    {
        LedBalloonProject project = House();
        project.Scenes.Add(Scene("Winter", ("porchline", new SceneEntry { Palette = 254 })));

        IReadOnlyList<PaletteUse> uses = CustomPaletteUsage.Find(project, North, 254);

        Assert.Single(uses);
        Assert.Equal(PaletteUseKind.Scene, uses[0].Kind);
        Assert.Equal("Winter", uses[0].Name);

        // Editing a palette one scene uses is just editing that scene. A warning every time is a
        // warning nobody reads.
        Assert.Empty(CustomPaletteUsage.Warn(uses));
    }

    [Fact]
    public void Two_scenes_using_it_is()
    {
        LedBalloonProject project = House();
        project.Scenes.Add(Scene("Winter", ("porchline", new SceneEntry { Palette = 254 })));
        project.Scenes.Add(Scene("Fourth", ("stairs", new SceneEntry { Palette = 254 })));

        string warning = CustomPaletteUsage.Warn(CustomPaletteUsage.Find(project, North, 254));

        Assert.Contains("'Winter' and 'Fourth'", warning);
        Assert.Contains("Changing it changes all of them", warning);
    }

    [Fact]
    public void A_look_is_named_once_however_many_scenes_wear_it()
    {
        LedBalloonProject project = House();
        project.Looks.Add(new Look { Id = "icicles", Name = "Icicles", Palette = 254 });

        foreach (string name in new[] { "Winter", "Fourth", "Advent" })
        {
            project.Scenes.Add(Scene(name, ("porchline", new SceneEntry { LookId = "icicles" })));
        }

        IReadOnlyList<PaletteUse> uses = CustomPaletteUsage.Find(project, North, 254);

        // One entry, not three. The look is the thing being changed; listing the scenes behind it
        // would read as three separate decisions when it is one.
        Assert.Single(uses);
        Assert.Equal(PaletteUseKind.Look, uses[0].Kind);
        Assert.Equal("Icicles", uses[0].Name);
    }

    [Fact]
    public void A_look_and_a_scene_together_are_two_places()
    {
        LedBalloonProject project = House();
        project.Looks.Add(new Look { Id = "icicles", Name = "Icicles", Palette = 254 });
        project.Scenes.Add(Scene("Winter", ("porchline", new SceneEntry { LookId = "icicles" })));
        project.Scenes.Add(Scene("Fourth", ("stairs", new SceneEntry { Palette = 254 })));

        string warning = CustomPaletteUsage.Warn(CustomPaletteUsage.Find(project, North, 254));

        Assert.Contains("the look 'Icicles' and 'Fourth'", warning);
    }

    [Fact]
    public void The_other_controllers_scenes_are_not_counted()
    {
        LedBalloonProject project = House();
        project.Scenes.Add(Scene("Winter",
            ("porchline", new SceneEntry { Palette = 254 }),
            ("garage", new SceneEntry { Palette = 254 })));

        // Garage is on South, so it is asking for South's 254, which is a different gradient and may
        // not even exist. Editing North's must not claim it reaches the garage.
        Assert.Single(CustomPaletteUsage.Find(project, North, 254));
        Assert.Single(CustomPaletteUsage.Find(project, South, 254));
    }
}
