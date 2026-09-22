using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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
    /// <summary>
    /// The shape of the stored file.
    /// <para>
    /// 1 stored a slice per controller: each box held only its own runs. 2 mirrors the whole house
    /// onto every box, which is what makes any one of them enough to rebuild it. Loading a 1 unions
    /// the slices exactly as before; the next save writes 2 everywhere and the question goes away.
    /// </para>
    /// </summary>
    public const int CurrentSchema = 2;

    /// <summary>The first schema that mirrors rather than slices.</summary>
    public const int MirroredSchema = 2;

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

    /// <summary>
    /// The segments driven by one controller, in wiring order: output by output, and within an
    /// output in the order they are chained.
    /// <para>
    /// Order comes from <see cref="Segments"/> itself rather than from the stored starts, because
    /// the starts are worked out from this order and sorting by them would be circular. Loading
    /// normalises the list into address order once, so the two always agree.
    /// </para>
    /// </summary>
    public IReadOnlyList<Segment> SegmentsOn(string controllerKey) =>
        [.. Segments
            .Select((segment, index) => (Segment: segment, Index: index))
            .Where(x => KeyEquals(x.Segment.ControllerKey, controllerKey))
            .OrderBy(x => x.Segment.Output <= 0 ? int.MaxValue : x.Segment.Output)
            .ThenBy(x => x.Index)
            .Select(x => x.Segment)];

    /// <summary>The segments plugged into one output of one controller, in the order they chain.</summary>
    public IReadOnlyList<Segment> SegmentsOn(string controllerKey, int output) =>
        [.. SegmentsOn(controllerKey).Where(s => Math.Max(1, s.Output) == output)];

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
    /// Works out where every run on a controller starts, output by output.
    /// <para>
    /// This is the whole placement model in one method. A WS281x strip has no addressing - data is
    /// shifted down the chain and each LED takes the first 24 bits it sees - so the runs on one
    /// output are end to end in the order they are wired, and an output begins where the outputs
    /// before it finish. Everything here is therefore arithmetic on the lengths, and the lengths
    /// are the one thing a person actually knows: they counted them.
    /// </para>
    /// <para>
    /// Because nothing else is typed, an overlap or a gap cannot be expressed. There was
    /// previously a pile of machinery to detect them, push runs out of each other's way and offer
    /// to close what was left, and none of it is needed once the numbers are worked out instead of
    /// entered.
    /// </para>
    /// </summary>
    public void Reflow(string controllerKey)
    {
        int next = 0;

        foreach (IGrouping<int, Segment> output in SegmentsOn(controllerKey)
                     .GroupBy(s => Math.Max(1, s.Output))
                     .OrderBy(g => g.Key))
        {
            foreach (Segment segment in output)
            {
                segment.Start = next;
                next += segment.Count;
            }
        }
    }

    /// <summary>Works out every controller.</summary>
    public void ReflowAll()
    {
        foreach (string key in ActiveControllerKeys())
        {
            Reflow(key);
        }
    }

    /// <summary>
    /// How long each of a controller's outputs is, according to the runs plugged into it.
    /// <para>
    /// The other direction from the one you might expect: the controller's configured length is not
    /// a limit the runs have to fit inside, it is a setting that should agree with them. If you
    /// counted eight LEDs on the porch then that output has eight more LEDs on it than the setting
    /// says, and the setting is what is wrong. Saving writes these back.
    /// </para>
    /// </summary>
    public IReadOnlyList<(int Number, int Length)> OutputLengths(string controllerKey) =>
        [.. SegmentsOn(controllerKey)
            .GroupBy(s => Math.Max(1, s.Output))
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(s => s.Count)))];

    /// <summary>
    /// Fills in which output each run is plugged into, for layouts written before outputs were
    /// modelled, by reading it back out of the start that was stored at the time.
    /// </summary>
    public void AssignOutputs(string controllerKey, IReadOnlyList<LedBus> wiring)
    {
        ArgumentNullException.ThrowIfNull(wiring);

        if (wiring.Count == 0)
        {
            return;
        }

        foreach (Segment segment in Segments.Where(s =>
                     KeyEquals(s.ControllerKey, controllerKey) && s.Output <= 0))
        {
            int number = 1;

            for (int i = 0; i < wiring.Count; i++)
            {
                if (segment.Start >= wiring[i].Start && segment.Start < wiring[i].StopExclusive)
                {
                    number = i + 1;
                    break;
                }
            }

            segment.Output = number;
        }
    }

    /// <summary>
    /// Moves a run one place along the output it is on. Returns false when it is already at the end.
    /// </summary>
    public bool MoveWithinOutput(Segment segment, int delta)
    {
        ArgumentNullException.ThrowIfNull(segment);

        IReadOnlyList<Segment> siblings = SegmentsOn(segment.ControllerKey ?? string.Empty,
            Math.Max(1, segment.Output));

        int at = IndexOf(siblings, segment);
        int to = at + delta;

        if (at < 0 || to < 0 || to >= siblings.Count)
        {
            return false;
        }

        // Order lives in the project's own list, so the swap has to happen there.
        int here = Segments.IndexOf(segment);
        int there = Segments.IndexOf(siblings[to]);

        (Segments[here], Segments[there]) = (Segments[there], Segments[here]);

        Reflow(segment.ControllerKey ?? string.Empty);
        return true;
    }

    /// <summary>
    /// Puts a run on an output at a given place in the chain, moving it between outputs if that is
    /// what is being asked.
    /// <para>
    /// Order lives in <see cref="Segments"/> itself, so placing a run means taking it out of that
    /// list and putting it back somewhere else. The alternative - a position number stored on each
    /// run - is a second copy of the same fact, and two copies of a fact drift.
    /// </para>
    /// </summary>
    /// <param name="index">
    /// Where it should land among the runs already on that output, counting from the controller.
    /// Past the end simply means last.
    /// </param>
    public void PlaceOnOutput(Segment segment, int output, int index)
    {
        ArgumentNullException.ThrowIfNull(segment);

        string key = segment.ControllerKey ?? string.Empty;
        output = Math.Max(1, output);

        Segments.Remove(segment);
        segment.Output = output;

        IReadOnlyList<Segment> siblings = SegmentsOn(key, output);

        // Before the run currently in that place; failing that, after the last run on this output;
        // failing that, after the last run on the controller, so the flat list stays grouped.
        int at = index < siblings.Count
            ? Segments.IndexOf(siblings[index])
            : siblings.Count > 0
                ? Segments.IndexOf(siblings[^1]) + 1
                : LastOn(key) + 1;

        Segments.Insert(Math.Clamp(at, 0, Segments.Count), segment);
        Reflow(key);
    }

    private int LastOn(string controllerKey)
    {
        int last = -1;

        for (int i = 0; i < Segments.Count; i++)
        {
            if (KeyEquals(Segments[i].ControllerKey, controllerKey))
            {
                last = i;
            }
        }

        return last;
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
    /// Reports what is left to go wrong once the placement is worked out rather than typed.
    /// <para>
    /// Overlaps and gaps are not on the list any more, because <see cref="Reflow"/> makes them
    /// impossible to express: runs on an output are laid end to end in wiring order, and an output
    /// begins where the one before it finishes. Nor is running past the controller's configured LED
    /// count, which was never a limit - that setting is the sum of the runs plugged in, and saving
    /// writes it to agree with them.
    /// </para>
    /// <para>
    /// What is left is a run with no length, a run on no controller, and an output number with
    /// nothing wired to the outputs before it.
    /// </para>
    /// </summary>
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

            foreach (Segment segment in SegmentsOn(key).Where(s => s.Count <= 0))
            {
                problems.Add($"{label}: '{segment.Name}' has no LEDs.");
            }

            // Outputs are numbered by their place in the controller's own wiring, so output 3 with
            // nothing on outputs 1 and 2 means the numbering has drifted from the hardware.
            IReadOnlyList<(int Number, int Length)> outputs = OutputLengths(key);

            for (int i = 0; i < outputs.Count; i++)
            {
                if (outputs[i].Number != i + 1)
                {
                    problems.Add(
                        $"{label}: there are runs on output {outputs[i].Number} but nothing on output {i + 1}.");
                }
            }

            // Not a limit being broken - the controller's setting is simply out of date with what
            // is plugged in, and WLED clamps segments to it, so the difference would sit dark.
            // Said here until LedBalloon writes the output lengths back itself.
            int described = outputs.Sum(o => o.Length);

            if (ledCounts is not null && ledCounts.TryGetValue(key, out int configured) &&
                configured > 0 && described > configured)
            {
                problems.Add(
                    $"{label}: the runs add up to {described} LEDs but the controller is still set up for " +
                    $"{configured}. Lengthen its LED outputs to match, or the last {described - configured} " +
                    "stay dark.");
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
    /// Everything about this house except when it was written, hashed.
    /// <para>
    /// For telling two copies apart when their revisions match, which means either that they are
    /// the same document or that two people edited in a network split and neither knows. The
    /// revision and the timestamp are left out on purpose: they say when, not what.
    /// </para>
    /// </summary>
    public string Fingerprint()
    {
        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(ProjectSerialization.ToUtf8(this)));

        if (node is JsonObject document)
        {
            document.Remove("revision");
            document.Remove("savedUtc");
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(node?.ToJsonString() ?? string.Empty)))[..16];
    }

    /// <summary>
    /// Reassembles a house from the slices each controller holds. Later slices add to the picture;
    /// none of them overwrites another's runs.
    /// <para>
    /// Only for reading schema 1, where each box held its own runs and the house was what they added
    /// up to. Mirrored copies must not be unioned: a box that was offline while a run was deleted
    /// still holds it, and unioning would bring it back from the dead.
    /// </para>
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
