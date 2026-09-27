using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// FastLED's sixteen-entry palettes as the effects that build their own need them: made from four
/// colors, and nudged one channel-step at a time toward another.
/// <para>
/// Only Noise Pal uses either of these, and it uses both together - it invents a target palette every
/// few seconds and then crawls toward it, so what is on the strip is never quite either. That crawl is
/// the effect, so it has to be the firmware's crawl and not a reasonable one.
/// </para>
/// </summary>
internal static class Palette16
{
    public const int Entries = 16;

    /// <summary>
    /// A palette spread over four colors, the way FastLED's four-color constructor does it.
    /// <para>
    /// Three gradients rather than one - nought to five, five to ten, ten to fifteen - so the four
    /// colors land on entries 0, 5, 10 and 15 and the spacing is uneven by a third of an entry either
    /// side. The middle two stops are written twice, once as the end of one gradient and once as the
    /// start of the next, with the same value both times.
    /// </para>
    /// </summary>
    public static RgbColor[] FromFour(RgbColor first, RgbColor second, RgbColor third, RgbColor fourth)
    {
        var entries = new RgbColor[Entries];

        Gradient(entries, 0, first, 5, second);
        Gradient(entries, 5, second, 10, third);
        Gradient(entries, 10, third, 15, fourth);

        return entries;
    }

    /// <summary>
    /// Fills one run of entries with a gradient, in FastLED's fixed point rather than by interpolating.
    /// <para>
    /// The step is held as a signed 8.7 value, divided, and then doubled to make it 8.8 - which throws
    /// away the bottom bit of the step. On a five entry run that is a unit or so of drift by the far
    /// end, and it is the reason a palette built this way is not quite the palette the same four colors
    /// would give if they were interpolated properly.
    /// </para>
    /// </summary>
    private static void Gradient(
        RgbColor[] entries, int startPos, RgbColor startColor, int endPos, RgbColor endColor)
    {
        unchecked
        {
            var redStep = (short)(((endColor.R - startColor.R) << 7) / (endPos - startPos));
            var greenStep = (short)(((endColor.G - startColor.G) << 7) / (endPos - startPos));
            var blueStep = (short)(((endColor.B - startColor.B) << 7) / (endPos - startPos));

            redStep = (short)(redStep * 2);
            greenStep = (short)(greenStep * 2);
            blueStep = (short)(blueStep * 2);

            var red = (ushort)(startColor.R << 8);
            var green = (ushort)(startColor.G << 8);
            var blue = (ushort)(startColor.B << 8);

            for (int i = startPos; i <= endPos; i++)
            {
                entries[i] = new RgbColor((byte)(red >> 8), (byte)(green >> 8), (byte)(blue >> 8));

                red = (ushort)(red + redStep);
                green = (ushort)(green + greenStep);
                blue = (ushort)(blue + blueStep);
            }
        }
    }

    /// <summary>
    /// Nudges <paramref name="current"/> toward <paramref name="target"/> by at most
    /// <paramref name="maxChanges"/> single steps, FastLED's <c>nblendPaletteTowardPalette</c>.
    /// <para>
    /// Channel by channel across the whole palette, one unit at a time, stopping as soon as the budget
    /// is spent - so it works through the entries in order and the early ones converge first. With 48
    /// changes allowed against 48 channels it gets almost all the way round each frame, which is why
    /// the crawl takes a couple of hundred frames rather than thousands.
    /// </para>
    /// </summary>
    public static void BlendToward(RgbColor[] current, RgbColor[] target, int maxChanges)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        byte[] from = Channels(current);
        byte[] to = Channels(target);
        int changes = 0;

        for (int i = 0; i < from.Length; i++)
        {
            if (from[i] == to[i])
            {
                continue;
            }

            if (from[i] < to[i])
            {
                from[i]++;
                changes++;
            }
            else
            {
                // FastLED takes two steps here when the gap is still wide - except that the check is a
                // signed comparison of a gap that has just been closed toward zero from above, so it is
                // never true and the second step never happens. Left as a single step, which is what the
                // code does rather than what it reads as.
                from[i]--;
                changes++;
            }

            if (changes >= maxChanges)
            {
                break;
            }
        }

        for (int i = 0; i < current.Length; i++)
        {
            current[i] = new RgbColor(from[i * 3], from[(i * 3) + 1], from[(i * 3) + 2]);
        }
    }

    private static byte[] Channels(RgbColor[] palette)
    {
        var channels = new byte[palette.Length * 3];

        for (int i = 0; i < palette.Length; i++)
        {
            channels[i * 3] = palette[i].R;
            channels[(i * 3) + 1] = palette[i].G;
            channels[(i * 3) + 2] = palette[i].B;
        }

        return channels;
    }
}
