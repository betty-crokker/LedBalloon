using LedBalloon.App.ViewModels;
using Xunit;

namespace LedBalloon.App.Tests;

/// <summary>
/// What a controller reports while it is connecting has to reach the things drawn from it.
/// <para>
/// This is the one test that exists because of a bug nobody could see from the code. The view model
/// subscribed to a controller only after connecting it, so everything the controller reported on the
/// way - its effect list among it - was announced to nobody. Reading the effect list afterwards gave
/// the right answer, which is why nothing caught it; but the photo had bound to it long before and
/// was never told to look again, so it held an empty list for the rest of the session. With no effect
/// names, an effect number on a run resolves to nothing, no effect is drawn for it, and every run on
/// the house shows as its own flat color. Lit, and completely still.
/// </para>
/// <para>
/// So the first assertion is about the announcement, not the value. The value was never wrong.
/// </para>
/// </summary>
[Collection(UiThreadCollection.Name)]
public class EffectNamesReachThePhotoTests(UiThreadFixture ui)
{
    [Fact]
    public void A_controller_that_finishes_connecting_announces_its_effect_list() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink", "Pacifica");

        var app = new MainViewModel(scanForControllers: false);

        List<string?> announced = [];
        app.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        await app.AddDeviceAsync(controller.Host, null);

        Assert.Contains(nameof(MainViewModel.EffectNames), announced);
    });

    [Fact]
    public void And_the_list_it_announces_is_the_one_the_controller_reported() => ui.Run(async () =>
    {
        using FakeController controller = FakeController.Start("Solid", "Blink", "Pacifica");

        var app = new MainViewModel(scanForControllers: false);

        await app.AddDeviceAsync(controller.Host, null);

        Assert.True(
            app.EffectNames.ContainsKey(FakeController.Key),
            "the effect list should be keyed by the controller: " +
            $"[{string.Join(", ", app.EffectNames.Keys)}]");

        Assert.Equal<IEnumerable<string>>(
            ["Solid", "Blink", "Pacifica"], app.EffectNames[FakeController.Key]);
    });
}
