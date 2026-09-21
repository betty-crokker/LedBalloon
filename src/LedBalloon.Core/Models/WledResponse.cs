using System.Text.Json.Serialization;

namespace LedBalloon.Core.Models;

/// <summary>
/// The whole-device document from <c>GET /json</c>. WebSocket pushes use the same shape but
/// normally carry only <see cref="State"/> and <see cref="Info"/>.
/// </summary>
public sealed class WledResponse
{
    [JsonPropertyName("state")] public WledState? State { get; set; }
    [JsonPropertyName("info")] public WledInfo? Info { get; set; }

    /// <summary>Effect names, indexed by the value of a segment's <c>fx</c> field.</summary>
    [JsonPropertyName("effects")] public string[]? Effects { get; set; }

    /// <summary>Palette names, indexed by the value of a segment's <c>pal</c> field.</summary>
    [JsonPropertyName("palettes")] public string[]? Palettes { get; set; }
}
