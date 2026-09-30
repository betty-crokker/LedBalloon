using Avalonia.Media;
using LedBalloon.App.ViewModels;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A color slot, and the undo that a flyout could not have.
/// <para>
/// The picker used to hang off the swatch as a flyout, which applies as you drag and closes when you
/// click somewhere else. There was no way to try a color and decide against it — the one thing in
/// the editor that could not be taken back.
/// </para>
/// <para>
/// Picking stays live, because watching a color land on the house is the reason to pick it here
/// rather than in the WLED app. So Cancel undoes rather than declines, and undoing needs somewhere
/// to undo to.
/// </para>
/// </summary>
public class PickingAcolorCanBeTakenBackTests
{
    private static SegmentColorSlot Slot(out List<Color> sent)
    {
        var went = new List<Color>();
        sent = went;

        return new SegmentColorSlot(0, "Color", Color.FromRgb(146, 146, 146), (_, c) => went.Add(c));
    }

    [Fact]
    public void Cancelling_puts_back_the_color_it_opened_on()
    {
        SegmentColorSlot slot = Slot(out List<Color> sent);

        slot.Begin();
        slot.Picked = Color.FromRgb(0, 200, 40);
        slot.Revert();

        Assert.Equal(Color.FromRgb(146, 146, 146), slot.Picked);

        // And the house was told both times, because the picking was live and the undo is a change
        // like any other.
        Assert.Equal([Color.FromRgb(0, 200, 40), Color.FromRgb(146, 146, 146)], sent);
    }

    [Fact]
    public void Keeping_it_keeps_it()
    {
        SegmentColorSlot slot = Slot(out _);

        slot.Begin();
        slot.Picked = Color.FromRgb(0, 200, 40);

        Assert.Equal(Color.FromRgb(0, 200, 40), slot.Picked);
    }

    [Fact]
    public void The_two_patches_show_where_it_started_and_where_it_is()
    {
        SegmentColorSlot slot = Slot(out _);

        slot.Begin();
        slot.Picked = Color.FromRgb(0, 200, 40);

        Assert.Equal(Color.FromRgb(146, 146, 146), slot.Opened);
        Assert.Equal(Color.FromRgb(0, 200, 40), slot.Picked);
    }

    /// <summary>
    /// The readout that answers "did that click do anything", which the swatch alone does not.
    /// </summary>
    /// <remarks>
    /// The spectrum draws every hue at full brightness while the bar down its left side decides how
    /// bright the result is. With that bar near the bottom - which is where a dark segment leaves it
    /// - every hue clicked comes out a near-black version of itself, so a 22-pixel swatch barely
    /// changes and the picker looks broken. A number cannot be misread that way.
    /// </remarks>
    [Fact]
    public void And_it_says_the_color_in_a_form_that_cannot_be_squinted_at()
    {
        SegmentColorSlot slot = Slot(out _);

        Assert.Equal("#929292", slot.Hex);

        slot.Picked = Color.FromRgb(0, 200, 40);
        Assert.Equal("#00C828", slot.Hex);
    }

    [Fact]
    public void A_slot_with_nowhere_to_open_a_picker_does_not_fall_over()
    {
        SegmentColorSlot slot = Slot(out _);

        // No window, so no picker. The command still has to be safe to invoke, because the button
        // is on screen either way.
        Assert.True(slot.PickCommand.CanExecute(null));
        slot.PickCommand.Execute(null);
    }
}
