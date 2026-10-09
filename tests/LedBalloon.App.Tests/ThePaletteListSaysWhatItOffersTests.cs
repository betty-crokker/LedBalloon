using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// The palette list has two kinds of thing in it, and used to say so only in WLED's shorthand.
/// <para>
/// Five of the entries are not gradients but recipes made from the segment's own color slots — "*
/// Colors 1&amp;2" is c1, c1, c2, c2 — and they are the only way to put your own colors on the 22
/// ported effects that read no color slot of their own. Getting there meant choosing something
/// called "* Color 1" and knowing what the asterisk meant.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ThePaletteListSaysWhatItOffersTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-palette-tests-{Guid.NewGuid():N}.json");

    [Theory]
    [InlineData("* Color 1", "My color")]
    [InlineData("* Colors 1&2", "My two colors")]
    [InlineData("* Color Gradient", "My three colors, blended")]
    [InlineData("* Colors Only", "My three colors, in bands")]
    [InlineData("* Random Cycle", "Random colors")]
    public void The_shorthand_is_said_out_loud(string reported, string plain) =>
        Assert.Equal(plain, MainViewModel.PlainName(reported));

    [Fact]
    public void Default_is_named_for_what_it_does_to_the_effect_that_is_chosen()
    {
        // WLED's palette 0 is not one palette. Its color_from_palette hands back a color slot rather
        // than a gradient while the palette is 0, so for most effects it means "use the colors that
        // are set" - and for the seventeen that read no slot even then, the effect supplies its own
        // and the boxes reach nothing. One row, two meanings.
        Assert.Equal("The colors above", MainViewModel.PlainName("Default", defaultIsYourColors: true));
        Assert.Equal("Colors the effect picks itself", MainViewModel.PlainName("Default"));
    }

    [Fact]
    public void Anything_else_keeps_the_name_the_controller_gave_it()
    {
        Assert.Equal("Ocean", MainViewModel.PlainName("Ocean"));

        // Including an asterisked one from a fork that this does not know about. It still joins the
        // group - the asterisk is what says which group - but nobody here gets to rename it.
        Assert.Equal("* Something New", MainViewModel.PlainName("* Something New"));
    }

    [Fact]
    public void The_ones_made_from_your_colors_come_first_and_the_gradients_after() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];

        string[] names = [.. app.SegmentPalettes.Select(p => p.IsSeparator ? "---" : p.Name)];

        Assert.Equal(
            [
                // Solid reads color 1 on Default, so Default is the colors above it.
                "The colors above",
                "My color",
                "My two colors",
                "My three colors, blended",
                "My three colors, in bands",
                "Random colors",
                "---",
                "Analogous",
                "Ocean",

                // Last, because it is not a palette but a way to get one. A controller with none of
                // its own would otherwise offer no way in at all.
                "Make a new palette...",
            ],
            names);
    });

    [Fact]
    public void The_row_is_renamed_when_the_effect_changes_under_it() => ui.Run(async () =>
    {
        // Measured on a GL-C-616WL with pure green in slot 1: Colorwaves on palette 0 rendered 285
        // LEDs of one hue, 120 degrees - the green. Pacifica on the same setting came back 72 hues
        // of its own teals with no green in it anywhere. Same palette, opposite meanings, and the
        // row said only the second, which is how it came to sit over a green color box claiming the
        // effect chose its own colors.
        using FakeController controller = FakeController.Start(
            ["Solid", "Colorwaves", "Pacifica"], ["", "!,Hue;!;!;01", ";;!;01"]);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Colorwaves");
        Assert.Equal("The colors above", app.SegmentPalettes.Single(p => p.Id == 0).Name);

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Pacifica");
        Assert.Equal("Colors the effect picks itself", app.SegmentPalettes.Single(p => p.Id == 0).Name);
    });

    [Fact]
    public void And_renaming_it_does_not_drop_the_selection_or_send_anything() => ui.Run(async () =>
    {
        // Renaming replaces the row, which is the shape of the bug that had this dropdown reverting
        // a beat after it was set - and the rebuild it runs inside is suppressing its own writes, so
        // it has to put that suppression back rather than clear it.
        using FakeController controller = FakeController.Start(
            ["Solid", "Pacifica"], ["", ";;!;01"]);

        MainViewModel app = await HouseAsync(controller);
        app.MasterOn = true;
        Assert.True(await controller.WaitForPostAsync(TimeSpan.FromSeconds(2)));

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Id == 0);

        int before = controller.Posts.Count;

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Pacifica");

        await Task.Delay(300);

        Assert.Equal("Colors the effect picks itself", app.SegmentPaletteChoice?.Name);
        Assert.Equal(0, app.SegmentPaletteChoice?.Id);

        // One post, for the effect that was actually picked. Not a palette on top of it.
        Assert.Equal(before + 1, controller.Posts.Count);
    });

    [Fact]
    public void The_rule_between_them_is_not_something_you_can_choose() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];

        PaletteOption rule = app.SegmentPalettes.Single(p => p.IsSeparator);

        Assert.True(rule.Id < 0);

        // Every row that actually names a palette carries the id the controller answers to, and
        // none of those is negative - which is what keeps the picker from matching a segment's
        // palette against the rule, or against the invitation to make one.
        Assert.DoesNotContain(
            app.SegmentPalettes.Where(p => p.Kind == PaletteKind.Palette), p => p.Id < 0);

        Assert.All(
            app.SegmentPalettes.Where(p => p.Kind != PaletteKind.Palette),
            p => Assert.True(p.Id < 0));
    });

    private async Task<MainViewModel> HouseAsync(FakeController controller)
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch",
            Name = "Porch",
            ControllerKey = FakeController.Key,
            Output = 1,
            Count = 5,
            SegmentId = 0,
        });

        app.LiveSync = true;

        await app.AddDeviceAsync(controller.Host, null);

        return app;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (File.Exists(_preferences))
        {
            File.Delete(_preferences);
        }
    }
}
