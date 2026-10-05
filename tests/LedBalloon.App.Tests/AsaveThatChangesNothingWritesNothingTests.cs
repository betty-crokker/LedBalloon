using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Pressing Save when nothing has happened since the last one.
/// <para>
/// Every save wrote the whole project to every controller and bumped the revision whether or not a
/// byte of it had changed, so saving after turning a light on rewrote the layout — and the progress
/// window named each step as though it had work to do. Flash has a finite write budget, and saying
/// "written" about an unchanged file is a small lie that makes the true ones harder to believe.
/// </para>
/// <para>
/// What is compared is the project's fingerprint, which covers everything except the revision and
/// the time of saving — the two things a save changes by happening. So the question asked is
/// whether the house changed, not whether a save was requested.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AsaveThatChangesNothingWritesNothingTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-nowrite-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void The_second_save_does_not_write_the_layout_again() => ui.Run(async () =>
    {
        (MainViewModel app, FakeController controller) = await HouseAsync();

        await app.SaveProjectCommand.ExecuteAsync(null);
        int afterFirst = Layouts(controller);

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Equal(afterFirst, Layouts(controller));
    });

    /// <summary>
    /// And the revision does not move, since it is the thing that tells two machines apart.
    /// </summary>
    [Fact]
    public void The_revision_stays_where_it_was() => ui.Run(async () =>
    {
        (MainViewModel app, _) = await HouseAsync();

        await app.SaveProjectCommand.ExecuteAsync(null);
        int settled = app.Project.Revision;

        await app.SaveProjectCommand.ExecuteAsync(null);
        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Equal(settled, app.Project.Revision);
    });

    /// <summary>
    /// Said plainly rather than as "Saved revision 36 to ." with nothing after the "to".
    /// </summary>
    /// <remarks>
    /// The layout is what is asserted on rather than the whole sentence. The stand-in controller
    /// does not report its segments back the way a real one does, so the step that re-cuts them
    /// believes it has work every time and says so - which is a limit of the stand-in, not of the
    /// save. What matters here is that the layout write is named as skipped.
    /// </remarks>
    [Fact]
    public void It_says_that_nothing_needed_writing() => ui.Run(async () =>
    {
        (MainViewModel app, _) = await HouseAsync();

        await app.SaveProjectCommand.ExecuteAsync(null);
        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Contains("already on the controllers", app.Status, StringComparison.Ordinal);
    });

    /// <summary>
    /// And a real change still goes out, which is the half that matters.
    /// </summary>
    [Fact]
    public void Achange_is_still_written() => ui.Run(async () =>
    {
        (MainViewModel app, FakeController controller) = await HouseAsync();

        await app.SaveProjectCommand.ExecuteAsync(null);
        int afterFirst = Layouts(controller);

        app.Project.Segments[0].Name = "Front porch";

        await app.SaveProjectCommand.ExecuteAsync(null);

        Assert.Equal(afterFirst + 1, Layouts(controller));
    });

    private static int Layouts(FakeController controller) =>
        controller.Written.Count(name => name.Contains("ledballoon.json", StringComparison.Ordinal));

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
