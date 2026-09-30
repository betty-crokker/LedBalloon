namespace LedBalloon.Core.Models;

/// <summary>An 8-bit-per-channel color, with the optional white channel WLED uses for RGBW strips.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B, byte W = 0)
{
    public static RgbColor Black => new(0, 0, 0);
    public static RgbColor White => new(255, 255, 255);

    /// <summary>True when this color carries a non-zero dedicated white channel.</summary>
    public bool HasWhite => W != 0;

    /// <summary>Parses "#RRGGBB", "RRGGBB", "#RRGGBBWW" or "RRGGBBWW".</summary>
    public static RgbColor Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        ReadOnlySpan<char> s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
        {
            s = s[1..];
        }

        if (s.Length is not (6 or 8))
        {
            throw new FormatException($"Expected 6 or 8 hex digits, got '{hex}'.");
        }

        const System.Globalization.NumberStyles Style = System.Globalization.NumberStyles.HexNumber;
        byte r = byte.Parse(s.Slice(0, 2), Style);
        byte g = byte.Parse(s.Slice(2, 2), Style);
        byte b = byte.Parse(s.Slice(4, 2), Style);
        byte w = s.Length == 8 ? byte.Parse(s.Slice(6, 2), Style) : (byte)0;

        return new RgbColor(r, g, b, w);
    }

    /// <summary>Reads a color, or returns false. Never throws for the ordinary absence of one.</summary>
    /// <remarks>
    /// The empty case is answered before <see cref="Parse"/> is asked, because it is not a failure
    /// and not rare: an appearance carries three color slots and most of them are null, so
    /// resolving one scene used to throw and catch an ArgumentNullException a dozen times over. It
    /// cost nothing visible and made the debugger stop on every scene anyone selected, which is the
    /// kind of noise a real fault then hides in.
    /// <para>
    /// A malformed string is still exceptional and is still caught, but narrowly - the bare catch
    /// here swallowed everything that could possibly go wrong, including what should not be caught
    /// at all.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? hex, out RgbColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = Parse(hex);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            color = default;
            return false;
        }
    }

    /// <summary>The wire form WLED expects inside a segment's <c>col</c> array.</summary>
    public int[] ToWledArray() => HasWhite ? [R, G, B, W] : [R, G, B];

    /// <summary>Reads one entry out of a segment's <c>col</c> array. Tolerates 3- and 4-element forms.</summary>
    public static RgbColor FromWledArray(IReadOnlyList<int>? values)
    {
        if (values is null || values.Count < 3)
        {
            return Black;
        }

        static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
        return new RgbColor(
            Clamp(values[0]),
            Clamp(values[1]),
            Clamp(values[2]),
            values.Count > 3 ? Clamp(values[3]) : (byte)0);
    }

    public string ToHex() => HasWhite
        ? $"#{R:X2}{G:X2}{B:X2}{W:X2}"
        : $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();
}
