using Avalonia.Media;
using LedBalloon.App.ViewModels;
using LedBalloon.Core;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What colour to put beside a segment's name, for a list that is both a description of what the
/// segment is showing and a picker for what it should show.
/// <para>
/// The first attempt at this dimmed the swatch by the controller's brightness, so that it matched
/// the photo. It is a picker: North sits at 38 of 255, which turned every row in the list the same
/// near-black and left no two of them distinguishable. How dim the house is belongs to the slider
/// above, which says it once for all of them, and to the photo, which shows it.
/// </para>
/// <para>
/// What the swatch does have to get right is which colours the effect reads at all, and the palette
/// it draws from when it draws from one — which is most of them.
/// </para>
/// </summary>
public class SwatchesMatchThePhotoTests
{
    private static readonly Segment Porch = new()
    {
        Id = "porch",
        Name = "Porch",
        ControllerKey = FakeController.Key,
        Output = 1,
        Count = 5,
    };

    private static WledSegment Lit(int effect, byte? segmentBrightness = null) => new()
    {
        Id = 0,
        On = true,
        Effect = effect,
        Palette = 1,
        Brightness = segmentBrightness,
        Colors = [[0, 255, 0], [255, 0, 255]],
    };

    private static byte Green(PresetDetail d) => ((SolidColorBrush)d.PrimarySwatch).Color.G;

    /// <summary>A palette of its own, so the gradient does not depend on a controller answering.</summary>
    private static IReadOnlyDictionary<int, WledPalette> Palettes() =>
        new Dictionary<int, WledPalette>
        {
            [1] = new()
            {
                Stops =
                [
                    new PaletteStop(0, new RgbColor(255, 0, 0)),
                    new PaletteStop(255, new RgbColor(0, 0, 255)),
                ],
            },
        };

    [Fact]
    public void A_controller_turned_right_down_does_not_darken_the_swatch()
    {
        // The reversal, pinned. Dimming these made every row on North the same near-black, and a
        // list you cannot tell apart is not a picker.
        PresetDetail full = MainViewModel.Describe(Porch, Lit(0), null, controllerBrightness: 255);
        PresetDetail dim = MainViewModel.Describe(Porch, Lit(0), null, controllerBrightness: 38);

        Assert.Equal(255, Green(full));
        Assert.Equal(255, Green(dim));
    }

    [Fact]
    public void Nor_does_a_segment_turned_right_down()
    {
        PresetDetail dim = MainViewModel.Describe(
            Porch, Lit(0, segmentBrightness: 8), null, controllerBrightness: 255);

        Assert.Equal(255, Green(dim));
    }

    [Fact]
    public void An_effect_that_declares_nothing_still_shows_its_colors()
    {
        // No device, so no metadata, so nothing is known - and "we do not know" must not read as
        // "it uses none", which would blank the swatch on every effect for an unreachable box.
        PresetDetail plain = MainViewModel.Describe(Porch, Lit(0), null);

        Assert.True(plain.HasPrimary);
        Assert.True(plain.HasSecondary);
    }

    [Fact]
    public void And_gets_no_gradient_either()
    {
        // It may well ignore the palette - Solid does - and a gradient it ignores is a picture
        // rather than a control. A wrong picture is worse than none.
        PresetDetail plain = MainViewModel.Describe(Porch, Lit(0), null, palettes: Palettes());

        Assert.Null(plain.PaletteSwatch);
    }

    [Fact]
    public void A_palette_nobody_has_the_gradient_for_draws_none()
    {
        PresetDetail plain = MainViewModel.Describe(Porch, Lit(0), null, palettes: null);

        Assert.Null(plain.PaletteSwatch);
    }
}
