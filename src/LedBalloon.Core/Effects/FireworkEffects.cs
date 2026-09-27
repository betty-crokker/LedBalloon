using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A flare launched from one end of the run, which slows, stops, and bursts into sparks that fall.
/// <para>
/// Three phases in one counter, and the counter is again doing two jobs: 0 and 1 are the flare, 2 and 3
/// the burst, and anything from 4 up is a countdown of frames to wait before the next launch. Counting
/// down to 4 rather than to 0 keeps the wait out of the way of the phases.
/// </para>
/// <para>
/// The flare does not explode at the top of its arc. It explodes when its velocity has fallen past
/// twelve times gravity - which, since gravity is negative, means it has already been falling for a
/// while. So the burst happens slightly below the peak and on the way down, which is what stops it
/// looking mechanical.
/// </para>
/// <para>
/// A spark's color is its own brightness read as a temperature: above 300 it is white cooling toward the
/// spark color, below that the spark color fading to black - and while it fades, green and blue are
/// taken off faster than red, twice as fast for blue. That is what turns a fading spark orange and then
/// red rather than simply dimming it.
/// </para>
/// <para>
/// How many sparks depends on how high the flare got, so a low launch gives a small burst. It is also
/// capped by how much scratch memory this segment may use, which is the only place a controller's
/// segment count reaches into what an effect looks like.
/// </para>
/// </summary>
public sealed class Fireworks1DEffect : IWledEffect
{
    public string Name => "Fireworks 1D";

    /// <summary>Twenty bytes to a spark in the firmware, which is what caps how many there can be.</summary>
    private const int SparkBytes = 20;

    private sealed class State
    {
        public Spark[] Sparks = [];

        /// <summary>
        /// Gravity for the sparks, which weakens by a fifth every frame - so they fall more slowly as
        /// they burn out rather than accelerating away.
        /// </summary>
        public float DyingGravity;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        int rows = segment.Length;
        int count = Math.Min(5 + (rows >> 1), SegmentData.Budget(segment.ActiveSegments) / SparkBytes);

        State state = segment.Scratch(() => new State());

        if (state.Sparks.Length != count)
        {
            state.Sparks = [.. Enumerable.Range(0, count).Select(_ => new Spark())];
        }

        // The firmware compares the allocation size rather than the count, which is the same test with
        // more arithmetic - and it is how a first frame is told from any other here, since this effect
        // never looks at the frame counter.
        if (segment.Aux1 != (uint)count)
        {
            state.DyingGravity = 0f;
            segment.Aux0 = 0;
            segment.Aux1 = (uint)count;
        }

        segment.FadeOut(252);

        Spark[] sparks = state.Sparks;
        Spark flare = sparks[0];

        float gravity = (-0.0004f - (segment.Speed / 800000f)) * rows;

        if (segment.Aux0 < 2)
        {
            if (segment.Aux0 == 0)
            {
                flare.Position = 0;

                // Which end it fires from, decided by a coin weighted with the intensity slider - the
                // one place this effect reads it.
                flare.PositionX = segment.Intensity > segment.Random8() ? 1f : 0f;

                int peakHeight = 75 + segment.Random8(180);
                peakHeight = peakHeight * (rows - 1) >> 8;

                // Launched at the speed that reaches that height rather than at a chosen speed.
                flare.Velocity = MathF.Sqrt(-2f * gravity * peakHeight);
                flare.VelocityX = 0f;
                flare.Brightness = 255;
                segment.Aux0 = 1;
            }

            // Gravity is negative, so this holds until the flare has been falling for a while - the
            // burst is below the peak, not at it.
            if (flare.Velocity > 12 * gravity)
            {
                var white = new RgbColor(
                    (byte)flare.Brightness, (byte)flare.Brightness, (byte)flare.Brightness);

                segment.SetPixel(At(flare, rows), white);

                flare.Position = Math.Clamp(flare.Position + flare.Velocity, 0, rows - 1);
                flare.Velocity += gravity;
                flare.Brightness -= 2;
            }
            else
            {
                segment.Aux0 = 2;
            }
        }
        else if (segment.Aux0 < 4)
        {
            // Bigger bursts from higher flares, and never fewer than four sparks even on a run too
            // short to afford them - which is why this is not a clamp between the two.
            int burst = Math.Min(Math.Max((int)flare.Position + segment.Random8(4), 4), count);

            if (segment.Aux0 == 2)
            {
                for (int i = 1; i < burst; i++)
                {
                    Spark spark = sparks[i];

                    spark.Position = flare.Position;
                    spark.PositionX = flare.PositionX;

                    // Mostly upward but not entirely, so some sparks start by falling.
                    spark.Velocity = (segment.Random16(20001) / 10000f) - 0.9f;
                    spark.Velocity *= rows < 32 ? 0.5f : 1f;
                    spark.VelocityX = 0f;

                    // Set before the velocity is scaled, so every spark starts equally bright however
                    // fast it is going.
                    spark.Brightness = 345;
                    spark.ColorIndex = segment.Random8();

                    // Proportional to how high the flare got, so a low launch scatters less far. The
                    // firmware scales the second axis here too, by zero on a one-dimensional run.
                    spark.Velocity *= flare.Position / rows;
                    spark.Velocity *= -gravity * 50f;
                }

                state.DyingGravity = gravity / 2f;
                segment.Aux0 = 3;
            }

            // Spark one stands for all of them: they were all lit together and dim together, so when it
            // is out the burst is over.
            if (sparks[1].Brightness > 4)
            {
                for (int i = 1; i < burst; i++)
                {
                    Spark spark = sparks[i];

                    spark.Position += spark.Velocity;
                    spark.PositionX += spark.VelocityX;
                    spark.Velocity += state.DyingGravity;

                    if (spark.Brightness > 3)
                    {
                        spark.Brightness -= 4;
                    }

                    if (spark.Position <= 0 || spark.Position >= rows)
                    {
                        continue;
                    }

                    segment.SetPixel(At(spark, rows), Ember(segment, spark));
                }

                if (segment.Option3)
                {
                    segment.Blur(16);
                }

                state.DyingGravity *= 0.8f;
            }
            else
            {
                // Six to fifteen frames of dark before the next launch.
                segment.Aux0 = 6u + segment.Random8(10);
            }
        }
        else
        {
            segment.Aux0--;

            if (segment.Aux0 < 4)
            {
                segment.Aux0 = 0;
            }
        }
    }

    /// <summary>Where along the run this spark is, counted from whichever end the flare came from.</summary>
    private static int At(Spark spark, int rows) =>
        spark.PositionX > 0f ? rows - (int)spark.Position - 1 : (int)spark.Position;

    /// <summary>
    /// A spark's color, read off its brightness as a temperature rather than by scaling one color.
    /// </summary>
    private static RgbColor Ember(EffectSegment segment, Spark spark)
    {
        int heat = spark.Brightness;

        RgbColor sparkColor = segment.PaletteId != 0
            ? segment.ColorWheel(spark.ColorIndex)
            : segment.Colors[0];

        if (heat > 300)
        {
            // Still white hot, cooling toward the spark's own color.
            return EffectSegment.Blend(sparkColor, RgbColor.White, (byte)((heat - 300) * 5));
        }

        if (heat <= 45)
        {
            return RgbColor.Black;
        }

        RgbColor color = EffectSegment.Blend(RgbColor.Black, sparkColor, (byte)(heat - 45));

        // Green comes off as it cools and blue twice as fast, which is what makes a dying spark go
        // orange and then red instead of just dimming.
        var cooling = (byte)((300 - heat) >> 5);

        return new RgbColor(
            color.R,
            FastLed.QSub8(color.G, cooling),
            FastLed.QSub8(color.B, cooling * 2));
    }
}

/// <summary>
/// Stars bursting in place, each throwing fragments outward symmetrically and fading from white.
/// <para>
/// Thirty-six stars on a run this long, each mostly idle: every frame every idle star has about one
/// chance in sixty-four of going off, so they arrive scattered rather than in turn. A star that goes off
/// picks a position, a color from the wheel, a speed and between three and nine fragments, all of which
/// start at that position.
/// </para>
/// <para>
/// The fragments spread at different rates from the same point, and the rates come from the fragment
/// number: <c>i / 2 / 3</c>, so the first two do not move at all and each pair after them goes a third
/// faster than the last. Then every fragment is drawn twice, once where it is and once reflected through
/// the star's position - which is why a burst is always symmetrical and why only half of it is ever
/// computed.
/// </para>
/// <para>
/// A star flashes white for a quarter second, then fades over another second and a half, and shrinks as
/// it fades: the fragments are drawn two LEDs wide at first and one by the end. Both the color and the
/// width come off the same fraction, so it thins as it dims.
/// </para>
/// </summary>
public sealed class StarburstEffect : IWledEffect
{
    public string Name => "Fireworks Starburst";

    /// <summary>Fragments a star can throw, on a build with sixty bytes to spend on each.</summary>
    private const int MostFragments = 10;

    /// <summary>Sixty bytes to a star, which is what caps how many there can be.</summary>
    private const int StarBytes = 60;

    private const float MaxSpeed = 375f;
    private const float IgnitionMilliseconds = 250f;
    private const float FadeMilliseconds = 1500f;

    private sealed class Star
    {
        public RgbColor Color;
        public uint Birth;
        public uint Last;
        public float Velocity;
        public ushort Position;
        public float[] Fragment = new float[MostFragments];
    }

    private sealed class State
    {
        public Star[] Stars = [];
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        int count = Math.Min(
            1 + (segment.Length >> 3), SegmentData.Budget(segment.ActiveSegments) / StarBytes);

        State state = segment.Scratch(() => new State());

        if (state.Stars.Length != count)
        {
            state.Stars = [.. Enumerable.Range(0, count).Select(_ => new Star())];
        }

        Star[] stars = state.Stars;

        foreach (Star star in stars)
        {
            // One chance in sixty-four per frame at a middling speed, and only for a star that is not
            // already going.
            if (star.Birth != 0 || segment.Random8(144 - (segment.Speed >> 1)) != 0)
            {
                continue;
            }

            int startPos = segment.Random16(segment.Length - 1);

            // Two independent draws multiplied together, so slow bursts are far commoner than fast
            // ones even though each draw is flat.
            float multiplier = segment.Random8() / 255f;

            star.Color = segment.ColorWheel(segment.Random8());
            star.Position = (ushort)startPos;
            star.Velocity = MaxSpeed * (segment.Random8() / 255f) * multiplier;
            star.Birth = now;
            star.Last = now;

            int fragments = segment.Random8(3, 6 + (segment.Intensity >> 5));

            for (int i = 0; i < MostFragments; i++)
            {
                star.Fragment[i] = i < fragments ? startPos : -1;
            }
        }

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        foreach (Star star in stars)
        {
            if (star.Birth != 0)
            {
                float dt = (now - star.Last) / 1000f;

                for (int i = 0; i < MostFragments; i++)
                {
                    // Pairs of fragments share a rate, and the first pair does not move at all - so a
                    // burst has a still core with shells spreading around it.
                    int shell = i >> 1;

                    if (star.Fragment[i] > 0)
                    {
                        star.Fragment[i] += star.Velocity * dt * shell / 3f;
                    }
                }

                star.Last = now;
                star.Velocity -= 3 * star.Velocity * dt;
            }

            RgbColor color = star.Color;
            float fade = 0f;
            float age = now - star.Birth;

            if (age < IgnitionMilliseconds)
            {
                color = EffectSegment.Blend(
                    RgbColor.White, color, (byte)(254.5f * (age / IgnitionMilliseconds)));
            }
            else if (age > IgnitionMilliseconds + FadeMilliseconds)
            {
                fade = 1f;
                star.Birth = 0;
                color = segment.Colors[1];
            }
            else
            {
                age -= IgnitionMilliseconds;
                fade = age / FadeMilliseconds;
                color = EffectSegment.Blend(star.Color, segment.Colors[1], (byte)(254.5f * fade));
            }

            // Two LEDs wide when new and nothing by the end, off the same fraction as the color - so a
            // star thins as it dims.
            float particleSize = (1f - fade) * 2f;

            for (int index = 0; index < MostFragments * 2; index++)
            {
                bool mirrored = (index & 1) != 0;
                int i = index >> 1;

                if (star.Fragment[i] <= 0)
                {
                    continue;
                }

                float at = star.Fragment[i];

                if (mirrored)
                {
                    // Reflected through the star, which is why only one side is ever worked out.
                    at -= (at - star.Position) * 2;
                }

                int start = (int)(at - particleSize);
                int end = (int)(at + particleSize);

                if (start < 0)
                {
                    start = 0;
                }

                if (start == end)
                {
                    // A fragment too small to cover a whole LED still covers one.
                    end++;
                }

                if (end > segment.Length)
                {
                    end = segment.Length;
                }

                for (int p = start; p < end; p++)
                {
                    segment.SetPixel(p, color);
                }
            }
        }
    }
}
