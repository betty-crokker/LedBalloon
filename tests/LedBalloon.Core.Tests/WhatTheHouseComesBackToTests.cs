using System.Text.Json;
using System.Text.Json.Nodes;
using LedBalloon.Core;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The boot state, against a real <c>cfg.json</c> from a GL-C-616WL on 0.15.3.
/// <para>
/// This is the setting with the longest delay between writing it and finding out. Every other thing
/// this app writes is seen immediately; what the controller comes back to is read once, by the
/// controller, at the end of a power cut that nobody chose the timing of. So the test that matters
/// most here is not that the value lands - it is that writing it leaves the rest of the document
/// exactly as it was, because a boot state posted carelessly would take the frame rate with it.
/// </para>
/// </summary>
public class WhatTheHouseComesBackToTests
{
    private static JsonObject Config() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("Fixtures", "wled-0.15.3-two-outputs.json")))!;

    private static BootState? Read(JsonObject configuration) =>
        WledBootState.Parse(JsonDocument.Parse(configuration.ToJsonString()).RootElement);

    [Fact]
    public void The_fixture_comes_back_to_its_first_preset()
    {
        BootState? state = Read(Config());

        Assert.NotNull(state);
        Assert.True(state.LightsUp);
        Assert.Equal(128, state.Brightness);
        Assert.Equal(1, state.PresetId);
        Assert.True(state.FromPreset);
    }

    /// <summary>
    /// The trap the whole <c>def</c> round trip exists to avoid, the same one
    /// <see cref="LedOutputWriterTests"/> guards: a post without <c>hw.led.fps</c> is read as zero
    /// and uncaps the frame rate.
    /// </summary>
    [Fact]
    public void Everything_it_was_not_asked_about_is_left_exactly_as_it_was()
    {
        JsonObject config = Config();

        Assert.True(WledBootState.Apply(config, new BootState(LightsUp: false, Brightness: 64)));

        Assert.Equal(42, config["hw"]!["led"]!["fps"]!.GetValue<int>());
        Assert.Equal(310, config["hw"]!["led"]!["total"]!.GetValue<int>());
        Assert.Equal(25, config["hw"]!["led"]!["ins"]![0]!["len"]!.GetValue<int>());
        Assert.Equal(285, config["hw"]!["led"]!["ins"]![1]!["len"]!.GetValue<int>());
        Assert.Equal(18, config["hw"]!["relay"]!["pin"]!.GetValue<int>());
        Assert.NotNull(config["timers"]);
        Assert.NotNull(config["if"]);
        Assert.NotNull(config["light"]);
    }

    [Fact]
    public void Coming_back_dark_is_sayable_and_is_not_the_same_as_no_preset()
    {
        JsonObject config = Config();

        WledBootState.Apply(config, new BootState(LightsUp: false, Brightness: 128, PresetId: 1));

        BootState? state = Read(config);

        // The preset is still named. A controller told to come up dark and then handed a preset
        // that was saved while the lights were on comes up lit, and the honest thing is to carry
        // both rather than to quietly drop one of them here.
        Assert.NotNull(state);
        Assert.False(state.LightsUp);
        Assert.Equal(1, state.PresetId);
    }

    [Fact]
    public void Pointing_it_at_nothing_clears_the_boot_preset()
    {
        JsonObject config = Config();

        Assert.True(WledBootState.Apply(
            config, new BootState(LightsUp: true, Brightness: 200, PresetId: BootState.NoPreset)));

        Assert.Equal(0, config["def"]!["ps"]!.GetValue<int>());
        Assert.Equal(200, config["def"]!["bri"]!.GetValue<int>());
        Assert.False(Read(config)!.FromPreset);
    }

    [Fact]
    public void Asking_for_what_it_already_has_changes_nothing_and_writes_nothing()
    {
        JsonObject config = Config();

        Assert.False(WledBootState.Apply(
            config, new BootState(LightsUp: true, Brightness: 128, PresetId: 1)));
    }

    /// <summary>
    /// A controller whose configuration has no <c>def</c> at all still has to be writable: the
    /// section is where this app puts the setting, not something it can only edit in place.
    /// </summary>
    [Fact]
    public void Acontroller_that_has_never_been_told_can_still_be_told()
    {
        var bare = (JsonObject)JsonNode.Parse("""{"hw":{"led":{"fps":42}}}""")!;

        Assert.Null(WledBootState.Parse(JsonDocument.Parse(bare.ToJsonString()).RootElement));
        Assert.True(WledBootState.Apply(bare, new BootState(LightsUp: true, Brightness: 96, PresetId: 3)));

        BootState? state = Read(bare);

        Assert.NotNull(state);
        Assert.Equal(3, state.PresetId);
        Assert.Equal(96, state.Brightness);
        Assert.Equal(42, bare["hw"]!["led"]!["fps"]!.GetValue<int>());
    }

    /// <summary>
    /// "It did not say" and "it comes back dark" are different answers, and the second one is worth
    /// showing somebody as what their house will do.
    /// </summary>
    [Fact]
    public void Aconfiguration_with_nothing_to_read_reads_as_nothing_rather_than_as_dark()
    {
        Assert.Null(WledBootState.Parse(JsonDocument.Parse("""{"hw":{}}""").RootElement));
        Assert.Null(WledBootState.Parse(JsonDocument.Parse("""{"def":4}""").RootElement));
        Assert.Null(WledBootState.Parse(JsonDocument.Parse("[]").RootElement));
    }

    [Fact]
    public void Abrightness_or_aslot_out_of_range_is_pulled_back_in()
    {
        JsonObject config = Config();

        WledBootState.Apply(config, new BootState(LightsUp: true, Brightness: 4000, PresetId: 9000));

        Assert.Equal(255, config["def"]!["bri"]!.GetValue<int>());
        Assert.Equal(BootState.HighestSlot, config["def"]!["ps"]!.GetValue<int>());

        WledBootState.Apply(config, new BootState(LightsUp: true, Brightness: -5, PresetId: -1));

        Assert.Equal(0, config["def"]!["bri"]!.GetValue<int>());
        Assert.Equal(BootState.NoPreset, config["def"]!["ps"]!.GetValue<int>());
    }

    /// <summary>
    /// The sentence a reader gets. A slot this app has never written still has to describe as
    /// something they can go and look at, because a preset saved from WLED's own pages is just as
    /// much what the house will come back to.
    /// </summary>
    [Fact]
    public void It_says_what_apower_cut_would_leave_behind()
    {
        Assert.Equal(
            "comes back to Christmas",
            new BootState(true, 128, 2).Describe(slot => slot == 2 ? "Christmas" : null));

        Assert.Equal(
            "comes back to preset 7",
            new BootState(true, 128, 7).Describe(_ => null));

        Assert.Equal(
            "comes back lit at 200, however it was last set up",
            new BootState(true, 200).Describe(_ => "ignored"));

        Assert.Equal("comes back dark", new BootState(false, 128).Describe(_ => "ignored"));
    }
}
