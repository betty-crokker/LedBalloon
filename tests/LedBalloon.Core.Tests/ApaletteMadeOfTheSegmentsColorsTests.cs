using LedBalloon.Core;
using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The five palettes WLED marks with an asterisk are recipes, not gradients.
/// <para>
/// They are written as placeholders — "* Colors 1&amp;2" is <c>c1, c1, c2, c2</c> — and resolved
/// against whatever the segment's color slots happen to hold. That makes them the only way to get
/// your own colors into the 22 ported effects that read no color slot at all on a real palette:
/// Colortwinkles, Aurora, both Meteors, the whole Noise family.
/// </para>
/// <para>
/// Which is also where it went wrong. Whether to offer a color box was decided entirely by what the
/// <em>effect</em> reads, so on an effect that reads none the boxes were hidden — and a palette made
/// out of those boxes had nothing to be made of.
/// </para>
/// </summary>
public class ApaletteMadeOfTheSegmentsColorsTests
{
    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor Green = new(0, 255, 0);
    private static readonly RgbColor White = new(255, 255, 255);

    /// <summary>Exactly what the controller returns for ids 1 to 5.</summary>
    private static WledPalette Recipe(params string[] placeholders) =>
        new() { Placeholders = placeholders };

    [Fact]
    public void It_says_which_slots_it_is_made_of()
    {
        Assert.Equal([0], Recipe("c1").ColorSlots);
        Assert.Equal([0, 1], Recipe("c1", "c1", "c2", "c2").ColorSlots);
        Assert.Equal([0, 1, 2], Recipe("c3", "c2", "c1").ColorSlots);
    }

    [Fact]
    public void A_real_gradient_is_made_of_nothing_of_the_sort()
    {
        var ocean = new WledPalette
        {
            Stops = [new PaletteStop(0, new RgbColor(0, 0, 255)), new PaletteStop(255, White)],
        };

        Assert.Empty(ocean.ColorSlots);
    }

    [Fact]
    public void Random_cycle_is_made_of_no_slot_either()
    {
        Assert.Empty(Recipe("r", "r", "r", "r").ColorSlots);
    }

    /// <summary>
    /// What "r" draws as, which used to be whatever the segment's primary happened to be.
    /// </summary>
    /// <remarks>
    /// A palette called Random Cycle drawn as a flat block of red is a palette claiming to be red.
    /// There is no one color to show for it, so it shows that it could be any of them.
    /// </remarks>
    [Fact]
    public void Random_cycle_draws_as_colors_rather_than_as_one_color()
    {
        WledPalette random = Recipe("r", "r", "r", "r");

        RgbColor[] across =
        [
            random.ColorAt(0.0, Red, Green, White),
            random.ColorAt(0.3, Red, Green, White),
            random.ColorAt(0.6, Red, Green, White),
            random.ColorAt(0.9, Red, Green, White),
        ];

        Assert.Equal(4, across.Distinct().Count());

        // And not simply the segment's own colors handed back under another name.
        Assert.DoesNotContain(Green, across);
    }

    [Fact]
    public void A_recipe_resolves_against_the_segments_colors()
    {
        WledPalette two = Recipe("c1", "c1", "c2", "c2");

        Assert.Equal(Red, two.ColorAt(0.1, Red, Green, White));
        Assert.Equal(Green, two.ColorAt(0.9, Red, Green, White));
    }

    [Fact]
    public void An_effect_reading_no_slot_still_leaves_the_palette_reading_two()
    {
        // The pairing the bug lived in. Colortwinkles asks the palette for everything, so on a real
        // palette no color box belongs to it - and on this palette two do, because the palette is
        // made of them.
        EffectUse twinkles = EffectUses.For("Colortwinkles")!;

        Assert.Empty(twinkles.SlotsFor(paletteId: 3));
        Assert.Equal([0, 1], Recipe("c1", "c1", "c2", "c2").ColorSlots);
    }
}
