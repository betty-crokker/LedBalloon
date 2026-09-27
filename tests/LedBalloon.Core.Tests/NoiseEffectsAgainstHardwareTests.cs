using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Fire 2012 and Phased Noise, captured on the south controller's 285 LED roofline.
/// <para>
/// Fire 2012 was captured on palette 11, "Rainbow", rather than on the color slots - and not for
/// convenience. It looks the palette up without blending, so every pixel lands exactly on one of the
/// sixteen entries, and a rainbow makes those sixteen tell each other apart. That turns the capture
/// into a direct reading of the effect's own heat values rather than a brightness that has to be
/// argued about.
/// </para>
/// </summary>
public class NoiseEffectsAgainstHardwareTests(ITestOutputHelper output)
{
    private static readonly RgbColor[] Rainbow =
    [
        new(255, 0, 0), new(213, 42, 0), new(171, 85, 0), new(171, 127, 0),
        new(171, 171, 0), new(86, 213, 0), new(0, 255, 0), new(0, 213, 42),
        new(0, 171, 85), new(0, 86, 170), new(0, 0, 255), new(42, 0, 213),
        new(85, 0, 171), new(127, 0, 129), new(171, 0, 85), new(213, 0, 43),
    ];

    private static WledPalette RainbowPalette() => new()
    {
        Stops = [.. Rainbow.Select((c, i) => new PaletteStop((byte)(i * 16), c))],
    };

    /// <summary>Which palette entry a pixel is, and how far off it was - zero says it landed on one.</summary>
    private static (int Entry, int Apart) Nearest(RgbColor pixel)
    {
        int best = 0, apart = int.MaxValue;

        for (int k = 0; k < Rainbow.Length; k++)
        {
            int distance = Strip.Apart(pixel, Rainbow[k]);

            if (distance < apart)
            {
                apart = distance;
                best = k;
            }
        }

        return (best, apart);
    }

    /// <summary>
    /// Fire 2012 burns the bottom fifth of the run and leaves the rest dark.
    /// <para>
    /// Every pixel on the strip landed exactly on a palette entry, which is what says the lookup is
    /// unblended: heat is read as one of sixteen bands rather than as a gradient, and the bands are
    /// the flame's own structure.
    /// </para>
    /// <para>
    /// Read that way the profile is unambiguous. Averaged into ten slices the strip gave 5.29, 2.32
    /// and 0.16 for the three slices at the lit end and nothing at all for the other seven - a fire
    /// that reaches about a quarter of the way up a roofline this long and is mostly in the first
    /// tenth, which is the ignition area. It reached the top entry, 15, at some point.
    /// </para>
    /// <para>
    /// The slices come out back to front here, because the capture reads the strip along the wire and
    /// the roofline is wired in reverse. So the fire is in the last three rather than the first.
    /// </para>
    /// </summary>
    [Fact]
    public void Fire_2012_burns_the_bottom_of_the_run_in_sixteen_unblended_bands()
    {
        var segment = new EffectSegment(Strip.Roofline)
        {
            Speed = 64,
            Intensity = 160,
            Custom3 = 16,
            FrameMilliseconds = Strip.FrameMs,
            PaletteId = 11,
            Palette = RainbowPalette(),
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
        };

        var profile = new double[Strip.Roofline];
        List<double> means = [];
        int frames = 0, exact = 0, pixels = 0, highest = 0;

        Strip.Sample(new Fire2012Effect(), segment, 16_000, (frame, _) =>
        {
            frames++;
            double total = 0;

            for (int i = 0; i < frame.Length; i++)
            {
                (int entry, int apart) = Nearest(frame[i]);

                if (apart == 0)
                {
                    exact++;
                }

                pixels++;
                profile[i] += entry;
                total += entry;
                highest = Math.Max(highest, entry);
            }

            means.Add(total / frame.Length);
        });

        double[] slices = [.. Enumerable.Range(0, 10).Select(s =>
        {
            int from = s * Strip.Roofline / 10, to = (s + 1) * Strip.Roofline / 10;
            return profile[from..to].Average() / frames;
        })];

        output.WriteLine($"simulated: every pixel on an entry {(double)exact / pixels:F4}, " +
            $"mean entry {means.Average():F2}, highest {highest}");
        output.WriteLine($"simulated profile: {string.Join(", ", slices.Select(x => x.ToString("F2")))}");
        output.WriteLine("measured : every pixel on an entry 1.0000, mean entry 0.78, highest 15");
        output.WriteLine("measured profile: 0, 0, 0, 0, 0, 0, 0, 0.16, 2.32, 5.29");

        // Unblended, which is the whole reason a rainbow can be read back as heat.
        Assert.Equal(1.0, (double)exact / pixels, 4);

        // Hot at the ignition end - the last slice, the run being read backwards - and dark by a
        // third of the way along.
        Assert.True(slices[9] > slices[8], "the fire should be hottest where it is lit");
        Assert.True(slices[8] > slices[7], "and cool going away from there");
        Assert.All(slices[..6], x => Assert.True(x < 0.2, $"the far end should be dark, got {x:F2}"));

        Assert.InRange(means.Average(), 0.4, 1.6);
        Assert.InRange(highest, 12, 15);
    }

    /// <summary>
    /// Phased Noise against Phased: the aggregate numbers cannot tell them apart, and the spacing of
    /// their peaks can.
    /// <para>
    /// The strip gave the two effects the same mean, the same spread, the same band count and the same
    /// share of the run below the cutoff - 39.5, 48.3, 76 and 0.51 for both, to within a band. Which
    /// makes sense: they are the same waves, grouped differently.
    /// </para>
    /// <para>
    /// The grouping is what differs and it shows in how far apart the peaks are. Phased divides the
    /// phase advance by position modulo five, so the spacings it can produce are a structured set with
    /// holes in it: the strip put 1.0% of its gaps at five LEDs, skipping from four straight to seven.
    /// Phased Noise takes the modulus from Perlin noise instead, and fills the hole in - 9.1%, nine
    /// times as many.
    /// </para>
    /// </summary>
    [Fact]
    public void Phased_Noise_fills_in_the_peak_spacings_that_Phased_cannot_reach()
    {
        (double noisyFives, double noisyBands) = Spacings(new PhasedNoiseEffect());
        (double plainFives, double plainBands) = Spacings(new PhasedEffect());

        output.WriteLine($"simulated: Phased Noise puts {noisyFives:F3} of gaps at five, " +
            $"Phased {plainFives:F3}; bands {noisyBands:F1} and {plainBands:F1}");
        output.WriteLine("measured : 0.091 and 0.010; bands 76.1 and 75.8");

        // Both are the same waves, so the band counts agree.
        Assert.InRange(noisyBands, 60, 90);
        Assert.InRange(Math.Abs(noisyBands - plainBands), 0, 8);

        // And the spacing is where they part company.
        Assert.InRange(noisyFives, 0.04, 0.16);
        Assert.InRange(plainFives, 0, 0.04);
        Assert.True(noisyFives > plainFives * 3,
            $"noise should fill the gap Phased leaves: {noisyFives:F3} against {plainFives:F3}");
    }

    /// <summary>How far apart the peaks along the run are, and how many there are.</summary>
    private static (double FiveApart, double Bands) Spacings(IWledEffect effect)
    {
        List<int> gaps = [];
        List<int> bands = [];

        Strip.Sample(effect, Strip.Run(), 14_000, (frame, _) =>
        {
            int last = -1, found = 0;
            bool climbing = false;

            for (int i = 1; i < frame.Length; i++)
            {
                if (frame[i].R > frame[i - 1].R)
                {
                    climbing = true;
                }
                else if (climbing && frame[i].R < frame[i - 1].R)
                {
                    if (last >= 0)
                    {
                        gaps.Add(i - last);
                    }

                    last = i;
                    found++;
                    climbing = false;
                }
            }

            bands.Add(found);
        });

        return ((double)gaps.Count(g => g == 5) / gaps.Count, bands.Average());
    }
}
