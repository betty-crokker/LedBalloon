using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
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
    /// <summary>
    /// What the line reads as, which is not the same as what it is matched by.
    /// </summary>
    /// <remarks>
    /// A row saying "At sunset | Fall" leaves the verb to the reader, and the two halves then look
    /// like a pair of settings rather than a sentence. "Show Fall" says what happens. The name
    /// underneath is untouched, because it is what the controllers agree on.
    /// </remarks>
    public string Label { get; init; } = $"Show {Name}";

    /// <summary>
    /// A band of what the scene puts on the house, for telling one name from another.
    /// </summary>
    /// <remarks>
    /// Null for the switch, which is not a look and has nothing to draw - and for a scene whose
    /// colours are not known here, which is better than a grey box implying it has none.
    /// </remarks>
    public IBrush? Swatch { get; init; }

    public bool HasSwatch => Swatch is not null;

    public override string ToString() => Label;

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
        new("Turn the lights off", false) { Label = "Turn the lights off" },
        new("Turn the lights on (whatever was showing last)", true)
        {
            Label = "Turn the lights on (whatever was showing last)",
        },
    ];

    /// <summary>The switch and then the scenes, which is the order they are worth reading in.</summary>
    public static IReadOnlyList<PresetChoice> Offered(IEnumerable<(string Name, IBrush? Swatch)> scenes) =>
    [
        .. Switches,
        .. scenes.Select(scene => new PresetChoice(scene.Name) { Swatch = scene.Swatch }),
    ];
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

    /// <summary>Which days of the week it fires on, as WLED's bitmask: bit 0 is Monday.</summary>
    [ObservableProperty] private int _daysOfWeek = 0x7F;

    [ObservableProperty] private int _startMonth = 1;
    [ObservableProperty] private int _startDay = 1;
    [ObservableProperty] private int _endMonth = 12;
    [ObservableProperty] private int _endDay = 31;

    /// <summary>True when this line is narrower than every day of every year.</summary>
    public bool IsLimited => (DaysOfWeek & 0x7F) != 0x7F || !IsAllYear;

    public bool IsAllYear => StartMonth == 1 && StartDay == 1 && EndMonth == 12 && EndDay == 31;

    /// <summary>
    /// The narrowing, in words, for the line to carry beside itself.
    /// </summary>
    /// <remarks>
    /// Said on the row rather than only behind the button, because a timer that quietly does not
    /// fire on a Tuesday is the kind of thing nobody thinks to go and check.
    /// </remarks>
    public string Limits
    {
        get
        {
            var said = new List<string>();

            if ((DaysOfWeek & 0x7F) != 0x7F)
            {
                string[] names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
                said.Add(string.Join(" ", Enumerable.Range(0, 7)
                    .Where(day => (DaysOfWeek & (1 << day)) != 0)
                    .Select(day => names[day])));
            }

            if (!IsAllYear)
            {
                string[] months =
                [
                    "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                    "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
                ];

                said.Add($"{months[StartMonth - 1]} {StartDay} to {months[EndMonth - 1]} {EndDay}");
            }

            return said.Count == 0 ? string.Empty : string.Join("  ·  ", said);
        }
    }

    /// <summary>The days as seven properties, because that is what seven tick boxes bind to.</summary>
    /// <remarks>
    /// Bit 0 is Monday, which is WLED's order and not the one a C# DayOfWeek would suggest. Written
    /// out rather than generated: seven named properties are longer than a loop and are the thing
    /// the window actually says, which makes a mis-wired box a mis-wired box rather than an
    /// off-by-one in a bitmask nobody reads.
    /// </remarks>
    public bool Monday
    {
        get => Day(0);
        set => SetDay(0, value);
    }

    public bool Tuesday
    {
        get => Day(1);
        set => SetDay(1, value);
    }

    public bool Wednesday
    {
        get => Day(2);
        set => SetDay(2, value);
    }

    public bool Thursday
    {
        get => Day(3);
        set => SetDay(3, value);
    }

    public bool Friday
    {
        get => Day(4);
        set => SetDay(4, value);
    }

    public bool Saturday
    {
        get => Day(5);
        set => SetDay(5, value);
    }

    public bool Sunday
    {
        get => Day(6);
        set => SetDay(6, value);
    }

    /// <summary>True when every day has been unticked, which is a timer that can never fire.</summary>
    public bool NoDays => (DaysOfWeek & 0x7F) == 0;

    /// <summary>The months, for the two pickers.</summary>
    public static IReadOnlyList<string> Months { get; } =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    /// <summary>The pickers count from zero; the controller counts from one.</summary>
    public int StartMonthIndex
    {
        get => StartMonth - 1;
        set => StartMonth = value + 1;
    }

    public int EndMonthIndex
    {
        get => EndMonth - 1;
        set => EndMonth = value + 1;
    }

    /// <summary>
    /// True when the range runs backwards through the new year, which WLED allows and means.
    /// </summary>
    public bool WrapsTheYear =>
        EndMonth < StartMonth || (EndMonth == StartMonth && EndDay < StartDay);

    /// <summary>Back to no limits at all.</summary>
    public void EveryDayAllYear()
    {
        DaysOfWeek = 0x7F;
        StartMonth = 1;
        StartDay = 1;
        EndMonth = 12;
        EndDay = 31;

        foreach (string named in DayNames)
        {
            OnPropertyChanged(named);
        }

        OnPropertyChanged(nameof(StartMonthIndex));
        OnPropertyChanged(nameof(EndMonthIndex));
    }

    private static readonly string[] DayNames =
    [
        nameof(Monday), nameof(Tuesday), nameof(Wednesday), nameof(Thursday),
        nameof(Friday), nameof(Saturday), nameof(Sunday),
    ];

    private bool Day(int bit) => (DaysOfWeek & (1 << bit)) != 0;

    private void SetDay(int bit, bool on)
    {
        int next = on ? DaysOfWeek | (1 << bit) : DaysOfWeek & ~(1 << bit);

        if (next == DaysOfWeek)
        {
            return;
        }

        DaysOfWeek = next & 0x7F;
        OnPropertyChanged(DayNames[bit]);
    }

    partial void OnDaysOfWeekChanged(int value)
    {
        OnPropertyChanged(nameof(NoDays));
        LimitsChanged();
    }

    partial void OnStartMonthChanged(int value)
    {
        OnPropertyChanged(nameof(StartMonthIndex));
        OnPropertyChanged(nameof(WrapsTheYear));
        LimitsChanged();
    }

    partial void OnStartDayChanged(int value)
    {
        OnPropertyChanged(nameof(WrapsTheYear));
        LimitsChanged();
    }

    partial void OnEndMonthChanged(int value)
    {
        OnPropertyChanged(nameof(EndMonthIndex));
        OnPropertyChanged(nameof(WrapsTheYear));
        LimitsChanged();
    }

    partial void OnEndDayChanged(int value)
    {
        OnPropertyChanged(nameof(WrapsTheYear));
        LimitsChanged();
    }

    private void LimitsChanged()
    {
        OnPropertyChanged(nameof(IsLimited));
        OnPropertyChanged(nameof(IsAllYear));
        OnPropertyChanged(nameof(Limits));
    }

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
        DaysOfWeek: DaysOfWeek & 0x7F,
        Sun: Trigger?.Sun ?? SunTrigger.None,
        StartMonth: StartMonth,
        StartDay: StartDay,
        EndMonth: EndMonth,
        EndDay: EndDay);

    /// <summary>
    /// What makes two controllers' copies of a line the same line.
    /// </summary>
    /// <remarks>
    /// Everything a person set, and nothing about where it was read from. Lines that match on this
    /// are folded into one; lines that do not are left as they are, because two boxes disagreeing
    /// about when the house goes dark is worth seeing rather than quietly resolving.
    /// </remarks>
    public (string? Target, SunTrigger Sun, int Hour, int Minute, bool Enabled, int Days,
        int StartMonth, int StartDay, int EndMonth, int EndDay) Shape =>
        (Preset?.Name, Trigger?.Sun ?? SunTrigger.None, Hour, Minute, Enabled, DaysOfWeek & 0x7F,
         StartMonth, StartDay, EndMonth, EndDay);

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
            DaysOfWeek = change.DaysOfWeek & 0x7F,
            StartMonth = change.StartMonth,
            StartDay = change.StartDay,
            EndMonth = change.EndMonth,
            EndDay = change.EndDay,
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
