using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Every LED twinkling on its own sine, with its phase, its brightness gate and its color all drawn
/// from a sequence reset to the same seed every frame.
/// <para>
/// Nothing is stored between frames and nothing is random in the usual sense: setting the seed to 535
/// and walking three draws per LED gives every LED the same three numbers it had last frame, so it
/// keeps its phase and its color while the whole run breathes. Reset the seed differently and it is a
/// different, equally stable pattern.
/// </para>
/// <para>
/// Which is why this needs the controller's own generator rather than a convenient one. The sequence
/// <em>is</em> the effect: get the arithmetic wrong and you get a plausible twinkle that is not this
/// one.
/// </para>
/// </summary>
public sealed class TwinkleUpEffect : IWledEffect
{
    public string Name => "Twinkleup";

    /// <summary>The seed WLED picks, which decides the whole pattern.</summary>
    private const ushort Pattern = 535;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        ushort borrowed = segment.Sequence.Seed;
        segment.Sequence.Seed = Pattern;

        for (int i = 0; i < segment.Length; i++)
        {
            byte start = segment.Sequence.Byte();

            var bright = FastLed.Sin8(
                (byte)(start + (16 * now / (uint)(256 - segment.Speed))));

            // A second draw gates it: intensity is what share of the run is allowed to light at all,
            // and the ones it excludes are the same ones every frame.
            if (segment.Sequence.Byte() > segment.Intensity)
            {
                bright = 0;
            }

            // And a third picks the color, drifting with the clock so the whole run walks the palette.
            var index = (int)(segment.Sequence.Byte() + (now / 100));

            segment.Pixels[i] = EffectSegment.Blend(
                segment.Colors[1],
                segment.ColorFromPalette(index, wrap: segment.SolidWrap),
                bright);
        }

        // Put the generator back, because it is shared and the next effect's pattern depends on it.
        segment.Sequence.Seed = borrowed;
    }
}

/// <summary>
/// A color travelling down the run and mutating as it goes: each LED mostly copies its neighbor and
/// now and then replaces one channel outright.
/// <para>
/// Drawn from the far end back, so the colors flow the other way. Each channel has a one in six
/// chance of being replaced rather than copied, which gives long stretches of near-identical color
/// broken by sudden shifts - and because the chain is built afresh every frame from one saved seed,
/// the whole stream slides along the run rather than flickering.
/// </para>
/// <para>
/// The seed is stored, not the pixels. A new colour is only admitted at the head when the tick
/// changes, and the seed that produced the rest is kept so the next frame rebuilds the same chain one
/// place along.
/// </para>
/// </summary>
public sealed class RandomChaseEffect : IWledEffect
{
    public string Name => "Stream 2";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Call == 0)
        {
            segment.Step = Pack(
                segment.Sequence.Byte(), segment.Sequence.Byte(), segment.Sequence.Byte());

            segment.Aux0 = segment.Sequence.Word();
        }

        ushort borrowed = segment.Sequence.Seed;

        uint cycleTime = 25u + (3u * (255u - segment.Speed));
        uint tick = now / cycleTime;

        uint color = segment.Step;

        segment.Sequence.Seed = (ushort)segment.Aux0;

        for (int i = segment.Length - 1; i >= 0; i--)
        {
            byte r = segment.Sequence.Byte(6) != 0 ? (byte)(color >> 16) : segment.Sequence.Byte();
            byte g = segment.Sequence.Byte(6) != 0 ? (byte)(color >> 8) : segment.Sequence.Byte();
            byte b = segment.Sequence.Byte(6) != 0 ? (byte)color : segment.Sequence.Byte();

            color = Pack(r, g, b);
            segment.Pixels[i] = new RgbColor(r, g, b);

            if (i == segment.Length - 1 && segment.Aux1 != (tick & 0xFFFF))
            {
                // The head has moved on, so this colour and the generator's state behind it become
                // next frame's starting point.
                segment.Step = color;
                segment.Aux0 = segment.Sequence.Seed;
            }
        }

        segment.Aux1 = tick & 0xFFFF;
        segment.Sequence.Seed = borrowed;
    }

    private static uint Pack(byte r, byte g, byte b) => ((uint)r << 16) | ((uint)g << 8) | b;
}
