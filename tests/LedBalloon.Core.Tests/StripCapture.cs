using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Draws an effect the way the south controller draws it, and hands back frames sampled the way its
/// live preview sends them, so a port can be measured on the same footing as the strip.
/// </summary>
internal static class Strip
{
    /// <summary>The south roofline, segment two, LEDs 25 to 309 on the wire.</summary>
    public const int Roofline = 285;

    /// <summary>
    /// What the controller measured at: 107 frames a second on this run, not the 42 that
    /// <c>hw.led.fps</c> reports - that setting is a ceiling, and a strip this short never reaches it.
    /// </summary>
    public const int FrameMs = 9;

    /// <summary>How often the live preview sends a frame, which is far slower than it draws them.</summary>
    public const int PreviewSampleMs = 71;

    /// <summary>The run as it was set up for every one of these captures.</summary>
    public static EffectSegment Run(byte speed = 128, byte intensity = 128) => new(Roofline)
    {
        Speed = speed,
        Intensity = intensity,
        FrameMilliseconds = FrameMs,
        PaletteId = 0,
        Palette = null,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// Runs <paramref name="effect"/> for <paramref name="milliseconds"/>, calling
    /// <paramref name="read"/> with each sampled frame and the time it was drawn at.
    /// <para>
    /// The pixels arrive reversed, because the roofline is wired back to front and the capture read
    /// it off the wire. Most measurements do not care - a band is as wide either way round - but
    /// anything asymmetric does, and Saw is exactly that.
    /// </para>
    /// <para>
    /// Drawn through <see cref="EffectSegment.Draw"/> rather than
    /// <see cref="IWledEffect.Render"/>, so the frame counter advances as the controller's does.
    /// </para>
    /// </summary>
    public static void Sample(
        IWledEffect effect,
        EffectSegment segment,
        uint milliseconds,
        Action<RgbColor[], uint> read,
        uint settleMilliseconds = 1500)
    {
        var frame = new RgbColor[segment.Length];
        uint nextSample = settleMilliseconds;

        for (uint t = 0; t < settleMilliseconds + milliseconds; t += FrameMs)
        {
            segment.Draw(effect, t);

            if (t < nextSample)
            {
                continue;
            }

            nextSample = t + PreviewSampleMs;

            for (int i = 0; i < frame.Length; i++)
            {
                frame[i] = segment.Pixels[segment.Length - 1 - i];
            }

            read(frame, t);
        }
    }

    /// <summary>How far apart two colors are, summed over the three channels.</summary>
    public static int Apart(RgbColor first, RgbColor second) =>
        Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);

    /// <summary>
    /// How many distinct colors cover more than a few LEDs, which is how a flat pattern was counted
    /// on the strip. Quantized to the top four bits of each channel, as the capture was.
    /// </summary>
    public static int Fills(RgbColor[] frame)
    {
        Dictionary<int, int> tally = [];

        foreach (RgbColor pixel in frame)
        {
            int key = ((pixel.R >> 4) << 8) | ((pixel.G >> 4) << 4) | (pixel.B >> 4);
            tally[key] = tally.GetValueOrDefault(key) + 1;
        }

        return tally.Values.Count(c => c > 3);
    }

    /// <summary>Which of the three channels a pixel leans toward, ties going to red then green.</summary>
    public static int Dominant(RgbColor pixel) =>
        Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) < 12 ? -1
        : pixel.R >= pixel.G && pixel.R >= pixel.B ? 0
        : pixel.G >= pixel.B ? 1
        : 2;
}
