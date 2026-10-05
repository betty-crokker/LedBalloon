using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Adding to a line that has already been traced, in the two ways that means.
/// <para>
/// They were one command, and should never have been. Both add to a line already drawn, and the
/// difference between them is the whole question: does the strip keep going, or does it stop and
/// start again somewhere else? A porch line that climbs to the peak and comes back down is one
/// unbroken run with corners in it. The stairs are two rails with a landing between them. Offered
/// as one button, the first case got the second's gap, and the only way back was to trace the whole
/// line again.
/// </para>
/// <para>
/// Carrying on also has to ask which end, because a run has two and either can be the one with
/// strip left on it.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AddingToAlineAlreadyTracedTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-addline-{Guid.NewGuid():N}.json");

    // The porch line as traced: left to right along the gutter.
    private static readonly LayoutPoint Left = new(0.1, 0.5);
    private static readonly LayoutPoint Right = new(0.4, 0.5);

    [Fact]
    public void Carrying_on_from_the_far_end_adds_to_the_back() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.CarryOnDrawingCommand.Execute(null);

        // Which end: the click nearest the right-hand one.
        app.AddPointToSelectedSegment(0.41, 0.5);

        // Then the climb to the peak and back down.
        app.AddPointToSelectedSegment(0.5, 0.2);
        app.AddPointToSelectedSegment(0.6, 0.5);

        Assert.Equal(
            [Left, Right, new LayoutPoint(0.5, 0.2), new LayoutPoint(0.6, 0.5)],
            porch.Path);

        // One unbroken run, which is the point.
        Assert.Empty(porch.Breaks);

        return Task.CompletedTask;
    });

    /// <summary>
    /// And from the other end it grows backwards, so LED 1 moves to where the strip now starts.
    /// </summary>
    [Fact]
    public void Carrying_on_from_the_led_one_end_adds_to_the_front() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.CarryOnDrawingCommand.Execute(null);
        app.AddPointToSelectedSegment(0.09, 0.5);

        app.AddPointToSelectedSegment(0.05, 0.5);
        app.AddPointToSelectedSegment(0.0, 0.5);

        Assert.Equal(
            [new LayoutPoint(0.0, 0.5), new LayoutPoint(0.05, 0.5), Left, Right],
            porch.Path);

        Assert.Empty(porch.Breaks);

        return Task.CompletedTask;
    });

    [Fact]
    public void Aseparate_line_records_the_jump_as_a_gap() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.StartSeparateLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);
        app.AddPointToSelectedSegment(0.9, 0.5);

        Assert.Equal([1], porch.Breaks);
        Assert.True(porch.IsBreak(1));
        Assert.False(porch.IsBreak(2));

        return Task.CompletedTask;
    });

    /// <summary>
    /// A gap already in the line travels with it when the line grows at the front.
    /// </summary>
    [Fact]
    public void Agap_keeps_up_when_the_line_grows_backwards() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.StartSeparateLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);
        Assert.Equal([1], porch.Breaks);

        app.CarryOnDrawingCommand.Execute(null);
        app.AddPointToSelectedSegment(0.09, 0.5);
        app.AddPointToSelectedSegment(0.0, 0.5);

        // The jump is still between the same two points, one place further along the list.
        Assert.Equal([2], porch.Breaks);

        return Task.CompletedTask;
    });

    [Fact]
    public void Apoint_can_be_taken_back_off() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.CarryOnDrawingCommand.Execute(null);
        app.AddPointToSelectedSegment(0.41, 0.5);
        app.AddPointToSelectedSegment(0.5, 0.2);

        app.RemovePointFromSelectedSegment(2);

        Assert.Equal([Left, Right], porch.Path);

        return Task.CompletedTask;
    });

    /// <summary>
    /// And deleting the first point of a separate stretch must not quietly join it to the one
    /// before: the jump is still a jump, to whatever is now first.
    /// </summary>
    [Fact]
    public void Deleting_the_start_of_aseparate_line_keeps_the_gap() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.StartSeparateLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);
        app.AddPointToSelectedSegment(0.9, 0.5);

        app.RemovePointFromSelectedSegment(2);

        Assert.Equal(3, porch.Path.Count);
        Assert.Equal([1], porch.Breaks);

        return Task.CompletedTask;
    });

    [Fact]
    public void Starting_over_clears_the_gaps_too() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();

        app.StartSeparateLineCommand.Execute(null);
        app.AddPointToSelectedSegment(0.8, 0.5);

        app.StartDrawingSegmentCommand.Execute(null);

        Assert.Empty(porch.Path);
        Assert.Empty(porch.Breaks);

        return Task.CompletedTask;
    });

    [Fact]
    public void There_is_nothing_to_add_to_an_untraced_run() => ui.Run(() =>
    {
        (MainViewModel app, Segment porch) = House();
        porch.Path.Clear();

        app.CarryOnDrawingCommand.Execute(null);
        Assert.False(app.IsDrawingSegment);

        app.StartSeparateLineCommand.Execute(null);
        Assert.False(app.IsDrawingSegment);

        return Task.CompletedTask;
    });

    private (MainViewModel App, Segment Porch) House()
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        var porch = new Segment
        {
            Id = "porchline", Name = "Porchline", ControllerKey = FakeController.Key,
            Output = 1, Count = 308, SegmentId = 0,
            Path = [Left, Right],
        };

        app.Project.Segments.Add(porch);
        app.SelectedSegment = porch;

        return (app, porch);
    }

    public void Dispose()
    {
        File.Delete(_preferences);
        GC.SuppressFinalize(this);
    }
}
