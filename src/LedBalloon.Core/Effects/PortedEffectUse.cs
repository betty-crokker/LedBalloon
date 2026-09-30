namespace LedBalloon.Core.Effects;

/// <summary>
/// What one effect's code actually reads: which color slots, and whether the palette.
/// </summary>
/// <param name="Direct">
/// Slots the effect reads outright, whatever the palette is set to.
/// </param>
/// <param name="WhenPaletteIsDefault">
/// Slots that only matter while the palette is Default. WLED's <c>color_from_palette</c> returns
/// the segment's color slot rather than a gradient when the palette is 0, so an effect that draws
/// everything through the palette still reads a color slot in that one case — and reads none of
/// them the rest of the time.
/// </param>
/// <param name="UsesPalette">Whether the effect ever asks the palette for anything.</param>
public sealed record EffectUse(
    IReadOnlyList<int> Direct,
    IReadOnlyList<int> WhenPaletteIsDefault,
    bool UsesPalette)
{
    /// <summary>The slots worth offering for a segment whose palette is <paramref name="paletteId"/>.</summary>
    public IReadOnlyList<int> SlotsFor(int? paletteId) => paletteId is null or 0
        ? [.. Direct.Concat(WhenPaletteIsDefault).Distinct().Order()]
        : Direct;
}

/// <summary>
/// What each ported effect reads, taken from the ports rather than from the firmware's own
/// <c>/json/fxdata</c>.
/// </summary>
/// <remarks>
/// fxdata is hand-maintained metadata and over-declares: it says Twinklecat reads two color slots
/// when the code reads one - the background - and takes the twinkles from the palette. A box that
/// does nothing is worse than no box, so where there is a port to read, the port decides.
/// <para>
/// Everything not in here falls back to fxdata, which is all there is for the effects nobody has
/// ported and for a fork's extra ones.
/// </para>
/// <para>
/// Built by reading every port: colour slots by their use of <c>Colors[n]</c>, palette use by calls
/// to <c>ColorFromPalette</c>, and the slot each of those calls names. Four effects compute the
/// slot rather than naming it - Scan, Scan Dual, Aurora and Meteor - and those were read by hand.
/// </para>
/// </remarks>
public static class PortedEffectUse
{
    private static readonly Dictionary<string, EffectUse> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Android"] = new([0], [1], true),
        ["Aurora"] = new([], [0, 1, 2], true),
        ["Blends"] = new([], [], true),
        ["Blink"] = new([0, 1], [0], true),
        ["Blink Rainbow"] = new([1], [0], true),
        ["Bouncing Balls"] = new([0, 1, 2], [], false),
        ["Bpm"] = new([], [0], true),
        ["Breathe"] = new([1], [0], true),
        ["Candle"] = new([1], [0], true),
        ["Candle Multi"] = new([1], [0], true),
        ["Chase"] = new([0, 1, 2], [1], true),
        ["Chase 2"] = new([0, 1], [0], true),
        ["Chase 3"] = new([0, 2], [1], true),
        ["Chase Flash"] = new([0, 1], [0], true),
        ["Chase Flash Rnd"] = new([0, 1], [], false),
        ["Chase Rainbow"] = new([0, 1, 2], [1], true),
        ["Chase Random"] = new([0, 1, 2], [1], true),
        ["Chunchun"] = new([0], [0], true),
        ["Colorful"] = new([], [], true),
        ["Colorloop"] = new([], [], false),
        ["Colortwinkles"] = new([], [0], true),
        ["Colorwaves"] = new([], [0], true),
        ["Dancing Shadows"] = new([0], [], true),
        ["Dissolve"] = new([0, 1], [0], true),
        ["Dissolve Rnd"] = new([0, 1], [0], true),
        ["Drip"] = new([0, 1], [], false),
        ["Dynamic"] = new([], [], false),
        ["Dynamic Smooth"] = new([], [], false),
        ["Fade"] = new([1], [0], true),
        ["Fairy"] = new([1], [0], true),
        ["Fairytwinkle"] = new([1], [0], true),
        ["Fill Noise"] = new([], [0], true),
        ["Fire 2012"] = new([0], [0], true),
        ["Fire Flicker"] = new([0], [0], true),
        ["Fireworks"] = new([0], [0], true),
        ["Fireworks 1D"] = new([0], [], false),
        ["Fireworks Starburst"] = new([0, 1], [], false),
        ["Flow"] = new([], [0], true),
        ["Flow Stripe"] = new([0], [], false),
        ["Glitter"] = new([2], [], true),
        ["Gradient"] = new([0], [1], true),
        ["Halloween Eyes"] = new([0, 1], [0], true),
        ["Heartbeat"] = new([1], [0], true),
        ["ICU"] = new([1], [0], true),
        ["Juggle"] = new([0], [0], true),
        ["Lake"] = new([], [0], true),
        ["Lighthouse"] = new([0], [0], true),
        ["Lightning"] = new([0, 1], [0], true),
        ["Loading"] = new([0], [1], true),
        ["Meteor"] = new([0], [0], true),
        ["Meteor Smooth"] = new([0], [0], true),
        ["Multi Comet"] = new([2], [0], true),
        ["Noise 1"] = new([], [0], true),
        ["Noise 2"] = new([], [0], true),
        ["Noise 3"] = new([], [0], true),
        ["Noise 4"] = new([], [0], true),
        ["Noise Pal"] = new([], [0], true),
        ["Oscillate"] = new([], [], false),
        ["Pacifica"] = new([], [0], true),
        ["Percent"] = new([1], [0], true),
        ["Perlin Move"] = new([0], [0], true),
        ["Phased"] = new([1], [0], true),
        ["Phased Noise"] = new([1], [0], true),
        ["Plasma"] = new([], [0], true),
        ["Popcorn"] = new([0, 1, 2], [], false),
        ["Pride 2015"] = new([], [], false),
        ["Railway"] = new([0], [], true),
        ["Rain"] = new([0], [], false),
        ["Rainbow"] = new([], [], false),
        ["Rainbow Runner"] = new([0, 2], [1], true),
        ["Random Colors"] = new([], [], false),
        ["Ripple"] = new([0, 1], [0], true),
        ["Ripple Rainbow"] = new([0], [0], true),
        ["Rolling Balls"] = new([0, 1, 2], [0], true),
        ["Running"] = new([1], [0, 2], true),
        ["Running Dual"] = new([1], [0, 2], true),
        ["Saw"] = new([1], [0, 2], true),
        ["Scan"] = new([1, 2], [0, 2], true),
        ["Scan Dual"] = new([1, 2], [0, 2], true),
        ["Scanner"] = new([0, 2], [0], true),
        ["Scanner Dual"] = new([0, 2], [0], true),
        ["Sine"] = new([1], [0], true),
        ["Sinelon"] = new([0, 2], [0], true),
        ["Sinelon Dual"] = new([0, 2], [0], true),
        ["Sinelon Rainbow"] = new([0, 2], [0], true),
        ["Solid"] = new([0], [], false),
        ["Solid Glitter"] = new([0, 2], [], false),
        ["Solid Pattern"] = new([1], [0], true),
        ["Solid Pattern Tri"] = new([], [], false),
        ["Sparkle"] = new([0], [1], true),
        ["Sparkle Dark"] = new([1], [0], true),
        ["Sparkle+"] = new([1], [0], true),
        ["Spots"] = new([0, 1], [0], true),
        ["Spots Fade"] = new([0, 1], [0], true),
        ["Stream"] = new([], [], false),
        ["Stream 2"] = new([], [], false),
        ["Strobe"] = new([0, 1], [0], true),
        ["Strobe Mega"] = new([0], [1], true),
        ["Strobe Rainbow"] = new([1], [0], true),
        ["Sunrise"] = new([0], [], true),
        ["Sweep"] = new([0, 1], [0], true),
        ["Sweep Random"] = new([0, 1], [0], true),
        ["TV Simulator"] = new([], [], false),
        ["Tetrix"] = new([0, 1], [0], true),
        ["Theater"] = new([0, 1], [0], true),
        ["Theater Rainbow"] = new([0, 1], [0], true),
        ["Traffic Light"] = new([0], [1], true),
        ["Tri Fade"] = new([], [2], true),
        ["Tri Wipe"] = new([0, 1], [2], true),
        ["Twinkle"] = new([], [0], true),
        ["Twinklecat"] = new([1], [0], true),
        ["Twinklefox"] = new([1], [0], true),
        ["Twinkleup"] = new([1], [0], true),
        ["Two Dots"] = new([0, 1, 2], [], false),
        ["Washing Machine"] = new([], [], true),
        ["Wavesins"] = new([], [0], true),
        ["Wipe"] = new([0, 1], [0], true),
        ["Wipe Random"] = new([0, 1], [0], true),
    };

    /// <summary>What this effect reads, or null when no port of it has been read.</summary>
    public static EffectUse? For(string? effectName) =>
        effectName is { Length: > 0 } name && Known.TryGetValue(name, out EffectUse? use) ? use : null;

    /// <summary>How many effects have been audited, for the test that guards the count.</summary>
    public static int Count => Known.Count;
}
