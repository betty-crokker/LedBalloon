using System.Text.Json;
using System.Text.Json.Nodes;
using Ledwright.Core.Json;
using Ledwright.Core.Models;

namespace Ledwright.Core.Layout;

/// <summary>
/// Copies a preset from the controller that has it onto one that does not.
/// <para>
/// A preset lives on one box, so recalling it lights only that part of the house. Copying it means
/// both sides can show the same thing — which is what someone means by "make the house look like
/// this", not "make half the house look like this".
/// </para>
/// <para>
/// Written straight into the target's <c>presets.json</c> rather than saved through the live state.
/// WLED's own preset save stores whatever the strip is currently showing, so going that way would
/// have to light the target up to copy onto it. Editing the file copies the preset without touching
/// a single LED.
/// </para>
/// </summary>
public static class PresetCopier
{
    /// <summary>Highest slot WLED will store a preset in.</summary>
    private const int MaxSlot = 250;

    /// <summary>
    /// Builds the preset to store on <paramref name="targetKey"/>, with its segments re-cut to that
    /// controller's runs.
    /// <para>
    /// Runs are matched by position: the first run here takes the first run's appearance there. A
    /// target with more runs repeats the last appearance rather than leaving them dark.
    /// </para>
    /// </summary>
    public static WledPreset BuildFor(
        WledPreset source,
        LedwrightProject project,
        string targetKey,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);

        List<WledSegment> appearances = source.Segments?
            .Where(s => !s.IsPlaceholder && s.Stop is > 0)
            .ToList() ?? [];

        var copy = new WledPreset
        {
            Name = name ?? source.DisplayName,
            On = source.On,
            Brightness = source.Brightness,
            Segments = [],
        };

        IReadOnlyList<Segment> targetRuns = project.SegmentsOn(targetKey);

        for (int i = 0; i < targetRuns.Count; i++)
        {
            Segment run = targetRuns[i];

            WledSegment? appearance = appearances.Count == 0
                ? null
                : appearances[Math.Min(i, appearances.Count - 1)];

            copy.Segments.Add(new WledSegment
            {
                Id = project.WledSegmentIdFor(run),

                // The bounds are this controller's, never the one it was copied from.
                Start = run.Start,
                Stop = run.StopExclusive,
                Reverse = run.Reverse,

                On = appearance?.On ?? true,
                Brightness = appearance?.Brightness,
                Colors = appearance?.Colors,
                Effect = appearance?.Effect,
                Palette = appearance?.Palette,
                Speed = appearance?.Speed,
                Intensity = appearance?.Intensity,
                Custom1 = appearance?.Custom1,
                Custom2 = appearance?.Custom2,
                Custom3 = appearance?.Custom3,
            });
        }

        return copy;
    }

    /// <summary>
    /// Stores a preset on a controller by editing its <c>presets.json</c>, keeping the previous
    /// file beside it. Returns the slot it landed in.
    /// </summary>
    /// <param name="reuseSlotWithSameName">
    /// Overwrite an existing preset of the same name rather than adding a second one, so copying
    /// twice does not litter the controller.
    /// </param>
    public static async Task<int> StoreAsync(
        string host,
        WledPreset preset,
        bool reuseSlotWithSameName = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(preset);

        using var files = new WledFileSystemClient(host);

        byte[]? current = await files.DownloadAsync("presets.json", cancellationToken).ConfigureAwait(false);
        JsonNode root = current is { Length: > 0 }
            ? JsonNode.Parse(current) ?? new JsonObject()
            : new JsonObject();

        if (root is not JsonObject presets)
        {
            throw new WledException($"{host} returned a presets file that is not an object.");
        }

        // Keep what is being replaced. Presets are the one thing on these boxes nobody else has a
        // copy of.
        if (current is { Length: > 0 })
        {
            await files.UploadAsync("presets.bak.json", current, "application/json", cancellationToken)
                .ConfigureAwait(false);
        }

        int slot = ChooseSlot(presets, preset.DisplayName, reuseSlotWithSameName);

        string json = JsonSerializer.Serialize(preset, WledJson.Default.WledPreset);
        presets[slot.ToString(System.Globalization.CultureInfo.InvariantCulture)] = JsonNode.Parse(json);

        byte[] updated = System.Text.Encoding.UTF8.GetBytes(presets.ToJsonString());
        await files.UploadAsync("presets.json", updated, "application/json", cancellationToken)
            .ConfigureAwait(false);

        return slot;
    }

    private static int ChooseSlot(JsonObject presets, string name, bool reuseSlotWithSameName)
    {
        if (reuseSlotWithSameName)
        {
            foreach ((string key, JsonNode? value) in presets)
            {
                if (value?["n"]?.GetValue<string>() is { } existing &&
                    string.Equals(existing.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(key, out int reusable) && reusable > 0)
                {
                    return reusable;
                }
            }
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
}
