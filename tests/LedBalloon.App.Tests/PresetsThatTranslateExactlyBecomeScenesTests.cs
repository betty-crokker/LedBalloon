using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Presets the app takes on by itself, and the ones it leaves alone.
/// <para>
/// Adoption is the translation from a preset, which addresses LED numbers, to a scene, which
/// addresses runs. It is a decision when the translation loses something — a preset made against a
/// layout that has since moved may light ranges matching no run, and nobody should find that out
/// from the house. It is not a decision when it loses nothing.
/// </para>
/// <para>
/// The adopted form is the better one to hold: a scene follows the layout, so correcting a run's
/// length makes it cover the new length by itself, where a preset pins the length it was saved with.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class PresetsThatTranslateExactlyBecomeScenesTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-adopt-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void A_preset_that_covers_the_run_exactly_is_a_scene_without_being_asked() =>
        ui.Run(async () =>
    {
        // The fake's segment is 0..10, and the layout says the same, so the translation is exact.
        MainViewModel app = await HouseAsync(count: 10);

        Assert.Contains(app.Project.Scenes, scene =>
            string.Equals(scene.Name, "Evening", StringComparison.Ordinal));
    });

    /// <summary>
    /// It must land in the slot it is already in, not beside it.
    /// </summary>
    /// <remarks>
    /// The preset is on the controller under this name, and a timer or a wall button may be
    /// pointing at that slot. Saving a second copy under a new name would leave the original behind
    /// and the timer firing the version that is no longer being edited.
    /// </remarks>
    [Fact]
    public void It_keeps_the_name_it_is_already_published_under() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync(count: 10);

        Scene adopted = app.Project.Scenes.Single(s => s.Name == "Evening");

        Assert.Equal("Evening", adopted.PublishedAs);
    });

    /// <summary>
    /// A preset whose segments do not fit the runs is left for somebody to look at.
    /// </summary>
    [Fact]
    public void A_preset_the_layout_cannot_account_for_is_left_alone() => ui.Run(async () =>
    {
        // The layout says this run is 40 LEDs; the preset lights 0..10 of it, which is a quarter -
        // under the half a segment needs before the run takes its appearance.
        MainViewModel app = await HouseAsync(count: 40);

        Assert.DoesNotContain(app.Project.Scenes, scene =>
            string.Equals(scene.Name, "Evening", StringComparison.Ordinal));
    });

    /// <summary>
    /// The two presets the app writes for the timers are not scenes and must never become ones.
    /// </summary>
    /// <remarks>
    /// Adopting "Everything off" would put a scene that turns the house off in the list beside the
    /// scenes, one click from being applied by accident.
    /// </remarks>
    [Fact]
    public void The_presets_behind_the_timers_are_never_adopted() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync(count: 10);

        Assert.DoesNotContain(app.Project.Scenes, scene =>
            TimedSwitch.SwitchIn(scene.Name) is not null);
    });

    private async Task<MainViewModel> HouseAsync(int count)
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);

        // One preset already on the box, lighting LEDs 0 to 10 of its single run, and the app's own
        // switch preset beside it so the exclusion of those can be seen.
        controller.Files["presets.json"] = System.Text.Encoding.UTF8.GetBytes(
            """
            {
              "0": {},
              "1": { "n": "Evening", "on": true, "bri": 128,
                     "seg": [ { "id": 0, "start": 0, "stop": 10, "on": true,
                                "col": [[255,0,0],[0,0,0],[0,0,0]],
                                "fx": 0, "sx": 128, "ix": 128, "pal": 0 } ] },
              "2": { "n": "Everything off", "on": false }
            }
            """);

        _open.Add(controller);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = count, SegmentId = 0,
        });

        await app.AddDeviceAsync(controller.Host, null);

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
