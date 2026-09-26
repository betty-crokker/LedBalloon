using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The Android loading bar: a block that grows from one end, slides round, and shrinks again.
/// <para>
/// Unusually it moves on the frame count rather than on the clock - one frame in three advances the
/// leading edge and the other two grow or shrink the block - and then asks to be called at a rate
/// that depends on how long the run is. On a 285 LED roofline that works out at about one frame
/// anyway, but on a short run it slows right down, which is the only way the same code can look the
/// same on both.
/// </para>
/// </summary>
public sealed class AndroidEffect : IWledEffect
{
    public string Name => "Android";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1);
        }

        // Grown until it is as long as intensity allows, then shrunk until it is nearly nothing.
        if (segment.Aux1 > (uint)(segment.Intensity * segment.Length / 255))
        {
            segment.Aux0 = 1;
        }
        else if (segment.Aux1 < 2)
        {
            segment.Aux0 = 0;
        }

        uint at = segment.Step & 0xFFFF;

        if (segment.Aux0 == 0)
        {
            // Growing: the tail stays put two frames in three while the head moves on the third.
            if (segment.Call % 3 == 1)
            {
                at++;
            }
            else
            {
                segment.Aux1++;
            }
        }
        else
        {
            at++;

            if (segment.Call % 3 != 1)
            {
                segment.Aux1--;
            }
        }

        if (at >= segment.Length)
        {
            at = 0;
        }

        for (uint i = 0; i < segment.Aux1; i++)
        {
            segment.SetPixel((int)((at + i) % (uint)segment.Length), segment.Colors[0]);
        }

        segment.Step = at;

        segment.FrameDelay = 3 + (int)(8 * (255 - (uint)segment.Speed) / (uint)segment.Length);
    }
}

/// <summary>
/// A pair of eyes that look about: two lit LEDs a fixed distance apart, sliding to a new place,
/// pausing, and blinking now and then.
/// <para>
/// This is the effect the whole variable frame delay exists for. It spends most of its time doing
/// nothing - a second to three seconds between movements - and a blink is a single frame held for
/// 200 ms. Drawn every frame like everything else it would slide continuously and never pause,
/// which is not the effect at all.
/// </para>
/// </summary>
public sealed class IcuEffect : IWledEffect
{
    public string Name => "ICU";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var destination = (int)(segment.Step & 0xFFFF);
        int space = (segment.Intensity >> 3) + 2;
        int apart = segment.Length / space;

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        // The color is taken from how far along the run the eyes have got, so they change color as
        // they move rather than staying one color.
        var index = (byte)FastLed.Map(destination, 0, segment.Length - apart, 0, 255);
        RgbColor color = segment.ColorFromPalette(index, mapping: false, wrap: false);

        segment.SetPixel(destination, color);
        segment.SetPixel(destination + apart, color);

        if (segment.Aux0 == destination)
        {
            // Arrived, so stop and look about.
            if (segment.Random8(6) == 0)
            {
                segment.SetPixel(destination, segment.Colors[1]);
                segment.SetPixel(destination + apart, segment.Colors[1]);
                segment.FrameDelay = 200;
                return;
            }

            segment.Aux0 = segment.Random16(segment.Length - apart);
            segment.FrameDelay = 1000 + segment.Random16(2000);
            return;
        }

        if (segment.Aux0 > segment.Step)
        {
            segment.Step++;
            destination++;
        }
        else if (segment.Aux0 < segment.Step)
        {
            segment.Step--;
            destination--;
        }

        segment.SetPixel(destination, color);
        segment.SetPixel(destination + apart, color);

        // WLED's SPEED_FORMULA_L: how long one step takes, scaled so a long run crosses in about
        // the same time as a short one.
        segment.FrameDelay = 5 + (50 * (255 - segment.Speed) / segment.Length);
    }
}

/// <summary>
/// A pair of LEDs running along the run, stopping four times on the way to flash.
/// <para>
/// The flashing is where the variable delay earns its keep: a flash frame is held for 20 ms and the
/// gap between flashes for 30, while the travelling frames take however long the speed slider says -
/// 23 ms at the middle of it on this roofline. Drawn at a flat rate the flashes would be over before
/// anyone saw them.
/// </para>
/// <para>
/// Which of the nine phases it is in comes from the frame count rather than from the clock, so the
/// flashes are always four however slowly it is running.
/// </para>
/// </summary>
internal static class ChaseFlash
{
    /// <summary>Four flashes, which with the gaps between them makes nine phases in all.</summary>
    public const int Flashes = 4;

    public static uint Phase(EffectSegment segment) => segment.Call % ((Flashes * 2) + 1);
}

/// <summary>Two background-colored LEDs running along the palette, flashing as they go.</summary>
public sealed class ChaseFlashEffect : IWledEffect
{
    public string Name => "Chase Flash";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        uint phase = ChaseFlash.Phase(segment);

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap);
        }

        int delay = 10 + (30 * (255 - segment.Speed) / segment.Length);

        if (phase < ChaseFlash.Flashes * 2)
        {
            if (phase % 2 == 0)
            {
                segment.SetPixel((int)segment.Step, segment.Colors[1]);
                segment.SetPixel((int)((segment.Step + 1) % (uint)segment.Length), segment.Colors[1]);
                delay = 20;
            }
            else
            {
                delay = 30;
            }
        }
        else
        {
            segment.Step = (segment.Step + 1) % (uint)segment.Length;
        }

        segment.FrameDelay = delay;
    }
}

/// <summary>
/// The same flashing pair leaving a random color behind them: the run fills up one LED at a time in
/// whatever color the wheel last landed on, and a new color is chosen each time it fills.
/// </summary>
public sealed class ChaseFlashRandomEffect : IWledEffect
{
    public string Name => "Chase Flash Rnd";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        uint phase = ChaseFlash.Phase(segment);

        RgbColor filled = segment.ColorWheel((byte)segment.Aux0);

        for (int i = 0; i < segment.Aux1; i++)
        {
            segment.SetPixel(i, filled);
        }

        int delay = 1 + (10 * (255 - segment.Speed) / segment.Length);

        if (phase < ChaseFlash.Flashes * 2)
        {
            var n = (int)segment.Aux1;
            var m = (int)((segment.Aux1 + 1) % (uint)segment.Length);

            if (phase % 2 == 0)
            {
                segment.SetPixel(n, segment.Colors[0]);
                segment.SetPixel(m, segment.Colors[0]);
                delay = 20;
            }
            else
            {
                segment.SetPixel(n, filled);
                segment.SetPixel(m, segment.Colors[1]);
                delay = 30;
            }
        }
        else
        {
            segment.Aux1 = (segment.Aux1 + 1) % (uint)segment.Length;

            if (segment.Aux1 == 0)
            {
                segment.Aux0 = segment.RandomWheelIndex((byte)segment.Aux0);
            }
        }

        segment.FrameDelay = delay;
    }
}
