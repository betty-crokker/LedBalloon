using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Perlin noise checked against the strip pixel for pixel, which nothing else here has managed.
/// <para>
/// Every other effect built on randomness can only be compared in aggregate, because the
/// controller's generator has its own state and nothing can line it up with ours. Noise is
/// different: it is a smooth function of position, not a draw, so the same coordinates must give the
/// same answer on both - and if they do not, the port is wrong in a way no statistic would catch.
/// </para>
/// <para>
/// Fill Noise is the effect that makes the comparison possible. Its whole output is
/// <c>inoise8(i * length, step + i * length)</c> looked up in the palette, with one unknown: the
/// accumulated step, which starts from a random seed. So the test searches all 65536 possible steps
/// for the one that reproduces the captured frame. If the noise function is right, exactly one step
/// fits and it fits nearly exactly; if it is wrong, none fits at all.
/// </para>
/// </summary>
public class PerlinAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Roofline = 285;

    /// <summary>Palette 11, "Rainbow", as <c>/json/palx</c> reports it - sixteen evenly spaced stops.</summary>
    private static WledPalette Rainbow() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(255, 0, 0)),
            new PaletteStop(16, new RgbColor(213, 42, 0)),
            new PaletteStop(32, new RgbColor(171, 85, 0)),
            new PaletteStop(48, new RgbColor(171, 127, 0)),
            new PaletteStop(64, new RgbColor(171, 171, 0)),
            new PaletteStop(80, new RgbColor(86, 213, 0)),
            new PaletteStop(96, new RgbColor(0, 255, 0)),
            new PaletteStop(112, new RgbColor(0, 213, 42)),
            new PaletteStop(128, new RgbColor(0, 171, 85)),
            new PaletteStop(144, new RgbColor(0, 86, 170)),
            new PaletteStop(160, new RgbColor(0, 0, 255)),
            new PaletteStop(176, new RgbColor(42, 0, 213)),
            new PaletteStop(192, new RgbColor(85, 0, 171)),
            new PaletteStop(208, new RgbColor(127, 0, 129)),
            new PaletteStop(224, new RgbColor(171, 0, 85)),
            new PaletteStop(240, new RgbColor(213, 0, 43)),
        ],
    };

    /// <summary>The captured frames, in segment order - the wire runs back to front.</summary>
    private static List<RgbColor[]> Captured()
    {
        List<RgbColor[]> frames = [];

        foreach (string line in File.ReadAllLines(Path.Combine("Fixtures", "fill-noise.txt")))
        {
            if (line.StartsWith('#') || line.Length < Roofline * 6)
            {
                continue;
            }

            var frame = new RgbColor[Roofline];

            for (int i = 0; i < Roofline; i++)
            {
                int at = i * 6;

                frame[Roofline - 1 - i] = new RgbColor(
                    Convert.ToByte(line.Substring(at, 2), 16),
                    Convert.ToByte(line.Substring(at + 2, 2), 16),
                    Convert.ToByte(line.Substring(at + 4, 2), 16));
            }

            frames.Add(frame);
        }

        return frames;
    }

    private static EffectSegment Run()
    {
        var segment = new EffectSegment(Roofline)
        {
            Speed = 0,
            Intensity = 128,
            PaletteId = 11,
            Palette = Rainbow(),
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
        };

        return segment;
    }

    /// <summary>What Fill Noise would draw at a given accumulated step.</summary>
    private static RgbColor[] Predict(EffectSegment segment, int step)
    {
        var frame = new RgbColor[Roofline];

        for (int i = 0; i < Roofline; i++)
        {
            byte index = Perlin.Noise8(
                (ushort)(i * Roofline),
                (ushort)(step + (i * Roofline)));

            frame[i] = segment.ColorFromPalette(index);
        }

        return frame;
    }

    /// <summary>Average error per channel between a prediction and a capture.</summary>
    private static double Error(RgbColor[] predicted, RgbColor[] captured, int upTo)
    {
        double total = 0;

        for (int i = 0; i < upTo; i++)
        {
            total += Math.Abs(predicted[i].R - captured[i].R)
                + Math.Abs(predicted[i].G - captured[i].G)
                + Math.Abs(predicted[i].B - captured[i].B);
        }

        return total / (upTo * 3.0);
    }

    /// <summary>
    /// Finds the step that best reproduces a captured frame, and how much better it is than the
    /// runner-up that is not near it.
    /// </summary>
    private static (int Step, double Error, double NextBest) Solve(EffectSegment segment, RgbColor[] captured)
    {
        int best = 0;
        double bestError = double.MaxValue;
        double runnerUp = double.MaxValue;

        for (int step = 0; step < 65536; step++)
        {
            // Twenty LEDs is plenty to rule a step out, so only twenty are worked out - drawing all
            // 285 for every candidate makes this fourteen times slower for nothing.
            double error = 0;

            for (int i = 0; i < 20; i++)
            {
                byte index = Perlin.Noise8(
                    (ushort)(i * Roofline),
                    (ushort)(step + (i * Roofline)));

                RgbColor at = segment.ColorFromPalette(index);

                error += Math.Abs(at.R - captured[i].R)
                    + Math.Abs(at.G - captured[i].G)
                    + Math.Abs(at.B - captured[i].B);
            }

            error /= 60.0;

            if (error < bestError)
            {
                if (Math.Abs(step - best) > 4)
                {
                    runnerUp = bestError;
                }

                bestError = error;
                best = step;
            }
            else if (error < runnerUp && Math.Abs(step - best) > 4)
            {
                runnerUp = error;
            }
        }

        return (best, Error(Predict(segment, best), captured, Roofline), runnerUp);
    }

    /// <summary>
    /// The noise function reproduces the strip exactly, to within the rounding of a palette lookup.
    /// <para>
    /// One step out of 65536 fits, and it fits to about a unit per channel - which is the difference
    /// between two ways of interpolating sixteen palette stops, not a difference in the noise. Every
    /// other step is off by tens of units, because noise a single step away is a different picture.
    /// </para>
    /// <para>
    /// This is what the eight-bit arithmetic was worth getting right. The permutation table is walked
    /// three times, not twice, and a port that stopped a level short gives noise that looks entirely
    /// plausible and would have passed any statistical test in this file.
    /// </para>
    /// </summary>
    [Fact]
    public void Perlin_noise_reproduces_the_strip_pixel_for_pixel()
    {
        List<RgbColor[]> frames = Captured();
        Assert.Equal(2, frames.Count);

        EffectSegment segment = Run();

        (int step, double error, double nextBest) = Solve(segment, frames[0]);

        output.WriteLine($"best step {step}: {error:F2} per channel over all {Roofline} LEDs");
        output.WriteLine($"next best step elsewhere: {nextBest:F2} per channel over the first 20");

        // A palette lookup's rounding, and nothing more.
        Assert.InRange(error, 0, 4);

        // And no other step comes close, which is what makes one fit meaningful.
        Assert.True(nextBest > error * 8,
            $"the best fit ({error:F2}) should be far better than any other ({nextBest:F2})");
    }

    /// <summary>
    /// The second frame fits too, at a step a little further on - which is the effect advancing rather
    /// than a coincidence.
    /// <para>
    /// Speed zero still moves it: the step grows by <c>beatsin8(0, 1, 6)</c> a frame, which is a
    /// constant 3. The two frames were about 360 ms apart at 9 ms a frame, so around 120 steps, and
    /// solving them separately should land about that far apart.
    /// </para>
    /// </summary>
    [Fact]
    public void The_second_frame_fits_a_step_a_little_further_on()
    {
        List<RgbColor[]> frames = Captured();
        EffectSegment segment = Run();

        (int first, double firstError, _) = Solve(segment, frames[0]);
        (int second, double secondError, _) = Solve(segment, frames[1]);

        output.WriteLine($"frame one fits step {first} at {firstError:F2} per channel");
        output.WriteLine($"frame two fits step {second} at {secondError:F2} per channel");
        output.WriteLine($"apart by {second - first}, where 360 ms at 3 a frame is about 120");

        Assert.InRange(secondError, 0, 4);

        // Forward, and by roughly what the frame rate and the step size predict.
        Assert.InRange(second - first, 20, 400);
    }

    /// <summary>
    /// The shape of the function, checked without the strip: smooth, centered, and periodic in the
    /// way eight-bit noise has to be.
    /// </summary>
    [Fact]
    public void Perlin_noise_is_smooth_and_centered()
    {
        byte[] line = [.. Enumerable.Range(0, 4096).Select(i => Perlin.Noise8((ushort)(i * 16)))];

        double mean = line.Average(x => (double)x);
        double biggestStep = Enumerable.Range(1, line.Length - 1).Max(i => Math.Abs(line[i] - line[i - 1]));

        output.WriteLine($"mean {mean:F1}, biggest step between neighbours {biggestStep}");
        output.WriteLine($"range {line.Min()}-{line.Max()}");

        // Centered on the middle of the byte, which is what shifting and doubling the raw value gives.
        Assert.InRange(mean, 110, 145);

        // Smooth: sixteen units of input apart cannot jump the whole range.
        Assert.True(biggestStep < 60, $"neighbours jumped by {biggestStep}");

        // And it uses most of the byte without being uniform over it.
        Assert.True(line.Max() > 220);
        Assert.True(line.Min() < 40);
    }
}
