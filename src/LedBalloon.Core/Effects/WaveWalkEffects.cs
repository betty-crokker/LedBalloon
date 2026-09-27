using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A handful of points whose positions come from Perlin noise rather than from a sine, so they wander
/// instead of oscillating.
/// <para>
/// Both noise coordinates advance together with one offset by 15000 per point, which means the points
/// share a path and follow each other along it at a fixed distance. The noise is mapped from the
/// range 50 to 192 of 256 onto the run without clamping, so a point whose noise strays outside that
/// band simply is not drawn - which is why the count on screen is fewer than the slider says.
/// </para>
/// </summary>
public sealed class PerlinMoveEffect : IWledEffect
{
    public string Name => "Perlin Move";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        segment.FadeOut((byte)(255 - segment.Custom1));

        uint along = now * 128 / (uint)(260 - segment.Speed);

        for (int i = 0; i < (segment.Intensity / 16) + 1; i++)
        {
            ushort noise = Perlin.Noise16(along + (uint)(i * 15000), along);

            int at = FastLed.Map(noise, 50 * 256, 192 * 256, 0, segment.Length - 1);

            segment.SetPixel(at, segment.ColorFromPalette(
                at % 255, wrap: segment.SolidWrap));
        }
    }
}

/// <summary>
/// Two sines at once: one sets each LED's brightness and the other picks its color, with a phase
/// shift along the run so neither lines up with the other.
/// <para>
/// Every slider does something different here and none of them is speed in the usual sense.
/// Intensity is the brightness wave's wavelength, Custom 1 is where in the palette the colors start,
/// Custom 2 how much of the palette they span, and Custom 3 how fast the color changes along the run.
/// The brightness wave runs on the clock and the color wave on a tempo, so the two drift.
/// </para>
/// </summary>
public sealed class WavesinsEffect : IWledEffect
{
    public string Name => "Wavesins";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        for (int i = 0; i < segment.Length; i++)
        {
            byte bright = FastLed.Sin8((byte)((now / 4) + (uint)(i * segment.Intensity)));

            // Deliberately byte arithmetic: at the default settings the top of the range is
            // 128 + 128, which wraps to zero, and the range becomes 128 wide the other way round.
            // The effect is built on that wrapping rather than in spite of it.
            byte index = FastLed.BeatSin8(
                segment.Speed,
                segment.Custom1,
                (byte)(segment.Custom1 + segment.Custom2),
                now,
                (byte)(i * (segment.Custom3 << 3)));

            segment.Pixels[i] = segment.ColorFromPalette(
                index, wrap: segment.SolidWrap, brightness: bright);
        }
    }
}

/// <summary>
/// A cubic wave sliding along the run over the secondary color, with the palette walking slowly on
/// its own clock.
/// <para>
/// The wave moves on an accumulated step rather than on the clock, so its speed follows how fast the
/// controller is drawing; the color does not. Intensity is the wave's frequency, and the palette
/// index is scaled by position <em>and</em> by a counter that grows without bound, so the coloring
/// sweeps along the run faster and faster - which sounds wrong and is what it does.
/// </para>
/// </summary>
public sealed class SineEffect : IWledEffect
{
    public string Name => "Sine";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint colorIndex = now / 32;

        segment.Step += (uint)(segment.Speed / 16);

        int frequency = segment.Intensity / 4;

        for (int i = 0; i < segment.Length; i++)
        {
            byte bright = FastLed.CubicWave8((byte)((uint)(i * frequency) + segment.Step));

            segment.Pixels[i] = EffectSegment.Blend(
                segment.Colors[1],
                segment.ColorFromPalette(
                    (int)((uint)i * colorIndex / 255), wrap: segment.SolidWrap),
                bright);
        }
    }
}

/// <summary>
/// Hues rolling along the run - or that is the intent. In WLED 0.15.3 the whole run is one color.
/// <para>
/// The position term is <c>(abs(i - hl) / hl) * 127</c> with <c>hl</c> ten thirteenths of the run's
/// length, and that division is integer. <c>abs(i - hl)</c> reaches <c>hl</c> at exactly one LED -
/// the first, where it is <c>hl</c> itself - and is below it everywhere else, so the quotient is one
/// there and zero for the other 284. What is left is three nested sines of time: a single color
/// washing through the hues, with one LED out of step.
/// </para>
/// <para>
/// Reproduced rather than corrected, and checkable because of it: the strip shows two colors at most
/// and only ever one of them on more than a single LED, which is not something a working gradient
/// could do.
/// </para>
/// </summary>
public sealed class FlowStripeEffect : IWledEffect
{
    public string Name => "Flow Stripe";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        int third = segment.Length * 10 / 13;

        var hue = (byte)(now / (uint)(segment.Speed + 1));
        uint t = now / (uint)((segment.Intensity / 8) + 1);

        for (int i = 0; i < segment.Length; i++)
        {
            // Integer division, and it is always zero. See the remarks.
            int c = Math.Abs(i - third) / third * 127;

            c = FastLed.Sin8((byte)c);
            c = FastLed.Sin8((byte)((c / 2) + t));

            var b = FastLed.Sin8((byte)(c + (t / 8)));

            segment.Pixels[i] = FastLed.Hsv2Rgb((byte)(b + hue), 255, 255);
        }
    }
}
