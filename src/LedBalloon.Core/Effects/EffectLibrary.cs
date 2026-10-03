using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The effects LedBalloon can draw, looked up the way a controller names them.
/// <para>
/// By name, never by number. WLED's effect ids are stable but not fixed: id 48 was Police through
/// 0.13 and is Rolling Balls now, and id 114 changed from Candy Cane to a 2D effect in 0.15. A
/// preset stores the number, so the number has to be resolved against the list the controller
/// itself reports before it means anything.
/// </para>
/// <para>
/// Anything not in here falls back to sliding the run's palette along it, which is wrong in
/// detail but right in spirit and costs nothing. That fallback is also the only thing that can
/// cover a fork's extra effects or one a usermod registered, since no amount of reading WLED's
/// source describes code that was never in it.
/// </para>
/// <para>
/// <b>Provenance.</b> These are WLED's own effects, compiled from WLED's own source - see
/// native/README.md. This project is GPL-3.0 because of that; see the NOTICE file at the root.
/// </para>
/// </summary>
public static class EffectLibrary
{
    /// <summary>
    /// WLED's own effect engine: see <see cref="NativeEngine"/> and native/README.md. There is
    /// nothing else now - this used to hold a hand-written port of each effect, and those are gone,
    /// because the firmware's own code is both more of them and more right.
    /// </summary>
    private static readonly IWledEffect[] FromEngine = NativeEngine.All().ToArray();

    private static readonly Dictionary<string, IWledEffect> ByName =
        FromEngine.ToDictionary(effect => effect.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the engine loaded. False means nothing here can draw anything.</summary>
    public static bool UsingEngine => FromEngine.Length > 0;

    /// <summary>
    /// Why there are no effects, when there are none. Null when the engine loaded.
    /// <para>
    /// Worth surfacing rather than swallowing: there is no longer a fallback behind this, so a build
    /// without the engine beside it draws nothing at all. <c>pwsh native/build.ps1</c> is the fix,
    /// and <c>publish.ps1</c> needs it to have been run.
    /// </para>
    /// </summary>
    public static string? EngineUnavailable => UsingEngine ? null : NativeEngine.Unavailable;

    /// <summary>Every effect that can be drawn.</summary>
    public static IReadOnlyList<IWledEffect> All => FromEngine;

    /// <summary>The effect a controller calls <paramref name="name"/>, if it is one we can draw.</summary>
    public static IWledEffect? Find(string? name) =>
        name is not null && ByName.TryGetValue(name.Trim(), out IWledEffect? effect) ? effect : null;

    /// <summary>
    /// The effect at <paramref name="effectId"/> on a controller whose effect list is
    /// <paramref name="names"/>.
    /// </summary>
    public static IWledEffect? Find(int? effectId, IReadOnlyList<string>? names) =>
        effectId is { } id && names is not null && id >= 0 && id < names.Count
            ? Find(names[id])
            : null;

    /// <summary>
    /// Sets up a run to be drawn, or returns null when this is not an effect the engine has.
    /// </summary>
    /// <param name="wled">The segment as a preset or live state describes it.</param>
    /// <param name="length">How many LEDs the run has here, which is not always what the preset says.</param>
    /// <param name="effectNames">The controller's own effect list, from <c>/json/eff</c>.</param>
    /// <param name="palette">The gradient behind the segment's palette id, if it uses one.</param>
    /// <param name="frameMilliseconds">How often the controller actually draws a frame.</param>
    /// <param name="frameTimeMilliseconds">
    /// WLED's <c>FRAMETIME</c> on that controller, which is a different number and is not how often
    /// it draws - see <see cref="EffectSegment.FrameTime"/>.
    /// </param>
    public static EffectSimulation? Simulate(
        WledSegment wled,
        int length,
        IReadOnlyList<string>? effectNames,
        WledPalette? palette,
        int frameMilliseconds = EffectSimulation.DefaultFrameMilliseconds,
        int frameTimeMilliseconds = FrameTime.MinimumFrameDelay)
    {
        ArgumentNullException.ThrowIfNull(wled);

        if (length < 1 || Find(wled.Effect, effectNames) is not { } effect)
        {
            return null;
        }

        var segment = new EffectSegment(length)
        {
            FrameMilliseconds = frameMilliseconds,
            FrameTime = frameTimeMilliseconds,
        };
        segment.Adopt(wled, palette);

        var simulation = new EffectSimulation(effect, segment, frameMilliseconds);

        // Trails need a few frames of history before they are trails, so do not open on one
        // frame's worth of dots scattered over an otherwise dark run.
        simulation.Prime();

        return simulation;
    }
}
