using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Whether the Save button on the main screen is live.
/// <para>
/// It was only ever offered in Setup, so everything made on the colour screen — scenes, looks —
/// had no button at all: the status bar said "Unsaved changes" and left the reader to go and find
/// another page to deal with it on. Now it is here, and greyed rather than hidden, so "is all of
/// this written down" has one place to be asked whatever the answer is.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheSaveButtonSaysWhetherThereIsAnythingToSaveTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-savebtn-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Nothing_changed_means_nothing_to_press() => ui.Run(async () =>
    {
        (MainViewModel app, _) = await HouseAsync();

        app.HasUnsavedChanges = false;

        Assert.False(app.SaveProjectCommand.CanExecute(null));
    });

    [Fact]
    public void A_change_lights_it_up() => ui.Run(async () =>
    {
        (MainViewModel app, _) = await HouseAsync();

        app.HasUnsavedChanges = true;

        Assert.True(app.SaveProjectCommand.CanExecute(null));
    });

    /// <summary>
    /// And a save already running takes it away again, so it cannot be pressed twice.
    /// </summary>
    [Fact]
    public void A_save_in_flight_takes_it_away() => ui.Run(async () =>
    {
        (MainViewModel app, _) = await HouseAsync();

        app.HasUnsavedChanges = true;
        app.IsBusy = true;

        Assert.False(app.SaveProjectCommand.CanExecute(null));
    });

    private async Task<(MainViewModel App, FakeController Controller)> HouseAsync()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);
        _open.Add(controller);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        await app.AddDeviceAsync(controller.Host, null);

        return (app, controller);
    }

    public void Dispose()
    {
        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }

        File.Delete(_preferences);
        GC.SuppressFinalize(this);
    }
}
