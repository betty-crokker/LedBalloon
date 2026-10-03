using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// What the stand-in bus reports it can show, and why a palette effect depends on it.
/// <para>
/// WLED decides whether a segment can show colour by asking the busses underneath it, once, and
/// recording the answer. Say no and <c>color_from_palette</c> gives up at its second line and hands
/// back the colour slot for every pixel - so the whole run comes out one flat colour that no
/// palette, no effect and no amount of elapsed time can move.
/// </para>
/// <para>
/// The flags behind that answer are not set by the <c>Bus</c> base constructor: every real bus is a
/// derived class that sets them itself from its own BusConfig, and the host's stand-in did not. It
/// therefore reported whatever was in that memory, which is worse than reporting nothing, because it
/// is a different answer in each process. It read 105 under the tests and 0 on the machine running
/// the house, where every effect drew flat.
/// </para>
/// </summary>
public class TheBusSaysWhatItCanShowTests
{
    /// <summary>Indices into <see cref="NativeEngine.CapabilityTrace"/>.</summary>
    private const int HasRgb = 9;

    private const int Hult = 28;

    [Fact]
    public void The_bus_reports_RGB_rather_than_whatever_was_in_the_memory()
    {
        Render(Hult);

        uint[] trace = NativeEngine.CapabilityTrace();

        // Exactly one, not merely true. An uninitialised bool is usually truthy, which is why this
        // worked everywhere it was tried and failed on the one machine that mattered - so "it draws
        // colour here" is not the assertion worth making.
        Assert.Equal(1u, trace[HasRgb]);
    }

    [Fact]
    public void A_gradient_palette_does_not_come_out_one_flat_colour()
    {
        Render(Hult);

        uint[] samples = NativeEngine.PaletteSamples();

        Assert.Equal(samples.Length, samples.Distinct().Count());
    }

    /// <summary>Draws a few frames, which is what sets the engine up.</summary>
    private static void Render(int palette)
    {
        string[] names = [.. EffectLibrary.All.Select(effect => effect.Name)];

        var wled = new WledSegment
        {
            Id = 0,
            Effect = Array.IndexOf(names, "Sunrise"),
            Palette = palette,
            Speed = 1,
            Intensity = 128,
            Colors = [[0x60, 0x52, 0xFF], [0, 0, 0], [0, 0, 0]],
        };

        EffectSimulation simulation = EffectLibrary.Simulate(wled, 308, names, null, 11, 2)!;
        simulation.Advance(110);
    }
}
