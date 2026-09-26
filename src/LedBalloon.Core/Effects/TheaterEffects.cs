using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Evenly spaced pixels stepping one place along the run at a time, which is the marquee lights of
/// a theater front.
/// <para>
/// The pattern does not slide; it steps, and each step moves it one LED. There are two shapes of
/// it: Theater lights one LED in every few, and Chase 2 lights a block and leaves a gap the same
/// size - so at the same settings one reads as chasing dots and the other as chasing dashes.
/// </para>
/// <para>
/// Intensity is the spacing rather than a brightness, and the pattern's period is what changes with
/// it: a step takes the same time whatever the spacing, so a wider pattern takes longer to come back
/// to where it started.
/// </para>
/// </summary>
internal static class Marquee
{
    public static void Render(EffectSegment segment, uint now, RgbColor lit, RgbColor unlit, bool theater)
    {
        int width = (theater ? 3 : 1) + (segment.Intensity >> 4);

        uint cycleTime = 50u + (255u - segment.Speed);
        uint tick = now / cycleTime;

        // The color slot rather than a computed one means the palette is wanted, which is how the
        // plain versions get a gradient and the rainbow ones do not.
        bool usePalette = lit == segment.Colors[0];

        for (int i = 0; i < segment.Length; i++)
        {
            RgbColor on = usePalette
                ? segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap)
                : lit;

            bool show;

            if (theater)
            {
                show = i % width == segment.Aux0;
            }
            else
            {
                int place = i % (width << 1);

                // Two ranges rather than one, because the block wraps round the end of its own
                // period: what has gone past the start shows at the far end of it.
                show = place < (int)segment.Aux0 - width
                    || (place >= (int)segment.Aux0 && place < (int)segment.Aux0 + width);
            }

            segment.Pixels[i] = show ? on : unlit;
        }

        if (tick != segment.Step)
        {
            segment.Aux0 = (segment.Aux0 + 1) % (uint)(theater ? width : width << 1);
            segment.Step = tick;
        }
    }
}

/// <summary>One LED in every few, stepping along the run.</summary>
public sealed class TheaterChaseEffect : IWledEffect
{
    public string Name => "Theater";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Marquee.Render(segment, now, segment.Colors[0], segment.Colors[1], theater: true);
    }
}

/// <summary>
/// The same marquee with its lit color walking the wheel, a step per tick rather than a step per
/// frame - so unlike the rainbow chases this one keeps time whatever the controller is managing.
/// </summary>
public sealed class TheaterRainbowEffect : IWledEffect
{
    public string Name => "Theater Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Marquee.Render(
            segment, now, segment.ColorWheel((byte)segment.Step), segment.Colors[1], theater: true);
    }
}

/// <summary>Alternating blocks of palette and background, stepping along the run.</summary>
public sealed class RunningColorEffect : IWledEffect
{
    public string Name => "Chase 2";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Marquee.Render(segment, now, segment.Colors[0], segment.Colors[1], theater: false);
    }
}

/// <summary>
/// Bands of color off the wheel drifting along the run, each band a different color from its
/// neighbors.
/// <para>
/// The colors are not drawn fresh each frame. One seed per band-width is taken and the rest are
/// generated from it with a small generator carried in the effect, so the bands keep their colors as
/// they drift and a new one is only introduced at the leading end. That is what makes it read as a
/// stream rather than as static.
/// </para>
/// <para>
/// Each new color is forced at least 42 of 255 around the wheel from the one before it - the same
/// rule the random wipes use - so neighboring bands never blur into one.
/// </para>
/// </summary>
public sealed class RunningRandomEffect : IWledEffect
{
    public string Name => "Stream";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint cycleTime = 25u + (3u * (255u - segment.Speed));
        uint tick = now / cycleTime;

        if (segment.Call == 0)
        {
            segment.Aux0 = segment.Random16();
        }

        int zone = ((255 - segment.Intensity) >> 4) + 1;

        var generator = (ushort)segment.Aux0;

        uint into = tick % (uint)zone;

        // A new band is due when the drift has carried the pattern a whole band along.
        bool fresh = into == 0 && tick != segment.Aux1;

        // Drawn from the far end back, which is the direction the stream travels.
        for (int i = segment.Length - 1; i >= 0; i--)
        {
            if (fresh || into >= zone)
            {
                int previous = generator >> 8;
                int apart = 0;

                while (Math.Abs(apart) < 42)
                {
                    unchecked
                    {
                        generator = (ushort)((generator * 2053) + 13849);
                    }

                    apart = (generator >> 8) - previous;
                }

                if (fresh)
                {
                    // Remembered so the next frame starts where this one did, which is what carries
                    // the bands along rather than redrawing them.
                    segment.Aux0 = generator;
                    fresh = false;
                }

                into = 0;
            }

            segment.Pixels[i] = segment.ColorWheel((byte)(generator >> 8));
            into++;
        }

        segment.Aux1 = tick;
    }
}
