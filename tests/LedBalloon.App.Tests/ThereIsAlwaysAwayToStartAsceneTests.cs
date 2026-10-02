using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Making a scene, from wherever you happen to be.
/// <para>
/// The button used to appear only while the house was showing something not already written down.
/// That is the moment it is most wanted and it is not the only one: somebody opening the app meaning
/// to build a Christmas scene had nothing on screen to start from, and somebody looking at a saved
/// scene had no way to make a second one like it without first changing something.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ThereIsAlwaysAwayToStartAsceneTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-scene-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void The_name_is_asked_for_up_front() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        ConfirmRequest? asked = null;
        app.Ask = request =>
        {
            asked = request;
            return Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Christmas"));
        };

        await app.NewSceneCommand.ExecuteAsync(null);

        // Rather than making one called "New scene" and saying to rename it, which was two steps
        // and left scenes called "New scene" behind whenever anybody stopped after the first.
        Assert.NotNull(asked);
        Assert.Equal("Scene name", asked.InputLabel);
        Assert.Equal("Christmas", app.Project.Scenes.Single().Name);
    });

    [Fact]
    public void Backing_out_makes_nothing() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        app.Ask = _ => Task.FromResult(ConfirmResult.Cancelled);

        await app.NewSceneCommand.ExecuteAsync(null);

        Assert.Empty(app.Project.Scenes);
    });

    [Fact]
    public void So_does_accepting_with_the_name_rubbed_out() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: string.Empty));

        await app.NewSceneCommand.ExecuteAsync(null);

        // A scene with no name is worse than no scene: the picker is a list of names.
        Assert.Empty(app.Project.Scenes);
    });

    [Fact]
    public void A_second_one_can_be_made_while_a_saved_scene_is_open() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "First"));
        await app.NewSceneCommand.ExecuteAsync(null);

        // Nothing touched in between. This is the case that had no button at all: the house matches
        // the scene that is open, so there was nothing unsaved to offer to name.
        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Second"));
        await app.NewSceneCommand.ExecuteAsync(null);

        Assert.Equal(["First", "Second"], app.Project.Scenes.Select(s => s.Name));
        Assert.Equal("Second", app.SelectedScene?.Name);
    });

    [Fact]
    public void A_name_already_taken_is_made_unique_rather_than_refused() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Christmas"));

        await app.NewSceneCommand.ExecuteAsync(null);
        await app.NewSceneCommand.ExecuteAsync(null);

        // The controllers keep a preset per scene and two of them under one name is a scene that
        // cannot be put back on purpose.
        Assert.Equal(2, app.Project.Scenes.Count);
        Assert.Distinct(app.Project.Scenes.Select(s => s.Name));
    });

    [Fact]
    public void With_no_controller_answering_it_says_so_rather_than_writing_down_nothing()
        => ui.Run(async () =>
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));
        app.Ask = _ => Task.FromResult(new ConfirmResult(ConfirmChoice.Accept, Input: "Christmas"));

        await app.NewSceneCommand.ExecuteAsync(null);

        // A scene is what each run is showing, and with nothing answering there is nothing to read.
        Assert.Empty(app.Project.Scenes);
        Assert.Contains("No connected controller", app.Status, StringComparison.Ordinal);
    });

    private async Task<MainViewModel> HouseAsync(FakeController controller)
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        await app.AddDeviceAsync(controller.Host, null);

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
