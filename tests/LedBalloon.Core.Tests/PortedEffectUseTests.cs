using LedBalloon.Core.Effects;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// What each ported effect reads, taken from the ports instead of from the firmware's own metadata.
/// <para>
/// fxdata is hand-maintained and over-declares. It says Twinklecat reads two color slots; the code
/// reads one — the background — and takes the twinkles from the palette, so the second box was
/// offered for an effect that never looks at it. That is the case this exists to get right, and
/// these are the readings that were checked by hand against the source rather than extracted.
/// </para>
/// </summary>
public class PortedEffectUseTests
{
    [Fact]
    public void Every_ported_effect_has_been_read()
    {
        // The audit covers the library. If a new effect is ported and not read, this says so rather
        // than letting it quietly fall back to the firmware's guess.
        Assert.Equal(EffectLibrary.PortedCount, PortedEffectUse.Count);
    }

    [Fact]
    public void An_effect_nobody_has_ported_is_not_claimed()
    {
        Assert.Null(PortedEffectUse.For("Akemi"));
        Assert.Null(PortedEffectUse.For(""));
        Assert.Null(PortedEffectUse.For(null));
    }

    [Fact]
    public void Twinklecat_reads_the_background_and_the_palette_but_not_the_color()
    {
        // The whole reason for this table. RgbColor background = segment.Colors[1], and the
        // twinkles come from segment.ColorFromPalette - so slot 0 is only ever reached when the
        // palette is Default and that call falls back to it.
        EffectUse use = PortedEffectUse.For("Twinklecat")!;

        Assert.Equal([1], use.Direct);
        Assert.True(use.UsesPalette);

        Assert.Equal([1], use.SlotsFor(paletteId: 11));   // a real palette: the color box goes
        Assert.Equal([0, 1], use.SlotsFor(paletteId: 0)); // Default: the palette IS slot 0
    }

    [Fact]
    public void Solid_reads_one_color_and_no_palette()
    {
        EffectUse use = PortedEffectUse.For("Solid")!;

        Assert.Equal([0], use.Direct);
        Assert.False(use.UsesPalette);
        Assert.Equal([0], use.SlotsFor(paletteId: 11));
    }

    [Fact]
    public void Aurora_reads_whichever_slot_it_picks_at_random()
    {
        // colorSlot: segment.Random8(0, 3) - one of the three, decided per wave. Computed rather
        // than named, so this one was read by hand.
        EffectUse use = PortedEffectUse.For("Aurora")!;

        Assert.Empty(use.Direct);
        Assert.Equal([0, 1, 2], use.SlotsFor(paletteId: 0));
        Assert.Empty(use.SlotsFor(paletteId: 11));
    }

    [Fact]
    public void Colortwinkles_reads_no_color_of_its_own()
    {
        EffectUse use = PortedEffectUse.For("Colortwinkles")!;

        Assert.Empty(use.Direct);
        Assert.True(use.UsesPalette);
        Assert.Empty(use.SlotsFor(paletteId: 11));
    }

    [Fact]
    public void Scan_reads_the_slot_it_chooses_between()
    {
        // int slot = segment.Colors[2] == RgbColor.Black ? 0 : 2 - and Colors[1] fills the
        // background outright. Read by hand, the slot being computed.
        EffectUse use = PortedEffectUse.For("Scan")!;

        Assert.Equal([1, 2], use.Direct);
        Assert.Equal([0, 1, 2], use.SlotsFor(paletteId: 0));
    }

    [Fact]
    public void An_effect_that_never_asks_the_palette_says_so()
    {
        // Two Dots draws from its slots and nothing else, so the palette picker has no business
        // being offered for it.
        EffectUse use = PortedEffectUse.For("Two Dots")!;

        Assert.False(use.UsesPalette);
        Assert.NotEmpty(use.Direct);
    }

    [Fact]
    public void A_slot_read_outright_survives_any_palette()
    {
        // The distinction the whole table turns on: Direct is unconditional, WhenPaletteIsDefault
        // is not. Every effect that reads a slot outright must keep reading it on a real palette.
        foreach (string name in new[] { "Twinklecat", "Solid", "Scan", "Two Dots" })
        {
            EffectUse use = PortedEffectUse.For(name)!;

            Assert.All(use.Direct, slot => Assert.Contains(slot, use.SlotsFor(paletteId: 11)));
        }
    }
}
