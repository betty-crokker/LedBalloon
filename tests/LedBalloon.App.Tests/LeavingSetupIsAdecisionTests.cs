using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What happens on the way out of Setup with changes the controllers have not been told about.
/// <para>
/// Saving the layout is not only writing a document: it re-cuts the segment boundaries on the
/// controllers. Until that has happened the colours screen draws a house the controllers are not set
/// up for — lengthen a run and the box still thinks it is the old length, so the photo, the strip and
/// anything applied disagree with the wall. A screen quietly lying is worse than a question.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class LeavingSetupIsAdecisionTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-leaving-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Nothing_is_asked_when_there_is_nothing_to_lose() => ui.Run(async () =>
    {
        MainViewModel app = await SetupAsync();

        var asked = 0;
        app.Ask = _ => { asked++; return Task.FromResult(ConfirmResult.Cancelled); };

        await app.FinishSetupCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);
        Assert.Equal(AppMode.Design, app.Mode);
    });

    [Fact]
    public void Staying_leaves_both_the_changes_and_the_screen_alone() => ui.Run(async () =>
    {
        MainViewModel app = await SetupAsync();
        Change(app);

        app.Ask = _ => Task.FromResult(ConfirmResult.Cancelled);

        await app.FinishSetupCommand.ExecuteAsync(null);

        Assert.Equal(AppMode.Setup, app.Mode);
        Assert.True(app.HasUnsavedChanges, "the changes are still there to go back to");
    });

    [Fact]
    public void Saving_writes_them_and_then_goes() => ui.Run(async () =>
    {
        MainViewModel app = await SetupAsync();
        Change(app);

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept));

        await app.FinishSetupCommand.ExecuteAsync(null);

        Assert.Equal(AppMode.Design, app.Mode);
        Assert.False(app.HasUnsavedChanges);
    });

    /// <summary>
    /// Throwing them away reads the layout back off the controllers, which is what "discard" means
    /// here: the controllers are the copy of record.
    /// </summary>
    [Fact]
    public void Throwing_them_away_goes_too() => ui.Run(async () =>
    {
        MainViewModel app = await SetupAsync();
        Change(app);

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Alternate));

        await app.FinishSetupCommand.ExecuteAsync(null);

        Assert.Equal(AppMode.Design, app.Mode);
    });

    /// <summary>A change the controllers have not got, made where Setup makes them.</summary>
    private static void Change(MainViewModel app)
    {
        app.Project.Segments.Add(new Segment
        {
            Id = "stairs", Name = "Under stairs", ControllerKey = FakeController.Key,
            Output = 1, Count = 6, SegmentId = 1,
        });

        app.HasUnsavedChanges = true;
    }

    private async Task<MainViewModel> SetupAsync()
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

        app.GoToSetupCommand.Execute(null);
        app.HasUnsavedChanges = false;

        return app;
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
