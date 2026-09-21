using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Which controller stores a preset is a wiring detail. These pin down that the merged list hides
/// that, without hiding the case where a preset only covers part of the house.
/// </summary>
public class PresetCatalogTests
{
    private const string Front = "20e7c86a1be8";
    private const string Garage = "704bca414644";

    private static WledPreset Preset(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void The_same_preset_on_both_controllers_appears_once()
    {
        IReadOnlyList<HousePreset> catalog = PresetCatalog.Merge(new Dictionary<string, IReadOnlyList<WledPreset>>
        {
            [Front] = [Preset(1, "All off"), Preset(4, "Winter both")],
            [Garage] = [Preset(2, "Winter both")],
        });

        HousePreset winter = Assert.Single(catalog, p => p.Name == "Winter both");

        Assert.Equal(2, winter.ControllerCount);

        // Three presets across the two controllers, but only two distinct names.
        Assert.Equal(2, catalog.Count);
    }

    [Fact]
    public void Slot_numbers_need_not_match_between_controllers()
    {
        IReadOnlyList<HousePreset> catalog = PresetCatalog.Merge(new Dictionary<string, IReadOnlyList<WledPreset>>
        {
            [Front] = [Preset(4, "Winter both")],
            [Garage] = [Preset(9, "Winter both")],
        });

        HousePreset winter = Assert.Single(catalog);

        // Each controller is told its own slot number.
        Assert.Equal(4, PresetCatalog.PatchFor(winter, Front)!.Preset);
        Assert.Equal(9, PresetCatalog.PatchFor(winter, Garage)!.Preset);
    }

    [Fact]
    public void A_preset_only_one_controller_has_is_flagged_rather_than_hidden()
    {
        IReadOnlyList<HousePreset> catalog = PresetCatalog.Merge(new Dictionary<string, IReadOnlyList<WledPreset>>
        {
            [Front] = [Preset(1, "Valentine")],
            [Garage] = [Preset(1, "Bpm")],
        });

        HousePreset valentine = Assert.Single(catalog, p => p.Name == "Valentine");

        Assert.True(valentine.IsPartial(totalControllers: 2));
        Assert.Null(PresetCatalog.PatchFor(valentine, Garage));
    }

    [Fact]
    public void Names_are_matched_without_regard_to_case_or_padding()
    {
        IReadOnlyList<HousePreset> catalog = PresetCatalog.Merge(new Dictionary<string, IReadOnlyList<WledPreset>>
        {
            [Front] = [Preset(1, "Winter Both")],
            [Garage] = [Preset(1, "  winter both  ")],
        });

        Assert.Single(catalog);
    }

    [Fact]
    public void Wleds_own_slot_ordering_is_preserved()
    {
        IReadOnlyList<HousePreset> catalog = PresetCatalog.Merge(new Dictionary<string, IReadOnlyList<WledPreset>>
        {
            [Front] = [Preset(3, "Stairs white"), Preset(1, "All off"), Preset(2, "Twinkle both")],
        });

        Assert.Equal(["All off", "Twinkle both", "Stairs white"], catalog.Select(p => p.Name));
    }
}
