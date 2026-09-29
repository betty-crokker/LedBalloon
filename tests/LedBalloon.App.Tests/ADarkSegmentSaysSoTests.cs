using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A segment card describes what the house will show, so it has to agree with the photo beside it.
/// The photo folds both brightnesses into the color it draws and stops calling a run lit once there
/// is nothing left of it; the card only knew about the two switches, so a house turned down to
/// nothing was listed as running effects.
/// <para>
/// Not a corner case: WLED's own Off preset is built exactly this way — brightness nothing, segments
/// left alone — so it was the everyday way of putting the house to bed that read wrong.
/// </para>
/// </summary>
public class ADarkSegmentSaysSoTests
{
    private static readonly Segment Porch = new()
    {
        Id = "porch",
        Name = "Porch",
        ControllerKey = "aa:bb:cc:dd:ee:ff",
        Output = 1,
        Count = 5,
    };

    /// <summary>A segment that is switched on and running something, so only the dimming decides.</summary>
    private static WledSegment Running(byte? segmentBrightness = null) => new()
    {
        Id = 0,
        On = true,
        Effect = 74,
        Brightness = segmentBrightness,
        Colors = [[255, 255, 255]],
    };

    private static bool IsOff(WledSegment wled, bool controllerOn = true, byte? brightness = null) =>
        MainViewModel.Describe(Porch, wled, device: null, controllerOn, brightness).IsOff;

    [Fact]
    public void A_lit_segment_on_a_lit_controller_is_not_off()
    {
        Assert.False(IsOff(Running(), controllerOn: true, brightness: 255));
    }

    [Fact]
    public void A_controller_turned_down_to_nothing_is_off()
    {
        // The case this was written for. Opening the Off scene used to list every segment as
        // running an effect, beside a photo that correctly drew them all dark.
        Assert.True(IsOff(Running(), controllerOn: true, brightness: 0));
    }

    [Fact]
    public void A_segment_turned_down_to_nothing_is_off_too()
    {
        // Same argument one level down: a segment has its own brightness, and nothing of it at
        // full master is still nothing.
        Assert.True(IsOff(Running(segmentBrightness: 0), controllerOn: true, brightness: 255));
    }

    [Fact]
    public void One_is_enough_to_darken_it()
    {
        Assert.True(IsOff(Running(segmentBrightness: 0), controllerOn: true, brightness: 0));
    }

    [Fact]
    public void The_switches_still_count()
    {
        Assert.True(IsOff(Running(), controllerOn: false, brightness: 255));
        Assert.True(IsOff(new WledSegment { Id = 0, On = false, Effect = 74 }, brightness: 255));
    }

    [Fact]
    public void Barely_lit_is_not_the_same_as_dark()
    {
        // Only zero counts. A house turned right down is still showing something, and saying "off"
        // about it would be the same mistake in the other direction - the photo draws it, faintly.
        Assert.False(IsOff(Running(), controllerOn: true, brightness: 1));
        Assert.False(IsOff(Running(segmentBrightness: 1), controllerOn: true, brightness: 255));
    }

    [Fact]
    public void A_dark_card_has_no_caveat_about_how_well_the_photo_draws_it()
    {
        // "approximated on the photo" is an apology for standing in for an effect. A segment that is
        // off has no effect being stood in for, so the apology was about nothing - and it appeared
        // the moment a segment was switched off, because an absent effect matches nothing in the
        // library and so counted as inexact.
        Assert.Equal(
            string.Empty,
            MainViewModel.Describe(Porch, Running(), null, controllerOn: true, 0).Fidelity);
    }

    [Fact]
    public void A_brightness_nobody_has_stated_is_not_a_dark_one()
    {
        // Null is "no opinion", which reaches WLED as no bri field at all and leaves the controller
        // where it is. Reading it as zero would darken every card a scene says nothing about.
        Assert.False(IsOff(Running(), controllerOn: true, brightness: null));
    }
}
