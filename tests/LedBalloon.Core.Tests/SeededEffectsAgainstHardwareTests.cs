using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The effects that use the controller's own random generator as a <em>pattern</em> rather than as
/// noise, captured on the south controller's 285 LED roofline.
/// <para>
/// These reset the seed to a fixed number and walk the sequence once per LED, so every LED gets the
/// same draws it got last frame without anything being stored. That makes the sequence part of the
/// effect, which is why <see cref="SeededSequence"/> ports FastLED's generator exactly rather than
/// standing in for it - and it makes Twinkleup checkable pixel for pixel, which effects built on real
/// randomness can never be.
/// </para>
/// </summary>
public class SeededEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static WledPalette Rainbow() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(255, 0, 0)), new PaletteStop(16, new RgbColor(213, 42, 0)),
            new PaletteStop(32, new RgbColor(171, 85, 0)), new PaletteStop(48, new RgbColor(171, 127, 0)),
            new PaletteStop(64, new RgbColor(171, 171, 0)), new PaletteStop(80, new RgbColor(86, 213, 0)),
            new PaletteStop(96, new RgbColor(0, 255, 0)), new PaletteStop(112, new RgbColor(0, 213, 42)),
            new PaletteStop(128, new RgbColor(0, 171, 85)), new PaletteStop(144, new RgbColor(0, 86, 170)),
            new PaletteStop(160, new RgbColor(0, 0, 255)), new PaletteStop(176, new RgbColor(42, 0, 213)),
            new PaletteStop(192, new RgbColor(85, 0, 171)), new PaletteStop(208, new RgbColor(127, 0, 129)),
            new PaletteStop(224, new RgbColor(171, 0, 85)), new PaletteStop(240, new RgbColor(213, 0, 43)),
        ],
    };

    /// <summary>
    /// Twinkleup reproduces the strip pixel for pixel, and needs only a 256-value search to prove it.
    /// <para>
    /// On palette Default its color collapses to the primary, so the blend leaves each LED's
    /// brightness sitting in the red channel where it can be read straight off the wire. Nothing is
    /// accumulated between frames either - the seed is reset to 535 every frame - so the one unknown in
    /// a captured frame is where the clock had got to, and that enters the arithmetic only as a phase
    /// of 256.
    /// </para>
    /// <para>
    /// One phase out of 256 fits, to under half a unit per LED across all 285. That is the generator,
    /// the sine, the brightness gate and the order of the three draws per LED all confirmed at once -
    /// including the third draw, which does nothing on this palette but has to happen anyway or every
    /// LED after the first is wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void Twinkleup_reproduces_the_strip_pixel_for_pixel()
    {
        RgbColor[] captured = Captured("twinkleup.txt");

        var segment = new EffectSegment(Strip.Roofline)
        {
            Speed = 128,
            Intensity = 128,
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
        };

        var effect = new TwinkleUpEffect();

        double best = double.MaxValue, runnerUp = double.MaxValue;
        int bestPhase = -1;

        // The clock enters as 16 * now / (256 - speed), which at speed 128 is now / 8 - so stepping
        // the clock by 8 steps the phase by one, and 256 of those covers every picture it can draw.
        for (int phase = 0; phase < 256; phase++)
        {
            effect.Render(segment, (uint)(phase * 8));

            double error = Enumerable.Range(0, Strip.Roofline)
                .Sum(i => Math.Abs(segment.Pixels[i].R - captured[i].R)) / (double)Strip.Roofline;

            if (error < best)
            {
                runnerUp = best;
                best = error;
                bestPhase = phase;
            }
            else if (error < runnerUp)
            {
                runnerUp = error;
            }
        }

        output.WriteLine($"best phase {bestPhase}: {best:F2} per LED over {Strip.Roofline}");
        output.WriteLine($"next best phase: {runnerUp:F2} per LED");

        Assert.InRange(best, 0, 2);

        // And no other phase is close, which is what makes one fit mean something.
        Assert.True(runnerUp > best * 8,
            $"the best fit ({best:F2}) should be far better than any other ({runnerUp:F2})");
    }

    /// <summary>
    /// Twinklefox ramps each twinkle up over a third of its cycle and down over the other two, where
    /// Twinklecat snaps it to full and only ever falls.
    /// <para>
    /// Brightness cannot tell them apart and it would be easy to think it could: the two curves enclose
    /// the same area, so both average 65 on the strip. The first version of this test asserted that the
    /// ramping one was brighter and passed by four hundredths of a unit, which is luck rather than
    /// evidence.
    /// </para>
    /// <para>
    /// The real difference is direction. Cat's brightness only falls, so between one frame and the next
    /// almost no LED is brighter than it was; fox spends a third of every cycle rising, so plenty are.
    /// That is the same shape of statistic that separated Running from Saw, and unlike the mean it
    /// follows from the curves rather than in spite of them.
    /// </para>
    /// </summary>
    [Fact]
    public void Twinklefox_ramps_up_where_Twinklecat_only_falls()
    {
        (double foxBright, double foxPeaks, double foxColors) = Measure(new TwinkleFoxEffect());

        double foxRising = Rising(new TwinkleFoxEffect());
        double catRising = Rising(new TwinkleCatEffect());

        output.WriteLine($"simulated fox: brightness {foxBright:F2}, {foxPeaks:F2} peaks, {foxColors:F1} colors");
        output.WriteLine("measured fox : brightness 66.07, 85.79 peaks, 160.1 colors");
        output.WriteLine($"rising between frames: fox {foxRising:F3}, cat {catRising:F3}");

        Assert.InRange(foxBright, 50, 84);
        Assert.InRange(foxPeaks, 68, 104);
        Assert.InRange(foxColors, 125, 195);

        // The one that ramps has LEDs on the way up; the one that snaps has almost none.
        Assert.True(foxRising > catRising * 2,
            $"the ramping variant should have far more LEDs brightening: " +
            $"{foxRising:F3} against {catRising:F3}");
    }

    /// <summary>What share of LEDs are brighter than they were a frame ago.</summary>
    private static double Rising(IWledEffect effect)
    {
        var segment = new EffectSegment(Strip.Roofline)
        {
            Speed = 128,
            Intensity = 128,
            FrameMilliseconds = Strip.FrameMs,
            PaletteId = 11,
            Palette = Rainbow(),
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
        };

        byte[]? before = null;
        List<double> rising = [];

        // Sampled straight off consecutive frames rather than through the preview, because "brighter
        // than a frame ago" means the frame before and not the sample before.
        for (uint t = 0; t < 4_000; t += (uint)Strip.FrameMs)
        {
            segment.Draw(effect, t);

            byte[] now = [.. segment.Pixels.Select(p => Math.Max(p.R, Math.Max(p.G, p.B)))];

            if (before is not null && t > 500)
            {
                rising.Add((double)Enumerable.Range(0, now.Length).Count(i => now[i] > before[i])
                    / now.Length);
            }

            before = now;
        }

        return rising.Average();
    }

    /// <summary>
    /// Stream 2 builds a chain of colors down the run, each LED mostly copying its neighbor, and slides
    /// the whole chain along one place per tick.
    /// <para>
    /// The picture only changes on a tick - the strip changed 30 times in twelve seconds, which is
    /// every 400 ms against the 406 the cycle formula gives - and within a frame it is long stretches
    /// of near-identical color broken by sudden shifts, which is the one-in-six chance per channel of
    /// being replaced rather than copied: 16.9 local maxima along 285 LEDs.
    /// </para>
    /// <para>
    /// The chain is rebuilt from a saved seed every frame rather than stored, so sliding it along is
    /// what the saved seed is for. A preview cannot share the strip's seed, so what is checked is the
    /// texture and the tick rather than the colors.
    /// </para>
    /// </summary>
    [Fact]
    public void Stream_2_slides_a_chain_of_mostly_copied_colors_along_the_run()
    {
        var segment = new EffectSegment(Strip.Roofline)
        {
            Speed = 128,
            Intensity = 128,
            FrameMilliseconds = Strip.FrameMs,
            PaletteId = 11,
            Palette = Rainbow(),
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
        };

        List<double> peaks = [], colors = [];
        int changes = 0;
        RgbColor[]? previous = null;

        Strip.Sample(new RandomChaseEffect(), segment, 12_000, (frame, _) =>
        {
            peaks.Add(Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B))).Bands);
            colors.Add(frame.Distinct().Count());

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                changes++;
            }

            previous = [.. frame];
        });

        output.WriteLine($"simulated: {peaks.Average():F2} peaks, {colors.Average():F1} colors, " +
            $"a change every {12_000.0 / changes:F0} ms");
        output.WriteLine("measured : 16.91 peaks, 114.9 colors, a change every 400 ms");
        output.WriteLine("arithmetic: 25 + 3 * (255 - 128) = 406 ms a tick");

        // Still between ticks, which is what says the chain slides rather than being redrawn.
        Assert.InRange(12_000.0 / changes, 340, 470);

        // Long stretches of copied color with occasional breaks.
        Assert.InRange(peaks.Average(), 10, 26);
        Assert.InRange(colors.Average(), 80, 150);
    }

    private static (double Brightness, double Peaks, double Colors) Measure(IWledEffect effect)
    {
        var segment = new EffectSegment(Strip.Roofline)
        {
            Speed = 128,
            Intensity = 128,
            FrameMilliseconds = Strip.FrameMs,
            PaletteId = 11,
            Palette = Rainbow(),
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
        };

        List<double> bright = [], peaks = [], colors = [];

        Strip.Sample(effect, segment, 12_000, (frame, _) =>
        {
            Strip.Shape shape = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));
            bright.Add(shape.Mean);
            peaks.Add(shape.Bands);
            colors.Add(frame.Distinct().Count());
        });

        return (bright.Average(), peaks.Average(), colors.Average());
    }

    /// <summary>One captured frame, in segment order - the roofline is wired back to front.</summary>
    private static RgbColor[] Captured(string fixture)
    {
        string line = File.ReadAllLines(Path.Combine("Fixtures", fixture))
            .First(l => !l.StartsWith('#') && l.Length >= Strip.Roofline * 6);

        var frame = new RgbColor[Strip.Roofline];

        for (int i = 0; i < Strip.Roofline; i++)
        {
            int at = i * 6;

            frame[Strip.Roofline - 1 - i] = new RgbColor(
                Convert.ToByte(line.Substring(at, 2), 16),
                Convert.ToByte(line.Substring(at + 2, 2), 16),
                Convert.ToByte(line.Substring(at + 4, 2), 16));
        }

        return frame;
    }
}
