using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Two waves of slightly different frequency walking along the run at slightly different rates, with
/// the palette read where they add up.
/// <para>
/// The two frequencies are deliberately close and deliberately not related - 6 and 7 beats a minute
/// for the phases, and spatial frequencies of <c>2 + 3(speed/32)</c> and <c>1 + 2(speed/32)</c>. Close
/// enough that they beat against each other over tens of seconds, unrelated enough that the pattern
/// never repeats. That is what makes it read as a fluid rather than as two waves.
/// </para>
/// <para>
/// A third wave, slower still, subtracts from the brightness rather than the color, and it subtracts
/// with a floor: the dim parts of the run go fully dark and stay there for a while instead of dipping
/// and recovering. Intensity sets how deep that can cut, and at full intensity it cannot cut at all.
/// </para>
/// <para>
/// One random bit at the start shifts both phase frequencies by one, so two runs of this side by side
/// drift apart instead of moving together.
/// </para>
/// </summary>
public sealed class PlasmaEffect : IWledEffect
{
    public string Name => "Plasma";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Call == 0)
        {
            segment.Aux0 = segment.Random8(0, 2);
        }

        // Written as -64 to 64 in the firmware, into parameters that are bytes - so the range is
        // 192 to 64, which wraps rather than spanning 128. It is used as a phase and added modulo
        // 256, so the wrap costs nothing and the effect is a swing of 128 either way.
        const byte Low = 192;
        const byte High = 64;

        byte thisPhase = FastLed.BeatSin8((byte)(6 + segment.Aux0), Low, High, now);
        byte thatPhase = FastLed.BeatSin8((byte)(7 + segment.Aux0), Low, High, now);

        // The speed slider only moves in steps of 32, so the spatial frequency changes in jumps -
        // which is why turning it up changes the texture rather than the rate.
        int fast = 2 + (3 * (segment.Speed >> 5));
        int slow = 1 + (2 * (segment.Speed >> 5));

        for (int i = 0; i < segment.Length; i++)
        {
            // Half of each wave, so the two together span a byte.
            int colorIndex = (FastLed.CubicWave8((byte)((i * fast) + thisPhase)) / 2)
                + (FastLed.Cos8((byte)((i * slow) + thatPhase)) / 2);

            byte bright = FastLed.QSub8(
                (byte)colorIndex,
                FastLed.BeatSin8(7, 0, (byte)(128 - (segment.Intensity >> 1)), now));

            segment.Pixels[i] = segment.ColorFromPalette(
                colorIndex, wrap: segment.SolidWrap, brightness: bright);
        }
    }
}

/// <summary>
/// A sun rising from both ends of the run at once, over anything from a minute to an hour.
/// <para>
/// The only effect here whose speed slider is a duration in minutes rather than a rate: 1 to 60 is a
/// sunrise of that many minutes, 61 to 120 is a sunset of that many minus sixty, and above 120 it
/// stops being a clock and becomes a fast breathing rise and fall. Which means this is also the only
/// effect that has to notice the slider moving - it restarts its clock when the speed changes, because
/// a sunrise half done at one duration is not half done at another.
/// </para>
/// <para>
/// The picture is a mirror. Every LED is drawn twice, once from each end, so the sun comes up at both
/// ends and meets in the middle - which on a roofline is what you want and on a single strip is a
/// deliberate symmetry rather than an accident.
/// </para>
/// <para>
/// It reads its gradient through color slot 255, which is out of range on purpose: that is how an
/// effect says "use the palette even on palette Default", where a real slot number would give a flat
/// color instead. Glitter and Meteor Smooth do the same thing.
/// </para>
/// </summary>
public sealed class SunriseEffect : IWledEffect
{
    public string Name => "Sunrise";

    /// <summary>Where the slider stops being a duration and becomes a rate.</summary>
    private const int Breathing = 120;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        // Restart whenever the duration changes, since the progress so far means something different
        // against a new duration.
        if (segment.Call == 0 || segment.Speed != segment.Aux0)
        {
            segment.Step = now;
            segment.Aux0 = segment.Speed;
        }

        segment.Fill(RgbColor.Black);

        // Fully risen, which is what a speed of zero leaves it at.
        uint stage = 0xFFFF;

        uint tenths = (now - segment.Step) / 100;

        if (segment.Speed > Breathing)
        {
            uint counter = (now >> 1) * (uint)(((segment.Speed - Breathing) >> 1) + 1);
            stage = FastLed.TriWave16((ushort)counter);
        }
        else if (segment.Speed != 0)
        {
            int minutes = segment.Speed;

            if (minutes > 60)
            {
                minutes -= 60;
            }

            uint target = (uint)minutes * 600u;

            if (tenths > target)
            {
                tenths = target;
            }

            stage = (uint)FastLed.Map((int)tenths, 0, (int)target, 0, 0xFFFF);

            // Past sixty the same arithmetic runs backwards, which is a sunset.
            if (segment.Speed > 60)
            {
                stage = 0xFFFF - stage;
            }
        }

        for (int i = 0; i <= segment.Length / 2; i++)
        {
            uint wave = FastLed.TriWave16((ushort)(i * stage / (uint)segment.Length));

            // The top eight bits, plus a share of the whole sixteen that intensity sets - so
            // intensity widens the bright part rather than brightening it.
            wave = (wave >> 8) + ((wave * segment.Intensity) >> 15);

            RgbColor color = segment.ColorFromPalette(
                (int)Math.Min(wave, 240u), wrap: true, colorSlot: 255);

            segment.SetPixel(i, color);
            segment.SetPixel(segment.Length - i - 1, color);
        }
    }
}
