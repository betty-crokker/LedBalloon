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
/// <para>
/// Every slider runs 0 to 255 whatever room the stop has, so a thumb halfway along means halfway
/// along the gradient on every row. Bounding each slider by its neighbours instead would be the
/// obvious way to stop them crossing, and it would put a thumb at a third of its track above a
/// label reading 15%.
/// </para>
/// </remarks>
public sealed partial class PaletteStopRow : ObservableObject
{
    private readonly Action? _changed;
    private readonly Action<PaletteStopRow>? _remove;
    private readonly Func<PaletteStopRow, Task>? _pick;
    private readonly Func<PaletteStopRow, (double Low, double High)>? _room;

    public PaletteStopRow(
        PaletteStop stop,
        Action? changed = null,
        Action<PaletteStopRow>? remove = null,
        Func<PaletteStopRow, Task>? pick = null,
        Func<PaletteStopRow, (double Low, double High)>? room = null)
    {
        _changed = changed;
        _remove = remove;
        _pick = pick;
        _room = room;

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
        // A stop cannot pass its neighbours. The rows are the gradient read top to bottom, and one
        // sliding past another would either put the list out of order or make a row jump while the
        // hand dragging it was still on the slider; the slider simply stops instead.
        //
        // Set back rather than refused, so the thumb follows the limit instead of running on ahead
        // of the value it is bound to. The second pass clamps to itself and falls through.
        if (_room?.Invoke(this) is { } room)
        {
            double held = Math.Clamp(value, room.Low, room.High);

            if (held != value)
            {
                Position = held;
                return;
            }
        }

        OnPropertyChanged(nameof(Percent));
        _changed?.Invoke();
    }

    partial void OnPickedChanged(Color value)
    {
        OnPropertyChanged(nameof(Brush));
        _changed?.Invoke();
    }
}
