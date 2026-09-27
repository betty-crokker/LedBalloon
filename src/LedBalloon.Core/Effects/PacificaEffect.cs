using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Four layers of slow ocean waves on top of each other, with white crests where they line up.
/// <para>
/// The most elaborate thing in WLED's one-dimensional set, and all of it deterministic - there is no
/// randomness anywhere. The variety comes from stacking sines whose periods have no common factor:
/// four layers, each with its own color-index counter advancing at its own rate, and each of those rates
/// is itself a sine at a tempo of about one beat a minute. So nothing repeats on any timescale anyone
/// watches it over.
/// </para>
/// <para>
/// It runs on a warped clock. Every beat inside it is measured against <c>now / 4 + now * speed / 128</c>
/// rather than against the real clock, which is how one slider stretches the whole thing at once. The
/// firmware does that by overwriting the strip's clock, running, and putting it back.
/// </para>
/// <para>
/// The crests are the part that makes it look like water. Where the four layers add up past a threshold
/// - itself a slow sine, plus a fast ripple along the run - the excess is added back as white, weighted
/// one to two to four across red, green and blue. Then blue and green are pulled down a little and every
/// channel is floored, so the sea is never black: red never below 2, green below 5 or blue below 7.
/// </para>
/// </summary>
public sealed class PacificaEffect : IWledEffect
{
    public string Name => "Pacifica";

    /// <summary>Nearly black through to a pale green crest.</summary>
    private static readonly RgbColor[] Layer1 = Entries(
    [
        0x000507, 0x000409, 0x00030B, 0x00030D, 0x000210, 0x000212, 0x000114, 0x000117,
        0x000019, 0x00001C, 0x000026, 0x000031, 0x00003B, 0x000046, 0x14554B, 0x28AA50,
    ]);

    /// <summary>The same, with a slightly different crest, so two layers never quite agree.</summary>
    private static readonly RgbColor[] Layer2 = Entries(
    [
        0x000507, 0x000409, 0x00030B, 0x00030D, 0x000210, 0x000212, 0x000114, 0x000117,
        0x000019, 0x00001C, 0x000026, 0x000031, 0x00003B, 0x000046, 0x0C5F52, 0x19BE5F,
    ]);

    /// <summary>Brighter and bluer throughout, which the two deeper layers share.</summary>
    private static readonly RgbColor[] Layer3 = Entries(
    [
        0x000208, 0x00030E, 0x000514, 0x00061A, 0x000820, 0x000927, 0x000B2D, 0x000C33,
        0x000E39, 0x001040, 0x001450, 0x001860, 0x001C70, 0x002080, 0x1040BF, 0x2060FF,
    ]);

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        RgbColor[] first = Layer1, second = Layer2, deep = Layer3;

        // A chosen palette replaces all three, so the layers stop differing from each other and only
        // their speeds and scales tell them apart.
        if (segment.PaletteId != 0 && segment.Palette is not null)
        {
            first = second = deep = Sixteen(segment);
        }

        uint start1 = segment.Aux0;
        uint start2 = segment.Aux1;
        uint start3 = segment.Step & 0xFFFF;
        uint start4 = segment.Step >> 16;

        // How far the counters move in one frame. Measured in FRAMETIME rather than in real frames, and
        // then shifted down seven - so the whole slider only reaches four values, and at speed zero the
        // sea stops dead.
        uint perFrame = (uint)((segment.FrameTime >> 2) + ((segment.FrameTime * segment.Speed) >> 7));

        // The warped clock every beat below is measured against.
        var warped = (uint)((now >> 2) + (((ulong)now * segment.Speed) >> 7));

        uint speedfactor1 = FastLed.BeatSin16(3, 179, 269, warped);
        uint speedfactor2 = FastLed.BeatSin16(4, 179, 269, warped);

        uint delta1 = perFrame * speedfactor1 / 256;
        uint delta2 = perFrame * speedfactor2 / 256;
        uint delta21 = (delta1 + delta2) / 2;

        unchecked
        {
            // Two forward and two back, each at its own slowly varying rate. The sums wrap at 65536,
            // because that is how wide the fields they live in are.
            start1 += delta1 * FastLed.BeatSin88(1011, 10, 13, warped);
            start2 -= delta21 * FastLed.BeatSin88(777, 8, 11, warped);
            start3 -= delta1 * FastLed.BeatSin88(501, 5, 7, warped);
            start4 -= delta2 * FastLed.BeatSin88(257, 4, 6, warped);
        }

        segment.Aux0 = start1;
        segment.Aux1 = start2;
        segment.Step = ((start4 & 0xFFFF) << 16) | (start3 & 0xFFFF);

        uint baseThreshold = FastLed.BeatSin8(9, 55, 65, warped);
        uint wave = FastLed.Beat8(7, warped);

        for (int i = 0; i < segment.Length; i++)
        {
            // Not black: the sea has a color before any wave is added to it.
            var color = new RgbColor(2, 6, 10);

            color = Add(color, OneLayer(
                segment, i, first, segment.Aux0,
                FastLed.BeatSin16(3, 11 * 256, 14 * 256, warped),
                FastLed.BeatSin8(10, 70, 130, warped),
                (ushort)-FastLed.Beat16(301, warped)));

            color = Add(color, OneLayer(
                segment, i, second, segment.Aux1,
                FastLed.BeatSin16(4, 6 * 256, 9 * 256, warped),
                FastLed.BeatSin8(17, 40, 80, warped),
                FastLed.Beat16(401, warped)));

            // The two deep layers have a fixed scale rather than a breathing one, which is what makes
            // them read as depth rather than as more waves.
            color = Add(color, OneLayer(
                segment, i, deep, segment.Step & 0xFFFF,
                6 * 256,
                FastLed.BeatSin8(9, 10, 38, warped),
                (ushort)-FastLed.Beat16(503, warped)));

            color = Add(color, OneLayer(
                segment, i, deep, segment.Step >> 16,
                5 * 256,
                FastLed.BeatSin8(8, 10, 28, warped),
                FastLed.Beat16(601, warped)));

            // A threshold that breathes slowly and ripples quickly along the run - seven steps of the
            // sine per LED, which at 256 to a cycle is a crest every 36 LEDs.
            uint threshold = FastLed.Scale8(FastLed.Sin8((byte)wave), 20) + baseThreshold;
            wave += 7;

            byte light = EffectSegment.AverageLight(color);

            if (light > threshold)
            {
                var overage = (byte)(light - threshold);
                byte twice = FastLed.QAdd8(overage, overage);

                // One, two and four, so a crest goes white through blue rather than through yellow.
                color = Add(color, new RgbColor(overage, twice, FastLed.QAdd8(twice, twice)));
            }

            // Deepen the blues and greens, then floor every channel - which is the last thing that
            // happens, so the floor always holds.
            color = new RgbColor(color.R, FastLed.Scale8(color.G, 200), FastLed.Scale8(color.B, 145));

            segment.Pixels[i] = new RgbColor(
                Math.Max(color.R, (byte)2),
                Math.Max(color.G, (byte)5),
                Math.Max(color.B, (byte)7));
        }
    }

    /// <summary>
    /// One layer of waves: a wave in space whose wavelength is itself waved, read off the palette.
    /// </summary>
    /// <remarks>
    /// Both accumulators are truncated to sixteen bits before the sine reads them, which is where the
    /// periodicity comes from - the products below run to hundreds of thousands and it is only the
    /// bottom sixteen bits of them that matter.
    /// </remarks>
    private static RgbColor OneLayer(
        EffectSegment segment,
        int i,
        RgbColor[] palette,
        uint indexStart,
        ushort waveScale,
        byte brightness,
        ushort offset)
    {
        var halfScale = (ushort)((waveScale >> 1) + 20);

        unchecked
        {
            // Where along the wave this LED sits. Intensity is the only slider that reaches this, and it
            // sets the wavelength rather than any kind of amount.
            var waveAngle = (ushort)(offset + ((120 + segment.Intensity) * i));

            var s16 = (ushort)(FastLed.Sin16(waveAngle) + 32768);

            // The local stretch, between one and two times the half scale - so the wavelength varies
            // along the run instead of being constant.
            uint stretch = (uint)FastLed.Scale16(s16, halfScale) + halfScale;

            var ci = (ushort)(indexStart + (stretch * (uint)i));

            var sindex16 = (ushort)(FastLed.Sin16(ci) + 32768);
            var sindex8 = (byte)FastLed.Scale16(sindex16, 240);

            return FastLed.ColorFromPalette16(palette, sindex8, brightness);
        }
    }

    /// <summary>Saturating channel-wise addition, which is what adding two CRGBs does.</summary>
    private static RgbColor Add(RgbColor first, RgbColor second) => new(
        FastLed.QAdd8(first.R, second.R),
        FastLed.QAdd8(first.G, second.G),
        FastLed.QAdd8(first.B, second.B));

    /// <summary>The chosen palette as sixteen entries, which is how the firmware holds every palette.</summary>
    private static RgbColor[] Sixteen(EffectSegment segment) =>
        [.. Enumerable.Range(0, 16).Select(k =>
            segment.ColorFromPalette(k * 16, wrap: true, blend: false))];

    private static RgbColor[] Entries(int[] packed) =>
        [.. packed.Select(c => new RgbColor((byte)(c >> 16), (byte)(c >> 8), (byte)c))];
}
