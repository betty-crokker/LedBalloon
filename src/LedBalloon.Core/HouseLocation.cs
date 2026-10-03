using System.Globalization;
using System.Text.RegularExpressions;

namespace LedBalloon.Core;

/// <summary>
/// One of WLED's fixed time zones, by the number it stores.
/// </summary>
/// <remarks>
/// A closed list rather than anything derived from this machine: the controller computes sunrise and
/// sunset itself, from its own clock, and it only understands these. A zone the PC knows about and
/// WLED does not is not a zone the house can keep.
/// </remarks>
/// <param name="Id">WLED's own number for it, which is what goes in cfg.json.</param>
/// <param name="Name">What it is called here.</param>
public sealed record WledTimeZone(int Id, string Name)
{
    public override string ToString() => Name;

    /// <summary>Every zone WLED 0.15.3 has, in its own order. TZ_COUNT is 24.</summary>
    public static IReadOnlyList<WledTimeZone> All { get; } =
    [
        new(0, "UTC"),
        new(1, "United Kingdom"),
        new(2, "Europe — central"),
        new(3, "Europe — eastern"),
        new(4, "US — eastern"),
        new(5, "US — central"),
        new(6, "US — mountain"),
        new(7, "US — Arizona"),
        new(8, "US — Pacific"),
        new(9, "China"),
        new(10, "Japan"),
        new(11, "Australia — eastern"),
        new(12, "New Zealand"),
        new(13, "North Korea"),
        new(14, "India"),
        new(15, "Saskatchewan"),
        new(16, "Australia — northern"),
        new(17, "Australia — southern"),
        new(18, "Hawaii"),
        new(19, "Novosibirsk"),
        new(20, "Anchorage"),
        new(21, "Mexico — central"),
        new(22, "Pakistan"),
        new(23, "Brasilia"),
    ];

    /// <summary>The zone with that id, or null when the controller reports one we do not know.</summary>
    public static WledTimeZone? ById(int? id) => All.FirstOrDefault(zone => zone.Id == id);
}

/// <summary>
/// Where the house is, as the controllers need it.
/// <para>
/// Only sunrise and sunset timers read this, and that is the whole reason it is here: a timer set
/// for sunset is computed on the controller, from this, so a house with no location gets a sun timer
/// that never fires and says nothing about why.
/// </para>
/// </summary>
/// <param name="Latitude">Degrees north, negative for south.</param>
/// <param name="Longitude">Degrees east, negative for west.</param>
public readonly partial record struct HouseLocation(double Latitude, double Longitude)
{
    /// <summary>True when both are inside the range the earth has.</summary>
    public bool IsOnEarth =>
        Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;

    /// <summary>The pair as it is usually seen, which is also what this will read back.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Latitude:0.######}, {Longitude:0.######}");

    /// <summary>
    /// Reads a location out of whatever was pasted in.
    /// <para>
    /// Pasting is the whole interface for this. Google Maps puts
    /// <c>22.694768, 114.283689</c> on the clipboard when you right-click a spot, and a copied map
    /// link carries the same pair after an <c>@</c> - so both are accepted, and so is the
    /// <c>?q=</c> form that a shared link uses. Anything else is refused rather than guessed at: a
    /// location that is wrong by a digit is worse than one that was never set, because the sun
    /// timers still fire, just at the wrong time of day.
    /// </para>
    /// </summary>
    public static bool TryParse(string? pasted, out HouseLocation location)
    {
        location = default;

        if (string.IsNullOrWhiteSpace(pasted))
        {
            return false;
        }

        // A map link puts the pair after an @ or a q=, and follows it with a zoom level that must
        // not be read as part of it. A bare paste is just the pair.
        Match match = Pair().Match(pasted);

        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            || !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
        {
            return false;
        }

        var found = new HouseLocation(lat, lon);

        if (!found.IsOnEarth)
        {
            return false;
        }

        location = found;
        return true;
    }

    [GeneratedRegex(@"(-?\d{1,3}(?:\.\d+)?)\s*,\s*(-?\d{1,3}(?:\.\d+)?)")]
    private static partial Regex Pair();
}
