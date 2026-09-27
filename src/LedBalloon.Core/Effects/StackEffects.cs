using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Bricks falling one at a time and stacking up from the far end, until the run is full and clears.
/// <para>
/// A state machine in one field, and that field is two things at once: 0 means "make a new brick", 1
/// forming, 2 falling, and anything larger is a timestamp for when the fade that clears a full run
/// should end. Reading it as a state or as a clock depending on its size is what lets one
/// <c>uint32_t</c> carry both.
/// </para>
/// <para>
/// The falling brick repaints everything above the stack every frame, so the stack itself is the only
/// thing that persists between frames - it is never redrawn, just left alone. Which means the picture
/// is genuinely accumulated rather than recomputed, and the one effect here where a dropped frame would
/// leave a permanent mark.
/// </para>
/// <para>
/// Its speed is stated in the firmware as seconds for a full drop - five at the bottom of the slider,
/// a quarter of a second at the top - and that only holds if the strip runs a frame every FRAMETIME.
/// These controllers have no frame rate cap, so FRAMETIME is 2 ms while the wire takes 9, and every
/// brick falls about four and a half times slower than the slider claims.
/// </para>
/// </summary>
public sealed class TetrixEffect : IWledEffect
{
    public string Name => "Tetrix";

    /// <summary>How long the full run fades for once it is full.</summary>
    private const uint FadeMilliseconds = 2000;

    private sealed class Tetris
    {
        public float Pos;
        public float Speed;
        public byte Col;
        public ushort Brick;
        public ushort Stack;

        /// <summary>A state below three, or the time a fade ends above it.</summary>
        public uint Step;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        Tetris drop = segment.Scratch(() => new Tetris());

        if (segment.Call == 0)
        {
            drop.Stack = 0;

            // Open by fading whatever was there, rather than by snapping to the background - which is
            // also why there is no fill on the first frame.
            drop.Step = now + FadeMilliseconds;

            if (segment.Option1)
            {
                drop.Col = 0;
            }
        }

        if (drop.Step == 0)
        {
            // A speed of zero means every brick picks its own.
            int speed = segment.Speed != 0 ? segment.Speed : segment.Random8(1, 255);

            // Milliseconds for a full drop, five seconds down to a quarter.
            speed = FastLed.Map(speed, 1, 255, 5000, 250);

            // LEDs per frame - and per FRAMETIME rather than per actual frame, which is where the
            // stated timing and the real one part company.
            drop.Speed = (float)(segment.Length * segment.FrameTime) / speed;

            drop.Pos = segment.Length;

            if (!segment.Option1)
            {
                // Sixteen choices rather than 256, so consecutive bricks are clearly different colors
                // instead of two shades of the same one.
                drop.Col = (byte)(segment.Random8(0, 15) << 4);
            }

            drop.Step = 1;

            // Bricks grow with the run: a 285 LED run gets five times the base size.
            drop.Brick = (ushort)((segment.Intensity != 0
                ? (segment.Intensity >> 5) + 1
                : segment.Random8(1, 5)) * (1 + (segment.Length >> 6)));
        }

        if (drop.Step == 1 && (segment.Random8() >> 6) != 0)
        {
            // Three chances in four each frame, so forming is over almost at once - it is a hesitation
            // rather than a stage.
            drop.Step = 2;
        }

        if (drop.Step == 2)
        {
            if (drop.Pos > drop.Stack)
            {
                drop.Pos -= drop.Speed;

                if ((int)drop.Pos < (int)drop.Stack)
                {
                    drop.Pos = drop.Stack;
                }

                // Everything from the brick to the far end, brick first and background after - so the
                // brick paints out its own trail and the stack below is left untouched.
                for (int i = (int)drop.Pos; i < segment.Length; i++)
                {
                    segment.Pixels[i] = i < (int)drop.Pos + drop.Brick
                        ? segment.ColorFromPalette(drop.Col)
                        : segment.Colors[1];
                }
            }
            else
            {
                drop.Step = 0;
                drop.Stack += drop.Brick;

                if (drop.Stack >= segment.Length)
                {
                    drop.Step = now + FadeMilliseconds;
                }
            }
        }

        if (drop.Step > 2)
        {
            drop.Brick = 0;

            if (drop.Step > now)
            {
                // A tenth of the way to the background per frame, which over two seconds takes a full
                // run down to nothing.
                for (int i = 0; i < segment.Length; i++)
                {
                    segment.Pixels[i] = EffectSegment.Blend(segment.Pixels[i], segment.Colors[1], 25);
                }
            }
            else
            {
                drop.Stack = 0;
                drop.Step = 0;

                if (segment.Option1)
                {
                    drop.Col += 8;
                }
            }
        }
    }
}

/// <summary>
/// Broad soft waves of color drifting along the run and through each other, on a very dim backlight.
/// <para>
/// Each wave has a lifetime, a width, a center, a direction and a speed, all drawn when it is born, and
/// its brightness is the product of three things: how far this LED is from its center, how far through
/// its life it is - brightest at exactly half, nothing at either end - and an alpha of its own between
/// 0.6 and 1. So a wave swells, drifts and dies without anything tracking it.
/// </para>
/// <para>
/// Waves add rather than replace, and they add with saturation, so where several overlap the color goes
/// white rather than wrapping round. That is the aurora: the shapes are broad and dim individually and
/// the interesting color is all in the overlaps.
/// </para>
/// <para>
/// The backlight is one unit plus one for each color slot that is set, so a run with all three slots
/// in use never goes fully dark - it sits at four. Which is not decoration: it is what keeps the run
/// from looking switched off between waves.
/// </para>
/// </summary>
public sealed class AuroraEffect : IWledEffect
{
    public string Name => "Aurora";

    /// <summary>As many waves as an ESP32 with 32 segments can afford.</summary>
    private const int MostWaves = 20;

    /// <summary>Scales every wave's speed - higher is faster.</summary>
    private const int MaxSpeed = 6;

    /// <summary>Divides the run to get the widest a wave may be - higher is smaller.</summary>
    private const int WidthFactor = 6;

    private sealed class State
    {
        public AuroraWave[] Waves = [];
    }

    private sealed class AuroraWave
    {
        private ushort _ttl;
        private RgbColor _baseColor;
        private float _baseAlpha;
        private ushort _age;
        private ushort _width;
        private float _center;
        private bool _goingLeft;
        private float _speedFactor;

        public bool Alive { get; private set; }

        public void Init(EffectSegment segment, RgbColor color)
        {
            // 500 to 1500 frames, which on a controller with no frame cap is four to fourteen seconds
            // rather than the twelve to thirty-six the firmware's own FRAMETIME would suggest.
            _ttl = segment.Random16(500, 1501);
            _baseColor = color;
            _baseAlpha = segment.Random8(60, 101) / 100f;
            _age = 0;

            // Half the wave's width, which is what makes the arithmetic below symmetrical.
            _width = segment.Random16(segment.Length / 20, segment.Length / WidthFactor);

            if (_width == 0)
            {
                _width = 1;
            }

            _center = segment.Random8(101) / 100f * segment.Length;
            _goingLeft = segment.Random8(0, 2) == 0;
            _speedFactor = segment.Random8(10, 31) / 100f * MaxSpeed / 255f;
            Alive = true;
        }

        public RgbColor ColorAt(int index)
        {
            if (index < _center - _width || index > _center + _width)
            {
                return RgbColor.Black;
            }

            float offset = Math.Abs(index - _center);
            float offsetFactor = offset / _width;

            // Brightest at exactly half its life, nothing at either end. The halves are worked out
            // differently in the firmware - one divides by an integer half and the other by a floating
            // point one - so they do not quite meet in the middle, and that is kept.
            float ageFactor = (float)_age / _ttl < 0.5f
                ? _age / (float)(_ttl / 2)
                : (_ttl - _age) / (_ttl * 0.5f);

            float factor = (1 - offsetFactor) * ageFactor * _baseAlpha;

            return new RgbColor(
                (byte)(_baseColor.R * factor),
                (byte)(_baseColor.G * factor),
                (byte)(_baseColor.B * factor));
        }

        public void Update(int length, byte speed)
        {
            _center += _goingLeft ? -(_speedFactor * speed) : _speedFactor * speed;
            _age++;

            if (_age > _ttl)
            {
                Alive = false;
            }
            else if (_goingLeft ? _center + _width < 0 : _center - _width > length)
            {
                Alive = false;
            }
        }
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        State state = segment.Scratch(() => new State());

        if (segment.Aux0 != segment.Intensity || segment.Call == 0)
        {
            segment.Aux1 = (uint)FastLed.Map(segment.Intensity, 0, 255, 2, MostWaves);
            segment.Aux0 = segment.Intensity;

            state.Waves = [.. Enumerable.Range(0, (int)segment.Aux1).Select(_ => new AuroraWave())];

            foreach (AuroraWave wave in state.Waves)
            {
                wave.Init(segment, NewColor(segment));
            }
        }

        AuroraWave[] waves = state.Waves;

        foreach (AuroraWave wave in waves)
        {
            wave.Update(segment.Length, segment.Speed);

            if (!wave.Alive)
            {
                wave.Init(segment, NewColor(segment));
            }
        }

        byte backlight = 1;

        foreach (RgbColor slot in segment.Colors)
        {
            if (slot != RgbColor.Black)
            {
                backlight++;
            }
        }

        for (int i = 0; i < segment.Length; i++)
        {
            byte r = backlight, g = backlight, b = backlight;

            foreach (AuroraWave wave in waves)
            {
                RgbColor color = wave.ColorAt(i);

                if (color == RgbColor.Black)
                {
                    continue;
                }

                // Saturating, because these are CRGBs in the firmware and adding those clamps. Plain
                // addition would wrap a bright overlap round to dark.
                r = FastLed.QAdd8(r, color.R);
                g = FastLed.QAdd8(g, color.G);
                b = FastLed.QAdd8(b, color.B);
            }

            segment.Pixels[i] = new RgbColor(r, g, b);
        }
    }

    /// <summary>
    /// A color for a new wave: a random point on the palette, or - on palette Default - one of the
    /// three color slots at random.
    /// </summary>
    /// <remarks>
    /// The firmware draws the index and the slot as two arguments to one call, and C++ does not say
    /// which is drawn first. Taken as index then slot here. It shifts the sequence and not the
    /// distribution, which is all this effect can be held to anyway.
    /// </remarks>
    private static RgbColor NewColor(EffectSegment segment) =>
        segment.ColorFromPalette(segment.Random8(), colorSlot: segment.Random8(0, 3));
}
