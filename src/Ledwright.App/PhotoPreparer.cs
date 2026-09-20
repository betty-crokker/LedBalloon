using System;
using SkiaSharp;

namespace Ledwright.App;

/// <summary>
/// Shrinks a photo until it will fit on a controller's flash.
/// <para>
/// The whole point of storing the project on the controllers is that a second machine needs no
/// setup at all — so the photo has to travel with it. A phone photo is several megabytes and the
/// filesystem has under a megabyte free, so it is scaled down and re-encoded as JPEG before it goes
/// anywhere. What you see on screen is the prepared version, so the preview never flatters the
/// thing that actually gets stored.
/// </para>
/// </summary>
public static class PhotoPreparer
{
    /// <summary>Longest edge to keep. Plenty for drawing runs onto a house.</summary>
    private const int DefaultMaxDimension = 1600;

    /// <summary>Quality steps to try before giving up resolution.</summary>
    private static readonly int[] QualitySteps = [82, 70, 60, 50, 40];

    /// <summary>
    /// Scales and re-encodes <paramref name="source"/> to fit within <paramref name="budgetBytes"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The image could not be decoded.</exception>
    public static byte[] ToStoredJpeg(
        byte[] source,
        int budgetBytes,
        int maxDimension = DefaultMaxDimension)
    {
        ArgumentNullException.ThrowIfNull(source);

        using SKBitmap? decoded = SKBitmap.Decode(source)
            ?? throw new InvalidOperationException("That file could not be read as an image.");

        byte[]? best = null;

        // Drop quality first, because resolution is what makes a run easy to trace accurately.
        for (int dimension = maxDimension; dimension >= 640; dimension /= 2)
        {
            using SKBitmap scaled = Scale(decoded, dimension);

            foreach (int quality in QualitySteps)
            {
                byte[] encoded = Encode(scaled, quality);
                best ??= encoded;

                if (encoded.Length <= budgetBytes)
                {
                    return encoded;
                }

                best = encoded;
            }
        }

        // Nothing fit; hand back the smallest attempt and let the caller report it.
        return best ?? Encode(decoded, QualitySteps[^1]);
    }

    private static SKBitmap Scale(SKBitmap source, int maxDimension)
    {
        int longest = Math.Max(source.Width, source.Height);
        if (longest <= maxDimension)
        {
            return source.Copy();
        }

        double factor = maxDimension / (double)longest;
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * factor)),
            Math.Max(1, (int)Math.Round(source.Height * factor)));

        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
               ?? source.Copy();
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }
}
