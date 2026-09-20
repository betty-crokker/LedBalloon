using System.Text.Json.Serialization;

namespace Ledwright.Core.Models;

public sealed class WledNightlight
{
    [JsonPropertyName("on")] public bool? On { get; set; }

    /// <summary>Duration in minutes.</summary>
    [JsonPropertyName("dur")] public int? Duration { get; set; }

    /// <summary>0 instant, 1 fade, 2 colour fade, 3 sunrise.</summary>
    [JsonPropertyName("mode")] public int? Mode { get; set; }

    /// <summary>Target brightness at the end of the nightlight period.</summary>
    [JsonPropertyName("tbri")] public byte? TargetBrightness { get; set; }

    /// <summary>Read-only: seconds remaining, or -1 when inactive.</summary>
    [JsonPropertyName("rem")] public int? Remaining { get; set; }
}
