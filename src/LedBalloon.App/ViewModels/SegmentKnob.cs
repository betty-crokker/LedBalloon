using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One of an effect's three extra sliders, under the name the effect gives it.
/// </summary>
/// <remarks>
/// WLED gives every effect five sliders and three tick boxes. The first two sliders are the ones
/// everybody knows - speed and intensity - and the other six were parsed out of the metadata here
/// for months and shown to nobody, which left any effect whose character lives in one of them half
/// reachable. Fire 2012's "Boost", Palette's "Rotation", Blends' "Blend speed".
/// </remarks>
public sealed partial class SegmentKnob : ObservableObject
{
    private readonly Action<int, byte>? _set;

    public SegmentKnob(int index, string label, byte value, Action<int, byte>? set = null)
    {
        Index = index;
        Label = label;
        _value = value;
        _set = set;
    }

    /// <summary>Which of the three it is, zero first - c1, c2 and c3 on the wire.</summary>
    public int Index { get; }

    public string Label { get; }

    [ObservableProperty] private double _value;

    partial void OnValueChanged(double value) =>
        _set?.Invoke(Index, (byte)Math.Clamp(value, 0, 255));
}

/// <summary>
/// One of an effect's three tick boxes, under the name the effect gives it.
/// </summary>
/// <remarks>
/// Not a lesser kind of slider. Palette's first box is "Animate Shift", and whether it is ticked is
/// the difference between a gradient lying across the run and one scrolling along it - which is a
/// bigger difference than anything either of its sliders makes.
/// </remarks>
public sealed partial class SegmentSwitch : ObservableObject
{
    private readonly Action<int, bool>? _set;

    public SegmentSwitch(int index, string label, bool on, Action<int, bool>? set = null)
    {
        Index = index;
        Label = label;
        _on = on;
        _set = set;
    }

    /// <summary>Which of the three it is, zero first - o1, o2 and o3 on the wire.</summary>
    public int Index { get; }

    public string Label { get; }

    [ObservableProperty] private bool _on;

    partial void OnOnChanged(bool value) => _set?.Invoke(Index, value);
}
