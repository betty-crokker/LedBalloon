using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>What one fairy light is doing: how long it has been doing it, and for how long.</summary>
/// <remarks>
/// Four bytes per light in the firmware, which is why Fairytwinkle carries a warning about its memory
/// and Fairy keeps only one of these per flasher rather than one per LED.
/// </remarks>
internal struct Flasher
{
    /// <summary>When the current state began, on a clock that wraps every 65.5 seconds.</summary>
    public ushort StateStart;

    /// <summary>How long it lasts - tens of milliseconds for Fairy, hundreds for Fairytwinkle.</summary>
    public byte StateDur;

    /// <summary>Whether it is on its way up or on its way down.</summary>
    public bool StateOn;
}

/// <summary>
/// The pattern the two fairy-light effects share.
/// <para>
/// A third generator, and not the one <see cref="SeededSequence"/> ports: the multiplier is the same
/// 2053 but the addend is 1384 rather than 13849, and the draw is the top byte of the state rather
/// than the sum of its halves. It is written out inline in both effects in the firmware rather than
/// called, which is how the difference survived - so it is worth keeping the two apart here too.
/// </para>
/// </summary>
internal static class Flashers
{
    /// <summary>
    /// Where the pattern starts, which depends on which segment this is so that a strip cut into
    /// several does not show the same twinkle on each.
    /// </summary>
    public static ushort Seed(EffectSegment segment) => (ushort)(5100 + segment.SegmentId);

    /// <summary>The next value in the sequence.</summary>
    public static ushort Next(ushort state) => unchecked((ushort)((state * 2053) + 1384));
}

/// <summary>
/// A string of fairy lights: every LED a fixed color from the palette, with a few of them flashing
/// slowly in and out, and the rest dimming very slightly while they do.
/// <para>
/// The dimming is the whole idea. Real fairy lights share a supply, so when several flashers are on
/// the others lose a little voltage and the string sags. That is modelled per zone of six flashers
/// rather than across the whole run, so a burst at one end dims its neighbors and leaves the far end
/// alone.
/// </para>
/// <para>
/// Every LED's base color comes from walking a sequence seeded on the segment, so the colors are
/// fixed without being stored. At intensity zero there are no flashers at all and the effect becomes
/// exactly that: one still picture, the same one every frame.
/// </para>
/// </summary>
public sealed class FairyEffect : IWledEffect
{
    public string Name => "Fairy";

    /// <summary>Six flashers share a supply, and a seventh starts a new zone.</summary>
    private const int FlashersPerZone = 6;

    /// <summary>How far the run can sag - 92 of 255, so never below about 72%.</summary>
    private const int MaxShimmer = 92;

    private sealed class State
    {
        public Flasher[] Flashers = [];
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        ushort prng = Flashers.Seed(segment);

        for (int i = 0; i < segment.Length; i++)
        {
            prng = Flashers.Next(prng);
            segment.Pixels[i] = segment.ColorFromPalette(prng >> 8);
        }

        // No flashers at all, so the still picture above is the whole effect.
        if (segment.Intensity == 0)
        {
            return;
        }

        int flasherDistance = ((255 - segment.Intensity) / 28) + 1;
        int numFlashers = (segment.Length / flasherDistance) + 1;

        State state = segment.Scratch(() => new State());

        // Moving the intensity slider changes how many flashers there are, and the firmware starts
        // them over rather than trying to carry the old ones across.
        if (state.Flashers.Length != numFlashers)
        {
            state.Flashers = new Flasher[numFlashers];
        }

        Flasher[] flashers = state.Flashers;
        uint now16 = now & 0xFFFF;

        int zones = numFlashers / FlashersPerZone;

        if (zones == 0)
        {
            zones = 1;
        }

        int flashersInZone = numFlashers / zones;

        // Up to eleven in a zone: dividing by six leaves a remainder that the last zone absorbs.
        var flasherBri = new byte[(FlashersPerZone * 2) - 1];

        for (int z = 0; z < zones; z++)
        {
            int flasherBriSum = 0;

            // Read before the last zone widens, or every zone after the first starts in the wrong
            // place.
            int firstFlasher = z * flashersInZone;

            if (z == zones - 1)
            {
                flashersInZone = numFlashers - (flashersInZone * (zones - 1));
            }

            for (int f = firstFlasher; f < firstFlasher + flashersInZone; f++)
            {
                int stateTime = (ushort)(now16 - flashers[f].StateStart);

                if (stateTime > flashers[f].StateDur * 10)
                {
                    flashers[f].StateOn = !flashers[f].StateOn;

                    // On for a quarter second to a second and a bit, off for slightly longer, both
                    // shortening as the speed goes up.
                    flashers[f].StateDur = flashers[f].StateOn
                        ? (byte)(12 + segment.Random8(12 + ((255 - segment.Speed) >> 2)))
                        : (byte)(20 + segment.Random8(6 + ((255 - segment.Speed) >> 2)));

                    flashers[f].StateStart = (ushort)now16;

                    // A light that has only just switched would jump: it is a fraction of the way
                    // into its fade already, and starting the new state from nothing throws that
                    // away. So the new state is backdated to where the brightness has got to.
                    if (stateTime < 255)
                    {
                        flashers[f].StateStart =
                            unchecked((ushort)(flashers[f].StateStart - (255 - stateTime)));

                        flashers[f].StateDur =
                            unchecked((byte)(flashers[f].StateDur + 26 - (stateTime / 10)));

                        stateTime = 255 - stateTime;
                    }
                    else
                    {
                        stateTime = 0;
                    }
                }

                // The fade takes the first quarter second of a state; after that it holds.
                if (stateTime > 255)
                {
                    stateTime = 255;
                }

                flasherBri[f - firstFlasher] =
                    (byte)(flashers[f].StateOn ? stateTime : 255 - stateTime);

                flasherBriSum += flasherBri[f - firstFlasher];
            }

            int avgFlasherBri = flasherBriSum / flashersInZone;
            var globalPeakBri = (byte)(255 - ((avgFlasherBri * MaxShimmer) >> 8));

            for (int f = firstFlasher; f < firstFlasher + flashersInZone; f++)
            {
                var bri = (byte)(flasherBri[f - firstFlasher] * globalPeakBri / 255);

                prng = Flashers.Next(prng);
                int flasherPos = f * flasherDistance;

                // A flasher blends up from the background rather than dimming its own color, so it
                // goes out to the background instead of to black.
                segment.SetPixel(
                    flasherPos,
                    EffectSegment.Blend(segment.Colors[1], segment.ColorFromPalette(prng >> 8), bri));

                // And the LEDs between this flasher and the next are redrawn dimmed, which is the
                // sag. The sequence carries on from the still picture above rather than restarting,
                // so these are not the colors those LEDs had a moment ago - one flasher more or less
                // shifts the colors of everything after it.
                for (int i = flasherPos + 1; i < flasherPos + flasherDistance && i < segment.Length; i++)
                {
                    prng = Flashers.Next(prng);
                    segment.Pixels[i] = segment.ColorFromPalette(prng >> 8, brightness: globalPeakBri);
                }
            }
        }
    }
}

/// <summary>
/// Every LED breathing in and out on its own slow clock, on a palette color it keeps.
/// <para>
/// Colortwinkles by another route. Rather than reading the strip back to find what is already fading,
/// each LED carries its own timer - so the run starts fully lit and settles into a twinkle instead of
/// building up out of the dark, and nothing depends on what the last frame left behind.
/// </para>
/// <para>
/// The rise and fall go through the gamma table, which is what makes it look like filaments rather
/// than a fader. And the colors come from a sequence that keeps drawing until two neighbors differ by
/// at least a quarter of its range, so no two adjacent LEDs come out nearly the same - a loop that
/// may turn several times for one LED, which puts every LED's color at the mercy of all the draws
/// before it.
/// </para>
/// </summary>
public sealed class FairyTwinkleEffect : IWledEffect
{
    public string Name => "Fairytwinkle";

    private sealed class State
    {
        public Flasher[] Flashers = [];
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        State state = segment.Scratch(() => new State());

        if (state.Flashers.Length != segment.Length)
        {
            state.Flashers = new Flasher[segment.Length];
        }

        Flasher[] flashers = state.Flashers;
        uint now16 = now & 0xFFFF;
        ushort prng = Flashers.Seed(segment);

        int riseFallTime = 400 + ((255 - segment.Speed) * 3);

        // A ceiling on how long a lit LED may stay lit, applied every frame rather than only when the
        // state changes - so dragging the intensity slider down takes effect at once instead of after
        // however long the twinkles already alight had committed to.
        int maxDur = (riseFallTime / 100)
            + ((255 - segment.Intensity) >> 2) + 13 + ((255 - segment.Intensity) >> 1);

        for (int f = 0; f < segment.Length; f++)
        {
            int stateTime = (ushort)(now16 - flashers[f].StateStart);

            if (stateTime > flashers[f].StateDur * 100)
            {
                flashers[f].StateOn = !flashers[f].StateOn;

                // A duration of zero can only mean this LED has never run, since every branch below
                // adds at least one - so it doubles as the "first frame" flag.
                bool init = flashers[f].StateDur == 0;

                flashers[f].StateDur = flashers[f].StateOn
                    ? (byte)((riseFallTime / 100) + ((255 - segment.Intensity) >> 2)
                        + segment.Random8(12 + ((255 - segment.Intensity) >> 1)) + 1)
                    : (byte)((riseFallTime / 100)
                        + segment.Random8(3 + ((255 - segment.Speed) >> 6)) + 1);

                flashers[f].StateStart = (ushort)now16;
                stateTime = 0;

                if (init)
                {
                    // Backdate the start by a whole rise so this LED begins at full brightness: the
                    // run comes up lit and fades into a twinkle rather than climbing out of the dark.
                    flashers[f].StateStart =
                        unchecked((ushort)(flashers[f].StateStart - riseFallTime));

                    flashers[f].StateDur = (byte)((riseFallTime / 100)
                        + segment.Random8(12 + ((255 - segment.Intensity) >> 1)) + 5);

                    stateTime = riseFallTime;
                }
            }

            if (flashers[f].StateOn && flashers[f].StateDur > maxDur)
            {
                flashers[f].StateDur = (byte)maxDur;
            }

            if (stateTime > riseFallTime)
            {
                stateTime = riseFallTime;
            }

            int fadeprog = 255 - (stateTime * 255 / riseFallTime);

            var bri = (byte)(flashers[f].StateOn
                ? 255 - Gamma.Correct((byte)fadeprog)
                : Gamma.Correct((byte)fadeprog));

            // Keep drawing until this LED's color is far enough from its neighbor's. The comparison
            // is against the value the previous LED settled on, so a run of near-identical draws
            // costs several turns of the sequence and shifts everything after it.
            int lastR = prng;
            int diff = 0;

            while (diff < 0x4000)
            {
                prng = Flashers.Next(prng);
                diff = prng > lastR ? prng - lastR : lastR - prng;
            }

            segment.Pixels[f] = EffectSegment.Blend(
                segment.Colors[1], segment.ColorFromPalette(prng >> 8), bri);
        }
    }
}
