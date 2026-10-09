using System.Text;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Reading a timetable back off the controllers and still knowing what each line fires.
/// <para>
/// Three timers were set — a scene at 23:00, the switch at sunrise, a scene at sunset — saved,
/// and the app closed. Reopened, all three lines were there with their times and triggers intact
/// and two of them pointing at nothing: "pick what it does". The controllers were right the whole
/// time; the slots and the names in them were exactly as written.
/// </para>
/// <para>
/// A line is matched to a choice by name, and the choices are the scenes plus the switch. The
/// switch is a constant, which is why that line alone survived: the scenes were not there yet. The
/// timetable was being read before the project, and the project is where the scenes come from.
/// </para>
/// <para>
/// So this covers the shape rather than the ordering — a line naming a preset the app has no scene
/// for at all must still read back as itself, since the controllers are the record.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AtimerRemembersWhatItFiresTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-timer-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Aline_firing_a_scene_comes_back_naming_it() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        ScheduleRow row = Assert.Single(app.Schedule);

        Assert.Equal("Fall", row.Preset?.Name);
        Assert.Equal(23, row.Hour);
    });

    /// <summary>
    /// And it is offered in the list, so opening the dialog does not blank it on the way past.
    /// </summary>
    [Fact]
    public void And_is_among_the_choices_that_line_offers() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        ScheduleRow row = Assert.Single(app.Schedule);

        Assert.Contains(row.Presets, choice => choice.Name == "Fall");
    });

    private async Task<MainViewModel> HouseAsync()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);
        _open.Add(controller);

        // A preset this app did not write, so nothing but the controller can say what the timer
        // fires. That is the case the old order could never get right.
        controller.Files["presets.json"] = Encoding.UTF8.GetBytes(
            """
            {
              "0": {},
              "1": { "n": "Fall", "on": true, "bri": 255,
                     "seg": [ { "id": 0, "start": 0, "stop": 5, "on": true, "fx": 39, "pal": 68 } ] }
            }
            """);

        controller.Configuration =
            """
            { "if": { "ntp": { "en": true, "lt": 40.0, "ln": -105.0, "tz": 6 } },
              "timers": { "ins": [ { "en": 1, "hour": 23, "min": 0, "macro": 1, "dow": 127 } ] } }
            """;

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        await app.AddDeviceAsync(controller.Host, null);

        // The scan does this after the project; adding one by hand does not, so say it here.
        await app.LoadScheduleAsync();

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
