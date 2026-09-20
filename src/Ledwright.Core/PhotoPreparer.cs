using System.Security.Cryptography;
using SkiaSharp;

namespace Ledwright.Core;

/// <summary>What shrinking a photo produced, and whether it will fit.</summary>
/// <param name="Jpeg">The prepared bytes.</param>
/// <param name="Width">Prepared width in pixels.</param>
/// <param name="Height">Prepared height in pixels.</param>
/// <param name="Quality">JPEG quality it settled on.</param>
/// <param name="OriginalBytes">Size of what went in.</param>
/// <param name="BudgetBytes">The budget it was aiming at.</param>
public sealed record PreparedPhoto(
    byte[] Jpeg,
    int Width,
    int Height,
    int Quality,
    int OriginalBytes,
    int BudgetBytes)
{
    public bool FitsBudget => Jpeg.Length <= BudgetBytes;

    /// <summary>
    /// Content hash of the prepared bytes. Identifies the photo without anyone having to agree on
    /// where it lives, which is the only way a layout can be shared between machines.
    /// </summary>
    public string Hash => Convert.ToHexStringLower(SHA256.HashData(Jpeg))[..16];

    public override string ToString() =>
        $"{OriginalBytes / 1024} KB -> {Jpeg.Length / 1024} KB ({Width}x{Height}, q{Quality})";
}

/// <summary>
/// Shrinks a photo until it fits the space a controller has free.
/// <para>
/// The layout is stored on the controllers so a second machine needs no setup, and a photo that
/// only exists on one person's disk would break that — paths differ between machines and a path is
/// not something you can share. So the photo is scaled and re-encoded until it fits alongside the
/// layout, and the bytes travel with it. Nothing ever stores a filesystem path.
/// </para>
/// </summary>
public static class PhotoPreparer
{
    /// <summary>Longest edge to keep. Plenty for tracing runs onto a house.</summary>
    public const int DefaultMaxDimension = 1600;

    /// <summary>Quality steps to try before giving up resolution.</summary>
    private static readonly int[] QualitySteps = [82, 74, 66, 58, 50, 42, 35];

    /// <summary>
    /// Works out how many bytes a photo may occupy, given what each controller has free.
    /// <para>
    /// The smallest controller decides, because the project is mirrored to all of them, and a
    /// margin is left so a firmware update and a growing preset file still have somewhere to go.
    /// </para>
    /// </summary>
    /// <param name="freeKilobytesPerController">Free space reported by each controller.</param>
    /// <param name="headroomKilobytes">Space to leave alone.</param>
    public static int BudgetFor(IEnumerable<int> freeKilobytesPerController, int headroomKilobytes = 250)
    {
        ArgumentNullException.ThrowIfNull(freeKilobytesPerController);

        int[] free = [.. freeKilobytesPerController];
        if (free.Length == 0)
        {
            return 0;
        }

        int usable = free.Min() - headroomKilobytes;
        return Math.Max(0, usable) * 1024;
    }

    /// <summary>Scales and re-encodes <paramref name="source"/> to fit within the budget.</summary>
    /// <exception cref="InvalidOperationException">The image could not be decoded.</exception>
    public static PreparedPhoto Prepare(
        byte[] source,
        int budgetBytes,
        int maxDimension = DefaultMaxDimension)
    {
        ArgumentNullException.ThrowIfNull(source);

        using SKBitmap? decoded = SKBitmap.Decode(source)
            ?? throw new InvalidOperationException("That file could not be read as an image.");

        PreparedPhoto? smallest = null;

        // Give up quality before resolution: detail is what makes a run easy to trace accurately.
        for (int dimension = maxDimension; dimension >= 640; dimension = (int)(dimension * 0.75))
        {
            using SKBitmap scaled = Scale(decoded, dimension);

            foreach (int quality in QualitySteps)
            {
                byte[] encoded = Encode(scaled, quality);
                var candidate = new PreparedPhoto(
                    encoded, scaled.Width, scaled.Height, quality, source.Length, budgetBytes);

                if (candidate.FitsBudget)
                {
                    return candidate;
                }

                if (smallest is null || encoded.Length < smallest.Jpeg.Length)
                {
                    smallest = candidate;
                }
            }
        }

        // Nothing fit. Hand back the smallest attempt; the caller reports it rather than guessing.
        return smallest!;
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
