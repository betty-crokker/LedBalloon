using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// Picking one of a great many things that are told apart by looking at them.
/// <para>
/// Both lists this replaces were drop-downs: one line at a time, a swatch the size of a word, and
/// 187 effects or seventy-odd palettes to go through a dozen at a time. Neither is a list anybody
/// reads — they are lists people hunt in, and a drop-down is the wrong shape for hunting. A grid
/// shows thirty at once at a size the pattern survives, and a filter box turns "somewhere in the
/// P's" into three keystrokes.
/// </para>
/// <para>
/// One window for both, because the two jobs differ only in what a tile looks like, and that is a
/// data template. The separator between the segment's own palettes and the firmware's is dropped
/// on the way in: a rule drawn across a row of a grid separates nothing.
/// </para>
/// </summary>
public partial class PickerDialog : Window
{
    private readonly ObservableCollection<object> _shown = [];

    private IReadOnlyList<object> _all = [];

    private object? _picked;

    public PickerDialog()
    {
        InitializeComponent();

        Tiles.ItemsSource = _shown;
        FilterBox.TextChanged += (_, _) => Narrow();
    }

    /// <summary>Puts the choices up and waits for one, or for the window to be shut.</summary>
    /// <param name="owner">The window this belongs over.</param>
    /// <param name="title">What is being picked, said at the top.</param>
    /// <param name="items">Everything on offer, in the order it should be read.</param>
    /// <param name="current">What is picked now, so the window opens on it.</param>
    /// <returns>The choice, or null if there was not one.</returns>
    public static async Task<object?> PickAsync(
        Window owner, string title, IEnumerable<object> items, object? current)
    {
        ArgumentNullException.ThrowIfNull(items);

        var dialog = new PickerDialog();

        dialog.TitleText.Text = title;

        // A rule is an item in a drop-down because a drop-down has no notion of a real one. In a
        // grid it would be a tile with nothing in it.
        dialog._all = [.. items.Where(item => item is not PaletteOption { IsSeparator: true })];

        dialog.Narrow();
        dialog.Tiles.SelectedItem = current;

        // Focus on Opened rather than here: focus set before a window is shown does not survive
        // being shown, because opening moves it.
        dialog.Opened += (_, _) =>
        {
            dialog.FilterBox.Focus();

            if (dialog.Tiles.SelectedItem is { } selected)
            {
                dialog.Tiles.ScrollIntoView(selected);
            }
        };

        await dialog.ShowDialog(owner);

        return dialog._picked;
    }

    /// <summary>
    /// Everything whose name contains what has been typed, matched the way a reader means it.
    /// </summary>
    /// <remarks>
    /// Contains rather than starts-with: "fire" should find "Halloween Eyes"' neighbours "Fire
    /// Flicker" and "Lake Fire" alike, and nobody hunting a palette remembers which word of its
    /// name came first.
    /// </remarks>
    private void Narrow()
    {
        string typed = FilterBox.Text?.Trim() ?? string.Empty;

        object? was = Tiles.SelectedItem;

        _shown.Clear();

        foreach (object item in _all)
        {
            if (typed.Length == 0 || Named(item).Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            {
                _shown.Add(item);
            }
        }

        // Keep the selection if it survived the filter, so typing and then clearing the box does
        // not quietly lose what was picked.
        if (was is not null && _shown.Contains(was))
        {
            Tiles.SelectedItem = was;
        }
        else if (typed.Length > 0 && _shown.Count > 0)
        {
            // Narrowed to something: Enter should take the obvious one rather than nothing.
            Tiles.SelectedItem = _shown[0];
        }

        CountText.Text = _shown.Count == _all.Count
            ? $"{_all.Count} to choose from"
            : $"{_shown.Count} of {_all.Count}";
    }

    private static string Named(object item) => item switch
    {
        PickerOption effect => effect.Name,
        PaletteOption palette => palette.Name,
        _ => item.ToString() ?? string.Empty,
    };

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (Tiles.SelectedItem is not { } selected)
        {
            return;
        }

        _picked = selected;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
