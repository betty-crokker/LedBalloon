using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A flock of dots sweeping back and forth along the run, each trailing behind it.
/// <para>
/// Ported from WLED 0.15.3's <c>mode_chunchun</c>, which is unchanged back to 0.12. The name says
/// nothing, and a still photo of it says less, so: the count comes from the run's length, the
/// sweep from its speed, how strung out the flock is from its intensity, and the colors from
/// stepping through the palette. On a three hundred LED porch at speed 172 that is forty dots
/// crossing every four seconds or so, spread over half a sweep, each with a trail about a fifth
/// of a second long.
/// </para>
/// <para>
/// It is a good first effect to port because it needs both halves of the machinery. Where the dots
/// are depends only on the clock, so it looks the same on any controller. How long the trails are
/// depends on how many frames get drawn, so it does not.
/// </para>
/// </summary>
public sealed class ChunchunEffect : IWledEffect
{
    public string Name => "Chunchun";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            // WLED hands a one-LED run to Solid rather than trying to fly a flock on it.
            segment.Fill(segment.Colors[0]);
            return;
        }

        // Everything still lit dims toward the secondary color, which is the trail.
        segment.FadeOut(254);

        uint counter;
        unchecked
        {
            counter = now * (uint)(6 + (segment.Speed >> 4));
        }

        int birds = 2 + (segment.Length >> 3);
        var span = (uint)((segment.Intensity << 8) / birds);

        for (int i = 0; i < birds; i++)
        {
            // Each dot is a little further back round the sweep than the one before it, so the
            // flock strings out instead of moving as one.
            unchecked
            {
                counter -= span;
            }

            // Sine over the run, shifted from signed to unsigned so it spans the whole length.
            int position = FastLed.Sin16((ushort)counter) + 0x8000;

            uint at;
            unchecked
            {
                at = (uint)(position * segment.Length) >> 16;
            }

            RgbColor color = segment.ColorFromPalette(i * 255 / birds);

            segment.SetPixel(Math.Clamp((int)at, 0, segment.Length - 1), color);
        }
    }
}
