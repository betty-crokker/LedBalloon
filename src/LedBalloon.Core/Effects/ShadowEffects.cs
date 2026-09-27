using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Up to forty-nine spotlights of random width, speed, color and shape, sliding along the run and
/// respawning off the far end.
/// <para>
/// Written to be shone through branches or leaves so the shadows move rather than the light - which is
/// why the spotlights are so various. Each one picks its own width of one to nine LEDs, its own speed
/// as a reciprocal so slow ones are common and fast ones rare, its own direction, and one of six
/// shapes: solid, a gradient, a doubled gradient, or dots every two, three or four LEDs.
/// </para>
/// <para>
/// Every spotlight is blended half and half onto whatever is already there rather than written over
/// it, so where two cross they mix instead of one winning. On a run this long with this many lights
/// that is most of what gives the effect its texture.
/// </para>
/// <para>
/// Speed is genuinely a rate and not a frame count: a spotlight keeps the time it last moved and only
/// moves when a whole LED has accumulated, so a slow one sits still for many frames and then steps.
/// That is the same trick Chase Flash uses to look unhurried without dropping frames.
/// </para>
/// </summary>
public sealed class DancingShadowsEffect : IWledEffect
{
    public string Name => "Dancing Shadows";

    /// <summary>As many as fit in a segment's share of data on an ESP32 with 32 segments.</summary>
    private const int MostSpotlights = 49;

    /// <summary>Solid, gradient, doubled gradient, and dots every two, three or four.</summary>
    private const int ShapeCount = 6;

    private const byte Solid = 0;
    private const byte Gradient = 1;
    private const byte DoubleGradient = 2;

    private sealed class Spotlight
    {
        public float Speed;
        public byte ColorIndex;
        public short Position;
        public uint LastUpdateTime;
        public byte Width;
        public byte Shape;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        int count = FastLed.Map(segment.Intensity, 0, 255, 2, MostSpotlights);

        // Moving the intensity slider starts every spotlight over, which is what the stored count is
        // for - there is no other way to notice it moved.
        bool initialize = segment.Aux0 != (uint)count;
        segment.Aux0 = (uint)count;

        Spotlight[] lights = segment.Scratch(
            () => Enumerable.Range(0, MostSpotlights).Select(_ => new Spotlight()).ToArray());

        segment.Fill(RgbColor.Black);

        for (int i = 0; i < count; i++)
        {
            Spotlight light = lights[i];
            bool respawn = false;

            if (!initialize)
            {
                // Only move when a whole LED has built up, and leave the clock alone until it does -
                // so a slow spotlight steps rather than rounding to nothing every frame and never
                // going anywhere.
                var delta = (int)((float)(now - light.LastUpdateTime)
                    * (light.Speed * ((1f + segment.Speed) / 100f)));

                if (Math.Abs(delta) >= 1)
                {
                    light.Position = (short)(light.Position + delta);
                    light.LastUpdateTime = now;
                }

                respawn = (light.Speed > 0f && light.Position > segment.Length + 2)
                    || (light.Speed < 0f && light.Position < -(light.Width + 2));
            }

            if (initialize || respawn)
            {
                light.ColorIndex = segment.Random8();
                light.Width = segment.Random8(1, 10);

                // A reciprocal, so most spotlights are slow and a few are quick.
                light.Speed = 1f / segment.Random8(4, 50);

                if (initialize)
                {
                    // On the first frame they are scattered along the run and head either way.
                    light.Position = (short)segment.Random16(segment.Length);

                    if (segment.Random8(2) == 0)
                    {
                        light.Speed = -light.Speed;
                    }
                }
                else if (segment.Random8(2) != 0)
                {
                    // Coming back on from the far end, which means turning round.
                    light.Position = (short)(segment.Length + light.Width);
                    light.Speed = -light.Speed;
                }
                else
                {
                    light.Position = (short)-light.Width;
                }

                light.LastUpdateTime = now;
                light.Shape = segment.Random8(ShapeCount);
            }

            // Color slot 255 is out of range on purpose: that is how an effect asks for the palette
            // even on palette Default, where a real slot number would give a flat color.
            RgbColor color = segment.ColorFromPalette(light.ColorIndex, colorSlot: 255);
            int start = light.Position;

            if (light.Width <= 1)
            {
                Mix(segment, start, color, 128);
                continue;
            }

            switch (light.Shape)
            {
                case Solid:
                    for (int j = 0; j < light.Width; j++)
                    {
                        Mix(segment, start + j, color, 128);
                    }

                    break;

                case Gradient:
                case DoubleGradient:
                    for (int j = 0; j < light.Width; j++)
                    {
                        int along = FastLed.Map(j, 0, light.Width - 1, 0, 255);

                        // The doubled one multiplies before the byte, so it wraps rather than
                        // clipping - two humps across the width instead of one.
                        Mix(segment, start + j, color,
                            FastLed.CubicWave8((byte)(light.Shape == Gradient ? along : along * 2)));
                    }

                    break;

                default:
                    // Shapes three, four and five are dots every two, three or four LEDs, which is
                    // the shape number less one.
                    for (int j = 0; j < light.Width; j += light.Shape - 1)
                    {
                        Mix(segment, start + j, color, 128);
                    }

                    break;
            }
        }
    }

    /// <summary>Blends onto what is already there, ignoring anything off either end.</summary>
    private static void Mix(EffectSegment segment, int index, RgbColor color, byte amount)
    {
        if ((uint)index < (uint)segment.Length)
        {
            segment.Pixels[index] = EffectSegment.Blend(segment.Pixels[index], color, amount);
        }
    }
}

/// <summary>
/// Balls rolling the length of the run, bouncing off the ends and optionally off each other.
/// <para>
/// Nothing accelerates. Each ball has a constant velocity and reverses at the ends, which is what
/// makes it read as rolling on the flat rather than falling - the other ball effect here, Bouncing
/// Balls, is the one with gravity in it.
/// </para>
/// <para>
/// Position is kept as a height from nought to one and converted to an LED only when it is drawn, and
/// each ball stores the time of its last bounce rather than stepping every frame. So the arithmetic
/// stays exact however long it runs, and a ball that has not bounced for a minute is still where it
/// should be.
/// </para>
/// <para>
/// With collisions turned on the balls exchange momentum properly, each carrying its own mass between
/// a tenth and one. The collision is solved for the moment it happened rather than the frame it was
/// noticed in - the code works out when the two paths crossed, winds both balls back to there,
/// exchanges their velocities and replays the rest of the frame. Without that, two balls closing
/// quickly would pass through each other between frames.
/// </para>
/// </summary>
public sealed class RollingBallsEffect : IWledEffect
{
    public string Name => "Rolling Balls";

    private const int MostBalls = 16;

    private sealed class RollingBall
    {
        public uint LastBounceUpdate;
        public float Mass;
        public float Velocity;
        public float Height;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        RollingBall[] balls = segment.Scratch(
            () => Enumerable.Range(0, MostBalls).Select(_ => new RollingBall()).ToArray());

        int count = (segment.Intensity / 16) + 1;
        bool hasThird = segment.Colors[2] != RgbColor.Black;

        if (segment.Call == 0)
        {
            segment.Fill(hasThird ? RgbColor.Black : segment.Colors[1]);

            // Every ball is given a state, not just the ones in use, so raising the count later puts
            // balls into play that have been rolling all along rather than appearing from nowhere.
            for (int i = 0; i < MostBalls; i++)
            {
                balls[i].LastBounceUpdate = now;
                balls[i].Velocity = 20f * segment.Random16(1000, 10000) / 10000f;

                if (segment.Random8() < 128)
                {
                    balls[i].Velocity = -balls[i].Velocity;
                }

                balls[i].Height = segment.Random16(10000) / 10000f;
                balls[i].Mass = segment.Random16(1000, 10000) / 10000f;
            }
        }

        // How much real time a unit of the run's length is worth. The speed slider only reaches this
        // through scale8, so it moves in eight steps rather than 256.
        float cfac = (FastLed.Scale8(8, (byte)(255 - segment.Speed)) + 1) * 20000f;

        if (segment.Option3)
        {
            segment.FadeOut(250);
        }
        else if (!segment.Option2)
        {
            segment.Fill(hasThird ? RgbColor.Black : segment.Colors[1]);
        }

        for (int i = 0; i < count; i++)
        {
            RollingBall ball = balls[i];

            float since = (now - ball.LastBounceUpdate) / cfac;
            float height = ball.Height + (ball.Velocity * since);

            // A ball well off the run can only mean the count was just raised and this one had been
            // left somewhere impossible, so drop it back on at random.
            if (height is < -0.5f or > 1.5f)
            {
                height = ball.Height = segment.Random16(10000) / 10000f;
                ball.LastBounceUpdate = now;
            }

            if ((height <= 0f && ball.Velocity < 0f) || (height >= 1f && ball.Velocity > 0f))
            {
                ball.Velocity = -ball.Velocity;
                ball.LastBounceUpdate = now;
                ball.Height = height;
            }

            if (segment.Option1)
            {
                height = Collide(balls, i, count, now, cfac, height);
            }

            RgbColor color = segment.Colors[0];

            if (segment.PaletteId != 0)
            {
                color = segment.ColorFromPalette(i * 255 / count, wrap: segment.SolidWrap);
            }
            else if (hasThird)
            {
                color = segment.Colors[i % 3];
            }

            height = Math.Clamp(height, 0f, 1f);

            // Away from zero, as C rounds - not to even, as .NET does by default.
            var at = (int)MathF.Round(height * (segment.Length - 1), MidpointRounding.AwayFromZero);

            segment.SetPixel(at, color);

            ball.LastBounceUpdate = now;
            ball.Height = height;
        }
    }

    /// <summary>
    /// Works out whether this ball has met any of the ones after it since the last frame, and if so
    /// winds both back to the moment it happened.
    /// </summary>
    private static float Collide(
        RollingBall[] balls,
        int i,
        int count,
        uint now,
        float cfac,
        float height)
    {
        RollingBall ball = balls[i];

        for (int j = i + 1; j < count; j++)
        {
            RollingBall other = balls[j];

            // Two balls at the same speed never meet, and the arithmetic below would divide by zero
            // working out when they did.
            if (other.Velocity == ball.Velocity)
            {
                continue;
            }

            // When the two paths crossed, measured from the other ball's last bounce - which keeps
            // the precision where it is needed rather than in a difference of two large clocks.
            float apart = unchecked(other.LastBounceUpdate - ball.LastBounceUpdate);

            float when = ((cfac * (ball.Height - other.Height)) + (ball.Velocity * apart))
                / (other.Velocity - ball.Velocity);

            // Two milliseconds minimum, or a pair that has just bounced off each other bounces again
            // on the next frame and they stick together.
            if (when <= 2f || when >= unchecked(now - other.LastBounceUpdate))
            {
                continue;
            }

            ball.Height += ball.Velocity
                * (when + unchecked(other.LastBounceUpdate - ball.LastBounceUpdate)) / cfac;

            other.Height = ball.Height;

            ball.LastBounceUpdate = (uint)(when + 0.5f) + other.LastBounceUpdate;
            other.LastBounceUpdate = ball.LastBounceUpdate;

            // An elastic collision between two masses, which is where the masses earn their keep:
            // equal masses swap velocities, and a light ball bounces off a heavy one.
            float was = ball.Velocity;

            ball.Velocity = (((ball.Mass - other.Mass) * was) + (2f * other.Mass * other.Velocity))
                / (ball.Mass + other.Mass);

            other.Velocity = (((other.Mass - ball.Mass) * other.Velocity) + (2f * ball.Mass * was))
                / (ball.Mass + other.Mass);

            // Replay the rest of the frame at the new velocity.
            height = ball.Height + (ball.Velocity * (now - ball.LastBounceUpdate) / cfac);
        }

        return height;
    }
}
