using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Correcting a miscounted run is the single most common edit this app exists for, and it is the
/// one that can quietly break the house: LEDs are addressed along one wire, so a run that grows
/// lands on top of the next one and a run that shrinks strands the LEDs behind it.
/// </summary>
public class SegmentPlacementTests
{
    private const string Key = "aa:bb:cc:dd:ee:ff";

    private static Segment Run(string name, int start, int count) => new()
    {
        Name = name,
        ControllerKey = Key,
        Start = start,
        Count = count,
    };

    /// <summary>First 20, then 10 butted up behind it — the scenario, exactly.</summary>
    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject();
        project.Segments.Add(Run("One", 0, 20));
        project.Segments.Add(Run("Two", 20, 10));
        return project;
    }

    [Fact]
    public void Correcting_twenty_to_twenty_two_pushes_the_next_run_clear()
    {
        LedBalloonProject project = House();
        Segment one = project.Segments[0];
        Segment two = project.Segments[1];

        one.Count = 22;
        IReadOnlyList<Segment> moved = project.MakeRoomAfter(one);

        Assert.Same(two, Assert.Single(moved));
        Assert.Equal(22, two.Start);
        Assert.Equal(10, two.Count);
        Assert.Empty(project.Validate());
    }

    [Fact]
    public void A_run_with_room_in_front_of_it_absorbs_the_push()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 20);
        Segment two = Run("Two", 25, 10);
        project.Segments.Add(one);
        project.Segments.Add(two);

        one.Count = 22;

        // 22 still clears 25, so the deliberate gap is left exactly as it was.
        Assert.Empty(project.MakeRoomAfter(one));
        Assert.Equal(25, two.Start);
    }

    [Fact]
    public void The_push_cascades_only_as_far_as_the_collision_reaches()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 20);
        Segment two = Run("Two", 20, 10);
        Segment three = Run("Three", 60, 10);
        project.Segments.Add(one);
        project.Segments.Add(two);
        project.Segments.Add(three);

        one.Count = 25;
        IReadOnlyList<Segment> moved = project.MakeRoomAfter(one);

        Assert.Same(two, Assert.Single(moved));
        Assert.Equal(25, two.Start);

        // Two now ends at 35, well clear of Three, which keeps the space it was given.
        Assert.Equal(60, three.Start);
    }

    [Fact]
    public void A_run_swallowing_several_pushes_all_of_them()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 20);
        Segment two = Run("Two", 20, 10);
        Segment three = Run("Three", 30, 10);
        project.Segments.Add(one);
        project.Segments.Add(two);
        project.Segments.Add(three);

        one.Count = 45;
        IReadOnlyList<Segment> moved = project.MakeRoomAfter(one);

        Assert.Equal(2, moved.Count);
        Assert.Equal(45, two.Start);
        Assert.Equal(55, three.Start);
        Assert.Empty(project.Validate());
    }

    [Fact]
    public void Nothing_moves_when_the_last_run_grows()
    {
        LedBalloonProject project = House();
        Segment two = project.Segments[1];

        two.Count = 40;

        Assert.Empty(project.MakeRoomAfter(two));
        Assert.Equal(20, two.Start);
    }

    [Fact]
    public void Shrinking_leaves_the_gap_alone_until_it_is_asked()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 22);
        Segment two = Run("Two", 22, 10);
        project.Segments.Add(one);
        project.Segments.Add(two);

        one.Count = 20;

        // The point of the offer: shortening does not move anything by itself.
        Assert.Empty(project.MakeRoomAfter(one));
        Assert.Equal(22, two.Start);
        Assert.Equal(2, project.SpareAfter(one));
    }

    [Fact]
    public void Closing_the_gap_brings_the_rest_back_down_the_wire()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 22);
        Segment two = Run("Two", 22, 10);
        Segment three = Run("Three", 32, 10);
        project.Segments.Add(one);
        project.Segments.Add(two);
        project.Segments.Add(three);

        one.Count = 20;
        IReadOnlyList<Segment> moved = project.CloseSpareAfter(one);

        Assert.Equal(2, moved.Count);
        Assert.Equal(20, two.Start);
        Assert.Equal(30, three.Start);
        Assert.Empty(project.Validate());
    }

    [Fact]
    public void Closing_a_gap_keeps_spacing_further_down_the_wire()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 22);
        Segment two = Run("Two", 22, 10);
        Segment three = Run("Three", 50, 10);
        project.Segments.Add(one);
        project.Segments.Add(two);
        project.Segments.Add(three);

        one.Count = 20;
        project.CloseSpareAfter(one);

        // Everything shifts by the same 2: the 18 LEDs between Two and Three were put there on
        // purpose and closing a gap somewhere else is no reason to swallow them.
        Assert.Equal(20, two.Start);
        Assert.Equal(48, three.Start);
    }

    [Fact]
    public void There_is_no_gap_behind_the_last_run()
    {
        LedBalloonProject project = House();
        Segment two = project.Segments[1];

        // LEDs past the end are not described yet, which the controller card already says.
        Assert.Equal(0, project.SpareAfter(two));
        Assert.Empty(project.CloseSpareAfter(two));
    }

    [Fact]
    public void An_overlap_reads_as_no_spare_rather_than_negative()
    {
        var project = new LedBalloonProject();
        Segment one = Run("One", 0, 30);
        project.Segments.Add(one);
        project.Segments.Add(Run("Two", 20, 10));

        Assert.Equal(0, project.SpareAfter(one));
    }

    [Fact]
    public void A_neighbour_on_another_controller_is_not_in_the_way()
    {
        var project = new LedBalloonProject();
        Segment mine = Run("One", 0, 20);
        var theirs = new Segment { Name = "Other", ControllerKey = "11:22:33:44:55:66", Start = 20, Count = 10 };
        project.Segments.Add(mine);
        project.Segments.Add(theirs);

        mine.Count = 40;

        Assert.Empty(project.MakeRoomAfter(mine));
        Assert.Equal(20, theirs.Start);
    }
}
