using System.Text.Json;

namespace LedBalloon.Core.Models;

/// <summary>
/// What is feeding the sound-reactive effects, as the controller describes it.
/// <para>
/// A sound-reactive build lists two dozen effects that read a microphone or a UDP audio feed and
/// draw nothing without one. They sit in the effect list beside every other effect, and picking one
/// on a controller with no sound leaves the run exactly as dark as the moment before — which looks
/// like the app failing to send rather than the effect having nothing to say.
/// </para>
/// <para>
/// Read from the controller rather than assumed, because sound sync is a setting somebody can turn
/// on: a box with no microphone at all starts answering the moment another one on the network
/// begins broadcasting what it hears.
/// </para>
/// </summary>
/// <param name="Source">
/// What the usermod says it is listening to — "I2S digital", "UDP sync receiver", "not initialized".
/// Null when the build has no sound-reactive usermod at all.
/// </param>
/// <param name="Silent">
/// True while nothing is coming in. The usermod appends " - quiet" to its source line after a few
/// seconds of nothing, which is both what a missing microphone looks like and what a quiet room
/// looks like; the two are not distinguishable from here and do not need to be, since an effect
/// that follows sound does nothing in either case.
/// </param>
public sealed record SoundInput(string? Source, bool Silent)
{
    /// <summary>A build with no sound-reactive usermod, which is also the "could not tell" answer.</summary>
    public static SoundInput None { get; } = new(null, true);

    /// <summary>True when an effect that follows sound would have something to follow.</summary>
    public bool Hearing => Source is { Length: > 0 } && !Silent;

    /// <summary>
    /// What the usermod reports, read out of <c>/json/info</c>'s usermod block.
    /// </summary>
    /// <remarks>
    /// The block is HTML fragments and loose arrays rather than a schema — the "AudioReactive" entry
    /// is a button element — so only the two plain lines are read and anything unexpected comes back
    /// as <see cref="None"/>, which says nothing about any effect.
    /// </remarks>
    public static SoundInput From(IReadOnlyDictionary<string, JsonElement>? usermods)
    {
        if (usermods is null || !usermods.ContainsKey("AudioReactive"))
        {
            return None;
        }

        string[] said = Lines(usermods, "Audio Source");

        if (said.Length == 0)
        {
            return None;
        }

        // "I2S digital" on its own is a live microphone; "I2S digital" followed by " - quiet" is the
        // same microphone with nothing reaching it, which is what a board that has none looks like.
        string source = said[0].Trim();
        bool silent = said.Skip(1).Any(x => x.Contains("quiet", StringComparison.OrdinalIgnoreCase));

        if (source.Length == 0 ||
            source.Contains("not initialized", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            // A UDP feed still counts even with no microphone to fall back on, so the sync line is
            // read before giving up on the controller.
            return Receiving(usermods) ? new SoundInput("UDP sound sync", false) : None;
        }

        return new SoundInput(source, silent && !Receiving(usermods));
    }

    /// <summary>True when another controller on the network is broadcasting what it hears.</summary>
    private static bool Receiving(IReadOnlyDictionary<string, JsonElement> usermods) =>
        Lines(usermods, "UDP Sound Sync")
            .Any(x => x.Contains("receive", StringComparison.OrdinalIgnoreCase));

    /// <summary>One usermod line, which is always an array of strings and numbers.</summary>
    private static string[] Lines(IReadOnlyDictionary<string, JsonElement> usermods, string key)
    {
        if (!usermods.TryGetValue(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)];
    }
}
