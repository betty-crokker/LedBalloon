using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A wave of the palette travelling along the run over the secondary color, which is where Running,
/// Running Dual and Saw all come from.
/// <para>
/// Each LED reads a sine at its own place in the wave, so the whole run is one standing waveform
/// sliding along rather than a block being moved. Intensity is the wavelength - it divides the
/// spacing between LEDs, so turning it down stretches the wave out until at zero the whole run
/// pulses together.
/// </para>
/// <para>
/// The three differ only in what shapes the wave. Running uses a plain sine, so light never quite
/// leaves the run. Saw replaces it with a ramp that rises over a sixteenth of the cycle and falls
/// over the rest, which reads as a tide. Running Dual uses a sine with a gap - flat zero for half
/// its period - so its two waves are separate pulses crossing rather than one wash.
/// </para>
/// </summary>
internal static class Running
{
    public static void Render(EffectSegment segment, uint now, bool saw, bool dual = false)
    {
        int scale = segment.Intensity >> 2;
        uint counter = (now * segment.Speed) >> 9;

        for (int i = 0; i < segment.Length; i++)
        {
            // Deliberately unsigned and allowed to wrap, which is what carries the wave along.
            uint a = unchecked((uint)(i * scale) - counter);

            if (saw)
            {
                a &= 0xFF;

                // Up steeply over a sixteenth of the cycle, down gently over the rest, then turned
                // upside down - so the wave breaks rather than swells.
                a = a < 16 ? 192 + (a * 8) : (uint)FastLed.Map((int)a, 16, 255, 64, 192);
                a = 255 - a;
            }

            byte s = dual ? FastLed.SinGap((ushort)a) : FastLed.Sin8((byte)a);

            RgbColor color = EffectSegment.Blend(
                segment.Colors[1],
                segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap),
                s);

            if (dual)
            {
                uint b = unchecked((uint)((segment.Length - 1 - i) * scale) - counter);

                RgbColor other = EffectSegment.Blend(
                    segment.Colors[1],
                    segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap, colorSlot: 2),
                    FastLed.SinGap((ushort)b));

                // Half and half, so where the two pulses cross they add up to one brighter one
                // rather than one hiding the other.
                color = EffectSegment.Blend(color, other, 127);
            }

            segment.Pixels[i] = color;
        }
    }
}

/// <summary>A smooth wave of the palette travelling along the run.</summary>
public sealed class RunningLightsEffect : IWledEffect
{
    public string Name => "Running";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Running.Render(segment, now, saw: false);
    }
}

/// <summary>
/// Two waves travelling in opposite directions, the second one colored from the third color slot.
/// </summary>
public sealed class RunningDualEffect : IWledEffect
{
    public string Name => "Running Dual";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Running.Render(segment, now, saw: false, dual: true);
    }
}

/// <summary>The same wave with a sawtooth profile, so it breaks rather than swells.</summary>
public sealed class SawEffect : IWledEffect
{
    public string Name => "Saw";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Running.Render(segment, now, saw: true);
    }
}
