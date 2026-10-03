using System.Text.Json;
using LedBalloon.App.ViewModels;
using LedBalloon.Core;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Putting the lights on a timetable without having to know what a preset is.
/// <para>
/// A WLED timer stores a preset slot and nothing else, so "off at half past ten" is really "apply
/// the preset in slot N, and make sure slot N holds something that is off". That is a fact about the
/// firmware, and nobody should have to learn it to have the porch go dark at bedtime - so the app
/// writes the preset itself, on every controller, and the timetable points at wherever it landed.
/// </para>
/// <para>
/// Whole-house, unlike the scene timers beside it. A scene sits in a different slot on each box, so
/// a scene timer has to say which box it belongs to; the switch is the app's own preset and can be
/// guaranteed on all of them. One line means the two boxes cannot be left disagreeing about when the
/// house goes dark.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class OneLineTurnsTheWholeHouseOnAndOffTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-switch-tests-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    private const string North = "11:22:33:44:55:66";
    private const string South = "66:55:44:33:22:11";

    [Fact]
    public void A_timer_can_be_pointed_at_the_switch_as_well_as_at_a_scene() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        app.AddSwitchTimerCommand.Execute(null);

        ScheduleRow row = Assert.Single(app.Schedule);

        // Not one controller's. The row says so, and the save fans it out.
        Assert.Null(row.ControllerKey);
        Assert.Equal("The whole house", row.Where);
        Assert.True(row.IsSwitch);
        Assert.False(row.SwitchOn);
    });

    [Fact]
    public void Saving_puts_the_preset_on_every_controller_and_points_each_timetable_at_it() =>
        ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        app.AddSwitchTimerCommand.Execute(null);
        ScheduleRow row = app.Schedule.Single();
        row.Hour = 22;
        row.Minute = 30;

        await app.SaveScheduleCommand.ExecuteAsync(null);

        foreach (FakeController controller in _open)
        {
            // The preset the timer fires has to exist on the box the timer is on, or the timer
            // fires nothing. This is the part a person would otherwise have to do by hand, twice.
            Assert.Contains("presets.json", controller.Files.Keys);

            string written = System.Text.Encoding.UTF8.GetString(controller.Files["presets.json"]);
            Assert.Contains(TimedSwitch.OffName, written, StringComparison.Ordinal);

            // And the timetable on that box points at a slot, at the time asked for.
            IReadOnlyList<ScheduledChange> entries = Timetable(controller);
            ScheduledChange entry = Assert.Single(entries);

            Assert.True(entry.Enabled);
            Assert.Equal(22, entry.Hour);
            Assert.Equal(30, entry.Minute);
            Assert.True(entry.PresetId > 0, "the timer has to name a slot");
        }
    });

    /// <summary>
    /// Reading the timetables back has to give one line again, not one per controller.
    /// </summary>
    /// <remarks>
    /// Two lines would invite editing one and not the other, which is the state the whole-house row
    /// exists to make unreachable - and the house would then go dark in two halves.
    /// </remarks>
    [Fact]
    public void The_copies_on_each_controller_read_back_as_one_line() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        app.AddSwitchTimerCommand.Execute(null);
        await app.SaveScheduleCommand.ExecuteAsync(null);

        // SaveScheduleAsync reloads, so what is on screen now is what came back off the two boxes.
        ScheduleRow row = Assert.Single(app.Schedule);

        Assert.True(row.IsSwitch);
        Assert.Null(row.ControllerKey);
    });

    private static IReadOnlyList<ScheduledChange> Timetable(FakeController controller)
    {
        using JsonDocument document = JsonDocument.Parse(controller.Configuration);
        return WledSchedule.Parse(document.RootElement);
    }

    private async Task<MainViewModel> HouseAsync()
    {
        FakeController north = FakeController.Start(["Solid"], null, null, North);
        FakeController south = FakeController.Start(["Solid"], null, null, South);
        _open.Add(north);
        _open.Add(south);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        await app.AddDeviceAsync(north.Host, null);
        await app.AddDeviceAsync(south.Host, null);

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
