using System.Text.Json;
using System.Text.Json.Nodes;
using LedBalloon.Core.Json;
using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>What publishing one scene to one controller did.</summary>
/// <param name="ControllerKey">The controller it was published to.</param>
/// <param name="Slot">The preset slot it occupies there, which differs per controller.</param>
/// <param name="Written">
/// False when the controller already held exactly this preset, so nothing was written.
/// </param>
/// <param name="PalettesCarried">Custom palette files copied across to make it look right.</param>
/// <param name="Hash">
/// What the controller holds under this name now, for telling next time whether it is still what
/// LedBalloon put there. Null when nothing was found and nothing written.
/// </param>
/// <param name="Drift">How a disagreement was settled, or null when there was none.</param>
public sealed record ScenePublication(
    string ControllerKey,
    int Slot,
    bool Written,
    int PalettesCarried = 0,
    string? Hash = null,
    DriftChoice? Drift = null);

/// <summary>
/// What publishing would do to one controller's preset file: which slot it lands in, whether
/// anything there actually changes, and whether what is there was put there by somebody else.
/// </summary>
/// <param name="Slot">The slot the preset lands in.</param>
/// <param name="Changed">True when writing would make the file different.</param>
/// <param name="Drifted">
/// True when the slot holds something that is neither what LedBalloon last wrote there nor what it
/// is about to write — so somebody edited it in the WLED app, and republishing would throw that
/// away without saying so.
/// </param>
/// <param name="StoredHash">What is in the slot now, or null when the slot is empty.</param>
public sealed record PublishPlan(int Slot, bool Changed, bool Drifted = false, string? StoredHash = null);

/// <summary>What to do about a published copy somebody else has changed.</summary>
public enum DriftChoice
{
    /// <summary>The scene wins. The controller's version is overwritten.</summary>
    Replace,

    /// <summary>The controller wins. What is on it is read back into the scene.</summary>
    KeepController,

    /// <summary>Neither. Nothing is written, and the disagreement is still there next time.</summary>
    Skip,
}

/// <summary>
/// A published copy that has been changed since LedBalloon wrote it, put to the user before
/// anything is overwritten.
/// </summary>
/// <param name="ControllerKey">The controller whose copy disagrees.</param>
/// <param name="Name">The scene's name, which is also the preset's.</param>
/// <param name="OnController">What is actually stored there, for reading back if that is wanted.</param>
public sealed record DriftReport(string ControllerKey, string Name, WledPreset OnController);

/// <summary>
/// Writes a scene onto the controllers it covers, as a WLED preset of the same name.
/// <para>
/// This is what makes a scene reachable without a PC: the 23:30 timer, the wall button and the
/// phone app all recall presets, and nothing else. Publishing happens as part of saving rather than
/// as a button, so "preset" never has to be a word the user knows.
/// </para>
/// <para>
/// The input is always the scene's own per-segment truth, resolved against the current layout for
/// the controller being written to. It is never another controller's preset — that was
/// <c>PresetCopier.BuildFor</c>, which paired runs by position and so lit South's porch with what
/// North's stairs were doing. There is nothing to pair here, because each controller's part of the
/// scene is derived from the scene rather than translated from somewhere else.
/// </para>
/// </summary>
public static class ScenePublisher
{
    /// <summary>Highest slot WLED will store a preset in.</summary>
    private const int MaxSlot = 250;

    /// <summary>
    /// The preset to store on one controller so that recalling it there puts this scene on that
    /// part of the house.
    /// </summary>
    public static WledPreset BuildFor(LedBalloonProject project, Scene scene, string controllerKey)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);

        WledState state = SceneResolver.ResolveFor(project, scene, controllerKey);

        return new WledPreset
        {
            Name = scene.Name,
            On = state.On,
            Brightness = state.Brightness,
            Transition = scene.Transition,
            Segments = state.Segments,
        };
    }

    /// <summary>
    /// Stores a preset on a controller, keeping the previous file beside it — unless the controller
    /// already holds exactly this, in which case nothing is written at all.
    /// <para>
    /// The skip is not an optimization. Flash has a finite write budget and saving a layout is not
    /// a reason to spend one; republishing four unchanged scenes on every save would burn through
    /// it for nothing.
    /// </para>
    /// </summary>
    /// <param name="previousName">
    /// What this scene was called last time it was published, when it has since been renamed. The
    /// new name lands in the slot the old one holds, so the timers pointing at that slot follow the
    /// rename instead of going on firing the version of the scene it used to be.
    /// </param>
    /// <param name="expectedHash">
    /// What LedBalloon last wrote here. Anything else in the slot was put there by somebody else.
    /// </param>
    /// <param name="onDrift">
    /// Asked before overwriting a copy somebody else changed. Left null, the scene wins silently,
    /// which is the old behaviour and the wrong one: it throws away an edit without saying so.
    /// </param>
    public static async Task<ScenePublication> PublishAsync(
        string controllerKey,
        string host,
        WledPreset preset,
        string? previousName = null,
        string? expectedHash = null,
        Func<DriftReport, Task<DriftChoice>>? onDrift = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(preset);

        using var files = new WledFileSystemClient(host);

        byte[]? current = await files.DownloadAsync("presets.json", cancellationToken).ConfigureAwait(false);
        JsonObject presets = ReadPresets(host, current);

        PublishPlan plan = PlanFor(presets, preset, previousName, expectedHash);

        if (!plan.Changed)
        {
            // Already exactly this, however it got that way. Nothing to write and nothing to argue
            // about: the box agrees with the scene.
            return new ScenePublication(controllerKey, plan.Slot, Written: false, Hash: plan.StoredHash);
        }

        if (plan.Drifted && onDrift is not null)
        {
            DriftChoice choice = await onDrift(
                new DriftReport(controllerKey, preset.DisplayName, StoredAt(presets, plan.Slot)))
                .ConfigureAwait(false);

            if (choice is not DriftChoice.Replace)
            {
                // Keeping the controller's version records it as what is there, so it stops being a
                // disagreement. Skipping records nothing, so the question comes back next save.
                return new ScenePublication(
                    controllerKey,
                    plan.Slot,
                    Written: false,
                    Hash: choice is DriftChoice.KeepController ? plan.StoredHash : expectedHash,
                    Drift: choice);
            }
        }

        // Keep what is being replaced. Presets are the one thing on these boxes that nobody else
        // has a copy of.
        if (current is { Length: > 0 })
        {
            await files.UploadAsync("presets.bak.json", current, "application/json", cancellationToken)
                .ConfigureAwait(false);
        }

        JsonNode? built = ToNode(preset);
        presets[plan.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture)] = built;
        await UploadAsync(files, presets, cancellationToken).ConfigureAwait(false);

        return new ScenePublication(
            controllerKey,
            plan.Slot,
            Written: true,
            Hash: Hash(built),
            Drift: plan.Drifted ? DriftChoice.Replace : null);
    }

    /// <summary>
    /// Works out which slot a preset would land in on a given preset file, and whether writing it
    /// would change anything.
    /// <para>
    /// The comparison is against the stored JSON rather than a parsed preset, because what we are
    /// asking is whether the file would come out different — and the file is written from exactly
    /// these bytes, so the round trip is exact. A field WLED stores that LedBalloon does not model
    /// therefore counts as a difference, which is the safe direction: republishing costs a write,
    /// while skipping wrongly leaves the house showing the old scene.
    /// </para>
    /// </summary>
    public static PublishPlan PlanFor(
        JsonObject presets,
        WledPreset preset,
        string? previousName = null,
        string? expectedHash = null)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(preset);

        int slot = ChooseSlot(presets, preset.DisplayName, previousName);
        string key = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        JsonNode? stored = presets[key];
        bool changed = !JsonNode.DeepEquals(stored, ToNode(preset));
        string? storedHash = stored is null ? null : Hash(stored);

        // Drift is the slot holding something that is neither what we last wrote nor what we are
        // about to write. An empty slot is not drift, and neither is a slot we have never written:
        // with nothing to compare against there is no edit to be throwing away.
        bool drifted = changed
            && stored is not null
            && expectedHash is { Length: > 0 }
            && !string.Equals(storedHash, expectedHash, StringComparison.Ordinal);

        return new PublishPlan(slot, changed, drifted, storedHash);
    }

    /// <summary>What a slot holds, as a preset rather than as JSON.</summary>
    private static WledPreset StoredAt(JsonObject presets, int slot)
    {
        string key = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        WledPreset? stored = presets[key] is { } node
            ? JsonSerializer.Deserialize(node.ToJsonString(), WledJson.Default.WledPreset)
            : null;

        stored ??= new WledPreset();
        stored.Id = slot;
        return stored;
    }

    /// <summary>
    /// A short fingerprint of a stored preset, for telling later whether it is still what we wrote.
    /// <para>
    /// Taken from the JSON rather than from a parsed preset, so a field WLED stores that LedBalloon
    /// does not model still counts. Someone editing a preset in the WLED app changes such fields,
    /// and noticing is the whole point.
    /// </para>
    /// </summary>
    public static string Hash(JsonNode? node) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(node?.ToJsonString() ?? string.Empty)))[..16];

    /// <summary>The fingerprint a preset would have once written.</summary>
    public static string Hash(WledPreset preset) => Hash(ToNode(preset));

    private static JsonNode? ToNode(WledPreset preset) =>
        JsonNode.Parse(JsonSerializer.Serialize(preset, WledJson.Default.WledPreset));

    /// <summary>
    /// Removes the preset of this name from a controller, for a scene being deleted or renamed.
    /// Returns the slot it was in, or null when the controller did not have it.
    /// <para>
    /// A rename has to do this, because publishing matches by name and would otherwise leave the
    /// old name behind — and an orphan here is worse than usual, since a timer pointing at its slot
    /// would go on firing the previous version of the scene forever.
    /// </para>
    /// </summary>
    public static async Task<int?> RemoveAsync(
        string host,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var files = new WledFileSystemClient(host);

        byte[]? current = await files.DownloadAsync("presets.json", cancellationToken).ConfigureAwait(false);
        if (current is not { Length: > 0 })
        {
            return null;
        }

        JsonObject presets = ReadPresets(host, current);

        if (FindSlot(presets, name) is not { } slot)
        {
            return null;
        }

        await files.UploadAsync("presets.bak.json", current, "application/json", cancellationToken)
            .ConfigureAwait(false);

        presets.Remove(slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await UploadAsync(files, presets, cancellationToken).ConfigureAwait(false);

        return slot;
    }

    /// <summary>
    /// Carries the custom palettes a preset asks for onto the controller it is being published to,
    /// taking each from the first controller that has it, and repoints the preset at where they
    /// landed.
    /// <para>
    /// A look storing <c>palette: 254</c> means "the third file this particular box holds", which
    /// is not a fact about the house at all. A controller that has never been given that file does
    /// not say so — WLED silently falls back to plain color, so July 4th published to the other
    /// half of the house would come out white instead of red, white and blue.
    /// </para>
    /// </summary>
    /// <returns>How many palettes were carried across.</returns>
    public static async Task<int> CarryPalettesAsync(
        IEnumerable<string> sourceHosts,
        string targetHost,
        WledPreset preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceHosts);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);
        ArgumentNullException.ThrowIfNull(preset);

        int wanted = CustomPaletteCopier.CustomPalettesUsedBy(preset).Count;
        if (wanted == 0)
        {
            return 0;
        }

        var moved = new Dictionary<int, int>();

        foreach (string source in sourceHosts)
        {
            if (moved.Count == wanted)
            {
                break;
            }

            if (string.Equals(source, targetHost, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyDictionary<int, int> landed = await CustomPaletteCopier
                .CopyForAsync(source, targetHost, preset, cancellationToken)
                .ConfigureAwait(false);

            foreach ((int from, int to) in landed)
            {
                moved.TryAdd(from, to);
            }
        }

        // A palette no source could supply keeps its id, which is right when the target is the box
        // the id came from in the first place.
        CustomPaletteCopier.Remap(preset, moved);

        return moved.Count;
    }

    private static JsonObject ReadPresets(string host, byte[]? current)
    {
        JsonNode root = current is { Length: > 0 }
            ? JsonNode.Parse(current) ?? new JsonObject()
            : new JsonObject();

        return root as JsonObject
            ?? throw new WledException($"{host} returned a presets file that is not an object.");
    }

    private static async Task UploadAsync(
        WledFileSystemClient files,
        JsonObject presets,
        CancellationToken cancellationToken)
    {
        await files.UploadAsync(
            "presets.json",
            System.Text.Encoding.UTF8.GetBytes(presets.ToJsonString()),
            "application/json",
            cancellationToken).ConfigureAwait(false);

        // And then say so, because writing the file does not. See AnnouncePresetsChangedAsync.
        if (FirstEmptySlot(presets) is { } spare)
        {
            await files.AnnouncePresetsChangedAsync(spare, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The lowest slot this file leaves empty, or null when it leaves none.</summary>
    private static int? FirstEmptySlot(JsonObject presets)
    {
        for (int slot = 1; slot <= MaxSlot; slot++)
        {
            if (!presets.ContainsKey(slot.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// Where this name already lives on the box, or the first free slot.
    /// <para>
    /// Reusing the slot is what keeps a scene's slot stable across republishes, which is what lets
    /// a timer point at it once and go on working. The slots differ per controller and nobody is
    /// ever shown them.
    /// </para>
    /// </summary>
    private static int ChooseSlot(JsonObject presets, string name, string? previousName)
    {
        // The name it has now, then the name it used to have, so a rename replaces its old self
        // rather than adding a second preset and orphaning the first.
        if (FindSlot(presets, name) is { } existing)
        {
            return existing;
        }

        if (previousName is { Length: > 0 } && FindSlot(presets, previousName) is { } renamed)
        {
            return renamed;
        }

        // Slot 0 is WLED's scratch slot and never a real preset.
        for (int slot = 1; slot <= MaxSlot; slot++)
        {
            if (!presets.ContainsKey(slot.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                return slot;
            }
        }

        throw new WledException("That controller has no free preset slots left.");
    }

    private static int? FindSlot(JsonObject presets, string name)
    {
        foreach ((string key, JsonNode? value) in presets)
        {
            if (value?["n"]?.GetValue<string>() is { } existing &&
                string.Equals(existing.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(key, out int slot) && slot > 0)
            {
                return slot;
            }
        }

        return null;
    }
}
