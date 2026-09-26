using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The wipe all four of Wipe, Sweep, Wipe Random and Sweep Random are built from: a boundary
/// running the length of the run filling behind it, then the same again with the two colors the
/// other way round.
/// <para>
/// Wipe restarts from the same end each time. Sweep draws the return leg back to front, so the
/// boundary runs back the way it came - the only difference between them is which end the return
/// leg starts from, and on a long roofline it is the difference between a repeating stroke and one
/// that paces to and fro.
/// </para>
/// <para>
/// The boundary LED is not simply one color or the other. The multiply that places the boundary is
/// deliberately allowed to overflow sixteen bits, and what is left over is how far between two LEDs
/// the front has actually reached - so the leading edge is anti-aliased rather than stepping a whole
/// LED at a time. Intensity divides that remainder: low intensity saturates it at once and gives a
/// hard edge, high intensity leaves the boundary LED trailing behind the front.
/// </para>
/// </summary>
internal static class ColorWipe
{
    public static void Render(EffectSegment segment, uint now, bool reverse, bool randomColors = false)
    {
        if (segment.Length == 1)
        {
            // WLED hands a one-LED run to Solid, there being nowhere for a boundary to run.
            segment.Fill(segment.Colors[0]);
            return;
        }

        uint cycleTime = 750u + ((255u - segment.Speed) * 150u);
        uint into = now % cycleTime;
        uint progress = into * 65535 / cycleTime;

        // The second half of the cycle is the return leg, with the two colors the other way round.
        bool back = progress > 32767;

        if (back)
        {
            progress -= 32767;
        }

        if (randomColors)
        {
            AdvanceColors(segment, back);
        }
        else if (back)
        {
            segment.Step = segment.Step == 0 ? 1 : segment.Step;
        }
        else if (segment.Step == 2)
        {
            segment.Step = 3;
        }

        var boundary = (int)((progress * (uint)segment.Length) >> 15);

        // Deliberately sixteen bits: the multiply is meant to overflow, and what is left is how far
        // past the boundary LED the front has reached.
        var remainder = (ushort)(progress * (uint)segment.Length * 2);
        remainder /= (ushort)(segment.Intensity + 1);

        var blend = (byte)Math.Min(remainder, (ushort)255);

        RgbColor second = randomColors
            ? segment.ColorWheel((byte)segment.Aux1)
            : segment.Colors[1];

        // Flat when the colors are random, so it is worked out once rather than per LED.
        RgbColor? flat = randomColors ? segment.ColorWheel((byte)segment.Aux0) : null;

        for (int i = 0; i < segment.Length; i++)
        {
            int at = reverse && back ? segment.Length - 1 - i : i;

            RgbColor first = flat
                ?? segment.ColorFromPalette(at, mapping: true, wrap: segment.SolidWrap);

            RgbColor behind = back ? second : first;
            RgbColor ahead = back ? first : second;

            segment.Pixels[at] =
                i < boundary ? behind
                : i == boundary ? EffectSegment.Blend(ahead, behind, blend)
                : ahead;
        }
    }

    /// <summary>
    /// Picks the next color as each leg starts, which is the whole of what the Random variants add.
    /// <para>
    /// A four-state machine rather than "change color when the leg changes", because the leg
    /// changing is noticed on some frame within that leg and the color must only change once. State
    /// 1 and 3 are the flags saying a change is owed; 0 and 2 say it has been paid.
    /// </para>
    /// </summary>
    private static void AdvanceColors(EffectSegment segment, bool back)
    {
        // Stands in for WLED's call == 0: the first frame has no previous color to move away from.
        WipeState state = segment.Scratch(() => new WipeState());

        if (!state.Started)
        {
            state.Started = true;
            segment.Aux0 = segment.Random8();
            segment.Step = 3;
        }
        else if (back)
        {
            segment.Step = segment.Step == 0 ? 1 : segment.Step;
        }
        else if (segment.Step == 2)
        {
            segment.Step = 3;
        }

        if (segment.Step == 1)
        {
            segment.Aux1 = segment.RandomWheelIndex((byte)segment.Aux0);
            segment.Step = 2;
        }

        if (segment.Step == 3)
        {
            segment.Aux0 = segment.RandomWheelIndex((byte)segment.Aux1);
            segment.Step = 0;
        }
    }

    /// <summary>Whether a color has ever been picked, which the first frame has to do.</summary>
    private sealed class WipeState
    {
        public bool Started { get; set; }
    }
}

/// <summary>
/// The palette filling the run from one end over the secondary color, then draining back the same
/// way.
/// </summary>
public sealed class WipeEffect : IWledEffect
{
    public string Name => "Wipe";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ColorWipe.Render(segment, now, reverse: false);
    }
}

/// <summary>
/// The same fill, except the return leg runs back from the far end rather than starting over, so
/// the boundary sweeps to and fro instead of always travelling the same way.
/// </summary>
public sealed class SweepEffect : IWledEffect
{
    public string Name => "Sweep";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ColorWipe.Render(segment, now, reverse: true);
    }
}

/// <summary>
/// Wipe with the colors picked off the wheel instead of from the palette: a new color fills the run,
/// then another fills over it, and so on.
/// <para>
/// A preview of this cannot line up with the strip, because the colors are the controller's own
/// random draws. What it does reproduce is the behavior - two colors on screen at a time, a fresh
/// one each leg, and never two in a row that look alike.
/// </para>
/// </summary>
public sealed class WipeRandomEffect : IWledEffect
{
    public string Name => "Wipe Random";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ColorWipe.Render(segment, now, reverse: false, randomColors: true);
    }
}

/// <summary>
/// Sweep with the colors picked off the wheel: each new color is introduced from the end the last
/// one finished at, so the run alternates which way it fills.
/// </summary>
public sealed class SweepRandomEffect : IWledEffect
{
    public string Name => "Sweep Random";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ColorWipe.Render(segment, now, reverse: true, randomColors: true);
    }
}
