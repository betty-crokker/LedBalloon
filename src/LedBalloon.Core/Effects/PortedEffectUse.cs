namespace LedBalloon.Core.Effects;

/// <summary>
/// What one effect's code actually reads: which color slots, whether the palette, and which of the
/// two sliders.
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
/// <param name="UsesSpeed">Whether the effect ever reads its speed slider.</param>
/// <param name="UsesIntensity">Whether the effect ever reads its intensity slider.</param>
public sealed record EffectUse(
    IReadOnlyList<int> Direct,
    IReadOnlyList<int> WhenPaletteIsDefault,
    bool UsesPalette,
    bool UsesSpeed,
    bool UsesIntensity)
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
/// fxdata is hand-maintained metadata and is wrong in both directions. It over-declares: it says
/// Twinklecat reads two color slots when the code reads one - the background - and takes the
/// twinkles from the palette. It also under-declares: Solid's entry is empty, so WLED's own UI
/// offers it a speed and an intensity it never looks at, and Wipe Random is given no intensity
/// slider although the wipe it shares with Sweep divides its remainder by exactly that. A box that
/// does nothing is worse than no box, and a missing box for something that works is worse still, so
/// where there is a port to read, the port decides.
/// <para>
/// Everything not in here falls back to fxdata, which is all there is for the effects nobody has
/// ported and for a fork's extra ones.
/// </para>
/// <para>
/// Built by reading every port: color slots by their use of <c>Colors[n]</c>, palette use by calls
/// to <c>ColorFromPalette</c>, and the sliders by reading <c>Speed</c> and <c>Intensity</c> through
/// the shared helper classes a dozen effects delegate to. Checked against one controller's own
/// fxdata for all 118: 228 of the 232 declared answers agree, the 4 that do not are the ones fxdata
/// under-declares, and 4 more are effects fxdata says nothing about at all. Four effects compute
/// their color slot rather than naming it - Scan, Scan Dual, Aurora and Meteor - and those were read
/// by hand.
/// </para>
/// </remarks>
public static class PortedEffectUse
{
    private static readonly Dictionary<string, EffectUse> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Android"] = new([0], [1], true, true, true),
        ["Aurora"] = new([], [0, 1, 2], true, true, true),
        ["Blends"] = new([], [], true, true, true),
        ["Blink"] = new([0, 1], [0], true, true, true),
        ["Blink Rainbow"] = new([1], [0], true, true, true),
        ["Bouncing Balls"] = new([0, 1, 2], [], false, true, true),
        ["Bpm"] = new([], [0], true, true, false),
        ["Breathe"] = new([1], [0], true, true, false),
        ["Candle"] = new([1], [0], true, true, true),
        ["Candle Multi"] = new([1], [0], true, true, true),
        ["Chase"] = new([0, 1, 2], [1], true, true, true),
        ["Chase 2"] = new([0, 1], [0], true, true, true),
        ["Chase 3"] = new([0, 2], [1], true, true, true),
        ["Chase Flash"] = new([0, 1], [0], true, true, false),
        ["Chase Flash Rnd"] = new([0, 1], [], false, true, false),
        ["Chase Rainbow"] = new([0, 1, 2], [1], true, true, true),
        ["Chase Random"] = new([0, 1, 2], [1], true, true, true),
        ["Chunchun"] = new([0], [0], true, true, true),
        ["Colorful"] = new([], [], true, true, true),
        ["Colorloop"] = new([], [], false, true, true),
        ["Colortwinkles"] = new([], [0], true, true, true),
        ["Colorwaves"] = new([], [0], true, true, true),
        ["Dancing Shadows"] = new([0], [], true, true, true),
        ["Dissolve"] = new([0, 1], [0], true, true, true),
        ["Dissolve Rnd"] = new([0, 1], [0], true, true, true),
        ["Drip"] = new([0, 1], [], false, true, true),
        ["Dynamic"] = new([], [], false, true, true),
        ["Dynamic Smooth"] = new([], [], false, true, true),
        ["Fade"] = new([1], [0], true, true, false),
        ["Fairy"] = new([1], [0], true, true, true),
        ["Fairytwinkle"] = new([1], [0], true, true, true),
        ["Fill Noise"] = new([], [0], true, true, false),
        ["Fire 2012"] = new([0], [0], true, true, true),
        ["Fire Flicker"] = new([0], [0], true, true, true),
        ["Fireworks"] = new([0], [0], true, false, true),
        ["Fireworks 1D"] = new([0], [], false, true, true),
        ["Fireworks Starburst"] = new([0, 1], [], false, true, true),
        ["Flow"] = new([], [0], true, true, true),
        ["Flow Stripe"] = new([0], [], false, true, true),
        ["Glitter"] = new([2], [], true, true, true),
        ["Gradient"] = new([0], [1], true, true, true),
        ["Halloween Eyes"] = new([0, 1], [0], true, true, true),
        ["Heartbeat"] = new([1], [0], true, true, true),
        ["ICU"] = new([1], [0], true, true, true),
        ["Juggle"] = new([0], [0], true, true, true),
        ["Lake"] = new([], [0], true, true, false),
        ["Lighthouse"] = new([0], [0], true, true, true),
        ["Lightning"] = new([0, 1], [0], true, true, true),
        ["Loading"] = new([0], [1], true, true, true),
        ["Meteor"] = new([0], [0], true, true, true),
        ["Meteor Smooth"] = new([0], [0], true, true, true),
        ["Multi Comet"] = new([2], [0], true, true, true),
        ["Noise 1"] = new([], [0], true, true, false),
        ["Noise 2"] = new([], [0], true, true, false),
        ["Noise 3"] = new([], [0], true, true, false),
        ["Noise 4"] = new([], [0], true, true, false),
        ["Noise Pal"] = new([], [0], true, true, true),
        ["Oscillate"] = new([], [], false, true, true),
        ["Pacifica"] = new([], [0], true, true, true),
        ["Percent"] = new([1], [0], true, true, true),
        ["Perlin Move"] = new([0], [0], true, true, true),
        ["Phased"] = new([1], [0], true, true, true),
        ["Phased Noise"] = new([1], [0], true, true, true),
        ["Plasma"] = new([], [0], true, true, true),
        ["Popcorn"] = new([0, 1, 2], [], false, true, true),
        ["Pride 2015"] = new([], [], false, true, false),
        ["Railway"] = new([0], [], true, true, true),
        ["Rain"] = new([0], [], false, true, true),
        ["Rainbow"] = new([], [], false, true, true),
        ["Rainbow Runner"] = new([0, 2], [1], true, true, true),
        ["Random Colors"] = new([], [], false, true, true),
        ["Ripple"] = new([0, 1], [0], true, true, true),
        ["Ripple Rainbow"] = new([0], [0], true, true, true),
        ["Rolling Balls"] = new([0, 1, 2], [0], true, true, true),
        ["Running"] = new([1], [0, 2], true, true, true),
        ["Running Dual"] = new([1], [0, 2], true, true, true),
        ["Saw"] = new([1], [0, 2], true, true, true),
        ["Scan"] = new([1, 2], [0, 2], true, true, true),
        ["Scan Dual"] = new([1, 2], [0, 2], true, true, true),
        ["Scanner"] = new([0, 2], [0], true, true, true),
        ["Scanner Dual"] = new([0, 2], [0], true, true, true),
        ["Sine"] = new([1], [0], true, true, true),
        ["Sinelon"] = new([0, 2], [0], true, true, true),
        ["Sinelon Dual"] = new([0, 2], [0], true, true, true),
        ["Sinelon Rainbow"] = new([0, 2], [0], true, true, true),
        ["Solid"] = new([0], [], false, false, false),
        ["Solid Glitter"] = new([0, 2], [], false, false, true),
        ["Solid Pattern"] = new([1], [0], true, true, true),
        ["Solid Pattern Tri"] = new([], [], false, false, true),
        ["Sparkle"] = new([0], [1], true, true, false),
        ["Sparkle Dark"] = new([1], [0], true, true, true),
        ["Sparkle+"] = new([1], [0], true, true, true),
        ["Spots"] = new([0, 1], [0], true, true, true),
        ["Spots Fade"] = new([0, 1], [0], true, true, true),
        ["Stream"] = new([], [], false, true, true),
        ["Stream 2"] = new([], [], false, true, false),
        ["Strobe"] = new([0, 1], [0], true, true, true),
        ["Strobe Mega"] = new([0], [1], true, true, true),
        ["Strobe Rainbow"] = new([1], [0], true, true, true),
        ["Sunrise"] = new([0], [], true, true, true),
        ["Sweep"] = new([0, 1], [0], true, true, true),
        ["Sweep Random"] = new([0, 1], [0], true, true, true),
        ["TV Simulator"] = new([], [], false, true, true),
        ["Tetrix"] = new([0, 1], [0], true, true, true),
        ["Theater"] = new([0, 1], [0], true, true, true),
        ["Theater Rainbow"] = new([0, 1], [0], true, true, true),
        ["Traffic Light"] = new([0], [1], true, true, true),
        ["Tri Fade"] = new([], [2], true, true, false),
        ["Tri Wipe"] = new([0, 1], [2], true, true, false),
        ["Twinkle"] = new([], [0], true, true, true),
        ["Twinklecat"] = new([1], [0], true, true, true),
        ["Twinklefox"] = new([1], [0], true, true, true),
        ["Twinkleup"] = new([1], [0], true, true, true),
        ["Two Dots"] = new([0, 1, 2], [], false, true, true),
        ["Washing Machine"] = new([], [], true, true, true),
        ["Wavesins"] = new([], [0], true, true, true),
        ["Wipe"] = new([0, 1], [0], true, true, true),
        ["Wipe Random"] = new([0, 1], [0], true, true, true),
    };

    /// <summary>What this effect reads, or null when no port of it has been read.</summary>
    public static EffectUse? For(string? effectName) =>
        effectName is { Length: > 0 } name && Known.TryGetValue(name, out EffectUse? use) ? use : null;

    /// <summary>How many effects have been audited, for the test that guards the count.</summary>
    public static int Count => Known.Count;
}
