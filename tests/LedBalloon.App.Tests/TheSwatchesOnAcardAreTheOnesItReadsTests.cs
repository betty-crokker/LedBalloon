using Avalonia.Media;
using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// Which colors go beside a run's name in the scene panel.
/// <para>
/// A pink Solid was drawn as a pink square and a black one. The black stood for nothing: WLED keeps
/// three colors on every segment whether or not anything reads them, the card counted how many the
/// segment was carrying rather than how many the effect reads, and slot 2 of a segment nobody has
/// set is black.
/// </para>
/// <para>
/// The editor had this right already - it asks the ported effect, and offered Solid a single box -
/// so the card and the editor were two calculations of one fact, disagreeing on screen at the same
/// time. They are one calculation now.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheSwatchesOnAcardAreTheOnesItReadsTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-swatch-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void Solid_reads_one_color_and_is_drawn_with_one() => ui.Run(async () =>
    {
        PresetDetail card = await CardAsync(effect: 0);

        Assert.True(card.HasPrimary);
        Assert.False(card.HasSecondary);
        Assert.Equal(Color.FromRgb(255, 0, 128), ((SolidColorBrush)card.PrimarySwatch).Color);
    });

    [Fact]
    public void Blink_on_the_default_palette_reads_two_and_is_drawn_with_two() => ui.Run(async () =>
    {
        // The other side of the same rule, so this is not "show one square always".
        PresetDetail card = await CardAsync(effect: 1);

        Assert.True(card.HasPrimary);
        Assert.True(card.HasSecondary);
        Assert.Equal(Color.FromRgb(255, 0, 128), ((SolidColorBrush)card.PrimarySwatch).Color);
        Assert.Equal(Color.FromRgb(0, 80, 255), ((SolidColorBrush)card.SecondarySwatch).Color);
    });

    [Fact]
    public void One_that_reads_only_its_background_is_drawn_with_the_background() => ui.Run(async () =>
    {
        // Blink reads slot 1 whatever the palette, and slot 0 only while the palette is Default -
        // because on Default the palette IS the slots. So on a real palette it is one color and it
        // is not the first one. The card showed slots 1 and 2 regardless of what was read, which
        // here is not one square too many but the wrong square.
        PresetDetail card = await CardAsync(effect: 1, palette: 6);

        Assert.True(card.HasPrimary);
        Assert.False(card.HasSecondary);
        Assert.Equal(Color.FromRgb(0, 80, 255), ((SolidColorBrush)card.PrimarySwatch).Color);
    });

    private async Task<PresetDetail> CardAsync(int effect, int palette = 0)
    {
        using FakeController controller = FakeController.Start(
            ["Solid", "Blink", "Android"],
            ["", "!,Duty cycle;!,!;!;01", "!,Width;;!;01"]);

        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        var run = new Segment
        {
            Id = "stairs", Name = "Under stairs", ControllerKey = FakeController.Key,
            Output = 1, Count = 48, SegmentId = 0,
        };

        app.Project.Segments.Add(run);

        await app.AddDeviceAsync(controller.Host, null);

        app.SelectedSegment = run;

        // Pink in slot 0, a blue in slot 1 so the two can be told apart, and black in slot 2 -
        // which WLED keeps on every segment whether or not anything reads it, and which is the
        // square that started this.
        var wled = new WledSegment
        {
            Id = 0,
            On = true,
            Effect = effect,
            Palette = palette,
            Colors = [[255, 0, 128], [0, 80, 255], [0, 0, 0]],
        };

        return MainViewModel.Describe(run, wled, app.SegmentController);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (File.Exists(_preferences))
        {
            File.Delete(_preferences);
        }
    }
}
