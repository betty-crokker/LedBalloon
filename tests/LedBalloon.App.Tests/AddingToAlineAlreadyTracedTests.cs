using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Carrying on with a line that has already been traced.
/// <para>
/// Re-tracing was the only way to change anything about a drawn run, which charged several minutes
/// of careful clicking for adding two points to the end of a porch. And a run that hangs in two
/// places — the stairs are two rails with a landing between — could not be drawn at all.
/// </para>
/// <para>
/// Both are the same mechanism: keep what is there, and record a gap where the line jumps.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AddingToAlineAlreadyTracedTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-addline-{Guid.NewGuid():N}.json");

    [Fact]
    public void The_points_already_there_are_kept() => ui.Run(() =>
    {
        (MainViewModel app, Segment stairs) = House();

        app.AddAnotherLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);

        Assert.Equal(3, stairs.Path.Count);
        Assert.Equal(new LayoutPoint(0.1, 0.5), stairs.Path[0]);

        return Task.CompletedTask;
    });

    [Fact]
    public void The_jump_to_the_new_stretch_is_recorded_as_a_gap() => ui.Run(() =>
    {
        (MainViewModel app, Segment stairs) = House();

        app.AddAnotherLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);
        app.AddPointToSelectedSegment(0.9, 0.5);

        // The span from the old last point to the new first one, and only that one.
        Assert.Equal([1], stairs.Breaks);
        Assert.True(stairs.IsBreak(1));
        Assert.False(stairs.IsBreak(2));

        return Task.CompletedTask;
    });

    /// <summary>
    /// Tracing from scratch throws the gaps away with the points, since neither describes the
    /// line being drawn now.
    /// </summary>
    [Fact]
    public void Starting_over_clears_the_gaps_too() => ui.Run(() =>
    {
        (MainViewModel app, Segment stairs) = House();

        app.AddAnotherLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);

        app.StartDrawingSegmentCommand.Execute(null);

        Assert.Empty(stairs.Path);
        Assert.Empty(stairs.Breaks);

        return Task.CompletedTask;
    });

    /// <summary>
    /// And there is nothing to add to before anything has been traced.
    /// </summary>
    [Fact]
    public void There_is_nothing_to_add_to_an_untraced_run() => ui.Run(() =>
    {
        (MainViewModel app, Segment stairs) = House();
        stairs.Path.Clear();

        app.AddAnotherLineCommand.Execute(null);

        Assert.False(app.IsDrawingSegment);

        return Task.CompletedTask;
    });

    private (MainViewModel App, Segment Stairs) House()
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        var stairs = new Segment
        {
            Id = "stairs", Name = "Under stairs", ControllerKey = FakeController.Key,
            Output = 2, Count = 48, SegmentId = 1,
            Path = [new LayoutPoint(0.1, 0.5), new LayoutPoint(0.2, 0.5)],
        };

        app.Project.Segments.Add(stairs);
        app.SelectedSegment = stairs;

        return (app, stairs);
    }

    public void Dispose()
    {
        File.Delete(_preferences);
        GC.SuppressFinalize(this);
    }
}
