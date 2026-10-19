using System.Text.Json;
using LedBalloon.Core;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Writing the boot state to a controller, over HTTP, against a stand-in that keeps what it is
/// posted.
/// <para>
/// The boot state is the one setting nobody sees land. It is read once, by the controller, at the
/// end of a power cut - so the questions worth asking a test are whether the rest of the
/// configuration survived the write, and whether a write that did not take says so now rather than
/// in six months in the dark.
/// </para>
/// </summary>
public class WhatThehouseComesBackToIsWrittenWholeTests : IDisposable
{
    private readonly List<FakeController> _open = [];

    /// <summary>
    /// A configuration with the keys that have been lost to a careless post before now: the frame
    /// rate, the output lengths and the timetable.
    /// </summary>
    private const string AsItStands =
        """
        {"hw":{"led":{"fps":42,"total":310,"maxpwr":850,"ins":[{"start":0,"len":25},{"start":25,"len":285}]}},
         "def":{"ps":1,"on":true,"bri":128},
         "timers":{"cntdwn":{"en":false},"ins":[{"en":1,"hour":17,"min":30,"macro":2}]},
         "if":{"ntp":{"en":true,"tz":6}}}
        """;

    [Fact]
    public async Task The_whole_configuration_goes_back_with_only_the_boot_state_changed()
    {
        FakeController controller = Start();
        controller.BootPresetReported = 4;

        var client = new WledConfigClient(controller.Host);

        Assert.True(await client.SetBootStateAsync(new BootState(LightsUp: true, Brightness: 96, PresetId: 4)));

        JsonDocument posted = JsonDocument.Parse(controller.Configuration);
        JsonElement root = posted.RootElement;

        Assert.Equal(4, root.GetProperty("def").GetProperty("ps").GetInt32());
        Assert.Equal(96, root.GetProperty("def").GetProperty("bri").GetInt32());
        Assert.True(root.GetProperty("def").GetProperty("on").GetBoolean());

        // The frame rate above all. A document posted without it reads as zero and uncaps the rate,
        // which is how a controller here ended up rendering flat out at 96 for weeks.
        Assert.Equal(42, root.GetProperty("hw").GetProperty("led").GetProperty("fps").GetInt32());
        Assert.Equal(310, root.GetProperty("hw").GetProperty("led").GetProperty("total").GetInt32());
        Assert.Equal(850, root.GetProperty("hw").GetProperty("led").GetProperty("maxpwr").GetInt32());
        Assert.Equal(285, root.GetProperty("hw").GetProperty("led")
            .GetProperty("ins")[1].GetProperty("len").GetInt32());

        // And the timetable, which is the other thing on this document that runs the house alone.
        Assert.Equal(17, root.GetProperty("timers").GetProperty("ins")[0].GetProperty("hour").GetInt32());
        Assert.Equal(6, root.GetProperty("if").GetProperty("ntp").GetProperty("tz").GetInt32());
    }

    [Fact]
    public async Task What_was_written_is_what_reads_back()
    {
        FakeController controller = Start();
        controller.BootPresetReported = 0;

        var client = new WledConfigClient(controller.Host);

        await client.SetBootStateAsync(new BootState(LightsUp: false, Brightness: 64));

        BootState? read = await client.GetBootStateAsync();

        Assert.NotNull(read);
        Assert.False(read.LightsUp);
        Assert.Equal(64, read.Brightness);
        Assert.Equal(BootState.NoPreset, read.PresetId);
        Assert.False(read.FromPreset);
    }

    /// <summary>
    /// Flash has a finite number of writes in it, and a save that changed a colour has no business
    /// spending one on a boot state nobody touched.
    /// </summary>
    [Fact]
    public async Task Acontroller_that_already_comes_back_to_this_is_not_written_to()
    {
        FakeController controller = Start();

        var client = new WledConfigClient(controller.Host);

        Assert.False(await client.SetBootStateAsync(
            new BootState(LightsUp: true, Brightness: 128, PresetId: 1)));

        Assert.Empty(controller.Posts);
    }

    /// <summary>
    /// The failure this checks for is silent by design: a settings PIN makes the post answer 200 and
    /// change nothing at all.
    /// </summary>
    [Fact]
    public async Task Awrite_that_did_not_take_is_reported_now_rather_than_discovered_later()
    {
        FakeController controller = Start();

        // The controller goes on holding the slot it had, whatever it answered to the post.
        controller.BootPresetReported = 1;

        var client = new WledConfigClient(controller.Host);

        WledException blew = await Assert.ThrowsAsync<WledException>(
            () => client.SetBootStateAsync(new BootState(LightsUp: true, Brightness: 128, PresetId: 4)));

        Assert.Contains("did not take", blew.Message, StringComparison.Ordinal);
        Assert.Contains(controller.Host, blew.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A controller that does not report the slot has not said the write failed, and refusing a
    /// write that probably landed would be worse than taking the 200 at its word.
    /// </summary>
    [Fact]
    public async Task Acontroller_that_will_not_say_what_it_boots_to_is_taken_at_its_word()
    {
        FakeController controller = Start();

        // BootPresetReported left null, so /json/info is not answered at all.
        var client = new WledConfigClient(controller.Host);

        Assert.True(await client.SetBootStateAsync(
            new BootState(LightsUp: true, Brightness: 128, PresetId: 4)));
    }

    private FakeController Start()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, FakeController.Key);

        controller.Configuration = AsItStands;
        _open.Add(controller);

        return controller;
    }

    public void Dispose()
    {
        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
