using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What the preview strip in the segment panel is drawn from.
/// <para>
/// The photo cannot answer "what is this segment doing". A run is drawn on the photo where it
/// actually is — foreshortened, behind a downpipe, or diffused into scallops that deliberately hide
/// the individual LEDs — with a dozen others competing for the same few hundred pixels. The strip is
/// the same segment straightened out and given the width of the panel, so it is the control that
/// answers for the one being edited.
/// </para>
/// <para>
/// Which makes where it reads from the whole question. It reads the same state as the swatches, the
/// pickers and the sliders, rather than a second description assembled from them — two descriptions
/// of one segment drift, and the one that drifts is always the one being looked at.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheStripPreviewShowsTheSegmentTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-preview-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void It_reads_the_run_the_layout_describes() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];

        // From the layout, not from the controller. Length is part of the effect rather than just
        // the width it is drawn at - Chase fits a fixed number of groups into whatever it is given -
        // and the controller's segment table can be stale, since recalling a preset saved with other
        // bounds resizes it.
        Assert.Equal(5, app.PreviewLedCount);
        Assert.NotNull(app.PreviewSegment);
    });

    [Fact]
    public void Nothing_is_selected_and_there_is_nothing_to_draw() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        Assert.Null(app.PreviewSegment);
        Assert.Equal(0, app.PreviewLedCount);
    });

    /// <summary>
    /// The regression that holding edits in the dark introduced, and the reason this matters.
    /// </summary>
    /// <remarks>
    /// Edits made while the house is off no longer go to the controller, so the panel cannot read
    /// them back off it. Everything in the editor is drawn from the same place, so leaving them out
    /// meant clicking away from a segment edited in the dark and clicking back showed the
    /// controller's old settings - the edit was still going to happen, and the panel had forgotten
    /// it had been asked for.
    /// </remarks>
    [Fact]
    public void An_edit_made_in_the_dark_is_still_what_the_panel_reads() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        Assert.False(app.MasterOn, "the fake controller reports itself off");

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentSpeed = 200;

        Assert.Equal<byte?>(200, app.PreviewSegment?.Speed);

        // And it survives looking away and back, which is the way it was noticed.
        app.SelectedSegment = null;
        app.SelectedSegment = app.Project.Segments[0];

        Assert.Equal<byte?>(200, app.PreviewSegment?.Speed);
        Assert.Equal(200, app.SegmentSpeed);
    });

    [Fact]
    public void And_says_nothing_about_it_on_the_banner() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentSpeed = 200;

        // The banner and its Discard button are for changes held because Sync is off, which someone
        // chose and has to undo. These go by themselves, so offering to send them would be offering
        // to do something that is going to happen anyway.
        Assert.False(app.HasPendingChanges);
        Assert.False(app.AppOnly);
    });

    [Fact]
    public void An_effect_the_app_cannot_run_says_so() => ui.Run(async () =>
    {
        // Akemi is one of the 2D effects, which are not ported - rev 2.0 - so its colors are known
        // and its movement is not. A preview that is lying is worse than no preview.
        using FakeController controller = FakeController.Start("Solid", "Akemi");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Akemi");

        Assert.Contains("cannot run this effect", app.SegmentPreviewNote);
    });

    [Fact]
    public void And_one_it_can_run_says_nothing() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Akemi");
        MainViewModel app = await HouseAsync(controller);

        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Solid");

        Assert.Equal(string.Empty, app.SegmentPreviewNote);
    });

    /// <summary>One controller with one five-LED run on it, connected, sync on, lights off.</summary>
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
