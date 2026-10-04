using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>Something adoption cannot carry across faithfully, said in runs and LEDs.</summary>
public sealed record AdoptionNote(string ControllerKey, string Message);

/// <summary>What adopting an existing preset would produce, and what it would cost.</summary>
public sealed record AdoptionReport(Scene Scene, IReadOnlyList<AdoptionNote> Notes)
{
    /// <summary>True when the layout accounts for every LED the preset has an opinion about.</summary>
    public bool IsClean => Notes.Count == 0;
}

/// <summary>
/// Turns a preset somebody made in the WLED app into a scene.
/// <para>
/// A preset addresses LED numbers; a scene addresses runs. Adoption is the translation, and it is
/// a deliberate act rather than something that happens on first edit, because it cannot always be
/// exact: a preset made against a layout that has since changed may light ranges that match no run,
/// or match one only partly. Dropping that silently would be the worst of the options, so it is
/// reported and the person decides.
/// </para>
/// <para>
/// Runs are matched by what the preset actually lights, never by position. Position is what
/// produced South's porch wearing North's stairs, and it is not a relationship.
/// </para>
/// </summary>
public static class PresetAdoption
{
    /// <summary>
    /// How much of a run a preset segment has to cover before the run takes its appearance.
    /// <para>
    /// Half. Below that the segment is better described as clipping the run than as lighting it,
    /// and adopting on the strength of a few LEDs would put a pattern somewhere it was never shown.
    /// </para>
    /// </summary>
    private const double Enough = 0.5;

    /// <summary>Works out the scene a preset would become, without changing anything.</summary>
    public static AdoptionReport Plan(LedBalloonProject project, HousePreset preset)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preset);

        var scene = new Scene
        {
            Name = preset.Name,

            // Faithful to what a preset does. WLED applies the segments a preset lists and leaves
            // the rest as they were, so an adopted preset must too -- otherwise adopting South's
            // Bpm would turn it into a whole-house scene that blacks North out, which is not what
            // the thing being adopted does. A captured scene is the opposite and describes
            // everything, because that is what it was made from.
            UnlistedSegmentsOff = false,
        };
        var notes = new List<AdoptionNote>();

        foreach (PresetPlacement placement in preset.Placements)
        {
            // Power is one idea for the house, so the first placement to state it wins. Brightness
            // is the preset's own, and each placement is a different controller's preset: the pair
            // these are adopted from can disagree, and collapsing them used to be how that
            // disagreement was lost.
            scene.On ??= placement.Preset.On;
            scene.SetBrightnessOn(placement.ControllerKey, placement.Preset.Brightness);

            AdoptOne(project, placement, scene, notes);
        }

        return new AdoptionReport(scene, notes);
    }

    private static void AdoptOne(
        LedBalloonProject project,
        PresetPlacement placement,
        Scene scene,
        List<AdoptionNote> notes)
    {
        string key = placement.ControllerKey;

        // Every segment the preset defines, switched on or not. Off used to be filtered out here,
        // on the grounds that a segment showing nothing cannot say what a run should look like -
        // which is true, and was the wrong conclusion. A preset that switches Porchline off is not
        // a preset with nothing to say about Porchline: it says off, and dropping that turned
        // "Stairs white" into a scene that left the porchline running.
        List<WledSegment> defined =
        [
            .. (placement.Preset.Segments ?? []).Where(s => !s.IsPlaceholder && s.Stop is > 0),
        ];

        // The lit ones on their own, for counting LEDs the preset lights that no run covers. An
        // unlit segment lights nothing, so it strays nowhere.
        List<WledSegment> lit = [.. defined.Where(s => s.On != false)];

        IReadOnlyList<Segment> runs = project.SegmentsOn(key);

        foreach (Segment run in runs)
        {
            WledSegment? best = null;
            int covered = 0;

            foreach (WledSegment segment in defined)
            {
                int overlap = Overlap(run.Start, run.StopExclusive, segment.Start ?? 0, segment.Stop ?? 0);

                if (overlap > covered)
                {
                    covered = overlap;
                    best = segment;
                }
            }

            if (best is null || covered < run.Count * Enough)
            {
                notes.Add(new AdoptionNote(key, covered == 0
                    ? $"'{run.Name}' is not in this preset, so the scene leaves it as it is."
                    : $"'{run.Name}' is only covered for {covered} of its {run.Count} LEDs, which is " +
                      "too little to say what it should look like, so the scene leaves it as it is."));

                continue;
            }

            // Off is a thing the preset says, so the scene says it too. A fresh entry rather than
            // Describe's, because what an unlit segment was last set to show is not part of what
            // the preset is asking for.
            if (best.On == false)
            {
                scene.TurnOff(run.Id);
                continue;
            }

            scene.Segments[run.Id] = SceneResolver.Describe(best);

            if (covered < run.Count)
            {
                notes.Add(new AdoptionNote(key,
                    $"'{run.Name}' takes what this preset does to {covered} of its {run.Count} LEDs. " +
                    $"The other {run.Count - covered} join in, where the preset left them alone."));
            }
        }

        // LEDs the preset has an opinion about that belong to no run. Nothing carries them across,
        // because a scene has nowhere to put them.
        int stray = lit.Sum(segment => Uncovered(segment, runs));

        if (stray > 0)
        {
            notes.Add(new AdoptionNote(key,
                $"{stray} LED(s) this preset lights are not part of any segment, so the scene " +
                "has nowhere to put them. Trace them as a segment first if they matter."));
        }
    }

    /// <summary>How many LEDs two half-open ranges have in common.</summary>
    private static int Overlap(int aStart, int aStop, int bStart, int bStop) =>
        Math.Max(0, Math.Min(aStop, bStop) - Math.Max(aStart, bStart));

    /// <summary>How many of a preset segment's LEDs no run on that controller covers.</summary>
    private static int Uncovered(WledSegment segment, IReadOnlyList<Segment> runs)
    {
        int start = segment.Start ?? 0;
        int stop = segment.Stop ?? 0;
        int length = Math.Max(0, stop - start);

        // The runs on one controller are contiguous and ordered by construction, so summing their
        // overlaps cannot double-count.
        return length - runs.Sum(run => Overlap(start, stop, run.Start, run.StopExclusive));
    }
}
