using LedBalloon.App.ViewModels;
using LedBalloon.Core.Layout;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// A run of LED along a roofline is not a matrix, and the effects that need one were being offered.
/// <para>
/// WLED declares them with a flag and its own UI hides them; this read the flag, checked it against
/// the controller's own filtering and agreed on all 37 of them, and then offered all 37 anyway.
/// Drift Rose is the one that made it obvious: pickable, unrunnable, and sitting under two notes
/// that disagreed about where its colors came from.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class TheEffectListLeavesOutWhatCannotWorkTests(UiThreadFixture ui) : IDisposable
{
    private readonly string _preferences = Path.Combine(
        Path.GetTempPath(), $"ledballoon-2d-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void An_effect_that_needs_a_matrix_is_not_offered() => ui.Run(async () =>
    {
        // "Fade,Blur;;;2" is exactly what the controller reports for Drift Rose: a 2 in the flags
        // and no 1, which is WLED for "this one needs a matrix".
        using FakeController controller = FakeController.Start(
            ["Solid", "Blink", "Drift Rose"],
            ["", "!,Duty cycle;!,!;!;01", "Fade,Blur;;;2"]);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];

        Assert.DoesNotContain(app.SegmentEffects, o => o.Name == "Drift Rose");
        Assert.Contains(app.SegmentEffects, o => o.Name == "Blink");
    });

    [Fact]
    public void A_reserved_slot_is_not_an_effect_and_is_not_offered() => ui.Run(async () =>
    {
        // WLED retires an effect by leaving its number in the list under this name, so presets saved
        // against the numbers after it still recall the right thing. Seven of the 187 are this.
        using FakeController controller = FakeController.Start(
            ["Solid", "RSVD", "Blink"],
            ["", "", "!,Duty cycle;!,!;!;01"]);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];

        Assert.DoesNotContain(app.SegmentEffects, o => o.Name == "RSVD");
        Assert.Contains(app.SegmentEffects, o => o.Name == "Blink");
    });

    [Fact]
    public void An_effect_that_follows_sound_says_so_when_there_is_none() => ui.Run(async () =>
    {
        // Freqwave's own entry, and the usermod block 192.168.0.131 really reports: the driver is
        // running, it names a source, and it has heard nothing. Picking this leaves the run exactly
        // as dark as it was, which reads as the app failing to send.
        using FakeController controller = FakeController.Start(
            ["Solid", "Freqwave"],
            ["", "Speed,Sound effect,Low bin,High bin,Pre-amp;;;01f;m12=2,si=0"],
            Quiet);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Freqwave");

        Assert.Contains("follows sound", app.SegmentPreviewNote, StringComparison.Ordinal);
        Assert.Contains("none coming in", app.SegmentPreviewNote, StringComparison.Ordinal);
    });

    [Fact]
    public void And_nothing_is_drawn_for_it_out_of_a_palette_it_never_reads() => ui.Run(async () =>
    {
        // The three lines that disagreed. The preview laid the chosen gradient across the run while
        // the line beneath it said the effect does not use a palette and the line beneath that said
        // it works out its own colors - and all three were separately true, which is how it survived.
        using FakeController controller = FakeController.Start(
            ["Solid", "Freqwave"],
            ["", "Speed,Sound effect,Low bin,High bin,Pre-amp;;;01f;m12=2,si=0"],
            Quiet);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Freqwave");
        app.SegmentPaletteChoice = app.SegmentPalettes.First(o => o.Name == "Ocean");

        Assert.False(app.SegmentUsesPalette);

        // Null by the withholding rule above. It would also be null while the controller had yet to
        // echo the palette back, so the assertion worth reading is the one above it.
        Assert.Null(app.PreviewPalette);
        Assert.DoesNotContain("palette", app.SegmentPaletteNote, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void The_one_beside_it_that_does_read_a_palette_keeps_it() => ui.Run(async () =>
    {
        // Pixelwave is unported and sound-driven too, and it reads the palette, so there is
        // something real to lay across the run - held still rather than slid along, but shown. The
        // rule has to tell these two apart or it is the old behaviour with more words on it.
        using FakeController controller = FakeController.Start(
            ["Solid", "Pixelwave"],
            ["", "Fade rate,Sensitivity;!,!;!;1v;ix=64,m12=2,si=0"],
            Quiet);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];
        app.SegmentEffectChoice = app.SegmentEffects.Single(o => o.Name == "Pixelwave");

        Assert.True(app.SegmentUsesPalette);
        Assert.Equal(string.Empty, app.SegmentPaletteNote);
    });

    /// <summary>The usermod block from 192.168.0.131, which has no microphone wired to its I2S pins.</summary>
    private const string Quiet = """
    {
      "AudioReactive": ["<button></button>"],
      "Audio Source": ["I2S digital", " - quiet"],
      "Sound Processing": ["running"],
      "UDP Sound Sync": ["off"]
    }
    """;

    [Fact]
    public void One_that_works_on_either_is_still_offered() => ui.Run(async () =>
    {
        // A 1 alongside the 2 means it runs on a strip as well, and WLED's own UI keeps it.
        using FakeController controller = FakeController.Start(
            ["Solid", "Ripple"],
            ["", "!,Wave width;;!;12"]);

        MainViewModel app = await HouseAsync(controller);
        app.SelectedSegment = app.Project.Segments[0];

        Assert.Contains(app.SegmentEffects, o => o.Name == "Ripple");
    });

    private async Task<MainViewModel> HouseAsync(FakeController controller)
    {
        MainViewModel app = new(scanForControllers: false, AppPreferences.Load(_preferences));

        app.Project.Segments.Add(new Segment
        {
            Id = "porch", Name = "Porch", ControllerKey = FakeController.Key,
            Output = 1, Count = 5, SegmentId = 0,
        });

        app.LiveSync = true;

        await app.AddDeviceAsync(controller.Host, null);

        return app;
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
