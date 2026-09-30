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

    /// <summary>
    /// The case the slider half of this exists for.
    /// </summary>
    /// <remarks>
    /// Solid's fxdata entry is the empty string on both controllers here. WLED's own UI reads an
    /// empty entry as "no opinion" - <c>!t &amp;&amp; n &lt; 2</c> in setEffectParameters - and
    /// answers by showing the first two sliders, so Solid is offered a speed and an intensity that
    /// its four lines never look at. Its code is the better witness.
    /// </remarks>
    [Fact]
    public void Solid_reads_neither_slider()
    {
        EffectUse use = PortedEffectUse.For("Solid")!;

        Assert.False(use.UsesSpeed);
        Assert.False(use.UsesIntensity);
    }

    [Fact]
    public void An_effect_with_one_slider_says_which()
    {
        // Both corroborated by the firmware, which declares exactly the same thing: Breathe is
        // "!;!,!;!;01" - a speed and no intensity - and Fireworks is ",Frequency;!,!;!;12", an
        // intensity named Frequency and no speed.
        Assert.True(PortedEffectUse.For("Breathe")!.UsesSpeed);
        Assert.False(PortedEffectUse.For("Breathe")!.UsesIntensity);

        Assert.False(PortedEffectUse.For("Fireworks")!.UsesSpeed);
        Assert.True(PortedEffectUse.For("Fireworks")!.UsesIntensity);
    }

    [Fact]
    public void A_slider_the_firmware_forgot_to_declare_is_still_offered()
    {
        // Wipe Random's fxdata is "!;;!" - no intensity slider - but the wipe it shares with Sweep
        // divides its remainder by exactly that, for every one of its callers. fxdata is wrong in
        // both directions, and this is the direction that costs somebody a control that works.
        Assert.True(PortedEffectUse.For("Wipe Random")!.UsesIntensity);
        Assert.True(PortedEffectUse.For("Sweep Random")!.UsesIntensity);
    }

    [Fact]
    public void Nearly_everything_reads_both()
    {
        // A guard on the extractor rather than on the effects. It follows calls into the shared
        // helper classes a dozen effects delegate to, and an earlier version keyed them by bare
        // method name - so every effect that called Render() was handed every other effect's body
        // and all 118 came back reading everything.
        Assert.Equal(114, Counted(u => u.UsesSpeed));
        Assert.Equal(101, Counted(u => u.UsesIntensity));
    }

    private static int Counted(Func<EffectUse, bool> match) =>
        EffectLibrary.All.Count(e => PortedEffectUse.For(e.Name) is { } use && match(use));

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
