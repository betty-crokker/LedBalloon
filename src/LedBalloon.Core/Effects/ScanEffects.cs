using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A block of the palette running the length of the run and back, on a background of the secondary
/// color.
/// <para>
/// Both Scan and Scan Dual come from here, and the only difference is whether a mirror image runs
/// the other way at the same time. A block rather than a dot: the slider WLED labels "# of dots"
/// actually sets its width, and at the middle of the slider that is a quarter of the run.
/// </para>
/// <para>
/// The block draws the palette at the position it has reached rather than carrying its own color
/// along, so on a rainbow the block changes color as it travels rather than sliding a fixed color
/// across a fixed gradient.
/// </para>
/// </summary>
internal static class Scan
{
    public static void Render(EffectSegment segment, uint now, bool dual)
    {
        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        uint cycleTime = 750u + ((255u - segment.Speed) * 150u);
        uint into = now % cycleTime;
        var progress = (int)(into * 65535 / cycleTime);

        int size = 1 + ((segment.Intensity * segment.Length) >> 9);

        // Travels twice the run's length less the block, so the block's near edge turns round at the
        // far end rather than the block running off it.
        int at = (progress * ((segment.Length * 2) - (size * 2))) >> 16;

        // Folded rather than wrapped, which is what makes the return leg a return rather than a
        // restart.
        int offset = Math.Abs(at - (segment.Length - size));

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        if (dual)
        {
            // The third color slot picks the mirrored block's color, and where it is unset both
            // blocks come from the primary.
            int slot = segment.Colors[2] == RgbColor.Black ? 0 : 2;

            for (int j = offset; j < offset + size; j++)
            {
                int mirrored = segment.Length - 1 - j;

                segment.SetPixel(mirrored, segment.ColorFromPalette(
                    mirrored, mapping: true, wrap: segment.SolidWrap, colorSlot: slot));
            }
        }

        for (int j = offset; j < offset + size; j++)
        {
            segment.SetPixel(j, segment.ColorFromPalette(
                j, mapping: true, wrap: segment.SolidWrap));
        }
    }
}

/// <summary>One block of the palette pacing back and forth along the run.</summary>
public sealed class ScanEffect : IWledEffect
{
    public string Name => "Scan";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Scan.Render(segment, now, dual: false);
    }
}

/// <summary>
/// Two blocks pacing back and forth in opposite directions, so they meet in the middle and part
/// again.
/// </summary>
public sealed class DualScanEffect : IWledEffect
{
    public string Name => "Scan Dual";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Scan.Render(segment, now, dual: true);
    }
}
