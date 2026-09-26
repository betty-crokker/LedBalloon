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
    /// What the controller measured at: 107 frames a second on this run.
    /// <para>
    /// There is no configured rate to compare it against - <c>hw.led.fps</c> is 0 on both
    /// controllers, WLED's "unlimited" setting, which is exactly why the strip runs at whatever the
    /// wire allows rather than at some ceiling.
    /// </para>
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
        uint end = settleMilliseconds + milliseconds;
        uint t = 0;

        segment.Draw(effect, t);

        while (nextSample < end)
        {
            // The gap to the next frame is what the effect asked for, or the time the strip takes
            // to clock out, whichever is longer.
            uint until = t + (uint)Math.Max(FrameMs, segment.FrameDelay);

            // The preview sends whatever is in the buffer on its own schedule, so a frame held for a
            // long time is sent over and over - which is how a pause looks from outside.
            //
            // Sampled before the next frame is drawn, not after. Drawing first and then filling in
            // the samples that the frame covered gets the pause backwards: it fills a three second
            // pause with the picture that ended it. That put ICU's still frames at half a percent
            // where the strip had thirty-eight.
            while (nextSample < until && nextSample < end)
            {
                for (int i = 0; i < frame.Length; i++)
                {
                    frame[i] = segment.Pixels[segment.Length - 1 - i];
                }

                read(frame, nextSample);
                nextSample += PreviewSampleMs;
            }

            t = until;
            segment.Draw(effect, t);
        }
    }

    /// <summary>
    /// The shape of one frame read along one channel, which is how a wave, a blob or a row of
    /// pools gets measured without caring what color it happens to be.
    /// </summary>
    /// <param name="Bands">Peaks along the run: how many of whatever it is fit on it.</param>
    /// <param name="Rising">What share of neighboring pairs get brighter, which is its symmetry.</param>
    /// <param name="SteepestRise">
    /// The largest single step up, which is what tells a soft edge from a hard one - and note the
    /// run arrives reversed, so a cliff on the falling side of the effect shows up here.
    /// </param>
    public readonly record struct Shape(
        double Mean, double Deviation, int Bands, double Rising, int SteepestRise, int SteepestFall);

    public static Shape Profile(RgbColor[] frame, Func<RgbColor, byte> channel)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(channel);

        byte[] v = [.. frame.Select(channel)];

        double mean = v.Average(x => (double)x);
        double deviation = Math.Sqrt(v.Average(x => (x - mean) * (x - mean)));

        int peaks = 0, ups = 0, steepestRise = 0, steepestFall = 0;
        bool climbing = false;

        for (int i = 1; i < v.Length; i++)
        {
            int step = v[i] - v[i - 1];

            steepestRise = Math.Max(steepestRise, step);
            steepestFall = Math.Max(steepestFall, -step);

            if (step > 0)
            {
                climbing = true;
                ups++;
            }
            else if (climbing && step < 0)
            {
                peaks++;
                climbing = false;
            }
        }

        return new Shape(
            mean, deviation, peaks, (double)ups / (v.Length - 1), steepestRise, steepestFall);
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
