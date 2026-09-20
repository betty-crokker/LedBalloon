using System.Text.Json.Serialization;

namespace Ledwright.Core.Models;

/// <summary>
/// One entry from the device's <c>presets.json</c>.
/// <para>
/// Presets are not part of the <c>/json</c> document. They live in a file on the device's flash
/// filesystem and are fetched whole from <c>GET /presets.json</c> — which is exactly what WLED's own
/// web UI does. Vendor-preloaded presets (Gledopto and friends ship a set) are ordinary WLED presets
/// in that same file, so they come back with everything else.
/// </para>
/// </summary>
public sealed class WledPreset
{
    /// <summary>Slot number, 1-250. Not stored in the file; filled in from the dictionary key.</summary>
    [JsonIgnore] public int Id { get; set; }

    /// <summary>Preset name as shown in the WLED UI.</summary>
    [JsonPropertyName("n")] public string? Name { get; set; }

    /// <summary>Quick-load label: the one- or two-character button shown on WLED's main screen.</summary>
    [JsonPropertyName("ql")] public string? QuickLabel { get; set; }

    /// <summary>Present when this slot holds a playlist rather than a single look.</summary>
    [JsonPropertyName("playlist")] public WledPlaylist? Playlist { get; set; }

    // A preset body is a state snapshot. These are the fields worth previewing in a list;
    // fetch the preset by applying it if you need the rest.
    [JsonPropertyName("on")] public bool? On { get; set; }
    [JsonPropertyName("bri")] public byte? Brightness { get; set; }
    [JsonPropertyName("seg")] public List<WledSegment>? Segments { get; set; }

    [JsonIgnore] public bool IsPlaylist => Playlist is not null;

    /// <summary>True for a slot that exists in the file but holds nothing usable.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && Playlist is null && Segments is null && On is null;

    /// <summary>What to show in a list when the preset was saved without a name.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Preset {Id}" : Name;

    public override string ToString() => $"{Id}: {DisplayName}{(IsPlaylist ? " (playlist)" : string.Empty)}";
}

/// <summary>A preset slot holding a sequence of other presets.</summary>
public sealed class WledPlaylist
{
    /// <summary>Preset ids to play, in order.</summary>
    [JsonPropertyName("ps")] public int[]? Presets { get; set; }

    /// <summary>Per-entry duration in 100 ms units. A single value applies to all entries.</summary>
    [JsonPropertyName("dur")] public int[]? Durations { get; set; }

    /// <summary>Per-entry crossfade in 100 ms units.</summary>
    [JsonPropertyName("transition")] public int[]? Transitions { get; set; }

    /// <summary>Times to repeat the sequence; 0 means loop forever.</summary>
    [JsonPropertyName("repeat")] public int? Repeat { get; set; }

    /// <summary>Preset to settle on once the playlist finishes.</summary>
    [JsonPropertyName("end")] public int? EndPreset { get; set; }

    /// <summary>Play entries in random order.</summary>
    [JsonPropertyName("r")] public bool? Shuffle { get; set; }
}
