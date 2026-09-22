using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LedBalloon.Core;

namespace LedBalloon.App.ViewModels;

/// <summary>A preset on one controller, as something to pick from a list.</summary>
public sealed record PresetChoice(int Id, string Name)
{
    public override string ToString() => Name;
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
/// Presets are listed per controller rather than merged, unlike everywhere else in the app. A
/// timer stores a slot number and the controller runs whatever is in that slot, so this is the one
/// place where which box holds a preset genuinely matters.
/// </para>
/// </summary>
public sealed partial class ScheduleRow : ObservableObject
{
    /// <summary>Which controller runs it.</summary>
    public required string ControllerKey { get; init; }

    /// <summary>What that controller is called, for the row's heading.</summary>
    public required string ControllerName { get; init; }

    /// <summary>The presets that controller holds, which is what a timer can point at.</summary>
    public required IReadOnlyList<PresetChoice> Presets { get; init; }

    public IReadOnlyList<TriggerChoice> Triggers => TriggerChoice.All;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _hour;
    [ObservableProperty] private int _minute;
    [ObservableProperty] private PresetChoice? _preset;
    [ObservableProperty] private TriggerChoice? _trigger = TriggerChoice.All[0];

    /// <summary>True when this entry hangs off the sun rather than the clock, so the time is moot.</summary>
    public bool IsClock => Trigger?.Sun is null or SunTrigger.None;

    partial void OnTriggerChanged(TriggerChoice? value) => OnPropertyChanged(nameof(IsClock));

    /// <summary>The entry as the controller stores it.</summary>
    public ScheduledChange ToChange() => new(
        Enabled: Enabled,
        Hour: IsClock ? System.Math.Clamp(Hour, 0, 23) : 0,
        Minute: IsClock ? System.Math.Clamp(Minute, 0, 59) : 0,
        PresetId: Preset?.Id ?? 0,
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

        foreach (PresetChoice preset in presets)
        {
            if (preset.Id == change.PresetId)
            {
                row.Preset = preset;
            }
        }

        return row;
    }
}
