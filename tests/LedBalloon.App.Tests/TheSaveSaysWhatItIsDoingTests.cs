using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// The account a save gives of itself while it runs.
/// <para>
/// Saving the setup is several writes to every controller — the layout, the output lengths and
/// settings, the segment boundaries, the scenes, the timetable, any preset repairs asked for — and
/// on a slow network that is a few seconds of a window that appears to be doing nothing. The buttons
/// disabling themselves say only that something is going on.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheSaveSaysWhatItIsDoingTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-progress-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    /// <summary>Stands in for the window, and remembers what it was told.</summary>
    private sealed class Spy : IProgressHandle
    {
        public List<string> Steps { get; } = [];

        public bool Closed { get; private set; }

        public void Say(string step) => Steps.Add(step);

        public void Dispose() => Closed = true;
    }

    [Fact]
    public void It_names_each_part_as_it_reaches_it() => ui.Run(async () =>
    {
        (MainViewModel app, Spy spy, _) = await SavingAsync();

        // In the order they happen, because the point of showing them is to account for the wait.
        Assert.Equal(
            [
                "Writing the layout",
                "Setting the LED output lengths",
                "Writing the output settings",
                "Moving the segment boundaries to match",
                "Putting the scenes on the controllers",
                "Writing the timetable",
                "Repairing the presets that were asked about",
            ],
            spy.Steps);

        Assert.False(app.IsBusy);
    });

    [Fact]
    public void It_comes_down_when_the_save_finishes() => ui.Run(async () =>
    {
        (_, Spy spy, _) = await SavingAsync();

        Assert.True(spy.Closed);
    });

    /// <summary>
    /// And when it does not finish.
    /// </summary>
    /// <remarks>
    /// The window cannot be closed by hand — there is nothing to cancel the work with, so a close
    /// button would either lie or leave the controllers half written. That makes a save which
    /// throws the one case where a leaked window would be unclosable, with the app still running
    /// behind it.
    /// </remarks>
    [Fact]
    public void It_comes_down_when_the_save_fails() => ui.Run(async () =>
    {
        (MainViewModel app, Spy spy, FakeController controller) = await SavingAsync(save: false);

        // The controller goes away mid-session, which is what the shed router losing power looks
        // like from here.
        controller.Dispose();
        _open.Remove(controller);

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.True(spy.Closed);
        Assert.False(app.IsBusy);
    });

    private async Task<(MainViewModel App, Spy Spy, FakeController Controller)> SavingAsync(
        bool save = true)
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

        var spy = new Spy();
        app.ShowProgress = _ => spy;

        if (save)
        {
            await app.SaveProjectCommand.ExecuteAsync(null);
        }

        return (app, spy, controller);
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
