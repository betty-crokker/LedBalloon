using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// One color slot, in a window with a way out of it.
/// <para>
/// This was a flyout hanging off the swatch, and a flyout has no OK and no Cancel — it applies as
/// you drag and it closes when you click somewhere else, so there was no way to try a color and
/// decide against it. Everything else in the editor is undoable by closing the editor; this was the
/// one thing that was not.
/// </para>
/// <para>
/// Picking stays live, for the same reason the segment editor does: the point of picking a color
/// here rather than in the WLED app is watching it land on the house. So Cancel undoes rather than
/// declines.
/// </para>
/// </summary>
public partial class ColorPickerDialog : Window
{
    private bool _accepted;

    public ColorPickerDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens the picker on one slot, and puts the color back if it is cancelled.</summary>
    public static async Task<bool> ShowAsync(Window owner, SegmentColorSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        slot.Begin();

        var dialog = new ColorPickerDialog { DataContext = slot };

        await dialog.ShowDialog(owner);

        if (!dialog._accepted)
        {
            slot.Revert();
        }

        return dialog._accepted;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        _accepted = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
