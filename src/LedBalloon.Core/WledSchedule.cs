using System.Text.Json;

namespace LedBalloon.Core;

/// <summary>Which end of the day a sun-based timer hangs off.</summary>
public enum SunTrigger
{
    None,
    Sunrise,
    Sunset,
}

/// <summary>
/// One entry in a controller's own timetable: a preset it applies by itself, at a time nobody is
/// present for.
/// </summary>
public sealed record ScheduledChange(
    bool Enabled,
    int Hour,
    int Minute,
    int PresetId,
    int DaysOfWeek,
    SunTrigger Sun)
{
    /// <summary>All seven days, which is how these are almost always set.</summary>
    public bool EveryDay => (DaysOfWeek & 0x7F) == 0x7F;

    /// <summary>When it fires, in words. Preset names are resolved by the caller.</summary>
    public string When => Sun switch
    {
        SunTrigger.Sunrise => "at sunrise",
        SunTrigger.Sunset => "at sunset",
        _ => $"at {Hour:00}:{Minute:00}",
    };
}

/// <summary>
/// The timetable a controller keeps for itself, read from <c>cfg.json</c>.
/// <para>
/// This matters more than it looks. The whole design puts the house on the controllers so there is
/// nothing on the PC to lose - and a schedule is part of what they hold. It will change the lights
/// whether or not this app is running, whether or not anyone is looking, and whatever was picked
/// here five minutes earlier.
/// </para>
/// <para>
/// Found by accident: a run of frames captured off the strip came back entirely black, and the
/// cause turned out to be the controller applying "All off" at dawn, on its own, in the middle of
/// the capture. An app that does not know about that looks broken at exactly the moment it is not.
/// </para>
/// </summary>
public static class WledSchedule
{
    /// <summary>
    /// Reads the timetable out of a parsed <c>cfg.json</c>.
    /// <para>
    /// Sun-based entries are stored with an hour of 255, and WLED files the first of them as
    /// sunrise and the second as sunset - the position in the list is the only thing that says
    /// which, so the order is load-bearing.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ScheduledChange> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("timers", out JsonElement timers) ||
            !timers.TryGetProperty("ins", out JsonElement entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var found = new List<ScheduledChange>();
        int sunSeen = 0;

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            int hour = Read(entry, "hour");

            SunTrigger sun = SunTrigger.None;
            if (hour > 23)
            {
                sun = sunSeen == 0 ? SunTrigger.Sunrise : SunTrigger.Sunset;
                sunSeen++;
            }

            int preset = Read(entry, "macro");

            // A timer pointing at no preset is an empty slot, not an event.
            if (preset <= 0)
            {
                continue;
            }

            found.Add(new ScheduledChange(
                Enabled: Read(entry, "en") != 0,
                Hour: hour,
                Minute: Read(entry, "min"),
                PresetId: preset,
                DaysOfWeek: entry.TryGetProperty("dow", out JsonElement dow) && dow.TryGetInt32(out int days)
                    ? days
                    : 0x7F,
                Sun: sun));
        }

        return found;
    }

    /// <summary>The entries that will actually fire, which is the only kind worth mentioning.</summary>
    public static IReadOnlyList<ScheduledChange> Active(IReadOnlyList<ScheduledChange> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return [.. schedule.Where(entry => entry.Enabled)];
    }

    /// <summary>
    /// The timetable in a sentence, with preset numbers turned into names by
    /// <paramref name="nameOf"/>.
    /// </summary>
    public static string Describe(
        IReadOnlyList<ScheduledChange> schedule,
        Func<int, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(nameOf);

        IReadOnlyList<ScheduledChange> active = Active(schedule);

        if (active.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> parts = active
            .OrderBy(entry => entry.Sun == SunTrigger.None ? 0 : 1)
            .ThenBy(entry => entry.Hour)
            .ThenBy(entry => entry.Minute)
            .Select(entry => $"{nameOf(entry.PresetId) ?? $"preset {entry.PresetId}"} {entry.When}");

        return string.Join(", ", parts);
    }

    private static int Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : 0;
}
