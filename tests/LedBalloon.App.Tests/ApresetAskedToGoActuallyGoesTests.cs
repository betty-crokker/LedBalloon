using System.Text;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A repair asked for on a stale preset, and whether it survives long enough to happen.
/// <para>
/// It did not. The intention was held on the row, and the rows do not survive: the list is rebuilt
/// whenever the presets are read, and a save reads them several times over — publishing the scenes
/// and writing the timetable both end by reading them back. So the flag was thrown away partway
/// through the very save that was meant to act on it, and the delete found nothing pending by the
/// time it looked. The progress window named the step and the preset was still there afterwards,
/// which is the worst way for this to fail: it looked exactly like success.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ApresetAskedToGoActuallyGoesTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-repair-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Asking_for_a_delete_and_saving_takes_it_off_the_controller() => ui.Run(async () =>
    {
        (MainViewModel app, FakeController controller) = await HouseAsync();

        PresetGapRow row = Assert.Single(app.PresetGaps);
        Assert.Equal("Half the strip", row.Name);

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept));
        await app.DeletePresetCommand.ExecuteAsync(row);

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Half the strip", Presets(controller), StringComparison.Ordinal);
    });

    /// <summary>
    /// And the row goes with it, since the trouble has gone.
    /// </summary>
    [Fact]
    public void The_banner_clears_once_it_has_happened() => ui.Run(async () =>
    {
        (MainViewModel app, _) = await HouseAsync();

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept));
        await app.DeletePresetCommand.ExecuteAsync(app.PresetGaps.Single());

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Empty(app.PresetGaps);
        Assert.False(app.HasPresetGaps);
    });

    /// <summary>
    /// Taking it back before saving leaves the preset alone.
    /// </summary>
    [Fact]
    public void Leaving_it_alone_leaves_it_alone() => ui.Run(async () =>
    {
        (MainViewModel app, FakeController controller) = await HouseAsync();

        PresetGapRow row = app.PresetGaps.Single();

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept));
        await app.DeletePresetCommand.ExecuteAsync(row);
        app.LeavePresetCommand.Execute(row);

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Contains("Half the strip", Presets(controller), StringComparison.Ordinal);
    });

    private static string Presets(FakeController controller) =>
        controller.Files.TryGetValue("presets.json", out byte[]? file)
            ? Encoding.UTF8.GetString(file)
            : string.Empty;

    private async Task<(MainViewModel App, FakeController Controller)> HouseAsync()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);
        _open.Add(controller);

        // The controller drives 10 LEDs; this preset pins its segment at 5, so half the strip would
        // stay dark when it runs. That is the shape the audit looks for.
        controller.Files["presets.json"] = Encoding.UTF8.GetBytes(
            """
            {
              "0": {},
              "1": { "n": "Half the strip", "on": true, "bri": 128,
                     "seg": [ { "id": 0, "start": 0, "stop": 5, "on": true,
                                "col": [[255,0,0],[0,0,0],[0,0,0]],
                                "fx": 0, "sx": 128, "ix": 128, "pal": 0 } ] }
            }
            """);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 10, SegmentId = 0,
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
