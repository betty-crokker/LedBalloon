using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// The house's timetable, in a window of its own.
/// <para>
/// A window rather than a panel, because this is not something anybody reads while picking colours:
/// it is opened, set, and closed, perhaps twice a year. Giving it a permanent column of the screen
/// put a long list of times in front of somebody who was choosing what the porch should look like.
/// </para>
/// <para>
/// Nothing in here writes to a controller. A timetable is part of the description of the house, so
/// it goes out when the rest of it does - "Done" closes the window, and the one Save on the Setup
/// screen is what reaches the lights. Cancel is a real cancel: the lines are put back as they were
/// when this opened, which the view model does, since it is the thing that knows.
/// </para>
/// </summary>
public partial class ScheduleDialog : Window
{
    private bool _saved;

    public ScheduleDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens the timetable and waits for the way out.</summary>
    /// <returns>True for Done, false for Cancel — and for the close button, which is a Cancel.</returns>
    public static async Task<bool> ShowAsync(Window owner, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var dialog = new ScheduleDialog { DataContext = viewModel };
        await dialog.ShowDialog(owner);

        return dialog._saved;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
