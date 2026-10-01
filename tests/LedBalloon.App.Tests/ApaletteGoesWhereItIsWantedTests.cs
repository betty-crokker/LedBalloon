using System.Text;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Picking, on one controller, a palette that lives on the other.
/// <para>
/// Which box a gradient's file sits on is not something anybody making a house look nice should
/// have to think about, and WLED gives no help at all: a segment asking for a custom palette its
/// controller does not hold falls silently back to palette 0, so the run comes out plain with
/// nothing said about it.
/// </para>
/// <para>
/// The copier itself has been tested against a stand-in and, on 2026-10-01, against the house: a
/// gradient that was id 255 on North arrived on South as 254, byte for byte, and the firmware
/// reported all sixteen expanded stops at that id. What had no test at all was this half - the row
/// in the picker that offers it and the copy that happens when somebody chooses it.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class ApaletteGoesWhereItIsWantedTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-carry-tests-{Guid.NewGuid():N}.json");

    private const string North = "aa:bb:cc:dd:ee:ff";
    private const string South = "11:22:33:44:55:66";

    /// <summary>North's two, in its own slot order. The second is the one South also has.</summary>
    private const string Fireworks = """{"palette":[0,"000000",37,"ffffff",116,"ff00ff",255,"ff0000"]}""";
    private const string Icicles = """{"palette":[0,"000000",1,"0000ff",129,"ffffff",255,"870000"]}""";

    [Fact]
    public void One_the_other_controller_has_is_offered_here() => ui.Run(async () =>
    {
        (MainViewModel app, _, _) = await HouseAsync();

        app.SelectedSegment = app.Project.Segments.Single(s => s.ControllerKey == South);

        PaletteOption row = app.SegmentPalettes.Single(o => o.Kind == PaletteKind.Elsewhere);

        // Its gradient travels with it, so the row is not a bare name for something nobody can see.
        Assert.NotNull(row.Gradient);
        Assert.Equal(Fireworks, Encoding.UTF8.GetString(row.Content!));
    });

    [Fact]
    public void One_this_controller_already_has_is_not_offered_twice() => ui.Run(async () =>
    {
        (MainViewModel app, _, _) = await HouseAsync();

        app.SelectedSegment = app.Project.Segments.Single(s => s.ControllerKey == South);

        // Icicles is on both, under a different id on each. Offering it as something from elsewhere
        // would be offering a copy of a palette this box is already holding.
        Assert.DoesNotContain(
            app.SegmentPalettes.Where(o => o.Kind == PaletteKind.Elsewhere),
            o => Encoding.UTF8.GetString(o.Content!) == Icicles);
    });

    [Fact]
    public void Picking_it_writes_it_here_and_lands_on_the_id_it_got_here() => ui.Run(async () =>
    {
        (MainViewModel app, _, FakeController south) = await HouseAsync();

        app.SelectedSegment = app.Project.Segments.Single(s => s.ControllerKey == South);
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(o => o.Kind == PaletteKind.Elsewhere);

        await Settled(app);

        // South held one already, so this is its second: slot 1, which answers to 254. On North the
        // same gradient is slot 0 and answers to 255. The id is a position in each box's own list
        // and carrying a palette almost never leaves it alone.
        Assert.Equal(Fireworks, Encoding.UTF8.GetString(south.Files["palette1.json"]));
        Assert.Equal(254, app.SegmentPaletteChoice?.Id);
        Assert.Equal(PaletteKind.Palette, app.SegmentPaletteChoice?.Kind);
    });

    [Fact]
    public void And_goes_to_the_handler_that_makes_the_controller_notice() => ui.Run(async () =>
    {
        (MainViewModel app, _, FakeController south) = await HouseAsync();

        app.SelectedSegment = app.Project.Segments.Single(s => s.ControllerKey == South);
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(o => o.Kind == PaletteKind.Elsewhere);

        await Settled(app);

        // /edit writes the file and stops there, leaving the firmware none the wiser and the segment
        // falling back to palette 0 with nothing said. Measured on 0.15.3, and the reason this
        // assertion exists twice.
        Assert.Contains("upload", south.Uploads);
    });

    [Fact]
    public void The_name_travels_with_it_because_the_name_was_the_only_part_that_was_ours()
        => ui.Run(async () =>
    {
        (MainViewModel app, _, _) = await HouseAsync();

        // WLED knows a custom palette only by a number counted down from 255, so what it is called
        // lives in the project - and a palette that arrives nameless is "Custom 1" to whoever
        // carried it over.
        app.Project.NamePalette(North, 255, "Fireworks");

        app.SelectedSegment = app.Project.Segments.Single(s => s.ControllerKey == South);
        app.SegmentPaletteChoice = app.SegmentPalettes.Single(o => o.Kind == PaletteKind.Elsewhere);

        await Settled(app);

        Assert.Equal("Fireworks", app.Project.NameForPalette(South, 254));
    });

    /// <summary>Waits for the copy, the reload and the refill, which run off the dispatcher.</summary>
    private static async Task Settled(MainViewModel app)
    {
        for (int tries = 0; tries < 60 && app.SegmentPaletteChoice?.Kind != PaletteKind.Palette; tries++)
        {
            await Task.Delay(50);
        }
    }

    private async Task<(MainViewModel App, FakeController North, FakeController South)> HouseAsync()
    {
        FakeController north = FakeController.Start(["Solid"], null, null, North);
        FakeController south = FakeController.Start(["Solid"], null, null, South);

        north.Holds(Fireworks, Icicles);
        south.Holds(Icicles);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porchline", ControllerKey = North,
            Output = 1, Count = 5, SegmentId = 0,
        });

        app.Project.Segments.Add(new Segment
        {
            Id = "roof", Name = "Roofline", ControllerKey = South,
            Output = 1, Count = 5, SegmentId = 0,
        });

        await app.AddDeviceAsync(north.Host, null);
        await app.AddDeviceAsync(south.Host, null);

        await app.LoadPalettesAsync();

        _open.Add(north);
        _open.Add(south);

        return (app, north, south);
    }

    private readonly List<FakeController> _open = [];

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }

        if (File.Exists(_preferences))
        {
            File.Delete(_preferences);
        }
    }
}
