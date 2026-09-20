using Ledwright.Core.Models;

namespace Ledwright.Core.Layout;

/// <summary>
/// Turns device-independent <see cref="Look"/>s into concrete <see cref="WledState"/> patches using
/// the project's current segment geometry.
/// <para>
/// Every LED index WLED ever sees is produced here, at apply time, from
/// <see cref="Segment.Start"/> and <see cref="Segment.Count"/>. That is what makes a corrected run
/// length take effect everywhere at once instead of leaving old presets pointing at the old range.
/// </para>
/// <para>
/// Results are keyed by controller, because a look describes a house and a house may be wired to
/// more than one box. Callers send each patch to its own controller.
/// </para>
/// </summary>
public static class LookResolver
{
    /// <summary>Builds the per-controller patches that put <paramref name="look"/> on the wall now.</summary>
    public static IReadOnlyDictionary<string, WledState> Resolve(LedwrightProject project, Look look)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(look);

        var states = new Dictionary<string, WledState>(StringComparer.OrdinalIgnoreCase);

        foreach (string controllerKey in project.ActiveControllerKeys())
        {
            var state = new WledState
            {
                On = look.On,
                Brightness = look.Brightness,
                TransitionOnce = look.Transition,
                Segments = [],
            };

            foreach (Segment segment in project.SegmentsOn(controllerKey))
            {
                var wled = new WledSegment
                {
                    Id = project.WledSegmentIdFor(segment),

                    // Resolved now, from the layout, not recalled from whenever the look was saved.
                    Start = segment.Start,
                    Stop = segment.StopExclusive,
                    Reverse = segment.Reverse,
                };

                if (look.Segments.TryGetValue(segment.Id, out SegmentLook? appearance))
                {
                    Apply(wled, appearance);
                }
                else if (look.UnlistedSegmentsOff)
                {
                    wled.On = false;
                }

                state.Segments.Add(wled);
            }

            states[controllerKey] = state;
        }

        return states;
    }

    /// <summary>
    /// Builds the per-controller patches that make each device's segments match the project layout,
    /// without touching colours. Send these after correcting a run length to re-cut the segments.
    /// </summary>
    public static IReadOnlyDictionary<string, WledState> ResolveGeometry(LedwrightProject project)
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
    /// Captures what the house currently looks like as a look, given each controller's state.
    /// The geometry is deliberately dropped: that is what the project already knows.
    /// </summary>
    public static Look Capture(
        LedwrightProject project,
        IReadOnlyDictionary<string, WledState> states,
        string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(states);

        var look = new Look { Name = name };

        foreach ((string controllerKey, WledState state) in states)
        {
            // Master power and brightness are per-controller on the wire but one idea to the user,
            // so the first controller that reports them wins.
            look.On ??= state.On;
            look.Brightness ??= state.Brightness;

            foreach (Segment segment in project.SegmentsOn(controllerKey))
            {
                int segmentId = project.WledSegmentIdFor(segment);
                WledSegment? wled = state.Segments?.FirstOrDefault(s => s.Id == segmentId);
                if (wled is null)
                {
                    continue;
                }

                look.Segments[segment.Id] = new SegmentLook
                {
                    On = wled.On,
                    Brightness = wled.Brightness,
                    Primary = wled.Colors is { Length: > 0 } ? wled.PrimaryColor : null,
                    Secondary = wled.Colors is { Length: > 1 } ? wled.SecondaryColor : null,
                    Effect = wled.Effect,
                    Palette = wled.Palette,
                    Speed = wled.Speed,
                    Intensity = wled.Intensity,
                };
            }
        }

        return look;
    }

    private static void Apply(WledSegment segment, SegmentLook appearance)
    {
        segment.On = appearance.On ?? true;
        segment.Brightness = appearance.Brightness;
        segment.Effect = appearance.Effect;
        segment.Palette = appearance.Palette;
        segment.Speed = appearance.Speed;
        segment.Intensity = appearance.Intensity;

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
