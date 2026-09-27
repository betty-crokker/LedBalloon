using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The integer math WLED's effects are built out of, ported rather than approximated.
/// <para>
/// These look like things <see cref="Math"/> already does, and they are not. WLED runs on a
/// microcontroller with no FPU to spare, so its sine is a fixed-point Bhaskara approximation and
/// its scaling is an 8-bit multiply-and-shift. Both are a little wrong in ways that are part of
/// how the effects look - a run drawn with real trigonometry drifts against the same run on the
/// wall. Reproducing the arithmetic is the cheap half of reproducing the effect.
/// </para>
/// </summary>
public static class FastLed
{
    /// <summary>
    /// Sine over a full turn: input 0-65535, output -32767 to +32767.
    /// <para>
    /// Bhaskara I's approximation, <c>16x(pi - x) / (5pi^2 - 4x(pi - x))</c>, in integers. Accurate
    /// to about a part in a thousand, which is half an LED on a three hundred LED run.
    /// </para>
    /// <para>
    /// This one is WLED's <c>sin16_t</c> rather than FastLED's <c>sin16</c>, despite where it lives.
    /// WLED replaced the FastLED version outright - its own comment says "1:1 replacements to remove
    /// the use of fastled sin16()" - and the replacement is more accurate, so an effect ported
    /// against the FastLED table would drift against the wall. Checked line by line against
    /// <c>wled_math.cpp</c>.
    /// </para>
    /// </summary>
    public static short Sin16(ushort theta)
    {
        int scale = 1;

        if (theta > 0x7FFF)
        {
            theta = (ushort)(0xFFFF - theta);
            scale = -1; // The back half of the turn is the front half, negated.
        }

        uint precomputed = (uint)theta * (uint)(0x7FFF - theta);
        ulong numerator = (ulong)precomputed * (4 * 0x7FFF);

        // 1342095361 is 5 * 0x7FFF^2 / 4.
        int denominator = 1342095361 - (int)precomputed;

        var result = (short)(numerator / (ulong)denominator);
        return (short)(result * scale);
    }

    /// <summary>Cosine, which is sine a quarter turn along.</summary>
    public static short Cos16(ushort theta) => Sin16((ushort)(theta + 0x4000));

    /// <summary>Sine over a byte: input 0-255, output 0-255 centered on 128.</summary>
    public static byte Sin8(byte theta)
    {
        int value = Sin16((ushort)(theta * 257)); // 255 * 257 == 0xFFFF
        value += 0x7FFF + 128;                    // to 0-0xFFFF, plus a half for rounding
        return (byte)(Math.Min(value, 0xFFFF) >> 8);
    }

    /// <summary>Cosine over a byte.</summary>
    public static byte Cos8(byte theta) => Sin8((byte)(theta + 64));

    /// <summary>
    /// Scales a byte by a fraction expressed as a byte: <c>i * scale / 256</c>.
    /// <para>
    /// The truncation matters. Effects lean on <c>scale8(i, 240)</c> to stop a palette lookup short
    /// of the end so it does not blend back round to the start, and doing that in floating point
    /// lands on different colors.
    /// </para>
    /// <para>
    /// The scale is <c>(i * (1 + scale)) &gt;&gt; 8</c>, not <c>i * scale / 256</c>. FastLED calls
    /// that its "fixed" scale8 and has defaulted to it since 3.x, so it is what the firmware
    /// actually runs. The two differ by at most one step, which is why nothing noticed for a
    /// while — but it is one step on every lookup, and the top of a palette lands a unit short
    /// without it.
    /// </para>
    /// </summary>
    public static byte Scale8(byte i, byte scale) => (byte)((i * (1 + scale)) >> 8);

    /// <summary>
    /// The same scaling, but a non-zero input never scales all the way to zero.
    /// <para>
    /// FastLED calls this the "video" variant and uses it wherever a value fading out should stay
    /// visible until it is meant to vanish. Without it a dim pixel snaps to black a step early.
    /// </para>
    /// </summary>
    public static byte Scale8Video(byte i, byte scale) =>
        (byte)(((i * scale) >> 8) + (i != 0 && scale != 0 ? 1 : 0));

    /// <summary>
    /// Where in a beat we are, 0-255, at a given tempo. WLED counts beats off the same clock the
    /// effects use, so this needs the same milliseconds they get.
    /// </summary>
    /// <param name="beatsPerMinute">Tempo. WLED hands the segment's speed straight in as a BPM.</param>
    public static byte Beat8(byte beatsPerMinute, uint now)
    {
        // beat88's fixed-point tempo: the BPM shifted up eight bits, times 280, over 2^16.
        uint tempo = (uint)beatsPerMinute << 8;

        uint beat16;
        unchecked
        {
            beat16 = (ushort)((now * tempo * 280) >> 16);
        }

        return (byte)(beat16 >> 8);
    }

    /// <summary>A sine at a given tempo, swinging between two values.</summary>
    public static byte BeatSin8(
        byte beatsPerMinute,
        byte lowest,
        byte highest,
        uint now,
        byte phaseOffset = 0)
    {
        byte beat = Beat8(beatsPerMinute, now);
        byte wave = Sin8((byte)(beat + phaseOffset));

        return (byte)(lowest + Scale8(wave, (byte)(highest - lowest)));
    }

    /// <summary>
    /// Where in a beat we are, 0-65535, at a tempo given in 8.8 fixed point.
    /// <para>
    /// The tempo is beats per minute shifted up eight bits, so 341 is about 1.3 BPM. Effects that
    /// want a slow drift rather than a pulse are written against this rather than against whole
    /// beats, because one beat a minute is already too fast for some of them.
    /// </para>
    /// </summary>
    public static ushort Beat88(ushort beatsPerMinute88, uint now)
    {
        unchecked
        {
            return (ushort)((now * (uint)beatsPerMinute88 * 280) >> 16);
        }
    }

    /// <summary>A sine at an 8.8 fixed-point tempo, swinging between two 16-bit values.</summary>
    public static ushort BeatSin88(
        ushort beatsPerMinute88,
        ushort lowest,
        ushort highest,
        uint now,
        ushort phaseOffset = 0)
    {
        ushort beat = Beat88(beatsPerMinute88, now);

        unchecked
        {
            var wave = (ushort)(Sin16((ushort)(beat + phaseOffset)) + 32768);
            return (ushort)(lowest + Scale16(wave, (ushort)(highest - lowest)));
        }
    }

    /// <summary>
    /// A 16-bit beat at a whole number of beats a minute.
    /// <para>
    /// Takes the tempo in beats a minute rather than in the 8.8 fixed point its sibling wants, and
    /// shifts it up itself - which is why an effect can pass <c>speed / 10</c> and get something
    /// sensible.
    /// </para>
    /// </summary>
    public static ushort Beat16(ushort beatsPerMinute, uint now) =>
        Beat88(beatsPerMinute < 256 ? (ushort)(beatsPerMinute << 8) : beatsPerMinute, now);

    /// <summary>A sine at a whole-number tempo, swinging between two 16-bit values.</summary>
    public static ushort BeatSin16(
        ushort beatsPerMinute,
        ushort lowest,
        ushort highest,
        uint now,
        ushort phaseOffset = 0)
    {
        ushort beat = Beat16(beatsPerMinute, now);

        unchecked
        {
            var wave = (ushort)(Sin16((ushort)(beat + phaseOffset)) + 32768);
            return (ushort)(lowest + Scale16(wave, (ushort)(highest - lowest)));
        }
    }

    /// <summary>
    /// <see cref="Scale8"/>'s wider sibling: a fraction of a 16-bit value.
    /// <para>
    /// Fixed the same way and for the same reason - <c>(i * (1 + scale)) / 65536</c>, not
    /// <c>i * scale / 65536</c>. One switch in FastLED turns both on, so a port that fixed the
    /// narrow one and not the wide one would be half right.
    /// </para>
    /// </summary>
    public static ushort Scale16(ushort i, ushort scale) =>
        (ushort)((uint)i * (1 + (uint)scale) / 65536);

    /// <summary>
    /// Hue, saturation and value to RGB, the way FastLED does it rather than the way a colour
    /// picker does.
    /// <para>
    /// This is the "rainbow" conversion, not the mathematically even one. It deliberately spends
    /// more of the hue range on yellow and boosts it, because pure yellow reads as brighter than
    /// any other colour and an even conversion makes it look like a dull band between red and
    /// green. Every effect that builds colours from a hue gets that bias, so a port without it
    /// comes out subtly wrong in exactly the place people look.
    /// </para>
    /// </summary>
    public static RgbColor Hsv2Rgb(byte hue, byte saturation, byte value)
    {
        var offset8 = (byte)((hue & 0x1F) << 3);
        byte third = Scale8(offset8, 256 / 3);

        byte r, g, b;

        unchecked
        {
            if ((hue & 0x80) == 0)
            {
                if ((hue & 0x40) == 0)
                {
                    if ((hue & 0x20) == 0)
                    {
                        // red toward orange
                        r = (byte)(255 - third);
                        g = third;
                        b = 0;
                    }
                    else
                    {
                        // orange toward yellow, held wide on purpose
                        r = 171;
                        g = (byte)(85 + third);
                        b = 0;
                    }
                }
                else if ((hue & 0x20) == 0)
                {
                    byte twoThirds = Scale8(offset8, 256 * 2 / 3);
                    r = (byte)(171 - twoThirds);
                    g = (byte)(170 + third);
                    b = 0;
                }
                else
                {
                    r = 0;
                    g = (byte)(255 - third);
                    b = third;
                }
            }
            else if ((hue & 0x40) == 0)
            {
                if ((hue & 0x20) == 0)
                {
                    byte twoThirds = Scale8(offset8, 256 * 2 / 3);
                    r = 0;
                    g = (byte)(171 - twoThirds);
                    b = (byte)(85 + twoThirds);
                }
                else
                {
                    r = third;
                    g = 0;
                    b = (byte)(255 - third);
                }
            }
            else if ((hue & 0x20) == 0)
            {
                r = (byte)(85 + third);
                g = 0;
                b = (byte)(171 - third);
            }
            else
            {
                r = (byte)(170 + third);
                g = 0;
                b = (byte)(85 - third);
            }

            if (saturation != 255)
            {
                if (saturation == 0)
                {
                    r = g = b = 255;
                }
                else
                {
                    byte desaturated = Scale8Video((byte)(255 - saturation), (byte)(255 - saturation));
                    var keep = (byte)(255 - desaturated);

                    r = Scale8(r, keep);
                    g = Scale8(g, keep);
                    b = Scale8(b, keep);

                    // Washing out raises the floor rather than only pulling the peaks down, which
                    // is what keeps a pastel from also going dim.
                    r += desaturated;
                    g += desaturated;
                    b += desaturated;
                }
            }

            if (value != 255)
            {
                byte scaled = Scale8Video(value, value);

                if (scaled == 0)
                {
                    r = g = b = 0;
                }
                else
                {
                    r = Scale8(r, scaled);
                    g = Scale8(g, scaled);
                    b = Scale8(b, scaled);
                }
            }
        }

        return new RgbColor(r, g, b);
    }

    /// <summary>
    /// The same triangle wave over sixteen bits, which is what the effects that need a shape
    /// smoother than an LED apart use.
    /// </summary>
    public static ushort TriWave16(ushort input) =>
        input < 0x8000 ? (ushort)(input * 2) : (ushort)(0xFFFF - ((input - 0x8000) * 2));

    /// <summary>A symmetrical triangle wave over a byte: up to 255 and back down.</summary>
    public static byte TriWave8(byte input)
    {
        if ((input & 0x80) != 0)
        {
            input = (byte)(255 - input);
        }

        return (byte)(input << 1);
    }

    /// <summary>
    /// A half sine that sits at zero for half its input, so what it drives has gaps between pulses.
    /// <para>
    /// Takes a full sixteen bits and looks only at bit 8: any input with that bit set returns zero,
    /// which is every other run of 256. The rest is a sine shifted by three quarters of a turn so
    /// the pulse starts and ends at zero rather than stepping in and out of one.
    /// </para>
    /// </summary>
    public static byte SinGap(ushort input) =>
        (input & 0x100) != 0 ? (byte)0 : Sin8((byte)(input + 192));

    /// <summary>
    /// Arduino's <c>map</c>: rescales <paramref name="value"/> from one range onto another with
    /// integer division, so it truncates toward zero rather than rounding.
    /// </summary>
    public static int Map(int value, int fromLow, int fromHigh, int toLow, int toHigh) =>
        ((value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow)) + toLow;

    /// <summary>
    /// A square wave with sloped sides and a rest between pulses, which is how Washing Machine gets
    /// forward, pause, backward out of one function.
    /// <para>
    /// Signed: the back half of the input gives the pulse upside down. Within each half it ramps up
    /// over <paramref name="attack"/>, holds, ramps back down, and then sits at zero for whatever is
    /// left of the half - so the pause is whatever the pulse width does not use.
    /// </para>
    /// </summary>
    public static int TristateSquare8(byte x, byte pulseWidth, byte attack)
    {
        int amplitude = 127;

        if (x > 127)
        {
            amplitude = -127;
            x -= 127;
        }

        if (x < attack)
        {
            return x * amplitude / attack;
        }

        if (x < pulseWidth - attack)
        {
            return amplitude;
        }

        if (x < pulseWidth)
        {
            return (pulseWidth - x) * amplitude / attack;
        }

        return 0;
    }

    /// <summary>Eases a value in and out on a quadratic, which is flatter at the ends than the cubic.</summary>
    public static byte Ease8InOutQuad(byte i)
    {
        byte j = i;

        if ((j & 0x80) != 0)
        {
            j = (byte)(255 - j);
        }

        byte squared = Scale8(j, j);
        var doubled = (byte)(squared << 1);

        return (i & 0x80) != 0 ? (byte)(255 - doubled) : doubled;
    }

    /// <summary>A triangle wave eased at its corners on a quadratic - <c>quadwave8</c>.</summary>
    public static byte QuadWave8(byte input) => Ease8InOutQuad(TriWave8(input));

    /// <summary>A triangle wave with its corners rounded off, which reads as a swell rather than a ramp.</summary>
    public static byte CubicWave8(byte input) => EaseInOutCubic8(TriWave8(input));

    /// <summary>
    /// Eases a value in and out, the cubic curve FastLED uses - <c>3x&#178; - 2x&#179;</c>.
    /// <para>
    /// Both powers are taken with <see cref="Scale8"/> rather than by multiplying and shifting, which
    /// is not the same thing: the fixed scaling multiplies by one more than the scale, so squaring 200
    /// gives 157 where a plain multiply gives 156. Off by one in the wrong direction here bends the
    /// whole curve, because the cube is taken from the square.
    /// </para>
    /// </summary>
    public static byte EaseInOutCubic8(byte i)
    {
        byte squared = Scale8(i, i);
        byte cubed = Scale8(squared, i);

        int result = (3 * squared) - (2 * cubed);

        return (byte)Math.Min(result, 255);
    }

    /// <summary>Adds without going past 255, FastLED's <c>qadd8</c>.</summary>
    public static byte QAdd8(byte from, int amount) => (byte)Math.Min(255, from + amount);

    /// <summary>Subtraction that stops at zero instead of wrapping round to 255.</summary>
    public static byte QSub8(byte from, int amount)
    {
        int result = from - amount;
        return (byte)(result < 0 ? 0 : result);
    }

    /// <summary>Moves one channel a fraction of the way toward another, never stalling short of it.</summary>
    /// <param name="mappedRate">How far to move, out of 256.</param>
    public static byte FadeChannel(byte from, byte to, int mappedRate)
    {
        int delta = (to - from) * mappedRate / 256;

        // Without this a fade rounds to zero while still a shade off and stops there forever.
        if (delta == 0)
        {
            delta = to == from ? 0 : to > from ? 1 : -1;
        }

        return (byte)(from + delta);
    }
}
