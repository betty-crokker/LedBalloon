using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Slow bands of palette color drifting along the run, each swelling and dimming on its own.
/// <para>
/// Five independent slow sines drive it — how fast the hue walks, how fast the brightness wave
/// travels, how deep the dimming goes, and the pace of the whole thing — all at tempos under two
/// beats a minute. Nothing lines up with anything else, which is why it never looks like it is
/// repeating even though every part of it is a sine.
/// </para>
/// <para>
/// Each frame is blended half and half into the one before rather than replacing it. That is what
/// stops the bands from stepping: at 42 frames a second the strip is always showing a smear of the
/// last few frames, and taking that out makes it visibly chop.
/// </para>
/// </summary>
public sealed class ColorwavesEffect : IWledEffect
{
    public string Name => "Colorwaves";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint duration = 10u + segment.Speed;

        // Carried between frames: how far the brightness wave has travelled, and where the hue
        // walk had got to. Without these the pattern restarts every frame and sits still.
        uint pseudotime = segment.Step;
        uint hueBase = segment.Aux0;

        ushort brightDepth = FastLed.BeatSin88(341, 96, 224, now);
        ushort brightnessThetaInc = FastLed.BeatSin88(203, 25 * 256, 40 * 256, now);
        ushort msMultiplier = FastLed.BeatSin88(147, 23, 60, now);

        uint hue = hueBase;

        // The intensity slider stretches or compresses the hue walk, which is what decides how many
        // bands fit along the run.
        uint hueInc = (uint)FastLed.BeatSin88(113, 60, 300, now) * segment.Intensity * 10 / 255;

        pseudotime += duration * msMultiplier;
        hueBase += duration * FastLed.BeatSin88(400, 5, 9, now);
        uint brightnessTheta = pseudotime;

        for (int i = 0; i < segment.Length; i++)
        {
            hue += hueInc;

            // A triangle fold rather than a wrap, so the hue walks up the palette and back down
            // instead of snapping from the far end to the near one. This is the only thing that
            // separates Colorwaves from Pride 2015.
            uint folded = (hue >> 7) & 0x1FF;
            var hue8 = (byte)((folded & 0x100) != 0 ? 255 - (folded >> 1) : folded >> 1);

            brightnessTheta += brightnessThetaInc;

            unchecked
            {
                var wave = (uint)(FastLed.Sin16((ushort)brightnessTheta) + 32768);

                // Squared, so the bands spend longer dim than bright. A plain sine here reads as
                // the whole run pulsing rather than as separate waves passing along it.
                uint squared = wave * wave / 65536;

                var brightness = (byte)(squared * brightDepth / 65536 + (255 - brightDepth));

                RgbColor wanted = segment.ColorFromPalette(
                    hue8,
                    mapping: false,
                    wrap: segment.SolidWrap,
                    colorSlot: 0,
                    brightness: brightness);

                segment.Pixels[i] = EffectSegment.Blend(segment.Pixels[i], wanted, 128);
            }
        }

        segment.Step = pseudotime;
        segment.Aux0 = hueBase;
    }
}
