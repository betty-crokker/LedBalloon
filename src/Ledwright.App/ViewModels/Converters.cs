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
