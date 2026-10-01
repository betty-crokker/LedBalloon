using System.Globalization;
using System.Text;
using System.Text.Json;
using LedBalloon.Core.Models;

namespace LedBalloon.Core.Layout;

/// <summary>
/// Reads and writes the <c>paletteN.json</c> files a controller keeps its own palettes in.
/// <para>
/// Not the same shape as <c>/json/palx</c>, which is what <see cref="WledPalette"/> parses. The
/// endpoint hands back sixteen evenly spaced stops as <c>[position, r, g, b]</c> arrays, because
/// that is what the firmware has after it has expanded the file. The file itself is a flat list of
/// whatever stops someone wrote, and it is the file that has to be edited.
/// </para>
/// </summary>
/// <remarks>
/// WLED accepts two spellings of a stop and this reads both: a position followed by a
/// <c>"RRGGBB"</c> string, which is what both controllers here hold, or a position followed by three
/// numbers, which is what WLED's own editor starts a new palette with. Written back as hex, to match
/// what is already on the hardware.
/// </remarks>
public static class CustomPaletteFile
{
    /// <summary>What WLED's own editor allows, and what the firmware expands from.</summary>
    public const int MaxStops = 16;

    /// <summary>Reads a palette file, or an empty list when it is not one.</summary>
    public static IReadOnlyList<PaletteStop> Parse(ReadOnlySpan<byte> content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content.ToArray());

            if (!document.RootElement.TryGetProperty("palette", out JsonElement palette) ||
                palette.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return Read([.. palette.EnumerateArray()]);
        }
        catch (JsonException)
        {
            // A file that is not JSON is not a palette. The caller shows what it has rather than
            // failing the whole read for one bad slot.
            return [];
        }
    }

    private static List<PaletteStop> Read(JsonElement[] items)
    {
        var stops = new List<PaletteStop>();

        for (int i = 0; i < items.Length;)
        {
            if (!items[i].TryGetInt32(out int position))
            {
                break;
            }

            if (i + 1 < items.Length && items[i + 1].ValueKind == JsonValueKind.String)
            {
                if (FromHex(items[i + 1].GetString()) is { } color)
                {
                    stops.Add(new PaletteStop(Clamp(position), color));
                }

                i += 2;
                continue;
            }

            if (i + 3 < items.Length &&
                items[i + 1].TryGetInt32(out int r) &&
                items[i + 2].TryGetInt32(out int g) &&
                items[i + 3].TryGetInt32(out int b))
            {
                stops.Add(new PaletteStop(
                    Clamp(position),
                    new RgbColor((byte)Clamp(r), (byte)Clamp(g), (byte)Clamp(b))));

                i += 4;
                continue;
            }

            break;
        }

        return stops;
    }

    /// <summary>Writes the stops as WLED reads them, lowest position first.</summary>
    public static byte[] Write(IEnumerable<PaletteStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);

        var text = new StringBuilder("{\"palette\":[");
        bool first = true;

        foreach (PaletteStop stop in stops.OrderBy(s => s.Position).Take(MaxStops))
        {
            if (!first)
            {
                text.Append(',');
            }

            first = false;

            // The six digits written out rather than taken from ToHex, which leads with a hash and
            // grows a fourth pair for a color that carries a white channel. A palette file holds
            // neither.
            text.Append(stop.Position.ToString(CultureInfo.InvariantCulture))
                .Append(",\"")
                .Append(stop.Color.R.ToString("x2", CultureInfo.InvariantCulture))
                .Append(stop.Color.G.ToString("x2", CultureInfo.InvariantCulture))
                .Append(stop.Color.B.ToString("x2", CultureInfo.InvariantCulture))
                .Append('"');
        }

        return Encoding.UTF8.GetBytes(text.Append("]}").ToString());
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private static RgbColor? FromHex(string? text)
    {
        if (text is null)
        {
            return null;
        }

        ReadOnlySpan<char> digits = text.AsSpan().TrimStart('#');

        return digits.Length == 6 &&
            byte.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(digits[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)
                ? new RgbColor(r, g, b)
                : null;
    }
}
