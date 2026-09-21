using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Resolving a preset's effect number to something drawable, which is the join between what the
/// controller stores and what the photo can show. A preset stores a number; the number only means
/// anything against the list the controller itself reports.
/// </summary>
public class EffectLibraryTests
{
    /// <summary>
    /// A stand-in for <c>/json/eff</c>: the real list is 187 long on 0.15.3 with Chunchun at 111.
    /// </summary>
    private static string[] EffectList()
    {
        var names = new string[187];
        Array.Fill(names, "RSVD");

        names[0] = "Solid";
        names[1] = "Blink";
        names[9] = "Rainbow";
        names[111] = "Chunchun";

        return names;
    }

    private static WledSegment July4thPorchline() => new()
    {
        Id = 0,
        Start = 0,
        Stop = 308,
        Effect = 111,
        Palette = 254,
        Speed = 172,
        Intensity = 128,
        Colors = [[255, 0, 0], [0, 0, 0], [0, 0, 0]],
    };

    [Fact]
    public void The_effect_a_preset_names_by_number_resolves_through_the_controllers_own_list()
    {
        IWledEffect? effect = EffectLibrary.Find(111, EffectList());

        Assert.NotNull(effect);
        Assert.Equal("Chunchun", effect.Name);
    }

    [Fact]
    public void The_same_number_against_a_different_list_is_a_different_effect()
    {
        // Which is the whole reason for going through the list: a controller on another version,
        // or a fork, can have something else at 111.
        var shuffled = new string[187];
        Array.Fill(shuffled, "RSVD");
        shuffled[111] = "Rolling Balls";

        Assert.Null(EffectLibrary.Find(111, shuffled));
    }

    [Fact]
    public void An_effect_we_have_not_ported_simply_does_not_resolve()
    {
        Assert.Null(EffectLibrary.Find(9, EffectList()));
        Assert.Null(EffectLibrary.Find(null, EffectList()));
        Assert.Null(EffectLibrary.Find(999, EffectList()));
        Assert.Null(EffectLibrary.Find(111, null));
    }

    [Fact]
    public void A_porchline_running_july_4th_comes_back_ready_to_draw()
    {
        EffectSimulation? simulation = EffectLibrary.Simulate(
            July4thPorchline(), length: 308, effectNames: EffectList(), palette: null);

        Assert.NotNull(simulation);
        Assert.Equal(308, simulation.Segment.Pixels.Length);

        // Primed, so it opens on a settled picture rather than one frame of scattered dots.
        Assert.True(simulation.Frames >= 24);
        Assert.Contains(simulation.Segment.Pixels, p => p.R + p.G + p.B > 0);
    }

    [Fact]
    public void A_long_run_carries_enough_birds_that_their_trails_merge()
    {
        EffectSimulation? simulation = EffectLibrary.Simulate(
            July4thPorchline(), length: 308, effectNames: EffectList(), palette: null);

        int lit = simulation!.Segment.Pixels.Count(p => p.R + p.G + p.B > 0);

        // About two thirds, and worth pinning down because it is counter-intuitive. The flock is
        // one bird per eight LEDs, and in the nine frames a trail survives a bird covers several
        // birds' worth of spacing - so on a long run Chunchun reads as a flowing wash rather than
        // as separate dots. Short runs are where you actually see birds.
        Assert.InRange(lit, 170, 250);
    }

    [Fact]
    public void A_short_run_really_does_show_separate_birds()
    {
        WledSegment stairs = July4thPorchline();

        EffectSimulation? simulation = EffectLibrary.Simulate(
            stairs, length: 48, effectNames: EffectList(), palette: null);

        int lit = simulation!.Segment.Pixels.Count(p => p.R + p.G + p.B > 0);

        // 2 + (48 >> 3) = 8 birds on 48 LEDs, so most of the run is dark between them.
        Assert.True(lit < 48, $"expected gaps between birds, but {lit} of 48 were lit");
    }

    [Fact]
    public void The_settings_the_preset_carries_reach_the_run()
    {
        EffectSimulation? simulation = EffectLibrary.Simulate(
            July4thPorchline(), length: 308, effectNames: EffectList(), palette: null);

        EffectSegment segment = simulation!.Segment;

        Assert.Equal(172, segment.Speed);
        Assert.Equal(128, segment.Intensity);
        Assert.Equal(254, segment.PaletteId);
        Assert.Equal(new RgbColor(255, 0, 0), segment.Colors[0]);
    }

    [Fact]
    public void The_run_is_as_long_as_the_house_says_not_as_long_as_the_preset_says()
    {
        // The preset was saved when the porch was 308 LEDs; the layout is what it is now. This is
        // the original complaint about WLED presets, so the simulation must not reintroduce it.
        EffectSimulation? simulation = EffectLibrary.Simulate(
            July4thPorchline(), length: 180, effectNames: EffectList(), palette: null);

        Assert.Equal(180, simulation!.Segment.Pixels.Length);
    }
}
