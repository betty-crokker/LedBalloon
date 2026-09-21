using Ledwright.Core.Models;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// The photo draws the house as reported plus whatever has been picked but not sent, which means
/// laying changes over a copy of the live state. If the copy shares anything with the original,
/// looking at a preset quietly rewrites what the app believes the lights are doing — and then
/// discarding it puts back the wrong thing.
/// </summary>
public class WledStateCloneTests
{
    private static WledState Live() => new()
    {
        On = true,
        Brightness = 200,
        Segments =
        [
            new WledSegment { Id = 0, Start = 0, Stop = 200, Effect = 0, Colors = [[255, 0, 0]] },
            new WledSegment { Id = 1, Start = 200, Stop = 356, Effect = 9, Palette = 11 },
        ],
    };

    [Fact]
    public void Changing_a_copy_leaves_the_state_it_was_copied_from_alone()
    {
        WledState live = Live();
        WledState copy = live.Clone();

        copy.On = false;
        copy.Brightness = 10;
        copy.Segments![0].Effect = 111;

        Assert.True(live.On);
        Assert.Equal((byte)200, live.Brightness);
        Assert.Equal(0, live.Segments![0].Effect);
    }

    [Fact]
    public void The_color_slots_are_copied_too_rather_than_shared()
    {
        WledState live = Live();
        WledState copy = live.Clone();

        copy.Segments![0].PrimaryColor = new RgbColor(0, 0, 255);

        // The live state is still red, which is what the strip is actually showing.
        Assert.Equal(255, live.Segments![0].Colors![0][0]);
        Assert.Equal(0, live.Segments[0].Colors![0][2]);
    }

    [Fact]
    public void Merging_a_held_change_into_a_copy_does_not_reach_the_original()
    {
        WledState live = Live();
        WledState copy = live.Clone();

        // What holding an edit looks like: a sparse patch laid over the copy.
        copy.MergeFrom(WledState.ForSegment(1, segment => segment.Palette = 254));

        Assert.Equal(254, copy.Segments![1].Palette);
        Assert.Equal(11, live.Segments![1].Palette);
    }

    [Fact]
    public void A_copy_carries_everything_that_decides_how_a_run_looks()
    {
        WledState copy = Live().Clone();

        Assert.True(copy.On);
        Assert.Equal((byte)200, copy.Brightness);
        Assert.Equal(2, copy.Segments!.Count);
        Assert.Equal(9, copy.Segments[1].Effect);
        Assert.Equal(11, copy.Segments[1].Palette);
        Assert.Equal(200, copy.Segments[1].Start);
        Assert.Equal(356, copy.Segments[1].Stop);
    }

    [Fact]
    public void A_state_with_no_segments_copies_without_inventing_any()
    {
        var sparse = new WledState { Brightness = 64 };

        WledState copy = sparse.Clone();

        Assert.Null(copy.Segments);
        Assert.Equal((byte)64, copy.Brightness);
    }
}
