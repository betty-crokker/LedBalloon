using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The Knight Rider sweep: a point running the length of the run and back, everything behind it
/// fading out.
/// <para>
/// Unusually, this one paints a position it carries rather than working one out from the clock. It
/// advances by however many LEDs a frame is worth and remembers where it got to, which is why it
/// needs a frame time to be right - and the frame time it needs is WLED's <c>FRAMETIME</c>
/// constant, not how often the strip actually draws.
/// </para>
/// <para>
/// Two regimes, and which one it is in depends on the run's length as much as on the slider. Fast
/// enough and it advances whole LEDs per frame; slow enough and it spends several frames on each
/// LED instead, counting them out in the same field it uses to hold a pause. Intensity is the trail
/// and Custom 1 is a pause at the ends, in units of 25 ms.
/// </para>
/// </summary>
internal static class LarsonScanner
{
    public static void Render(EffectSegment segment, uint now, bool dual)
    {
        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        // Scanner Dual is Scanner with the mirror option forced on, which is all it is.
        bool mirrored = dual || segment.Option1;

        int pace = segment.FrameTime * FastLed.Map(segment.Speed, 0, 255, 96, 2);
        int perFrame = segment.Length / Math.Max(1, pace);

        segment.FadeOut((byte)(255 - segment.Intensity));

        // Waiting out a pause at one end.
        if (segment.Step > now)
        {
            return;
        }

        int index = (int)segment.Aux1 + perFrame;

        if (perFrame == 0)
        {
            // Too slow to move an LED a frame, so count frames per LED instead - in the same field
            // that holds the pause, which is why WLED is careful to clear it when there is none.
            int framesPerLed = pace / segment.Length;

            if (segment.Step++ < framesPerLed)
            {
                return;
            }

            segment.Step = 0;
            index++;
        }

        if (index > segment.Length)
        {
            segment.Aux0 = segment.Aux0 != 0 ? 0u : 1u;
            segment.Aux1 = 0;

            // The pause is taken at one end only unless the bi-delay option is set.
            segment.Step = segment.Aux0 != 0 || segment.Option2
                ? now + ((uint)segment.Custom1 * 25u)
                : 0;

            return;
        }

        // Paint every LED the point passed over this frame, not just the one it landed on.
        for (int i = (int)segment.Aux1; i < index; i++)
        {
            int j = segment.Aux0 != 0 ? i : segment.Length - 1 - i;

            RgbColor color = segment.ColorFromPalette(j, mapping: true, wrap: segment.SolidWrap);
            segment.SetPixel(j, color);

            if (mirrored)
            {
                segment.SetPixel(
                    segment.Length - 1 - j,
                    segment.Colors[2] == RgbColor.Black ? color : segment.Colors[2]);
            }
        }

        segment.Aux1 = (uint)index;
    }
}

/// <summary>A point sweeping back and forth with a fading trail behind it.</summary>
public sealed class ScannerEffect : IWledEffect
{
    public string Name => "Scanner";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        LarsonScanner.Render(segment, now, dual: false);
    }
}

/// <summary>
/// Two points sweeping in opposite directions, which is the same effect with its mirror option
/// forced on.
/// </summary>
public sealed class DualScannerEffect : IWledEffect
{
    public string Name => "Scanner Dual";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        LarsonScanner.Render(segment, now, dual: true);
    }
}

/// <summary>
/// A point running the length of the run one way only, everything behind it fading - so it reads as
/// a beam sweeping past rather than as a dot.
/// <para>
/// It paints every LED it crossed since the last frame rather than only the one it landed on, which
/// is what keeps the beam solid when the run is long enough that a frame moves it several LEDs. The
/// return to the start is deliberately not painted, except within the first ten LEDs - so the beam
/// leaves at one end and reappears at the other rather than being dragged back.
/// </para>
/// </summary>
public sealed class LighthouseEffect : IWledEffect
{
    public string Name => "Lighthouse";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        uint counter = (now * (uint)((segment.Speed >> 2) + 1)) & 0xFFFF;
        var index = (int)((counter * (uint)segment.Length) >> 16);

        if (segment.Call == 0)
        {
            segment.Aux0 = (uint)index;
        }

        segment.FadeOut(segment.Intensity);

        segment.SetPixel(index, segment.ColorFromPalette(
            index, mapping: true, wrap: segment.SolidWrap));

        if (index > segment.Aux0)
        {
            for (int i = (int)segment.Aux0; i < index; i++)
            {
                segment.SetPixel(i, segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap));
            }
        }
        else if (index < segment.Aux0 && index < 10)
        {
            for (int i = 0; i < index; i++)
            {
                segment.SetPixel(i, segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap));
            }
        }

        segment.Aux0 = (uint)index;
    }
}
