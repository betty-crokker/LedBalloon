using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Noise Pal and TV Simulator, captured on the south controller's 285 LED roofline on palette Default -
/// which for both of these is the only setting worth capturing, since each invents its own colors and a
/// chosen palette throws that away.
/// <para>
/// They are opposites. Noise Pal is all structure and no timing: fifty-eight peaks along the run and a
/// new picture almost every frame. TV Simulator is all timing and no structure: one flat color across the
/// whole run, changing a few times a second.
/// </para>
/// </summary>
public class ScreenEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>
    /// TV Simulator lights the whole run one color, and the strip has exactly one color in every frame.
    /// <para>
    /// Not approximately one - one, in all 341 captured frames, with a spatial deviation of exactly zero.
    /// That is the whole shape of this effect and the only one here that has none: it is a lamp whose
    /// subject is when it changes rather than what it looks like.
    /// </para>
    /// <para>
    /// About a quarter of consecutive samples differ, which is the time spent fading. A color lasts a
    /// quarter second to two and a half, and the fade takes anywhere from none of that to all of it, with
    /// none forced three times in ten - so roughly a third of the time it is on the move and the rest it
    /// is holding. Eighty distinct colors over twenty seconds.
    /// </para>
    /// </summary>
    [Fact]
    public void Tv_simulator_lights_the_whole_run_one_color()
    {
        EffectSegment segment = Run();

        int mostColors = 0, fewestColors = int.MaxValue, differing = 0, frames = 0;
        double spread = 0, brightness = 0;
        HashSet<RgbColor> colors = [];
        RgbColor[]? previous = null;

        Strip.Sample(new TvSimulatorEffect(), segment, 20_000, (frame, _) =>
        {
            Strip.Shape profile = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            spread += profile.Deviation;
            brightness += profile.Mean;
            frames++;

            int distinct = frame.Distinct().Count();
            mostColors = Math.Max(mostColors, distinct);
            fewestColors = Math.Min(fewestColors, distinct);

            colors.Add(frame[0]);

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                differing++;
            }

            previous = [.. frame];
        });

        output.WriteLine($"simulated: {fewestColors} to {mostColors} colors a frame, " +
            $"spread {spread / frames:F2}, brightness {brightness / frames:F2}, " +
            $"{(double)differing / (frames - 1):F3} of samples differ, {colors.Count} colors in all");
        output.WriteLine("measured : 1 to 1 colors a frame, spread 0.00, brightness 75.01, " +
            "0.238 of samples differ, 80 colors in all");

        // One color, every frame, exactly.
        Assert.Equal(1, mostColors);
        Assert.Equal(1, fewestColors);
        Assert.Equal(0.0, spread);

        // Moving about a quarter to a third of the time and holding the rest.
        Assert.InRange((double)differing / (frames - 1), 0.12, 0.45);

        Assert.InRange(colors.Count, 25, 200);
        Assert.InRange(brightness / frames, 20, 190);
    }

    /// <summary>
    /// TV Simulator never holds one color for longer than two and a half seconds.
    /// <para>
    /// A color lasts <c>random16(250, 2500)</c> milliseconds, and the fade into it takes some part of
    /// that - so however long it sits still afterwards, the whole thing is over inside two and a half
    /// seconds. Over two minutes the longest unchanging stretch came to just that, which pins the upper
    /// bound of a draw that nothing else about the effect reveals.
    /// </para>
    /// <para>
    /// This started out as a test that three changes in ten are cuts rather than fades, and that turned
    /// out not to be measurable from outside. A slow fade only moves the top eight bits every few frames,
    /// so it looks like a run of single-frame changes - indistinguishable from a run of cuts, which is why
    /// counting them gave six in ten rather than three. The hold time is the part of the same arithmetic
    /// that does survive being watched.
    /// </para>
    /// <para>
    /// Simulation only, and an upper bound rather than an average: a new color that happens to quantize to
    /// the same eight bits as the old one extends a hold silently, so the occasional long one means
    /// nothing while the shape of the limit means something.
    /// </para>
    /// </summary>
    [Fact]
    public void Tv_simulator_never_holds_a_color_for_longer_than_two_and_a_half_seconds()
    {
        EffectSegment segment = Run();
        var effect = new TvSimulatorEffect();

        List<int> holds = [];
        int hold = 0;
        RgbColor last = default;
        bool first = true;

        for (uint t = 0; t < 120_000; t += (uint)Strip.FrameMs)
        {
            segment.Draw(effect, t);

            RgbColor now = segment.Pixels[0];

            if (!first && now != last)
            {
                holds.Add(hold);
                hold = 0;
            }
            else
            {
                hold++;
            }

            last = now;
            first = false;
        }

        double longest = holds.Max() * Strip.FrameMs;
        double mean = holds.Average() * Strip.FrameMs;

        output.WriteLine($"{holds.Count} holds over two minutes, longest {longest:F0} ms, " +
            $"mean {mean:F0} ms");
        output.WriteLine("arithmetic: a color lasts 250 to 2500 ms, fade included");

        // The upper bound of the draw, read off the longest stretch that never changed.
        Assert.InRange(longest, 1_800, 3_000);

        // And plenty of them, so the bound is not one lucky stretch.
        Assert.True(holds.Count > 100, $"two minutes should give plenty of holds: {holds.Count}");
    }

    /// <summary>
    /// Noise Pal shows about fifty-eight peaks along the run and changes almost every frame.
    /// <para>
    /// The peak count is arithmetic: the noise is sampled 47 steps apart per LED at this intensity, and
    /// Perlin noise turns over every 256 steps, so the pattern repeats roughly every five and a half LEDs
    /// - which on 285 of them is about 52 humps. The strip counted 57.7.
    /// </para>
    /// <para>
    /// What it never does is hold still, once it has a palette at all. The noise slides along the run one
    /// to four steps a frame on a slow sine of its own, so four fifths of consecutive samples differ.
    /// </para>
    /// <para>
    /// The dark opening is real, and it settled a question about the harness. This effect invents its
    /// first palette only when the clock has passed one whole change interval, so if the clock an effect
    /// sees restarts with the effect, the run should sit black for 5.28 seconds and then light. A capture
    /// taken with no settling shows exactly that: black through 4.1 seconds, 45 units by 5.5 and 116 by
    /// 6.9. So it does restart - which is what every test here has assumed by starting at zero - and it
    /// is why both sides of this comparison are dragged down by a few seconds of darkness, and why a frame
    /// with only one color in it turns up on both.
    /// </para>
    /// </summary>
    [Fact]
    public void Noise_pal_ripples_along_the_run_and_never_holds_still()
    {
        EffectSegment segment = Run();

        List<double> brightness = [], spread = [], peaks = [], colors = [];
        int differing = 0, samples = 0;
        RgbColor[]? previous = null;

        Strip.Sample(new NoisePalEffect(), segment, 20_000, (frame, _) =>
        {
            Strip.Shape profile = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            brightness.Add(profile.Mean);
            spread.Add(profile.Deviation);
            peaks.Add(profile.Bands);
            colors.Add(frame.Distinct().Count());
            samples++;

            if (previous is not null && !frame.SequenceEqual(previous))
            {
                differing++;
            }

            previous = [.. frame];
        });

        output.WriteLine($"simulated: brightness {brightness.Average():F2}, spread {spread.Average():F2}, " +
            $"{peaks.Average():F2} peaks, {colors.Average():F2} colors a frame " +
            $"(fewest {colors.Min()}), {(double)differing / (samples - 1):F3} of samples differ");
        output.WriteLine("measured : brightness 88.06, spread 17.66, 57.71 peaks, 58.75 colors a frame " +
            "(fewest 1), 0.814 of samples differ");
        output.WriteLine("arithmetic: 15 + (128 >> 2) = 47 steps an LED, 256 to a turn, so 285 / 5.4 " +
            "= 52 humps");

        Assert.InRange(peaks.Average(), 40, 72);
        Assert.InRange(brightness.Average(), 60, 120);
        Assert.InRange(spread.Average(), 10, 26);
        Assert.InRange(colors.Average(), 35, 85);

        // A frame with one color in it, which is the dark opening before the first palette exists.
        Assert.Equal(1, colors.Min());

        Assert.InRange((double)differing / (samples - 1), 0.5, 1.0);
    }

    /// <summary>
    /// Noise Pal waits one change interval and then crawls toward its first palette.
    /// <para>
    /// Nothing at all for 5.28 seconds, because it only invents a palette once the clock has passed
    /// <c>4000 + speed * 10</c> - and then the run does not switch on, it climbs, one unit per channel per
    /// frame. The strip was black through 4.1 seconds, at 45 units by 5.5 and 116 by 6.9, which is a wait
    /// and then a climb rather than either alone.
    /// </para>
    /// </summary>
    [Fact]
    public void Noise_pal_waits_and_then_crawls_toward_its_first_palette()
    {
        EffectSegment segment = Run();
        var effect = new NoisePalEffect();

        double atFour = 0, atFiveAndAHalf = 0, atSeven = 0;

        for (uint t = 0; t < 8_000; t += (uint)Strip.FrameMs)
        {
            segment.Draw(effect, t);

            double mean = segment.Pixels.Average(p => (double)Math.Max(p.R, Math.Max(p.G, p.B)));

            if (t < 4_100)
            {
                atFour = mean;
            }
            else if (t < 5_500)
            {
                atFiveAndAHalf = mean;
            }
            else if (t < 7_000)
            {
                atSeven = mean;
            }
        }

        output.WriteLine($"simulated: {atFour:F1} at 4.1 s, {atFiveAndAHalf:F1} at 5.5 s, " +
            $"{atSeven:F1} at 6.9 s");
        output.WriteLine("measured : 0.0 at 4.1 s, 45.2 at 5.5 s, 115.8 at 6.9 s");
        output.WriteLine("arithmetic: 4000 + 128 * 10 = 5280 ms before the first palette exists");

        // Nothing before the first palette exists.
        Assert.Equal(0.0, atFour);

        // Then a climb rather than a switch: on its way at five and a half, arrived by seven.
        Assert.InRange(atFiveAndAHalf, 1, 90);
        Assert.True(atSeven > atFiveAndAHalf,
            $"it should still be climbing: {atFiveAndAHalf:F1} to {atSeven:F1}");
        Assert.True(atSeven > 60, $"and have arrived by seven seconds: {atSeven:F1}");
    }

    /// <summary>The run as both captures had it - palette Default, since both invent their own colors.</summary>
    private static EffectSegment Run() => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 0,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };
}
