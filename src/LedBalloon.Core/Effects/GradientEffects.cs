using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A soft blob of the primary color travelling along the run over the palette, and the same blob
/// with one edge cut off square.
/// <para>
/// Gradient measures each LED's distance to the blob the short way round the run, so the blob wraps
/// and both its edges fade. Loading measures it backwards only, so the blob has a hard leading edge
/// and a long tail - which is what makes it read as a progress bar rather than as a wave.
/// </para>
/// </summary>
internal static class GradientRun
{
    public static void Render(EffectSegment segment, uint now, bool loading)
    {
        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        var counter = (ushort)(now * (uint)((segment.Speed >> 2) + 1));
        var at = (int)(((uint)counter * (uint)segment.Length) >> 16);

        if (segment.Call == 0)
        {
            at = 0;
        }

        // WLED writes this as `1 + loading ? intensity/2 : intensity/4`, which C parses as
        // `(1 + loading) ? ... : ...` - always true, so the second branch is dead and both effects
        // get intensity/2. Reproduced rather than corrected: Loading looks the way it looks on the
        // wall because of this, and a "fixed" preview would be the one that disagreed.
        int spread = segment.Intensity / 2;

        int behind = at - segment.Length;
        int ahead = at + segment.Length;

        for (int i = 0; i < segment.Length; i++)
        {
            int distance = loading
                ? Math.Abs((i > at ? ahead : at) - i)
                : Math.Min(Math.Abs(at - i), Math.Min(Math.Abs(behind - i), Math.Abs(ahead - i)));

            // Zero at the middle of the blob and 255 once past its edge. The guard runs first, so a
            // spread of nothing never divides by zero.
            int mix = spread > distance ? distance * 255 / spread : 255;

            segment.Pixels[i] = EffectSegment.Blend(
                segment.Colors[0],
                segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1),
                (byte)mix);
        }
    }
}

/// <summary>A blob of the primary color sliding round the run, fading off at both edges.</summary>
public sealed class GradientEffect : IWledEffect
{
    public string Name => "Gradient";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        GradientRun.Render(segment, now, loading: false);
    }
}

/// <summary>The same blob with a hard leading edge and a tail behind it, like a bar filling.</summary>
public sealed class LoadingEffect : IWledEffect
{
    public string Name => "Loading";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        GradientRun.Render(segment, now, loading: true);
    }
}
