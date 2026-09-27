using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A candle flame: brightness wandering toward a fresh random target, over and over.
/// <para>
/// Not a random brightness each frame, which reads as noise. It picks a target, walks toward it a
/// fixed step at a time, and picks another when it arrives - so the flicker has a speed as well as a
/// depth, and the step is sized from the distance so a big change takes no longer than a small one.
/// </para>
/// <para>
/// The sliders are not what they look like. Intensity is the depth of the flicker, and it is the one
/// to turn <em>up</em> for a candle: it sets how far below full the brightness can wander. Speed
/// picks one of four step divisors rather than scaling smoothly, and the comment in WLED's source
/// says why - values near 100 give the roughly 5 Hz flicker that reads as a flame.
/// </para>
/// <para>
/// And it asks to be drawn at a fixed 23 ms whatever the controller could manage, because those step
/// divisors assume it. On this hardware that is four times slower than the strip's own rate; drawn
/// every frame the flame would gutter like a fire alarm.
/// </para>
/// </summary>
internal static class Candle
{
    public static void Render(EffectSegment segment, bool multi)
    {
        int depth = segment.Intensity;
        int spread = depth >> 1;

        // Four steps rather than a scale, which is how WLED gets a usable range out of a slider that
        // is really choosing a character.
        int divisor = segment.Speed switch
        {
            > 252 => 1,
            > 99 => 2,
            > 49 => 3,
            _ => 4,
        };

        bool separate = multi && segment.Length > 1;
        int flames = separate ? segment.Length : 1;

        Flame[] state = segment.Scratch(() => NewFlames(segment.Length));

        for (int i = 0; i < flames; i++)
        {
            // The first flame lives in the segment's own scratch fields, as WLED's does, and the
            // rest in the array - which is why a multi candle and a single one agree on LED zero.
            ref Flame flame = ref state[i];

            if (flame.Step == 0)
            {
                flame.Level = 128;
                flame.Target = (byte)(130 + segment.Random8(4));
                flame.Step = 1;
            }

            bool arrived;

            if (flame.Target > flame.Level)
            {
                flame.Level = FastLed.QAdd8(flame.Level, flame.Step);
                arrived = flame.Level >= flame.Target;
            }
            else
            {
                flame.Level = FastLed.QSub8(flame.Level, flame.Step);
                arrived = flame.Level <= flame.Target;
            }

            if (arrived)
            {
                // Two draws added together rather than one, which bunches the targets toward the
                // middle instead of spreading them evenly - a flame that rarely goes right out.
                int target = segment.Random8(spread) + segment.Random8(spread);

                if (target < spread >> 1)
                {
                    target = (spread >> 1) + segment.Random8(spread);
                }

                target += 255 - depth;

                int distance = Math.Abs(target - flame.Level);

                flame.Target = (byte)Math.Min(255, target);
                flame.Step = (byte)Math.Max(1, distance >> divisor);
            }

            if (separate && i > 0)
            {
                segment.SetPixel(i, EffectSegment.Blend(
                    segment.Colors[1],
                    segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap),
                    flame.Level));

                continue;
            }

            // The first flame lights the whole run on a single candle, and LED zero on a multi one.
            int upTo = separate ? 1 : segment.Length;

            for (int j = 0; j < upTo; j++)
            {
                segment.Pixels[j] = EffectSegment.Blend(
                    segment.Colors[1],
                    segment.ColorFromPalette(j, mapping: true, wrap: segment.SolidWrap),
                    flame.Level);
            }
        }

        segment.FrameDelay = FrameTime.FixedFrameDelay;
    }

    private static Flame[] NewFlames(int length) => new Flame[Math.Max(1, length)];

    /// <summary>One flame's wandering: where it is, where it is going, and how fast.</summary>
    private struct Flame
    {
        public byte Level;
        public byte Target;
        public byte Step;
    }
}

/// <summary>One flame, the whole run flickering together.</summary>
public sealed class CandleEffect : IWledEffect
{
    public string Name => "Candle";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Candle.Render(segment, multi: false);
    }
}

/// <summary>
/// A flame per LED, each wandering independently - so the run shimmers rather than pulsing as one.
/// </summary>
public sealed class CandleMultiEffect : IWledEffect
{
    public string Name => "Candle Multi";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Candle.Render(segment, multi: true);
    }
}

/// <summary>
/// Cubic waves whose wavelength varies along the run, which is where Phased and Phased Noise both
/// come from.
/// </summary>
internal static class Phased
{
    /// <summary>The base frequency, which is not adjustable.</summary>
    private const int Frequency = 16;

    /// <param name="noisy">
    /// True to take the modulus from Perlin noise per LED instead of holding it at five, which is the
    /// whole of what Phased Noise adds: the groups that drift apart from each other stop being evenly
    /// sized, so the beating is irregular rather than periodic.
    /// </param>
    public static void Render(EffectSegment segment, uint now, bool noisy)
    {
        double[] phase = segment.Scratch(() => new double[1]);

        int cutOff = 255 - segment.Intensity;
        int modulus = 5;

        phase[0] += segment.Speed / 32.0;

        uint index = now / 64;

        for (int i = 0; i < segment.Length; i++)
        {
            if (noisy)
            {
                // The doubled term is in the source as written - i*10 + i*10 - so the noise is
                // sampled every twenty units along, not ten.
                modulus = Perlin.Noise8((ushort)((i * 10) + (i * 10))) / 16;
            }

            var val = (uint)((i + 1) * Frequency);

            // Zero would divide by nothing, and a run long enough reaches it.
            val += (uint)(phase[0] * ((i % Math.Max(1, modulus)) + 1) / 2);

            byte b = FastLed.CubicWave8((byte)val);
            b = b > cutOff ? (byte)(b - cutOff) : (byte)0;

            segment.Pixels[i] = EffectSegment.Blend(
                segment.Colors[1],
                segment.ColorFromPalette((int)index, mapping: false, wrap: false),
                b);

            index += (uint)(256 / segment.Length);

            // Without this a run longer than the palette would never get past its first entry.
            if (segment.Length > 256)
            {
                index++;
            }
        }
    }
}

/// <summary>
/// Sine waves whose wavelength varies along the run, so they beat against each other.
/// <para>
/// Each LED reads a cubic wave at its own frequency, and the phase advance each LED gets is divided
/// by its position modulo five - so groups of five LEDs drift apart from each other and the pattern
/// never repeats cleanly. Intensity is a cutoff: it clips the bottom off the wave, so turning it
/// down leaves fewer and narrower bands lit.
/// </para>
/// <para>
/// The phase advances per frame rather than per millisecond, which makes this one of the few effects
/// whose speed depends on how fast the controller draws. The color, by contrast, rotates on the
/// clock.
/// </para>
/// </summary>
public sealed class PhasedEffect : IWledEffect
{
    public string Name => "Phased";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Phased.Render(segment, now, noisy: false);
    }
}

/// <summary>
/// The same waves with their grouping taken from Perlin noise rather than a fixed five, so the
/// beating is irregular - patches of the run drift together and others pull apart.
/// </summary>
public sealed class PhasedNoiseEffect : IWledEffect
{
    public string Name => "Phased Noise";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Phased.Render(segment, now, noisy: true);
    }
}
