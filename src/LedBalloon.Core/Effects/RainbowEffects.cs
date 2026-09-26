using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The whole run one color at a time, walking round the wheel together.
/// <para>
/// WLED calls this <c>mode_rainbow</c> in the source and <b>Colorloop</b> in its effect list,
/// while <c>mode_rainbow_cycle</c> is the one it lists as <b>Rainbow</b>. Matching on the
/// source name would bind both to the wrong effect, which is what the names here are for.
/// </para>
/// <para>
/// Intensity washes it out rather than speeding it up: below halfway the color is mixed toward
/// white, so the slider runs from pastel to saturated and stops. On any palette but Default this
/// walks that palette instead of a rainbow.
/// </para>
/// </summary>
public sealed class ColorloopEffect : IWledEffect
{
    public string Name => "Colorloop";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var counter = (byte)(((now * (uint)((segment.Speed >> 2) + 2)) & 0xFFFF) >> 8);
        RgbColor wheel = segment.ColorWheel(counter);

        segment.Fill(segment.Intensity < 128
            ? EffectSegment.Blend(wheel, RgbColor.White, (byte)(128 - segment.Intensity))
            : wheel);
    }
}

/// <summary>
/// The wheel laid along the run and turning, so the colors travel rather than change together.
/// <para>
/// Intensity sets how many times the wheel repeats along the run, and it does so in doublings —
/// the slider is divided by 29 and used as a shift, giving nine steps from a sixteenth of a turn
/// to sixteen turns. It is the same walk as Rainbow with the position offset per LED.
/// </para>
/// </summary>
public sealed class RainbowEffect : IWledEffect
{
    public string Name => "Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var counter = (byte)(((now * (uint)((segment.Speed >> 2) + 2)) & 0xFFFF) >> 8);
        int repeats = 16 << (segment.Intensity / 29);

        for (int i = 0; i < segment.Length; i++)
        {
            var index = (byte)((i * repeats / segment.Length) + counter);
            segment.Pixels[i] = segment.ColorWheel(index);
        }
    }
}

/// <summary>
/// Bursts of flashes with a rest between them, rather than an even blink.
/// <para>
/// Intensity is how many flashes are in a burst and speed is how long the rest lasts, so the two
/// sliders set the rhythm rather than the rate. The flashes themselves are fixed — 15 ms lit, 50
/// off — which is why they stay sharp however slowly the pattern repeats.
/// </para>
/// </summary>
public sealed class StrobeMegaEffect : IWledEffect
{
    public string Name => "Strobe Mega";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1);
        }

        segment.Aux0 = (uint)(50 + (20 * (255 - segment.Speed)));

        uint count = 2 * (uint)((segment.Intensity / 10) + 1);

        if (segment.Aux1 < count)
        {
            if ((segment.Aux1 & 1) == 0)
            {
                segment.Fill(segment.Colors[0]);
                segment.Aux0 = 15;
            }
            else
            {
                segment.Aux0 = 50;
            }
        }

        if (now - segment.Aux0 > segment.Step)
        {
            segment.Aux1++;

            if (segment.Aux1 > count)
            {
                segment.Aux1 = 0;
            }

            segment.Step = now;
        }
    }
}

/// <summary>
/// The three color slots crossfading into each other in a loop.
/// <para>
/// Only the middle leg involves the palette: the run goes color 1 to color 2 flat, then fades
/// through the palette to color 3, then back to color 1. That asymmetry is deliberate and is what
/// keeps it from reading as a plain three-way fade.
/// </para>
/// </summary>
public sealed class TriFadeEffect : IWledEffect
{
    public string Name => "Tri Fade";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        // Deliberately sixteen bits: the counter is meant to wrap, and that wrap is the loop.
        var counter = (ushort)(now * (uint)((segment.Speed >> 3) + 1));
        uint progress = ((uint)counter * 768) >> 16;

        int stage = progress < 256 ? 0 : progress < 512 ? 1 : 2;

        RgbColor first = segment.Colors[stage];
        RgbColor second = segment.Colors[(stage + 1) % 3];

        var step = (byte)progress;

        for (int i = 0; i < segment.Length; i++)
        {
            RgbColor fromPalette = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap, colorSlot: 2);

            segment.Pixels[i] = stage switch
            {
                2 => EffectSegment.Blend(fromPalette, second, step),
                1 => EffectSegment.Blend(first, fromPalette, step),
                _ => EffectSegment.Blend(first, second, step),
            };
        }
    }
}
