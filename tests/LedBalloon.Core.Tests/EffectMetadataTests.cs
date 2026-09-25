using LedBalloon.Core.Effects;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Every string here was read off a real controller, so these are a record of the format as the
/// firmware actually emits it rather than as anyone remembers it.
/// </summary>
public class EffectMetadataTests
{
    /// <summary>
    /// The distinction the first version of this got wrong. Oscillate ships no metadata and WLED's
    /// own UI therefore offers everything; Stream 2 ships "!;;" and declares both unused. Reading
    /// a missing section as an empty one made Oscillate look like it ignored the palette.
    /// </summary>
    [Fact]
    public void An_effect_with_no_metadata_offers_everything()
    {
        EffectMetadata oscillate = EffectMetadata.Parse("");

        Assert.False(oscillate.Declared);
        Assert.True(oscillate.UsesPalette);
        Assert.Equal([1, 2, 3], oscillate.UsedSlots);
    }

    [Fact]
    public void An_effect_declaring_empty_sections_uses_neither()
    {
        EffectMetadata streamTwo = EffectMetadata.Parse("!;;");

        Assert.True(streamTwo.Declared);
        Assert.False(streamTwo.UsesPalette);
        Assert.Empty(streamTwo.UsedSlots);
    }

    [Fact]
    public void A_palette_only_effect_offers_no_color_slots()
    {
        // Pacifica, which also ships the palette it was designed around.
        EffectMetadata pacifica = EffectMetadata.Parse("!,Angle;;!;;pal=51");

        Assert.True(pacifica.PaletteOnly);
        Assert.Empty(pacifica.UsedSlots);
        Assert.Equal("Palette", pacifica.PaletteLabel);
        Assert.Equal(["!", "Angle"], pacifica.Sliders);
    }

    [Fact]
    public void A_single_color_effect_offers_one_slot()
    {
        EffectMetadata bpm = EffectMetadata.Parse("!;!;!;;sx=64");

        Assert.Equal([1], bpm.UsedSlots);
        Assert.Equal("Color", bpm.ColorSlots[0]);
        Assert.True(bpm.UsesPalette);
    }

    /// <summary>
    /// Several effects name their slots, and the name is the only documentation there is. Solid
    /// Glitter uses slots 1 and 3 and skips 2, which is why "show the first N swatches" is wrong.
    /// </summary>
    [Fact]
    public void A_named_slot_keeps_the_name_the_effect_gave_it()
    {
        EffectMetadata glitter = EffectMetadata.Parse(",!;Bg,,Glitter color;;;m12=0");

        Assert.Equal([1, 3], glitter.UsedSlots);
        Assert.Equal("Bg", glitter.ColorSlots[0]);
        Assert.Null(glitter.ColorSlots[1]);
        Assert.Equal("Glitter color", glitter.ColorSlots[2]);
        Assert.False(glitter.UsesPalette);
    }

    [Fact]
    public void An_effect_can_use_only_the_third_slot()
    {
        EffectMetadata plain = EffectMetadata.Parse("!,!,,,,,Overlay;,,Glitter color;!;;pal=0,m12=0");

        Assert.Equal([3], plain.UsedSlots);
        Assert.Equal("Glitter color", plain.ColorSlots[2]);
    }

    /// <summary>One effect renames the palette picker rather than using the default label.</summary>
    [Fact]
    public void An_effect_can_rename_the_palette_picker()
    {
        EffectMetadata akemi = EffectMetadata.Parse(
            "Color speed,Dance;Head palette,Arms & Legs,Eyes & Mouth;Face palette;;m12=2");

        Assert.Equal("Face palette", akemi.PaletteLabel);
        Assert.Equal("Arms & Legs", akemi.ColorSlots[1]);
    }

    /// <summary>
    /// A number in the palette section is not a label. WLED's own UI excludes it with isNaN(), and
    /// taking it for one would put a stray digit where the picker's name goes.
    /// </summary>
    [Fact]
    public void A_number_in_the_palette_section_is_not_a_label()
    {
        EffectMetadata numbered = EffectMetadata.Parse("!;!,!;0");

        Assert.False(numbered.UsesPalette);
    }

    [Fact]
    public void Parsing_the_whole_list_lines_up_with_the_effect_list()
    {
        IReadOnlyList<EffectMetadata> all = EffectMetadata.ParseAll(["", "!;!,!;!;01", "!,Zone size;;!"]);

        Assert.Equal(3, all.Count);
        Assert.False(all[0].Declared);
        Assert.Equal([1, 2], all[1].UsedSlots);
        Assert.True(all[2].PaletteOnly);
    }
}
