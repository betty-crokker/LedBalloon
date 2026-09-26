using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// One LED lit at a time over a palette background, hopping somewhere new every so often.
/// <para>
/// Exactly one, however long the run: on a 285 LED roofline that is a single moving point, which
/// is the whole idea. Speed sets how often it hops and nothing sets how many there are.
/// </para>
/// </summary>
public sealed class SparkleEffect : IWledEffect
{
    public string Name => "Sparkle";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (!segment.Option2)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                segment.Pixels[i] = segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1);
            }
        }

        uint cycleTime = 10u + ((255u - segment.Speed) * 2u);
        uint tick = now / cycleTime;

        if (tick != segment.Step)
        {
            segment.Aux0 = segment.Random16(segment.Length);
            segment.Step = tick;
        }

        segment.SetPixel((int)segment.Aux0, segment.Colors[0]);
    }
}

/// <summary>
/// A palette background with the occasional single LED flashing the secondary color.
/// <para>
/// Both sliders work on the odds rather than on the pattern: speed sets how often the dice are
/// thrown and intensity sets how many faces they have. It is genuinely sparse — at the middle of
/// both sliders a flash lands about once a second, and most frames show nothing at all.
/// </para>
/// </summary>
public sealed class FlashSparkleEffect : IWledEffect
{
    public string Name => "Sparkle Dark";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (!segment.Option2)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                segment.Pixels[i] = segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap);
            }
        }

        if (now - segment.Aux0 > segment.Step)
        {
            if (segment.Random8((255 - segment.Intensity) >> 4) == 0)
            {
                segment.SetPixel(segment.Random16(segment.Length), segment.Colors[1]);
            }

            segment.Step = now;
            segment.Aux0 = (uint)(255 - segment.Speed);
        }
    }
}

/// <summary>
/// The same throw of the dice as Sparkle Dark, but a hit lights a third of the run at once.
/// <para>
/// The scatter is drawn with replacement, so the same LED can be picked twice and rather fewer
/// than a third actually light — about a quarter of the run on average. That is not a bug being
/// reproduced for its own sake: it is what stops the flash looking like a solid block.
/// </para>
/// </summary>
public sealed class HyperSparkleEffect : IWledEffect
{
    public string Name => "Sparkle+";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (!segment.Option2)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                segment.Pixels[i] = segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap);
            }
        }

        if (now - segment.Aux0 > segment.Step)
        {
            if (segment.Random8((255 - segment.Intensity) >> 4) == 0)
            {
                for (int i = 0; i < Math.Max(1, segment.Length / 3); i++)
                {
                    segment.SetPixel(segment.Random16(segment.Length), segment.Colors[1]);
                }
            }

            segment.Step = now;
            segment.Aux0 = (uint)(255 - segment.Speed);
        }
    }
}

/// <summary>
/// LEDs lighting one by one in a scattered order, then all fading away and starting over.
/// <para>
/// The scatter is not redrawn each frame. One random seed is taken per round and the positions are
/// generated from it with a small generator carried in the effect itself, so the same LEDs light
/// in the same order every frame until the round ends — which is what makes it read as a pattern
/// filling in rather than as noise.
/// </para>
/// <para>
/// That generator is ported exactly rather than replaced, because it is the pattern. Only the seed
/// differs from the controller's.
/// </para>
/// </summary>
public sealed class TwinkleEffect : IWledEffect
{
    public string Name => "Twinkle";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        segment.FadeOut(224);

        uint cycleTime = 20u + ((255u - segment.Speed) * 5u);
        uint tick = now / cycleTime;

        if (tick != segment.Step)
        {
            // At least one, however low the slider goes.
            int mostOn = 1 + (segment.Intensity * (segment.Length - 1) / 255);

            if (segment.Aux0 >= mostOn)
            {
                segment.Aux0 = 0;
                segment.Aux1 = segment.Random16();
            }

            segment.Aux0++;
            segment.Step = tick;
        }

        var generator = (ushort)segment.Aux1;

        for (uint i = 0; i < segment.Aux0; i++)
        {
            unchecked
            {
                generator = (ushort)((generator * 2053) + 13849);
            }

            int at = (int)((uint)segment.Length * generator >> 16);

            segment.SetPixel(at, segment.ColorFromPalette(
                at, mapping: true, wrap: segment.SolidWrap));
        }
    }
}

/// <summary>
/// Every LED dimmed by its own random amount each tick, which reads as a guttering flame.
/// <para>
/// It subtracts rather than scales, and it subtracts the same amount from every channel, so a warm
/// colour goes dark without going grey. How deep the flicker runs is set by intensity; on palette
/// Default the depth is taken from how bright the chosen colour is to begin with, so a dim colour
/// flickers less rather than dropping to black.
/// </para>
/// </summary>
public sealed class FireFlickerEffect : IWledEffect
{
    public string Name => "Fire Flicker";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint cycleTime = 40u + (255u - segment.Speed);
        uint tick = now / cycleTime;

        if (segment.Step == tick)
        {
            return;
        }

        RgbColor first = segment.Colors[0];

        int brightest = segment.PaletteId == 0
            ? Math.Max(first.R, Math.Max(first.G, first.B))
            : 255;

        int depth = brightest / (((256 - segment.Intensity) / 16) + 1);

        for (int i = 0; i < segment.Length; i++)
        {
            byte flicker = segment.Random8(depth);

            segment.Pixels[i] = segment.PaletteId == 0
                ? new RgbColor(
                    (byte)Math.Max(first.R - flicker, 0),
                    (byte)Math.Max(first.G - flicker, 0),
                    (byte)Math.Max(first.B - flicker, 0))
                : segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap, colorSlot: 0,
                    brightness: (byte)(255 - flicker));
        }

        segment.Step = tick;
    }
}
