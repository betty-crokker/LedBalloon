namespace LedBalloon.Core.Effects;

/// <summary>
/// How long one frame takes on a controller, which is what decides how fast anything that fades,
/// trails or decays actually moves.
/// <para>
/// Not the configured frame rate, and on these controllers there is not one to read:
/// <c>hw.led.fps</c> is 0 on both, which is WLED's "unlimited" setting. In that mode the firmware
/// services the strip as often as <c>MIN_FRAME_DELAY</c> and the wire between them allow, so the
/// pace is set by how long the LEDs take to clock out - 107 frames a second on the south roofline
/// and 96 on north. Reading the configured figure as the effect rate cost the Heartbeat port an
/// afternoon.
/// </para>
/// <para>
/// There are two frame times here and they are not the same number. This one is the interval
/// between frames, which is what a fade or a frame count is measured in. The other is WLED's
/// <c>FRAMETIME</c> macro, which a handful of effects use as a constant in their own arithmetic -
/// see <see cref="Constant"/>.
/// </para>
/// </summary>
/// <summary>
/// The two frame times a controller has, kept together because using one where the other belongs is
/// the mistake this pair exists to stop.
/// </summary>
/// <param name="IntervalMilliseconds">
/// How often a frame actually gets drawn, which is what a fade or a frame count is measured in.
/// </param>
/// <param name="FrameTimeMilliseconds">
/// WLED's <c>FRAMETIME</c>, a constant derived from the configured frame rate that a handful of
/// effects use as a number in their own arithmetic.
/// </param>
public readonly record struct ControllerTiming(int IntervalMilliseconds, int FrameTimeMilliseconds);

public static class FrameTime
{
    /// <summary>
    /// What one LED costs to clock out: 24 bits at 800 kHz, the WS281x line rate.
    /// <para>
    /// The whole strip is paid for, not the longest output. Measured against both controllers —
    /// 310 LEDs predicts 9.30 ms against 9.35 measured, and 356 predicts 10.68 against 10.36. If
    /// the outputs were driven in parallel the longer one alone would set the pace, and it does
    /// not: North's 308 LED output would predict 108 fps where the strip reports 96.
    /// </para>
    /// </summary>
    public const double MicrosecondsPerLed = 30.0;

    /// <summary>
    /// Below this, a reported rate is telling you about the effect rather than about the strip.
    /// <para>
    /// WLED does not re-clock a frame that has not changed, so a static effect reports single
    /// figures — North sat at 15 fps showing Solid and jumped to 96 the moment something moved.
    /// A preview needs the rate an animating effect would get, so a low reading is no use.
    /// </para>
    /// </summary>
    public const int SlowestAnimatedFps = 30;

    /// <summary>How long a frame takes on a strip of this length, when it cannot be measured.</summary>
    public static int EstimateMilliseconds(int totalLeds) =>
        Math.Max(1, (int)Math.Round(Math.Max(0, totalLeds) * MicrosecondsPerLed / 1000.0));

    /// <summary>
    /// True when a reported frame rate is worth believing as the strip's own pace rather than as
    /// a measure of how little the current effect is doing.
    /// </summary>
    public static bool IsAnimating(int? reportedFps) => reportedFps is >= SlowestAnimatedFps;

    /// <summary>The frame time a reported rate implies, rounded the way the firmware rounds.</summary>
    public static int FromFps(int fps) => Math.Max(1, 1000 / Math.Max(1, fps));

    /// <summary>
    /// The shortest gap WLED will leave between repaints, whatever else is going on.
    /// <para>
    /// Two milliseconds on an ESP32, which is what both controllers here are. WLED uses three on
    /// the single core S2 and C3 and eight on the 8266, so this is not a universal constant - but
    /// it is the right one for this hardware, and it is what <c>FRAMETIME</c> comes to when the
    /// frame rate is set to unlimited.
    /// </para>
    /// </summary>
    public const int MinimumFrameDelay = 2;

    /// <summary>
    /// WLED's <c>FRAMETIME</c> for a controller configured at <paramref name="configuredFps"/>.
    /// <para>
    /// This is the one effects use as a number rather than as a pace: Blink adds it to its on time
    /// so that a duty cycle too short to last a frame still shows something, Scanner multiplies it
    /// to decide how many LEDs to advance, Two Dots divides by it. It comes from the configured
    /// rate and not from how fast the strip is actually managing to draw, which is why it is worth
    /// keeping apart from the interval - here they are 2 ms and 9 ms, a factor of four and a half.
    /// </para>
    /// </summary>
    public static int Constant(int? configuredFps) =>
        configuredFps is null or 0 ? MinimumFrameDelay : Math.Max(1, 1000 / configuredFps.Value);
}
