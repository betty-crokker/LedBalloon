using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.ViewModels;

/// <summary>What has been asked for about a preset that no longer covers its strip.</summary>
public enum PresetRepair
{
    /// <summary>Nothing yet.</summary>
    None,

    /// <summary>Move its bounds to the runs as they are now, keeping its look.</summary>
    Stretch,

    /// <summary>Take it off the controller.</summary>
    Delete,
}

/// <summary>
/// One preset that no longer covers the strip it is on, the controller it is on, and what is to be
/// done about it.
/// <para>
/// The gap itself knows the preset and the two numbers; it does not know which box it came off, and
/// neither repair can be carried out without that.
/// </para>
/// <para>
/// Asked for rather than done. Both repairs write to a controller, and everything else that writes
/// to a controller in this app waits for "Save to controllers" - a button that acted the moment it
/// was pressed would be the only one here that did, and the row sitting unchanged afterwards reads
/// as a button that did not work.
/// </para>
/// </summary>
public sealed partial class PresetGapRow : ObservableObject
{
    /// <summary>Which controller holds it.</summary>
    public required string ControllerKey { get; init; }

    /// <summary>Where to write to.</summary>
    public required string Host { get; init; }

    /// <summary>What that controller is called, for the sentence.</summary>
    public required string ControllerName { get; init; }

    /// <summary>What is wrong, in LEDs.</summary>
    public required PresetGap Gap { get; init; }

    /// <summary>What has been asked for, and not yet saved.</summary>
    [ObservableProperty] private PresetRepair _pending;

    partial void OnPendingChanged(PresetRepair value)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(PendingNote));
    }

    /// <summary>What the preset is called.</summary>
    public string Name => Gap.Preset.DisplayName;

    /// <summary>True once something has been asked for.</summary>
    public bool IsPending => Pending is not PresetRepair.None;

    /// <summary>
    /// The problem, in the terms somebody standing in front of the house would use.
    /// </summary>
    /// <remarks>
    /// LEDs rather than segment bounds. "Covers 100 of 125" is a fact about a JSON document;
    /// "25 LEDs stay dark" is a fact about the porch.
    /// </remarks>
    public string Trouble =>
        $"{Name} on {ControllerName} was saved when the strip was shorter. " +
        $"{Gap.DarkLeds} LED{(Gap.DarkLeds == 1 ? "" : "s")} stay dark when it runs.";

    /// <summary>What will happen, and when, once something has been asked for.</summary>
    public string PendingNote => Pending switch
    {
        PresetRepair.Stretch => "Will be stretched to fit when you save to the controllers.",
        PresetRepair.Delete => "Will be deleted from the controller when you save.",
        _ => string.Empty,
    };
}
