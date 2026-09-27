using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Balls dropped down the run, bouncing and losing height each time - and the only effect here whose
/// arithmetic is floating point, because it is a physical simulation rather than a waveform.
/// <para>
/// Each ball keeps the time it last bounced and its speed at that moment, and its height is worked
/// out from the two: <c>(g t / 2 + v) t</c>. So it is a function of the clock after all, just one
/// whose constants are reset on every bounce. Speed is gravity - it scales time rather than height -
/// and intensity is how many balls, up to sixteen.
/// </para>
/// <para>
/// The balls damp differently from each other on purpose: the damping is
/// <c>0.9 - i / (count * count)</c>, so the later ones lose energy faster and the group spreads out
/// into a rhythm instead of bouncing in unison. A ball that has nearly stopped is relaunched at a
/// random speed rather than left flat.
/// </para>
/// </summary>
public sealed class BouncingBallsEffect : IWledEffect
{
    public string Name => "Bouncing Balls";

    private const int MostBalls = 16;
    private const float Gravity = -9.81f;

    private sealed class Ball
    {
        public uint LastBounce;
        public float Velocity;
        public float Height;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        Ball[] balls = segment.Scratch(() => Start(segment, now));

        bool hasThird = segment.Colors[2] != RgbColor.Black;

        if (!segment.Option2)
        {
            segment.Fill(hasThird ? RgbColor.Black : segment.Colors[1]);
        }

        int count = (segment.Intensity * (MostBalls - 1) / 255) + 1;

        for (int i = 0; i < count; i++)
        {
            Ball ball = balls[i];

            // Integer division on the slider first, so gravity comes in four steps rather than
            // smoothly - which is what makes the bounce rhythm change character rather than rate.
            float seconds = (now - ball.LastBounce) / (float)(((255 - segment.Speed) / 64) + 1) / 1000f;

            ball.Height = ((0.5f * Gravity * seconds) + ball.Velocity) * seconds;

            if (ball.Height <= 0f)
            {
                ball.Height = 0f;

                float damping = 0.9f - (i / (float)(count * count));
                ball.Velocity *= damping;
                ball.LastBounce = now;

                if (ball.Velocity < 0.015f)
                {
                    ball.Velocity = MathF.Sqrt(-2f * Gravity) * segment.Random8(5, 11) / 10f;
                }
            }
            else if (ball.Height > 1f)
            {
                // Thrown clear of the run, which happens when the slider is turned up under it.
                continue;
            }

            RgbColor color =
                segment.PaletteId != 0 ? segment.ColorWheel((byte)(i * (256 / Math.Max(count, 8))))
                : hasThird ? segment.Colors[i % 3]
                : segment.Colors[0];

            segment.SetPixel((int)MathF.Round(ball.Height * (segment.Length - 1)), color);
        }
    }

    private static Ball[] Start(EffectSegment segment, uint now)
    {
        var balls = new Ball[MostBalls];

        for (int i = 0; i < balls.Length; i++)
        {
            // No speed and no height, so the first frame reads as a bounce and launches it.
            balls[i] = new Ball { LastBounce = now };
        }

        return balls;
    }
}

/// <summary>
/// Alternate LEDs crossfading in opposite directions between two ends of the palette, which is a
/// level crossing's two lamps - or a string of fairy lights, depending on the palette.
/// <para>
/// Every other LED gets the palette read forwards and the ones between it read backwards, so the two
/// sets are always opposites. Intensity is how much of each half cycle is spent moving: at zero the
/// lamps snap over, and turning it up stretches the crossfade until they are always on the way
/// somewhere.
/// </para>
/// <para>
/// It counts in milliseconds of <c>FRAMETIME</c> rather than reading the clock, so its pace follows
/// the configured frame rate rather than the real one.
/// </para>
/// </summary>
public sealed class RailwayEffect : IWledEffect
{
    public string Name => "Railway";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        uint half = (256u - segment.Speed) * 40u;
        uint ramp = (half * segment.Intensity) >> 8;

        if (segment.Step > half)
        {
            segment.Step = 0;
            segment.Aux0 = segment.Aux0 != 0 ? 0u : 1u;
        }

        int at = 255;

        if (ramp != 0)
        {
            uint along = segment.Step * 255 / ramp;

            if (along < 255)
            {
                at = (int)along;
            }
        }

        if (segment.Aux0 != 0)
        {
            at = 255 - at;
        }

        for (int i = 0; i < segment.Length; i += 2)
        {
            // Color slot 255 is out of range on purpose: this effect always wants the gradient,
            // never a slot, and the source says so in a comment.
            segment.Pixels[i] = segment.ColorFromPalette(255 - at, colorSlot: 255);

            if (i < segment.Length - 1)
            {
                segment.Pixels[i + 1] = segment.ColorFromPalette(at, colorSlot: 255);
            }
        }

        segment.Step += (uint)segment.FrameTime;
    }
}

/// <summary>
/// Waves rolling one way, pausing, then rolling back - a drum turning over.
/// <para>
/// The direction comes from a sloped square wave with a rest in it, which is what gives the pause at
/// each end rather than a smooth reversal. That wave is signed and the position accumulates it, so
/// turning back really is turning back rather than starting again.
/// </para>
/// </summary>
public sealed class WashingMachineEffect : IWledEffect
{
    public string Name => "Washing Machine";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int turning = FastLed.TristateSquare8((byte)(now >> 7), 90, 15);

        // Deliberately allowed to wrap: the position is unsigned and the direction is not, so
        // turning back subtracts by overflowing, exactly as the firmware does.
        unchecked
        {
            segment.Step += (uint)(turning * 2048 / (512 - segment.Speed));
        }

        for (int i = 0; i < segment.Length; i++)
        {
            var wave = (byte)(((segment.Intensity / 25) + 1) * 255 * i / segment.Length
                + (int)(segment.Step >> 7));

            // Slot 3 does not exist, which is this effect's way of insisting on the gradient.
            segment.Pixels[i] = segment.ColorFromPalette(
                FastLed.Sin8(wave), wrap: segment.SolidWrap, colorSlot: 3);
        }
    }
}

/// <summary>
/// Every LED creeping toward a color drawn from a sliding palette, so the run is always somewhere
/// between where it was and where it is going.
/// <para>
/// It keeps the picture it drew last and blends a fraction of the way toward the target each frame -
/// intensity is that fraction - which means the colors on screen are never quite the palette's own.
/// The target for each LED comes from a quadratic wave of its position plus a shift that grows with
/// time, and the shift also creeps along by three per LED, so the pattern is never periodic.
/// </para>
/// <para>
/// Its buffer is capped at 255 LEDs and read back with an off-by-one - the index is reset only after
/// it has gone past the end - so on a run longer than that, every 256th LED shows whatever is in the
/// spare slot. Reproduced: it is one dark pixel in a known place, and correcting it would be a
/// different effect.
/// </para>
/// </summary>
public sealed class BlendsEffect : IWledEffect
{
    public string Name => "Blends";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int span = Math.Min(segment.Length, 255);

        RgbColor[] kept = segment.Scratch(() => new RgbColor[span + 1]);

        var rate = (byte)FastLed.Map(segment.Intensity, 0, 255, 10, 128);
        uint shift = (now * (uint)((segment.Speed >> 3) + 1)) >> 8;

        for (int i = 0; i < span; i++)
        {
            RgbColor want = segment.ColorFromPalette(
                (int)(shift + FastLed.QuadWave8((byte)((i + 1) * 16))),
                wrap: segment.SolidWrap,
                colorSlot: 255);

            kept[i] = EffectSegment.Blend(kept[i], want, rate);
            shift += 3;
        }

        int offset = 0;

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = kept[offset++];

            if (offset > span)
            {
                offset = 0;
            }
        }
    }
}
