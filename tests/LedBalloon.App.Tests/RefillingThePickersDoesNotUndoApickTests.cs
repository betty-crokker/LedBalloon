using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Reloading the palettes must not put the picker back where it was.
/// <para>
/// Both pickers are refilled from what the segment is <em>reported</em> to be showing, which is the
/// right source when a segment is first opened and the wrong one a moment after a change: with the
/// house lit the report does not catch up until the controller echoes it back, so anything refilling
/// the list in that window restores the palette that was just replaced.
/// </para>
/// <para>
/// Reading the palettes again is exactly such a refill, and it happens on a timer at startup and
/// every time the palette editor is closed - neither of which the person choosing a palette knows
/// about. The symptom is a dropdown that reverts a beat after it was set.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class RefillingThePickersDoesNotUndoApickTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-refill-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void Reading_the_palettes_again_leaves_the_chosen_palette_chosen() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        // Lit, so a change has to travel to the controller and back before the report agrees with
        // it. That window is where this goes wrong; with the house off the change is held locally
        // and the report never disagrees at all.
        app.MasterOn = true;
        Assert.True(await controller.WaitForPostAsync(TimeSpan.FromSeconds(2)));

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        // What closing the palette editor does, and what the startup pass does: read the gradients
        // again and refill the pickers.
        app.ShowPaletteEditor = _ => Task.FromResult(true);
        await app.EditPalettesCommand.ExecuteAsync(null);

        Assert.Equal("Ocean", app.SegmentPaletteChoice?.Name);
    });

    [Fact]
    public void And_the_chosen_effect_chosen() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.MasterOn = true;
        Assert.True(await controller.WaitForPostAsync(TimeSpan.FromSeconds(2)));

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Blink");

        app.ShowPaletteEditor = _ => Task.FromResult(true);
        await app.EditPalettesCommand.ExecuteAsync(null);

        Assert.Equal("Blink", app.SegmentEffectChoice?.Name);
    });

    [Fact]
    public void But_opening_a_different_segment_still_reads_that_segment() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(p => p.Name == "Ocean");

        // The other half of the rule: keeping a choice is only right while it is the same segment.
        // Moving to another one has to show what that one is wearing, not what the last one was.
        app.SelectedSegment = null;
        app.SelectedSegment = app.Project.Segments[1];

        Assert.NotEqual("Ocean", app.SegmentPaletteChoice?.Name);
    });

    private async Task<MainViewModel> HouseAsync(FakeController controller)
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        app.Project.Segments.Add(new Segment
        {
            Id = "eaves", Name = "Eaves", ControllerKey = FakeController.Key,
            Output = 2, Count = 5, SegmentId = 1,
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
