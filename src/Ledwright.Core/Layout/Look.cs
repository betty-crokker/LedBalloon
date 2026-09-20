using System.Text.Json.Serialization;
using Ledwright.Core.Models;

namespace Ledwright.Core.Layout;

/// <summary>How one segment should appear. Colours and effects only — never LED indices.</summary>
public sealed class SegmentLook
{
    [JsonPropertyName("on")] public bool? On { get; set; }
    [JsonPropertyName("brightness")] public byte? Brightness { get; set; }

    [JsonPropertyName("primary")] public string? PrimaryHex { get; set; }
    [JsonPropertyName("secondary")] public string? SecondaryHex { get; set; }
    [JsonPropertyName("tertiary")] public string? TertiaryHex { get; set; }

    [JsonPropertyName("effect")] public int? Effect { get; set; }
    [JsonPropertyName("palette")] public int? Palette { get; set; }
    [JsonPropertyName("speed")] public byte? Speed { get; set; }
    [JsonPropertyName("intensity")] public byte? Intensity { get; set; }

    [JsonIgnore]
    public RgbColor? Primary
    {
        get => RgbColor.TryParse(PrimaryHex, out RgbColor c) ? c : null;
        set => PrimaryHex = value?.ToHex();
    }

    [JsonIgnore]
    public RgbColor? Secondary
    {
        get => RgbColor.TryParse(SecondaryHex, out RgbColor c) ? c : null;
        set => SecondaryHex = value?.ToHex();
    }

    [JsonIgnore]
    public RgbColor? Tertiary
    {
        get => RgbColor.TryParse(TertiaryHex, out RgbColor c) ? c : null;
        set => TertiaryHex = value?.ToHex();
    }
}

/// <summary>
/// Ledwright's own preset: a named appearance for the whole house, stored per segment rather than
/// per LED range.
/// <para>
/// This is what a WLED preset is not. A WLED preset can bake in the segment bounds that were current
/// when you saved it, so correcting a segment's length later leaves the tail of that run dark on
/// recall. A look carries no indices at all — the geometry is resolved from the project at the
/// moment it is applied, so it is always right by construction.
/// </para>
/// </summary>
public sealed class Look
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    [JsonPropertyName("name")] public string Name { get; set; } = "Look";

    /// <summary>Master power. Null leaves it alone.</summary>
    [JsonPropertyName("on")] public bool? On { get; set; }

    /// <summary>Master brightness. Null leaves it alone.</summary>
    [JsonPropertyName("brightness")] public byte? Brightness { get; set; }

    /// <summary>Crossfade into this look, in 100 ms units.</summary>
    [JsonPropertyName("transition")] public int? Transition { get; set; }

    /// <summary>Appearance per segment, keyed by <see cref="Segment.Id"/>.</summary>
    [JsonPropertyName("segments")] public Dictionary<string, SegmentLook> Segments { get; set; } = [];

    /// <summary>
    /// Whether segments this look says nothing about are switched off. True makes a look a complete
    /// description of the house, which is usually what you want from something called a scene.
    /// </summary>
    [JsonPropertyName("unlistedSegmentsOff")] public bool UnlistedSegmentsOff { get; set; } = true;

    public override string ToString() => Name;
}
