using System.Text.Json.Serialization;

namespace LedBalloon.Core.Models;

/// <summary>Read-only device description from <c>GET /json/info</c>.</summary>
public sealed class WledInfo
{
    /// <summary>Friendly name set in WLED's settings, e.g. "Desk Strip".</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>Human-readable firmware version, e.g. "0.15.0".</summary>
    [JsonPropertyName("ver")] public string? Version { get; set; }

    /// <summary>Build number. Compare this rather than <see cref="Version"/> when gating on features.</summary>
    [JsonPropertyName("vid")] public long? BuildId { get; set; }

    [JsonPropertyName("leds")] public WledLedInfo? Leds { get; set; }

    [JsonPropertyName("udpport")] public int? UdpPort { get; set; }

    /// <summary>True while a realtime source is driving the strip.</summary>
    [JsonPropertyName("live")] public bool? Live { get; set; }

    /// <summary>Name of the realtime source currently in control.</summary>
    [JsonPropertyName("lm")] public string? LiveSource { get; set; }

    /// <summary>IP of the realtime source currently in control.</summary>
    [JsonPropertyName("lip")] public string? LiveSourceIp { get; set; }

    /// <summary>Connected WebSocket clients, or -1 when the WebSocket server is off.</summary>
    [JsonPropertyName("ws")] public int? WebSocketClients { get; set; }

    [JsonPropertyName("fxcount")] public int? EffectCount { get; set; }
    [JsonPropertyName("palcount")] public int? PaletteCount { get; set; }

    /// <summary>Firmware codename, e.g. "Kosen".</summary>
    [JsonPropertyName("cn")] public string? CodeName { get; set; }

    /// <summary>Build flavour, e.g. "ESP32_Ethernet".</summary>
    [JsonPropertyName("release")] public string? Release { get; set; }

    /// <summary>A long per-device identifier, present from 0.15 onwards.</summary>
    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }

    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("product")] public string? Product { get; set; }

    /// <summary>Custom palettes loaded from the filesystem, on top of the built-in ones.</summary>
    [JsonPropertyName("cpalcount")] public int? CustomPaletteCount { get; set; }

    /// <summary>Filesystem usage, which is what decides whether settings can live on the device.</summary>
    [JsonPropertyName("fs")] public WledFileSystemInfo? FileSystem { get; set; }

    [JsonPropertyName("arch")] public string? Architecture { get; set; }
    [JsonPropertyName("core")] public string? CoreVersion { get; set; }
    [JsonPropertyName("freeheap")] public long? FreeHeap { get; set; }
    [JsonPropertyName("uptime")] public long? UptimeSeconds { get; set; }
    [JsonPropertyName("mac")] public string? MacAddress { get; set; }
    [JsonPropertyName("ip")] public string? IpAddress { get; set; }

    /// <summary>Pixel maps loaded from the filesystem. A non-empty list means a 2D setup.</summary>
    [JsonPropertyName("maps")] public WledMapInfo[]? Maps { get; set; }

    /// <summary>
    /// Usermod data, keyed by usermod name. Sound-reactive builds report "AudioReactive" here.
    /// Values vary wildly by usermod, so they stay untyped.
    /// </summary>
    [JsonPropertyName("u")] public Dictionary<string, System.Text.Json.JsonElement>? Usermods { get; set; }

    /// <summary>True for an ESP8266, which is the constrained case worth designing around.</summary>
    [JsonIgnore]
    public bool IsEsp8266 =>
        Architecture is not null &&
        Architecture.Contains("8266", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The identity to file this device under. The MAC never changes, survives a DHCP lease moving
    /// the device to a new address, and is unique where the friendly name is not — two Gledopto
    /// controllers out of the box are both called "WLED-Gledopto".
    /// </summary>
    [JsonIgnore]
    public string? DeviceKey => MacAddress ?? DeviceId;

    /// <summary>
    /// The mDNS hostname WLED derives from the last three bytes of its MAC, e.g. "wled-6a1be8.local".
    /// Stable across reboots and address changes, which makes it the right thing to reconnect to.
    /// </summary>
    [JsonIgnore]
    public string? MdnsHostName =>
        MacAddress is { Length: >= 6 } mac ? $"wled-{mac[^6..].ToLowerInvariant()}.local" : null;
}

/// <summary>Flash filesystem usage, in kilobytes.</summary>
public sealed class WledFileSystemInfo
{
    [JsonPropertyName("u")] public int? UsedKb { get; set; }
    [JsonPropertyName("t")] public int? TotalKb { get; set; }

    /// <summary>Unix time of the last filesystem write.</summary>
    [JsonPropertyName("pmt")] public long? LastModified { get; set; }

    [JsonIgnore] public int FreeKb => Math.Max(0, (TotalKb ?? 0) - (UsedKb ?? 0));
}

public sealed class WledLedInfo
{
    /// <summary>Total LEDs across all outputs.</summary>
    [JsonPropertyName("count")] public int? Count { get; set; }

    /// <summary>Estimated current draw in milliamps, or 0 when the power estimate is disabled.</summary>
    [JsonPropertyName("pwr")] public int? PowerMilliamps { get; set; }

    /// <summary>Configured current limit in milliamps.</summary>
    [JsonPropertyName("maxpwr")] public int? MaxPowerMilliamps { get; set; }

    /// <summary>Maximum number of segments this build supports.</summary>
    [JsonPropertyName("maxseg")] public int? MaxSegments { get; set; }

    /// <summary>Frames per second the strip is currently rendering.</summary>
    [JsonPropertyName("fps")] public int? Fps { get; set; }

    /// <summary>True when the strip has a dedicated white channel.</summary>
    [JsonPropertyName("rgbw")] public bool? Rgbw { get; set; }

    /// <summary>White channel handling mode.</summary>
    [JsonPropertyName("wv")] public int? WhiteValue { get; set; }

    /// <summary>Non-zero when the device supports color temperature.</summary>
    [JsonPropertyName("cct")] public int? Cct { get; set; }

    /// <summary>True when the device is configured as a 2D matrix.</summary>
    [JsonPropertyName("matrix")] public bool? Matrix { get; set; }

    /// <summary>Preset applied at boot.</summary>
    [JsonPropertyName("bootps")] public int? BootPreset { get; set; }
}

/// <summary>A pixel map file loaded from the device's filesystem.</summary>
public sealed class WledMapInfo
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("n")] public string? Name { get; set; }
}
