using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>
/// Turns device-independent <see cref="Scene"/>s into concrete <see cref="WledState"/> patches using
/// the project's current segment geometry.
/// <para>
/// Every LED index WLED ever sees is produced here, at apply time, from
/// <see cref="Segment.Start"/> and <see cref="Segment.Count"/>. That is what makes a corrected run
/// length take effect everywhere at once instead of leaving old presets pointing at the old range.
/// </para>
/// <para>
/// Results are keyed by controller, because a scene describes a house and a house may be wired to
/// more than one box. Callers send each patch to its own controller.
/// </para>
/// </summary>
public static class SceneResolver
{
    /// <summary>Builds the per-controller patches that put <paramref name="scene"/> on the wall now.</summary>
    public static IReadOnlyDictionary<string, WledState> Resolve(LedBalloonProject project, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(scene);

        var states = new Dictionary<string, WledState>(StringComparer.OrdinalIgnoreCase);

        foreach (string controllerKey in project.ActiveControllerKeys())
        {
            states[controllerKey] = ResolveFor(project, scene, controllerKey);
        }

        return states;
    }

    /// <summary>
    /// The patch for one controller, which is also what publishing turns into a preset on that box.
    /// </summary>
    public static WledState ResolveFor(LedBalloonProject project, Scene scene, string controllerKey)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(scene);

        // Silence about every segment on a controller is silence about its brightness too.
        // Brightness is one value for the whole box, so sending it to a controller this scene
        // leaves alone would dim or raise segments it has just promised to keep out of - which is
        // most of the way to not leaving them alone at all.
        bool mentioned = scene.Mentions(project.SegmentsOn(controllerKey).Select(s => s.Id));

        var state = new WledState
        {
            // Power goes the same way as brightness, and for the same reason. Both are one setting
            // for the whole box, so sending either to a controller this scene says nothing about
            // reaches every segment it has just promised to leave alone. The "Off" preset on South
            // carries on:true with the brightness at zero, and applying it switched North on.
            // True rather than scene.On, for the same reason the published preset says true: a
            // scene is a look and a look is on. A scene written down while the house was dark
            // carried off inside it, and applying it then switched the house off - the app and the
            // timer agreeing with each other and both of them wrong.
            On = mentioned ? true : null,
            Brightness = mentioned ? scene.BrightnessOn(controllerKey) : null,
            TransitionOnce = scene.Transition,
            Segments = [],
        };

        foreach (Segment segment in project.SegmentsOn(controllerKey))
        {
            var wled = new WledSegment
            {
                Id = project.WledSegmentIdFor(segment),

                // Resolved now, from the layout, not recalled from whenever the scene was saved.
                Start = segment.Start,
                Stop = segment.StopExclusive,
                Reverse = segment.Reverse,
            };

            if (scene.Segments.TryGetValue(segment.Id, out SceneEntry? entry))
            {
                Apply(wled, project.Wearing(entry));
            }
            else if (scene.UnlistedSegmentsOff)
            {
                wled.On = false;
            }

            state.Segments.Add(wled);
        }

        return state;
    }

    /// <summary>
    /// Builds the per-controller patches that make each device's segments match the project layout,
    /// without touching colors. Send these after correcting a run length to re-cut the segments.
    /// </summary>
    public static IReadOnlyDictionary<string, WledState> ResolveGeometry(LedBalloonProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var states = new Dictionary<string, WledState>(StringComparer.OrdinalIgnoreCase);

        foreach (string controllerKey in project.ActiveControllerKeys())
        {
            var state = new WledState { Segments = [] };

            foreach (Segment segment in project.SegmentsOn(controllerKey))
            {
                state.Segments.Add(new WledSegment
                {
                    Id = project.WledSegmentIdFor(segment),
                    Start = segment.Start,
                    Stop = segment.StopExclusive,
                    Reverse = segment.Reverse,
                    Name = segment.Name,
                });
            }

            states[controllerKey] = state;
        }

        return states;
    }

    /// <summary>
    /// Captures what the house currently looks like as a scene, given each controller's state.
    /// The geometry is deliberately dropped: that is what the project already knows.
    /// </summary>
    public static Scene Capture(
        LedBalloonProject project,
        IReadOnlyDictionary<string, WledState> states,
        string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(states);

        var scene = new Scene { Name = name };

        foreach ((string controllerKey, WledState state) in states)
        {
            // A scene is a look, and a look is on. What is written down here is what the house
            // would be showing, not whether the switch happened to be up at the moment somebody
            // pressed New scene - and a house whose lights are off is exactly when anybody sits
            // down to arrange one.
            //
            // Captured as off, it published a preset that begins by switching the house off, so a
            // timer firing it set every segment correctly and then killed the power. Which is what
            // happened: "Stairs white" fired at 23:00 and the stairs were dark at a quarter past
            // five. The way to make the house go dark on a timetable is the switch, which is a
            // line in the timetable and not a scene.
            scene.On = true;
            scene.SetBrightnessOn(controllerKey, state.Brightness);

            foreach (Segment segment in project.SegmentsOn(controllerKey))
            {
                int segmentId = project.WledSegmentIdFor(segment);
                WledSegment? wled = state.Segments?.FirstOrDefault(s => s.Id == segmentId);

                if (wled is null)
                {
                    continue;
                }

                scene.Segments[segment.Id] = Describe(wled);
            }
        }

        return scene;
    }

    /// <summary>What one WLED segment is doing, as a one-off appearance.</summary>
    public static SceneEntry Describe(WledSegment wled)
    {
        ArgumentNullException.ThrowIfNull(wled);

        return new SceneEntry
        {
            On = wled.On,
            Brightness = wled.Brightness,
            Primary = wled.Colors is { Length: > 0 } ? wled.PrimaryColor : null,
            Secondary = wled.Colors is { Length: > 1 } ? wled.SecondaryColor : null,
            Effect = wled.Effect,
            Palette = wled.Palette,
            Speed = wled.Speed,
            Intensity = wled.Intensity,
            Custom1 = wled.Custom1,
            Custom2 = wled.Custom2,
            Custom3 = wled.Custom3,
        };
    }

    /// <summary>
    /// Folds a hand edit into what a run wears, changing only what the edit actually said.
    /// <para>
    /// A patch from the color wheel carries a color and nothing else, so absorbing it must leave
    /// the effect and the palette alone. Taking the whole segment instead would quietly replace
    /// everything with whatever defaults the patch happened to have.
    /// </para>
    /// <para>
    /// A run wearing a named look stops wearing it, because the edit is about this scene and this
    /// run. Changing the look itself is a different intention with its own button, and doing it
    /// here would reach into every other scene using it without anyone asking for that.
    /// </para>
    /// </summary>
    public static void Absorb(SceneEntry entry, WledSegment patch, Appearance? wearing = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(patch);

        // What it looked like before the edit has to survive it, and for a run wearing a look that
        // description lives on the look rather than on the entry.
        if (entry.LookId is not null)
        {
            wearing?.CopyTo(entry);
            entry.LookId = null;
        }

        entry.On = patch.On ?? entry.On;
        entry.Brightness = patch.Brightness ?? entry.Brightness;
        entry.Effect = patch.Effect ?? entry.Effect;
        entry.Palette = patch.Palette ?? entry.Palette;
        entry.Speed = patch.Speed ?? entry.Speed;
        entry.Intensity = patch.Intensity ?? entry.Intensity;
        entry.Custom1 = patch.Custom1 ?? entry.Custom1;
        entry.Custom2 = patch.Custom2 ?? entry.Custom2;
        entry.Custom3 = patch.Custom3 ?? entry.Custom3;

        if (patch.Colors is { Length: > 0 })
        {
            entry.Primary = patch.PrimaryColor;
        }

        if (patch.Colors is { Length: > 1 })
        {
            entry.Secondary = patch.SecondaryColor;
        }
    }

    /// <summary>Writes an appearance onto a WLED segment, leaving its bounds alone.</summary>
    public static void Apply(WledSegment segment, Appearance appearance)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(appearance);

        segment.On = appearance.On ?? true;
        segment.Brightness = appearance.Brightness;
        segment.Effect = appearance.Effect;
        segment.Palette = appearance.Palette;
        segment.Speed = appearance.Speed;
        segment.Intensity = appearance.Intensity;
        segment.Custom1 = appearance.Custom1;
        segment.Custom2 = appearance.Custom2;
        segment.Custom3 = appearance.Custom3;

        if (appearance.Primary is { } primary)
        {
            segment.SetColorSlot(0, primary);
        }

        if (appearance.Secondary is { } secondary)
        {
            segment.SetColorSlot(1, secondary);
        }

        if (appearance.Tertiary is { } tertiary)
        {
            segment.SetColorSlot(2, tertiary);
        }
    }
}
