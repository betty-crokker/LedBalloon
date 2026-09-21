using LedBalloon.Core.Models;

namespace LedBalloon.Core;

/// <summary>
/// What a particular device can actually do, derived from its own <c>/json/info</c>.
/// <para>
/// WLED runs on everything from a bare ESP8266 with a single 30-LED strip to a multi-output ESP32
/// driving a 2D matrix with sound reactivity. Rather than assume, LedBalloon reads the device and
/// adapts: every list is fetched from the firmware, every optional transport is probed, and nothing
/// here is specific to one vendor's board.
/// </para>
/// </summary>
public sealed class WledCapabilities
{
    private WledCapabilities(WledInfo info)
    {
        Info = info;
    }

    public WledInfo Info { get; }

    public static WledCapabilities From(WledInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new WledCapabilities(info);
    }

    /// <summary>
    /// False when the WebSocket server is off or unbuilt, which WLED reports as -1. Fall back to
    /// polling: some minimal builds ship without it.
    /// </summary>
    public bool HasWebSocket => Info.WebSocketClients is not (null or -1);

    /// <summary>True when the strip has a dedicated white channel, so colors should offer one.</summary>
    public bool IsRgbw => Info.Leds?.Rgbw == true;

    /// <summary>True when the device supports color temperature control.</summary>
    public bool HasCct => Info.Leds?.Cct is > 0;

    /// <summary>True when the device has a 2D pixel map, where a segment polyline is the wrong model.</summary>
    public bool Is2DMatrix => Info.Maps is { Length: > 0 } && Info.Leds?.Matrix == true;

    /// <summary>How many segments this build allows. Older and smaller builds allow far fewer.</summary>
    public int SegmentLimit => Info.Leds?.MaxSegments ?? 16;

    public int LedCount => Info.Leds?.Count ?? 0;

    /// <summary>Free space on the device's flash filesystem, in kilobytes.</summary>
    public int FilesystemFreeKb => Info.FileSystem?.FreeKb ?? 0;

    /// <summary>
    /// Whether a LedBalloon project is small enough to live on the controller itself. A roomy ESP32
    /// has most of a megabyte free; a cramped ESP8266 build may have almost nothing, so the app
    /// keeps a local-file store as the fallback rather than assuming.
    /// </summary>
    public bool CanStoreProjectOnDevice => FilesystemFreeKb >= 64;

    /// <summary>Whether a downscaled photo could also live on the controller.</summary>
    public bool CanStorePhotoOnDevice => FilesystemFreeKb >= 512;

    /// <summary>True for sound-reactive builds, which expose extra effects and segment fields.</summary>
    public bool IsSoundReactive => Info.Usermods?.ContainsKey("AudioReactive") == true;

    /// <summary>
    /// True when the firmware is new enough for the per-effect custom sliders (c1/c2/c3), added in
    /// 0.14. Captured looks on older firmware simply carry fewer fields.
    /// </summary>
    public bool HasCustomSliders => (Info.BuildId ?? 0) >= 2206090;

    public override string ToString() =>
        $"{Info.Name} {Info.Version} on {Info.Architecture}: {LedCount} LEDs, " +
        $"{SegmentLimit} segments, ws={HasWebSocket}, fs={FilesystemFreeKb}KB";
}
