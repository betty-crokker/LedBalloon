using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A swatch beside a segment's name is a claim about what the photo will draw, and the two sat side
/// by side disagreeing. Porchline offered a green and a magenta for an effect that reads neither,
/// and Under stairs offered a full magenta on a controller turned down to 38 of 255, where the
/// photo drew something near black.
/// <para>
/// Both come of the swatch being the color the segment has stored rather than the color anybody
/// will see. The photo has always folded the two brightnesses in and always known which slots an
/// effect reads; this is the card catching up.
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
        Brightness = segmentBrightness,
        Colors = [[0, 255, 0], [255, 0, 255]],
    };

    private static byte Red(PresetDetail d) => ((Avalonia.Media.SolidColorBrush)d.PrimarySwatch).Color.R;

    private static byte Green(PresetDetail d) => ((Avalonia.Media.SolidColorBrush)d.PrimarySwatch).Color.G;

    [Fact]
    public void A_controller_turned_down_dims_the_swatch_the_way_it_dims_the_photo()
    {
        // 38 of 255 is what North sits at. The photo multiplies by that; so does this now.
        PresetDetail full = MainViewModel.Describe(Porch, Lit(0), null, controllerBrightness: 255);
        PresetDetail dim = MainViewModel.Describe(Porch, Lit(0), null, controllerBrightness: 38);

        Assert.Equal(255, Green(full));
        Assert.Equal((byte)(255 * 38 / 255), Green(dim));
    }

    [Fact]
    public void A_segment_turned_down_dims_it_too()
    {
        PresetDetail dim = MainViewModel.Describe(
            Porch, Lit(0, segmentBrightness: 64), null, controllerBrightness: 255);

        Assert.Equal((byte)(255 * 64 / 255), Green(dim));
    }

    [Fact]
    public void And_the_two_multiply_the_way_the_photo_multiplies_them()
    {
        PresetDetail dim = MainViewModel.Describe(
            Porch, Lit(0, segmentBrightness: 128), null, controllerBrightness: 128);

        Assert.Equal((byte)(255 * (128 / 255d) * (128 / 255d)), Green(dim));
    }

    [Fact]
    public void A_brightness_nobody_stated_leaves_the_color_alone()
    {
        // Null is "no opinion", not zero. Reading it as zero would black out every swatch a scene
        // says nothing about.
        PresetDetail plain = MainViewModel.Describe(Porch, Lit(0), null);

        Assert.Equal(255, Green(plain));
        Assert.Equal(0, Red(plain));
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
}
