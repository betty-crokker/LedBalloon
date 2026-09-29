using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// One controller's part of a scene: the segments wired to it, and the brightness they share.
/// <para>
/// Brightness belongs to a controller, not to the house and not to a segment — a scene becomes one
/// WLED preset per controller and each of those carries its own. That is hard to act on while the
/// segment list is a flat run of names, because nothing on screen says which of them a brightness
/// would reach. Grouping them under the controller puts the slider beside the segments it governs
/// and answers the question by arrangement rather than by explanation.
/// </para>
/// </summary>
public sealed partial class ControllerSegments : ObservableObject
{
    private readonly Action<string, byte>? _brightnessChanged;

    /// <summary>True while the constructor is filling values in, so they do not read as edits.</summary>
    private readonly bool _settling;

    public ControllerSegments(
        string key,
        string name,
        byte? sceneBrightness,
        byte? liveBrightness,
        Action<string, byte>? brightnessChanged)
    {
        Key = key;
        Name = name;
        _brightnessChanged = brightnessChanged;

        SetByTheScene = sceneBrightness is not null;

        _settling = true;

        // What the scene says, or failing that what the controller is actually doing. Never zero as
        // a stand-in for "no opinion": zero is a brightness, and it is how the Off preset works.
        Brightness = sceneBrightness ?? liveBrightness ?? 128;

        _settling = false;
    }

    public string Key { get; }

    public string Name { get; }

    public ObservableCollection<SceneSegmentRow> Segments { get; } = [];

    /// <summary>
    /// False when the open scene says nothing about this controller's brightness, so the slider is
    /// showing what the controller happens to be doing rather than what the scene asks for.
    /// </summary>
    /// <remarks>
    /// Drives whether the slider can be moved, rather than a sentence beside it saying it is not in
    /// force. A control that cannot do anything should look like one.
    /// </remarks>
    public bool SetByTheScene { get; }

    [ObservableProperty] private double _brightness;

    partial void OnBrightnessChanged(double value)
    {
        if (_settling)
        {
            return;
        }

        _brightnessChanged?.Invoke(Key, (byte)Math.Clamp(value, 0, 255));
    }
}
