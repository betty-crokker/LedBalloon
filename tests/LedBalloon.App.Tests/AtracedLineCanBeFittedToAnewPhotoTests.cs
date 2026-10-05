using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Moving the points of a line that was traced on a different photograph.
/// <para>
/// Photographs of a house are not interchangeable. A new one is taken from a slightly different
/// spot, in a different season, at a different time of day, and every line traced on the old one is
/// now a little off. Re-tracing a roofline is several minutes of careful clicking along a gable,
/// and it was the only way to correct that — for a line that was right to within a few pixels.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AtracedLineCanBeFittedToAnewPhotoTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-drag-{Guid.NewGuid():N}.json");

    [Fact]
    public void Apoint_moves_to_where_it_was_dragged() => ui.Run(() =>
    {
        (MainViewModel app, Segment roofline) = House();

        app.MoveSelectedSegmentPoint(1, 0.55, 0.42);

        Assert.Equal(new LayoutPoint(0.55, 0.42), roofline.Path[1]);

        // And only that one: a drag moves the point it took hold of.
        Assert.Equal(new LayoutPoint(0.1, 0.2), roofline.Path[0]);
        Assert.Equal(new LayoutPoint(0.9, 0.3), roofline.Path[2]);

        return Task.CompletedTask;
    });

    [Fact]
    public void Moving_one_is_a_change_worth_saving() => ui.Run(() =>
    {
        (MainViewModel app, _) = House();

        app.HasUnsavedChanges = false;
        app.MoveSelectedSegmentPoint(0, 0.11, 0.21);

        Assert.True(app.HasUnsavedChanges);

        return Task.CompletedTask;
    });

    /// <summary>
    /// Dragged past the edge of the photo it stops at the edge, rather than going somewhere the
    /// line reaches and no one can see or take hold of again.
    /// </summary>
    [Fact]
    public void Apoint_cannot_be_dragged_off_the_photo() => ui.Run(() =>
    {
        (MainViewModel app, Segment roofline) = House();

        app.MoveSelectedSegmentPoint(0, -0.4, 1.9);

        Assert.Equal(new LayoutPoint(0, 1), roofline.Path[0]);

        return Task.CompletedTask;
    });

    /// <summary>
    /// An index that is not there is ignored rather than throwing: the canvas reports a drag for
    /// every pointer movement, and the segment underneath can change between two of them.
    /// </summary>
    [Fact]
    public void Apoint_that_is_not_there_is_left_alone() => ui.Run(() =>
    {
        (MainViewModel app, Segment roofline) = House();

        app.MoveSelectedSegmentPoint(99, 0.5, 0.5);
        app.MoveSelectedSegmentPoint(-1, 0.5, 0.5);

        Assert.Equal(3, roofline.Path.Count);

        return Task.CompletedTask;
    });

    private (MainViewModel App, Segment Roofline) House()
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        var roofline = new Segment
        {
            Id = "roofline", Name = "Roofline", ControllerKey = FakeController.Key,
            Output = 1, Count = 285, SegmentId = 0,
            Path = [new LayoutPoint(0.1, 0.2), new LayoutPoint(0.5, 0.4), new LayoutPoint(0.9, 0.3)],
        };

        app.Project.Segments.Add(roofline);
        app.SelectedSegment = roofline;

        return (app, roofline);
    }

    public void Dispose()
    {
        File.Delete(_preferences);
        GC.SuppressFinalize(this);
    }
}
