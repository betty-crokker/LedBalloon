using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One of the three color slots a WLED effect can read, as the effect itself names it.
/// <para>
/// Effects differ in how many they use and what they mean by them. Aurora reads all three, Two Dots
/// reads two and a background, Colortwinkles reads none. Offering a single box called "Color" was
/// wrong in both directions: it hid two thirds of what most effects can be told, and it offered a
/// control to effects that ignore it.
/// </para>
/// </summary>
public sealed partial class SegmentColorSlot : ObservableObject
{
    private readonly Action<int, Color>? _changed;
    private readonly bool _settling;

    public SegmentColorSlot(int index, string label, Color color, Action<int, Color>? changed)
    {
        Index = index;
        Label = label;
        _changed = changed;

        _settling = true;
        Picked = color;
        _settling = false;
    }

    /// <summary>Which of WLED's three slots this is, zero-based.</summary>
    public int Index { get; }

    /// <summary>
    /// What the effect calls this slot, expanded from the shorthand it ships.
    /// </summary>
    /// <remarks>
    /// WLED's metadata gives the label as the firmware stores it, which for the effects that bother
    /// naming their slots means <c>Bg</c>, <c>Fx</c>, <c>L</c>, <c>R</c> or a bare digit. Those are
    /// notes to whoever wrote the effect, not to whoever is looking at a house.
    /// </remarks>
    public string Label { get; }

    [ObservableProperty] private Color _picked;

    public IBrush Brush => new SolidColorBrush(Picked);

    partial void OnPickedChanged(Color value)
    {
        OnPropertyChanged(nameof(Brush));

        if (!_settling)
        {
            _changed?.Invoke(Index, value);
        }
    }
}
