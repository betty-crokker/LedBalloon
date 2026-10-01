using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A run of LED along a roofline is not a matrix, and the effects that need one were being offered.
/// <para>
/// WLED declares them with a flag and its own UI hides them; this read the flag, checked it against
/// the controller's own filtering and agreed on all 37 of them, and then offered all 37 anyway.
/// Drift Rose is the one that made it obvious: pickable, unrunnable, and sitting under two notes
/// that disagreed about where its colors came from.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheEffectListLeavesOutWhatCannotWorkTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-2d-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void An_effect_that_needs_a_matrix_is_not_offered() => ui.Run(async () =>
    {
        // "Fade,Blur;;;2" is exactly what the controller reports for Drift Rose: a 2 in the flags
        // and no 1, which is WLED for "this one needs a matrix".
        using FakeController controller = FakeController.Start(
            ["Solid", "Blink", "Drift Rose"],
            ["", "!,Duty cycle;!,!;!;01", "Fade,Blur;;;2"]);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];

        Assert.DoesNotContain(app.SegmentEffects, o => o.Name == "Drift Rose");
        Assert.Contains(app.SegmentEffects, o => o.Name == "Blink");
    });

    [Fact]
    public void One_that_works_on_either_is_still_offered() => ui.Run(async () =>
    {
        // A 1 alongside the 2 means it runs on a strip as well, and WLED's own UI keeps it.
        using FakeController controller = FakeController.Start(
            ["Solid", "Ripple"],
            ["", "!,Wave width;;!;12"]);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];

        Assert.Contains(app.SegmentEffects, o => o.Name == "Ripple");
    });

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
