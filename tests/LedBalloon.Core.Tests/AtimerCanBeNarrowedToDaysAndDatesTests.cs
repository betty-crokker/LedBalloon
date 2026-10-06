using System.Text.Json;
using LedBalloon.Core;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The days a timer is allowed to fire on, and the dates it runs between.
/// <para>
/// The controller has kept both all along. The app wrote the whole year into every timer and never
/// read either back, so a rule set in WLED's own pages was silently flattened by the next save —
/// and a timer for the fortnight either side of Christmas meant remembering to turn it off in
/// January.
/// </para>
/// </summary>
public class AtimerCanBeNarrowedToDaysAndDatesTests
{
    private static JsonElement Config(string timers) =>
        JsonDocument.Parse($$"""{ "timers": { "ins": [ {{timers}} ] } }""").RootElement;

    [Fact]
    public void Adate_range_is_read_back_off_the_controller()
    {
        ScheduledChange entry = Assert.Single(WledSchedule.Parse(Config(
            """
            { "en": 1, "hour": 17, "min": 30, "macro": 4, "dow": 127,
              "start": { "mon": 12, "day": 1 }, "end": { "mon": 1, "day": 6 } }
            """)));

        Assert.Equal(12, entry.StartMonth);
        Assert.Equal(1, entry.StartDay);
        Assert.Equal(1, entry.EndMonth);
        Assert.Equal(6, entry.EndDay);
        Assert.False(entry.AllYear);
    }

    [Fact]
    public void Days_of_the_week_are_read_back()
    {
        // Monday is bit 0, which is WLED's order: this is weekdays only.
        ScheduledChange entry = Assert.Single(WledSchedule.Parse(Config(
            """{ "en": 1, "hour": 23, "min": 0, "macro": 3, "dow": 31 }""")));

        Assert.Equal(31, entry.DaysOfWeek);
        Assert.False(entry.EveryDay);
    }

    /// <summary>
    /// A sun entry carries no start or end at all, and that means the whole year rather than a
    /// broken entry — which is what a zero would have been read as.
    /// </summary>
    [Fact]
    public void Amissing_range_is_the_whole_year()
    {
        ScheduledChange entry = Assert.Single(WledSchedule.Parse(Config(
            """{ "en": 1, "hour": 255, "min": 0, "macro": 1, "dow": 127 }""")));

        Assert.True(entry.AllYear);
        Assert.Equal(1, entry.StartMonth);
        Assert.Equal(31, entry.EndDay);
    }

    [Fact]
    public void Aplain_timer_is_every_day_all_year()
    {
        ScheduledChange entry = Assert.Single(WledSchedule.Parse(Config(
            """
            { "en": 1, "hour": 23, "min": 0, "macro": 3, "dow": 127,
              "start": { "mon": 1, "day": 1 }, "end": { "mon": 12, "day": 31 } }
            """)));

        Assert.True(entry.EveryDay);
        Assert.True(entry.AllYear);
    }
}
