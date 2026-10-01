using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Where a stop can go, and where its row stays.
/// <para>
/// The rows are the gradient read top to bottom. The bar above them is drawn sorted and the file
/// written out is sorted, so a row that crosses its neighbour disagrees with both at once - and the
/// two ways out of that are both worse than not letting it happen. Reordering the rows means one
/// jumps while the hand dragging it is still on the slider; leaving them means the list silently
/// rearranges itself on the next open, with no edit in between.
/// </para>
/// <para>
/// So a stop stops. The alternative - shoving the neighbour along to make room - keeps the order
/// too, and quietly moves stops nobody asked to move: a drag to the far end would pile everything
/// above it into the last few places and undo a gradient somebody had tuned.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ThePaletteRowsFollowTheGradientTests(UiThreadFixture ui) : IDisposable
{
    /// <summary>North's own palette0, verbatim: black, white at 37, magenta, crimson, red.</summary>
    private const string Fireworks =
        """{"palette":[0,"000000",37,"ffffff",116,"ff00ff",207,"ff002d",255,"ff0000"]}""";

    /// <summary>One written out of order, which a text editor allows and nothing rejects.</summary>
    private const string Jumbled =
        """{"palette":[0,"000000",200,"00ff00",80,"0000ff",255,"ffffff"]}""";

    [Fact]
    public void A_stop_stops_where_the_next_one_is() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        Assert.Equal([0, 37, 116, 207, 255], Positions(editor));

        // The white one, dragged well past the magenta. This is the drag that started it: 37 of 255
        // is 15%, dragged to 150, which is 59% - and 59 sat above 45 in the list.
        editor.Stops[1].Position = 150;

        // It went as far as the magenta and no further, and every row is where it was.
        Assert.Equal([0, 116, 116, 207, 255], Positions(editor));
        Assert.Equal(Color(255, 255, 255), At(editor, 1));
        Assert.Equal(Color(255, 0, 255), At(editor, 2));
    });

    [Fact]
    public void And_where_the_one_before_it_is() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        // Downwards is penned in the same way, by the stop above rather than by the one below.
        editor.Stops[2].Position = 10;

        Assert.Equal([0, 37, 37, 207, 255], Positions(editor));
        Assert.Equal(Color(255, 0, 255), At(editor, 2));
    });

    [Fact]
    public void The_ends_are_penned_in_by_the_range_itself() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        // Nothing above the last one or below the first, so those two answer to 0 and 255 - the
        // whole of what the firmware reads.
        editor.Stops[4].Position = 300;
        editor.Stops[0].Position = -20;

        Assert.Equal(255, editor.Stops[4].Position);
        Assert.Equal(0, editor.Stops[0].Position);
    });

    [Fact]
    public void Freed_by_its_neighbour_moving_first() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        // Which is the cost of this, said out loud: to move a stop a long way you move what is in
        // its way first. Nothing is lost, and nothing moves that was not dragged.
        editor.Stops[2].Position = 200;
        editor.Stops[1].Position = 150;

        Assert.Equal([0, 150, 200, 207, 255], Positions(editor));
        Assert.Equal(Color(255, 255, 255), At(editor, 1));
    });

    [Fact]
    public void The_rows_read_in_the_order_the_bar_is_painted() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        editor.Stops[1].Position = 150;

        // Which is the whole point: these two were the same list drawn twice, disagreeing.
        Assert.Equal([.. editor.Edited.Stops.Select(s => (int)s.Position)], Positions(editor));
    });

    [Fact]
    public void A_palette_already_out_of_order_is_sorted_as_it_is_read() => ui.Run(async () =>
    {
        // Sorted before there is a row to move, which is not the same as moving one. Everything
        // downstream reads the list as ascending, and anything this editor saves will be.
        PaletteEditorViewModel editor = await OpenAsync(Jumbled);

        Assert.Equal([0, 80, 200, 255], Positions(editor));
        Assert.Equal(Color(0, 0, 255), At(editor, 1));
        Assert.Equal(Color(0, 255, 0), At(editor, 2));
    });

    [Fact]
    public void A_new_color_is_inserted_in_the_gap_it_was_aimed_at() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        // The widest gap is 116 to 207, so it goes in at 161 - and into row three, not onto the end
        // of a list it sits in the middle of.
        editor.AddStopCommand.Execute(null);

        Assert.Equal([0, 37, 116, 161, 207, 255], Positions(editor));
    });

    [Fact]
    public void Removing_one_gives_its_neighbours_the_room_back() => ui.Run(async () =>
    {
        PaletteEditorViewModel editor = await OpenAsync(Fireworks);

        editor.Stops[2].RemoveCommand.Execute(null);

        // The magenta is gone, so the white can now go as far as the crimson and not merely as far
        // as where the magenta used to be. The room is read off the list rather than remembered.
        editor.Stops[1].Position = 190;

        Assert.Equal([0, 190, 207, 255], Positions(editor));
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
