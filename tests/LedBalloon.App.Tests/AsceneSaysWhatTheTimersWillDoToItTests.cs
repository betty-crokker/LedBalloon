using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What the scene editor says about the house's own timetable.
/// <para>
/// This screen is where somebody decides what the house looks like, and it is the one screen the
/// timetable is invisible from. A scene can be chosen, adjusted and admired at four in the afternoon
/// while a timer waits to take it off the house at half past five, and the first anybody knows of it
/// is that the house "changed by itself".
/// </para>
/// <para>
/// Two facts, so two sentences, and they must not be swapped: a timer that fires this scene is the
/// scene working, and a timer that fires anything else is the scene being replaced.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AsceneSaysWhatTheTimersWillDoToItTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-timernote-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Nothing_is_said_when_there_are_no_timers() => ui.Run(async () =>
    {
        MainViewModel app = await HouseWithAsceneAsync();

        Assert.Empty(app.SceneTimerNote);
    });

    [Fact]
    public void A_timer_that_fires_this_scene_says_it_comes_up_by_itself() => ui.Run(async () =>
    {
        MainViewModel app = await HouseWithAsceneAsync();

        // Saved first, because a timer can only be pointed at a scene the controllers are already
        // holding - which is also why an unsaved scene is not offered in that picker at all.
        await app.SaveProjectCommand.ExecuteAsync(null);

        ScheduleRow row = Timer(app, at: 17, minute: 0);
        row.Preset = row.Presets.First(p => p.Name == "Christmas");

        Assert.Contains("comes up on the house by itself", app.SceneTimerNote, StringComparison.Ordinal);
        Assert.Contains("5:00 pm", app.SceneTimerNote, StringComparison.Ordinal);
        Assert.DoesNotContain("replaces", app.SceneTimerNote, StringComparison.Ordinal);
    });

    [Fact]
    public void A_timer_that_fires_anything_else_says_this_one_gets_replaced() => ui.Run(async () =>
    {
        MainViewModel app = await HouseWithAsceneAsync();

        // The house switch, which is the timer most houses have and the one most likely to take a
        // scene off the lights without anybody connecting the two.
        Timer(app, at: 23, minute: 30);

        Assert.Contains("replaces whatever is on the house", app.SceneTimerNote, StringComparison.Ordinal);
        Assert.Contains("11:30 pm", app.SceneTimerNote, StringComparison.Ordinal);
        Assert.DoesNotContain("comes up", app.SceneTimerNote, StringComparison.Ordinal);
    });

    /// <summary>
    /// A line that is switched off is not going to do anything, so it is not a warning.
    /// </summary>
    [Fact]
    public void A_timer_that_is_switched_off_is_not_mentioned() => ui.Run(async () =>
    {
        MainViewModel app = await HouseWithAsceneAsync();

        Timer(app, at: 23, minute: 30).Enabled = false;

        Assert.Empty(app.SceneTimerNote);
    });

    private static ScheduleRow Timer(MainViewModel app, int at, int minute)
    {
        app.AddScheduleEntryCommand.Execute(null);

        ScheduleRow row = app.Schedule.Last();
        row.Hour = at;
        row.Minute = minute;

        return row;
    }

    private async Task<MainViewModel> HouseWithAsceneAsync()
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

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Christmas"));
        await app.NewSceneCommand.ExecuteAsync(null);

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
