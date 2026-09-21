using System.Text.Json.Serialization;

namespace LedBalloon.Core.Models;

/// <summary>
/// The mutable WLED state, as returned by <c>GET /json/state</c> and accepted by <c>POST /json/state</c>.
/// Null properties are omitted when serialized, so the same type serves as both a full snapshot and a
/// sparse patch. See <see href="https://kno.wled.ge/interfaces/json-api/"/>.
/// </summary>
public sealed class WledState
{
    [JsonPropertyName("on")] public bool? On { get; set; }

    /// <summary>Master brightness, 0-255. WLED renders 0 as dark but leaves <c>on</c> alone.</summary>
    [JsonPropertyName("bri")] public byte? Brightness { get; set; }

    /// <summary>Default crossfade duration in 100 ms units, persisted on the device.</summary>
    [JsonPropertyName("transition")] public int? Transition { get; set; }

    /// <summary>Crossfade duration for this call only, in 100 ms units.</summary>
    [JsonPropertyName("tt")] public int? TransitionOnce { get; set; }

    /// <summary>Preset to apply, or -1 when none is active.</summary>
    [JsonPropertyName("ps")] public int? Preset { get; set; }

    /// <summary>Playlist to apply.</summary>
    [JsonPropertyName("pl")] public int? Playlist { get; set; }

    /// <summary>Index of the segment the UI treats as primary.</summary>
    [JsonPropertyName("mainseg")] public int? MainSegment { get; set; }

    /// <summary>Live-override mode: 0 off, 1 until realtime ends, 2 until reboot.</summary>
    [JsonPropertyName("lor")] public int? LiveOverride { get; set; }

    [JsonPropertyName("nl")] public WledNightlight? Nightlight { get; set; }
    [JsonPropertyName("udpn")] public WledUdpSync? UdpSync { get; set; }

    [JsonPropertyName("seg")] public List<WledSegment>? Segments { get; set; }

    /// <summary>Saves the resulting state into a preset slot. Pair with <see cref="PresetName"/>.</summary>
    [JsonPropertyName("psave")] public int? SavePreset { get; set; }

    /// <summary>Deletes a preset slot.</summary>
    [JsonPropertyName("pdel")] public int? DeletePreset { get; set; }

    /// <summary>Preset name, used when saving.</summary>
    [JsonPropertyName("n")] public string? PresetName { get; set; }

    /// <summary>When saving a preset: include master brightness in it.</summary>
    [JsonPropertyName("ib")] public bool? IncludeBrightness { get; set; }

    /// <summary>
    /// When saving a preset: bake the current segment start/stop indices into it.
    /// <para>
    /// This is the flag behind the "preset lights only the first 100 LEDs after I changed the segment
    /// to 105" problem. A preset saved with bounds included restores the old indices on recall. Save
    /// with this false and the preset applies to whatever the segments currently span.
    /// </para>
    /// <para>
    /// Verify the behavior against your own firmware before relying on it; it corresponds to the
    /// "save segment bounds" checkbox in WLED's preset dialog.
    /// </para>
    /// </summary>
    [JsonPropertyName("sb")] public bool? SaveSegmentBounds { get; set; }

    /// <summary>Send with a request to have WLED echo the full resulting state back. Not part of device state.</summary>
    [JsonPropertyName("v")] public bool? ReturnFullState { get; set; }

    /// <summary>Send true to reboot the device. Not part of device state.</summary>
    [JsonPropertyName("rb")] public bool? Reboot { get; set; }

    /// <summary>Read-only: true while a realtime source (UDP, E1.31, Art-Net) is driving the strip.</summary>
    [JsonPropertyName("live")] public bool? Live { get; set; }

    /// <summary>The segment WLED considers primary, else the first one, else null.</summary>
    public WledSegment? MainOrFirstSegment()
    {
        if (Segments is not { Count: > 0 })
        {
            return null;
        }

        if (MainSegment is { } main)
        {
            WledSegment? match = Segments.FirstOrDefault(s => s.Id == main);
            if (match is not null)
            {
                return match;
            }
        }

        return Segments[0];
    }

    /// <summary>Convenience patch builder: a state touching exactly one segment.</summary>
    public static WledState ForSegment(int segmentId, Action<WledSegment> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var segment = new WledSegment { Id = segmentId };
        configure(segment);
        return new WledState { Segments = [segment] };
    }

    /// <summary>
    /// An independent copy, down to the segments.
    /// <para>
    /// The photo draws the house as it is plus whatever has been picked but not sent yet, which
    /// means laying changes over a copy of the live state. Without this the overlay would write
    /// through into the state the controller actually reported.
    /// </para>
    /// </summary>
    public WledState Clone()
    {
        var copy = (WledState)MemberwiseClone();
        copy.Segments = Segments?.Select(segment => segment.Clone()).ToList();
        return copy;
    }

    /// <summary>Copies every non-null field of <paramref name="newer"/> over this state, merging segments by id.</summary>
    public void MergeFrom(WledState newer)
    {
        ArgumentNullException.ThrowIfNull(newer);

        On = newer.On ?? On;
        Brightness = newer.Brightness ?? Brightness;
        Transition = newer.Transition ?? Transition;
        TransitionOnce = newer.TransitionOnce ?? TransitionOnce;
        Preset = newer.Preset ?? Preset;
        Playlist = newer.Playlist ?? Playlist;
        MainSegment = newer.MainSegment ?? MainSegment;
        LiveOverride = newer.LiveOverride ?? LiveOverride;
        Nightlight = newer.Nightlight ?? Nightlight;
        UdpSync = newer.UdpSync ?? UdpSync;
        ReturnFullState = newer.ReturnFullState ?? ReturnFullState;
        Reboot = newer.Reboot ?? Reboot;
        Live = newer.Live ?? Live;

        if (newer.Segments is null)
        {
            return;
        }

        Segments ??= [];
        foreach (WledSegment incoming in newer.Segments)
        {
            WledSegment? existing = Segments.FirstOrDefault(s => s.Id == incoming.Id);
            if (existing is null)
            {
                Segments.Add(incoming);
            }
            else
            {
                existing.MergeFrom(incoming);
            }
        }
    }
}
