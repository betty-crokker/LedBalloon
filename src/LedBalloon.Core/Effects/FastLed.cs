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
    /// </summary>
    public static byte Scale8(byte i, byte scale) => (byte)(i * scale / 256);

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

    /// <summary>A symmetrical triangle wave over a byte: up to 255 and back down.</summary>
    public static byte TriWave8(byte input)
    {
        if ((input & 0x80) != 0)
        {
            input = (byte)(255 - input);
        }

        return (byte)(input << 1);
    }

    /// <summary>A triangle wave with its corners rounded off, which reads as a swell rather than a ramp.</summary>
    public static byte CubicWave8(byte input) => EaseInOutCubic8(TriWave8(input));

    /// <summary>Eases a value in and out, the cubic curve FastLED uses.</summary>
    public static byte EaseInOutCubic8(byte i)
    {
        int squared = i * i / 256;
        int cubed = squared * i / 256;

        int result = (3 * squared) - (2 * cubed);

        return (byte)Math.Min(result, 255);
    }

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
