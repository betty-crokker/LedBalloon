using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Saying out loud that presets.json has been written.
/// <para>
/// Writing it through the file editor leaves WLED's own presets-modified timestamp alone, and that
/// timestamp is how every WLED client decides whether its cached list of preset names is still
/// good. So the file was right and the stamp said nothing had happened: the web UI went on showing
/// a list from weeks earlier, through a hard reload, because the cache is in localStorage rather
/// than the browser's. Two people read that list and concluded the app had never saved anything —
/// right about what they saw, wrong about why, and it cost an afternoon to tell the difference.
/// </para>
/// <para>
/// Deleting a slot that holds nothing is the cheapest way through the code that stamps the time.
/// The file comes back byte for byte identical; only the stamp moves.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheControllerIsToldItsPresetsChangedTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-pmt-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Publishing_a_scene_tells_the_controller_its_presets_moved() => ui.Run(async () =>
    {
        (MainViewModel app, FakeController controller) = await HouseAsync();

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Fall"));
        await app.NewSceneCommand.ExecuteAsync(null);

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Contains(controller.Posts, post => post.Contains("pdel", StringComparison.Ordinal));
    });

    /// <summary>
    /// And at a slot the file leaves empty, since the point is to change no preset at all.
    /// </summary>
    [Fact]
    public void It_deletes_a_slot_that_holds_nothing() => ui.Run(async () =>
    {
        (MainViewModel app, FakeController controller) = await HouseAsync();

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Fall"));
        await app.NewSceneCommand.ExecuteAsync(null);
        await app.SaveProjectCommand.ExecuteAsync(null);

        string asked = Assert.Single(
            controller.Posts, post => post.Contains("pdel", StringComparison.Ordinal));

        int slot = int.Parse(
            new string([.. asked.Where(char.IsDigit)]), System.Globalization.CultureInfo.InvariantCulture);

        string presets = controller.Files.TryGetValue("presets.json", out byte[]? file)
            ? System.Text.Encoding.UTF8.GetString(file)
            : string.Empty;

        Assert.DoesNotContain($"\"{slot}\":{{\"n\"", presets, StringComparison.Ordinal);
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
