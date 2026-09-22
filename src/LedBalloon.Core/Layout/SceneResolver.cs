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

        var state = new WledState
        {
            On = scene.On,
            Brightness = scene.Brightness,
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
            // Master power and brightness are per-controller on the wire but one idea to the user,
            // so the first controller that reports them wins.
            scene.On ??= state.On;
            scene.Brightness ??= state.Brightness;

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

    private static void Apply(WledSegment segment, Appearance appearance)
    {
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
