using System.Text.Json;
using System.Text.Json.Nodes;

namespace LedBalloon.Core;

/// <summary>
/// What a controller comes back to when the power does.
/// <para>
/// The one setting on these boxes that only matters when nobody is watching. Everything else this
/// app writes is seen the moment it is written; this is read once, by the controller, at the end of
/// a power cut - and whatever it says then is what the house looks like until somebody notices.
/// </para>
/// <para>
/// It is three fields rather than one because the firmware applies them in order. First the switch:
/// <see cref="LightsUp"/> decides whether the strip comes up lit at all, at
/// <see cref="Brightness"/>. Then, if <see cref="PresetId"/> names a slot, the preset in it is
/// applied on top - and a preset carries its own on-ness and its own brightness, so one saved while
/// the lights were off comes up dark however this is set. Naming a slot does not make the first two
/// moot; it just usually overrules them.
/// </para>
/// </summary>
/// <param name="LightsUp">Whether the strip comes up lit rather than dark.</param>
/// <param name="Brightness">
/// What it comes up at, 0-255. WLED also uses this as the brightness to return to when the lights
/// are switched on having been off, so it is never meaningless, even for a controller that comes up
/// dark.
/// </param>
/// <param name="PresetId">
/// The preset slot applied at boot, or <see cref="NoPreset"/> for none - in which case the
/// controller comes up in whatever segment setup it last persisted.
/// </param>
public sealed record BootState(bool LightsUp, int Brightness, int PresetId = BootState.NoPreset)
{
    /// <summary>No preset at boot, which is how WLED spells it: slot zero is not a slot.</summary>
    public const int NoPreset = 0;

    /// <summary>The highest slot WLED will store a preset in.</summary>
    public const int HighestSlot = 250;

    /// <summary>True when a slot is named, so the preset decides the look rather than these fields.</summary>
    public bool FromPreset => PresetId > NoPreset;

    /// <summary>
    /// The state in a sentence, for a reader who wants to know what a power cut would leave behind.
    /// </summary>
    /// <param name="nameOfSlot">What that controller keeps in the slot, by name, if anything.</param>
    public string Describe(Func<int, string?> nameOfSlot)
    {
        ArgumentNullException.ThrowIfNull(nameOfSlot);

        if (FromPreset)
        {
            // The slot rather than nothing when the name is not known here: a preset somebody saved
            // from WLED's own pages is still what the house will come back to, and "preset 4" is a
            // thing the reader can go and look at.
            string holds = nameOfSlot(PresetId) is { Length: > 0 } named
                ? named
                : $"preset {PresetId}";

            return $"comes back to {holds}";
        }

        return LightsUp
            ? $"comes back lit at {Brightness}, however it was last set up"
            : "comes back dark";
    }
}

/// <summary>
/// Reads and writes the <c>def</c> section of a controller's configuration, which is the only
/// record of what a power cut leaves behind.
/// <para>
/// Separated from the HTTP the way <see cref="LedOutputWriter"/> is, so it can be tested against a
/// real <c>cfg.json</c> rather than against a house. The rule is the same and for the same reason:
/// the document is edited in place and posted back whole, because a partial post is not safe on
/// 0.15.3 - a missing <c>hw.led.fps</c> is read as zero and uncaps the frame rate.
/// </para>
/// </summary>
public static class WledBootState
{
    /// <summary>
    /// Reads the boot state out of a parsed <c>cfg.json</c>, or null when there is no <c>def</c>
    /// section to read.
    /// </summary>
    /// <remarks>
    /// Null rather than a default, deliberately. "The controller did not say" and "the controller
    /// comes up dark" are different answers, and only one of them is worth showing somebody as
    /// what their house will do.
    /// </remarks>
    public static BootState? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("def", out JsonElement def) ||
            def.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new BootState(
            LightsUp: def.TryGetProperty("on", out JsonElement on) && on.ValueKind is JsonValueKind.True,
            Brightness: Read(def, "bri"),
            PresetId: Read(def, "ps"));
    }

    /// <summary>
    /// Puts the boot state into the configuration, leaving everything else exactly as it was.
    /// </summary>
    /// <param name="configuration">A parsed <c>cfg.json</c>, edited in place.</param>
    /// <param name="state">What the controller should come back to.</param>
    /// <returns>True when something actually changed, so a no-op never writes to flash.</returns>
    public static bool Apply(JsonObject configuration, BootState state)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(state);

        if (configuration["def"] is not JsonObject def)
        {
            def = [];
            configuration["def"] = def;
        }

        bool changed = SetFlag(def, "on", state.LightsUp);

        // Clamped rather than refused. These arrive from a slider and a picker, so out of range
        // means a bug here rather than a decision worth reporting - and a configuration post that
        // throws halfway has already read the document and not written it, which is the one
        // outcome with nothing to show for it.
        changed |= Set(def, "bri", Math.Clamp(state.Brightness, 0, 255));
        changed |= Set(def, "ps", Math.Clamp(state.PresetId, BootState.NoPreset, BootState.HighestSlot));

        return changed;
    }

    private static int Read(JsonElement holder, string key) =>
        holder.TryGetProperty(key, out JsonElement value) && value.TryGetInt32(out int found) ? found : 0;

    private static int Read(JsonObject holder, string key) =>
        holder[key] is { } value && value.GetValueKind() is JsonValueKind.Number
            ? value.GetValue<int>()
            : 0;

    private static bool Set(JsonObject holder, string key, int value)
    {
        if (Read(holder, key) == value && holder[key] is not null)
        {
            return false;
        }

        holder[key] = value;
        return true;
    }

    private static bool SetFlag(JsonObject holder, string key, bool value)
    {
        bool current = holder[key] is { } node && node.GetValueKind() is JsonValueKind.True;

        if (current == value && holder[key] is not null)
        {
            return false;
        }

        holder[key] = value;
        return true;
    }
}
