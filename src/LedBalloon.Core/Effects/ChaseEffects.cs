using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Two bands of color chasing each other round the run, which is where four of WLED's effects come
/// from.
/// <para>
/// Two adjacent bands, each as wide as intensity says, travelling over a background and wrapping
/// round the end. What the four wrappers change is only where the three colors come from: fixed
/// slots, a random wheel position that changes each lap, or a wheel position that walks on by one
/// every frame.
/// </para>
/// </summary>
internal static class Chase
{
    /// <param name="background">What the rest of the run shows.</param>
    /// <param name="leading">The first band, which is the one in front.</param>
    /// <param name="trailing">The second band, immediately behind it.</param>
    /// <param name="usePalette">
    /// True to draw the background from the palette rather than from
    /// <paramref name="background"/>, which is what makes plain Chase a chase over a gradient.
    /// </param>
    /// <param name="randomColors">
    /// True to pick the background off the color wheel afresh each lap, keeping the previous one for
    /// the stretch of run the bands have already passed.
    /// </param>
    public static void Render(
        EffectSegment segment,
        uint now,
        RgbColor background,
        RgbColor leading,
        RgbColor trailing,
        bool usePalette,
        bool randomColors = false)
    {
        var counter = (ushort)(now * (uint)((segment.Speed >> 2) + 1));
        var a = (int)(((uint)counter * (uint)segment.Length) >> 16);

        if (randomColors)
        {
            // Back at the start means a lap has finished, so the color that was coming becomes the
            // color that has been.
            if (a < segment.Step)
            {
                segment.Aux1 = segment.Aux0;
                segment.Aux0 = segment.RandomWheelIndex((byte)segment.Aux0);
            }

            background = segment.ColorWheel((byte)segment.Aux0);
        }

        segment.Step = (uint)a;

        // Intensity runs the band up to half the run's length.
        int size = 1 + ((segment.Intensity * segment.Length) >> 10);

        int b = a + size;

        if (b > segment.Length)
        {
            b -= segment.Length;
        }

        int c = b + size;

        if (c > segment.Length)
        {
            c -= segment.Length;
        }

        if (usePalette)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                segment.Pixels[i] = segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1);
            }
        }
        else
        {
            segment.Fill(background);
        }

        if (randomColors)
        {
            // Everything the bands have already gone past keeps the previous lap's color, so the new
            // one arrives behind them rather than appearing everywhere at once.
            RgbColor previous = segment.ColorWheel((byte)segment.Aux1);

            for (int i = a; i < segment.Length; i++)
            {
                segment.Pixels[i] = previous;
            }
        }

        FillWrapping(segment, a, b, leading);
        FillWrapping(segment, b, c, trailing);
    }

    /// <summary>Paints from <paramref name="from"/> up to <paramref name="to"/>, round the end if need be.</summary>
    private static void FillWrapping(EffectSegment segment, int from, int to, RgbColor color)
    {
        if (from < to)
        {
            for (int i = from; i < to; i++)
            {
                segment.SetPixel(i, color);
            }

            return;
        }

        for (int i = from; i < segment.Length; i++)
        {
            segment.SetPixel(i, color);
        }

        for (int i = 0; i < to; i++)
        {
            segment.SetPixel(i, color);
        }
    }

    /// <summary>
    /// The leading band's color: the third slot when it is set, otherwise the primary - so the two
    /// bands are the same color until a third is chosen, and the effect reads as one band.
    /// </summary>
    public static RgbColor Leading(EffectSegment segment) =>
        segment.Colors[2] == RgbColor.Black ? segment.Colors[0] : segment.Colors[2];
}

/// <summary>
/// Three colors in repeating stripes, marching along the run.
/// <para>
/// Not built on the shared chase at all despite the name: this one has no bands over a background,
/// it is stripes of equal width all the way along, and it moves by shifting which stripe each LED
/// falls in rather than by advancing a position. Size runs from one LED to sixteen.
/// </para>
/// <para>
/// It also paints from the far end backwards, so at the same settings it marches the opposite way
/// from every other chase.
/// </para>
/// </summary>
public sealed class TricolorChaseEffect : IWledEffect
{
    public string Name => "Chase 3";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint cycleTime = 50u + ((255u - segment.Speed) << 1);
        uint tick = now / cycleTime;

        int width = 1 + (segment.Intensity >> 4);
        var index = (int)(tick % (uint)(width * 3));

        for (int i = 0; i < segment.Length; i++, index++)
        {
            if (index > (width * 3) - 1)
            {
                index = 0;
            }

            RgbColor color =
                index > (width << 1) - 1
                    ? segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1)
                    : index > width - 1 ? segment.Colors[0]
                    : segment.Colors[2];

            segment.SetPixel(segment.Length - 1 - i, color);
        }
    }
}

/// <summary>Two bands chasing each other over the palette.</summary>
public sealed class ChaseColorEffect : IWledEffect
{
    public string Name => "Chase";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Chase.Render(
            segment, now, segment.Colors[1], Chase.Leading(segment), segment.Colors[0],
            usePalette: true);
    }
}

/// <summary>
/// The same chase over a background that changes to a new random color each time the bands come
/// round, the new color following along behind them.
/// </summary>
public sealed class ChaseRandomEffect : IWledEffect
{
    public string Name => "Chase Random";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Chase.Render(
            segment, now, segment.Colors[1], Chase.Leading(segment), segment.Colors[0],
            usePalette: false, randomColors: true);
    }
}

/// <summary>
/// The bands in the two color slots over a background that walks round the color wheel.
/// <para>
/// The wheel position is a frame count, not a clock: one step per frame drawn, which makes this one
/// of the few effects whose speed on screen depends on how fast the controller is managing to draw.
/// It also reads the band position from the frame before, since the shared chase updates it after.
/// </para>
/// </summary>
public sealed class ChaseRainbowEffect : IWledEffect
{
    public string Name => "Chase Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        // A run longer than the wheel would give every LED the same color, so it is held at one.
        int separation = Math.Max(1, 256 / segment.Length);

        var background = segment.ColorWheel(
            (byte)(((segment.Step * separation) + (segment.Call & 0xFF)) & 0xFF));

        Chase.Render(
            segment, now, background, segment.Colors[0], segment.Colors[1], usePalette: false);
    }
}

/// <summary>
/// The other way round: the background is the primary color and the two bands are the ones walking
/// the wheel, a step apart from each other.
/// </summary>
public sealed class RainbowRunnerEffect : IWledEffect
{
    public string Name => "Rainbow Runner";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint n = segment.Step;
        uint m = (segment.Step + 1) % (uint)segment.Length;
        uint walk = segment.Call & 0xFF;

        RgbColor Wheel(uint at) =>
            segment.ColorWheel((byte)(((at * 256 / (uint)segment.Length) + walk) & 0xFF));

        Chase.Render(
            segment, now, segment.Colors[0], Wheel(n), Wheel(m), usePalette: false);
    }
}
