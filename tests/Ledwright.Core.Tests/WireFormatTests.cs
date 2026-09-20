using System.Text.Json;
using Ledwright.Core.Json;
using Ledwright.Core.Layout;
using Ledwright.Core.Models;
using Xunit;

namespace Ledwright.Core.Tests;

/// <summary>
/// Contract tests for the WLED wire format. The sample documents here are trimmed captures from a
/// real Gledopto controller running 0.15.3, so a firmware change that breaks parsing shows up here
/// rather than in the field.
/// </summary>
public class WireFormatTests
{
    [Fact]
    public void A_patch_serialises_only_the_fields_it_sets()
    {
        var patch = new WledState { On = true, Brightness = 128 };

        string json = JsonSerializer.Serialize(patch, WledJson.Default.WledState);

        // This is the whole point of the nullable model: everything else is left alone on the device.
        Assert.Equal("""{"on":true,"bri":128}""", json);
    }

    [Fact]
    public void A_segment_patch_carries_its_id_so_it_cannot_clobber_the_wrong_segment()
    {
        WledState patch = WledState.ForSegment(1, s => s.PrimaryColor = new RgbColor(255, 0, 255));

        string json = JsonSerializer.Serialize(patch, WledJson.Default.WledState);

        Assert.Equal("""{"seg":[{"id":1,"col":[[255,0,255]]}]}""", json);
    }

    [Fact]
    public void Device_state_round_trips()
    {
        const string Sample = """
            {"on":true,"bri":248,"transition":40,"mainseg":0,
             "seg":[{"id":0,"start":0,"stop":308,"on":true,"bri":255,
                     "col":[[255,0,0],[0,0,0],[0,0,0]],"fx":74,"sx":128,"ix":128,"pal":0,
                     "c1":128,"c2":128,"c3":16,"set":0,"m12":0}]}
            """;

        WledState? state = JsonSerializer.Deserialize(Sample, WledJson.Default.WledState);

        Assert.NotNull(state);
        Assert.True(state.On);
        Assert.Equal((byte?)248, state.Brightness);

        WledSegment segment = Assert.Single(state.Segments!);
        Assert.Equal(308, segment.Stop);
        Assert.Equal(74, segment.Effect);
        Assert.Equal((byte?)16, segment.Custom3);
        Assert.Equal(new RgbColor(255, 0, 0), segment.PrimaryColor);
    }

    [Fact]
    public void Info_exposes_a_stable_device_key_and_mdns_name()
    {
        const string Sample = """
            {"ver":"0.15.3","vid":2508020,"name":"WLED-Gledopto","arch":"esp32",
             "leds":{"count":356,"maxseg":32},"ws":0,"mac":"20e7c86a1be8",
             "fs":{"u":49,"t":983}}
            """;

        WledInfo? info = JsonSerializer.Deserialize(Sample, WledJson.Default.WledInfo);

        Assert.NotNull(info);

        // Two controllers out of the same box share a friendly name; the MAC is what distinguishes them.
        Assert.Equal("20e7c86a1be8", info.DeviceKey);
        Assert.Equal("wled-6a1be8.local", info.MdnsHostName);
        Assert.Equal(934, info.FileSystem!.FreeKb);
    }

    [Fact]
    public void Capabilities_are_read_from_the_device_rather_than_assumed()
    {
        var info = new WledInfo
        {
            WebSocketClients = 0,
            Leds = new WledLedInfo { Count = 356, MaxSegments = 32, Rgbw = false },
            FileSystem = new WledFileSystemInfo { UsedKb = 49, TotalKb = 983 },
        };

        WledCapabilities capabilities = WledCapabilities.From(info);

        Assert.True(capabilities.HasWebSocket);
        Assert.True(capabilities.CanStoreProjectOnDevice);
        Assert.True(capabilities.CanStorePhotoOnDevice);
        Assert.Equal(32, capabilities.SegmentLimit);
        Assert.False(capabilities.IsRgbw);
    }

    [Fact]
    public void A_build_with_the_websocket_disabled_reports_it()
    {
        var info = new WledInfo { WebSocketClients = -1 };

        Assert.False(WledCapabilities.From(info).HasWebSocket);
    }

    [Fact]
    public void Preset_padding_segments_are_not_mistaken_for_real_bounds()
    {
        // WLED pads every preset out to the segment limit with {"stop":0}. Counting those as real
        // would make every preset on a 32-segment controller look broken.
        var preset = new WledPreset
        {
            Id = 2,
            Name = "Twinkle both",
            Segments =
            [
                new WledSegment { Id = 0, Start = 0, Stop = 308, Colors = [[255, 0, 0]] },
                new WledSegment { Id = 1, Start = 308, Stop = 356, Colors = [[255, 0, 255]] },
                new WledSegment { Stop = 0 },
                new WledSegment { Stop = 0 },
            ],
        };

        Assert.Empty(PresetAudit.FindGaps([preset], deviceLedCount: 356));
    }

    [Fact]
    public void A_preset_saved_against_a_shorter_strip_is_reported()
    {
        var preset = new WledPreset
        {
            Id = 1,
            Name = "All off",
            Segments = [new WledSegment { Id = 0, Start = 0, Stop = 257, Colors = [[0, 0, 0]] }],
        };

        PresetGap gap = Assert.Single(PresetAudit.FindGaps([preset], deviceLedCount: 356));

        Assert.Equal(257, gap.HighestLedCovered);
        Assert.Equal(99, gap.DarkLeds);
    }

    [Theory]
    [InlineData("#ff6600", 255, 102, 0, 0)]
    [InlineData("ff6600", 255, 102, 0, 0)]
    [InlineData("#FF660080", 255, 102, 0, 128)]
    public void Colors_parse_from_hex_in_the_forms_people_actually_type(
        string hex, byte r, byte g, byte b, byte w)
    {
        RgbColor color = RgbColor.Parse(hex);

        Assert.Equal(new RgbColor(r, g, b, w), color);
    }

    [Fact]
    public void Setting_one_colour_slot_leaves_the_others_intact()
    {
        var segment = new WledSegment { Colors = [[1, 2, 3], [4, 5, 6], [7, 8, 9]] };

        segment.SetColorSlot(1, new RgbColor(255, 255, 255));

        Assert.Equal([1, 2, 3], segment.Colors![0]);
        Assert.Equal([255, 255, 255], segment.Colors[1]);
        Assert.Equal([7, 8, 9], segment.Colors[2]);
    }
}
