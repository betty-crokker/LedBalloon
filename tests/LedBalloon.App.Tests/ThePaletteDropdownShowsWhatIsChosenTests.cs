using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Picking a palette has to leave the picker showing the one that was picked.
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ThePaletteDropdownShowsWhatIsChosenTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-dropdown-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void Choosing_one_leaves_it_chosen() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];

        PaletteOption ocean = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        app.SegmentPaletteChoice = ocean;

        Assert.Same(ocean, app.SegmentPaletteChoice);
    });

    [Fact]
    public void And_is_still_chosen_once_the_controller_has_been_told() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        // Everything the change sets off - the patch, the pending states, the preview - and then
        // ask again.
        await Task.Delay(500);

        Assert.Equal("Ocean", app.SegmentPaletteChoice?.Name);
    });

    [Fact]
    public void An_option_that_is_chosen_is_one_of_the_options_offered() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        // A ComboBox shows its selection by finding it in its items. An instance that is not in
        // there - because the list was rebuilt underneath it - leaves the box showing whatever it
        // had before.
        Assert.Contains(app.SegmentPaletteChoice, app.SegmentPalettes);
    });

    [Fact]
    public void Rebuilding_the_list_keeps_the_segments_own_palette_chosen() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        await Task.Delay(400);

        // Re-selecting the segment refills both pickers from what the segment is showing.
        app.SelectedSegment = null;
        app.SelectedSegment = app.Project.Segments[0];

        Assert.Equal("Ocean", app.SegmentPaletteChoice?.Name);
        Assert.Contains(app.SegmentPaletteChoice, app.SegmentPalettes);
    });

    /// <summary>
    /// The one that was actually wrong: the pictures beside the effect names, not the palette name.
    /// </summary>
    /// <remarks>
    /// Each stamp is drawn against this segment's palette, and they were built once when the
    /// segment was opened. Pick a different palette and the names updated while every picture
    /// beside them went on showing the palette that had just been replaced.
    /// </remarks>
    [Fact]
    public void Changing_the_palette_redraws_the_pictures_beside_the_effects() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];

        PickerOption[] before = [.. app.SegmentEffects];

        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        // Rebuilt, so the instances are new ones - and the chosen effect survived the rebuild.
        Assert.NotSame(before[0], app.SegmentEffects[0]);
        Assert.Equal(before.Length, app.SegmentEffects.Count);
        Assert.Contains(app.SegmentEffectChoice, app.SegmentEffects);
    });

    [Fact]
    public void And_the_effect_stays_chosen_across_that_rebuild() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Blink");

        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        Assert.Equal("Blink", app.SegmentEffectChoice?.Name);
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
