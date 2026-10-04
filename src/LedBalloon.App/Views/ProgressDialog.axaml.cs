using System;
using Avalonia.Controls;
using Avalonia.Threading;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// What is happening, while it happens.
/// <para>
/// Saving the setup is several writes to every controller — the layout, the output lengths and
/// settings, the segment boundaries, the scenes, the timetable — and on a slow network it is a few
/// seconds of a window that does nothing. The buttons disable themselves, which says only that
/// something is going on; this says what.
/// </para>
/// <para>
/// It cannot be closed. The work is already in flight and there is nothing to cancel it with, so a
/// close button would either lie or leave the controllers half written.
/// </para>
/// </summary>
public partial class ProgressDialog : Window, IProgressHandle
{
    private bool _finished;

    public ProgressDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens it over <paramref name="owner"/> and hands back the way to close it.</summary>
    public static IProgressHandle Open(Window owner, string title)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new ProgressDialog();
        dialog.Heading.Text = title;

        // Not awaited: this returns when the window closes, and the caller is the thing that will
        // close it. Modal so the house cannot be edited out from under a save that is writing it.
        _ = dialog.ShowDialog(owner);

        return dialog;
    }

    public void Say(string step) =>
        Dispatcher.UIThread.Post(() => Step.Text = step);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Only the work that opened this may close it.
        if (!_finished)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Dispatcher.UIThread.Post(() =>
        {
            _finished = true;
            Close();
        });
    }
}
