using System.Text.Json.Serialization;

namespace LedBalloon.Core.Layout;

/// <summary>
/// Everything LedBalloon knows about one installation: the controllers, the runs of LED wired to
/// them, the photo they are drawn on, and the saved looks.
/// <para>
/// A house is not a controller. Lights round a roofline may be split across two boxes for no reason
/// other than how far a wire reaches, so segments here carry the controller that drives them and
/// the project spans all of them. Nothing above this layer needs to think about which box a run is
/// plugged into.
/// </para>
/// </summary>
public sealed class LedBalloonProject
{
    /// <summary>
    /// The format this file was written in. Bumped only when a change would misread an older file.
    /// <para>
    /// Adding fields does not need a bump: unknown properties are ignored on read and missing ones
    /// take their defaults. Renaming or repurposing one does, and then a project written by a newer
    /// build is refused rather than quietly mangled by an older one.
    /// </para>
    /// </summary>
    public const int CurrentSchema = 1;

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;

    [JsonPropertyName("name")] public string Name { get; set; } = "My House";

    /// <summary>
    /// Bumped on every save. When copies of this project are found on several controllers, the
    /// highest revision wins — which is how one person's setup reaches everyone else's machine
    /// without anything being shared by hand.
    /// </summary>
    [JsonPropertyName("revision")] public int Revision { get; set; }

    /// <summary>When this revision was written, for showing and for breaking revision ties.</summary>
    [JsonPropertyName("savedUtc")] public DateTimeOffset? SavedUtc { get; set; }

    /// <summary>The controllers this house is wired to, keyed by MAC.</summary>
    [JsonPropertyName("controllers")] public List<ControllerRef> Controllers { get; set; } = [];

    /// <summary>
    /// The photo the segments are drawn on, relative to the project file so the folder stays
    /// portable between machines.
    /// </summary>
    [JsonPropertyName("photoPath")] public string? PhotoPath { get; set; }

    /// <summary>
    /// Content hash of the house photo. The photo is identified by what it is, never by where it
    /// lives, so a layout can move between machines without anyone agreeing on a file path.
    /// </summary>
    [JsonPropertyName("photoHash")] public string? PhotoHash { get; set; }

    /// <summary>
    /// False when the photo was too large for the controllers and lives only in each machine's
    /// local cache. The layout still syncs; the picture has to be shared once.
    /// </summary>
    [JsonPropertyName("photoOnDevice")] public bool PhotoOnDevice { get; set; }

    [JsonPropertyName("segments")] public List<Segment> Segments { get; set; } = [];

    [JsonPropertyName("looks")] public List<Look> Looks { get; set; } = [];

    /// <summary>True once there is enough here to stop asking about hardware and start lighting.</summary>
    [JsonIgnore]
    public bool IsConfigured => Segments.Count > 0;

    /// <summary>Total LEDs across every controller.</summary>
    [JsonIgnore]
    public int TotalLeds => Segments.Sum(s => s.Count);

    public Segment? FindSegment(string id) =>
        Segments.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    public ControllerRef? FindController(string? key) =>
        key is null ? null : Controllers.FirstOrDefault(c => KeyEquals(c.Key, key));

    /// <summary>Adds a controller if it is new, and returns the stored record either way.</summary>
    public ControllerRef RegisterController(string key, string? host, string? mdns, string? factoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ControllerRef? existing = FindController(key);
        if (existing is null)
        {
            existing = new ControllerRef { Key = key.ToLowerInvariant() };
            Controllers.Add(existing);
        }

        existing.LastHost = host ?? existing.LastHost;
        existing.MdnsHost = mdns ?? existing.MdnsHost;

        // Only seed the name; never overwrite one the user chose.
        if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(factoryName))
        {
            existing.Name = factoryName;
        }

        return existing;
    }

    /// <summary>The segments driven by one controller, in address order.</summary>
    public IReadOnlyList<Segment> SegmentsOn(string controllerKey) =>
        [.. Segments.Where(s => KeyEquals(s.ControllerKey, controllerKey)).OrderBy(s => s.Start)];

    /// <summary>Every controller key that has segments assigned to it.</summary>
    public IReadOnlyList<string> ActiveControllerKeys() =>
        [.. Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.ControllerKey))
            .Select(s => s.ControllerKey!.ToLowerInvariant())
            .Distinct()];

    /// <summary>
    /// The WLED segment id this run drives. Numbered per controller, because segment ids are only
    /// unique within one device.
    /// </summary>
    public int WledSegmentIdFor(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.SegmentId is { } explicitId)
        {
            return explicitId;
        }

        IReadOnlyList<Segment> siblings = SegmentsOn(segment.ControllerKey ?? string.Empty);
        int index = siblings.ToList().FindIndex(s => ReferenceEquals(s, segment));
        return index < 0 ? 0 : index;
    }

    /// <summary>
    /// Re-packs one controller's segments end to end from LED 0, preserving order. Call after
    /// correcting a run's length so the runs downstream of it shift instead of overlapping.
    /// </summary>
    public void Repack(string controllerKey)
    {
        int next = 0;
        foreach (Segment segment in SegmentsOn(controllerKey))
        {
            segment.Start = next;
            next += segment.Count;
        }
    }

    /// <summary>Re-packs every controller.</summary>
    public void RepackAll()
    {
        foreach (string key in ActiveControllerKeys())
        {
            Repack(key);
        }
    }

    /// <summary>
    /// Pushes the segments after <paramref name="grown"/> down the wire until nothing sits on top
    /// of it, and returns the ones that had to move.
    /// <para>
    /// Two segments claiming the same LED is not a matter of taste: part of the house lights twice
    /// and part of it not at all. So correcting a length to the truth - the run really is 22 LEDs,
    /// not the 20 you counted - moves its neighbours rather than leaving a wreck behind.
    /// </para>
    /// <para>
    /// It cascades only as far as the collision actually reaches. A segment with room in front of
    /// it absorbs the push and everything past it stays where it was put, so deliberate gaps
    /// further down the wire survive.
    /// </para>
    /// </summary>
    public IReadOnlyList<Segment> MakeRoomAfter(Segment grown)
    {
        ArgumentNullException.ThrowIfNull(grown);

        IReadOnlyList<Segment> ordered = SegmentsOn(grown.ControllerKey ?? string.Empty);
        int index = IndexOf(ordered, grown);

        if (index < 0)
        {
            return [];
        }

        var moved = new List<Segment>();
        int cursor = grown.StopExclusive;

        for (int i = index + 1; i < ordered.Count; i++)
        {
            Segment next = ordered[i];

            if (next.Start >= cursor)
            {
                break;
            }

            next.Start = cursor;
            moved.Add(next);
            cursor = next.StopExclusive;
        }

        return moved;
    }

    /// <summary>
    /// Unused LEDs between this segment and the next one on the same controller, or 0 when it is
    /// the last. LEDs past the last segment are not a gap - they are simply not described yet.
    /// </summary>
    public int SpareAfter(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        IReadOnlyList<Segment> ordered = SegmentsOn(segment.ControllerKey ?? string.Empty);
        int index = IndexOf(ordered, segment);

        return index < 0 || index + 1 >= ordered.Count
            ? 0
            : Math.Max(0, ordered[index + 1].Start - segment.StopExclusive);
    }

    /// <summary>
    /// Pulls every segment after this one back by the unused LEDs sitting right behind it, keeping
    /// their spacing among themselves, and returns the ones that moved.
    /// <para>
    /// Unlike an overlap, a gap lights correctly - it only wastes LEDs - so nothing calls this on
    /// its own. It is what the offer in the segment editor does when it is taken.
    /// </para>
    /// </summary>
    public IReadOnlyList<Segment> CloseSpareAfter(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int spare = SpareAfter(segment);
        if (spare <= 0)
        {
            return [];
        }

        IReadOnlyList<Segment> ordered = SegmentsOn(segment.ControllerKey ?? string.Empty);
        int index = IndexOf(ordered, segment);
        var moved = new List<Segment>();

        for (int i = index + 1; i < ordered.Count; i++)
        {
            ordered[i].Start -= spare;
            moved.Add(ordered[i]);
        }

        return moved;
    }

    /// <summary>By reference: two segments can sit at the same start, and names are not unique.</summary>
    private static int IndexOf(IReadOnlyList<Segment> ordered, Segment segment)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], segment))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reports overlaps, gaps and runs that fall outside a controller's LED count. Checked per
    /// controller, since each has its own address space.
    /// </summary>
    /// <param name="ledCounts">LED count per controller key, where known.</param>
    public IReadOnlyList<string> Validate(IReadOnlyDictionary<string, int>? ledCounts = null)
    {
        var problems = new List<string>();

        foreach (Segment orphan in Segments.Where(s => string.IsNullOrWhiteSpace(s.ControllerKey)))
        {
            problems.Add($"'{orphan.Name}' is not assigned to a controller.");
        }

        foreach (string key in ActiveControllerKeys())
        {
            string label = FindController(key)?.DisplayName() ?? key;
            IReadOnlyList<Segment> ordered = SegmentsOn(key);

            int? ledCount = ledCounts is not null && ledCounts.TryGetValue(key, out int found) ? found : null;

            for (int i = 0; i < ordered.Count; i++)
            {
                Segment segment = ordered[i];

                if (segment.Count <= 0)
                {
                    problems.Add($"{label}: '{segment.Name}' has no LEDs.");
                }

                // Not a number this app made up, and not the end of the last segment either: it is
                // the lengths set against the controller's LED outputs, added up. WLED clamps any
                // segment to it - ask for a stop past the total and it hands back the total - so
                // those LEDs stay dark until the output itself is made longer. Which is a setting,
                // not a wall, so the message says where it is rather than just complaining.
                if (ledCount is { } max && segment.StopExclusive > max)
                {
                    problems.Add(
                        $"{label}: '{segment.Name}' ends at LED {segment.StopExclusive} but the controller drives " +
                        $"{max}, so its last {segment.StopExclusive - max} stay dark until an output is made " +
                        "longer in the controller's own LED settings.");
                }

                if (i + 1 < ordered.Count)
                {
                    Segment next = ordered[i + 1];
                    if (next.Start < segment.StopExclusive)
                    {
                        problems.Add($"{label}: '{segment.Name}' overlaps '{next.Name}' at LED {next.Start}.");
                    }
                    else if (next.Start > segment.StopExclusive)
                    {
                        problems.Add(
                            $"{label}: {next.Start - segment.StopExclusive} unassigned LEDs between " +
                            $"'{segment.Name}' and '{next.Name}'.");
                    }
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// The part of this project that belongs to one controller, and nothing else.
    /// <para>
    /// A controller has no business holding another controller's runs, name or LED counts. Each one
    /// stores its own slice, so a box is a complete and honest description of what is plugged into
    /// it — readable on its own, and meaningless to nobody.
    /// </para>
    /// <para>
    /// The photo is the exception, and it is stored as a separate file rather than in here: it is a
    /// picture of the whole house and cannot be divided.
    /// </para>
    /// </summary>
    public LedBalloonProject SliceFor(string controllerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);

        IReadOnlyList<Segment> mine = SegmentsOn(controllerKey);
        var ids = mine.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var slice = new LedBalloonProject
        {
            Schema = Schema,
            Name = Name,
            Revision = Revision,
            SavedUtc = SavedUtc,
            PhotoPath = PhotoPath,
            PhotoHash = PhotoHash,
            PhotoOnDevice = PhotoOnDevice,
            Controllers = [.. Controllers.Where(c => KeyEquals(c.Key, controllerKey))],
            Segments = [.. mine],
        };

        // A look spans the house, so each controller keeps only the part about its own runs.
        foreach (Look look in Looks)
        {
            var trimmed = new Look
            {
                Id = look.Id,
                Name = look.Name,
                On = look.On,
                Brightness = look.Brightness,
                Transition = look.Transition,
                UnlistedSegmentsOff = look.UnlistedSegmentsOff,
            };

            foreach ((string segmentId, SegmentLook appearance) in look.Segments.Where(p => ids.Contains(p.Key)))
            {
                trimmed.Segments[segmentId] = appearance;
            }

            slice.Looks.Add(trimmed);
        }

        return slice;
    }

    /// <summary>
    /// Reassembles a house from the slices each controller holds. Later slices add to the picture;
    /// none of them overwrites another's runs.
    /// </summary>
    public static LedBalloonProject Assemble(IEnumerable<LedBalloonProject> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);

        var house = new LedBalloonProject { Name = string.Empty };
        var seenSegments = new HashSet<string>(StringComparer.Ordinal);

        foreach (LedBalloonProject slice in slices)
        {
            if (string.IsNullOrEmpty(house.Name) || house.Name == "My House")
            {
                house.Name = slice.Name;
            }

            house.Revision = Math.Max(house.Revision, slice.Revision);
            house.SavedUtc = slice.SavedUtc > house.SavedUtc ? slice.SavedUtc : house.SavedUtc;
            house.PhotoHash ??= slice.PhotoHash;
            house.PhotoPath ??= slice.PhotoPath;
            house.PhotoOnDevice |= slice.PhotoOnDevice;

            foreach (ControllerRef controller in slice.Controllers)
            {
                if (house.FindController(controller.Key) is null)
                {
                    house.Controllers.Add(controller);
                }
            }

            foreach (Segment segment in slice.Segments.Where(s => seenSegments.Add(s.Id)))
            {
                house.Segments.Add(segment);
            }

            foreach (Look look in slice.Looks)
            {
                Look? existing = house.Looks.FirstOrDefault(l =>
                    string.Equals(l.Id, look.Id, StringComparison.Ordinal));

                if (existing is null)
                {
                    house.Looks.Add(look);
                    continue;
                }

                foreach ((string segmentId, SegmentLook appearance) in look.Segments)
                {
                    existing.Segments[segmentId] = appearance;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(house.Name))
        {
            house.Name = "My House";
        }

        return house;
    }

    private static bool KeyEquals(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
