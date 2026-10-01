namespace LedBalloon.Core.Layout;

/// <summary>
/// One place a custom palette is being used, named the way the person who made it would name it.
/// </summary>
/// <param name="Kind">Whether this is a look or a scene, for grouping and for the wording.</param>
/// <param name="Name">What that look or scene is called.</param>
public sealed record PaletteUse(PaletteUseKind Kind, string Name);

public enum PaletteUseKind
{
    /// <summary>A named look, which every scene wearing it follows.</summary>
    Look,

    /// <summary>One scene, on a segment that is not wearing a named look.</summary>
    Scene,
}

/// <summary>
/// Everywhere a controller's custom palette is being used, so editing one can say what it reaches.
/// </summary>
/// <remarks>
/// The same surprise a look has, and worse. A look at least has a name and sits in a list; a custom
/// palette is a file on a controller that several scenes may be quietly built on, and changing its
/// colours changes all of them at once with nothing on screen to say so.
/// <para>
/// Counted per controller, because a custom palette id means a different palette on each box. The
/// ids are positions in that controller's own file list - id 254 is North's <c>palette1.json</c> and
/// South's, and those are two different gradients. So a look asking for 254 is asking for whichever
/// one the segment wearing it is wired to, and the same look can mean two things on one house.
/// </para>
/// </remarks>
public static class CustomPaletteUsage
{
    /// <summary>
    /// Every look and scene that puts <paramref name="paletteId"/> on a segment of this controller.
    /// </summary>
    public static IReadOnlyList<PaletteUse> Find(
        LedBalloonProject project, string controllerKey, int paletteId)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!CustomPaletteCopier.IsCustom(paletteId))
        {
            return [];
        }

        var here = project.SegmentsOn(controllerKey)
            .Select(segment => segment.Id)
            .Where(id => id is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal);

        if (here.Count == 0)
        {
            return [];
        }

        var uses = new List<PaletteUse>();
        var looks = new HashSet<string>(StringComparer.Ordinal);

        foreach (Scene scene in project.Scenes)
        {
            bool directly = false;

            foreach ((string segmentId, SceneEntry entry) in scene.Segments)
            {
                if (!here.Contains(segmentId))
                {
                    continue;
                }

                if (entry.LookId is { Length: > 0 } wearing)
                {
                    // Named by the look rather than by every scene wearing it, because that is the
                    // thing being changed - say "the look Icicles" once rather than the four scenes
                    // that happen to use it, which would read as four separate decisions.
                    if (project.FindLook(wearing) is { Palette: { } worn } look && worn == paletteId)
                    {
                        looks.Add(look.Name);
                    }

                    continue;
                }

                directly |= entry.Palette == paletteId;
            }

            if (directly)
            {
                uses.Add(new PaletteUse(PaletteUseKind.Scene, scene.Name));
            }
        }

        // Looks first: one of them standing in for several scenes is the bigger surprise.
        return [.. looks.Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new PaletteUse(PaletteUseKind.Look, name))
            .Concat(uses)];
    }

    /// <summary>
    /// What to tell somebody about to change it, or empty when it reaches only one place.
    /// </summary>
    /// <remarks>
    /// Silent for nothing and for one, because a warning that fires every time is a warning nobody
    /// reads - and a palette used in exactly one place is the ordinary case, where changing it is
    /// simply editing the thing you are looking at.
    /// </remarks>
    public static string Warn(IReadOnlyList<PaletteUse> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);

        if (uses.Count < 2)
        {
            return string.Empty;
        }

        string[] named = [.. uses.Select(u => u.Kind == PaletteUseKind.Look
            ? $"the look '{u.Name}'"
            : $"'{u.Name}'")];

        return $"This palette is used by {Join(named)}. Changing it changes all of them.";
    }

    private static string Join(string[] names) => names.Length switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names[..^1])} and {names[^1]}",
    };
}
