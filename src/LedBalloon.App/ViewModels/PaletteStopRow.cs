using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedBalloon.Core;
using LedBalloon.Core.Models;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One stop in a custom palette: a colour and how far along the gradient it sits.
/// </summary>
/// <remarks>
/// Position is given as a number on a slider rather than by dragging a marker on the gradient. A
/// marker is nicer to use and much worse to use precisely, and two stops a pixel apart are
/// impossible to tell from one - which matters here, because WLED expands whatever is written into
/// sixteen evenly spaced entries and a stop that lands on the same expansion slot as its neighbour
/// simply disappears.
/// </remarks>
public sealed partial class PaletteStopRow : ObservableObject
{
    private readonly Action? _changed;
    private readonly Action<PaletteStopRow>? _remove;
    private readonly Func<PaletteStopRow, Task>? _pick;

    public PaletteStopRow(
        PaletteStop stop,
        Action? changed = null,
        Action<PaletteStopRow>? remove = null,
        Func<PaletteStopRow, Task>? pick = null)
    {
        _changed = changed;
        _remove = remove;
        _pick = pick;

        _position = stop.Position;
        _picked = Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B);
    }

    [ObservableProperty] private double _position;

    [ObservableProperty] private Color _picked;

    public IBrush Brush => new SolidColorBrush(Picked);

    /// <summary>Where it sits as a percentage, because 207 of 255 is not a position anybody reads.</summary>
    public string Percent => $"{Math.Round(Position / 255d * 100)}%";

    public PaletteStop Stop =>
        new((byte)Math.Clamp(Position, 0, 255), new RgbColor(Picked.R, Picked.G, Picked.B));

    [RelayCommand]
    private async Task PickAsync()
    {
        if (_pick is { } pick)
        {
            await pick(this);
        }
    }

    [RelayCommand]
    private void Remove() => _remove?.Invoke(this);

    partial void OnPositionChanged(double value)
    {
        OnPropertyChanged(nameof(Percent));
        _changed?.Invoke();
    }

    partial void OnPickedChanged(Color value)
    {
        OnPropertyChanged(nameof(Brush));
        _changed?.Invoke();
    }
}
