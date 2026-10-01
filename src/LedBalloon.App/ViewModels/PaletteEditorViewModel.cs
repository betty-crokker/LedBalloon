using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedBalloon.Core;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// The palettes one controller holds of its own, and what they are for.
/// </summary>
/// <remarks>
/// WLED's built-in palettes are firmware data, the same on every box and not editable by anyone.
/// These are files somebody uploaded, numbered down from 255, and the firmware knows them only by
/// that number - which is why its own UI calls them "Custom 0". The names live in the project
/// because there is nowhere on the controller to put one.
/// <para>
/// Editing one reaches further than editing a look, and with less to go on: a look has a name and
/// sits in a list, while a palette is a file several scenes may quietly be built on. So the reach is
/// said out loud before anything is changed.
/// </para>
/// </remarks>
public sealed partial class PaletteEditorViewModel : ObservableObject
{
    private readonly LedBalloonProject _project;
    private readonly DeviceViewModel _device;
    private readonly Func<PaletteStopRow, Task>? _pickColor;

    private bool _settling;

    public PaletteEditorViewModel(
        LedBalloonProject project,
        DeviceViewModel device,
        Func<PaletteStopRow, Task>? pickColor = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _pickColor = pickColor;
    }

    /// <summary>Which controller's palettes these are, for the heading.</summary>
    public string ControllerName => _device.DisplayName;

    /// <summary>The palettes this controller holds, lowest slot first.</summary>
    public ObservableCollection<PaletteChoice> Palettes { get; } = [];

    /// <summary>The stops of whichever palette is open.</summary>
    public ObservableCollection<PaletteStopRow> Stops { get; } = [];

    [ObservableProperty] private PaletteChoice? _chosen;

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>What this would do to the rest of the house. Empty when it reaches one place or none.</summary>
    [ObservableProperty] private string _warning = string.Empty;

    /// <summary>What went wrong, or what just happened.</summary>
    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty] private bool _busy;

    public bool HasChosen => Chosen is not null;

    public bool HasWarning => Warning.Length > 0;

    /// <summary>True while there is room for another. WLED reads palette0 to palette9 and no further.</summary>
    public bool CanAdd => Palettes.Count < CustomPaletteCopier.MaxSlots;

    /// <summary>
    /// True only for the last one, which is the only one that can go.
    /// </summary>
    /// <remarks>
    /// WLED's loader reads the files upward and stops at the first one missing, so removing a
    /// palette from the middle would orphan every palette above it. Its own UI offers "remove last"
    /// and nothing else, for exactly this reason.
    /// </remarks>
    public bool CanRemoveChosen => Chosen is not null && Chosen == Palettes.LastOrDefault();

    /// <summary>The gradient as it stands, for the bar above the stops and for the preview strip.</summary>
    public WledPalette Edited => new() { Stops = [.. Stops.Select(s => s.Stop).OrderBy(s => s.Position)] };

    /// <summary>What the segment editor would show for it, so the two agree.</summary>
    public IBrush Gradient => GradientOf(Edited);

    /// <summary>Reads what the controller holds. Called once as the window opens.</summary>
    public async Task LoadAsync()
    {
        Busy = true;

        try
        {
            using var files = new WledFileSystemClient(_device.Host);
            List<byte[]> held = await CustomPaletteCopier.ReadAllAsync(files);

            Palettes.Clear();

            for (int slot = 0; slot < held.Count; slot++)
            {
                int id = CustomPaletteCopier.IdForSlot(slot);

                Palettes.Add(new PaletteChoice(
                    slot,
                    id,
                    _project.NameForPalette(_device.DeviceKey, id),
                    [.. CustomPaletteFile.Parse(held[slot])]));
            }

            OnPropertyChanged(nameof(CanAdd));

            Chosen = Palettes.FirstOrDefault();
            Status = Palettes.Count == 0
                ? "This controller has no palettes of its own yet."
                : string.Empty;
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            Status = "Could not read this controller's palettes.";
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnChosenChanged(PaletteChoice? value)
    {
        _settling = true;

        try
        {
            Stops.Clear();

            foreach (PaletteStop stop in value?.Stops ?? [])
            {
                Stops.Add(Row(stop));
            }

            Name = value?.Name ?? string.Empty;
        }
        finally
        {
            _settling = false;
        }

        Warning = value is null
            ? string.Empty
            : CustomPaletteUsage.Warn(
                CustomPaletteUsage.Find(_project, _device.DeviceKey ?? string.Empty, value.Id));

        OnPropertyChanged(nameof(HasChosen));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(CanRemoveChosen));
        Redraw();
    }

    private PaletteStopRow Row(PaletteStop stop) =>
        new(stop, Redraw, Drop, _pickColor);

    private void Redraw()
    {
        OnPropertyChanged(nameof(Edited));
        OnPropertyChanged(nameof(Gradient));
    }

    private void Drop(PaletteStopRow row)
    {
        // Two is the fewest a gradient can be made of. Below that there is nothing to blend between
        // and WLED expands it into a flat colour, which is what "My color" is already for.
        if (Stops.Count <= 2)
        {
            Status = "A palette needs at least two colors.";
            return;
        }

        Stops.Remove(row);
        Redraw();
    }

    [RelayCommand]
    private void AddStop()
    {
        if (Stops.Count >= CustomPaletteFile.MaxStops)
        {
            Status = $"A palette holds at most {CustomPaletteFile.MaxStops} colors.";
            return;
        }

        // Dropped into the widest gap rather than at the end, so it lands somewhere it can be seen
        // instead of on top of a stop that is already there.
        PaletteStop[] sorted = [.. Stops.Select(s => s.Stop).OrderBy(s => s.Position)];
        int at = 128, widest = -1;

        for (int i = 0; i + 1 < sorted.Length; i++)
        {
            int gap = sorted[i + 1].Position - sorted[i].Position;

            if (gap > widest)
            {
                widest = gap;
                at = sorted[i].Position + (gap / 2);
            }
        }

        Stops.Add(Row(new PaletteStop((byte)at, Sample(at / 255d))));
        Redraw();
    }

    /// <summary>The colour the gradient already shows there, so adding a stop changes nothing by itself.</summary>
    private RgbColor Sample(double at) =>
        Edited.ColorAt(at, RgbColor.White, RgbColor.Black, RgbColor.Black);

    [RelayCommand]
    private void AddPalette()
    {
        if (!CanAdd)
        {
            Status = $"A controller holds at most {CustomPaletteCopier.MaxSlots} palettes.";
            return;
        }

        int slot = Palettes.Count;

        Palettes.Add(new PaletteChoice(
            slot,
            CustomPaletteCopier.IdForSlot(slot),
            null,
            [
                new PaletteStop(0, new RgbColor(255, 0, 0)),
                new PaletteStop(255, new RgbColor(0, 0, 255)),
            ]));

        OnPropertyChanged(nameof(CanAdd));
        Chosen = Palettes[^1];
        Status = "New palette. It is not on the controller until you save.";
    }

    /// <summary>Writes the open palette to the controller and its name to the project.</summary>
    public async Task<bool> SaveAsync()
    {
        if (Chosen is not { } choice)
        {
            return true;
        }

        Busy = true;

        try
        {
            using var files = new WledFileSystemClient(_device.Host);

            await files.UploadAsync(
                CustomPaletteCopier.FileForSlot(choice.Slot),
                CustomPaletteFile.Write(Edited.Stops));

            _project.NamePalette(_device.DeviceKey, choice.Id, Name);

            Status = "Saved.";
            return true;
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            // Said rather than thrown, because the window is still open and the work is still there.
            Status = "Could not write that palette to the controller.";
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Drops the last palette, which is the only one that can go.</summary>
    public async Task RemoveLastAsync()
    {
        if (Palettes.LastOrDefault() is not { } last)
        {
            return;
        }

        Busy = true;

        try
        {
            await _device.Device.ApplyNowAsync(new WledState { RemoveLastCustomPalette = true });

            _project.NamePalette(_device.DeviceKey, last.Id, null);

            Palettes.Remove(last);
            OnPropertyChanged(nameof(CanAdd));

            Chosen = Palettes.LastOrDefault();
            Status = "Removed.";
        }
        catch (Exception ex) when (ex is WledException or HttpRequestException or TaskCanceledException)
        {
            Status = "Could not remove that palette.";
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnNameChanged(string value)
    {
        if (!_settling && Chosen is { } choice)
        {
            choice.Name = value;
        }
    }

    partial void OnWarningChanged(string value) => OnPropertyChanged(nameof(HasWarning));

    /// <summary>One palette as a left-to-right gradient, the same way the pickers draw them.</summary>
    internal static IBrush GradientOf(WledPalette palette)
    {
        const int Steps = 24;
        var stops = new GradientStops();

        for (int i = 0; i < Steps; i++)
        {
            double t = i / (double)(Steps - 1);
            RgbColor c = palette.ColorAt(t, RgbColor.White, RgbColor.Black, RgbColor.Black);

            stops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), t));
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = stops,
        };
    }
}

/// <summary>One of a controller's own palettes, as a row in the list.</summary>
public sealed partial class PaletteChoice : ObservableObject
{
    public PaletteChoice(int slot, int id, string? name, IReadOnlyList<PaletteStop> stops)
    {
        Slot = slot;
        Id = id;
        Stops = stops;
        _name = name ?? string.Empty;
    }

    /// <summary>Which file it lives in: palette0.json upward.</summary>
    public int Slot { get; }

    /// <summary>The number WLED answers to, counted down from 255.</summary>
    public int Id { get; }

    public IReadOnlyList<PaletteStop> Stops { get; }

    [ObservableProperty] private string _name;

    /// <summary>What to call it when nobody has, which is what WLED's own UI shows.</summary>
    public string Shown => Name is { Length: > 0 } called ? called : $"Custom {Slot}";

    public IBrush Gradient =>
        PaletteEditorViewModel.GradientOf(new WledPalette { Stops = [.. Stops] });

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Shown));

    public override string ToString() => Shown;
}
