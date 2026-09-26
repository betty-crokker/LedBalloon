using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Sparks of white scattered over a background: the palette sliding along the run, or a flat color.
/// <para>
/// One spark a frame at most, and only if the dice allow - intensity is the odds rather than a
/// count, so the run twinkles rather than fizzing. The spark takes the third color slot when one is
/// set and is white otherwise.
/// </para>
/// <para>
/// Glitter is the reason <see cref="EffectSegment.PaletteBlend"/> is a number rather than a pair of
/// flags. It reads the setting directly to decide whether to cut the gradient short, and it asks for
/// the gradient even on palette Default by passing a color slot that does not exist - which is WLED's
/// own way of saying so.
/// </para>
/// </summary>
internal static class Glitter
{
    /// <summary>One spark, or none. The odds are the slider; the count never is.</summary>
    public static void Sparkle(EffectSegment segment, byte odds, RgbColor color)
    {
        if (odds > segment.Random8())
        {
            segment.SetPixel(segment.Random16(segment.Length), color);
        }
    }

    /// <summary>The spark's color: the third slot when set, and white when not.</summary>
    public static RgbColor SparkColor(EffectSegment segment) =>
        segment.Colors[2] == RgbColor.Black ? RgbColor.White : segment.Colors[2];
}

/// <summary>The palette sliding along the run, with sparks over it.</summary>
public sealed class GlitterEffect : IWledEffect
{
    public string Name => "Glitter";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (!segment.Option2)
        {
            uint counter = 0;

            // Speed zero holds the gradient still rather than sliding it slowly, which is what makes
            // this effect stand in for the old "Solid glitter" as well.
            if (segment.Speed != 0)
            {
                counter = ((now * (uint)((segment.Speed >> 3) + 1)) & 0xFFFF) >> 8;
            }

            bool noWrap = segment.PaletteBlend == 2
                || (segment.PaletteBlend == 0 && segment.Speed == 0);

            for (int i = 0; i < segment.Length; i++)
            {
                uint at = unchecked((uint)(i * 255 / segment.Length) - counter);

                if (noWrap)
                {
                    at = (uint)FastLed.Map((int)(at & 0xFF), 0, 255, 0, 240);
                }

                // Color slot 255 is not a slot: it is how this effect asks for the gradient even
                // when the segment is on palette Default.
                segment.Pixels[i] = segment.ColorFromPalette(
                    (int)at, mapping: false, wrap: true, colorSlot: 255);
            }
        }

        Glitter.Sparkle(segment, segment.Intensity, Glitter.SparkColor(segment));
    }
}

/// <summary>One flat color with sparks over it.</summary>
public sealed class SolidGlitterEffect : IWledEffect
{
    public string Name => "Solid Glitter";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        segment.Fill(segment.Colors[0]);
        Glitter.Sparkle(segment, segment.Intensity, Glitter.SparkColor(segment));
    }
}

/// <summary>
/// Three bars of the three color slots bouncing off the ends of the run and passing through each
/// other.
/// <para>
/// One of only two effects WLED ships no metadata for, which means its UI shows every control
/// whether or not it does anything. Both sliders do: speed is how fast the bars move and intensity
/// is how wide they are.
/// </para>
/// <para>
/// Where two bars overlap their colors mix half and half, so a crossing is a third color rather than
/// one bar hiding the other. Each bounce picks a new random step, so the three drift out of phase
/// and the pattern never settles.
/// </para>
/// </summary>
public sealed class OscillateEffect : IWledEffect
{
    public string Name => "Oscillate";

    private const int Bars = 3;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Bar[] bars = segment.Scratch(() => Start(segment.Length));

        uint cycleTime = 20u + (2u * (255u - segment.Speed));
        uint tick = now / cycleTime;

        for (int i = 0; i < Bars; i++)
        {
            ref Bar bar = ref bars[i];

            if (tick != segment.Step)
            {
                bar.Position = (ushort)(bar.Position + (bar.Direction * bar.Speed));
            }

            bar.Size = segment.Length / (3 + (segment.Intensity / 8));

            // Deliberately relying on the position being unsigned: a bar that steps off the near end
            // wraps round to a huge number, which is how the firmware notices it went past zero.
            if (bar.Direction == -1 && bar.Position > segment.Length << 1)
            {
                bar.Position = 0;
                bar.Direction = 1;
                bar.Speed = NewStep(segment);
            }

            if (bar.Direction == 1 && bar.Position >= segment.Length - 1)
            {
                bar.Position = (ushort)(segment.Length - 1);
                bar.Direction = -1;
                bar.Speed = NewStep(segment);
            }
        }

        segment.Step = tick;

        for (int i = 0; i < segment.Length; i++)
        {
            RgbColor color = RgbColor.Black;
            bool lit = false;

            for (int j = 0; j < Bars; j++)
            {
                if (i < bars[j].Position - bars[j].Size || i > bars[j].Position + bars[j].Size)
                {
                    continue;
                }

                color = lit
                    ? EffectSegment.Blend(color, segment.Colors[j], 128)
                    : segment.Colors[j];

                lit = true;
            }

            segment.Pixels[i] = color;
        }
    }

    /// <summary>Bigger steps at higher speeds, so the slider changes the character and not just the rate.</summary>
    private static byte NewStep(EffectSegment segment) =>
        segment.Speed > 100 ? segment.Random8(2, 4) : segment.Random8(1, 3);

    /// <summary>
    /// Two bars a quarter and three quarters along heading the same way, and one in the middle
    /// heading the other - which is what makes them cross rather than travel together.
    /// </summary>
    private static Bar[] Start(int length) =>
    [
        new Bar { Position = (ushort)(length / 4), Size = length / 8, Direction = 1, Speed = 1 },
        new Bar { Position = (ushort)(length / 4 * 3), Size = length / 8, Direction = 1, Speed = 2 },
        new Bar { Position = (ushort)(length / 4 * 2), Size = length / 8, Direction = -1, Speed = 1 },
    ];

    private struct Bar
    {
        public ushort Position;
        public int Size;
        public int Direction;
        public byte Speed;
    }
}
