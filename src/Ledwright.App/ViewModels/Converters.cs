using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ledwright.App.ViewModels;

public static class Converters
{
    /// <summary>Green when the live connection is up, grey when it is not.</summary>
    public static readonly IValueConverter ConnectionBrush =
        new FuncValueConverter<bool, IBrush>(connected => connected
            ? new SolidColorBrush(Color.FromRgb(82, 196, 122))
            : new SolidColorBrush(Color.FromRgb(110, 114, 124)));

    /// <summary>True when a collection is empty, for showing an empty-state message in its place.</summary>
    public static readonly IValueConverter IsEmpty = new FuncValueConverter<int, bool>(count => count == 0);

    /// <summary>True when a collection has anything in it.</summary>
    public static readonly IValueConverter IsNotEmpty = new FuncValueConverter<int, bool>(count => count > 0);

    /// <summary>
    /// Decodes the stored photo for display. The bytes are what gets written to the controllers, so
    /// what is drawn on screen is exactly what a second machine will see.
    /// </summary>
    public static readonly IValueConverter PhotoBitmap =
        new FuncValueConverter<byte[]?, Avalonia.Media.Imaging.Bitmap?>(bytes =>
        {
            if (bytes is null or { Length: 0 })
            {
                return null;
            }

            try
            {
                using var stream = new System.IO.MemoryStream(bytes);
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }
            catch
            {
                return null;
            }
        });

    /// <summary>A colour swatch from a hex string, for previewing a saved look.</summary>
    public static readonly IValueConverter HexBrush =
        new FuncValueConverter<string?, IBrush>(hex =>
        {
            if (Ledwright.Core.Models.RgbColor.TryParse(hex, out var colour))
            {
                return new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
            }

            return new SolidColorBrush(Color.FromRgb(60, 62, 70));
        });
}
