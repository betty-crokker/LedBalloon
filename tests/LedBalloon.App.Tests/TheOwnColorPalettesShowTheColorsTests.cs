using Avalonia.Media;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// The palettes WLED builds out of the segment's own color slots, and whether their swatches say so.
/// <para>
/// "My two colors" - WLED calls it "* Colors 1&amp;2" - is a recipe rather than a gradient:
/// <c>CRGBPalette16(prim, prim, sec, sec)</c> from the segment's first two color slots. The swatch
/// beside it is the only place that recipe becomes visible, so a swatch that does not follow the
/// color boxes is not merely stale, it is describing a palette nobody can select.
/// </para>
/// <para>
/// It was stale. The picker list is built once per segment, and changing a color sent the change to
/// the controller without refilling anything, so the row went on showing the two colors the segment
/// had when the editor opened.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheOwnColorPalettesShowTheColorsTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-swatch-tests-{Guid.NewGuid():N}.json");

    private readonly List<FakeController> _open = [];

    private const string Key = "11:22:33:44:55:66";

    [Fact]
    public void Changing_a_color_redraws_the_swatch_made_out_of_it() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        PaletteOption row = app.SegmentPalettes.Single(o => o.Id == 3);
        Color[] before = Swatch(row);

        app.SegmentColors[0].Picked = Colors.Red;

        PaletteOption after = app.SegmentPalettes.Single(o => o.Id == 3);

        Assert.NotEqual(before, Swatch(after));
        Assert.Contains(Swatch(after), c => c is { R: > 200, G: < 60, B: < 60 });
    });

    [Fact]
    public void A_real_gradient_is_left_alone() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        // Ocean is a stored gradient and has nothing to do with the color slots, so nothing about
        // it should move when one changes.
        IBrush? before = app.SegmentPalettes.Single(o => o.Name == "Ocean").Gradient;

        app.SegmentColors[0].Picked = Colors.Red;

        Assert.Same(before, app.SegmentPalettes.Single(o => o.Name == "Ocean").Gradient);
    });

    [Fact]
    public void The_choice_survives_its_row_being_redrawn() => ui.Run(async () =>
    {
        MainViewModel app = await HouseAsync();

        app.SegmentPaletteChoice = app.SegmentPalettes.Single(o => o.Id == 3);

        app.SegmentColors[0].Picked = Colors.Lime;

        // Replacing an item in the collection drops a ComboBox's selection, so the choice has to be
        // put back on the row that replaced it - otherwise changing a color silently deselects the
        // palette that was using it.
        Assert.NotNull(app.SegmentPaletteChoice);
        Assert.Equal(3, app.SegmentPaletteChoice!.Id);
    });

    private static Color[] Swatch(PaletteOption option) =>
        option.Gradient is LinearGradientBrush brush
            ? [.. brush.GradientStops.Select(s => s.Color)]
            : [];

    private async Task<MainViewModel> HouseAsync()
    {
        FakeController controller = FakeController.Start(["Solid"], null, null, Key);
        _open.Add(controller);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porchline", ControllerKey = Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        await app.AddDeviceAsync(controller.Host, null);
        await app.LoadPalettesAsync();

        app.SelectedSegment = app.Project.Segments.Single();

        return app;
    }

    public void Dispose()
    {
        foreach (FakeController controller in _open)
        {
            controller.Dispose();
        }

        File.Delete(_preferences);
        GC.SuppressFinalize(this);
    }
}
