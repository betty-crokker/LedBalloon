using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// One thrown or falling drop: where it is, how fast it is going, and how bright it is.
/// <para>
/// WLED's <c>spark</c> struct, which Popcorn and Drip share and use quite differently - Popcorn reads
/// the color field as a palette index and leaves the brightness alone, Drip reads it as a brightness
/// and a state machine and never touches the palette. Kept as one type here because it is one type
/// there, and because that is why both effects have to be careful about what the last one left
/// behind.
/// </para>
/// </summary>
internal sealed class Spark
{
    /// <summary>Position along the run, in LEDs, and fractional. Negative means idle.</summary>
    public float Position;

    /// <summary>
    /// The second axis, which on a one-dimensional run is not a position at all: Fireworks 1D stores
    /// which end it is firing from in here, as nought or one.
    /// </summary>
    public float PositionX;

    /// <summary>LEDs per frame, which gravity walks down every frame.</summary>
    public float Velocity;

    /// <summary>Speed along the second axis, which stays nought on a one-dimensional run.</summary>
    public float VelocityX;

    /// <summary>Brightness for Drip, unused by Popcorn.</summary>
    public int Brightness;

    /// <summary>A color slot or palette index for Popcorn; a state number for Drip.</summary>
    public byte ColorIndex;
}

/// <summary>
/// How much scratch memory an effect may use, which depends on how many segments the controller has.
/// <para>
/// A segment's fair share doubles if the controller is using half its segments or fewer, and doubles
/// again at a quarter. The two fireworks effects are the only ones that ask, and they divide the answer
/// by the size of one particle to decide how many they can have - so on a controller carved into many
/// segments the bursts are genuinely smaller.
/// </para>
/// </summary>
internal static class SegmentData
{
    /// <summary>A segment's share on an ESP32.</summary>
    private const int FairShare = 640;

    /// <summary>How many segments an ESP32 build allows.</summary>
    private const int MaxSegments = 32;

    public static int Budget(int activeSegments)
    {
        int data = FairShare;

        if (activeSegments <= MaxSegments / 2)
        {
            data *= 2;
        }

        if (activeSegments <= MaxSegments / 4)
        {
            data *= 2;
        }

        return data;
    }
}

/// <summary>
/// Kernels popping off the bottom of the run, flying up and falling back.
/// <para>
/// Real ballistics, not a waveform: each kernel gets a launch speed worked out from the height it is
/// meant to reach - <c>sqrt(-2 g h)</c> - and then has gravity added to its speed once per frame. A
/// kernel is idle while its position is negative, and each idle kernel has a one in 128 chance per
/// frame of popping, so they go off in an uneven scatter rather than in turn.
/// </para>
/// <para>
/// Gravity is scaled by the length of the run, so the arc takes the same time on a long run as on a
/// short one. Speed sets gravity and therefore how brisk the popping looks; intensity is how many
/// kernels there can be, up to 21.
/// </para>
/// <para>
/// On palette Default a kernel takes one of the three color slots at random - but only if the third
/// slot is set, otherwise they are all the primary. On any other palette it takes a random point on
/// the palette instead.
/// </para>
/// </summary>
public sealed class PopcornEffect : IWledEffect
{
    public string Name => "Popcorn";

    /// <summary>As many as fit in the data a segment is allowed on an ESP8266 with 16 segments.</summary>
    private const int MostKernels = 21;

    /// <summary>How many color slots there are, which is what a kernel picks among.</summary>
    private const int ColorSlots = 3;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        Spark[] kernels = segment.Scratch(
            () => Enumerable.Range(0, MostKernels).Select(_ => new Spark()).ToArray());

        // With a third color set the kernels are drawn on black; without one they are drawn on the
        // secondary. Overlay leaves whatever was there.
        bool hasThird = segment.Colors[2] != RgbColor.Black;

        if (!segment.Option2)
        {
            segment.Fill(hasThird ? RgbColor.Black : segment.Colors[1]);
        }

        // Per frame, and scaled by the run so the arc lasts the same time whatever the length.
        float gravity = (-0.0001f - (segment.Speed / 200000f)) * segment.Length;

        int count = segment.Intensity * MostKernels / 255;

        if (count == 0)
        {
            count = 1;
        }

        for (int i = 0; i < count; i++)
        {
            Spark kernel = kernels[i];

            if (kernel.Position >= 0f)
            {
                kernel.Position += kernel.Velocity;
                kernel.Velocity += gravity;
            }
            else if (segment.Random8() < 2)
            {
                // POP. Start just off the bottom so the position stays positive, and pick a launch
                // speed from the height it should reach rather than choosing a speed directly - so
                // the kernels all rise to somewhere in the top half and none of them overshoots.
                kernel.Position = 0.01f;

                int peakHeight = 128 + segment.Random8(128);
                peakHeight = peakHeight * (segment.Length - 1) >> 8;

                kernel.Velocity = MathF.Sqrt(-2f * gravity * peakHeight);

                if (segment.PaletteId != 0)
                {
                    kernel.ColorIndex = segment.Random8();
                }
                else
                {
                    var slot = (byte)segment.Random8(0, ColorSlots);

                    // Only mix the slots when all three are set: with the third left black a run of
                    // black kernels would just be a run of missing ones.
                    if (!hasThird || segment.Colors[slot] == RgbColor.Black)
                    {
                        slot = 0;
                    }

                    kernel.ColorIndex = slot;
                }
            }

            // Either it was already flying or it just popped, and the test is repeated rather than
            // combined because popping sets the position the branch above tested.
            if (kernel.Position >= 0f)
            {
                RgbColor color = segment.PaletteId == 0 && kernel.ColorIndex < ColorSlots
                    ? segment.Colors[kernel.ColorIndex]
                    : segment.ColorWheel(kernel.ColorIndex);

                var at = (int)kernel.Position;

                if (at < segment.Length)
                {
                    segment.Pixels[at] = color;
                }
            }
        }
    }
}

/// <summary>
/// Water gathering at the top of the run, growing until it lets go, falling, and bouncing once.
/// <para>
/// A state machine rather than a waveform: forming, falling, bouncing, and back to forming. A drop
/// swells in place at the top a few units of brightness per frame, and each frame it has a chance of
/// releasing proportional to how big it has grown - so a drop that has been forming a while is far
/// more likely to go than one that has just started, which is what makes the timing look natural
/// rather than metronomic.
/// </para>
/// <para>
/// Falling, it trails four dimming pixels behind it, each scaled by its distance - a streak drawn by
/// dividing rather than fading, so the trail shortens by itself as the drop slows. Bouncing, it trails
/// one, because the same arithmetic gives <c>7 - state</c> pixels and the bounce state is five: the
/// splash is narrower than the fall by construction.
/// </para>
/// <para>
/// Speed is gravity here, not rate, and it also sets how fast a drop swells - so turning it up makes
/// the drops both smaller and quicker. Intensity is how many drops, up to four.
/// </para>
/// </summary>
public sealed class DripEffect : IWledEffect
{
    public string Name => "Drip";

    private const int MostDrops = 4;

    /// <summary>The brightness the tap sits at, and what a new drop starts from.</summary>
    private const int SourceDrop = 12;

    /// <summary>Forming at the tap, swelling in place.</summary>
    private const byte Forming = 1;

    /// <summary>Let go, falling and trailing.</summary>
    private const byte Falling = 2;

    /// <summary>Come off the floor again, with water spreading at the bottom.</summary>
    private const byte Bouncing = 5;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        Spark[] drops = segment.Scratch(
            () => Enumerable.Range(0, MostDrops).Select(_ => new Spark()).ToArray());

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        int count = 1 + (segment.Intensity >> 6);

        float gravity = (-0.0005f - (segment.Speed / 50000f)) * Math.Max(1, segment.Length - 1);

        for (int j = 0; j < count; j++)
        {
            Spark drop = drops[j];

            // State zero is how a drop says it has never run, or has finished a bounce and wants to
            // start over - the two are the same thing here.
            if (drop.ColorIndex == 0)
            {
                drop.Position = segment.Length - 1;
                drop.Velocity = 0;
                drop.Brightness = SourceDrop;
                drop.ColorIndex = Forming;
            }

            // The tap itself, redrawn every frame at the brightness a drop starts from, so there is
            // always something at the top even between drops.
            segment.SetPixel(
                segment.Length - 1,
                EffectSegment.Blend(RgbColor.Black, segment.Colors[0], SourceDrop));

            if (drop.ColorIndex == Forming)
            {
                if (drop.Brightness > 255)
                {
                    drop.Brightness = 255;
                }

                segment.SetPixel(
                    (int)(ushort)drop.Position,
                    EffectSegment.Blend(RgbColor.Black, segment.Colors[0], (byte)drop.Brightness));

                drop.Brightness += FastLed.Map(segment.Speed, 0, 255, 1, 6);

                // The bigger it has grown the likelier it is to go, so the wait is not a fixed one.
                if (segment.Random8() < drop.Brightness / 10)
                {
                    drop.ColorIndex = Falling;
                    drop.Brightness = 255;
                }
            }

            if (drop.ColorIndex <= Forming)
            {
                continue;
            }

            if (drop.Position > 0)
            {
                drop.Position += drop.Velocity;

                if (drop.Position < 0)
                {
                    drop.Position = 0;
                }

                drop.Velocity += gravity;

                // The trail: four pixels while falling, one while bouncing, each divided down rather
                // than faded - so it is the arithmetic that makes the splash narrower than the fall.
                for (int i = 1; i < 7 - drop.ColorIndex; i++)
                {
                    int at = Math.Clamp((int)(ushort)drop.Position + i, 0, segment.Length - 1);

                    segment.Pixels[at] = EffectSegment.Blend(
                        RgbColor.Black, segment.Colors[0], (byte)(drop.Brightness / i));
                }

                // Water on the floor during the bounce, and blended the other way round - from the
                // color toward black - so it stays bright while the drop above it is dim.
                if (drop.ColorIndex > Falling)
                {
                    segment.SetPixel(
                        0,
                        EffectSegment.Blend(segment.Colors[0], RgbColor.Black, (byte)drop.Brightness));
                }
            }
            else if (drop.ColorIndex > Falling)
            {
                // Down for the second time, so start over at the tap.
                drop.ColorIndex = 0;
                drop.Brightness = SourceDrop;
            }
            else
            {
                if (drop.ColorIndex == Falling)
                {
                    // A quarter of the speed back up, which is the whole bounce.
                    drop.Velocity = -drop.Velocity / 4;
                    drop.Position += drop.Velocity;
                }

                drop.Brightness = SourceDrop * 2;
                drop.ColorIndex = Bouncing;
            }
        }
    }
}
