using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>One LED index that something on the device treats as a segment boundary.</summary>
/// <param name="Index">The LED index.</param>
/// <param name="PresetCount">How many presets put a segment edge here.</param>
/// <param name="IsBusBoundary">True when a physical output starts or ends here.</param>
public sealed record BoundaryEvidence(int Index, int PresetCount, bool IsBusBoundary)
{
    /// <summary>
    /// Hardware wins over habit. A physical output boundary is a fact; a boundary that merely recurs
    /// across presets is a strong hint.
    /// </summary>
    public int Weight => (IsBusBoundary ? 1000 : 0) + PresetCount;

    public string Describe(int presetTotal) => IsBusBoundary
        ? $"LED {Index}: a physical output boundary"
        : $"LED {Index}: used by {PresetCount} of {presetTotal} presets";
}

/// <summary>A segment the evidence suggests exists.</summary>
public sealed record SegmentCandidate(int Start, int Count, string Evidence)
{
    public int StopExclusive => Start + Count;
}

/// <summary>What the device's own data suggests the layout is, and where that data disagrees.</summary>
public sealed record LayoutProposal(
    IReadOnlyList<SegmentCandidate> Segments,
    IReadOnlyList<BoundaryEvidence> Boundaries,
    IReadOnlyList<string> Conflicts)
{
    public int TotalLeds => Segments.Count == 0 ? 0 : Segments.Max(s => s.StopExclusive);

    /// <summary>
    /// Adds the proposed segments to a project, assigned to one controller. Existing segments on
    /// other controllers are left alone, so a second controller can be worked out separately and
    /// the two halves of the house end up in the same project.
    /// </summary>
    public void AddTo(LedBalloonProject project, string controllerKey, string? namePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);

        project.Segments.RemoveAll(s =>
            string.Equals(s.ControllerKey, controllerKey, StringComparison.OrdinalIgnoreCase));

        string prefix = string.IsNullOrWhiteSpace(namePrefix) ? "Segment" : namePrefix;

        int index = 0;
        foreach (SegmentCandidate candidate in Segments)
        {
            project.Segments.Add(new Segment
            {
                Name = $"{prefix} {index + 1}",
                ControllerKey = controllerKey,
                Start = candidate.Start,
                Count = candidate.Count,
                SegmentId = index,
            });

            index++;
        }
    }
}

/// <summary>
/// Works out what the segments probably are, from everything the controller already knows.
/// <para>
/// There is no single segment definition stored on a WLED device. Every preset carries its own copy
/// of the segment bounds that were current when it was saved, so a controller with ten presets can
/// hold ten different opinions about where a run starts and ends — and on the hardware this was
/// written against, one of them disagreed with the rest by ninety-nine LEDs.
/// </para>
/// <para>
/// So the app cannot simply read the layout: it has to weigh the evidence and ask. Physical output
/// boundaries from <c>/cfg.json</c> are hardware fact and are always accepted. Boundaries that recur
/// across many presets are strong hints. Boundaries only one old preset believes in are surfaced as
/// conflicts for the user to rule on, and whatever they choose becomes the project's single source
/// of truth from then on.
/// </para>
/// </summary>
public static class LayoutInference
{
    /// <summary>
    /// A boundary needs to appear in at least this share of presets to be accepted without asking.
    /// </summary>
    private const double AgreementThreshold = 0.5;

    public static LayoutProposal Propose(
        IReadOnlyList<WledPreset> presets,
        IReadOnlyList<LedBus> buses,
        WledState? currentState = null,
        int? deviceLedCount = null)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(buses);

        var busBoundaries = new HashSet<int> { 0 };
        foreach (LedBus bus in buses)
        {
            busBoundaries.Add(bus.Start);
            busBoundaries.Add(bus.StopExclusive);
        }

        int total = deviceLedCount
            ?? (buses.Count > 0 ? buses.Max(b => b.StopExclusive) : 0);

        if (total > 0)
        {
            busBoundaries.Add(total);
        }

        // Count how many presets place a segment edge at each index.
        var votes = new Dictionary<int, int>();
        var conflicts = new List<string>();
        int countedPresets = 0;

        foreach (WledPreset preset in presets)
        {
            List<WledSegment> presetSegments = RealSegments(preset);
            if (presetSegments.Count == 0)
            {
                continue;
            }

            countedPresets++;

            var seen = new HashSet<int>();
            foreach (WledSegment segment in presetSegments)
            {
                if (segment.Start is { } start)
                {
                    seen.Add(start);
                }

                if (segment.Stop is { } stop)
                {
                    seen.Add(stop);
                }
            }

            foreach (int boundary in seen)
            {
                votes[boundary] = votes.GetValueOrDefault(boundary) + 1;
            }

            int highest = presetSegments.Max(s => s.Stop ?? 0);
            if (total > 0 && highest < total)
            {
                conflicts.Add(
                    $"'{preset.DisplayName}' (slot {preset.Id}) stops at LED {highest}, " +
                    $"leaving {total - highest} unlit. It was saved before the strip reached {total}.");
            }
        }

        // Include whatever is on the strip right now: it is the most recent opinion available.
        foreach (WledSegment segment in RealSegments(currentState))
        {
            if (segment.Start is { } start)
            {
                votes[start] = votes.GetValueOrDefault(start) + 1;
            }

            if (segment.Stop is { } stop)
            {
                votes[stop] = votes.GetValueOrDefault(stop) + 1;
            }
        }

        var evidence = votes.Keys
            .Union(busBoundaries)
            .Where(index => index >= 0 && (total == 0 || index <= total))
            .Select(index => new BoundaryEvidence(
                index,
                votes.GetValueOrDefault(index),
                busBoundaries.Contains(index)))
            .OrderBy(e => e.Index)
            .ToList();

        int required = Math.Max(1, (int)Math.Ceiling(countedPresets * AgreementThreshold));

        List<int> accepted = evidence
            .Where(e => e.IsBusBoundary || e.PresetCount >= required)
            .Select(e => e.Index)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        // Boundaries a minority of presets believe in are not silently discarded: they are the
        // interesting disagreements, and the user is the only one who can settle them.
        foreach (BoundaryEvidence rejected in evidence.Where(e => !e.IsBusBoundary && e.PresetCount is > 0 && e.PresetCount < required))
        {
            conflicts.Add(
                $"LED {rejected.Index} is a segment edge in only {rejected.PresetCount} of " +
                $"{countedPresets} presets. Confirm whether a segment really starts there.");
        }

        var segments = new List<SegmentCandidate>();
        for (int i = 0; i < accepted.Count - 1; i++)
        {
            int start = accepted[i];
            int count = accepted[i + 1] - start;

            if (count <= 0)
            {
                continue;
            }

            bool hardware = busBoundaries.Contains(start) && busBoundaries.Contains(accepted[i + 1]);
            segments.Add(new SegmentCandidate(
                start,
                count,
                hardware
                    ? "Matches a physical output, so this one is certain."
                    : $"Agreed on by at least {required} preset(s)."));
        }

        return new LayoutProposal(segments, evidence, conflicts);
    }

    /// <summary>
    /// A preset's real segments, with WLED's <c>{"stop":0}</c> padding removed. A 32-segment
    /// controller writes thirty of those into every preset.
    /// </summary>
    private static List<WledSegment> RealSegments(WledPreset? preset) =>
        preset?.Segments?.Where(s => !s.IsPlaceholder && s.Stop is > 0).ToList() ?? [];

    private static List<WledSegment> RealSegments(WledState? state) =>
        state?.Segments?.Where(s => !s.IsPlaceholder && s.Stop is > 0).ToList() ?? [];
}
