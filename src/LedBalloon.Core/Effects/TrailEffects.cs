using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Eight points travelling the run at once, each with a fading trail, each starting again at a random
/// moment after it falls off the end.
/// <para>
/// Not eight evenly spaced points: a comet that has finished waits for a one in <c>length</c> chance
/// each tick before restarting, so they drift out of step and sometimes several are on the run at
/// once and sometimes none. Every other comet takes the third color slot when one is set, which is
/// what lets it read as two kinds of comet rather than eight of the same.
/// </para>
/// </summary>
public sealed class MultiCometEffect : IWledEffect
{
    public string Name => "Multi Comet";

    private const int Comets = 8;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint cycleTime = 10u + (255u - segment.Speed);
        uint tick = now / cycleTime;

        // Moves on its own tick and does nothing in between, so the trails are the same length
        // however fast the controller draws.
        if (segment.Step == tick)
        {
            return;
        }

        int[] comets = segment.Scratch(() => new int[Comets]);

        segment.FadeOut((byte)((segment.Intensity / 2) + 128));

        for (int i = 0; i < Comets; i++)
        {
            if (comets[i] < segment.Length)
            {
                int at = comets[i];

                segment.SetPixel(at, segment.Colors[2] != RgbColor.Black && i % 2 == 0
                    ? segment.Colors[2]
                    : segment.ColorFromPalette(at, mapping: true, wrap: segment.SolidWrap));

                comets[i]++;
            }
            else if (segment.Random16(segment.Length) == 0)
            {
                comets[i] = 0;
            }
        }

        segment.Step = tick;
    }
}

/// <summary>
/// A meteor with a trail that decays unevenly, so it looks like burning debris rather than a fade.
/// <para>
/// The trail is a temperature per LED, as Fire 2012's is, but it is knocked down by a random amount
/// each frame and only on the frames intensity lets through - so neighbouring LEDs fall out of step
/// and the trail breaks up. Intensity is the trail length, backwards: turning it up means fewer LEDs
/// get faded each frame, so the trail lasts longer.
/// </para>
/// <para>
/// The head is 5% of the run, and the palette is read by <em>position</em> rather than by
/// temperature unless the Gradient option is set. Two of WLED's palettes are special-cased to draw at
/// full brightness, which is how Fire and Fire 2 stay looking like fire.
/// </para>
/// </summary>
public sealed class MeteorEffect : IWledEffect
{
    public string Name => "Meteor";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        byte[] trail = segment.Scratch(() => new byte[segment.Length]);

        int size = 1 + (segment.Length / 20);

        uint counter = now * (uint)((segment.Speed >> 2) + 8);
        var head = (int)((counter * (uint)segment.Length) >> 16);

        // "Colors only" blends its end back into its start, so it stops one entry short.
        int top = segment.PaletteId == 5 ? 239 : 255;

        for (int i = 0; i < segment.Length; i++)
        {
            if (segment.Random8() > 255 - segment.Intensity)
            {
                continue;
            }

            trail[i] = FastLed.Scale8(trail[i], (byte)(128 + segment.Random8(127)));

            int index = trail[i];
            int slot = 255;

            // Fire and Fire 2 are drawn at full brightness, the temperature going into the palette
            // index instead - which is what keeps them reading as flame rather than as a dimmed
            // gradient.
            int bright = segment.PaletteId is 35 or 36 ? 255 : trail[i];

            if (!segment.Option1)
            {
                slot = 0;
                index = FastLed.Map(i, 0, segment.Length, 0, top);
                bright = trail[i];
            }

            segment.Pixels[i] = segment.ColorFromPalette(
                index, colorSlot: slot, brightness: (byte)bright);
        }

        for (int j = 0; j < size; j++)
        {
            int at = (head + j) % segment.Length;

            trail[at] = (byte)top;

            int index = top;
            int slot = 255;

            if (!segment.Option1)
            {
                index = FastLed.Map(at, 0, segment.Length, 0, top);
                slot = 0;
            }

            segment.Pixels[at] = segment.ColorFromPalette(index, colorSlot: slot);
        }
    }
}

/// <summary>
/// The same meteor with a trail that drifts rather than decays: each LED's temperature wanders by a
/// few units a frame instead of being scaled down, so the trail thins out smoothly.
/// <para>
/// It also moves on an accumulated step rather than on the clock, which makes it one of the effects
/// whose speed depends on how fast the controller draws - and the wander is <c>+4 - random(24)</c>,
/// so it drifts downward on average but can brighten, which is what gives the trail its shimmer.
/// </para>
/// </summary>
public sealed class MeteorSmoothEffect : IWledEffect
{
    public string Name => "Meteor Smooth";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        byte[] trail = segment.Scratch(() => new byte[segment.Length]);

        int size = 1 + (segment.Length / 20);
        int head = FastLed.Map((int)((segment.Step >> 6) & 0xFF), 0, 255, 0, segment.Length - 1);

        int top = segment.PaletteId == 5 || !segment.Option1 ? 240 : 255;

        for (int i = 0; i < segment.Length; i++)
        {
            if (segment.Random8() > 255 - segment.Intensity)
            {
                continue;
            }

            int wander = trail[i] + 4 - segment.Random8(24);
            trail[i] = (byte)Math.Clamp(wander, 0, top);

            segment.Pixels[i] = Paint(segment, i, trail[i]);
        }

        for (int j = 0; j < size; j++)
        {
            int at = head + j;

            if (at >= segment.Length)
            {
                at -= segment.Length;
            }

            trail[at] = (byte)top;
            segment.Pixels[at] = Paint(segment, at, trail[at]);
        }

        segment.Step += (uint)segment.Speed + 1;
    }

    /// <summary>
    /// Gradient mode reads the palette by position and dims it by temperature; otherwise the
    /// temperature <em>is</em> the palette index and the color is drawn at full.
    /// </summary>
    private static RgbColor Paint(EffectSegment segment, int at, byte heat) =>
        segment.Option1
            ? segment.ColorFromPalette(at, mapping: true, brightness: heat)
            : segment.ColorFromPalette(heat, wrap: true, colorSlot: 255);
}
