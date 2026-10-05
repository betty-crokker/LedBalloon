using LedBalloon.Core.Effects;
using LedBalloon.Core.Models;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// A segment wired backwards shows its effect backwards, and the app has to draw it that way.
/// <para>
/// WLED reverses inside setPixelColor, so an effect never learns which way round its run is: it
/// writes forwards and the firmware mirrors it on the way to the strip. Nothing here did that, so
/// every reversed run was drawn end for end. South's roofline is 285 LEDs and reversed, and Fire
/// 2012 burned at the near end on the photo while burning at the far end on the house.
/// </para>
/// <para>
/// Caught by measurement rather than by reading. Frame-to-frame movement along the run, captured off
/// the controller's live preview and computed again from the simulation, came out exact mirror
/// images: the flame's 15% sat in the last three buckets of twenty on the house and the first three
/// in here.
/// </para>
/// </summary>
[Collection(EngineCollection.Name)]
public class AreversedRunIsDrawnTheRightWayTests
{
    private static EffectSegment Run(bool reversed)
    {
        var segment = new EffectSegment(10);

        segment.Adopt(new WledSegment { Id = 0, Reverse = reversed }, null);

        for (int i = 0; i < 10; i++)
        {
            segment.Pixels[i] = new RgbColor((byte)(i * 10), 0, 0);
        }

        return segment;
    }

    [Fact]
    public void A_run_wired_forwards_reads_as_it_was_written()
    {
        EffectSegment forwards = Run(reversed: false);

        Assert.Equal(0, forwards.AsWired(0).R);
        Assert.Equal(90, forwards.AsWired(9).R);
    }

    [Fact]
    public void A_run_wired_backwards_reads_the_other_way()
    {
        EffectSegment backwards = Run(reversed: true);

        Assert.Equal(90, backwards.AsWired(0).R);
        Assert.Equal(0, backwards.AsWired(9).R);
    }

    [Fact]
    public void The_effect_itself_still_writes_forwards()
    {
        // The distinction that makes this work. An effect that trails reads its own previous frame,
        // and it has to keep reading it in its own coordinates - reversing the buffer instead of the
        // view of it would flip the trail end for end on every frame.
        EffectSegment backwards = Run(reversed: true);

        Assert.Equal(0, backwards.Pixels[0].R);
        Assert.Equal(90, backwards.Pixels[9].R);
    }

    [Fact]
    public void Reversal_survives_a_frame_of_a_real_effect()
    {
        var wled = new WledSegment { Id = 0, Effect = 0, Reverse = true, Colors = [[255, 0, 0]] };
        EffectSimulation sim = EffectLibrary.Simulate(wled, 12, ["Solid"], null)!;

        sim.Advance(50);

        // Solid is the same everywhere, so this is only saying the mapping stays in range.
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(255, sim.Segment.AsWired(i).R);
        }
    }
}
