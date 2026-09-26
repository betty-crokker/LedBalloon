using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Evenly spaced pools of light along the run, each one a soft-edged triangle wave over the
/// background.
/// <para>
/// Intensity sets how many pools there are - up to one per four LEDs - and the run is divided into
/// equal zones, with whatever will not divide evenly split between the two ends. A threshold clips
/// the triangle: raise it and each pool narrows to a point, lower it and the pools spread until
/// they meet.
/// </para>
/// <para>
/// The two effects differ only in where the threshold comes from. Spots takes it from the speed
/// slider and holds it still, so the pools are a fixed size. Spots Fade drives it from a triangle
/// wave of its own, so they breathe.
/// </para>
/// </summary>
internal static class Spots
{
    public static void Render(EffectSegment segment, int threshold)
    {
        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        int mostZones = segment.Length >> 2;
        int zones = 1 + ((segment.Intensity * mostZones) >> 8);
        int zoneLength = segment.Length / zones;

        // What will not divide evenly is split between the two ends, so the pools stay centered.
        int offset = (segment.Length - (zones * zoneLength)) >> 1;

        for (int z = 0; z < zones; z++)
        {
            int start = offset + (z * zoneLength);

            for (int i = 0; i < zoneLength; i++)
            {
                ushort wave = FastLed.TriWave16((ushort)(i * 0xFFFF / zoneLength));

                if (wave <= threshold)
                {
                    continue;
                }

                int index = start + i;

                // Whatever clears the threshold is rescaled over what is left of the range, so a
                // pool fades to nothing at its edge however narrow the threshold has made it.
                int lit = (wave - threshold) * 255 / (0xFFFF - threshold);

                segment.Pixels[index] = EffectSegment.Blend(
                    segment.ColorFromPalette(index, mapping: true, wrap: segment.SolidWrap),
                    segment.Colors[1],
                    (byte)(255 - lit));
            }
        }
    }
}

/// <summary>Fixed pools of light, sized by the speed slider rather than moved by it.</summary>
public sealed class SpotsEffect : IWledEffect
{
    public string Name => "Spots";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Spots.Render(segment, (255 - segment.Speed) << 8);
    }
}

/// <summary>The same pools breathing in and out together.</summary>
public sealed class SpotsFadeEffect : IWledEffect
{
    public string Name => "Spots Fade";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var counter = (ushort)(now * (uint)((segment.Speed >> 2) + 8));
        ushort wave = FastLed.TriWave16(counter);

        // Three quarters of the swing, so the pools narrow almost to nothing but never quite go out.
        Spots.Render(segment, (wave >> 1) + (wave >> 2));
    }
}

/// <summary>
/// Two dots chasing round the run half a length apart, which is the police-light pattern with the
/// colors left to the user.
/// <para>
/// WLED still has the red-and-blue version in its source, commented out - what is left is this one,
/// where the two dots take the first two color slots and the third is the background. Intensity is
/// the dot size, up to half the run.
/// </para>
/// </summary>
public sealed class TwoDotsEffect : IWledEffect
{
    public string Name => "Two Dots";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[2]);
        }

        // A second dot the same color as the background would be invisible, so it falls back to the
        // primary rather than quietly leaving one dot.
        RgbColor second = segment.Colors[1] == segment.Colors[2]
            ? segment.Colors[0]
            : segment.Colors[1];

        // Longer runs step faster, so the dots take about the same time to go round whatever the
        // length - which is why the frame time is in here at all.
        int step = 1 + ((segment.FrameTime << 3) / segment.Length);

        uint at = now / (uint)FastLed.Map(segment.Speed, 0, 255, step << 4, step);
        var offset = (int)(at % (uint)segment.Length);

        int width = (segment.Length * (segment.Intensity + 1)) >> 9;

        if (width == 0)
        {
            width = 1;
        }

        for (int i = 0; i < width; i++)
        {
            segment.SetPixel((offset + i) % segment.Length, segment.Colors[0]);
            segment.SetPixel((offset + i + (segment.Length >> 1)) % segment.Length, second);
        }
    }
}
