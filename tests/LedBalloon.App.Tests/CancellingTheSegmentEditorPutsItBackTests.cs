using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// The segment editor edits live and has a Cancel, which are only compatible one way round.
/// <para>
/// Live is the point: Sync means the same thing inside the editor as everywhere else, so what is
/// being changed can be watched on the house while it is changed. That is what the panel is for, and
/// moving it into a window is no reason to give it up — so Cancel cannot mean "do not apply". It
/// means undo: put the segment back where it was when the dialog opened.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class CancellingTheSegmentEditorPutsItBackTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-editor-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void Cancelling_undoes_what_was_changed_while_it_was_open() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        // Read with the segment selected, since that is what the preview is drawn from.
        app.SelectedSegment = app.Project.Segments[0];
        byte? before = app.PreviewSegment?.Speed;

        Assert.NotNull(before);

        app.ShowSegmentEditor = () =>
        {
            // Standing in for someone dragging the speed slider with the dialog open.
            app.SegmentSpeed = 200;
            return Task.FromResult(false);
        };

        await app.PickSegmentAsync(app.Project.Segments[0]);

        app.SelectedSegment = app.Project.Segments[0];
        Assert.Equal(before, app.PreviewSegment?.Speed);
    });

    [Fact]
    public void Saving_keeps_it() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.ShowSegmentEditor = () =>
        {
            app.SegmentSpeed = 200;
            return Task.FromResult(true);
        };

        await app.PickSegmentAsync(app.Project.Segments[0]);

        app.SelectedSegment = app.Project.Segments[0];
        Assert.Equal<byte?>(200, app.PreviewSegment?.Speed);
    });

    [Fact]
    public void Either_way_it_closes_back_to_the_whole_house() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        app.ShowSegmentEditor = () => Task.FromResult(true);

        await app.PickSegmentAsync(app.Project.Segments[0]);

        // What the "Back to the whole house" button used to be for. Closing the window is the
        // same click, said by the frame instead of by a control inside it.
        Assert.Null(app.SelectedSegment);
    });

    [Fact]
    public void Cancelling_without_changing_anything_sends_nothing() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        // The lights on, so anything sent would actually go rather than waiting for the switch.
        app.MasterOn = true;
        Assert.True(await controller.WaitForPostAsync(TimeSpan.FromSeconds(2)));

        int before = controller.Posts.Count;

        app.ShowSegmentEditor = () => Task.FromResult(false);
        await app.PickSegmentAsync(app.Project.Segments[0]);

        await Task.Delay(400);

        Assert.Equal(before, controller.Posts.Count);
    });

    [Fact]
    public void With_nowhere_to_open_it_the_segment_is_only_picked() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        // No window, so nothing can be opened - and a segment that cannot be edited should at least
        // still be the one the photo highlights.
        app.ShowSegmentEditor = null;

        await app.PickSegmentAsync(app.Project.Segments[0]);

        Assert.Same(app.Project.Segments[0], app.SelectedSegment);
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
