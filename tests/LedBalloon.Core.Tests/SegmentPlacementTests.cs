using LedBalloon.Core;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Where a run sits is worked out, never typed. A WS281x strip has no addressing - the data is
/// shifted down the chain and each LED takes the first 24 bits it sees - so the runs on one output
/// are end to end in wiring order, and an output starts where the one before it finishes. The only
/// number anyone knows is how many LEDs are in a run, because they counted them.
/// </summary>
public class SegmentPlacementTests
{
    private const string Key = "aa:bb:cc:dd:ee:ff";

    private static Segment Run(string name, int output, int count) => new()
    {
        Name = name,
        ControllerKey = Key,
        Output = output,
        Count = count,
    };

    private static LedBalloonProject House(params Segment[] runs)
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = Key, Name = "South" }],
        };

        foreach (Segment run in runs)
        {
            project.Segments.Add(run);
        }

        project.Reflow(Key);
        return project;
    }

    [Fact]
    public void Runs_on_one_output_lie_end_to_end_in_order()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        House(garage, porch);

        Assert.Equal(0, garage.Start);
        Assert.Equal(20, porch.Start);
        Assert.Equal(25, porch.StopExclusive);
    }

    [Fact]
    public void An_output_starts_where_the_one_before_it_finishes()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        Segment roofline = Run("Roofline", 2, 285);
        House(garage, porch, roofline);

        Assert.Equal(25, roofline.Start);
        Assert.Equal(310, roofline.StopExclusive);
    }

    /// <summary>The scenario that started all of this, now with nothing to repair.</summary>
    [Fact]
    public void Correcting_a_length_moves_everything_after_it_by_itself()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        Segment roofline = Run("Roofline", 2, 285);
        LedBalloonProject project = House(garage, porch, roofline);

        // The porch really has eight. Nothing else is touched by hand.
        porch.Count = 8;
        project.Reflow(Key);

        Assert.Equal(20, porch.Start);
        Assert.Equal(28, roofline.Start);
        Assert.Empty(project.Validate());
    }

    [Fact]
    public void Shortening_a_run_closes_up_behind_it_with_no_offer_to_make()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        Segment roofline = Run("Roofline", 2, 285);
        LedBalloonProject project = House(garage, porch, roofline);

        porch.Count = 3;
        project.Reflow(Key);

        Assert.Equal(23, roofline.Start);
        Assert.Empty(project.Validate());
    }

    /// <summary>
    /// The output's configured length is not a limit the runs fit inside; it is their sum, and
    /// saving writes it back. Counting eight on the porch means that output has 28 LEDs on it.
    /// </summary>
    [Fact]
    public void An_outputs_length_is_the_runs_plugged_into_it()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        Segment roofline = Run("Roofline", 2, 285);
        LedBalloonProject project = House(garage, porch, roofline);

        Assert.Equal([(1, 25), (2, 285)], project.OutputLengths(Key));

        porch.Count = 8;
        project.Reflow(Key);

        Assert.Equal([(1, 28), (2, 285)], project.OutputLengths(Key));
    }

    [Fact]
    public void Moving_a_run_along_its_output_reorders_the_addresses()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        LedBalloonProject project = House(garage, porch);

        Assert.True(project.MoveWithinOutput(porch, -1));

        Assert.Equal(0, porch.Start);
        Assert.Equal(5, garage.Start);
    }

    [Fact]
    public void A_run_already_at_the_end_of_its_output_does_not_move()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        LedBalloonProject project = House(garage, porch);

        Assert.False(project.MoveWithinOutput(garage, -1));
        Assert.False(project.MoveWithinOutput(porch, 1));
        Assert.Equal(0, garage.Start);
        Assert.Equal(20, porch.Start);
    }

    [Fact]
    public void Moving_a_run_cannot_take_it_to_another_output()
    {
        Segment porch = Run("Porch", 1, 5);
        Segment roofline = Run("Roofline", 2, 285);
        LedBalloonProject project = House(porch, roofline);

        // They are each alone on their own output, so neither has anywhere to go.
        Assert.False(project.MoveWithinOutput(porch, 1));
        Assert.False(project.MoveWithinOutput(roofline, -1));
        Assert.Equal(5, roofline.Start);
    }

    [Fact]
    public void Removing_a_run_closes_the_output_up()
    {
        Segment garage = Run("Garage", 1, 20);
        Segment porch = Run("Porch", 1, 5);
        Segment roofline = Run("Roofline", 2, 285);
        LedBalloonProject project = House(garage, porch, roofline);

        project.Segments.Remove(garage);
        project.Reflow(Key);

        Assert.Equal(0, porch.Start);
        Assert.Equal(5, roofline.Start);
    }

    /// <summary>
    /// Layouts written before outputs were modelled carry a start and nothing else, so the output
    /// has to be read back out of it against the controller's own wiring.
    /// </summary>
    [Fact]
    public void An_older_layout_gets_its_outputs_from_the_wiring()
    {
        var project = new LedBalloonProject();
        var garage = new Segment { Name = "Garage", ControllerKey = Key, Start = 0, Count = 20 };
        var porch = new Segment { Name = "Porch", ControllerKey = Key, Start = 20, Count = 5 };
        var roofline = new Segment { Name = "Roofline", ControllerKey = Key, Start = 25, Count = 285 };
        project.Segments.Add(garage);
        project.Segments.Add(porch);
        project.Segments.Add(roofline);

        project.AssignOutputs(Key, [
            new LedBus(0, 25, [16], 1, false),
            new LedBus(25, 285, [2], 0, false),
        ]);

        Assert.Equal(1, garage.Output);
        Assert.Equal(1, porch.Output);
        Assert.Equal(2, roofline.Output);

        // And the starts it already had are exactly the ones the model works out.
        project.Reflow(Key);
        Assert.Equal(0, garage.Start);
        Assert.Equal(20, porch.Start);
        Assert.Equal(25, roofline.Start);
    }

    [Fact]
    public void Assigning_outputs_leaves_alone_a_run_that_already_says()
    {
        var project = new LedBalloonProject();
        var run = new Segment { Name = "Porch", ControllerKey = Key, Start = 0, Count = 5, Output = 2 };
        project.Segments.Add(run);

        project.AssignOutputs(Key, [new LedBus(0, 25, [16], 1, false)]);

        Assert.Equal(2, run.Output);
    }

    [Fact]
    public void Each_controller_is_laid_out_on_its_own()
    {
        var project = new LedBalloonProject();
        Segment mine = Run("Porch", 1, 20);
        var theirs = new Segment { Name = "Other", ControllerKey = "11:22:33:44:55:66", Count = 10, Output = 1 };
        project.Segments.Add(mine);
        project.Segments.Add(theirs);

        project.ReflowAll();

        Assert.Equal(0, mine.Start);
        Assert.Equal(0, theirs.Start);
    }

    [Fact]
    public void A_run_with_no_leds_is_still_worth_saying()
    {
        LedBalloonProject project = House(Run("Nothing", 1, 0));

        Assert.Contains(project.Validate(), p => p.Contains("has no LEDs"));
    }

    [Fact]
    public void Runs_on_output_two_with_nothing_on_output_one_are_reported()
    {
        LedBalloonProject project = House(Run("Roofline", 2, 285));

        Assert.Contains(project.Validate(), p => p.Contains("nothing on output 1"));
    }
}
