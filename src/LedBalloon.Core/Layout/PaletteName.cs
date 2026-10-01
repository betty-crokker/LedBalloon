using System.Text.Json.Serialization;

namespace LedBalloon.Core.Layout;

/// <summary>
/// What this house calls one controller's uploaded palette.
/// </summary>
/// <remarks>
/// The gradient lives on the controller and the name lives here, which is the one split in this
/// project that is not by choice: WLED has nowhere to put it. Its own UI falls back to "Custom 0",
/// which says where the file is rather than what the palette is for.
/// </remarks>
public sealed class PaletteName
{
    /// <summary>Which controller, since the id means a different palette on each one.</summary>
    [JsonPropertyName("controller")] public string ControllerKey { get; set; } = string.Empty;

    /// <summary>The palette id WLED answers to, counted down from 255.</summary>
    [JsonPropertyName("id")] public int PaletteId { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
