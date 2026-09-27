using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The two fireworks, captured on the south controller's 285 LED roofline on palette Rainbow with a
/// black background.
/// <para>
/// Both are particle systems with nothing repeatable in them, so what is measured is the gross
/// behaviour: how much of the run is lit, how bright it is, how long the lit stretches are, and - for
/// Fireworks 1D - the one asymmetry in its arithmetic that shows up in the channel balance of the whole
/// capture.
/// </para>
/// </summary>
public class FireworkEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static WledPalette Rainbow() => new()
    {
        Stops =
        [
            new PaletteStop(0, new RgbColor(255, 0, 0)), new PaletteStop(16, new RgbColor(213, 42, 0)),
            new PaletteStop(32, new RgbColor(171, 85, 0)), new PaletteStop(48, new RgbColor(171, 127, 0)),
            new PaletteStop(64, new RgbColor(171, 171, 0)), new PaletteStop(80, new RgbColor(86, 213, 0)),
            new PaletteStop(96, new RgbColor(0, 255, 0)), new PaletteStop(112, new RgbColor(0, 213, 42)),
            new PaletteStop(128, new RgbColor(0, 171, 85)), new PaletteStop(144, new RgbColor(0, 86, 170)),
            new PaletteStop(160, new RgbColor(0, 0, 255)), new PaletteStop(176, new RgbColor(42, 0, 213)),
            new PaletteStop(192, new RgbColor(85, 0, 171)), new PaletteStop(208, new RgbColor(127, 0, 129)),
            new PaletteStop(224, new RgbColor(171, 0, 85)), new PaletteStop(240, new RgbColor(213, 0, 43)),
        ],
    };

    private static EffectSegment Run(byte intensity = 128) => new(Strip.Roofline)
    {
        SegmentId = 2,

        // Three segments on this controller, which is what decides the spark budget - though anything up
        // to eight gives the same answer.
        ActiveSegments = 3,
        Speed = 128,
        Intensity = intensity,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 11,
        Palette = Rainbow(),
        Colors = [new RgbColor(255, 0, 0), RgbColor.Black, new RgbColor(0, 255, 0)],
    };

    /// <summary>
    /// Fireworks 1D takes blue off its sparks twice as fast as green, and the whole capture shows it.
    /// <para>
    /// A cooling spark has <c>cooling</c> subtracted from green and twice that from blue, with red left
    /// alone - which turns a dying spark orange and then red rather than dimming it evenly. Summed over
    /// every lit pixel of a twenty second capture on a palette that is even-handed about color, the strip
    /// gives 40.0% red, 32.0% green and 28.0% blue. An even-handed effect on this palette would give a
    /// third each, and the order is the order the arithmetic puts them in.
    /// </para>
    /// <para>
    /// The flare is a lone white pixel: about 1.5 near-white pixels per frame, which is one flare climbing
    /// plus the occasional freshly lit spark still above 300 on its own scale. And 24 of 327 frames had
    /// nothing lit at all, which is the wait between one burst and the next launch.
    /// </para>
    /// </summary>
    [Fact]
    public void Fireworks_1D_cools_blue_out_of_its_sparks_fastest()
    {
        Burst burst = Measure(new Fireworks1DEffect(), Run(), 20_000);

        output.WriteLine($"simulated: {burst.Lit:F2} lit per frame (most {burst.MostLit}), " +
            $"brightness {burst.Brightness:F2}, {burst.NearWhite:F2} near-white, " +
            $"{burst.DarkFrames} frames dark, runs {burst.RunLength:F2} long");
        output.WriteLine($"channel share: red {burst.Red:F3}, green {burst.Green:F3}, blue {burst.Blue:F3}");
        output.WriteLine("measured : 107.63 lit per frame (most 280), brightness 22.59, 1.54 near-white, " +
            "24 frames dark, runs 11.67 long");
        output.WriteLine("measured channel share: red 0.400, green 0.320, blue 0.280");

        // Red first, green second, blue last, because that is the order the cooling takes them off.
        Assert.True(burst.Red > burst.Green, $"red should lead: {burst.Red:F3} against {burst.Green:F3}");
        Assert.True(burst.Green > burst.Blue, $"and green should beat blue: {burst.Green:F3} against {burst.Blue:F3}");

        // And by roughly the margins the strip shows, not just in order.
        Assert.InRange(burst.Red, 0.36, 0.45);
        Assert.InRange(burst.Blue, 0.23, 0.31);

        Assert.InRange(burst.Lit, 75, 145);
        Assert.InRange(burst.Brightness, 14, 33);

        // A white dot rather than a white patch.
        Assert.InRange(burst.NearWhite, 0.4, 4.0);

        // There really is a pause between fireworks.
        Assert.True(burst.DarkFrames > 5, $"there should be dark frames between bursts: {burst.DarkFrames}");
    }

    /// <summary>
    /// Fireworks 1D fires from whichever end the intensity slider picks, and it is a coin rather than a
    /// switch.
    /// <para>
    /// Not measured against the strip - it would be two more captures to read one bit - but worth pinning,
    /// because the slider is labelled "Firing side" and does something no other slider here does: the
    /// firing end is decided by <c>intensity &gt; random8()</c>, so the middle is a fair coin and a run of
    /// this effect there alternates ends.
    /// </para>
    /// <para>
    /// The two extremes are not symmetrical, which is the part worth having a test for. At zero the
    /// comparison can never be true, so it fires from the near end every single time; at 255 it is false
    /// once in 256, because 255 is not greater than 255 - so even at the top of the slider about one
    /// firework in 256 comes from the wrong end. Twenty-one launches were enough to catch one.
    /// </para>
    /// </summary>
    [Fact]
    public void Fireworks_1D_fires_from_the_end_the_intensity_slider_weights()
    {
        foreach ((byte intensity, double least, double most) in
            new[] { ((byte)0, 0.0, 0.0), ((byte)255, 0.85, 1.0) })
        {
            EffectSegment segment = Run(intensity);
            var effect = new Fireworks1DEffect();

            int fromFarEnd = 0, launches = 0;
            bool climbing = false;

            for (uint t = 0; t < 30_000; t += (uint)Strip.FrameMs)
            {
                segment.Draw(effect, t);

                // A flare is the lone bright white pixel, and it only exists while one is climbing.
                int[] white = [.. Enumerable.Range(0, segment.Length)
                    .Where(i => segment.Pixels[i].R > 100
                        && segment.Pixels[i].R == segment.Pixels[i].G
                        && segment.Pixels[i].G == segment.Pixels[i].B)];

                if (white.Length == 1 && !climbing)
                {
                    launches++;
                    climbing = true;

                    if (white[0] > segment.Length / 2)
                    {
                        fromFarEnd++;
                    }
                }
                else if (white.Length == 0)
                {
                    climbing = false;
                }
            }

            output.WriteLine($"intensity {intensity}: {launches} launches, " +
                $"{(launches > 0 ? (double)fromFarEnd / launches : 0):F2} from the far end");

            Assert.True(launches > 3, $"30 s should cover several launches: {launches}");
            Assert.InRange((double)fromFarEnd / launches, least, most);
        }
    }

    /// <summary>
    /// Fireworks Starburst keeps most of the run lit and fairly bright, in short symmetrical fragments.
    /// <para>
    /// Thirty-six stars, each idle for about half a second and then alight for one and three quarters, so
    /// about three quarters of them are going at any moment - which is why this one never goes dark where
    /// Fireworks 1D pauses between bursts. The strip had something lit in every one of 315 frames, 155 of
    /// 285 LEDs lit on average, and a mean brightness of 76 against Fireworks 1D's 23.
    /// </para>
    /// <para>
    /// The lit stretches are short - 3.9 LEDs - because a fragment is two LEDs wide when new and one by
    /// the end, and the longer runs are fragments of the same star overlapping near its core, where the
    /// first two do not move at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Starburst_keeps_most_of_the_run_alight_in_short_fragments()
    {
        Burst burst = Measure(new StarburstEffect(), Run(), 20_000);

        output.WriteLine($"simulated: {burst.Lit:F2} lit per frame (most {burst.MostLit}), " +
            $"brightness {burst.Brightness:F2}, {burst.NearWhite:F2} near-white, " +
            $"{burst.DarkFrames} frames dark, runs {burst.RunLength:F2} long");
        output.WriteLine("measured : 155.26 lit per frame (most 224), brightness 76.38, 1.01 near-white, " +
            "0 frames dark, runs 3.93 long");
        output.WriteLine("arithmetic: 1 + (285 >> 3) = 36 stars, idle about 0.58 s and alight 1.75 s");

        // Never dark, which is the plainest difference from the other firework.
        Assert.Equal(0, burst.DarkFrames);

        Assert.InRange(burst.Lit, 115, 195);
        Assert.InRange(burst.Brightness, 55, 100);

        // Short fragments, not long trails.
        Assert.InRange(burst.RunLength, 2.8, 5.5);
    }

    /// <summary>
    /// Every one of Starburst's bursts is symmetrical, because only half of it is ever worked out.
    /// <para>
    /// Each fragment is drawn twice, once where it is and once reflected through the star's own position,
    /// so a burst cannot be lopsided. Checked on the first frame of the very first burst, when one star
    /// is alight and nothing else is on the run - which is the only moment thirty-six overlapping stars
    /// leave a symmetry visible from outside. Simulation only, since picking that moment out of a capture
    /// would mean catching it in the act.
    /// </para>
    /// </summary>
    [Fact]
    public void Starburst_s_first_burst_is_symmetrical_about_its_star()
    {
        EffectSegment segment = Run();
        var effect = new StarburstEffect();
        int[] lit = [];

        for (uint t = 0; t < 30_000; t += (uint)Strip.FrameMs)
        {
            segment.Draw(effect, t);

            lit = [.. Enumerable.Range(0, segment.Length)
                .Where(i => Brightness(segment.Pixels[i]) > 0)];

            if (lit.Length > 0)
            {
                break;
            }
        }

        Assert.NotEmpty(lit);

        // Reflected about the midpoint of what is lit, which for one star is the star itself.
        int midpoint = lit[0] + lit[^1];
        int[] mirrored = [.. lit.Select(i => midpoint - i).Order()];

        output.WriteLine($"first burst lit {lit.Length} LEDs from {lit[0]} to {lit[^1]}");
        output.WriteLine($"lit     : {string.Join(" ", lit)}");
        output.WriteLine($"mirrored: {string.Join(" ", mirrored)}");

        Assert.Equal<IEnumerable<int>>(lit, mirrored);
    }

    /// <summary>What a capture of a particle system looks like, reduced.</summary>
    private readonly record struct Burst(
        double Lit,
        int MostLit,
        double Brightness,
        double NearWhite,
        int DarkFrames,
        double RunLength,
        double Red,
        double Green,
        double Blue);

    private static Burst Measure(IWledEffect effect, EffectSegment segment, uint milliseconds)
    {
        List<double> brightness = [], lit = [], nearWhite = [];
        List<int> runs = [];
        int mostLit = 0, darkFrames = 0;
        long red = 0, green = 0, blue = 0;

        Strip.Sample(effect, segment, milliseconds, (frame, _) =>
        {
            brightness.Add(frame.Average(p => (double)Brightness(p)));

            int[] on = [.. Enumerable.Range(0, frame.Length).Where(i => Brightness(frame[i]) > 0)];

            lit.Add(on.Length);
            mostLit = Math.Max(mostLit, on.Length);

            if (on.Length == 0)
            {
                darkFrames++;
            }

            // Bright and near enough equal on all three channels, which is what a flare or a
            // freshly lit spark looks like and nothing else here does.
            nearWhite.Add(frame.Count(p =>
                Math.Min(p.R, Math.Min(p.G, p.B)) > 40 && Brightness(p) - Math.Min(p.R, Math.Min(p.G, p.B)) < 12));

            foreach (int i in on)
            {
                red += frame[i].R;
                green += frame[i].G;
                blue += frame[i].B;
            }

            int run = 0;

            for (int i = 0; i < on.Length; i++)
            {
                run++;

                if (i == on.Length - 1 || on[i + 1] != on[i] + 1)
                {
                    runs.Add(run);
                    run = 0;
                }
            }
        });

        double total = red + green + blue;

        return new Burst(
            lit.Average(),
            mostLit,
            brightness.Average(),
            nearWhite.Average(),
            darkFrames,
            runs.Count > 0 ? runs.Average() : 0,
            red / total,
            green / total,
            blue / total);
    }

    private static byte Brightness(RgbColor color) =>
        Math.Max(color.R, Math.Max(color.G, color.B));
}
