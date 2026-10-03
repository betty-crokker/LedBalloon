using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core;

namespace LedBalloon.App.ViewModels;

/// <summary>
/// Something a timer can be pointed at: a scene one controller holds, or the house switch.
/// <para>
/// A scene carries the slot that box happens to keep it in, because that is all a WLED timer
/// stores. The switch carries no slot at all - the preset behind it is written at save time, and
/// lands wherever each controller has room, which is not the same slot on each.
/// </para>
/// </summary>
/// <param name="Id">The slot, for a scene. Zero for the switch, which has no slot until it is saved.</param>
/// <param name="Name">What it is called in the picker.</param>
/// <param name="Switch">True for "everything on", false for "everything off", null for a scene.</param>
public sealed record PresetChoice(int Id, string Name, bool? Switch = null)
{
    public override string ToString() => Name;

    /// <summary>The two ends of the house switch, offered on every timer.</summary>
    public static IReadOnlyList<PresetChoice> Switches { get; } =
    [
        new(0, "Turn everything off", false),
        new(0, "Turn everything on", true),
    ];

    /// <summary>The switch and then the scenes, which is the order they are worth reading in.</summary>
    public static IReadOnlyList<PresetChoice> Offered(IEnumerable<PresetChoice> scenes) =>
        [.. Switches, .. scenes];
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
/// One line of a controller's timetable, editable.
/// <para>
/// The choices are listed per controller rather than merged, unlike everywhere else in the app.
/// A timer stores a slot number and the controller runs whatever is in that slot, so this is the
/// one place where which box holds a scene genuinely matters — and a scene that has not been
/// saved is not on any box yet, so it is not there to be fired.
/// </para>
/// </summary>
public sealed partial class ScheduleRow : ObservableObject
{
    /// <summary>
    /// Which controller runs it, or null when it is the whole house.
    /// <para>
    /// Null only for the switch. A scene lives in a different slot on each box and a timer stores a
    /// slot, so a scene timer belongs to one controller and says which. The switch does not: the
    /// preset behind it is the app's own, written to every controller, so one row can mean "the
    /// whole house, at this time" and the save works out the slots.
    /// </para>
    /// </summary>
    public string? ControllerKey { get; init; }

    /// <summary>What that controller is called, for the row's heading.</summary>
    public required string ControllerName { get; init; }

    /// <summary>What that controller holds, which is what a timer can point at.</summary>
    public required IReadOnlyList<PresetChoice> Presets { get; init; }

    public IReadOnlyList<TriggerChoice> Triggers => TriggerChoice.All;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _hour;
    [ObservableProperty] private int _minute;
    [ObservableProperty] private PresetChoice? _preset;
    [ObservableProperty] private TriggerChoice? _trigger = TriggerChoice.All[0];

    /// <summary>True when this entry hangs off the sun rather than the clock, so the time is moot.</summary>
    public bool IsClock => Trigger?.Sun is null or SunTrigger.None;

    /// <summary>True when this row is the house switch rather than a scene on one box.</summary>
    public bool IsSwitch => Preset?.Switch is not null;

    /// <summary>Which end of the switch, when it is one.</summary>
    public bool? SwitchOn => Preset?.Switch;

    partial void OnTriggerChanged(TriggerChoice? value) => OnPropertyChanged(nameof(IsClock));

    partial void OnPresetChanged(PresetChoice? value)
    {
        OnPropertyChanged(nameof(IsSwitch));
        OnPropertyChanged(nameof(SwitchOn));
        OnPropertyChanged(nameof(Where));
    }

    /// <summary>Which part of the house it acts on, for the row's heading.</summary>
    public string Where => IsSwitch ? "The whole house" : ControllerName;

    /// <summary>
    /// The entry as the controller stores it.
    /// </summary>
    /// <param name="presetId">
    /// The slot on the controller being written. Passed in rather than read off
    /// <see cref="Preset"/> because the switch has no slot until the preset behind it has been
    /// written, and it does not land in the same slot on every box.
    /// </param>
    public ScheduledChange ToChange(int presetId) => new(
        Enabled: Enabled,
        Hour: IsClock ? System.Math.Clamp(Hour, 0, 23) : 0,
        Minute: IsClock ? System.Math.Clamp(Minute, 0, 59) : 0,
        PresetId: presetId,
        DaysOfWeek: 0x7F,
        Sun: Trigger?.Sun ?? SunTrigger.None);

    /// <summary>Builds a row from what a controller reported.</summary>
    public static ScheduleRow From(
        ScheduledChange change,
        string controllerKey,
        string controllerName,
        IReadOnlyList<PresetChoice> presets)
    {
        var row = new ScheduleRow
        {
            ControllerKey = controllerKey,
            ControllerName = controllerName,
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

        // The switch first, by the name in the slot the timer points at: these presets are the
        // app's own and a reader should see "turn everything off", not the preset it is implemented
        // as. Falling through to the scene list would show the implementation.
        string? pointedAt = null;
        foreach (PresetChoice preset in presets)
        {
            if (preset.Id == change.PresetId && preset.Switch is null)
            {
                pointedAt = preset.Name;
            }
        }

        if (LedBalloon.Core.Layout.TimedSwitch.SwitchIn(pointedAt) is { } which)
        {
            foreach (PresetChoice preset in PresetChoice.Switches)
            {
                if (preset.Switch == which)
                {
                    row.Preset = preset;
                }
            }

            return row;
        }

        foreach (PresetChoice preset in presets)
        {
            if (preset.Id == change.PresetId && preset.Switch is null)
            {
                row.Preset = preset;
            }
        }

        return row;
    }
}
