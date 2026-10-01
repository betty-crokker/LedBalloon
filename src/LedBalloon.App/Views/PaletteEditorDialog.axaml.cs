using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.Views;

/// <summary>
/// The palettes one controller holds of its own.
/// </summary>
/// <remarks>
/// WLED's built-in palettes are firmware data, identical on every box and editable by nobody. These
/// are files somebody uploaded, and until now the only way to change one was WLED's own editor,
/// which names them "Custom 0" because the firmware knows them by nothing but their position.
/// <para>
/// Unlike the segment editor this one is not live: a palette only exists on the controller once the
/// file is written, and writing it on every drag of a slider would be a few hundred uploads. So Save
/// is a real save here, and Cancel simply closes.
/// </para>
/// </remarks>
public partial class PaletteEditorDialog : Window
{
    public PaletteEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens the editor on one controller's palettes and waits for it to close.</summary>
    /// <returns>True when something was saved, so the caller knows to read the palettes again.</returns>
    public static async Task<bool> ShowAsync(
        Window owner, LedBalloonProject project, DeviceViewModel device)
    {
        ArgumentNullException.ThrowIfNull(project);

        var dialog = new PaletteEditorDialog();

        var model = new PaletteEditorViewModel(
            project,
            device,
            slot => ColorPickerDialog.ShowAsync(dialog, Slot(slot)));

        dialog.DataContext = model;

        // Read on the way in rather than in the constructor, so the window is up and saying it is
        // busy while a controller that is slow to answer is answering.
        dialog.Opened += async (_, _) => await model.LoadAsync();

        await dialog.ShowDialog(owner);

        return dialog._saved;
    }

    /// <summary>
    /// Wraps a stop in the slot the color picker expects, so the two share one picker.
    /// </summary>
    /// <remarks>
    /// The picker already does old-against-new, the hex readout and an undo on Cancel. Writing a
    /// second one for this window would be a second one to keep in step.
    /// </remarks>
    private static SegmentColorSlot Slot(PaletteStopRow row) =>
        new(0, "Color", row.Picked, (_, color) => row.Picked = color);

    private bool _saved;

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PaletteEditorViewModel model)
        {
            return;
        }

        if (await model.SaveAsync())
        {
            _saved = true;
            Close();
        }
    }

    private async void OnRemoveLast(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PaletteEditorViewModel model)
        {
            await model.RemoveLastAsync();
            _saved = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
