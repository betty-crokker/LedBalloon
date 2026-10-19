using System.Text;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Reading what a controller comes back to after a power cut, and showing it as what it is.
/// <para>
/// The same trap as the timetable, with a much longer fuse. A boot preset is stored as a slot, the
/// slot means nothing without the name in it, and if the picker comes up empty it is
/// indistinguishable from "come back dark" - which is what the next save would then write over a
/// setting somebody meant. The difference is that a timer getting this wrong shows up that evening,
/// and this shows up during the next power cut, whenever that is.
/// </para>
/// <para>
/// So the case that matters is the one the app did not put there: a boot preset saved from WLED's
/// own pages, under a name this app has never heard of. The controller is the record.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AbootPresetReadsBackAsWhatItIsTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-boot-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void Acontroller_booting_into_apreset_comes_back_naming_it() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync("""{"ps":1,"on":true,"bri":128}""");

        Assert.Equal("Fall", app.ComesBackTo?.Scene);
        Assert.True(app.ComesBackTo?.LightsUp);
        Assert.Contains(app.BootChoices, choice => choice.Scene == "Fall");
    });

    /// <summary>
    /// Said on the screen as well as shown in the picker, because the picker is a list of things it
    /// could be and this is the answer to what it is.
    /// </summary>
    [Fact]
    public void And_says_so_in_words() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync("""{"ps":1,"on":true,"bri":128}""");

        Assert.Contains("comes back to Fall", app.BootNotice, StringComparison.Ordinal);
    });

    [Fact]
    public void Acontroller_that_comes_back_dark_reads_back_as_dark() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync("""{"ps":0,"on":false,"bri":64}""");

        Assert.Equal(BootChoice.Dark, app.ComesBackTo);
        Assert.Equal(64, app.BootBrightness);
        Assert.False(app.BootBrightnessApplies);
        Assert.Contains("comes back dark", app.BootNotice, StringComparison.Ordinal);
    });

    [Fact]
    public void Acontroller_that_comes_back_lit_without_ascene_reads_back_as_that() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync("""{"ps":0,"on":true,"bri":200}""");

        Assert.Equal(BootChoice.Lit, app.ComesBackTo);
        Assert.Equal(200, app.BootBrightness);

        // The one case where the brightness box is deciding something, so it is the one case where
        // it is not greyed out.
        Assert.True(app.BootBrightnessApplies);
    });

    /// <summary>
    /// A slot whose preset nothing here can name leaves the picker empty - and an empty picker
    /// writes nothing, which is how that setting survives a save rather than being replaced by the
    /// nearest thing in the list.
    /// </summary>
    [Fact]
    public void Aboot_preset_nothing_can_name_is_left_alone_rather_than_guessed_at() =>
        ui.Run(async () =>
        {
            MainViewModel app = await HouseAsync("""{"ps":9,"on":true,"bri":128}""");

            Assert.Null(app.ComesBackTo);

            // And it is still reported, because "preset 9" is a thing the reader can go and look at.
            Assert.Contains("comes back to preset 9", app.BootNotice, StringComparison.Ordinal);
        });

    /// <summary>Reading a controller is not touching it, so nothing is owed to the Save button.</summary>
    [Fact]
    public void Reading_it_off_the_controller_is_not_an_unsaved_change() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync("""{"ps":1,"on":true,"bri":128}""");

        Assert.False(app.BootStateChanged);
        Assert.False(app.SomethingHereIsUnsaved);
    });

    [Fact]
    public void Picking_something_else_is_unsaved_work() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync("""{"ps":1,"on":true,"bri":128}""");

        app.ComesBackTo = BootChoice.Dark;

        Assert.True(app.BootStateChanged);
        Assert.True(app.SomethingHereIsUnsaved);
        Assert.True(app.HasUnsavedChanges);
    });

    private async Task<MainViewModel> HouseAsync(string def)
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);
        _open.Add(controller);

        // A preset this app did not write, so nothing but the controller can say what slot 1 holds.
        controller.Files["presets.json"] = Encoding.UTF8.GetBytes(
            """
            {
              "0": {},
              "1": { "n": "Fall", "on": true, "bri": 255,
                     "seg": [ { "id": 0, "start": 0, "stop": 5, "on": true, "fx": 39, "pal": 68 } ] }
            }
            """);

        controller.Configuration =
            $$"""
            { "hw": { "led": { "fps": 42, "total": 5 } },
              "def": {{def}},
              "if": { "ntp": { "en": true, "lt": 40.0, "ln": -105.0, "tz": 6 } },
              "timers": { "ins": [] } }
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
