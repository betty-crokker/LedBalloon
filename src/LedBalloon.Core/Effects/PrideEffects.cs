using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Slow waves of saturated color drifting along the run, built from hue rather than from a palette.
/// <para>
/// The same five-sine machinery as Colorwaves, and the difference between them is only where the
/// color comes from: this builds it from a hue directly, so it is always a full rainbow whatever
/// the palette is set to, and it desaturates slightly as it goes. Colorwaves folds its hue back on
/// itself and looks the color up in the palette instead.
/// </para>
/// <para>
/// Blended a quarter at a time into the frame before, against Colorwaves' half, so the bands smear
/// further and it reads as softer.
/// </para>
/// </summary>
public sealed class Pride2015Effect : IWledEffect
{
    public string Name => "Pride 2015";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint duration = 10u + segment.Speed;

        uint pseudotime = segment.Step;
        uint hueBase = segment.Aux0;

        // Never quite fully saturated, and never the same amount twice: this is what keeps it from
        // looking like a test pattern.
        var saturation = (byte)FastLed.BeatSin88(87, 220, 250, now);

        ushort brightDepth = FastLed.BeatSin88(341, 96, 224, now);
        ushort brightnessThetaInc = FastLed.BeatSin88(203, 25 * 256, 40 * 256, now);
        ushort msMultiplier = FastLed.BeatSin88(147, 23, 60, now);

        uint hue = hueBase;

        // A far wider range than Colorwaves uses, so it runs from one slow wash of color to many
        // tight bands rather than staying at one scale.
        uint hueInc = FastLed.BeatSin88(113, 1, 3000, now);

        pseudotime += duration * msMultiplier;
        hueBase += duration * FastLed.BeatSin88(400, 5, 9, now);
        uint brightnessTheta = pseudotime;

        for (int i = 0; i < segment.Length; i++)
        {
            hue += hueInc;
            var hue8 = (byte)(hue >> 8);

            brightnessTheta += brightnessThetaInc;

            unchecked
            {
                var wave = (uint)(FastLed.Sin16((ushort)brightnessTheta) + 32768);
                uint squared = wave * wave / 65536;

                var brightness = (byte)((squared * brightDepth / 65536) + (255 - brightDepth));

                RgbColor wanted = FastLed.Hsv2Rgb(hue8, saturation, brightness);
                segment.Pixels[i] = EffectSegment.Blend(segment.Pixels[i], wanted, 64);
            }
        }

        segment.Step = pseudotime;
        segment.Aux0 = hueBase;
    }
}

/// <summary>
/// A three-stage wipe: color 1 sweeps over color 2, then color 2 over the palette, then color 1
/// again from the other end.
/// <para>
/// Nothing is remembered between frames — the whole pattern is a function of the clock, so it
/// cannot drift or need settling. The cycle is slow by default: a full turn at the middle speed
/// takes 26 seconds, and only at the top of the slider does it come down to one.
/// </para>
/// </summary>
public sealed class TriWipeEffect : IWledEffect
{
    public string Name => "Tri Wipe";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint cycleTime = 1000u + ((255u - segment.Speed) * 200u);
        uint into = now % cycleTime;
        uint progress = into * 65535 / cycleTime;

        var led = (int)((progress * (uint)segment.Length * 3) >> 16);

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap, colorSlot: 2);
        }

        if (led < segment.Length)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                segment.Pixels[i] = i > led ? segment.Colors[0] : segment.Colors[1];
            }
        }
        else if (led < segment.Length * 2)
        {
            for (int i = led - segment.Length + 1; i < segment.Length; i++)
            {
                segment.Pixels[i] = segment.Colors[1];
            }
        }
        else
        {
            int offset = led - (segment.Length * 2);

            for (int i = 0; i <= offset && i < segment.Length; i++)
            {
                segment.Pixels[i] = segment.Colors[0];
            }
        }
    }
}
