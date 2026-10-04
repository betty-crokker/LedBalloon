using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core;
using LedBalloon.Core.Layout;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// Something a timer can be pointed at, named rather than numbered.
/// <para>
/// A WLED timer stores a preset slot, and the same scene sits in a different slot on each box. So
/// the slot cannot be the identity here: the name is, and the save works out the number for each
/// controller in turn. That is what lets one line mean the whole house, which is what a timer should
/// mean — the lights do not come on in halves.
/// </para>
/// </summary>
/// <param name="Name">What it is called, and what it is matched by.</param>
/// <param name="Switch">True for "everything on", false for "everything off", null for a scene.</param>
public sealed record PresetChoice(string Name, bool? Switch = null)
{
    public override string ToString() => Name;

    /// <summary>The two ends of the house switch, offered on every timer.</summary>
    /// <remarks>
    /// Said at length because "Turn everything on" was read as "show the scene I am looking at".
    /// It is an easy reading - a scene was on the screen at the time - and the difference matters:
    /// these two pick no scene at all. They are the timetabled form of the switch at the top of
    /// the main screen, sending the same on and off and nothing else, so the house comes back
    /// to whatever it was last showing. A timer that should decide the colours wants a scene
    /// by name.
    /// </remarks>
    public static IReadOnlyList<PresetChoice> Switches { get; } =
    [
        new("Turn the lights off", false),
        new("Turn the lights on (whatever was showing last)", true),
    ];

    /// <summary>The switch and then the scenes, which is the order they are worth reading in.</summary>
    public static IReadOnlyList<PresetChoice> Offered(IEnumerable<string> scenes) =>
        [.. Switches, .. scenes.Select(name => new PresetChoice(name))];
}

/// <summary>When a timetable entry fires, as something to pick from a list.</summary>
public sealed record TriggerChoice(SunTrigger Sun, string Name)
{
    public override string ToString() => Name;

    public static IReadOnlyList<TriggerChoice> All { get; } =
    [
        new(SunTrigger.None, "At a time"),
        new(SunTrigger.Sunrise, "At sunrise"),
        new(SunTrigger.Sunset, "At sunset"),
    ];
}

/// <summary>
/// One line of the house's timetable, editable.
/// <para>
/// The house's, not one controller's. Every box gets its own copy of every line, because a timer
/// that covers half the house is a fault waiting to be noticed on a dark evening — and because what
/// a person means by "off at half ten" has never once been "off at half ten on the north controller".
/// </para>
/// <para>
/// Which is why a scene is picked by name here. The slot it occupies differs per box, and a line
/// that stored one could only ever have meant one box.
/// </para>
/// </summary>
public sealed partial class ScheduleRow : ObservableObject
{
    /// <summary>What a timer can be pointed at: the switch, then the scenes every box holds.</summary>
    public required IReadOnlyList<PresetChoice> Presets { get; init; }

    public IReadOnlyList<TriggerChoice> Triggers => TriggerChoice.All;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _hour;
    [ObservableProperty] private int _minute;
    [ObservableProperty] private PresetChoice? _preset;
    [ObservableProperty] private TriggerChoice? _trigger = TriggerChoice.All[0];

    /// <summary>True when this entry hangs off the sun rather than the clock, so the time is moot.</summary>
    public bool IsClock => Trigger?.Sun is null or SunTrigger.None;

    /// <summary>Which end of the switch, when it is one rather than a scene.</summary>
    public bool? SwitchOn => Preset?.Switch;


    partial void OnTriggerChanged(TriggerChoice? value) => OnPropertyChanged(nameof(IsClock));

    partial void OnPresetChanged(PresetChoice? value) => OnPropertyChanged(nameof(SwitchOn));

    /// <summary>
    /// The entry as one controller stores it.
    /// </summary>
    /// <param name="presetId">
    /// The slot on the controller being written. Passed in because the same line lands in a
    /// different slot on each box, and for the switch in no slot at all until it has been written.
    /// </param>
    public ScheduledChange ToChange(int presetId) => new(
        Enabled: Enabled,
        Hour: IsClock ? System.Math.Clamp(Hour, 0, 23) : 0,
        Minute: IsClock ? System.Math.Clamp(Minute, 0, 59) : 0,
        PresetId: presetId,
        DaysOfWeek: 0x7F,
        Sun: Trigger?.Sun ?? SunTrigger.None);

    /// <summary>
    /// What makes two controllers' copies of a line the same line.
    /// </summary>
    /// <remarks>
    /// Everything a person set, and nothing about where it was read from. Lines that match on this
    /// are folded into one; lines that do not are left as they are, because two boxes disagreeing
    /// about when the house goes dark is worth seeing rather than quietly resolving.
    /// </remarks>
    public (string? Target, SunTrigger Sun, int Hour, int Minute, bool Enabled) Shape =>
        (Preset?.Name, Trigger?.Sun ?? SunTrigger.None, Hour, Minute, Enabled);

    /// <summary>Builds a row from what one controller reported, naming what its slot holds.</summary>
    public static ScheduleRow From(
        ScheduledChange change,
        string? presetName,
        IReadOnlyList<PresetChoice> presets)
    {
        var row = new ScheduleRow
        {
            Presets = presets,
            Enabled = change.Enabled,
            Hour = change.Hour > 23 ? 0 : change.Hour,
            Minute = change.Minute,
        };

        foreach (TriggerChoice trigger in TriggerChoice.All)
        {
            if (trigger.Sun == change.Sun)
            {
                row.Trigger = trigger;
            }
        }

        // The switch first, by the name in the slot the timer points at. These presets are the
        // app's own, and a reader should see "turn everything off" rather than the preset it is
        // implemented as.
        if (TimedSwitch.SwitchIn(presetName) is { } which)
        {
            row.Preset = PresetChoice.Switches.FirstOrDefault(p => p.Switch == which);
            return row;
        }

        row.Preset = presets.FirstOrDefault(
            p => p.Switch is null && string.Equals(p.Name, presetName, System.StringComparison.Ordinal));

        return row;
    }
}
