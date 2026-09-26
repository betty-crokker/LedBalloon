namespace LedBalloon.Core.Effects;

/// <summary>
/// How long one frame takes on a controller, which is what decides how fast anything that fades,
/// trails or decays actually moves.
/// <para>
/// Not the configured frame rate. <c>hw.led.fps</c> reads 42 on both controllers here while the
/// strips run at 107 and 96 — WLED goes as fast as the wire allows and the configured figure is
/// not the effect rate. Reading it cost the Heartbeat port an afternoon: the port was right and
/// the frame time was out by a factor of four.
/// </para>
/// </summary>
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
}
