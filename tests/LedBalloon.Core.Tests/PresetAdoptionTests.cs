using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Adopting turns a preset made in the WLED app into a scene. It cannot always be exact — a preset
/// made against a layout that has since changed lights ranges the runs no longer agree with — so
/// what matters is that the mismatch is reported rather than dropped.
/// </summary>
public class PresetAdoptionTests
{
    private const string South = "020000000002";

    /// <summary>South as it is actually wired: garage, porch, roofline across 310 LEDs.</summary>
    private static LedBalloonProject House()
    {
        var project = new LedBalloonProject
        {
            Controllers = [new ControllerRef { Key = South, Name = "South" }],
            Segments =
            [
                new Segment { Id = "garage", Name = "Garage", ControllerKey = South, Output = 1, Count = 20 },
                new Segment { Id = "porch", Name = "Porch", ControllerKey = South, Output = 1, Count = 5 },
                new Segment { Id = "roof", Name = "Roofline", ControllerKey = South, Output = 2, Count = 285 },
            ],
        };

        project.ReflowAll();
        return project;
    }

    private static HousePreset Preset(string name, params WledSegment[] segments) =>
        new(name, [new PresetPlacement(South, 1, new WledPreset
        {
            Id = 1,
            Name = name,
            On = true,
            Brightness = 128,
            Segments = [.. segments],
        })]);

    /// <summary>South's real Bpm: two segments from a layout that no longer exists.</summary>
    private static HousePreset Bpm() => Preset(
        "Bpm",
        new WledSegment { Id = 0, Start = 0, Stop = 20, On = true, Effect = 1, Palette = 4, Speed = 128 },
        new WledSegment { Id = 1, Start = 20, Stop = 305, On = true, Effect = 68, Palette = 12, Speed = 64 });

    [Fact]
    public void A_preset_segment_that_matches_a_run_exactly_is_carried_across()
    {
        AdoptionReport report = PresetAdoption.Plan(House(), Bpm());

        SceneEntry garage = report.Scene.Segments["garage"];

        Assert.Equal(1, garage.Effect);
        Assert.Equal(4, garage.Palette);
        Assert.Equal((byte)128, garage.Speed);
    }

    [Fact]
    public void A_run_wholly_inside_a_preset_segment_takes_its_appearance()
    {
        AdoptionReport report = PresetAdoption.Plan(House(), Bpm());

        // The porch is LEDs 20-25, inside the preset's 20-305.
        Assert.Equal(68, report.Scene.Segments["porch"].Effect);
        Assert.Equal(12, report.Scene.Segments["porch"].Palette);
    }

    /// <summary>
    /// The corner that cannot be hidden. Bpm lights LEDs 20-305; the roofline runs to 310, because
    /// the layout was corrected after the preset was made. Adopting it lights the last five as
    /// well, which is a change to what the preset does, so it has to be said out loud.
    /// </summary>
    [Fact]
    public void A_run_the_preset_only_partly_lights_is_adopted_and_reported()
    {
        AdoptionReport report = PresetAdoption.Plan(House(), Bpm());

        Assert.Equal(68, report.Scene.Segments["roof"].Effect);

        Assert.False(report.IsClean);
        Assert.Contains(report.Notes, n => n.Message.Contains("Roofline") &&
                                           n.Message.Contains("280 of its 285"));
    }

    [Fact]
    public void A_run_the_preset_says_nothing_about_is_left_out_and_reported()
    {
        HousePreset roofOnly = Preset(
            "Roof only",
            new WledSegment { Id = 0, Start = 25, Stop = 310, On = true, Effect = 74 });

        AdoptionReport report = PresetAdoption.Plan(House(), roofOnly);

        Assert.DoesNotContain("garage", report.Scene.Segments.Keys);
        Assert.Contains(report.Notes, n => n.Message.Contains("'Garage' is not in this preset"));

        // Faithful to what the preset does: WLED leaves segments a preset omits as they were, so
        // an adopted one must too rather than becoming a blackout of everything it omits.
        Assert.False(report.Scene.UnlistedSegmentsOff);
    }

    /// <summary>
    /// A few LEDs clipped off the end of a run say nothing about what the run should look like,
    /// and adopting on that basis would put a pattern somewhere it was never shown.
    /// </summary>
    [Fact]
    public void A_run_barely_clipped_by_a_preset_segment_is_not_adopted_from_it()
    {
        HousePreset clipped = Preset(
            "Clipped",
            new WledSegment { Id = 0, Start = 0, Stop = 26, On = true, Effect = 74 });

        AdoptionReport report = PresetAdoption.Plan(House(), clipped);

        Assert.Equal(74, report.Scene.Segments["garage"].Effect);
        Assert.Equal(74, report.Scene.Segments["porch"].Effect);

        // One LED of 285 is not a description of the roofline.
        Assert.DoesNotContain("roof", report.Scene.Segments.Keys);
        Assert.Contains(report.Notes, n => n.Message.Contains("'Roofline' is only lit for 1 of its 285"));
    }

    [Fact]
    public void Leds_the_preset_lights_that_belong_to_no_run_are_reported()
    {
        HousePreset overrun = Preset(
            "Overrun",
            new WledSegment { Id = 0, Start = 0, Stop = 400, On = true, Effect = 74 });

        AdoptionReport report = PresetAdoption.Plan(House(), overrun);

        Assert.Contains(report.Notes, n => n.Message.Contains("90 LED(s) this preset lights are not part of any run"));
    }

    [Fact]
    public void A_preset_that_fits_the_layout_adopts_without_a_word()
    {
        HousePreset tidy = Preset(
            "Tidy",
            new WledSegment { Id = 0, Start = 0, Stop = 20, On = true, Effect = 1 },
            new WledSegment { Id = 1, Start = 20, Stop = 25, On = true, Effect = 2 },
            new WledSegment { Id = 2, Start = 25, Stop = 310, On = true, Effect = 3 });

        AdoptionReport report = PresetAdoption.Plan(House(), tidy);

        Assert.True(report.IsClean);
        Assert.Equal(3, report.Scene.Segments.Count);
        Assert.Equal("Tidy", report.Scene.Name);
        Assert.Equal((byte)128, report.Scene.Brightness);
    }

    /// <summary>
    /// A segment the preset switches off describes nothing, so it must not claim the run it covers
    /// — otherwise a run would adopt an appearance from a segment that never lit it.
    /// </summary>
    [Fact]
    public void A_segment_the_preset_switches_off_does_not_claim_a_run()
    {
        HousePreset half = Preset(
            "Half off",
            new WledSegment { Id = 0, Start = 0, Stop = 20, On = false, Effect = 9 },
            new WledSegment { Id = 1, Start = 20, Stop = 310, On = true, Effect = 74 });

        AdoptionReport report = PresetAdoption.Plan(House(), half);

        Assert.DoesNotContain("garage", report.Scene.Segments.Keys);
        Assert.Equal(74, report.Scene.Segments["roof"].Effect);
    }

    /// <summary>
    /// Adopting takes the appearance and leaves the numbers behind, which is the whole point: the
    /// bounds come from the layout at apply time from then on.
    /// </summary>
    [Fact]
    public void The_adopted_scene_carries_no_led_indices()
    {
        AdoptionReport report = PresetAdoption.Plan(House(), Bpm());

        WledState state = SceneResolver.ResolveFor(House(), report.Scene, South);

        Assert.Equal(310, state.Segments!.Single(s => s.Start == 25).Stop);
    }
}
