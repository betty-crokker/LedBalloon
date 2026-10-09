using System.Text;
using LedBalloon.App.ViewModels;
using LedBalloon.Core;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A timer narrowed to some days and some dates, written out and read back.
/// <para>
/// The round trip is the whole point: the app wrote the full year into every timer and never read
/// either limit, so anything set in WLED's own pages survived exactly until the next save.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class AtimerRuleSurvivesTheRoundTripTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-rule-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    [Fact]
    public void The_days_and_dates_come_back_as_they_were_written() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        ScheduleRow row = Assert.Single(app.Schedule);

        Assert.Equal("Fall", row.Preset?.Name);
        Assert.Equal(0b0011111, row.DaysOfWeek);
        Assert.Equal(12, row.StartMonth);
        Assert.Equal(6, row.EndDay);
    });

    /// <summary>
    /// And the row says so on its own line, since nobody goes looking for a reason a timer did not
    /// fire on a Saturday.
    /// </summary>
    [Fact]
    public void The_row_says_what_narrows_it() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        ScheduleRow row = Assert.Single(app.Schedule);

        Assert.True(row.IsLimited);
        Assert.Contains("Mon Tue Wed Thu Fri", row.Limits, StringComparison.Ordinal);
        Assert.Contains("Dec 1 to Jan 6", row.Limits, StringComparison.Ordinal);
    });

    [Fact]
    public void The_seven_tick_boxes_match_the_bitmask() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        ScheduleRow row = Assert.Single(app.Schedule);

        Assert.True(row.Monday);
        Assert.True(row.Friday);
        Assert.False(row.Saturday);
        Assert.False(row.Sunday);

        row.Saturday = true;

        Assert.Equal(0b0111111, row.DaysOfWeek);
    });

    [Fact]
    public void Clearing_it_puts_back_every_day_of_every_year() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        ScheduleRow row = Assert.Single(app.Schedule);
        row.EveryDayAllYear();

        Assert.False(row.IsLimited);
        Assert.Equal(string.Empty, row.Limits);
        Assert.True(row.Sunday);
    });

    /// <summary>
    /// A range that ends before it starts runs across the new year, which is both what WLED does
    /// and what anybody setting one for Christmas meant.
    /// </summary>
    [Fact]
    public void Arange_that_ends_before_it_starts_is_recognised() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        Assert.True(Assert.Single(app.Schedule).WrapsTheYear);
    });

    private async Task<MainViewModel> HouseAsync()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);
        _open.Add(controller);

        controller.Files["presets.json"] = Encoding.UTF8.GetBytes(
            """
            {
              "0": {},
              "1": { "n": "Fall", "on": true, "bri": 255,
                     "seg": [ { "id": 0, "start": 0, "stop": 5, "on": true, "fx": 39, "pal": 68 } ] }
            }
            """);

        // Weekdays only, from the first of December to Twelfth Night.
        controller.Configuration =
            """
            { "if": { "ntp": { "en": true, "lt": 40.0, "ln": -105.0, "tz": 6 } },
              "timers": { "ins": [
                { "en": 1, "hour": 17, "min": 30, "macro": 1, "dow": 31,
                  "start": { "mon": 12, "day": 1 }, "end": { "mon": 1, "day": 6 } } ] } }
            """;

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        await app.AddDeviceAsync(controller.Host, null);
        await app.LoadScheduleAsync();

        return app;
    }

    public void Dispose()
    {
        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }

        File.Delete(_preferences);
        GC.SuppressFinalize(this);
    }
}
