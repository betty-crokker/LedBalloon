using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A copied preset that asks for a palette the target controller has never been given does not
/// fail — WLED falls back to palette 0 without a word, so the house comes out plain and nothing
/// says why. These pin down the numbering that makes carrying the palette across work, which is
/// the part that is easy to get backwards: the ids count down from 255, the files count up from
/// zero, and a palette almost never keeps its id when it moves.
/// </summary>
public class CustomPaletteCopierTests
{
    [Theory]
    [InlineData(0, 255)]
    [InlineData(1, 254)]
    [InlineData(9, 246)]
    public void A_slot_and_its_palette_id_count_in_opposite_directions(int slot, int id)
    {
        Assert.Equal(id, CustomPaletteCopier.IdForSlot(slot));
        Assert.Equal(slot, CustomPaletteCopier.SlotForId(id));
        Assert.Equal($"palette{slot}.json", CustomPaletteCopier.FileForSlot(slot));
    }

    [Theory]
    [InlineData(0, false)]      // Default
    [InlineData(4, false)]      // * Color Gradient
    [InlineData(70, false)]     // the last built-in on 0.15.3
    [InlineData(245, false)]    // WLED's own cutoff is "> 245"
    [InlineData(246, true)]
    [InlineData(254, true)]
    [InlineData(255, true)]
    public void Only_the_top_ten_ids_mean_a_file_on_this_particular_controller(int id, bool custom)
    {
        Assert.Equal(custom, CustomPaletteCopier.IsCustom(id));
    }

    [Fact]
    public void A_preset_reports_the_custom_palettes_it_needs_and_ignores_the_built_in_ones()
    {
        var preset = new WledPreset
        {
            Name = "July 4th",
            Segments =
            [
                new WledSegment { Id = 0, Stop = 308, Effect = 111, Palette = 254 },
                new WledSegment { Id = 1, Stop = 356, Effect = 0, Palette = 0 },
                new WledSegment { Id = 2, Stop = 400, Effect = 9, Palette = 11 },
                new WledSegment { Id = 3, Stop = 440, Effect = 9, Palette = 255 },

                // Asking for the same one twice does not make it two jobs.
                new WledSegment { Id = 4, Stop = 480, Effect = 111, Palette = 254 },
            ],
        };

        Assert.Equal([254, 255], CustomPaletteCopier.CustomPalettesUsedBy(preset));
    }

    [Fact]
    public void The_padding_segments_wled_writes_into_every_preset_are_not_asking_for_anything()
    {
        var preset = new WledPreset
        {
            Name = "Off",
            Segments = [new WledSegment { Id = 0, Stop = 0 }, new WledSegment { Id = 1, Stop = 0 }],
        };

        Assert.Empty(CustomPaletteCopier.CustomPalettesUsedBy(preset));
    }

    [Fact]
    public void Moving_a_palette_repoints_the_preset_at_where_it_actually_landed()
    {
        var preset = new WledPreset
        {
            Name = "July 4th",
            Segments =
            [
                new WledSegment { Id = 0, Stop = 180, Effect = 111, Palette = 254 },
                new WledSegment { Id = 1, Stop = 310, Effect = 0, Palette = 0 },
            ],
        };

        // North's palette1.json is id 254 there; on a controller that held none it becomes the
        // first file, which is id 255.
        CustomPaletteCopier.Remap(preset, new Dictionary<int, int> { [254] = 255 });

        Assert.Equal(255, preset.Segments![0].Palette);

        // A built-in means the same thing everywhere, so it is left alone.
        Assert.Equal(0, preset.Segments[1].Palette);
    }

    [Fact]
    public void A_palette_that_did_not_move_keeps_the_id_it_had()
    {
        var preset = new WledPreset
        {
            Name = "July 4th",
            Segments = [new WledSegment { Id = 0, Stop = 180, Palette = 254 }],
        };

        CustomPaletteCopier.Remap(preset, new Dictionary<int, int>());

        Assert.Equal(254, preset.Segments![0].Palette);
    }
}
