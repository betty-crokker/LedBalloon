using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The effects LedBalloon can draw for itself, looked up the way a controller names them.
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
/// </summary>
public static class EffectLibrary
{
    private static readonly IWledEffect[] Ported =
    [
        new SolidEffect(),
        new BlinkEffect(),
        new BreatheEffect(),
        new BpmEffect(),
        new FlowEffect(),
        new ChunchunEffect(),
        new ColorwavesEffect(),
        new StaticPatternEffect(),
        new TriStaticPatternEffect(),
        new LakeEffect(),
        new HeartbeatEffect(),
        new ColorloopEffect(),
        new RainbowEffect(),
        new StrobeMegaEffect(),
        new TriFadeEffect(),
        new Pride2015Effect(),
        new TriWipeEffect(),
        new SparkleEffect(),
        new FlashSparkleEffect(),
        new HyperSparkleEffect(),
        new TwinkleEffect(),
        new FireFlickerEffect(),
        new WipeEffect(),
        new SweepEffect(),
        new WipeRandomEffect(),
        new SweepRandomEffect(),
        new ScanEffect(),
        new DualScanEffect(),
        new RunningLightsEffect(),
        new RunningDualEffect(),
        new SawEffect(),
        new ChaseColorEffect(),
        new ChaseRandomEffect(),
        new ChaseRainbowEffect(),
        new RainbowRunnerEffect(),
        new DissolveEffect(),
        new DissolveRandomEffect(),
        new TricolorChaseEffect(),
        new GradientEffect(),
        new LoadingEffect(),
        new ScannerEffect(),
        new DualScannerEffect(),
        new LighthouseEffect(),
        new SpotsEffect(),
        new SpotsFadeEffect(),
        new TwoDotsEffect(),
        new TheaterChaseEffect(),
        new TheaterRainbowEffect(),
        new RunningColorEffect(),
        new RunningRandomEffect(),
        new AndroidEffect(),
        new IcuEffect(),
        new ChaseFlashEffect(),
        new ChaseFlashRandomEffect(),
        new SinelonEffect(),
        new SinelonDualEffect(),
        new SinelonRainbowEffect(),
        new JuggleEffect(),
        new CandleEffect(),
        new CandleMultiEffect(),
        new PhasedEffect(),
        new GlitterEffect(),
        new SolidGlitterEffect(),
        new OscillateEffect(),
        new FireworksEffect(),
        new RainEffect(),
        new TrafficLightEffect(),
        new PercentEffect(),
        new PhasedNoiseEffect(),
        new FillNoiseEffect(),
        new Fire2012Effect(),
        new Noise16_1Effect(),
        new Noise16_2Effect(),
        new Noise16_3Effect(),
        new Noise16_4Effect(),
        new BlinkRainbowEffect(),
        new StrobeEffect(),
        new StrobeRainbowEffect(),
        new FadeEffect(),
        new RandomColorEffect(),
        new DynamicEffect(),
        new DynamicSmoothEffect(),
        new ColorfulEffect(),
        new RippleRainbowEffect(),
        new MultiCometEffect(),
        new MeteorEffect(),
        new MeteorSmoothEffect(),
        new PerlinMoveEffect(),
        new WavesinsEffect(),
        new SineEffect(),
        new FlowStripeEffect(),
        new BouncingBallsEffect(),
        new RailwayEffect(),
        new WashingMachineEffect(),
        new BlendsEffect(),
        new TwinkleFoxEffect(),
        new TwinkleUpEffect(),
        new RandomChaseEffect(),
        new ColorTwinklesEffect(),
        new TwinkleCatEffect(),
        new FairyEffect(),
        new FairyTwinkleEffect(),
        new PopcornEffect(),
        new DripEffect(),
        new LightningEffect(),
        new HalloweenEyesEffect(),
        new RippleEffect(),
    ];

    private static readonly Dictionary<string, IWledEffect> ByName =
        Ported.ToDictionary(effect => effect.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every effect that can be drawn exactly rather than approximated.</summary>
    public static IReadOnlyList<IWledEffect> All => Ported;

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
    /// Sets up a run to be drawn by a ported effect, or returns null when there is not one.
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
