using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Dissolve and Dissolve Rnd, captured on the south controller's 285 LED roofline with the color
/// slots set to red, blue and green.
/// <para>
/// These are the first two ported effects that are not a function of the clock at all. They keep the
/// picture they drew last and change a handful of LEDs in it each frame, so nothing about them can
/// be worked out from a timestamp - what can be checked is how fast the run fills, how long it
/// stays full, and how often it turns round.
/// </para>
/// </summary>
public class DissolveEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor Background = new(0, 0, 255);

    /// <param name="Full">Frames with the whole run covered, which is where it waits.</param>
    /// <param name="Empty">Frames back at the background, where it waits again.</param>
    /// <param name="Cycles">How many times it filled, over the capture.</param>
    private sealed record Dissolving(
        double Mean, double Most, double Least, int Full, int Empty, int Cycles, int Frames, int MostFills);

    private static Dissolving Measure(IWledEffect effect, uint milliseconds)
    {
        List<double> filled = [];
        int most = 0;

        Strip.Sample(effect, Strip.Run(), milliseconds, (frame, _) =>
        {
            filled.Add((double)frame.Count(p => p != Background) / frame.Length);
            most = Math.Max(most, Strip.Fills(frame));
        });

        int cycles = filled.Zip(filled.Skip(1)).Count(p => p.First <= 0.5 && p.Second > 0.5);

        return new Dissolving(
            filled.Average(), filled.Max(), filled.Min(),
            filled.Count(x => x > 0.99), filled.Count(x => x < 0.01),
            cycles, filled.Count, most);
    }

    /// <summary>
    /// It fills the run well before it is due to turn round, then waits, then empties and waits
    /// again.
    /// <para>
    /// Speed does not set how fast it dissolves - intensity does. Speed sets how long it waits
    /// before reversing, counted in frames rather than in milliseconds: 255 less the slider, plus
    /// fifteen. At the middle of both sliders the filling takes about thirty frames and the wait a
    /// hundred and forty, which is why the strip spent 105 of 280 frames completely full and 111
    /// completely empty and only the remaining quarter actually dissolving.
    /// </para>
    /// </summary>
    [Fact]
    public void Dissolve_fills_the_run_quickly_then_waits_before_turning_round()
    {
        Dissolving measured = Measure(new DissolveEffect(), 16_000);

        output.WriteLine($"simulated: mean {measured.Mean:F3}, {measured.Full} of {measured.Frames} full, " +
            $"{measured.Empty} empty, {measured.Cycles} cycles, {measured.MostFills} fills");
        output.WriteLine("measured : mean 0.487, 105 of 280 full, 111 empty, 6 cycles, 2 fills");

        Assert.Equal(1, measured.Most, 2);
        Assert.Equal(0, measured.Least, 2);
        Assert.Equal(2, measured.MostFills);
        Assert.InRange(measured.Mean, 0.42, 0.56);

        // Most of the time it is sitting at one end or the other rather than dissolving.
        Assert.InRange(measured.Full + measured.Empty, measured.Frames * 6 / 10, measured.Frames);
    }

    /// <summary>
    /// The turn is counted in frames, so its period measures the controller's frame rate.
    /// <para>
    /// At the middle of the speed slider a half cycle is 142 frames, so a full one is 284. The strip
    /// came round every 2667 ms, which puts a frame at 9.39 ms - 106.5 frames a second, against the
    /// 107 the controller reports for itself. Two independent measurements of the same thing, and
    /// neither of them the 42 that <c>hw.led.fps</c> claims.
    /// </para>
    /// <para>
    /// The simulation is drawn at a flat 9 ms, so it comes round at 2556 ms instead. The 4% is that
    /// rounding and nothing else.
    /// </para>
    /// </summary>
    [Fact]
    public void Dissolve_turns_round_on_a_frame_count_rather_than_a_clock()
    {
        Dissolving measured = Measure(new DissolveEffect(), 16_000);

        double cycleMs = 16_000.0 / measured.Cycles;

        output.WriteLine($"simulated: {measured.Cycles} cycles in 16 s, {cycleMs:F0} ms each");
        output.WriteLine("measured : 6 cycles in 16 s, 2667 ms each");
        output.WriteLine($"arithmetic: 2 * ((255 - 128) + 15) = 284 frames, " +
            $"which is {284 * Strip.FrameMs} ms at 9 ms and 2667 ms at the strip's 9.39");

        Assert.InRange(cycleMs, 2400, 2800);
    }

    /// <summary>
    /// The random variant takes a fresh color off the wheel for every LED it spawns, so the run
    /// fills in confetti rather than in one color.
    /// <para>
    /// The timing is identical - the strip gave the same 2667 ms and the same mean coverage - and
    /// the only difference is how many colors are on the run at once: two for Dissolve against 26
    /// for Dissolve Rnd.
    /// </para>
    /// </summary>
    [Fact]
    public void Dissolve_Rnd_fills_the_run_in_confetti_on_the_same_timing()
    {
        Dissolving random = Measure(new DissolveRandomEffect(), 16_000);
        Dissolving plain = Measure(new DissolveEffect(), 16_000);

        output.WriteLine($"simulated: mean {random.Mean:F3}, {random.Cycles} cycles, " +
            $"{random.MostFills} fills at most");
        output.WriteLine("measured : mean 0.497, 6 cycles, 26 fills at most");

        Assert.InRange(random.Mean, 0.42, 0.56);
        Assert.Equal(plain.Cycles, random.Cycles);

        // Nothing like the two a flat dissolve shows, which is the whole of the difference.
        Assert.InRange(random.MostFills, 12, 40);
    }
}
