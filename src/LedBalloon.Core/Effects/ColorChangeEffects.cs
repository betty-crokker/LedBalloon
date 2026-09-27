using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A run that fades between two colors and back, on a triangle rather than a sine - so it spends no
/// longer at the ends than anywhere else.
/// <para>
/// The sibling of Breathe, and the difference is the shape: Breathe holds near the bottom, which is
/// what makes it read as breathing, while this one moves at a constant rate and reads as a wash.
/// </para>
/// </summary>
public sealed class FadeEffect : IWledEffect
{
    public string Name => "Fade";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint counter = now * (uint)((segment.Speed >> 3) + 10);
        var level = (byte)(FastLed.TriWave16((ushort)counter) >> 8);

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = EffectSegment.Blend(
                segment.Colors[1],
                segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap),
                level);
        }
    }
}

/// <summary>
/// The whole run one color at a time, crossfading to a new random one.
/// <para>
/// Intensity is how much of each cycle is spent fading rather than how far it fades: turn it down
/// and the color snaps over and sits, turn it up and it is always on the way somewhere. Each new
/// color is at least a sixth of the wheel from the last, so a change always reads as a change.
/// </para>
/// </summary>
public sealed class RandomColorEffect : IWledEffect
{
    public string Name => "Random Colors";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint cycleTime = 200u + ((255u - segment.Speed) * 50u);
        uint tick = now / cycleTime;
        uint into = now % cycleTime;

        uint fading = (cycleTime * segment.Intensity) >> 8;

        byte crossfade = 255;

        if (fading > 0)
        {
            crossfade = (byte)Math.Min(255, into * 255 / fading);
        }

        if (segment.Call == 0)
        {
            segment.Aux0 = segment.Random8();

            // Deliberately not zero: the first frame must not count as a change of color.
            segment.Step = 2;
        }

        if (tick != segment.Step)
        {
            segment.Aux1 = segment.Aux0;
            segment.Aux0 = segment.RandomWheelIndex((byte)segment.Aux0);
            segment.Step = tick;
        }

        segment.Fill(EffectSegment.Blend(
            segment.ColorWheel((byte)segment.Aux1),
            segment.ColorWheel((byte)segment.Aux0),
            crossfade));
    }
}

/// <summary>
/// Every LED its own random color, a share of them changing at once.
/// <para>
/// Intensity is what share changes on each tick rather than how many colors there are, so turning it
/// down leaves most of the run alone and the picture drifts; turning it up replaces everything at
/// once. Speed zero holds whatever it last drew, which is a way of getting a fixed random scatter.
/// </para>
/// </summary>
internal static class Dynamic
{
    public static void Render(EffectSegment segment, uint now, bool smooth)
    {
        byte[] hues = segment.Scratch(() => Seed(segment));

        uint cycleTime = 50u + ((255u - segment.Speed) * 15u);
        uint tick = now / cycleTime;

        if (tick != segment.Step && segment.Speed != 0)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                if (segment.Random8() <= segment.Intensity)
                {
                    hues[i] = segment.Random8();
                }
            }

            segment.Step = tick;
        }

        for (int i = 0; i < segment.Length; i++)
        {
            RgbColor want = segment.ColorWheel(hues[i]);

            // Smooth creeps toward the new color a sixteenth at a time rather than cutting to it,
            // which turns a scatter that flickers into one that drifts.
            segment.Pixels[i] = smooth
                ? EffectSegment.Blend(segment.Pixels[i], want, 16)
                : want;
        }
    }

    private static byte[] Seed(EffectSegment segment)
    {
        var hues = new byte[segment.Length];

        for (int i = 0; i < hues.Length; i++)
        {
            hues[i] = segment.Random8();
        }

        return hues;
    }
}

/// <summary>Every LED its own random color, changing outright.</summary>
public sealed class DynamicEffect : IWledEffect
{
    public string Name => "Dynamic";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        // The option checkbox is the same thing Dynamic Smooth does by name.
        Dynamic.Render(segment, now, smooth: segment.Option1);
    }
}

/// <summary>The same scatter crossfading between colors instead of cutting.</summary>
public sealed class DynamicSmoothEffect : IWledEffect
{
    public string Name => "Dynamic Smooth";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Dynamic.Render(segment, now, smooth: true);
    }
}

/// <summary>
/// Four colors repeating along the run, stepping round one place at a time.
/// <para>
/// The colors are hard-coded unless you ask otherwise, and the intensity slider is really a three
/// way switch: below 80 gives pastels, above 160 gives the segment's own color slots and drops to
/// three colors, and the middle gives the default red, amber, green and blue. Setting any palette at
/// all overrides all of that and takes four colors from the gradient.
/// </para>
/// </summary>
public sealed class ColorfulEffect : IWledEffect
{
    public string Name => "Colorful";

    private static readonly RgbColor[] Default =
    [
        new(0xFF, 0x00, 0x00), new(0xEE, 0xBB, 0x00),
        new(0x00, 0xEE, 0x00), new(0x00, 0x77, 0xCC),
    ];

    private static readonly RgbColor[] Pastel =
    [
        new(0xFF, 0x80, 0x40), new(0xE5, 0xD2, 0x41),
        new(0x77, 0xFF, 0x77), new(0x77, 0xF0, 0xF0),
    ];

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int count = 4;
        var colors = new RgbColor[9];

        if (segment.Intensity > 160 || segment.PaletteId != 0)
        {
            if (segment.PaletteId == 0)
            {
                count = 3;
                Array.Copy(segment.Colors, colors, 3);
            }
            else
            {
                // C9 2 is the one palette with five colors rather than four, and it spaces them
                // differently as well.
                int spacing = 80;

                if (segment.PaletteId == 52)
                {
                    count = 5;
                    spacing = 61;
                }

                for (int i = 0; i < count; i++)
                {
                    colors[i] = segment.ColorFromPalette(i * spacing, wrap: true, colorSlot: 255);
                }
            }
        }
        else
        {
            Array.Copy(segment.Intensity < 80 ? Pastel : Default, colors, 4);
        }

        // Repeated once so the walk can start anywhere in the list without running off the end.
        for (int i = count; i < (count * 2) - 1; i++)
        {
            colors[i] = colors[i - count];
        }

        uint cycleTime = 50u + (8u * (255u - segment.Speed));
        uint tick = now / cycleTime;

        if (tick != segment.Step)
        {
            if (segment.Speed > 0)
            {
                segment.Aux0++;
            }

            if (segment.Aux0 >= count)
            {
                segment.Aux0 = 0;
            }

            segment.Step = tick;
        }

        for (int i = 0; i < segment.Length; i += count)
        {
            for (int j = 0; j < count; j++)
            {
                segment.SetPixel(i + j, colors[segment.Aux0 + j]);
            }
        }
    }
}
