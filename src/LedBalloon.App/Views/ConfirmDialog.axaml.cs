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

        if (request.InputLabel is { Length: > 0 } label)
        {
            dialog.InputLabel.Text = label;
            dialog.InputBox.Text = request.InputDefault ?? string.Empty;
            dialog.InputArea.IsVisible = true;

            // Named and ready to type over, because the default is a placeholder rather than an
            // answer: nobody wants a house full of scenes called "New scene".
            dialog.InputBox.SelectAll();
            dialog.InputBox.Focus();
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
        _result = new ConfirmResult(
            choice,
            OptionBox.IsChecked == true,
            InputArea.IsVisible ? InputBox.Text?.Trim() : null);
        Close();
    }
}
