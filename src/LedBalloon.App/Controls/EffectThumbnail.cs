using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LedBalloon.Core;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;

namespace LedBalloon.App.Controls;

/// <summary>
/// A postage stamp of what one effect does, for the row it sits on in the effect list.
/// <para>
/// "Chunchun" is not a description and neither is "Phased Noise". The list was 187 names and no way
/// to tell any of them apart without picking one and looking at the house, which for most of the
/// year means looking at a dark house.
/// </para>
/// <para>
/// Built the same way the panel's preview is: the run across, a handful of recent frames down. A
/// single frame is no use for the same reason it was no use there — half the effects are mostly dark
/// at any instant, and a row of black tells you nothing. Sixteen rows show that Chase moves and
/// Twinklecat does not.
/// </para>
/// <para>
/// Deliberately not faithful about length. It runs at <see cref="Leds"/> whatever the segment is,
/// because 187 simulations of a 308-LED run is a wait every time the list is filled, and because a
/// thumbnail is a way of telling effects apart rather than a preview. The strip at the top of the
/// editor is the preview, and that one is faithful.
/// </para>
/// </summary>
public static class EffectThumbnail
{
    public const int Width = 72;
    public const int Height = 18;

    /// <summary>How long a run to characterise the effect on. Short enough that 187 of them are free.</summary>
    private const int Leds = 48;

    /// <summary>The unlit strip, matching the preview so the two read as the same kind of picture.</summary>
    private const uint Unlit = 0xFF101116;

    /// <summary>
    /// False once it is known there is nowhere to draw: a test host, or anything running the view
    /// models without a rendering platform behind them.
    /// </summary>
    /// <remarks>
    /// Remembered rather than discovered again per effect, because the list asks for 187 of these in
    /// a row and one caught exception is enough to learn from. A row without its picture is a row
    /// with its name on it, which is where the list started.
    /// </remarks>
    private static bool _drawable = true;

    /// <summary>
    /// One effect as this segment would run it, or null when the effect is not one the app can run.
    /// </summary>
    /// <param name="effect">The effect's index on the controller reporting it.</param>
    /// <param name="names">That controller's effect list, since effects resolve by name.</param>
    /// <param name="like">
    /// The segment's own palette, colors and sliders, so the row shows what picking this effect
    /// would actually do here rather than what it does with somebody else's colors.
    /// </param>
    public static WriteableBitmap? For(
        int effect,
        IReadOnlyList<string>? names,
        WledSegment like,
        WledPalette? palette,
        ControllerTiming timing)
    {
        ArgumentNullException.ThrowIfNull(like);

        if (!_drawable)
        {
            return null;
        }

        var wled = new WledSegment
        {
            Id = like.Id,
            Effect = effect,
            Palette = like.Palette,
            Speed = like.Speed,
            Intensity = like.Intensity,
            Colors = like.Colors,
        };

        EffectSimulation? sim = EffectLibrary.Simulate(
            wled, Leds, names, palette, timing.IntervalMilliseconds, timing.FrameTimeMilliseconds);

        if (sim is null)
        {
            return null;
        }

        var rows = new int[Width * Height];
        double span = 1d / Width;

        for (int row = Height - 1; row >= 0; row--)
        {
            // Advanced by a frame between rows, so the stamp shows motion rather than one instant
            // eighteen times. Newest at the top, the same way round as the preview.
            sim.Advance(33);

            RgbColor[] pixels = sim.Segment.Pixels;
            int last = pixels.Length - 1;

            for (int column = 0; column < Width; column++)
            {
                double t = (column + 0.5) * span;

                int from = Math.Clamp((int)((t - (span / 2)) * last), 0, last);
                int to = Math.Clamp((int)Math.Ceiling((t + (span / 2)) * last), from, last);

                int r = 0, g = 0, b = 0;
                for (int i = from; i <= to; i++)
                {
                    RgbColor pixel = sim.Segment.AsWired(i);

                    r += pixel.R;
                    g += pixel.G;
                    b += pixel.B;
                }

                int count = to - from + 1;
                r /= count;
                g /= count;
                b /= count;

                rows[(row * Width) + column] = r + g + b == 0
                    ? unchecked((int)Unlit)
                    : unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
            }
        }

        try
        {
            var image = new WriteableBitmap(
                new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

            using (ILockedFramebuffer buffer = image.Lock())
            {
                // Row by row, because a locked framebuffer may pad its rows.
                for (int row = 0; row < Height; row++)
                {
                    Marshal.Copy(rows, row * Width, buffer.Address + (row * buffer.RowBytes), Width);
                }
            }

            return image;
        }
        catch (InvalidOperationException)
        {
            // Avalonia has no render interface, so there is nothing to make a bitmap with. Losing a
            // picture is not a reason to fail filling the list it was going in.
            _drawable = false;
            return null;
        }
    }
}
