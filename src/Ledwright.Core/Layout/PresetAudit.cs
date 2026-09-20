using Ledwright.Core.Models;

namespace Ledwright.Core.Layout;

/// <summary>One preset that will not light the whole strip any more.</summary>
/// <param name="Preset">The offending preset.</param>
/// <param name="HighestLedCovered">The last LED index any of its segments reaches.</param>
/// <param name="DeviceLedCount">What the controller says it actually drives.</param>
public sealed record PresetGap(WledPreset Preset, int HighestLedCovered, int DeviceLedCount)
{
    public int DarkLeds => Math.Max(0, DeviceLedCount - HighestLedCovered);

    public override string ToString() =>
        $"'{Preset.DisplayName}' (slot {Preset.Id}) covers {HighestLedCovered} of {DeviceLedCount} LEDs" +
        $" — {DarkLeds} would stay dark.";
}

/// <summary>
/// Finds device presets whose baked-in segment bounds no longer match the strip.
/// <para>
/// This is the diagnosis for the classic WLED annoyance: a preset saved when you believed a run was
/// 100 LEDs restores <c>stop: 100</c> on recall, so the five LEDs you later discovered stay dark.
/// Ledwright's own looks do not have this problem, but presets already on the controller do, and
/// they are what a wall button or the phone app will recall.
/// </para>
/// </summary>
public static class PresetAudit
{
    /// <summary>Lists presets that leave LEDs dark, worst first.</summary>
    public static IReadOnlyList<PresetGap> FindGaps(IEnumerable<WledPreset> presets, int deviceLedCount)
    {
        ArgumentNullException.ThrowIfNull(presets);

        if (deviceLedCount <= 0)
        {
            return [];
        }

        var gaps = new List<PresetGap>();

        foreach (WledPreset preset in presets)
        {
            // A playlist just points at other presets; auditing those covers it.
            if (preset.IsPlaylist || preset.Segments is not { Count: > 0 })
            {
                continue;
            }

            int highest = 0;
            bool sawBounds = false;

            foreach (WledSegment segment in preset.Segments)
            {
                // WLED pads every preset out to the controller's segment limit with {"stop":0}.
                // Counting those as real bounds would make every preset look broken.
                if (segment.IsPlaceholder || segment.Stop is not { } stop)
                {
                    continue;
                }

                sawBounds = true;
                highest = Math.Max(highest, stop);
            }

            // No bounds saved means the preset adapts to whatever the segments currently span,
            // which is exactly the behaviour we want and nothing to warn about.
            if (sawBounds && highest < deviceLedCount)
            {
                gaps.Add(new PresetGap(preset, highest, deviceLedCount));
            }
        }

        gaps.Sort(static (a, b) => b.DarkLeds.CompareTo(a.DarkLeds));
        return gaps;
    }

    /// <summary>
    /// Builds the patch that rewrites a preset in place so its segments span the project's current
    /// segments, keeping its colours and effects.
    /// <para>
    /// Applies the corrected look and re-saves it over the same slot under the same name, which is
    /// how WLED expects a preset to be rewritten.
    /// </para>
    /// </summary>
    public static WledState BuildRefit(LedwrightProject project, WledPreset preset)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preset);

        var state = new WledState
        {
            On = preset.On,
            Brightness = preset.Brightness,
            Segments = [],
            SavePreset = preset.Id,
            PresetName = preset.DisplayName,
        };

        foreach (Segment segment in project.Segments)
        {
            int segmentId = project.WledSegmentIdFor(segment);
            WledSegment? original = preset.Segments?.FirstOrDefault(s => s.Id == segmentId);

            var wled = new WledSegment
            {
                Id = segmentId,
                Start = segment.Start,
                Stop = segment.StopExclusive,
                Reverse = segment.Reverse,
            };

            if (original is not null)
            {
                wled.On = original.On;
                wled.Brightness = original.Brightness;
                wled.Colors = original.Colors;
                wled.Effect = original.Effect;
                wled.Palette = original.Palette;
                wled.Speed = original.Speed;
                wled.Intensity = original.Intensity;
            }

            state.Segments.Add(wled);
        }

        return state;
    }

    /// <summary>
    /// Builds a save that deliberately omits segment bounds, so the preset follows whatever the
    /// segments span at recall time instead of pinning them.
    /// <para>
    /// The cleaner long-term fix for presets you keep on the device. Confirm the behaviour on your
    /// own firmware — see <see cref="WledState.SaveSegmentBounds"/>.
    /// </para>
    /// </summary>
    public static WledState BuildBoundsFreeSave(int presetId, string name) => new()
    {
        SavePreset = presetId,
        PresetName = name,
        SaveSegmentBounds = false,
        IncludeBrightness = true,
    };
}
