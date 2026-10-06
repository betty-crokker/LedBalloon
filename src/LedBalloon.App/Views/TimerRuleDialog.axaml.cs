using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// The rule one timer runs under: which days it is allowed to fire on, and between which dates.
/// <para>
/// A window rather than more controls on the line, because this is the rare half of a timer. Most
/// of them run every day of every year, and putting seven tick boxes and two dates on every row
/// would charge everybody for the one timer in ten that needs them.
/// </para>
/// <para>
/// Edits the row directly, and there is no Cancel. Nothing here reaches a controller until the save
/// does, and the row says what it is doing on the line underneath it — so backing out is a matter
/// of looking at it and changing it back, which is cheaper than the bookkeeping a Cancel would need.
/// </para>
/// </summary>
public partial class TimerRuleDialog : Window
{
    public TimerRuleDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens the rule for one timer and waits for the window to be shut.</summary>
    public static async Task ShowAsync(Window owner, ScheduleRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var dialog = new TimerRuleDialog { DataContext = row };
        await dialog.ShowDialog(owner);
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ScheduleRow row)
        {
            row.EveryDayAllYear();
        }
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();
}
