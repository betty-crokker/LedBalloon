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
    [InlineData("Default", "The effect's own colors")]
    public void The_shorthand_is_said_out_loud(string reported, string plain) =>
        Assert.Equal(plain, MainViewModel.PlainName(reported));

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
                "The effect's own colors",
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
