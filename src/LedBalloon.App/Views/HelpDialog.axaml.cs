using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LedBalloon.App.Views;

/// <summary>
/// What the words mean, in one place.
/// <para>
/// Every one of these terms is on screen somewhere and none of them was ever defined, which is how
/// the app came to use two words for a segment without anyone noticing. Ordered smallest to largest
/// because each is built out of the one before it, and a definition that leans on a word further down
/// the page is no definition at all.
/// </para>
/// </summary>
public partial class HelpDialog : Window
{
    public HelpDialog()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner) => new HelpDialog().ShowDialog(owner);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
