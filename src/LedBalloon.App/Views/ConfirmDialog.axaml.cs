using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LedBalloon.App.ViewModels;

namespace LedBalloon.App.Views;

/// <summary>
/// The one dialog in the app: a question, an optional tick box, and up to three ways out.
/// <para>
/// Deliberately plain. It exists because a few things here cannot be undone - removing a run,
/// closing with work not written to the controllers - and those had been going ahead silently.
/// </para>
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmResult _result = ConfirmResult.Cancelled;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Puts a question to the user and waits for the answer.</summary>
    public static async Task<ConfirmResult> AskAsync(Window owner, ConfirmRequest request)
    {
        var dialog = new ConfirmDialog();

        dialog.TitleText.Text = request.Title;
        dialog.MessageText.Text = request.Message;
        dialog.AcceptButton.Content = request.AcceptText;
        dialog.CancelButton.Content = request.CancelText;

        if (request.AlternateText is { Length: > 0 } alternate)
        {
            dialog.AlternateButton.Content = alternate;
            dialog.AlternateButton.IsVisible = true;
        }

        if (request.OptionText is { Length: > 0 } option)
        {
            dialog.OptionBox.Content = option;
            dialog.OptionBox.IsChecked = request.OptionDefault;
            dialog.OptionBox.IsVisible = true;
        }

        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Finish(ConfirmChoice.Accept);

    private void OnAlternate(object? sender, RoutedEventArgs e) => Finish(ConfirmChoice.Alternate);

    private void OnCancel(object? sender, RoutedEventArgs e) => Finish(ConfirmChoice.Cancel);

    private void Finish(ConfirmChoice choice)
    {
        _result = new ConfirmResult(choice, OptionBox.IsChecked == true);
        Close();
    }
}
