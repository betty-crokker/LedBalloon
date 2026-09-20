using Ledwright.Core.Models;

namespace Ledwright.Core.Layout;

/// <summary>
/// Turns device-independent <see cref="Look"/>s into concrete <see cref="WledState"/> patches using
/// the project's current segment geometry.
/// <para>
/// Every LED index WLED ever sees is produced here, at apply time, from
/// <see cref="Segment.Start"/> and <see cref="Segment.Count"/>. That is what makes a corrected segment
/// length take effect everywhere at once instead of leaving old presets pointing at the old range.
/// </para>
/// </summary>
public static class LookResolver
{
    /// <summary>Builds the state patch that puts <paramref name="look"/> on the wall right now.</summary>
    public static WledState Resolve(LedwrightProject project, Look look)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(look);

        var state = new WledState
        {
            On = look.On,
            Brightness = look.Brightness,
            TransitionOnce = look.Transition,
            Segments = [],
        };

        foreach (Segment segment in project.Segments)
        {
            var wled = new WledSegment
            {
                Id = project.WledSegmentIdFor(segment),

                // Resolved now, from the layout, rather than recalled from whenever the look was saved.
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

        return state;
    }

    /// <summary>
    /// Builds the patch that makes the device's segments match the project layout, without touching
    /// colours. Send this after correcting a segment length to re-cut the segments on the controller.
    /// </summary>
    public static WledState ResolveGeometry(LedwrightProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var state = new WledState { Segments = [] };

        foreach (Segment segment in project.Segments)
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

        return state;
    }

    /// <summary>
    /// Captures the device's current appearance as a look, mapping segments back onto segments by id.
    /// The geometry is deliberately dropped: that is what the project already knows.
    /// </summary>
    public static Look Capture(LedwrightProject project, WledState state, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(state);

        var look = new Look
        {
            Name = name,
            On = state.On,
            Brightness = state.Brightness,
        };

        foreach (Segment segment in project.Segments)
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
