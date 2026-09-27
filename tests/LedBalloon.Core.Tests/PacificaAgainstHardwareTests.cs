using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Pacifica, captured on the south controller's 285 LED roofline on palette Default, where it uses the
/// three seascape palettes it carries rather than a chosen one.
/// <para>
/// There is no randomness in this effect at all, but it cannot be checked frame for frame either: four
/// color-index counters accumulate across frames, so a picture depends on every frame that came before
/// it and the controller's frame interval is not exactly nine milliseconds. What it can be held to is
/// the shape of the sea - how the three channels sit against each other, where the floor is, and how
/// often a crest breaks white.
/// </para>
/// </summary>
public class PacificaAgainstHardwareTests(ITestOutputHelper output)
{
    /// <summary>
    /// Pacifica floors its three channels at 2, 5 and 7, and the strip never goes below any of them.
    /// <para>
    /// The floor is applied last, after blue and green have been scaled down, so it always holds: over
    /// 199 frames and 285 LEDs the least red anywhere was exactly 2, the least green exactly 5 and the
    /// least blue exactly 7. Three exact numbers out of a capture of an effect with a dozen interacting
    /// sines in it, and they pin the last two lines of the effect between them.
    /// </para>
    /// <para>
    /// The sea is blue over green over red, by a wide margin - a mean of 27.3 blue against 22.7 green
    /// and 6.3 red. Which is not only the palettes: green is scaled by 200 and blue by 145 near the end,
    /// so blue is cut harder than green and still finishes ahead.
    /// </para>
    /// <para>
    /// White crests are rare and real: about one and a half LEDs a frame have all three channels above
    /// 30, which is where the four layers have lined up past the threshold and the excess has been added
    /// back as white. Nothing else in this effect can light red and green together that far.
    /// </para>
    /// </summary>
    [Fact]
    public void Pacifica_floors_its_channels_at_two_five_and_seven()
    {
        EffectSegment segment = Run();

        int leastRed = 255, leastGreen = 255, leastBlue = 255;
        int mostRed = 0, mostGreen = 0, mostBlue = 0;
        long red = 0, green = 0, blue = 0;
        int pixels = 0;
        double crests = 0;
        List<double> brightness = [], spread = [], peaks = [], colors = [];

        Strip.Sample(new PacificaEffect(), segment, 12_000, (frame, _) =>
        {
            Strip.Shape profile = Strip.Profile(frame, p => Math.Max(p.R, Math.Max(p.G, p.B)));

            brightness.Add(profile.Mean);
            spread.Add(profile.Deviation);
            peaks.Add(profile.Bands);
            colors.Add(frame.Distinct().Count());

            crests += frame.Count(p => Math.Min(p.R, Math.Min(p.G, p.B)) > 30);

            foreach (RgbColor pixel in frame)
            {
                leastRed = Math.Min(leastRed, pixel.R);
                leastGreen = Math.Min(leastGreen, pixel.G);
                leastBlue = Math.Min(leastBlue, pixel.B);

                mostRed = Math.Max(mostRed, pixel.R);
                mostGreen = Math.Max(mostGreen, pixel.G);
                mostBlue = Math.Max(mostBlue, pixel.B);

                red += pixel.R;
                green += pixel.G;
                blue += pixel.B;
                pixels++;
            }
        });

        crests /= brightness.Count;

        output.WriteLine($"simulated: least {leastRed}/{leastGreen}/{leastBlue}, " +
            $"most {mostRed}/{mostGreen}/{mostBlue}, " +
            $"mean {(double)red / pixels:F2}/{(double)green / pixels:F2}/{(double)blue / pixels:F2}");
        output.WriteLine($"           brightness {brightness.Average():F2}, spread {spread.Average():F2}, " +
            $"{peaks.Average():F2} peaks, {colors.Average():F1} colors, {crests:F2} crest LEDs");
        output.WriteLine("measured : least 2/5/7, most 71/188/145, mean 6.27/22.71/27.25");
        output.WriteLine("           brightness 32.10, spread 16.72, 22.65 peaks, 209.7 colors, " +
            "1.47 crest LEDs");

        // The floors, which are the last thing the effect does and so cannot be argued with.
        Assert.Equal(2, leastRed);
        Assert.Equal(5, leastGreen);
        Assert.Equal(7, leastBlue);

        // Blue over green over red, and by a margin rather than narrowly.
        Assert.True(blue > green, $"blue should lead green: {blue} against {green}");
        Assert.True(green > red * 2, $"and green should be well clear of red: {green} against {red}");

        Assert.InRange((double)red / pixels, 4, 9);
        Assert.InRange((double)green / pixels, 17, 29);
        Assert.InRange((double)blue / pixels, 21, 34);

        Assert.InRange(brightness.Average(), 25, 40);
        Assert.InRange(spread.Average(), 12, 22);
        Assert.InRange(peaks.Average(), 16, 30);

        // Crests: a handful of LEDs, not a handful of hundreds and not none.
        Assert.InRange(crests, 0.3, 5.0);
    }

    /// <summary>
    /// Pacifica's speed slider decides whether its waves travel, not whether anything moves.
    /// <para>
    /// The four color-index counters advance by
    /// <c>(FRAMETIME &gt;&gt; 2) + ((FRAMETIME * speed) &gt;&gt; 7)</c> per frame, and with FRAMETIME at
    /// its 2 ms floor the first term is zero and the second needs a speed of 64 before it reaches one. So
    /// below that the counters never move at all: the waves stop travelling along the run.
    /// </para>
    /// <para>
    /// The picture does not stop, though, and it was worth being wrong about that to find out. The first
    /// version of this test asserted a still run at speed zero and failed, because the warped clock every
    /// beat inside the effect is measured against carries on regardless - the layer brightnesses and the
    /// wavelengths keep breathing. What the slider holds still is the drift, not the sea.
    /// </para>
    /// <para>
    /// Simulation only, and asserted on the counters rather than on the pixels, since "the waves are not
    /// travelling but everything else is" is not something a brightness statistic can say.
    /// </para>
    /// </summary>
    [Fact]
    public void Pacifica_s_slider_decides_whether_its_waves_travel()
    {
        foreach ((byte speed, bool travels) in new[]
            { ((byte)0, false), ((byte)63, false), ((byte)128, true), ((byte)255, true) })
        {
            EffectSegment segment = Run();
            segment.Speed = speed;

            var effect = new PacificaEffect();

            segment.Draw(effect, 0);
            RgbColor[] first = [.. segment.Pixels];

            for (uint t = (uint)Strip.FrameMs; t < 4_000; t += (uint)Strip.FrameMs)
            {
                segment.Draw(effect, t);
            }

            bool drifted = segment.Aux0 != 0 || segment.Aux1 != 0 || segment.Step != 0;
            bool changed = !first.SequenceEqual(segment.Pixels);

            output.WriteLine($"speed {speed,3}: step {(2 >> 2) + ((2 * speed) >> 7)} a frame, " +
                $"counters {(drifted ? "drift" : "stay put")}, picture {(changed ? "moves" : "still")}");

            Assert.Equal(travels, drifted);

            // Either way the sea is alive, which is the thing the first version of this test got wrong.
            Assert.True(changed, $"the picture should keep moving at speed {speed}");
        }
    }

    /// <summary>The run as the capture had it - palette Default, so the built-in seascapes are used.</summary>
    private static EffectSegment Run() => new(Strip.Roofline)
    {
        SegmentId = 2,
        Speed = 128,
        Intensity = 128,
        FrameMilliseconds = Strip.FrameMs,
        PaletteId = 0,
        Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255), new RgbColor(0, 255, 0)],
    };
}
