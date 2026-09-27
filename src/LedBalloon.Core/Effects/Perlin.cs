namespace LedBalloon.Core.Effects;

/// <summary>
/// FastLED's eight-bit Perlin noise, which a dozen of WLED's effects are built on.
/// <para>
/// Not a random number generator. It is a smooth function of position - ask it about two nearby
/// points and you get two nearby answers - which is what makes noise-driven effects drift and swirl
/// rather than flicker. That also means it can be ported exactly rather than approximated
/// statistically, unlike anything built on a random draw: the same coordinates give the same answer
/// on the controller and here.
/// </para>
/// <para>
/// Ported from FastLED 3.6.0, which is the version WLED 0.15.3 pins. The arithmetic is eight-bit
/// throughout and deliberately lossy - gradients are averaged with a rounding rule that favors one
/// operand, and the interpolation curve is a quadratic ease rather than Perlin's original quintic.
/// Those choices are visible in the output, so they are reproduced rather than improved on.
/// </para>
/// </summary>
public static class Perlin
{
    /// <summary>
    /// Ken Perlin's permutation table, with its first entry repeated at the end.
    /// <para>
    /// The repeat is what lets the lookup index one past the end without wrapping by hand: a corner
    /// at 255 needs its neighbor at 256.
    /// </para>
    /// </summary>
    private static readonly byte[] P =
    [
        151, 160, 137,  91,  90,  15, 131,  13, 201,  95,  96,  53, 194, 233,   7, 225,
        140,  36, 103,  30,  69, 142,   8,  99,  37, 240,  21,  10,  23, 190,   6, 148,
        247, 120, 234,  75,   0,  26, 197,  62,  94, 252, 219, 203, 117,  35,  11,  32,
         57, 177,  33,  88, 237, 149,  56,  87, 174,  20, 125, 136, 171, 168,  68, 175,
         74, 165,  71, 134, 139,  48,  27, 166,  77, 146, 158, 231,  83, 111, 229, 122,
         60, 211, 133, 230, 220, 105,  92,  41,  55,  46, 245,  40, 244, 102, 143,  54,
         65,  25,  63, 161,   1, 216,  80,  73, 209,  76, 132, 187, 208,  89,  18, 169,
        200, 196, 135, 130, 116, 188, 159,  86, 164, 100, 109, 198, 173, 186,   3,  64,
         52, 217, 226, 250, 124, 123,   5, 202,  38, 147, 118, 126, 255,  82,  85, 212,
        207, 206,  59, 227,  47,  16,  58,  17, 182, 189,  28,  42, 223, 183, 170, 213,
        119, 248, 152,   2,  44, 154, 163,  70, 221, 153, 101, 155, 167,  43, 172,   9,
        129,  22,  39, 253,  19,  98, 108, 110,  79, 113, 224, 232, 178, 185, 112, 104,
        218, 246,  97, 228, 251,  34, 242, 193, 238, 210, 144,  12, 191, 179, 162, 241,
         81,  51, 145, 235, 249,  14, 239, 107,  49, 192, 214,  31, 181, 199, 106, 157,
        184,  84, 204, 176, 115, 121,  50,  45, 127,   4, 150, 254, 138, 236, 205,  93,
        222, 114,  67,  29,  24,  72, 243, 141, 128, 195,  78,  66, 215,  61, 156, 180,
        151,
    ];

    /// <summary>Noise along a line: 0 to 255, averaging 128, smooth over each 256 of input.</summary>
    public static byte Noise8(ushort x) => Spread(Raw(x));

    /// <summary>Noise over a plane, which is how most of the effects use it - one axis is time.</summary>
    public static byte Noise8(ushort x, ushort y) => Spread(Raw(x, y));

    /// <summary>Noise through a volume, for the effects that move a plane through it.</summary>
    public static byte Noise8(ushort x, ushort y, ushort z) => Spread(Raw(x, y, z));

    /// <summary>
    /// Noise over a plane at sixteen bits, where the top sixteen of each coordinate pick the cell and
    /// the bottom sixteen say where in it.
    /// <para>
    /// Not the eight-bit function widened. It has its own gradients, its own interpolation and its
    /// own way of spreading the result over the range - a multiply by 484 and a shift rather than a
    /// saturating double - so the two give visibly different fields for the same walk.
    /// </para>
    /// </summary>
    public static ushort Noise16(uint x, uint y)
    {
        int raw = Raw16(x, y) + 17308;
        return (ushort)(((uint)raw * 484u) >> 8);
    }

    /// <summary>Noise through a volume at sixteen bits, which is what three of the four Noise effects use.</summary>
    public static ushort Noise16(uint x, uint y, uint z)
    {
        int raw = Raw16(x, y, z) + 19052;
        return (ushort)(((uint)raw * 440u) >> 8);
    }

    private static short Raw16(uint x, uint y)
    {
        var gridX = (byte)(x >> 16);
        var gridY = (byte)(y >> 16);

        var cornerA = (byte)(P[gridX] + gridY);
        byte aa = P[P[cornerA]];
        byte ab = P[P[cornerA + 1]];

        var cornerB = (byte)(P[gridX + 1] + gridY);
        byte ba = P[P[cornerB]];
        byte bb = P[P[cornerB + 1]];

        var u = (ushort)x;
        var v = (ushort)y;

        var alongX = (short)((u >> 1) & 0x7FFF);
        var alongY = (short)((v >> 1) & 0x7FFF);
        var backX = (short)(alongX - 32768);
        var backY = (short)(alongY - 32768);

        u = Ease16(u);
        v = Ease16(v);

        short x1 = Lerp16(Gradient16(aa, alongX, alongY), Gradient16(ba, backX, alongY), u);
        short x2 = Lerp16(Gradient16(ab, alongX, backY), Gradient16(bb, backX, backY), u);

        return Lerp16(x1, x2, v);
    }

    private static short Raw16(uint x, uint y, uint z)
    {
        var gridX = (byte)(x >> 16);
        var gridY = (byte)(y >> 16);
        var gridZ = (byte)(z >> 16);

        var cornerA = (byte)(P[gridX] + gridY);
        var aa = (byte)(P[cornerA] + gridZ);
        var ab = (byte)(P[cornerA + 1] + gridZ);

        var cornerB = (byte)(P[gridX + 1] + gridY);
        var ba = (byte)(P[cornerB] + gridZ);
        var bb = (byte)(P[cornerB + 1] + gridZ);

        var u = (ushort)x;
        var v = (ushort)y;
        var w = (ushort)z;

        var alongX = (short)((u >> 1) & 0x7FFF);
        var alongY = (short)((v >> 1) & 0x7FFF);
        var alongZ = (short)((w >> 1) & 0x7FFF);
        var backX = (short)(alongX - 32768);
        var backY = (short)(alongY - 32768);
        var backZ = (short)(alongZ - 32768);

        u = Ease16(u);
        v = Ease16(v);
        w = Ease16(w);

        short x1 = Lerp16(Gradient16(P[aa], alongX, alongY, alongZ), Gradient16(P[ba], backX, alongY, alongZ), u);
        short x2 = Lerp16(Gradient16(P[ab], alongX, backY, alongZ), Gradient16(P[bb], backX, backY, alongZ), u);
        short x3 = Lerp16(Gradient16(P[aa + 1], alongX, alongY, backZ), Gradient16(P[ba + 1], backX, alongY, backZ), u);
        short x4 = Lerp16(Gradient16(P[ab + 1], alongX, backY, backZ), Gradient16(P[bb + 1], backX, backY, backZ), u);

        short y1 = Lerp16(x1, x2, v);
        short y2 = Lerp16(x3, x4, v);

        return Lerp16(y1, y2, w);
    }

    /// <summary>
    /// The sixteen-bit gradients. Two axes, and which is which comes from the bottom three bits of
    /// the hash rather than the fourth - so this is not the eight-bit version scaled up.
    /// </summary>
    private static short Gradient16(byte hash, short x, short y)
    {
        int masked = hash & 7;

        int u = masked < 4 ? x : y;
        int v = masked < 4 ? y : x;

        return Average16(Signed16(masked, 1, u), Signed16(masked, 2, v));
    }

    private static short Gradient16(byte hash, short x, short y, short z)
    {
        int masked = hash & 15;

        int u = masked < 8 ? x : y;
        int v = masked < 4 ? y : masked is 12 or 14 ? x : z;

        return Average16(Signed16(masked, 1, u), Signed16(masked, 2, v));
    }

    private static short Signed16(int hash, int bit, int value) =>
        unchecked((short)((hash & bit) != 0 ? -value : value));

    /// <summary>The average of two signed fifteens, rounded toward the first - <c>avg15</c>.</summary>
    private static short Average16(short first, short second) =>
        (short)((first >> 1) + (second >> 1) + (first & 0x1));

    /// <summary>Interpolates between two signed fifteens by a fraction of 65536.</summary>
    private static short Lerp16(short from, short to, ushort fraction)
    {
        if (to > from)
        {
            var up = (ushort)(to - from);
            return (short)(from + FastLed.Scale16(up, fraction));
        }

        var down = (ushort)(from - to);
        return (short)(from - FastLed.Scale16(down, fraction));
    }

    /// <summary>The sixteen-bit easing curve, the same quadratic shape as its narrow sibling.</summary>
    private static ushort Ease16(ushort i)
    {
        ushort j = i;

        if ((j & 0x8000) != 0)
        {
            j = (ushort)(65535 - j);
        }

        ushort squared = FastLed.Scale16(j, j);
        var doubled = (ushort)(squared << 1);

        return (i & 0x8000) != 0 ? (ushort)(65535 - doubled) : doubled;
    }

    /// <summary>
    /// The raw noise only spans -64 to +64, so it is shifted and doubled to fill a byte.
    /// <para>
    /// Doubled with a saturating add rather than a shift, which is why the result can sit at 255 for
    /// a whole stretch rather than only touching it - a small but visible flattening at the top.
    /// </para>
    /// </summary>
    private static byte Spread(sbyte raw)
    {
        var shifted = (byte)(raw + 64);
        return FastLed.QAdd8(shifted, shifted);
    }

    private static sbyte Raw(ushort x)
    {
        var gridX = (byte)(x >> 8);

        // Three lookups deep, not two. The table is walked once to hash the cell, once to hash that,
        // and once more on the way into the gradient - and stopping a level short gives noise that
        // looks plausible and is not the same function.
        byte near = P[P[P[gridX]]];
        byte far = P[P[P[gridX + 1]]];

        var alongX = (sbyte)((byte)x >> 1);

        byte u = Ease(unchecked((byte)x));

        return Lerp(Gradient(near, alongX), Gradient(far, (sbyte)(alongX - 128)), u);
    }

    private static sbyte Raw(ushort x, ushort y)
    {
        var gridX = (byte)(x >> 8);
        var gridY = (byte)(y >> 8);

        var cornerA = (byte)(P[gridX] + gridY);
        byte aa = P[P[cornerA]];
        byte ab = P[P[cornerA + 1]];

        var cornerB = (byte)(P[gridX + 1] + gridY);
        byte ba = P[P[cornerB]];
        byte bb = P[P[cornerB + 1]];

        var alongX = (sbyte)((byte)x >> 1);
        var alongY = (sbyte)((byte)y >> 1);
        var backX = (sbyte)(alongX - 128);
        var backY = (sbyte)(alongY - 128);

        byte u = Ease(unchecked((byte)x));
        byte v = Ease(unchecked((byte)y));

        sbyte x1 = Lerp(Gradient(aa, alongX, alongY), Gradient(ba, backX, alongY), u);
        sbyte x2 = Lerp(Gradient(ab, alongX, backY), Gradient(bb, backX, backY), u);

        return Lerp(x1, x2, v);
    }

    private static sbyte Raw(ushort x, ushort y, ushort z)
    {
        var gridX = (byte)(x >> 8);
        var gridY = (byte)(y >> 8);
        var gridZ = (byte)(z >> 8);

        var cornerA = (byte)(P[gridX] + gridY);
        var aa = (byte)(P[cornerA] + gridZ);
        var ab = (byte)(P[cornerA + 1] + gridZ);

        var cornerB = (byte)(P[gridX + 1] + gridY);
        var ba = (byte)(P[cornerB] + gridZ);
        var bb = (byte)(P[cornerB + 1] + gridZ);

        var alongX = (sbyte)((byte)x >> 1);
        var alongY = (sbyte)((byte)y >> 1);
        var alongZ = (sbyte)((byte)z >> 1);

        var backX = (sbyte)(alongX - 128);
        var backY = (sbyte)(alongY - 128);
        var backZ = (sbyte)(alongZ - 128);

        byte u = Ease(unchecked((byte)x));
        byte v = Ease(unchecked((byte)y));
        byte w = Ease(unchecked((byte)z));

        sbyte x1 = Lerp(Gradient(P[aa], alongX, alongY, alongZ), Gradient(P[ba], backX, alongY, alongZ), u);
        sbyte x2 = Lerp(Gradient(P[ab], alongX, backY, alongZ), Gradient(P[bb], backX, backY, alongZ), u);
        sbyte x3 = Lerp(Gradient(P[aa + 1], alongX, alongY, backZ), Gradient(P[ba + 1], backX, alongY, backZ), u);
        sbyte x4 = Lerp(Gradient(P[ab + 1], alongX, backY, backZ), Gradient(P[bb + 1], backX, backY, backZ), u);

        sbyte y1 = Lerp(x1, x2, v);
        sbyte y2 = Lerp(x3, x4, v);

        return Lerp(y1, y2, w);
    }

    /// <summary>
    /// The gradient at one corner of the cell, chosen by hashing the corner's coordinates.
    /// <para>
    /// Three of them, one per dimension, and they are not variations on a theme - each picks which
    /// axes contribute and which way round from different bits of the hash. Reproduced as written,
    /// including the one-dimensional case's constant 1, which makes it less even than the others.
    /// </para>
    /// </summary>
    private static sbyte Gradient(byte hash, sbyte x)
    {
        int u, v;

        if ((hash & 8) != 0)
        {
            u = x;
            v = x;
        }
        else if ((hash & 4) != 0)
        {
            u = 1;
            v = x;
        }
        else
        {
            u = x;
            v = 1;
        }

        return Average(Signed(hash, 1, u), Signed(hash, 2, v));
    }

    private static sbyte Gradient(byte hash, sbyte x, sbyte y)
    {
        int u = (hash & 4) != 0 ? y : x;
        int v = (hash & 4) != 0 ? x : y;

        return Average(Signed(hash, 1, u), Signed(hash, 2, v));
    }

    private static sbyte Gradient(byte hash, sbyte x, sbyte y, sbyte z)
    {
        int masked = hash & 0xF;

        int u = (masked & 8) != 0 ? y : x;
        int v = masked < 4 ? y : masked is 12 or 14 ? x : z;

        return Average(Signed(masked, 1, u), Signed(masked, 2, v));
    }

    /// <summary>Negates when the given bit of the hash is set, which is how a gradient gets a sign.</summary>
    private static sbyte Signed(int hash, int bit, int value) =>
        unchecked((sbyte)((hash & bit) != 0 ? -value : value));

    /// <summary>
    /// The average of two signed sevens, FastLED's <c>avg7</c>.
    /// <para>
    /// Rounded by adding back the first operand's low bit rather than either's, so it is very
    /// slightly biased toward one of the two. That asymmetry is in the output.
    /// </para>
    /// </summary>
    private static sbyte Average(sbyte first, sbyte second) =>
        (sbyte)((first >> 1) + (second >> 1) + (first & 0x1));

    /// <summary>Interpolates between two signed sevens by a fraction of 256.</summary>
    private static sbyte Lerp(sbyte from, sbyte to, byte fraction)
    {
        if (to > from)
        {
            var up = (byte)(to - from);
            return (sbyte)(from + FastLed.Scale8(up, fraction));
        }

        var down = (byte)(from - to);
        return (sbyte)(from - FastLed.Scale8(down, fraction));
    }

    /// <summary>
    /// The curve that smooths the step between cells: a quadratic ease in and out.
    /// <para>
    /// Perlin's own noise uses a quintic, which is smoother still. FastLED's quadratic leaves a
    /// faint seam at every cell boundary, and that seam is part of how noise-driven effects look.
    /// </para>
    /// </summary>
    private static byte Ease(byte i) => FastLed.Ease8InOutQuad(i);
}
