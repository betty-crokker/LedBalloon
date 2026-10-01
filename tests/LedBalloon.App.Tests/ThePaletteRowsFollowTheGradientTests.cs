using System.Text;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// The order of the rows in the palette editor.
/// <para>
/// The bar above them is drawn sorted and the file written out is sorted, so a list that is not
/// disagrees with both at once: a stop dragged past its neighbour sat above one with a lower
/// percentage while the gradient showed it below, and saving and reopening put the rows in an order
/// nobody had asked for.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ThePaletteRowsFollowTheGradientTests(UiThreadFixture ui) : IDisposable
{
    /// <summary>North's own palette0, verbatim: black, white at 37, magenta, crimson, red.</summary>
    private const string Fireworks =
        """{"palette":[0,"000000",37,"ffffff",116,"ff00ff",207,"ff002d",255,"ff0000"]}""";

    /// <summary>One written out of order, which WLED's own editor and a text editor both allow.</summary>
    private const string Jumbled =
        """{"palette":[0,"000000",200,"00ff00",80,"0000ff",255,"ffffff"]}""";

    [Fact]
    public void Dragging_a_stop_past_its_neighbour_moves_its_row() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        Assert.Equal([0, 37, 116, 207, 255], Positions(editor));

        // The white one, up past the magenta. This is the drag in the screenshot: 37 of 255 is 15%,
        // dragged to 150, which is 59% - and 59 sat above 45 in the list while the gradient drew it
        // below.
        editor.Stops[1].Position = 150;

        Assert.Equal([0, 116, 150, 207, 255], Positions(editor));

        // Still the same stop, carried with its color rather than its value being swapped into a
        // row that was already there.
        Assert.Equal(Color(255, 255, 255), At(editor, 2));
        Assert.Equal(Color(255, 0, 255), At(editor, 1));
    });

    [Fact]
    public void The_rows_read_in_the_order_the_bar_is_painted() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        editor.Stops[1].Position = 150;

        // Which is the whole complaint: these two were the same list drawn twice, disagreeing.
        Assert.Equal(
            [.. editor.Edited.Stops.Select(s => (int)s.Position)],
            Positions(editor));
    });

    [Fact]
    public void A_palette_already_out_of_order_is_tidied_on_opening() => ui.Run(async () =>
    {
        // Nothing stops a file being written this way, and anything this editor saves will be
        // sorted - so the rows would have reordered themselves on the next open with no edit in
        // between, which looks like the app losing track of them.
        PaletteEditorViewModel editor = await OpenAsync(Jumbled);

        Assert.Equal([0, 80, 200, 255], Positions(editor));
    });

    [Fact]
    public void A_new_color_lands_in_the_gap_it_was_aimed_at() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        // The widest gap here is 116 to 207, so it goes in at 161 - which is row three, not the end
        // of the list where it was added.
        editor.AddStopCommand.Execute(null);

        Assert.Equal([0, 37, 116, 161, 207, 255], Positions(editor));
    });

    [Fact]
    public void Two_at_the_same_place_keep_the_order_they_were_written_in() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        // The white one, dragged until it sits exactly on the magenta.
        editor.Stops[1].Position = 116;

        Assert.Equal([0, 116, 116, 207, 255], Positions(editor));

        // Stable, so neither of them moves: white was above magenta before they met and stays
        // there. A sort that broke ties any other way would have the two rows swapping places every
        // time the slider passed through the value, under a hand that is still holding it.
        Assert.Equal(Color(255, 255, 255), At(editor, 1));
        Assert.Equal(Color(255, 0, 255), At(editor, 2));
    });

    private static int[] Positions(PaletteEditorViewModel editor) =>
        [.. editor.Stops.Select(s => (int)s.Position)];

    private static (byte R, byte G, byte B) At(PaletteEditorViewModel editor, int row) =>
        (editor.Stops[row].Picked.R, editor.Stops[row].Picked.G, editor.Stops[row].Picked.B);

    private static (byte R, byte G, byte B) Color(byte r, byte g, byte b) => (r, g, b);

    private async Task<PaletteEditorViewModel> OpenAsync(string palette)
    {
        FakeController controller = FakeController.Start("Solid");
        controller.Holds(palette);
        _open.Add(controller);

        var device = new DeviceViewModel(controller.Host);
        await device.ConnectAsync();
        _devices.Add(device);

        var editor = new PaletteEditorViewModel(new LedBalloonProject(), device);
        await editor.LoadAsync();

        return editor;
    }

    private readonly List<FakeController> _open = [];
    private readonly List<DeviceViewModel> _devices = [];

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (DeviceViewModel device in _devices)
        {
            device.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }
    }
}
