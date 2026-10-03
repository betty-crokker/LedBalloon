namespace LedBalloon.Core.Effects;

/// <summary>
/// What one effect's code actually reads: which color slots, whether the palette, and which of the
/// two sliders.
/// </summary>
/// <param name="Direct">
/// Slots the effect reads whatever the palette is set to.
/// </param>
/// <param name="WhenPaletteIsDefault">
/// Slots that only matter while the palette is Default. WLED's <c>color_from_palette</c> returns
/// the segment's color slot rather than a gradient when the palette is 0, so an effect that draws
/// everything through the palette still reads a color slot in that one case \u2014 and reads none of
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
/// What each ported effect reads, measured by running it rather than by reading it.
/// </summary>
/// <remarks>
/// The firmware's own <c>/json/fxdata</c> is hand-maintained and wrong in both directions, so this
/// exists to overrule it: it says Twinklecat reads two color slots when the code reads one, and it
/// gives Wipe Random no intensity slider although the wipe it shares divides its remainder by
/// exactly that.
/// <para>
/// The first two versions of this were built by reading the ports, and that was wrong for 71 of the
/// 118. Static reach cannot tell which branch a caller takes - Running was credited with a line only
/// Running Dual reaches - and it cannot follow a color through a helper that uses it on one path and
/// ignores it on the other, which is how most of Blink's entry came to claim a slot the drawing
/// never touches. It also missed <c>ColorWheel</c>, which quietly becomes the palette the moment one
/// is selected, so a dozen effects were recorded as using no palette at all.
/// </para>
/// <para>
/// So every field here is measured: change one thing, run the effect, and see whether the picture
/// moves. Over 1200 frames - forty seconds, because Halloween Eyes keeps its eyes shut for up to
/// eight and came back claiming to ignore both its sliders when the run was shorter - hashing every
/// pixel of every frame rather than sampling, because Strobe is lit for one frame in a long cycle
/// and sampling caught both runs dark, and unioned over all eight combinations of the option flags,
/// because several effects branch on those. Every one of the 118 was checked to draw the same thing
/// twice from the same inputs first, since none of this would mean anything otherwise.
/// </para>
/// <para>
/// The one thing it can still miss is a slot read only on a branch that forty seconds never reach.
/// That errs towards offering one control too few, where the firmware's metadata errs towards one
/// too many; of the two, a missing box is the one somebody notices and reports.
/// </para>
/// <para>
/// Everything not in here falls back to fxdata, which is all there is for the effects nobody has
/// ported and for a fork's extra ones.
/// </para>
/// </remarks>
public static class EffectUses
{
    private static readonly Dictionary<string, EffectUse> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Android"] = new([0], [1], true, true, true),
        ["Aurora"] = new([], [0, 1, 2], true, true, true),
        ["Blends"] = new([], [], true, true, true),
        ["Blink"] = new([1], [0], true, true, true),
        ["Blink Rainbow"] = new([1], [], true, true, true),
        ["Bouncing Balls"] = new([], [0, 1, 2], true, true, true),
        ["Bpm"] = new([], [0], true, true, false),
        ["Breathe"] = new([1], [0], true, true, false),
        ["Candle"] = new([1], [0], true, true, true),
        ["Candle Multi"] = new([1], [0], true, true, true),
        ["Chase"] = new([0, 2], [1], true, true, true),
        ["Chase 2"] = new([1], [0], true, true, true),
        ["Chase 3"] = new([0, 2], [1], true, true, true),
        ["Chase Flash"] = new([1], [0], true, true, false),
        ["Chase Flash Rnd"] = new([0, 1], [], true, true, false),
        ["Chase Rainbow"] = new([0, 1], [], true, true, true),
        ["Chase Random"] = new([0, 2], [], true, true, true),
        ["Chunchun"] = new([1], [0], true, true, true),
        ["Colorful"] = new([], [], true, true, false),
        ["Colorloop"] = new([], [], true, true, true),
        ["Colortwinkles"] = new([], [0], true, true, true),
        ["Colorwaves"] = new([], [0], true, true, true),
        ["Dancing Shadows"] = new([], [], true, true, true),
        ["Dissolve"] = new([1], [0], true, true, true),
        ["Dissolve Rnd"] = new([1], [], true, true, true),
        ["Drip"] = new([0, 1], [], false, true, true),
        ["Dynamic"] = new([], [], true, true, true),
        ["Dynamic Smooth"] = new([], [], true, true, true),
        ["Fade"] = new([1], [0], true, true, false),
        ["Fairy"] = new([1], [0], true, true, true),
        ["Fairytwinkle"] = new([1], [0], true, true, true),
        ["Fill Noise"] = new([], [0], true, true, false),
        ["Fire 2012"] = new([], [0], true, true, true),
        ["Fire Flicker"] = new([], [0], true, true, true),
        ["Fireworks"] = new([1], [0], true, false, true),
        ["Fireworks 1D"] = new([1], [0], true, true, true),
        ["Fireworks Starburst"] = new([1], [], true, true, true),
        ["Flow"] = new([], [0], true, true, true),
        ["Flow Stripe"] = new([], [], false, true, true),
        ["Glitter"] = new([2], [], true, true, true),
        ["Gradient"] = new([0], [1], true, true, true),
        ["Halloween Eyes"] = new([1], [0], true, true, true),
        ["Heartbeat"] = new([1], [0], true, true, true),
        ["ICU"] = new([1], [0], true, true, true),
        ["Juggle"] = new([], [], true, true, true),
        ["Lake"] = new([], [0], true, true, false),
        ["Lighthouse"] = new([1], [0], true, true, true),
        ["Lightning"] = new([1], [0], true, true, true),
        ["Loading"] = new([0], [1], true, true, true),
        ["Meteor"] = new([], [0], true, true, true),
        ["Meteor Smooth"] = new([], [0], true, true, true),
        ["Multi Comet"] = new([1, 2], [0], true, true, true),
        ["Noise 1"] = new([], [0], true, true, false),
        ["Noise 2"] = new([], [0], true, true, false),
        ["Noise 3"] = new([], [0], true, true, false),
        ["Noise 4"] = new([], [0], true, true, false),
        ["Noise Pal"] = new([], [], true, false, true),
        ["Oscillate"] = new([0, 1, 2], [], false, true, true),
        ["Pacifica"] = new([], [], true, true, true),
        ["Percent"] = new([1], [0], true, false, true),
        ["Perlin Move"] = new([1], [0], true, true, true),
        ["Phased"] = new([1], [0], true, true, true),
        ["Phased Noise"] = new([1], [0], true, true, true),
        ["Plasma"] = new([], [0], true, true, true),
        ["Popcorn"] = new([], [0, 1, 2], true, true, true),
        ["Pride 2015"] = new([], [], false, true, false),
        ["Railway"] = new([], [], true, true, true),
        ["Rain"] = new([1], [0], true, true, true),
        ["Rainbow"] = new([], [], true, true, true),
        ["Rainbow Runner"] = new([0], [], true, true, true),
        ["Random Colors"] = new([], [], true, true, true),
        ["Ripple"] = new([1], [0], true, true, true),
        ["Ripple Rainbow"] = new([], [0], true, true, true),
        ["Rolling Balls"] = new([1], [0, 2], true, true, true),
        ["Running"] = new([1], [0], true, true, true),
        ["Running Dual"] = new([1], [0, 2], true, true, true),
        ["Saw"] = new([1], [0], true, true, true),
        ["Scan"] = new([1], [0], true, true, true),
        ["Scan Dual"] = new([1], [0, 2], true, true, true),
        ["Scanner"] = new([1, 2], [0], true, true, true),
        ["Scanner Dual"] = new([1, 2], [0], true, true, true),
        ["Sine"] = new([1], [0], true, true, true),
        ["Sinelon"] = new([1], [0], true, true, true),
        ["Sinelon Dual"] = new([1, 2], [0], true, true, true),
        ["Sinelon Rainbow"] = new([1], [], true, true, true),
        ["Solid"] = new([0], [], false, false, false),
        ["Solid Glitter"] = new([0, 2], [], false, false, true),
        ["Solid Pattern"] = new([], [0], true, true, false),
        ["Solid Pattern Tri"] = new([0, 1, 2], [], false, false, true),
        ["Sparkle"] = new([0], [1], true, true, false),
        ["Sparkle Dark"] = new([1], [0], true, true, true),
        ["Sparkle+"] = new([1], [0], true, true, true),
        ["Spots"] = new([1], [0], true, true, true),
        ["Spots Fade"] = new([1], [0], true, true, true),
        ["Stream"] = new([], [], true, true, true),
        ["Stream 2"] = new([], [], false, true, false),
        ["Strobe"] = new([1], [0], true, true, false),
        ["Strobe Mega"] = new([0], [1], true, true, true),
        ["Strobe Rainbow"] = new([1], [], true, true, false),
        ["Sunrise"] = new([], [], true, true, true),
        ["Sweep"] = new([1], [0], true, true, true),
        ["Sweep Random"] = new([], [], true, true, true),
        ["Tetrix"] = new([1], [0], true, true, true),
        ["Theater"] = new([1], [0], true, true, true),
        ["Theater Rainbow"] = new([1], [], true, true, true),
        ["Traffic Light"] = new([], [1], true, true, false),
        ["Tri Fade"] = new([0, 1], [2], true, true, false),
        ["Tri Wipe"] = new([0, 1], [2], true, true, false),
        ["TV Simulator"] = new([], [], false, true, true),
        ["Twinkle"] = new([1], [0], true, true, true),
        ["Twinklecat"] = new([1], [0], true, true, true),
        ["Twinklefox"] = new([1], [0], true, true, true),
        ["Twinkleup"] = new([1], [0], true, true, true),
        ["Two Dots"] = new([0, 1, 2], [], false, true, true),
        ["Washing Machine"] = new([], [], true, true, true),
        ["Wavesins"] = new([], [0], true, true, true),
        ["Wipe"] = new([1], [0], true, true, true),
        ["Wipe Random"] = new([], [], true, true, true),
    };

    /// <summary>What this effect reads, or null when it has not been read.</summary>
    public static EffectUse? For(string? effectName) =>
        effectName is { Length: > 0 } name && Known.TryGetValue(name, out EffectUse? use) ? use : null;

    /// <summary>The effects that have been read, for the test that checks they all still exist.</summary>
    public static IReadOnlyCollection<string> Names => Known.Keys;
}
