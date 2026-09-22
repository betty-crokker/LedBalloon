using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LedBalloon.App.ViewModels;
using LedBalloon.Core;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The view model asks its questions through here, so it never has to know what a window is.
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.Ask = request => ConfirmDialog.AskAsync(this, request);
            }
        };
    }

    private bool _shutDown;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Asks about unsaved work, then puts the lights out on the way out.
    /// <para>
    /// The close is cancelled once so the question can be put and the lights answered, then
    /// re-issued. Neither step survives the process exiting underneath it.
    /// </para>
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shutDown && ViewModel is { } viewModel)
        {
            e.Cancel = true;

            bool close;
            try
            {
                close = await viewModel.ConfirmClosingAsync();
            }
            catch
            {
                // A controller that has gone away must not trap the window open.
                close = true;
            }

            if (!close)
            {
                return;
            }

            _shutDown = true;

            try
            {
                await viewModel.ShutDownAsync();
            }
            catch
            {
                // Closing must not be blocked by a controller that has gone away.
            }

            Close();
            return;
        }

        base.OnClosing(e);
    }

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

            // Prepared on the way in, not on the way out: it has to fit whatever the controllers
            // actually have spare, and the preview should show what really gets stored.
            int budget = viewModel.PhotoBudgetBytes;
            PreparedPhoto prepared = await Task.Run(() => PhotoPreparer.Prepare(original, budget));

            viewModel.PhotoBytes = prepared.Jpeg;
            viewModel.MissingPhotoNotice = null;

            // Filed by content, never by path: where this file happens to live on this machine
            // means nothing on anyone else's.
            PhotoCache.Save(prepared.Jpeg);
            viewModel.Project.PhotoHash = prepared.Hash;

            viewModel.Status = prepared.FitsBudget
                ? $"Photo ready: {prepared}. Save to put it on the controllers with the layout."
                : $"Photo ready: {prepared}, but the controllers only have {budget / 1024} KB spare. " +
                  "It stays on this machine; send the file to anyone else who wants the preview.";
        }
        catch (Exception ex)
        {
            viewModel.Status = $"Could not load the photo: {ex.Message}";
        }
    }

    private void OnCanvasPointAdded(object? sender, LayoutPoint point) =>
        ViewModel?.AddPointToSelectedSegment(point.X, point.Y);

    /// <summary>A run was clicked on the photo, which is how you pick one once setup is done.</summary>
    private void OnSegmentPicked(object? sender, Segment segment) => ViewModel?.PickSegment(segment);
}
