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

    /// <summary>
    /// True when two appearances would put exactly the same thing on a run.
    /// <para>
    /// Used to notice that a segment is already wearing a named look, so capturing a house stores
    /// a reference rather than another copy of the same description — which is what makes editing
    /// the look afterwards reach the scenes that use it.
    /// </para>
    /// </summary>
    public bool Matches(Appearance other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return On == other.On
            && Brightness == other.Brightness
            && SameHex(PrimaryHex, other.PrimaryHex)
            && SameHex(SecondaryHex, other.SecondaryHex)
            && SameHex(TertiaryHex, other.TertiaryHex)
            && Effect == other.Effect
            && Palette == other.Palette
            && Speed == other.Speed
            && Intensity == other.Intensity
            && Custom1 == other.Custom1
            && Custom2 == other.Custom2
            && Custom3 == other.Custom3;
    }

    private static bool SameHex(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Master brightness, per controller. A controller with no entry here is left alone.
    /// </summary>
    /// <remarks>
    /// Per controller because that is what it is: a scene becomes one WLED preset on each
    /// controller, and each of those carries its own <c>bri</c>. Holding one number for the house
    /// meant whichever controller was read first decided the brightness of all of them, and a
    /// preset pair that really was bright on one box and dim on the other could not be described
    /// at all - which is exactly what adopting one of the existing presets here ran into.
    /// </remarks>
    [JsonPropertyName("brightnessByController")]
    public Dictionary<string, byte> Brightness { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The one house-wide brightness that schema 2 and earlier stored. Migration reads it.
    /// </summary>
    /// <remarks>
    /// Kept only so that a file written before the split can still be read, where it means the
    /// same value on every controller. <see cref="ProjectSerialization"/> spreads it and clears it,
    /// so it is null in anything this build writes and does not appear in the file.
    /// </remarks>
    [JsonPropertyName("brightness")] public byte? SharedBrightness { get; set; }

    /// <summary>This scene's brightness for one controller, or null to leave that one alone.</summary>
    public byte? BrightnessOn(string controllerKey) =>
        Brightness.TryGetValue(controllerKey, out byte value) ? value : null;

    /// <summary>Sets, or with null removes, this scene's brightness for one controller.</summary>
    public void SetBrightnessOn(string controllerKey, byte? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);

        if (value is { } brightness)
        {
            Brightness[controllerKey] = brightness;
        }
        else
        {
            Brightness.Remove(controllerKey);
        }
    }

    /// <summary>
    /// Spreads a pre-split brightness across the controllers given, and forgets it.
    /// </summary>
    /// <remarks>
    /// One number meaning "everywhere" becomes that number on each controller, which is what the
    /// old file was asking for. Doing nothing when the scene already has per-controller values
    /// keeps this idempotent, so reading a file twice cannot undo an edit made in between.
    /// </remarks>
    public void SplitSharedBrightness(IEnumerable<string> controllerKeys)
    {
        ArgumentNullException.ThrowIfNull(controllerKeys);

        if (SharedBrightness is not { } shared)
        {
            return;
        }

        if (Brightness.Count == 0)
        {
            foreach (string key in controllerKeys)
            {
                Brightness[key] = shared;
            }
        }

        SharedBrightness = null;
    }

    /// <summary>Crossfade into this scene, in 100 ms units.</summary>
    [JsonPropertyName("transition")] public int? Transition { get; set; }

    /// <summary>
    /// The name this scene was last published under, or null if it never has been.
    /// <para>
    /// Publishing matches by name, so a rename would otherwise leave the old name behind on the
    /// controllers — and an orphan here is worse than usual, because a timer pointing at its slot
    /// would go on firing the previous version of the scene forever. Knowing the old name lets the
    /// rename land in the slot the old one holds, which carries the timers with it.
    /// </para>
    /// </summary>
    [JsonPropertyName("publishedAs")] public string? PublishedAs { get; set; }

    /// <summary>
    /// A fingerprint of what was last published to each controller, keyed by controller.
    /// <para>
    /// What makes drift detectable. The scene and its published copy are two renderings of one
    /// fact; when the copy stops matching this, somebody changed it in the WLED app, and
    /// republishing over it would throw that away without anyone noticing. Knowing what we wrote is
    /// the only way to tell that apart from the ordinary case of the scene itself having changed.
    /// </para>
    /// </summary>
    [JsonPropertyName("published")]
    public Dictionary<string, string> Published { get; set; } = [];

    /// <summary>What each segment wears, keyed by <see cref="Segment.Id"/>.</summary>
    [JsonPropertyName("segments")] public Dictionary<string, SceneEntry> Segments { get; set; } = [];

    /// <summary>
    /// Whether segments this scene says nothing about are switched off. True makes a scene a
    /// complete description of the house, which is usually what you want from something you can
    /// recall by name.
    /// </summary>
    [JsonPropertyName("unlistedSegmentsOff")] public bool UnlistedSegmentsOff { get; set; } = true;

    /// <summary>
    /// An independent copy, down to the entries.
    /// <para>
    /// Taken when a scene is picked, so that editing it by hand can be undone: saving the result as
    /// a new scene has to leave the one it was started from exactly as it was.
    /// </para>
    /// </summary>
    public Scene Clone()
    {
        var copy = new Scene
        {
            Id = Id,
            Name = Name,
            On = On,
            Brightness = new Dictionary<string, byte>(Brightness, StringComparer.OrdinalIgnoreCase),
            Transition = Transition,
            PublishedAs = PublishedAs,
            UnlistedSegmentsOff = UnlistedSegmentsOff,
            Published = new Dictionary<string, string>(Published, StringComparer.OrdinalIgnoreCase),
        };

        foreach ((string id, SceneEntry entry) in Segments)
        {
            var duplicate = new SceneEntry { LookId = entry.LookId };
            entry.CopyTo(duplicate);
            copy.Segments[id] = duplicate;
        }

        return copy;
    }

    /// <summary>Makes this scene say what another one says, keeping its own identity.</summary>
    public void CopyFrom(Scene other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Scene source = other.Clone();

        On = source.On;
        Brightness = source.Brightness;
        Transition = source.Transition;
        UnlistedSegmentsOff = source.UnlistedSegmentsOff;
        Segments = source.Segments;
    }

    public override string ToString() => Name;
}
