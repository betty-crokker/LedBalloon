using System.Text.Json.Serialization;

namespace Ledwright.Core.Layout;

/// <summary>
/// Everything Ledwright knows about one installation: the controller, the physical runs of LED,
/// the photo they are drawn on, and the saved looks.
/// <para>
/// Saved as a single JSON file next to the photo, so it is diffable, syncable and easy to hand-edit
/// when something needs correcting in bulk.
/// </para>
/// </summary>
public sealed class LedwrightProject
{
    [JsonPropertyName("name")] public string Name { get; set; } = "My House";

    /// <summary>Host or IP of the controller, as passed to <see cref="WledClient"/>.</summary>
    [JsonPropertyName("deviceHost")] public string? DeviceHost { get; set; }

    /// <summary>
    /// The night photo the segments are drawn on, relative to the project file so the folder stays
    /// portable between machines.
    /// </summary>
    [JsonPropertyName("photoPath")] public string? PhotoPath { get; set; }

    [JsonPropertyName("segments")] public List<Segment> Segments { get; set; } = [];

    [JsonPropertyName("looks")] public List<Look> Looks { get; set; } = [];

    /// <summary>Total LEDs the layout accounts for. Compare against the device's reported count.</summary>
    [JsonIgnore]
    public int TotalLeds => Segments.Count == 0 ? 0 : Segments.Max(s => s.StopExclusive);

    public Segment? FindSegment(string id) =>
        Segments.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>The WLED segment id a segment drives: its explicit one, else its position in the list.</summary>
    public int WledSegmentIdFor(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.SegmentId is { } explicitId)
        {
            return explicitId;
        }

        int index = Segments.IndexOf(segment);
        return index < 0 ? 0 : index;
    }

    /// <summary>
    /// Re-packs every segment end to end from LED 0, preserving order. Call after correcting a run's
    /// length so the runs downstream of it shift instead of overlapping.
    /// </summary>
    public void Repack()
    {
        int next = 0;
        foreach (Segment segment in Segments)
        {
            segment.Start = next;
            next += segment.Count;
        }
    }

    /// <summary>
    /// Reports overlaps, gaps and runs that fall outside the controller's LED count. Worth surfacing
    /// in the UI, because an overlap is the other way a segment ends up looking wrong.
    /// </summary>
    public IReadOnlyList<string> Validate(int? deviceLedCount = null)
    {
        var problems = new List<string>();
        List<Segment> ordered = [.. Segments.OrderBy(s => s.Start)];

        for (int i = 0; i < ordered.Count; i++)
        {
            Segment segment = ordered[i];

            if (segment.Count <= 0)
            {
                problems.Add($"'{segment.Name}' has no LEDs.");
            }

            if (deviceLedCount is { } total && segment.StopExclusive > total)
            {
                problems.Add(
                    $"'{segment.Name}' ends at LED {segment.StopExclusive} but the controller reports {total}.");
            }

            if (i + 1 < ordered.Count)
            {
                Segment next = ordered[i + 1];
                if (next.Start < segment.StopExclusive)
                {
                    problems.Add($"'{segment.Name}' overlaps '{next.Name}' at LED {next.Start}.");
                }
                else if (next.Start > segment.StopExclusive)
                {
                    problems.Add(
                        $"{next.Start - segment.StopExclusive} unassigned LEDs between '{segment.Name}' and '{next.Name}'.");
                }
            }
        }

        return problems;
    }
}
