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

    /// <summary>
    /// Whether this controller has sound reaching it, as somebody who can see the box has said.
    /// </summary>
    /// <remarks>
    /// Two dozen of the effects WLED offers follow a microphone or a UDP audio feed, and they sit in
    /// the list beside every other effect. Picking one on a controller with no sound leaves the run
    /// exactly as dark as it was, which reads as the app failing to send.
    /// <para>
    /// Not something the controller can be asked. The usermod reports its source quiet after a few
    /// seconds of nothing, and that is what a missing microphone and a silent street look like
    /// alike; a Gledopto has no audio input at all, but the firmware's default pin assignment is
    /// there in its config either way. So it is asked once and remembered.
    /// </para>
    /// <para>
    /// Null for a controller nobody has answered for, which falls back to whether it is hearing
    /// anything at this moment - right for a house with a microphone and right for one without,
    /// until somebody says otherwise and their answer sticks.
    /// </para>
    /// </remarks>
    [JsonPropertyName("hasSound")] public bool? HasSound { get; set; }

    /// <summary>The name to show, preferring yours over the factory's.</summary>
    public string DisplayName(string? deviceReportedName = null) =>
        !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(deviceReportedName) ? deviceReportedName
        : !string.IsNullOrWhiteSpace(LastHost) ? LastHost
        : Key;

    public override string ToString() => DisplayName();
}
