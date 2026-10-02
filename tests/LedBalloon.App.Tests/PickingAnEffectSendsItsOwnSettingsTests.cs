using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What goes to the controller when somebody picks an effect.
/// <para>
/// It used to be the effect number and nothing else, so the effect arrived carrying whatever the
/// last one had been set to. Measured on 192.0.2.12: Palette declares Animate Shift on, and
/// without it the gradient is laid across the run and never moved - the same effect looking like
/// two different things depending on whether WLED's own app or this one had picked it.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class PickingAnEffectSendsItsOwnSettingsTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-defaults-tests-{Guid.NewGuid():N}.json");

    /// <summary>Palette's entry, verbatim.</summary>
    private const string Palette =
        "Shift,Size,Rotation,,,Animate Shift,Animate Rotation,Anamorphic;;!;12;ix=112,c1=0,o1=1,o2=0,o3=1";

    [Fact]
    public void The_effect_goes_with_the_settings_it_asks_for() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start(["Solid", "Palette"], ["", Palette]);
        MainViewModel app = await HouseAsync(controller);

        app.MasterOn = true;
        Assert.True(await controller.WaitForPostAsync(TimeSpan.FromSeconds(2)));

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Palette");

        Assert.True(await controller.WaitForPostAsync(TimeSpan.FromSeconds(2)));
        await Task.Delay(300);

        string sent = controller.Posts[^1];

        Assert.Contains("\"ix\":112", sent, StringComparison.Ordinal);
        Assert.Contains("\"o1\":true", sent, StringComparison.Ordinal);
        Assert.Contains("\"o2\":false", sent, StringComparison.Ordinal);
        Assert.Contains("\"o3\":true", sent, StringComparison.Ordinal);
        Assert.Contains("\"c1\":0", sent, StringComparison.Ordinal);
    });

    [Fact]
    public void The_sliders_move_to_match_rather_than_waiting_to_be_told() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start(["Solid", "Palette"], ["", Palette]);
        MainViewModel app = await HouseAsync(controller);

        app.SegmentIntensity = 20;

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Palette");

        // The report does not catch up for as long as it takes the controller to answer, and a
        // control left where it was for that beat is the shape of the bug that had the palette
        // picker reverting.
        Assert.Equal(112, app.SegmentIntensity);
    });

    [Fact]
    public void An_effect_that_asks_for_nothing_leaves_everything_alone() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start(
            ["Solid", "Blink"], ["", "!,Duty cycle;!,!;!;01"]);

        MainViewModel app = await HouseAsync(controller);

        app.SegmentSpeed = 77;

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Blink");

        Assert.Equal(77, app.SegmentSpeed);
    });

    [Fact]
    public void The_extra_sliders_and_boxes_appear_under_the_names_the_effect_gives_them()
        => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start(["Solid", "Palette"], ["", Palette]);
        MainViewModel app = await HouseAsync(controller);

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Palette");

        Assert.Equal(["Rotation"], app.SegmentKnobs.Select(k => k.Label));
        Assert.Equal(
            ["Animate Shift", "Animate Rotation", "Anamorphic"],
            app.SegmentSwitches.Select(k => k.Label));
    });

    [Fact]
    public void And_go_away_for_an_effect_that_reads_none_of_them() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start(
            ["Solid", "Palette"], ["", Palette]);

        MainViewModel app = await HouseAsync(controller);

        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Palette");
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Solid");

        Assert.Empty(app.SegmentKnobs);
        Assert.Empty(app.SegmentSwitches);
    });

    /// <summary>True once the controller has reported its effect list and the metadata behind it.</summary>
    private static bool Ready(MainViewModel app) =>
        app.SegmentController is { } device &&
        device.Effects.Count > 1 &&
        device.Device.MetadataFor(1).Declared;

    private async Task<MainViewModel> HouseAsync(FakeController controller)
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        app.LiveSync = true;

        await app.AddDeviceAsync(controller.Host, null);
        app.SelectedSegment = app.Project.Segments[0];

        // The metadata as well as the effect list: an effect whose fxdata has not arrived declares
        // nothing, which is a different test from the one these are trying to run.
        for (int tries = 0; tries < 60 && !Ready(app); tries++)
        {
            await Task.Delay(50);
        }

        Assert.True(Ready(app));

        return app;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (File.Exists(_preferences))
        {
            File.Delete(_preferences);
        }
    }
}
