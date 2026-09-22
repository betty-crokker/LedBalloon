using System.Text.Json.Serialization;
using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>
/// How one segment should appear: colors and effects only, never LED indices.
/// <para>
/// WLED has no name for this bundle — in its own UI it is just "the segment's settings". The
/// nearest term in the wider lighting world is ETC's <i>palette</i>, a named reusable set of
/// parameter values that cues reference, but "palette" is taken here.
/// </para>
/// </summary>
public class Appearance
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

    /// <summary>
    /// The three effect-specific sliders. Carried because some effects have nothing else to say:
    /// leaving them out meant capturing a house and getting back something that did not match it.
    /// </summary>
    [JsonPropertyName("custom1")] public byte? Custom1 { get; set; }
    [JsonPropertyName("custom2")] public byte? Custom2 { get; set; }
    [JsonPropertyName("custom3")] public byte? Custom3 { get; set; }

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

    /// <summary>Copies the appearance fields onto another. Used to promote and to adopt.</summary>
    public void CopyTo(Appearance other)
    {
        ArgumentNullException.ThrowIfNull(other);

        other.On = On;
        other.Brightness = Brightness;
        other.PrimaryHex = PrimaryHex;
        other.SecondaryHex = SecondaryHex;
        other.TertiaryHex = TertiaryHex;
        other.Effect = Effect;
        other.Palette = Palette;
        other.Speed = Speed;
        other.Intensity = Intensity;
        other.Custom1 = Custom1;
        other.Custom2 = Custom2;
        other.Custom3 = Custom3;
    }
}

/// <summary>
/// A named appearance, defined once and worn by as many segments in as many scenes as you like.
/// <para>
/// "Under stairs warm white" is written down here, not copied into every scene that wants it, so
/// when it turns out too orange you change it in one place. That reach is the point, and it is also
/// the thing people are surprised by, so anything editing a look should say how many scenes use it.
/// </para>
/// </summary>
public sealed class Look : Appearance
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    [JsonPropertyName("name")] public string Name { get; set; } = "Look";

    public override string ToString() => Name;
}

/// <summary>
/// What one segment wears in one scene: either a named <see cref="Look"/>, or a one-off appearance
/// of its own.
/// <para>
/// Both, never. Most segments in most scenes are one-offs and forcing a name on every one would be
/// tedious, so naming is a promotion rather than a requirement.
/// </para>
/// </summary>
public sealed class SceneEntry : Appearance
{
    /// <summary>The named look this segment wears, or null when the fields here are the answer.</summary>
    [JsonPropertyName("look")] public string? LookId { get; set; }
}

/// <summary>
/// A named appearance for the whole house, stored per segment rather than per LED range.
/// <para>
/// This is what a WLED preset is not. A preset bakes in the segment bounds that were current when
/// it was saved, so correcting a run's length later leaves the tail of it dark on recall. A scene
/// carries no indices at all — the geometry is resolved from the project at the moment it is
/// applied, so it is right by construction.
/// </para>
/// <para>
/// Saving a scene publishes it to every controller it covers, as a preset of the same name, so the
/// timers and the wall button can recall it with no PC involved. On the hardware a scene is a
/// preset; the word is not one the person using this has to learn.
/// </para>
/// </summary>
public sealed class Scene
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    [JsonPropertyName("name")] public string Name { get; set; } = "Scene";

    /// <summary>Master power. Null leaves it alone.</summary>
    [JsonPropertyName("on")] public bool? On { get; set; }

    /// <summary>Master brightness. Null leaves it alone.</summary>
    [JsonPropertyName("brightness")] public byte? Brightness { get; set; }

    /// <summary>Crossfade into this scene, in 100 ms units.</summary>
    [JsonPropertyName("transition")] public int? Transition { get; set; }

    /// <summary>What each segment wears, keyed by <see cref="Segment.Id"/>.</summary>
    [JsonPropertyName("segments")] public Dictionary<string, SceneEntry> Segments { get; set; } = [];

    /// <summary>
    /// Whether segments this scene says nothing about are switched off. True makes a scene a
    /// complete description of the house, which is usually what you want from something you can
    /// recall by name.
    /// </summary>
    [JsonPropertyName("unlistedSegmentsOff")] public bool UnlistedSegmentsOff { get; set; } = true;

    public override string ToString() => Name;
}
