using System.Text.Json.Serialization;

namespace LedBalloon.Core.Layout;

/// <summary>
/// A controller the house is wired to, as the project remembers it.
/// <para>
/// Identified by MAC, because that is the only thing about a WLED device that never changes. The
/// address moves with the DHCP lease and the factory name is not unique — two Gledopto controllers
/// out of the box are both called "WLED-Gledopto", which is precisely why a name you chose yourself
/// is worth storing.
/// </para>
/// </summary>
public sealed class ControllerRef
{
    /// <summary>The device's MAC address, lower case, no separators. The primary key.</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

    /// <summary>What you call it: "Garage", "Front of house". Falls back to the device's own name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>Where it answered last. A hint for reconnecting before mDNS finds it again.</summary>
    [JsonPropertyName("lastHost")] public string? LastHost { get; set; }

    /// <summary>The mDNS name WLED derives from the MAC, e.g. "wled-6a1be8.local".</summary>
    [JsonPropertyName("mdns")] public string? MdnsHost { get; set; }

    /// <summary>The name to show, preferring yours over the factory's.</summary>
    public string DisplayName(string? deviceReportedName = null) =>
        !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(deviceReportedName) ? deviceReportedName
        : !string.IsNullOrWhiteSpace(LastHost) ? LastHost
        : Key;

    public override string ToString() => DisplayName();
}
