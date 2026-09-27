using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// One flat color. WLED's effect zero, and the one most presets spend most of their runs on.
/// <para>
/// The only effect that asks to be drawn slowly on purpose: a third of a second between frames,
/// since nothing about it changes. It makes no difference to what is on the wall and every
/// difference to what the controller has time for.
/// </para>
/// </summary>
public sealed class SolidEffect : IWledEffect
{
    /// <summary>What WLED gives a static effect, barring bus types that must be refreshed.</summary>
    public const int RefreshMilliseconds = 350;

    public string Name => "Solid";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        segment.Fill(segment.Colors[0]);
        segment.FrameDelay = RefreshMilliseconds;
    }
}

/// <summary>
/// On, then off, then on again - which is four of WLED's effects depending on what "on" means and
/// how long it lasts.
/// <para>
/// Worth porting for what it is not: unlike most effects this one measures itself in milliseconds
/// rather than in sine waves, and its speed runs <em>backwards</em> - the cycle is
/// <c>(255 - speed) * 20</c> ms, so turning speed up shortens it. The palette-slide stand-in had no
/// way to express either of those and drew a blinking run as a gentle wash.
/// </para>
/// <para>
/// A strobe is the same thing with the duty cycle taken away: on for one <c>FRAMETIME</c> and off
/// for the rest, however long the cycle. And the rainbow variants take the lit color from the frame
/// count rather than from a slot, so they walk the wheel a step per frame drawn.
/// </para>
/// </summary>
internal static class Blinking
{
    public static void Render(
        EffectSegment segment, uint now, RgbColor lit, RgbColor unlit, bool strobe, bool usePalette)
    {
        var frame = (uint)segment.FrameTime;

        uint cycleTime = (uint)(255 - segment.Speed) * 20;

        // Intensity is the duty cycle: how much of each cycle is spent lit. A strobe has none.
        uint onTime = frame;

        if (!strobe)
        {
            onTime += (cycleTime * segment.Intensity) >> 8;
        }

        cycleTime += frame * 2;

        uint iteration = now / cycleTime;
        uint into = now % cycleTime;

        // Forced on for one frame at the start of every cycle, so a duty cycle too short to last
        // a whole frame still shows something rather than nothing.
        bool on = iteration != segment.Step || into <= onTime;
        segment.Step = iteration;

        if (!on)
        {
            segment.Fill(unlit);
            return;
        }

        if (!usePalette)
        {
            segment.Fill(lit);
            return;
        }

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap);
        }
    }

    /// <summary>The wheel position the rainbow variants blink at: one step per frame drawn.</summary>
    public static RgbColor Wheel(EffectSegment segment) =>
        segment.ColorWheel((byte)(segment.Call & 0xFF));
}

/// <summary>The palette on, the secondary color off, with intensity setting the duty cycle.</summary>
public sealed class BlinkEffect : IWledEffect
{
    public string Name => "Blink";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Blinking.Render(
            segment, now, segment.Colors[0], segment.Colors[1], strobe: false, usePalette: true);
    }
}

/// <summary>The same blink with its lit color walking the wheel instead of coming from the palette.</summary>
public sealed class BlinkRainbowEffect : IWledEffect
{
    public string Name => "Blink Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Blinking.Render(
            segment, now, Blinking.Wheel(segment), segment.Colors[1],
            strobe: false, usePalette: false);
    }
}

/// <summary>
/// A single frame of light per cycle, whatever the cycle length - which is what makes it a strobe
/// rather than a blink, and why it has no duty cycle control at all.
/// </summary>
public sealed class StrobeEffect : IWledEffect
{
    public string Name => "Strobe";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Blinking.Render(
            segment, now, segment.Colors[0], segment.Colors[1], strobe: true, usePalette: true);
    }
}

/// <summary>The same strobe flashing a different color off the wheel each time.</summary>
public sealed class StrobeRainbowEffect : IWledEffect
{
    public string Name => "Strobe Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Blinking.Render(
            segment, now, Blinking.Wheel(segment), segment.Colors[1],
            strobe: true, usePalette: false);
    }
}

/// <summary>
/// The whole run swelling and fading together.
/// <para>
/// The curve is not a sine. WLED bends one deliberately - a sine of a warped input, floored at a
/// tenth brightness - so that it dwells dark and rises quickly, which is what makes it read as
/// breathing rather than as a dimmer being turned up and down.
/// </para>
/// </summary>
public sealed class BreatheEffect : IWledEffect
{
    public string Name => "Breathe";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint counter;
        unchecked
        {
            counter = (now * (uint)((segment.Speed >> 3) + 10)) & 0xFFFF;
        }

        // Warps the input so the bright half passes faster than the dark half.
        counter = (counter >> 2) + (counter >> 4);

        int swell = 0;
        if (counter < 16384)
        {
            // Folded at the halfway point, so the curve comes back down the way it went up.
            if (counter > 8192)
            {
                counter = 8192 - (counter - 8192);
            }

            // Near enough parabolic over this stretch, and 103 keeps the peak just inside a byte.
            swell = FastLed.Sin16((ushort)counter) / 103;
        }

        var lum = (byte)Math.Clamp(30 + swell, 0, 255);

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = EffectSegment.Blend(
                segment.Colors[1],
                segment.ColorFromPalette(i, mapping: true),
                lum);
        }
    }
}

/// <summary>
/// A pulse running along the run in time with a tempo.
/// <para>
/// The only effect here that reads the speed slider as an actual musical tempo: it goes straight
/// into a beat generator as beats per minute, so 64 really is 64 beats a minute. Colors step
/// through the palette along the run while brightness rides the beat, and the two drift against
/// each other, which is what gives it its roll.
/// </para>
/// </summary>
public sealed class BpmEffect : IWledEffect
{
    public string Name => "Bpm";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint step = (now / 20) & 0xFF;
        byte beat = FastLed.BeatSin8(segment.Speed, 64, 255, now);

        for (int i = 0; i < segment.Length; i++)
        {
            byte brightness;
            unchecked
            {
                // Deliberately allowed to wrap: the fall off the top of a byte is what makes the
                // pulse repeat along the run instead of just brightening toward one end.
                brightness = (byte)(beat - step + (i * 10));
            }

            segment.Pixels[i] = segment.ColorFromPalette(
                (int)(step + (uint)(i * 2)), brightness: brightness);
        }
    }
}

/// <summary>
/// The palette sliding along the run, folded into zones that alternate direction.
/// <para>
/// Close to what the stand-in always drew, which makes it a useful check on the stand-in: a run on
/// Flow should look much the same drawn either way, and any run that does not is telling you
/// something about the other effect rather than about this one.
/// </para>
/// </summary>
public sealed class FlowEffect : IWledEffect
{
    public string Name => "Flow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint counter = 0;
        if (segment.Speed != 0)
        {
            unchecked
            {
                counter = now * (uint)((segment.Speed >> 2) + 1);
            }

            counter >>= 8;
        }

        // A zone shorter than six LEDs has nowhere to show a gradient, so that caps how many there
        // can be however high the intensity goes.
        int mostZones = segment.Length / 6;
        int zones = (segment.Intensity * mostZones) >> 8;

        // Even, so that the alternating directions pair up and the run has no seam.
        if ((zones & 1) != 0)
        {
            zones++;
        }

        zones = Math.Max(2, zones);

        int zoneLength = segment.Length / zones;
        int offset = (segment.Length - (zones * zoneLength)) >> 1;

        unchecked
        {
            segment.Fill(segment.ColorFromPalette((int)(0u - counter), wrap: true));
        }

        for (int zone = 0; zone < zones; zone++)
        {
            int start = offset + (zone * zoneLength);

            for (int i = 0; i < zoneLength; i++)
            {
                int colorIndex;
                unchecked
                {
                    colorIndex = (int)((uint)(i * 255 / zoneLength) - counter);
                }

                // Every other zone runs the other way, so the gradient folds back rather than
                // starting over with a hard edge.
                int led = (zone & 1) != 0 ? i : zoneLength - 1 - i;

                if (segment.Reverse)
                {
                    led = zoneLength - 1 - led;
                }

                segment.SetPixel(start + led, segment.ColorFromPalette(colorIndex, wrap: true));
            }
        }
    }
}
