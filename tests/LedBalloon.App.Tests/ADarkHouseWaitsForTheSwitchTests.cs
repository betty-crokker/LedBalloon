using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What happens to an edit made while the house is switched off.
/// <para>
/// It used to go straight to the controller, and the switch said so: "Changes reach the house, but
/// it is switched off". That is true and it is no use to anybody — where the bytes are sitting is
/// not a fact a person has any way to act on, and the house looked exactly the same either way. It
/// was also quietly wrong in one case: WLED reads a brightness as a request to come on, so sending
/// one to a dark controller lights it up, and the code had a special case saying "stay dark" bolted
/// to the side of it.
/// </para>
/// <para>
/// So the edit waits instead, and goes out with the power. Switching the house on brings up what
/// was asked for rather than what it was showing before — which is what "changes show when you
/// switch the house on" promises, and it arrives in the same request so there is nothing to see in
/// between.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ADarkHouseWaitsForTheSwitchTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-dark-tests-{Guid.NewGuid():N}.json");

    /// <summary>Long enough for the coalescer, which paces posts at about 40ms, several times over.</summary>
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(2);

    [Fact]
    public void An_edit_made_in_the_dark_is_not_sent() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        Assert.False(app.MasterOn, "the fake controller reports itself off");

        Brightness(app).Brightness = 200;

        // Deliberately waiting rather than asserting immediately: posts are paced, so "nothing yet"
        // a millisecond later would pass whatever the code did.
        Assert.False(
            await controller.WaitForPostAsync(TimeSpan.FromMilliseconds(400)),
            $"nothing should have gone to a dark controller, and this did: [{Posted(controller)}]");
    });

    [Fact]
    public void And_goes_out_with_the_power_when_the_house_is_switched_on() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        Brightness(app).Brightness = 200;
        await Task.Delay(200);

        app.MasterOn = true;

        Assert.True(
            await controller.WaitForPostAsync(LongEnough),
            "switching the house on should have sent something");

        // One request, carrying both. Two would light the house on what it was showing before and
        // then correct it, which is a flash of the wrong thing on every switch-on.
        string sent = controller.Posts[0];

        Assert.Contains("\"on\":true", sent);
        Assert.Contains("\"bri\":200", sent);
    });

    [Fact]
    public void A_house_that_is_already_on_is_not_made_to_wait() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink");
        MainViewModel app = await HouseAsync(controller);

        app.MasterOn = true;
        Assert.True(await controller.WaitForPostAsync(LongEnough));

        int before = controller.Posts.Count;
        Brightness(app).Brightness = 64;

        DateTime until = DateTime.UtcNow + LongEnough;
        while (DateTime.UtcNow < until && controller.Posts.Count == before)
        {
            await Task.Delay(20);
        }

        Assert.Contains("\"bri\":64", controller.Posts[^1]);
    });

    /// <summary>The switch says what will happen, not where the bytes are.</summary>
    [Fact]
    public void The_switch_promises_something_the_app_can_keep() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid");
        MainViewModel app = await HouseAsync(controller);

        Assert.Equal("Changes show when you switch the house on", app.SyncOnLabel);

        app.MasterOn = true;

        Assert.Equal("Changes show on the house as you make them", app.SyncOnLabel);
    });

    /// <summary>One controller with one run on it, connected, sync on, and its lights off.</summary>
    private async Task<MainViewModel> HouseAsync(FakeController controller)
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        // Before connecting, so the controller counts as one the layout uses by the time it reports.
        // The brightness card is drawn per active controller, and a controller is active because a
        // run names it.
        app.Project.Segments.Add(new Segment
        {
            Id = "porch",
            Name = "Porch",
            ControllerKey = FakeController.Key,
            Output = 1,
            Count = 5,
        });

        app.LiveSync = true;

        await app.AddDeviceAsync(controller.Host, null);

        return app;
    }

    private static ControllerSegments Brightness(MainViewModel app)
    {
        Assert.True(
            app.SceneDetails.Count > 0,
            "the panel should have a card for the controller to carry its brightness");

        return app.SceneDetails[0];
    }

    private static string Posted(FakeController controller) => string.Join(" | ", controller.Posts);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (File.Exists(_preferences))
        {
            File.Delete(_preferences);
        }
    }
}
