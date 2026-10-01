using System.Text.Json;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Whether the sound-reactive effects have anything to react to.
/// <para>
/// Two dozen of the effects a sound-reactive build lists read a microphone or a UDP audio feed, and
/// they sit in the picker beside every other effect. Picking one on a controller with no sound
/// leaves the run exactly as dark as the moment before, which reads as the app failing to send.
/// </para>
/// <para>
/// The usermod block these come from is not a schema. Its first entry is an HTML button, its values
/// are loose arrays of strings and numbers, and the one fact worth having - is anything coming in -
/// is the word "quiet" appended to a line naming the source.
/// </para>
/// </summary>
public class WhatTheControllerHearsTests
{
    /// <summary>Verbatim from 192.168.0.131, a GL-C-616WL on 0.15.3 with nothing wired to its I2S pins.</summary>
    private const string Gledopto = """
    {
      "AudioReactive": ["<button class=\"btn btn-xs\" onclick=\"requestJson({AudioReactive:{enabled:false}});\"><i class=\"icons on\">&#xe08f;</i></button>"],
      "GEQ Input Level": ["<div class=\"slider\"></div>"],
      "Audio Source": ["I2S digital", " - quiet"],
      "Sound Processing": ["running"],
      "AGC Gain": [9.68, "x"],
      "UDP Sound Sync": ["off"]
    }
    """;

    private static SoundInput Read(string json) =>
        SoundInput.From(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json));

    [Fact]
    public void A_box_with_nothing_wired_to_its_microphone_pins_hears_nothing()
    {
        SoundInput sound = Read(Gledopto);

        // The usermod is enabled and the driver is running - it will happily report a source and a
        // gain forever. The only thing that says there is no microphone is that it has been quiet,
        // which is why that word is what gets read.
        Assert.Equal("I2S digital", sound.Source);
        Assert.True(sound.Silent);
        Assert.False(sound.Hearing);
    }

    [Fact]
    public void One_with_sound_reaching_it_hears()
    {
        SoundInput sound = Read("""
        {
          "AudioReactive": ["<button></button>"],
          "Audio Source": ["I2S digital"],
          "UDP Sound Sync": ["off"]
        }
        """);

        Assert.True(sound.Hearing);
    }

    [Fact]
    public void A_quiet_room_and_a_missing_microphone_are_the_same_answer()
    {
        // Not a distinction worth drawing, and not one that can be drawn from here: an effect that
        // follows sound does nothing in either case, and saying so is right in either case.
        Assert.False(Read(Gledopto).Hearing);
    }

    [Fact]
    public void A_box_listening_to_another_one_hears_whatever_that_one_does()
    {
        // No microphone of its own, and it does not need one. This is the setting that makes hiding
        // these effects the wrong move rather than the kind one: it can be turned on at any time.
        SoundInput sound = Read("""
        {
          "AudioReactive": ["<button></button>"],
          "Audio Source": ["not initialized"],
          "UDP Sound Sync": ["receive"]
        }
        """);

        Assert.True(sound.Hearing);
    }

    [Fact]
    public void And_one_with_a_dead_microphone_and_no_feed_does_not()
    {
        SoundInput sound = Read("""
        {
          "AudioReactive": ["<button></button>"],
          "Audio Source": ["not initialized"],
          "UDP Sound Sync": ["off"]
        }
        """);

        Assert.False(sound.Hearing);
        Assert.Null(sound.Source);
    }

    [Fact]
    public void A_build_without_the_usermod_says_nothing_about_anything()
    {
        // It has no sound-reactive effects to say it about either, so the honest answer is the same
        // one as "could not tell".
        Assert.False(Read("""{"Uptime":["4 days"]}""").Hearing);
        Assert.False(SoundInput.From(null).Hearing);
    }

    [Fact]
    public void The_whole_info_document_carries_it()
    {
        WledInfo? info = JsonSerializer.Deserialize<WledInfo>($$"""
        {"name":"South","ver":"0.15.3","u":{{Gledopto}}}
        """);

        Assert.NotNull(info);
        Assert.False(info.Sound.Hearing);
    }
}
