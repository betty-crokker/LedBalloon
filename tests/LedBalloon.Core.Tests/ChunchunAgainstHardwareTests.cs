using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Measured against the real thing.
/// <para>
/// 120 frames were captured off the north controller's live feed while it was actually running
/// July 4th on the 308 LED porchline at speed 172 and intensity 128, by asking its websocket for
/// the LED buffer. That is 7.5 seconds, a bit under two full sweeps. Over those frames it lit
/// between 141 and 269 LEDs, averaging 214, with about 43 bright at a time - one per bird - so
/// each bird showed a head and around four LEDs of trail.
/// </para>
/// <para>
/// A whole sweep, because a shorter capture is worthless here and produced a wrong answer first
/// time round. The flock bunches at the sine's turning points, so how much of the run is lit swings
/// between a third and nine tenths of it within one sweep; eight frames spanning 412 ms of a 4096 ms
/// period sampled a tenth of that swing and made the port look 37% out when it was not.
/// </para>
/// <para>
/// The same capture confirmed the period. Comparing every frame against the first, the per-LED
/// difference sits around 130 at one, two and three seconds and falls to 60 at 4072 ms - the
/// picture is most like itself after one predicted 4096 ms sweep, which is what the formula says.
/// </para>
/// <para>
/// A port that quietly drifts from this is worse than no port, because the photo would look
/// authoritative while being wrong. These pin the numbers so a later change has to answer for it.
/// </para>
/// </summary>
public class ChunchunAgainstHardwareTests(ITestOutputHelper output)
{
    private const int Porchline = 308;

    /// <summary>The range the hardware was measured across, over 1.8 sweeps.</summary>
    private const int MeasuredLowest = 141;

    private const int MeasuredHighest = 269;

    /// <summary>And what it averaged, which is the number worth matching.</summary>
    private const int MeasuredMean = 214;

    /// <summary>Bright LEDs per frame on the strip: one per bird.</summary>
    private const int MeasuredHeads = 43;

    private static EffectSegment Porch()
    {
        var segment = new EffectSegment(Porchline)
        {
            Speed = 172,
            Intensity = 128,
            PaletteId = 0,
            Colors = [new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black],
        };

        return segment;
    }

    /// <summary>Lit counts across a whole sweep, at the controller's own frame time.</summary>
    private static List<int> LitAcrossASweep()
    {
        var effect = new ChunchunEffect();
        EffectSegment segment = Porch();

        const int FrameMs = EffectSimulation.DefaultFrameMilliseconds;
        var counts = new List<int>();

        // One full 4096 ms sweep, having let the trails build up first.
        for (uint t = 0; t < 4096 + (40 * FrameMs); t += FrameMs)
        {
            effect.Render(segment, t);

            if (t > 40 * FrameMs)
            {
                counts.Add(segment.Pixels.Count(p => p.R + p.G + p.B > 0));
            }
        }

        return counts;
    }

    [Fact]
    public void The_simulation_lights_about_as_much_of_the_run_as_the_hardware_does()
    {
        List<int> counts = LitAcrossASweep();

        output.WriteLine(
            $"simulated across a sweep: low={counts.Min()} high={counts.Max()} " +
            $"mean={counts.Average():F0}");
        output.WriteLine(
            $"measured on the strip:    low={MeasuredLowest} high={MeasuredHighest} " +
            $"mean={MeasuredMean}");

        // Within a tenth of what the strip did, which is as close as this can be checked: the two
        // are not in step, so they are being compared as distributions rather than frame by frame.
        Assert.InRange(counts.Average(), MeasuredMean * 0.9, MeasuredMean * 1.1);

        // And the swing has to be there too. A port that lit a steady two thirds of the run would
        // match on the average while missing the thing that makes it look like anything.
        Assert.InRange(counts.Min(), MeasuredLowest - 30, MeasuredLowest + 40);
        Assert.InRange(counts.Max(), MeasuredHighest - 40, MeasuredHighest + 30);
    }

    [Fact]
    public void The_flock_is_forty_strong_the_way_the_strip_showed_forty_bright_leds()
    {
        var effect = new ChunchunEffect();
        EffectSegment segment = Porch();

        effect.Render(segment, 5000);

        // One bird per eight LEDs plus two. The strip averaged 43 bright LEDs a frame, ranging
        // 29 to 49 - the spread being birds that happen to share an LED, and trail heads that have
        // not dimmed past the threshold yet.
        int heads = segment.Pixels.Count(p => p.R + p.G + p.B > 200);

        output.WriteLine($"heads drawn this frame: {heads}, strip averaged {MeasuredHeads}");
        Assert.InRange(heads, 28, 50);
    }

    [Fact]
    public void Trail_length_per_bird_matches_what_was_measured()
    {
        List<int> counts = LitAcrossASweep();

        // 40 birds into the lit count. The strip came to 5.36; this should land beside it.
        double perBird = counts.Average() / 40.0;
        const double OnTheStrip = MeasuredMean / 40.0;

        output.WriteLine($"simulated LEDs per bird: {perBird:F2}");
        output.WriteLine($"measured on the strip:   {OnTheStrip:F2}");

        Assert.InRange(perBird, OnTheStrip * 0.85, OnTheStrip * 1.15);
    }
}
