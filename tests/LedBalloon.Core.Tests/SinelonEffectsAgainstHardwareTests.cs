using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The three Sinelons and Juggle, captured on the south controller's 285 LED roofline with the color
/// slots set to red, blue and green.
/// <para>
/// Sinelon fades toward the secondary color and Juggle fades to black, which is the difference
/// between a red streak running through a blue run and eight dots on nothing. Porting Sinelon with
/// the wrong one of those was caught by the first capture: every LED is lit on the strip and the
/// port had the run dark but for a dot.
/// </para>
/// </summary>
public class SinelonEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    /// <param name="RedTrail">LEDs still carrying real red, which is how long the streak reads.</param>
    /// <param name="Swings">Times the dot turned round, which times the sine.</param>
    private sealed record Streak(
        double RedTrail, int RedTrailMost, double GreenTrail, double MeanRed,
        int Swings, int Lowest, int Highest);

    private static Streak Measure(IWledEffect effect, uint milliseconds)
    {
        List<int> spots = [], red = [], green = [];
        List<double> meanRed = [];

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            int at = 0;

            for (int i = 1; i < frame.Length; i++)
            {
                if (frame[i].R > frame[at].R)
                {
                    at = i;
                }
            }

            spots.Add(at);
            red.Add(frame.Count(p => p.R > 32));
            green.Add(frame.Count(p => p.G > 32));
            meanRed.Add(frame.Average(p => (double)p.R));
        });

        int swings = 0, last = 0;

        foreach (int direction in spots.Zip(spots.Skip(1)).Select(p => Math.Sign(p.Second - p.First)))
        {
            if (direction == 0)
            {
                continue;
            }

            if (last != 0 && direction != last)
            {
                swings++;
            }

            last = direction;
        }

        return new Streak(
            red.Average(), red.Max(), green.Average(), meanRed.Average(),
            swings, spots.Min(), spots.Max());
    }

    /// <summary>
    /// A dot on a sine, leaving a long red streak that fades into the blue background.
    /// <para>
    /// The sine is <c>beatsin16(speed / 10, ...)</c>, which at the middle of the slider is twelve
    /// beats a minute - a swing every five seconds, and the strip gave 5000 ms. The dot reaches both
    /// ends of the run exactly.
    /// </para>
    /// <para>
    /// The streak is long because the fade is slow: at intensity 128 the rate works out at three
    /// parts in 256 a frame, so red takes about a second to drop out of sight and the dot covers a
    /// hundred and ninety LEDs in that time. The strip held 191 LEDs of real red.
    /// </para>
    /// <para>
    /// The dot cannot reach the very last LED, in the firmware or here: the sine tops out at 32767,
    /// which scaled over 284 lands on 283. So the run is covered bar one end, and the strip's
    /// apparent reach to both ends was the tracker rather than the dot - with no red anywhere it
    /// returns index zero, and on a strip read back to front that reads as the far end.
    /// </para>
    /// </summary>
    [Fact]
    public void Sinelon_swings_every_five_seconds_and_leaves_a_long_streak()
    {
        Streak measured = Measure(new SinelonEffect(), 20_000);

        output.WriteLine($"simulated: red trail {measured.RedTrail:F1} (max {measured.RedTrailMost}), " +
            $"mean red {measured.MeanRed:F1}, green {measured.GreenTrail:F1}, " +
            $"{measured.Swings} swings, {40_000.0 / measured.Swings:F0} ms each, " +
            $"reaching {measured.Lowest}-{measured.Highest}");
        output.WriteLine("measured : red trail 191.2 (max 262), mean red 90.8, green 0.0, " +
            "8 swings, 5000 ms each, reaching 0-284");
        output.WriteLine("arithmetic: 128 / 10 = 12 beats a minute, so 4993 ms a swing");

        Assert.InRange(40_000.0 / measured.Swings, 4500, 5500);
        Assert.InRange(measured.RedTrail, 150, 230);
        Assert.InRange(measured.MeanRed, 70, 115);

        // No green anywhere: the third slot is the mirror's, and this one has no mirror.
        Assert.Equal(0, measured.GreenTrail, 3);

        // Essentially the whole run, bar the one LED the sine's scaling cannot reach.
        Assert.InRange(measured.Lowest, 0, 2);
        Assert.InRange(measured.Highest, Strip.Roofline - 3, Strip.Roofline - 1);
    }

    /// <summary>
    /// The mirror takes the third color slot, and it is the same streak the other way round: the
    /// strip held 134.5 LEDs of red and 134.6 of green.
    /// <para>
    /// Shorter than single Sinelon's 191, because the two streaks overlap in the middle and each
    /// one is fading the other out where they cross.
    /// </para>
    /// </summary>
    [Fact]
    public void Sinelon_Dual_mirrors_the_streak_in_the_third_color_slot()
    {
        Streak measured = Measure(new SinelonDualEffect(), 20_000);

        output.WriteLine($"simulated: red {measured.RedTrail:F1}, green {measured.GreenTrail:F1}, " +
            $"{measured.Swings} swings, {40_000.0 / measured.Swings:F0} ms each");
        output.WriteLine("measured : red 134.5, green 134.6, 8 swings, 5000 ms each");

        Assert.InRange(40_000.0 / measured.Swings, 4500, 5500);
        Assert.InRange(measured.RedTrail, 110, 165);
        Assert.InRange(measured.GreenTrail, 110, 165);

        // The two are the same sweep drawn twice, so they match each other closely.
        Assert.InRange(Math.Abs(measured.RedTrail - measured.GreenTrail), 0, 12);
    }

    /// <summary>
    /// The rainbow variant colors the dot from the wheel in eight steps, so red and green both
    /// appear and neither dominates.
    /// <para>
    /// The swing is not checked here. Following the reddest pixel works for a red dot and not for
    /// one that changes color: on the strip that tracker reported forty turns in twenty seconds
    /// where the dot swings four times, which is the tracker jumping between hues rather than
    /// anything the effect does.
    /// </para>
    /// </summary>
    [Fact]
    public void Sinelon_Rainbow_colors_the_dot_from_the_wheel()
    {
        Streak measured = Measure(new SinelonRainbowEffect(), 20_000);

        output.WriteLine($"simulated: red {measured.RedTrail:F1}, green {measured.GreenTrail:F1}, " +
            $"mean red {measured.MeanRed:F1}");
        output.WriteLine("measured : red 68.1, green 64.3, mean red 26.2");

        // Both channels present and neither one the whole story, which a palette dot would not do.
        Assert.InRange(measured.RedTrail, 40, 110);
        Assert.InRange(measured.GreenTrail, 40, 110);
    }

    /// <summary>
    /// Eight dots on eight tempos, each leaving a short trail that fades to black rather than to the
    /// background.
    /// <para>
    /// Far less lit than Sinelon at the same settings, and that is the fade rather than the dots:
    /// the strip carried 12.5 LEDs of red and 10.2 of green where Sinelon carried 191 and none.
    /// Green appears at all because the dots take their hue from the wheel.
    /// </para>
    /// </summary>
    [Fact]
    public void Juggle_weaves_eight_short_dots_over_black()
    {
        Streak measured = Measure(new JuggleEffect(), 16_000);

        output.WriteLine($"simulated: red {measured.RedTrail:F1} (max {measured.RedTrailMost}), " +
            $"green {measured.GreenTrail:F1}, reaching {measured.Lowest}-{measured.Highest}");
        output.WriteLine("measured : red 12.5 (max 17), green 10.2, reaching 0-284");

        Assert.InRange(measured.RedTrail, 6, 22);
        Assert.InRange(measured.GreenTrail, 5, 20);

        // Eight dots and a short trail each, so nowhere near a run's worth.
        Assert.True(measured.RedTrailMost < 60, "the trails should be short");
    }
}
