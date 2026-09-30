using System.Runtime.ExceptionServices;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// An appearance carries three color slots and most of them are empty, so resolving one scene used
/// to throw and catch an ArgumentNullException for every empty one — <c>TryParse</c> asked
/// <c>Parse</c> and caught the complaint. Nothing was visibly wrong and the cost was invisible, but
/// the debugger stopped on every scene anyone selected, which is the kind of noise a real fault
/// then hides in.
/// <para>
/// So this watches for the throw rather than the answer. Asserting that TryParse returns false for
/// null would have passed all along.
/// </para>
/// </summary>
public class ResolvingASceneThrowsNothingTests
{
    private const string South = "704bca414644";

    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = South, Name = "South" }],
            Segments =
            [
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
                new Segment { Id = "roof", Name = "Roofline", ControllerKey = South, Output = 1, Count = 285 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    /// <summary>Counts the complaints raised anywhere in this process while an action runs.</summary>
    private static int ThrowsWhile(Action work)
    {
        int seen = 0;

        void Watch(object? sender, FirstChanceExceptionEventArgs e)
        {
            // Narrow on purpose: the tests run in parallel, and this is process-wide.
            if (e.Exception is ArgumentNullException { ParamName: "hex" })
            {
                Interlocked.Increment(ref seen);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Watch;

        try
        {
            work();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Watch;
        }

        return seen;
    }

    [Fact]
    public void Resolving_a_scene_whose_colors_are_not_set_raises_nothing()
    {
        LedBalloonProject house = House();

        // An ordinary scene: an effect on each segment and no colors, which is most of them.
        var scene = new Scene
        {
            Name = "Twinkle",
            Segments =
            {
                ["porch"] = new SceneEntry { Effect = 74 },
                ["roof"] = new SceneEntry { Effect = 74 },
            },
        };

        Assert.Equal(0, ThrowsWhile(() => SceneResolver.Resolve(house, scene)));
    }

    [Fact]
    public void And_reading_an_appearance_with_no_colors_raises_nothing()
    {
        var entry = new SceneEntry { Effect = 74 };

        Assert.Equal(0, ThrowsWhile(() =>
        {
            _ = entry.Primary;
            _ = entry.Secondary;
            _ = entry.Tertiary;
        }));
    }

    [Fact]
    public void An_empty_color_is_simply_absent()
    {
        Assert.False(RgbColor.TryParse(null, out _));
        Assert.False(RgbColor.TryParse(string.Empty, out _));
        Assert.False(RgbColor.TryParse("   ", out _));
    }

    [Fact]
    public void A_color_that_is_there_still_reads()
    {
        Assert.True(RgbColor.TryParse("#FF8000", out RgbColor color));
        Assert.Equal(new RgbColor(255, 128, 0), color);
    }

    [Fact]
    public void And_one_that_is_nonsense_is_still_refused_rather_than_thrown()
    {
        Assert.False(RgbColor.TryParse("not a color", out _));
        Assert.False(RgbColor.TryParse("#12345", out _));
        Assert.False(RgbColor.TryParse("#GGGGGG", out _));
    }
}
