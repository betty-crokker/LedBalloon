using LedBalloon.Core.Effects;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// The last section of an effect's metadata, which says what it wants its controls set to.
/// <para>
/// WLED's own UI applies these the moment an effect is picked. An app that sends only the effect
/// number leaves the segment carrying whatever the last effect was set to, so the same effect looks
/// like two different things depending on which app selected it - measured on 192.168.0.131, where
/// Palette with its own defaults draws 244 colors across 285 LEDs and scrolls, and with the flags
/// left alone draws the same 244 and sits still.
/// </para>
/// </summary>
public class AnEffectBringsItsOwnSettingsTests
{
    /// <summary>Palette's entry, verbatim from the controller.</summary>
    private const string Palette =
        "Shift,Size,Rotation,,,Animate Shift,Animate Rotation,Anamorphic;;!;12;ix=112,c1=0,o1=1,o2=0,o3=1";

    [Fact]
    public void The_defaults_are_read_off_the_end_of_the_entry()
    {
        EffectMetadata effect = EffectMetadata.Parse(Palette);

        Assert.Equal(112, effect.Defaults["ix"]);
        Assert.Equal(0, effect.Defaults["c1"]);
        Assert.Equal(1, effect.Defaults["o1"]);
        Assert.Equal(0, effect.Defaults["o2"]);
        Assert.Equal(1, effect.Defaults["o3"]);
    }

    [Fact]
    public void The_firmwares_own_notes_to_itself_are_left_out()
    {
        // m12 says how a 2D effect maps itself onto a matrix and si which sound input it reads.
        // Neither is a control a segment carries, and sending one would be sending a settings field
        // as if it were a slider.
        EffectMetadata effect = EffectMetadata.Parse("Fade rate,Starting color;!,!;!;1f;m12=0,si=0");

        Assert.Empty(effect.Defaults);
    }

    [Fact]
    public void An_effect_that_asks_for_nothing_gets_nothing()
    {
        Assert.Empty(EffectMetadata.Parse("!,Duty cycle;!,!;!;01").Defaults);
        Assert.Empty(EffectMetadata.Parse(string.Empty).Defaults);
    }

    [Fact]
    public void The_three_extra_sliders_are_named_by_position()
    {
        EffectMetadata effect = EffectMetadata.Parse(Palette);

        // Positions three to five of the slider section, and the blanks matter: Palette names one
        // and leaves two empty, so reading the list with the blanks dropped would make "Animate
        // Shift" the second slider instead of the first tick box.
        Assert.Equal("Rotation", effect.CustomLabels[0]);
        Assert.Null(effect.CustomLabels[1]);
        Assert.Null(effect.CustomLabels[2]);
    }

    [Fact]
    public void And_so_are_the_three_tick_boxes()
    {
        EffectMetadata effect = EffectMetadata.Parse(Palette);

        Assert.Equal("Animate Shift", effect.OptionLabels[0]);
        Assert.Equal("Animate Rotation", effect.OptionLabels[1]);
        Assert.Equal("Anamorphic", effect.OptionLabels[2]);
    }

    [Fact]
    public void Fire_2012_keeps_its_blank_in_the_middle()
    {
        // The case the positional reading exists for: "Cooling,Spark rate,,2D Blur,Boost" has an
        // empty third entry, so its two named extras are custom 2 and 3 rather than 1 and 2.
        EffectMetadata effect = EffectMetadata.Parse("Cooling,Spark rate,,2D Blur,Boost;;;1;sx=120,ix=64");

        Assert.Null(effect.CustomLabels[0]);
        Assert.Equal("2D Blur", effect.CustomLabels[1]);
        Assert.Equal("Boost", effect.CustomLabels[2]);
        Assert.Equal(120, effect.Defaults["sx"]);
        Assert.Equal(64, effect.Defaults["ix"]);
    }
}
