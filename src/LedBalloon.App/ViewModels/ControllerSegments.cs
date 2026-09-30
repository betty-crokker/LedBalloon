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
        Action<string, byte>? brightnessChanged,
        bool settable = true,
        string brightnessReaches = "")
    {
        Key = key;
        Name = name;
        _brightnessChanged = brightnessChanged;
        BrightnessReaches = brightnessReaches;

        // Settable when the scene is about this controller at all, rather than when it happens to
        // have a brightness for it already: a scene that lights something here but has not been
        // given a brightness yet still has one to give.
        SetByTheScene = settable;

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

    /// <summary>
    /// The one thing a scene can promise that the hardware cannot keep, said plainly.
    /// </summary>
    /// <remarks>
    /// A controller has one brightness. A scene that lights some of its segments and leaves others
    /// alone has two intentions for it and one dial, so the brightness reaches the segments it is
    /// leaving alone as well - "keeps doing what it was doing" is then true of their effect and
    /// their colors and false of how bright they are.
    /// <para>
    /// Nothing can fix this, so it is written down rather than engineered around. It is reachable
    /// only by adopting a preset that does not cover every segment, because a scene made here
    /// always covers all of them.
    /// </para>
    /// </remarks>
    public string BrightnessReaches { get; } = string.Empty;

    public bool HasBrightnessWarning => BrightnessReaches.Length > 0;

    /// <summary>
    /// The brightness as a proportion, because 38 beside a slider says nothing about how dark it is.
    /// </summary>
    public string Percent => $"{Math.Round(Brightness / 255d * 100)}%";

    [ObservableProperty] private double _brightness;

    partial void OnBrightnessChanged(double value)
    {
        if (_settling)
        {
            return;
        }

        OnPropertyChanged(nameof(Percent));
        _brightnessChanged?.Invoke(Key, (byte)Math.Clamp(value, 0, 255));
    }
}
