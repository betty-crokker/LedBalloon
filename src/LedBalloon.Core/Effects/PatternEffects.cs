using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Alternating runs of lit and unlit, standing still.
/// <para>
/// The two sliders are lengths rather than speeds: how many LEDs are lit, then how many are not.
/// Nothing moves, which makes it the one effect that reads as decoration rather than animation —
/// evenly spaced bulbs along a roofline, with the gaps set to whatever the fixture spacing wants.
/// </para>
/// </summary>
public sealed class StaticPatternEffect : IWledEffect
{
    public string Name => "Solid Pattern";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int lit = 1 + segment.Speed;
        int unlit = 1 + segment.Intensity;

        bool drawingLit = true;
        int count = 0;

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = drawingLit
                ? segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap)
                : segment.Colors[1];

            if (++count >= (drawingLit ? lit : unlit))
            {
                count = 0;
                drawingLit = !drawingLit;
            }
        }
    }
}

/// <summary>
/// The three color slots in repeating blocks, standing still.
/// <para>
/// Only the intensity slider does anything, and it sets the block length — a third of it, rounded
/// up, so the slider spans one to eight LEDs a block rather than the whole run.
/// </para>
/// </summary>
public sealed class TriStaticPatternEffect : IWledEffect
{
    public string Name => "Solid Pattern Tri";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int blockSize = (segment.Intensity >> 5) + 1;
        int block = 0;
        int count = 0;

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.Colors[block % 3];

            if (++count >= blockSize)
            {
                block++;
                count = 0;
            }
        }
    }
}

/// <summary>
/// Two standing waves of different wavelengths crossing each other, lit where they add up.
/// <para>
/// The wavelengths are fixed and deliberately not multiples of each other, so where the crests
/// meet moves slowly along the run without anything actually travelling. A third slow wave sets
/// the waterline: everything below it is dark, so the lit patches appear and disappear rather
/// than dimming in place.
/// </para>
/// </summary>
public sealed class LakeEffect : IWledEffect
{
    public string Name => "Lake";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var speed = (byte)(segment.Speed / 10);

        // The first two are written as -64 to 64 in the original, which in byte arithmetic is 192
        // to 64 and wraps through zero on the way. Kept as the wrap rather than "fixed" to a
        // forward range, because the wrap is where the crossing pattern comes from.
        byte wave1 = FastLed.BeatSin8((byte)(speed + 2), unchecked((byte)-64), 64, now);
        byte wave2 = FastLed.BeatSin8((byte)(speed + 1), unchecked((byte)-64), 64, now);
        byte waterline = FastLed.BeatSin8((byte)(speed + 2), 0, 80, now);

        for (int i = 0; i < segment.Length; i++)
        {
            int index = (FastLed.Cos8((byte)((i * 15) + wave1)) / 2)
                      + (FastLed.CubicWave8((byte)((i * 23) + wave2)) / 2);

            var lum = (byte)(index > waterline ? index - waterline : 0);

            segment.Pixels[i] = segment.ColorFromPalette(
                index, mapping: false, wrap: false, colorSlot: 0, brightness: lum);
        }
    }
}

/// <summary>
/// The whole run pulsing twice in quick succession, then resting: lub-dub, lub-dub.
/// <para>
/// Two beats rather than one is the whole point, and the second comes a third of the way through
/// the cycle rather than halfway, which is what stops it reading as a metronome. Between beats the
/// brightness decays toward the background by a fixed ratio each frame, so it falls away quickly
/// at first and then lingers.
/// </para>
/// </summary>
public sealed class HeartbeatEffect : IWledEffect
{
    public string Name => "Heartbeat";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int bpm = 40 + (segment.Speed >> 3);
        uint msPerBeat = (uint)(60000 / bpm);
        uint secondBeat = msPerBeat / 3;

        uint sinceBeat = now - segment.Step;

        // The decay. Intensity makes the denominator bigger, which makes each frame keep less of
        // the last, so a higher setting falls away faster rather than beating faster.
        uint level = segment.Aux1 * 2042 / (2048u + segment.Intensity);
        segment.Aux1 = level;

        if (sinceBeat > secondBeat && segment.Aux0 == 0)
        {
            segment.Aux1 = ushort.MaxValue;
            segment.Aux0 = 1;
        }

        if (sinceBeat > msPerBeat)
        {
            segment.Aux1 = ushort.MaxValue;
            segment.Aux0 = 0;
            segment.Step = now;
        }

        var toBackground = (byte)(255 - (segment.Aux1 >> 8));

        for (int i = 0; i < segment.Length; i++)
        {
            RgbColor lit = segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap);
            segment.Pixels[i] = EffectSegment.Blend(lit, segment.Colors[1], toBackground);
        }
    }
}
