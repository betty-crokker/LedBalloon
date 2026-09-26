using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A dot swinging back and forth on a sine, leaving a fading trail - FastLED's own example, and
/// three of WLED's effects.
/// <para>
/// The dot's position is a sine of the clock rather than a walk, so it slows at the ends and hurries
/// through the middle. Because the swing can carry it several LEDs in one frame, every LED it passed
/// over is painted rather than only the one it landed on; otherwise a fast swing would draw a dotted
/// line instead of a streak.
/// </para>
/// <para>
/// Intensity is the trail, and it fades toward the <em>secondary color</em> rather than toward
/// black - so on a blue secondary the whole run sits in blue with a red streak running through it,
/// and every LED is lit all the time. Worth stating because the sibling effect three classes down
/// does fade to black, and using the wrong one here left the run reading as a dot on nothing.
/// </para>
/// </summary>
internal static class Sinelon
{
    public static void Render(EffectSegment segment, uint now, bool dual, bool rainbow = false)
    {
        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        segment.FadeOut(segment.Intensity);

        // Speed divided by ten, so the whole slider spans about 25 beats a minute - anything faster
        // and the dot would cross in less than a frame.
        int at = FastLed.BeatSin16(
            (ushort)(segment.Speed / 10), 0, (ushort)(segment.Length - 1), now);

        if (segment.Call == 0)
        {
            segment.Aux0 = (uint)at;
        }

        RgbColor color = rainbow
            ? segment.ColorWheel((byte)((at & 0x07) * 32))
            : segment.ColorFromPalette(at, mapping: true, wrap: false);

        RgbColor other = segment.Colors[2];

        segment.Pixels[at] = color;

        if (dual)
        {
            // The third slot colors the mirror; unset, both dots take the palette, and on the
            // rainbow variant the mirror always matches.
            if (other == RgbColor.Black)
            {
                other = segment.ColorFromPalette(at, mapping: true, wrap: false);
            }

            if (rainbow)
            {
                other = color;
            }

            segment.SetPixel(segment.Length - 1 - at, other);
        }

        if (segment.Aux0 == at)
        {
            return;
        }

        // Fill in everything between where it was and where it is, so the streak stays solid.
        int from = (int)segment.Aux0;
        int step = from < at ? 1 : -1;

        for (int i = from; i != at; i += step)
        {
            segment.SetPixel(i, color);

            if (dual)
            {
                segment.SetPixel(segment.Length - 1 - i, other);
            }
        }

        segment.Aux0 = (uint)at;
    }
}

/// <summary>One dot swinging back and forth with a trail.</summary>
public sealed class SinelonEffect : IWledEffect
{
    public string Name => "Sinelon";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Sinelon.Render(segment, now, dual: false);
    }
}

/// <summary>The same dot with a mirror image swinging the other way.</summary>
public sealed class SinelonDualEffect : IWledEffect
{
    public string Name => "Sinelon Dual";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Sinelon.Render(segment, now, dual: true);
    }
}

/// <summary>
/// The dot taking its color from the wheel instead of the palette, in eight steps rather than
/// smoothly - the position's low three bits pick the hue, so the color changes as it travels and
/// repeats every eight LEDs.
/// </summary>
public sealed class SinelonRainbowEffect : IWledEffect
{
    public string Name => "Sinelon Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Sinelon.Render(segment, now, dual: false, rainbow: true);
    }
}

/// <summary>
/// Eight dots on eight different sines, weaving through each other.
/// <para>
/// Each dot has its own tempo - <c>(16 + speed) * (n + 7)</c> beats a minute - so they drift in and
/// out of step and never settle into a pattern. Where two land on the same LED their colors combine
/// channel by channel, taking the brighter of each, which is why a crossing flares rather than one
/// dot simply hiding the other.
/// </para>
/// </summary>
public sealed class JuggleEffect : IWledEffect
{
    public string Name => "Juggle";

    /// <summary>How many dots, which is not adjustable.</summary>
    private const int Dots = 8;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        // Intensity is the trail, and it runs backwards: turning it up fades less.
        segment.FadeToBlack((byte)(192 - (3 * segment.Intensity / 4)));

        byte hue = 0;

        for (int i = 0; i < Dots; i++)
        {
            int at = FastLed.BeatSin88(
                (ushort)((16 + segment.Speed) * (i + 7)), 0, (ushort)(segment.Length - 1), now);

            RgbColor dot = segment.PaletteId == 0
                ? FastLed.Hsv2Rgb(hue, 220, 255)
                : segment.ColorFromPalette(hue, mapping: false, wrap: true);

            RgbColor already = segment.Pixels[at];

            // Channel-wise brighter of the two, which is what FastLED's |= on a color does.
            segment.Pixels[at] = new RgbColor(
                Math.Max(already.R, dot.R),
                Math.Max(already.G, dot.G),
                Math.Max(already.B, dot.B));

            hue += 32;
        }
    }
}
