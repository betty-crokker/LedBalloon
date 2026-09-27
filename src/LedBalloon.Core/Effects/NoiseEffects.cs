using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The palette sampled by Perlin noise along the run, drifting.
/// <para>
/// Both coordinates move with position, so the run is a diagonal slice through the noise field
/// rather than a straight line across it - which is what gives it patches that grow and shrink
/// rather than bands that slide. Speed moves the slice, slowly and unevenly: the step grows by a
/// sine of the speed each frame, between one and six.
/// </para>
/// <para>
/// This is the effect that let the noise function be checked exactly rather than statistically. Its
/// output is nothing but the noise and the palette, so solving a captured frame for its one unknown -
/// the accumulated step - either reproduces the strip to the LED or does not.
/// </para>
/// </summary>
public sealed class FillNoiseEffect : IWledEffect
{
    public string Name => "Fill Noise";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Call == 0)
        {
            segment.Step = segment.Random16(12345);
        }

        for (int i = 0; i < segment.Length; i++)
        {
            byte index = Perlin.Noise8(
                (ushort)(i * segment.Length),
                (ushort)(segment.Step + (uint)(i * segment.Length)));

            segment.Pixels[i] = segment.ColorFromPalette(index, wrap: segment.SolidWrap);
        }

        segment.Step += FastLed.BeatSin8(segment.Speed, 1, 6, now);
    }
}

/// <summary>
/// A fire burning up the run: heat that cools, drifts along and is fed by sparks at one end.
/// <para>
/// Not a function of the clock at all - it carries a temperature per LED and works the next frame
/// out from the last, the way a simulation does. Cooling, drift and ignition each happen once every
/// 32 ms however fast the controller draws; frames in between only cool a little, so the fire looks
/// the same on a fast strip as on a slow one.
/// </para>
/// <para>
/// The sliders are named for the model rather than for what they look like. Speed is how fast heat
/// cools, so turning it up makes the fire <em>shorter</em>; intensity is how often a spark lights.
/// The bottom tenth of the run is an ignition area held above black, which is why a fire never quite
/// goes out at its base.
/// </para>
/// </summary>
public sealed class Fire2012Effect : IWledEffect
{
    public string Name => "Fire 2012";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        byte[] heat = segment.Scratch(() => new byte[segment.Length]);

        uint tick = now >> 5;
        bool settling = tick != segment.Step;

        int ignition = Math.Max(3, segment.Length / 10);

        // Step one: everything cools. Hard on the frame that starts a new tick and barely at all on
        // the frames between, which is what keeps the fire's pace off the frame rate.
        for (int i = 0; i < segment.Length; i++)
        {
            byte cool = settling
                ? segment.Random8((((20 + (segment.Speed / 3)) * 16) / segment.Length) + 2)
                : segment.Random8(4);

            int floor = i < ignition ? ((ignition - i) / 4) + 16 : 0;

            byte cooled = FastLed.QSub8(heat[i], cool);
            heat[i] = cooled < floor ? (byte)floor : cooled;
        }

        if (settling)
        {
            // Step two: heat drifts along the run, two parts from the LED two back and one from the
            // LED behind - so it rises faster than it spreads.
            for (int k = segment.Length - 1; k > 1; k--)
            {
                heat[k] = (byte)((heat[k - 1] + (heat[k - 2] << 1)) / 3);
            }

            // Step three: a spark, sometimes, low down. Lower sparks are given more heat, so the
            // flame has a base rather than a scatter of embers.
            if (segment.Random8() <= segment.Intensity)
            {
                int at = segment.Random8(ignition);
                int boost = (17 + segment.Custom3) * (ignition - (at / 2)) / ignition;

                heat[at] = FastLed.QAdd8(heat[at], segment.Random8(96 + (2 * boost), 207 + boost));
            }
        }

        // Step four: temperature to color, and deliberately without blending - the palette's own
        // steps are the flame's bands.
        //
        // Looked up straight rather than through the segment, which is what the source does: no
        // cutting the index short of the palette's end, because the clamp to 240 above is already
        // doing that job and doing it twice would lose the top of the flame.
        for (int j = 0; j < segment.Length; j++)
        {
            segment.Pixels[j] = segment.ColorFromPalette(
                Math.Min(heat[j], (byte)240), wrap: true, blend: false);
        }

        if (settling)
        {
            segment.Step = tick;
        }
    }
}
