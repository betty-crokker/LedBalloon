using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
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
        if (ViewModel is not { } viewModel)
        {
            return;
        }

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

            byte[] original;
            await using (Stream stream = await files[0].OpenReadAsync())
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                original = buffer.ToArray();
            }

            // Prepared on the way in, not on the way out: the photo has to fit on a controller's
            // flash, and the preview should show what actually gets stored.
            byte[] prepared = await Task.Run(() =>
                PhotoPreparer.ToStoredJpeg(original, DeviceProjectStore.MaxPhotoBytes));

            viewModel.PhotoBytes = prepared;
            viewModel.Project.PhotoPath = DeviceProjectStore.PhotoFile;

            string summary = $"{original.Length / 1024} KB down to {prepared.Length / 1024} KB";
            viewModel.Status = prepared.Length > DeviceProjectStore.MaxPhotoBytes
                ? $"Photo loaded ({summary}), still too large for the controllers. Crop it and try again."
                : $"Photo loaded ({summary}). Pick a segment, then draw it onto the house.";
        }
        catch (Exception ex)
        {
            viewModel.Status = $"Could not load the photo: {ex.Message}";
        }
    }

    private void OnCanvasPointAdded(object? sender, LayoutPoint point) =>
        ViewModel?.AddPointToSelectedSegment(point.X, point.Y);
}
