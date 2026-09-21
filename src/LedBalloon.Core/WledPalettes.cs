using System.Text.Json;
using LedBalloon.Core.Models;

namespace LedBalloon.Core;

/// <summary>One stop in a palette gradient: where it sits (0-255) and what color it is.</summary>
public readonly record struct PaletteStop(byte Position, RgbColor Color);

/// <summary>
/// The actual colors behind a palette index.
/// <para>
/// A palette name is not enough to draw with. "July4th" on the development hardware is a red
/// primary color with a palette doing the red, white and blue — draw only the primary and the
/// preview says solid red, which is both wrong and unhelpful.
/// </para>
/// <para>
/// Some palettes are not fixed gradients at all: WLED describes them as taking the segment's own
/// color slots, or as random. Those are resolved per segment rather than baked in here.
/// </para>
/// </summary>
public sealed class WledPalette
{
    /// <summary>Gradient stops, empty when this palette is defined in terms of something else.</summary>
    public IReadOnlyList<PaletteStop> Stops { get; init; } = [];

    /// <summary>
    /// Placeholders in WLED's own order, where "c1", "c2", "c3" mean the segment's color slots and
    /// "r" means random. Present instead of <see cref="Stops"/> for the palettes that build
    /// themselves from whatever the segment is set to.
    /// </summary>
    public IReadOnlyList<string> Placeholders { get; init; } = [];

    public bool IsGradient => Stops.Count > 0;

    /// <summary>
    /// The color this palette shows at a fraction <paramref name="t"/> along a run, given the
    /// segment's own colors for the palettes that are defined in terms of them.
    /// </summary>
    public RgbColor ColorAt(double t, RgbColor primary, RgbColor secondary, RgbColor tertiary)
    {
        t = Math.Clamp(t, 0, 1);

        if (Placeholders.Count > 0)
        {
            return FromPlaceholders(t, primary, secondary, tertiary);
        }

        if (Stops.Count == 0)
        {
            return primary;
        }

        if (Stops.Count == 1)
        {
            return Stops[0].Color;
        }

        double position = t * 255;

        for (int i = 0; i < Stops.Count - 1; i++)
        {
            PaletteStop from = Stops[i];
            PaletteStop to = Stops[i + 1];

            if (position > to.Position)
            {
                continue;
            }

            double span = to.Position - from.Position;
            double local = span <= 0 ? 0 : (position - from.Position) / span;

            return Lerp(from.Color, to.Color, Math.Clamp(local, 0, 1));
        }

        return Stops[^1].Color;
    }

    private RgbColor FromPlaceholders(double t, RgbColor primary, RgbColor secondary, RgbColor tertiary)
    {
        // Evenly spaced bands, which is how these read across a run even though WLED blends them.
        int index = Math.Clamp((int)(t * Placeholders.Count), 0, Placeholders.Count - 1);

        return Placeholders[index] switch
        {
            "c1" => primary,
            "c2" => secondary,
            "c3" => tertiary,
            _ => primary,
        };
    }

    private static RgbColor Lerp(RgbColor from, RgbColor to, double t) => new(
        (byte)(from.R + ((to.R - from.R) * t)),
        (byte)(from.G + ((to.G - from.G) * t)),
        (byte)(from.B + ((to.B - from.B) * t)));
}

/// <summary>
/// Reads the palette gradients a controller actually has, from <c>/json/palx</c>.
/// <para>
/// Paginated, because the whole set does not fit in one response on a microcontroller. Each
/// response carries <c>m</c>, which is the <em>last</em> page number rather than how many there
/// are. Reading it as a count stops one page early, and the page that gets dropped is the last
/// one - which is where a controller's custom palettes live, since those are numbered down from
/// 255. On a house running a custom red-white-and-blue palette that is the only page that
/// mattered, and losing it drew the whole run flat red.
/// </para>
/// </summary>
public static class WledPalettes
{
    public static async Task<IReadOnlyDictionary<int, WledPalette>> LoadAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        using var http = new HttpClient
        {
            BaseAddress = WledClient.NormalizeHost(host),
            Timeout = TimeSpan.FromSeconds(10),
        };

        var palettes = new Dictionary<int, WledPalette>();
        int lastPage = 0;

        for (int page = 0; page <= lastPage; page++)
        {
            using HttpResponseMessage response = await http
                .GetAsync($"json/palx?page={page}", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                break;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("m", out JsonElement last) &&
                last.TryGetInt32(out int number))
            {
                lastPage = Math.Clamp(number, 0, 31);
            }

            if (!document.RootElement.TryGetProperty("p", out JsonElement entries))
            {
                continue;
            }

            foreach (JsonProperty entry in entries.EnumerateObject())
            {
                if (int.TryParse(entry.Name, out int index))
                {
                    palettes[index] = Parse(entry.Value);
                }
            }
        }

        return palettes;
    }

    private static WledPalette Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return new WledPalette();
        }

        var stops = new List<PaletteStop>();
        var placeholders = new List<string>();

        foreach (JsonElement item in value.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    placeholders.Add(item.GetString() ?? "c1");
                    break;

                case JsonValueKind.Array when item.GetArrayLength() >= 4:
                    int[] parts = [.. item.EnumerateArray().Select(n => n.TryGetInt32(out int v) ? v : 0)];
                    stops.Add(new PaletteStop(
                        (byte)Math.Clamp(parts[0], 0, 255),
                        new RgbColor(
                            (byte)Math.Clamp(parts[1], 0, 255),
                            (byte)Math.Clamp(parts[2], 0, 255),
                            (byte)Math.Clamp(parts[3], 0, 255))));
                    break;
            }
        }

        return new WledPalette { Stops = stops, Placeholders = placeholders };
    }
}
