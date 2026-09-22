using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Every controller holds the whole house, so losing one costs nothing but the controller. That
/// only works if the copies can be ordered, which is what the revision is for — and if nothing ever
/// tries to add two copies together, which would bring deleted runs back from the dead.
/// </summary>
public class MirroredProjectTests
{
    private const string South = "704bca414644";
    private const string North = "a842e36a1be8";

    private static LedBalloonProject House(int revision = 1, params string[] segmentNames)
    {
        var project = new LedBalloonProject
        {
            Name = "Home",
            Revision = revision,
            Schema = LedBalloonProject.MirroredSchema,
            Controllers =
            [
                new ControllerRef { Key = South, Name = "South" },
                new ControllerRef { Key = North, Name = "North" },
            ],
        };

        foreach (string name in segmentNames)
        {
            project.Segments.Add(new Segment
            {
                Id = name.ToLowerInvariant(),
                Name = name,
                ControllerKey = name.StartsWith('N') ? North : South,
                Output = 1,
                Count = 10,
            });
        }

        return project;
    }

    [Fact]
    public void Two_copies_of_the_same_house_have_the_same_fingerprint()
    {
        Assert.Equal(
            House(1, "Garage", "Porch").Fingerprint(),
            House(1, "Garage", "Porch").Fingerprint());
    }

    [Fact]
    public void When_it_was_written_is_not_part_of_the_fingerprint()
    {
        LedBalloonProject a = House(1, "Garage");
        LedBalloonProject b = House(1, "Garage");

        a.SavedUtc = DateTimeOffset.UnixEpoch;
        b.SavedUtc = DateTimeOffset.UtcNow;

        // Nor is the revision: two copies at different revisions can still be the same document.
        b.Revision = 99;

        Assert.Equal(a.Fingerprint(), b.Fingerprint());
    }

    [Fact]
    public void A_house_with_different_runs_has_a_different_fingerprint()
    {
        Assert.NotEqual(
            House(1, "Garage", "Porch").Fingerprint(),
            House(1, "Garage", "Porch", "Roofline").Fingerprint());
    }

    [Fact]
    public void Changing_a_length_changes_the_fingerprint()
    {
        LedBalloonProject a = House(1, "Garage");
        LedBalloonProject b = House(1, "Garage");

        b.Segments[0].Count = 22;

        Assert.NotEqual(a.Fingerprint(), b.Fingerprint());
    }

    /// <summary>
    /// The reason mirrored copies are never unioned. South loses a run while North is unplugged;
    /// North comes back still holding it. Taking the newest is right; adding them together would
    /// undo the deletion without anyone asking.
    /// </summary>
    [Fact]
    public void Unioning_mirrored_copies_would_resurrect_a_deleted_run()
    {
        LedBalloonProject stale = House(15, "Garage", "Porch");
        LedBalloonProject current = House(16, "Garage");

        LedBalloonProject unioned = LedBalloonProject.Assemble([current, stale]);

        // Assemble is right for schema 1 slices and wrong for mirrors. This is what wrong looks like.
        Assert.Equal(2, unioned.Segments.Count);
        Assert.Contains(unioned.Segments, s => s.Name == "Porch");

        // Which is why the load takes the newest whole.
        Assert.Single(current.Segments);
    }

    [Fact]
    public void Assembling_slices_still_adds_them_up()
    {
        // Schema 1: each box held only its own runs.
        LedBalloonProject southSlice = House(15, "Garage", "Porch");
        southSlice.Schema = 1;

        LedBalloonProject northSlice = House(15, "NPorchline");
        northSlice.Schema = 1;
        northSlice.Segments.RemoveAll(s => s.ControllerKey == South);

        LedBalloonProject house = LedBalloonProject.Assemble([southSlice, northSlice]);

        Assert.Equal(3, house.Segments.Count);
        Assert.Equal(15, house.Revision);
    }

    [Fact]
    public void The_newest_revision_is_the_one_to_believe()
    {
        LedBalloonProject stale = House(15, "Garage");
        LedBalloonProject current = House(16, "Garage", "Porch");

        LedBalloonProject[] copies = [stale, current];

        Assert.Same(current, copies.OrderByDescending(p => p.Revision).First());
    }

    [Fact]
    public void Two_copies_at_one_revision_that_disagree_are_detectable()
    {
        LedBalloonProject mine = House(17, "Garage", "Porch");
        LedBalloonProject theirs = House(17, "Garage", "Roofline");

        Assert.Equal(mine.Revision, theirs.Revision);
        Assert.NotEqual(mine.Fingerprint(), theirs.Fingerprint());
    }
}
