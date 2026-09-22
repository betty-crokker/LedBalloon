using System.Text.Json;
using LedBalloon.Core;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Reading the timetable a controller keeps for itself, from the shape the real hardware stores it
/// in - this is the north controller's own <c>timers</c> block, copied verbatim.
/// </summary>
public class WledScheduleTests
{
    private const string RealConfig =
        """
        {
          "timers": {
            "cntdwn": { "goal": [20, 1, 1, 0, 0, 0], "macro": 0 },
            "ins": [
              { "en": 1, "hour": 19, "min": 30, "macro": 9, "dow": 127,
                "start": { "mon": 1, "day": 1 }, "end": { "mon": 12, "day": 31 } },
              { "en": 1, "hour": 23, "min": 30, "macro": 3, "dow": 127,
                "start": { "mon": 1, "day": 1 }, "end": { "mon": 12, "day": 31 } },
              { "en": 1, "hour": 255, "min": 0, "macro": 1, "dow": 127 },
              { "en": 0, "hour": 255, "min": 0, "macro": 1, "dow": 127 }
            ]
          }
        }
        """;

    private static IReadOnlyList<ScheduledChange> Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return WledSchedule.Parse(document.RootElement);
    }

    private static string? NameOf(int preset) => preset switch
    {
        1 => "All off",
        3 => "Stairs white",
        9 => "Colorful Match Light Bulb Burst",
        _ => null,
    };

    [Fact]
    public void The_houses_own_timetable_comes_back_in_order()
    {
        IReadOnlyList<ScheduledChange> schedule = Read(RealConfig);

        Assert.Equal(4, schedule.Count);
        Assert.Equal(19, schedule[0].Hour);
        Assert.Equal(30, schedule[0].Minute);
        Assert.Equal(9, schedule[0].PresetId);
        Assert.True(schedule[0].EveryDay);
    }

    [Fact]
    public void An_hour_of_255_is_the_sun_and_the_first_one_is_sunrise()
    {
        // The position in the list is the only thing that says which end of the day it is: WLED
        // files the first such entry as sunrise and the second as sunset.
        IReadOnlyList<ScheduledChange> schedule = Read(RealConfig);

        Assert.Equal(SunTrigger.None, schedule[0].Sun);
        Assert.Equal(SunTrigger.None, schedule[1].Sun);
        Assert.Equal(SunTrigger.Sunrise, schedule[2].Sun);
        Assert.Equal(SunTrigger.Sunset, schedule[3].Sun);
    }

    [Fact]
    public void A_disabled_entry_is_not_something_that_will_happen()
    {
        IReadOnlyList<ScheduledChange> active = WledSchedule.Active(Read(RealConfig));

        // The sunset slot is switched off on this controller.
        Assert.Equal(3, active.Count);
        Assert.DoesNotContain(active, entry => entry.Sun == SunTrigger.Sunset);
    }

    [Fact]
    public void The_timetable_reads_as_a_sentence_with_preset_names_in_it()
    {
        string said = WledSchedule.Describe(Read(RealConfig), NameOf);

        Assert.Contains("Colorful Match Light Bulb Burst at 19:30", said);
        Assert.Contains("Stairs white at 23:30", said);
        Assert.Contains("All off at sunrise", said);
        Assert.DoesNotContain("sunset", said);
    }

    [Fact]
    public void This_is_what_turned_the_lights_off_during_a_capture()
    {
        // Frames captured off the strip came back black part way through, which looked like a bug
        // in the capture and was the house doing what it was told.
        ScheduledChange dawn = WledSchedule.Active(Read(RealConfig))
            .Single(entry => entry.Sun == SunTrigger.Sunrise);

        Assert.Equal(1, dawn.PresetId);
        Assert.Equal("All off", NameOf(dawn.PresetId));
        Assert.Equal("at sunrise", dawn.When);
    }

    [Fact]
    public void A_timer_pointing_at_no_preset_is_an_empty_slot()
    {
        IReadOnlyList<ScheduledChange> schedule = Read(
            """{"timers":{"ins":[{"en":1,"hour":8,"min":0,"macro":0,"dow":127}]}}""");

        Assert.Empty(schedule);
    }

    [Fact]
    public void A_controller_with_no_timetable_says_nothing()
    {
        Assert.Empty(Read("""{"hw":{}}"""));
        Assert.Equal(string.Empty, WledSchedule.Describe([], NameOf));
    }
}

/// <summary>
/// Whether one controller would copy another. Both of the development controllers are set to
/// receive on group 1 with sending switched off, so nothing currently propagates - but receiving
/// is wide open, and only that send toggle stands between this app and having its per-controller
/// writes overwritten by whichever box was written to last.
/// </summary>
public class WledSyncTopologyTests
{
    private static WledUdpSync Box(bool send, bool receive, int group = 1) => new()
    {
        Send = send,
        Receive = receive,
        SendGroups = group,
        ReceiveGroups = group,
    };

    [Fact]
    public void Nothing_propagates_while_sending_is_switched_off()
    {
        // The development hardware as found: receive on, send off, both boxes.
        WledUdpSync north = Box(send: false, receive: true);
        WledUdpSync south = Box(send: false, receive: true);

        Assert.False(WledSyncTopology.Reaches(north, south));
        Assert.False(WledSyncTopology.AnyCopyEachOther([north, south]));
    }

    [Fact]
    public void Turning_sending_on_is_all_it_would_take()
    {
        WledUdpSync north = Box(send: true, receive: true);
        WledUdpSync south = Box(send: false, receive: true);

        Assert.True(WledSyncTopology.Reaches(north, south));
        Assert.True(WledSyncTopology.AnyCopyEachOther([north, south]));
    }

    [Fact]
    public void Controllers_in_different_groups_ignore_each_other()
    {
        WledUdpSync north = Box(send: true, receive: true, group: 1);
        WledUdpSync south = Box(send: true, receive: true, group: 2);

        Assert.False(WledSyncTopology.Reaches(north, south));
    }

    [Fact]
    public void Groups_are_masks_so_overlapping_in_one_bit_is_enough()
    {
        WledUdpSync north = Box(send: true, receive: false, group: 0b0110);
        WledUdpSync south = Box(send: false, receive: true, group: 0b0100);

        Assert.True(WledSyncTopology.Reaches(north, south));
    }

    [Fact]
    public void A_controller_that_has_not_reported_yet_is_not_assumed_to_be_listening()
    {
        Assert.False(WledSyncTopology.Reaches(null, Box(send: false, receive: true)));
        Assert.False(WledSyncTopology.Reaches(Box(send: true, receive: false), null));
    }
}
