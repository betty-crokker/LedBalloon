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
[Collection(EngineCollection.Name)]
public class EffectUsesTests
{
    [Fact]
    public void Every_effect_that_has_been_read_is_one_the_engine_offers()
    {
        // This used to assert that the table covered every ported effect, one entry per port. The
        // ports are gone, so the invariant turns around: the table is a reading of WLED's source and
        // what it must not contain is an effect that does not exist. A name that has drifted - and
        // WLED does rename them - would otherwise sit here describing nothing.
        string[] unknown = [.. EffectUses.Names.Where(name => EffectLibrary.Find(name) is null)];

        Assert.Empty(unknown);
    }

    [Fact]
    public void An_effect_nobody_has_ported_is_not_claimed()
    {
        Assert.Null(EffectUses.For("Akemi"));
        Assert.Null(EffectUses.For(""));
        Assert.Null(EffectUses.For(null));
    }

    [Fact]
    public void Twinklecat_reads_the_background_and_the_palette_but_not_the_color()
    {
        // The whole reason for this table. RgbColor background = segment.Colors[1], and the
        // twinkles come from segment.ColorFromPalette - so slot 0 is only ever reached when the
        // palette is Default and that call falls back to it.
        EffectUse use = EffectUses.For("Twinklecat")!;

        Assert.Equal([1], use.Direct);
        Assert.True(use.UsesPalette);

        Assert.Equal([1], use.SlotsFor(paletteId: 11));   // a real palette: the color box goes
        Assert.Equal([0, 1], use.SlotsFor(paletteId: 0)); // Default: the palette IS slot 0
    }

    [Fact]
    public void Solid_reads_one_color_and_no_palette()
    {
        EffectUse use = EffectUses.For("Solid")!;

        Assert.Equal([0], use.Direct);
        Assert.False(use.UsesPalette);
        Assert.Equal([0], use.SlotsFor(paletteId: 11));
    }

    [Fact]
    public void Aurora_reads_whichever_slot_it_picks_at_random()
    {
        // colorSlot: segment.Random8(0, 3) - one of the three, decided per wave. Computed rather
        // than named, so this one was read by hand.
        EffectUse use = EffectUses.For("Aurora")!;

        Assert.Empty(use.Direct);
        Assert.Equal([0, 1, 2], use.SlotsFor(paletteId: 0));
        Assert.Empty(use.SlotsFor(paletteId: 11));
    }

    [Fact]
    public void Colortwinkles_reads_no_color_of_its_own()
    {
        EffectUse use = EffectUses.For("Colortwinkles")!;

        Assert.Empty(use.Direct);
        Assert.True(use.UsesPalette);
        Assert.Empty(use.SlotsFor(paletteId: 11));
    }

    /// <summary>
    /// Scan and Scan Dual share a body and do not read the same thing, which reading it could not
    /// tell.
    /// </summary>
    /// <remarks>
    /// The line that chooses between color slots - <c>Colors[2] == Black ? 0 : 2</c> - is inside
    /// <c>if (dual)</c>, and only Scan Dual passes dual. Following the call and reading what it
    /// contains credited plain Scan with a slot it never reaches, the same way Running was credited
    /// with Running Dual's. Running it instead tells them apart.
    /// </remarks>
    [Fact]
    public void Scan_and_scan_dual_do_not_read_the_same_slots()
    {
        EffectUse scan = EffectUses.For("Scan")!;

        Assert.Equal([1], scan.Direct);
        Assert.Equal([0, 1], scan.SlotsFor(paletteId: 0));

        // Its twin takes the branch, so the third slot is live there - and only while the palette is
        // Default, because on a real one that call comes back as the gradient.
        EffectUse dual = EffectUses.For("Scan Dual")!;

        Assert.Contains(2, dual.SlotsFor(paletteId: 0));
        Assert.DoesNotContain(2, dual.SlotsFor(paletteId: 11));
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
        EffectUse use = EffectUses.For("Solid")!;

        Assert.False(use.UsesSpeed);
        Assert.False(use.UsesIntensity);
    }

    [Fact]
    public void An_effect_with_one_slider_says_which()
    {
        // Both corroborated by the firmware, which declares exactly the same thing: Breathe is
        // "!;!,!;!;01" - a speed and no intensity - and Fireworks is ",Frequency;!,!;!;12", an
        // intensity named Frequency and no speed.
        Assert.True(EffectUses.For("Breathe")!.UsesSpeed);
        Assert.False(EffectUses.For("Breathe")!.UsesIntensity);

        Assert.False(EffectUses.For("Fireworks")!.UsesSpeed);
        Assert.True(EffectUses.For("Fireworks")!.UsesIntensity);
    }

    [Fact]
    public void A_slider_the_firmware_forgot_to_declare_is_still_offered()
    {
        // Wipe Random's fxdata is "!;;!" - no intensity slider - but the wipe it shares with Sweep
        // divides its remainder by exactly that, for every one of its callers. fxdata is wrong in
        // both directions, and this is the direction that costs somebody a control that works.
        Assert.True(EffectUses.For("Wipe Random")!.UsesIntensity);
        Assert.True(EffectUses.For("Sweep Random")!.UsesIntensity);
    }

    [Fact]
    public void Nearly_everything_reads_both()
    {
        // A guard on the measurement rather than on the effects. The numbers moved twice while it
        // was being built: too few frames and Halloween Eyes shut its eyes through the whole run and
        // reported both sliders unused, and sampling every seventh frame caught Strobe dark in both
        // runs and reported it using nothing at all.
        Assert.Equal(112, Counted(u => u.UsesSpeed));
        Assert.Equal(96, Counted(u => u.UsesIntensity));
    }

    private static int Counted(Func<EffectUse, bool> match) =>
        EffectLibrary.All.Count(e => EffectUses.For(e.Name) is { } use && match(use));

    /// <summary>
    /// The hand-read entry that was read wrong, and how it showed.
    /// </summary>
    /// <remarks>
    /// Both Meteors pass 255 as the color slot in one of their two branches. 255 is out of range, and
    /// color_from_palette only substitutes the slot for the gradient when the slot is a real one - so
    /// neither of them ever reads a color on a real palette. The table said they read slot 0 outright,
    /// which put a red color box over a meteor drawn entirely in Ocean.
    /// </remarks>
    [Fact]
    public void A_meteor_reads_its_color_only_while_the_palette_is_default()
    {
        foreach (string name in new[] { "Meteor", "Meteor Smooth" })
        {
            EffectUse use = EffectUses.For(name)!;

            Assert.Empty(use.Direct);
            Assert.Empty(use.SlotsFor(paletteId: 18));
            Assert.Equal([0], use.SlotsFor(paletteId: 0));
            Assert.True(use.UsesPalette);
        }
    }

    [Fact]
    public void An_effect_that_never_asks_the_palette_says_so()
    {
        // Two Dots draws from its slots and nothing else, so the palette picker has no business
        // being offered for it.
        EffectUse use = EffectUses.For("Two Dots")!;

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
            EffectUse use = EffectUses.For(name)!;

            Assert.All(use.Direct, slot => Assert.Contains(slot, use.SlotsFor(paletteId: 11)));
        }
    }
}
