using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The run filling in a few LEDs at a time until it is covered, then emptying the same way.
/// <para>
/// Unlike most effects this one is not a function of the clock at all: it keeps the picture it drew
/// last and changes a handful of LEDs in it each frame, so the same settings never give the same
/// picture twice. Which LEDs change is drawn at random, and an LED that has already changed is
/// passed over - with ten attempts per spawn, so as the run fills up the last few take longer to
/// land and the dissolve tails off rather than stopping dead.
/// </para>
/// <para>
/// Speed here is not how fast it dissolves. It sets how long the effect waits before turning round,
/// counted in frames; intensity is what sets the rate. Turn speed up far enough and it reverses
/// before it has finished, so the run never quite fills.
/// </para>
/// </summary>
internal static class Dissolve
{
    /// <param name="spawn">
    /// What a newly changed LED becomes. When it is the primary color the palette is used instead,
    /// which is how one function covers both a flat dissolve and a dissolve into a gradient.
    /// </param>
    public static void Render(EffectSegment segment, RgbColor spawn)
    {
        State state = segment.Scratch(() => new State(segment.Length, segment.Colors[1]));

        if (!state.Started)
        {
            // WLED does this on the effect's first frame; done where the state is, so it happens
            // exactly once however the effect is being driven.
            state.Started = true;
            segment.Aux0 = 1;
        }

        // Filling in or emptying out.
        bool filling = segment.Aux0 != 0;

        // One spawn attempt per fifteen LEDs, each of which intensity may or may not let through.
        for (int j = 0; j <= segment.Length / 15; j++)
        {
            if (segment.Random8() > segment.Intensity)
            {
                continue;
            }

            // Ten tries to find an LED that has not already changed, then give up for this frame.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int i = segment.Random16(segment.Length);

                if (filling)
                {
                    if (state.Pixels[i] != segment.Colors[1])
                    {
                        continue;
                    }

                    state.Pixels[i] = spawn == segment.Colors[0]
                        ? segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap)
                        : spawn;

                    break;
                }

                if (state.Pixels[i] == segment.Colors[1])
                {
                    continue;
                }

                state.Pixels[i] = segment.Colors[1];
                break;
            }
        }

        state.Pixels.CopyTo(segment.Pixels, 0);

        if (segment.Step > (uint)(255 - segment.Speed) + 15u)
        {
            segment.Aux0 = filling ? 0u : 1u;
            segment.Step = 0;
        }
        else
        {
            segment.Step++;
        }
    }

    /// <summary>
    /// The picture so far, which is the effect's whole state - WLED keeps it in the segment's own
    /// scratch buffer, one 32-bit color per LED.
    /// </summary>
    private sealed class State
    {
        public State(int length, RgbColor background)
        {
            Pixels = new RgbColor[length];
            Array.Fill(Pixels, background);
        }

        public RgbColor[] Pixels { get; }

        /// <summary>Whether the first frame has run, which is the one that sets it dissolving.</summary>
        public bool Started { get; set; }
    }
}

/// <summary>
/// The run dissolving into the palette and back out to the secondary color.
/// </summary>
public sealed class DissolveEffect : IWledEffect
{
    public string Name => "Dissolve";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        // The option checkbox swaps the palette for a fresh wheel color every frame, which scatters
        // the run rather than filling it in one color.
        Dissolve.Render(
            segment,
            segment.Option1 ? segment.ColorWheel(segment.Random8()) : segment.Colors[0]);
    }
}

/// <summary>
/// The same dissolve with every LED taking its own color off the wheel, so the run fills in confetti
/// rather than in one color.
/// </summary>
public sealed class DissolveRandomEffect : IWledEffect
{
    public string Name => "Dissolve Rnd";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        Dissolve.Render(segment, segment.ColorWheel(segment.Random8()));
    }
}
