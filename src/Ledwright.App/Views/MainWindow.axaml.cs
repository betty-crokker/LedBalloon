using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Ledwright.App.ViewModels;
using Ledwright.Core.Layout;

namespace Ledwright.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OnLoadPhoto(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;
            if (storage is null)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pick a photo of the house at dusk",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll],
            });

            if (files.Count == 0)
            {
                return;
            }

            await using Stream stream = await files[0].OpenReadAsync();
            House.Photo = new Bitmap(stream);

            if (ViewModel is { } viewModel)
            {
                viewModel.Project.PhotoPath = files[0].Path.LocalPath;
                viewModel.Status = "Photo loaded. Pick a segment, then draw it onto the house.";
            }
        }
        catch (Exception ex)
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.Status = $"Could not load the photo: {ex.Message}";
            }
        }
    }

    private void OnCanvasPointAdded(object? sender, LayoutPoint point) =>
        ViewModel?.AddPointToSelectedSegment(point.X, point.Y);
}
