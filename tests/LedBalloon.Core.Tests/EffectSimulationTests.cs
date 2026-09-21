using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The port is only worth anything if it matches the firmware, so these check against numbers read
/// out of WLED's own source rather than out of this implementation. Where a value is an
/// approximation in the firmware too, they check the approximation.
/// </summary>
public class FastLedTests
{
    [Fact]
    public void Sine_crosses_zero_at_nothing_and_at_half_a_turn()
    {
        Assert.Equal(0, FastLed.Sin16(0));
        Assert.Equal(0, FastLed.Sin16(0x8000));
    }

    [Fact]
    public void Sine_peaks_a_quarter_of_the_way_round_and_troughs_three_quarters()
    {
        // Bhaskara's approximation, so near enough to full scale rather than exactly on it.
        Assert.InRange(FastLed.Sin16(0x4000), 32700, 32767);
        Assert.InRange(FastLed.Sin16(0xC000), -32767, -32700);
    }

    [Fact]
    public void Sine_stays_within_a_tenth_of_a_percent_of_the_real_thing()
    {
        // The firmware's error budget: if the port drifted from it, a run would slide against the
        // same run on the wall.
        for (int theta = 0; theta < 65536; theta += 337)
        {
            double exact = Math.Sin(theta / 65536d * 2 * Math.PI) * 32767;
            Assert.InRange(FastLed.Sin16((ushort)theta) - exact, -80, 80);
        }
    }

    [Fact]
    public void Scaling_truncates_the_way_the_firmware_does()
    {
        // Effects lean on scale8(i, 240) to stop a palette lookup short of wrapping round.
        Assert.Equal(0, FastLed.Scale8(0, 240));
        Assert.Equal(120, FastLed.Scale8(128, 240));

        // 255 * 240 >> 8 is 239, not 240. The shift loses the remainder, and that last shade is
        // the whole point of the call: it is what keeps the lookup off the end of the palette.
        Assert.Equal(239, FastLed.Scale8(255, 240));
    }

    [Fact]
    public void A_fade_never_stalls_a_shade_short_of_its_target()
    {
        // The rounding guard in WLED: without it a channel one step away from the background
        // computes a delta of zero and stays lit forever.
        Assert.Equal(0, FastLed.FadeChannel(1, 0, 128));
        Assert.Equal(7, FastLed.FadeChannel(7, 7, 128));
    }
}

/// <summary>
/// Chunchun, checked against what <c>mode_chunchun</c> says it does. The arithmetic here was read
/// off WLED 0.15.3 and is unchanged back to 0.12, so these should hold for any controller anyone
/// points this at.
/// </summary>
public class ChunchunTests
{
    private const int Porchline = 308;

    private static EffectSegment Run(int length = Porchline, byte speed = 172, byte intensity = 128)
    {
        var segment = new EffectSegment(length)
        {
            Speed = speed,
            Intensity = intensity,
            Colors = [new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black],
        };

        return segment;
    }

    private static int Lit(EffectSegment segment) =>
        segment.Pixels.Count(p => p.R + p.G + p.B > 0);

    [Fact]
    public void The_flock_is_two_birds_plus_an_eighth_of_the_run()
    {
        var effect = new ChunchunEffect();
        EffectSegment segment = Run();

        effect.Render(segment, 1000);

        // 2 + (308 >> 3) = 40. Two can land on the same LED, so this is a ceiling.
        Assert.InRange(Lit(segment), 25, 40);
    }

    [Fact]
    public void A_shorter_run_carries_a_smaller_flock()
    {
        var effect = new ChunchunEffect();

        EffectSegment porch = Run();
        EffectSegment stairs = Run(length: 48);

        effect.Render(porch, 1000);
        effect.Render(stairs, 1000);

        // 2 + (48 >> 3) = 8, against 40.
        Assert.True(Lit(stairs) < Lit(porch));
        Assert.InRange(Lit(stairs), 4, 8);
    }

    [Fact]
    public void The_sweep_repeats_on_the_period_the_speed_asks_for()
    {
        var effect = new ChunchunEffect();

        // counter = now * (6 + speed>>4); at speed 172 that is 16 a millisecond, and the sine
        // wraps at 65536, so the flock is back where it started after 65536/16 = 4096 ms.
        EffectSegment first = Run();
        EffectSegment later = Run();

        effect.Render(first, 7_000);
        effect.Render(later, 7_000 + 4096);

        Assert.Equal(first.Pixels, later.Pixels);
    }

    [Fact]
    public void A_slower_setting_really_is_slower()
    {
        var effect = new ChunchunEffect();

        // speed 16 gives 6 + 1 = 7 a millisecond: a 9362 ms sweep, not 4096.
        EffectSegment slow = Run(speed: 16);
        EffectSegment alsoSlow = Run(speed: 16);

        effect.Render(slow, 7_000);
        effect.Render(alsoSlow, 7_000 + 4096);

        Assert.NotEqual(slow.Pixels, alsoSlow.Pixels);
    }

    [Fact]
    public void Without_a_palette_the_whole_flock_is_the_primary_color()
    {
        var effect = new ChunchunEffect();
        EffectSegment segment = Run();

        effect.Render(segment, 1000);

        foreach (RgbColor pixel in segment.Pixels.Where(p => p.R + p.G + p.B > 0))
        {
            Assert.Equal(new RgbColor(255, 0, 0), pixel);
        }
    }

    [Fact]
    public void Each_frame_leaves_a_trail_that_fades_by_half()
    {
        var effect = new ChunchunEffect();
        EffectSegment segment = Run();

        effect.Render(segment, 1000);
        int atFirst = Lit(segment);

        // The dots move on, but where they were stays lit and dimming: more of the run is showing
        // something after several frames than after one.
        for (uint t = 1023; t < 1200; t += 23)
        {
            effect.Render(segment, t);
        }

        Assert.True(Lit(segment) > atFirst);
    }

    [Fact]
    public void The_trail_dies_out_when_the_flock_stops_being_drawn()
    {
        EffectSegment segment = Run();
        new ChunchunEffect().Render(segment, 1000);

        // fade_out(254) halves the distance to the secondary color each frame, so a trail is gone
        // in single-figure frames rather than lingering.
        for (int i = 0; i < 12; i++)
        {
            segment.FadeOut(254);
        }

        Assert.Equal(0, Lit(segment));
    }
}

/// <summary>
/// The simulation exists so that the photo is stepped the way the controller steps, rather than
/// the way this machine happens to repaint.
/// </summary>
public class EffectSimulationHarnessTests
{
    private static EffectSimulation Build(int frameMs = EffectSimulation.DefaultFrameMilliseconds)
    {
        var segment = new EffectSegment(308)
        {
            Speed = 172,
            Intensity = 128,
            Colors = [new RgbColor(255, 0, 0), RgbColor.Black, RgbColor.Black],
        };

        return new EffectSimulation(new ChunchunEffect(), segment, frameMs);
    }

    [Fact]
    public void Wleds_default_frame_time_is_the_firmwares_own_rounding()
    {
        // 1000/42 in integer arithmetic, exactly as the firmware computes it. 23, not 23.8.
        Assert.Equal(23, EffectSimulation.DefaultFrameMilliseconds);
    }

    [Fact]
    public void Time_is_spent_in_whole_frames_and_the_remainder_is_carried()
    {
        EffectSimulation simulation = Build();

        simulation.Advance(30);   // one frame, 7 ms owed
        Assert.Equal(1, simulation.Frames);

        simulation.Advance(20);   // 27 ms owed, so a second frame and 4 left over
        Assert.Equal(2, simulation.Frames);
    }

    [Fact]
    public void A_preview_that_was_away_a_long_time_skips_ahead_rather_than_grinding()
    {
        EffectSimulation simulation = Build();

        simulation.Advance(60_000);

        // Capped, not two and a half thousand frames of catching up.
        Assert.InRange(simulation.Frames, 1, 8);
    }

    [Fact]
    public void The_same_frames_produce_the_same_picture_however_they_are_fed_in()
    {
        EffectSimulation oneGo = Build();
        EffectSimulation dribbled = Build();

        oneGo.Advance(23 * 5);

        for (int i = 0; i < 5; i++)
        {
            dribbled.Advance(23);
        }

        Assert.Equal(oneGo.Segment.Pixels, dribbled.Segment.Pixels);
        Assert.Equal(oneGo.ElapsedMilliseconds, dribbled.ElapsedMilliseconds);
    }

    [Fact]
    public void A_slower_controller_leaves_a_longer_trail_in_distance_covered()
    {
        // The trail is a number of frames, not an amount of time, so a controller drawing half as
        // often smears it over twice as much of the run. This is the part of an effect that really
        // does depend on the hardware.
        EffectSimulation fast = Build(frameMs: 10);
        EffectSimulation slow = Build(frameMs: 40);

        // Fed in helpings small enough that neither hits the catch-up cap, which would flatten
        // the very difference being measured.
        for (int i = 0; i < 25; i++)
        {
            fast.Advance(40);
            slow.Advance(40);
        }

        Assert.Equal(25, slow.Frames);
        Assert.Equal(100, fast.Frames);
    }

    [Fact]
    public void Priming_opens_on_a_settled_picture_rather_than_a_single_frame()
    {
        EffectSimulation simulation = Build();
        Assert.Equal(0, simulation.Frames);

        simulation.Prime();

        Assert.True(simulation.Frames >= 24);
        Assert.Contains(simulation.Segment.Pixels, p => p.R + p.G + p.B > 0);
    }
}
