using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// One segment's appearance, in a window of its own.
/// <para>
/// This was a block in the side panel that took the panel over, so opening it hid the list of
/// segments it was opened from and a "Back to the whole house" button had to be added to put the
/// list back — a button whose only job was undoing the click that opened the editor. A window
/// closes, which is the same thing said by the frame rather than by a control.
/// </para>
/// <para>
/// The editing inside is live, exactly as it was when this lived in the panel: Sync means the same
/// thing here as everywhere else, so changes can be watched on the house while they are made. Cancel
/// therefore undoes rather than declines — it puts the segment back to what it was when this opened,
/// which the view model does, since it is the thing that knows what that was.
/// </para>
/// </summary>
public partial class SegmentEditorDialog : Window
{
    private bool _saved;

    public SegmentEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the editor on whatever the view model has selected, and waits for the way out.
    /// </summary>
    /// <returns>True for Save, false for Cancel — and for the close button, which is a Cancel.</returns>
    public static async Task<bool> ShowAsync(Window owner, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var dialog = new SegmentEditorDialog { DataContext = viewModel };

        // Owned by this window rather than by the main one, so the picker sits over the editor it
        // belongs to instead of behind it. Taken back afterwards: a swatch cannot be clicked once
        // this has closed, and a stale owner would be a window parented to something that is gone.
        viewModel.ShowColorPicker = slot => ColorPickerDialog.ShowAsync(dialog, slot);
        viewModel.ShowPaletteEditor = device =>
            PaletteEditorDialog.ShowAsync(dialog, viewModel.Project, device);

        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            viewModel.ShowColorPicker = null;
            viewModel.ShowPaletteEditor = null;
        }

        return dialog._saved;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
