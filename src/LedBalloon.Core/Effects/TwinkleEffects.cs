using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Pixels lighting up one at a time, each brightening then fading, in colors off the palette.
/// <para>
/// The first ported effect that cannot be drawn the same twice. Where the next pixel lights is
/// chosen at random every frame, so the photo and the house will never agree pixel for pixel - only
/// on how many are lit and how fast they come and go. The randomness is seeded here so that at
/// least the preview is repeatable and can be measured.
/// </para>
/// <para>
/// Each LED needs one bit of memory saying whether it is on its way up or on its way down, which is
/// what the per-run scratch is for.
/// </para>
/// </summary>
public sealed class ColorTwinklesEffect : IWledEffect
{
    public string Name => "Colortwinkles";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        bool[] rising = segment.Scratch(() => new bool[segment.Length]);

        // Speed is the fade, intensity is how often a new one starts. Both slow right down on a
        // strip that is already dim, so a low brightness does not wash the whole thing out.
        var fadeUp = (byte)(8 + (segment.Speed >> 2));
        var fadeDown = (byte)(8 + (segment.Speed >> 3));

        for (int i = 0; i < segment.Length; i++)
        {
            RgbColor pixel = segment.Pixels[i];

            if (rising[i])
            {
                RgbColor step = EffectSegment.Fade(pixel, fadeUp);

                var brighter = new RgbColor(
                    (byte)Math.Min(255, pixel.R + step.R),
                    (byte)Math.Min(255, pixel.G + step.G),
                    (byte)Math.Min(255, pixel.B + step.B));

                // Once any channel tops out it has arrived, and starts back down.
                if (brighter.R == 255 || brighter.G == 255 || brighter.B == 255)
                {
                    rising[i] = false;
                }

                segment.Pixels[i] = brighter;
            }
            else
            {
                segment.Pixels[i] = EffectSegment.Scale(pixel, (byte)(255 - fadeDown));
            }
        }

        // One new twinkle per fifty LEDs per frame at most, and only onto a pixel that is dark -
        // which is what stops them piling up on top of each other.
        for (int spawn = 0; spawn <= segment.Length / 50; spawn++)
        {
            if (segment.Random.Next(256) > segment.Intensity)
            {
                continue;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                int at = segment.Random.Next(segment.Length);

                if (segment.Pixels[at] is { R: 0, G: 0, B: 0 })
                {
                    rising[at] = true;

                    segment.Pixels[at] = segment.ColorFromPalette(
                        segment.Random.Next(256), brightness: 64, blend: false);

                    break;
                }
            }
        }
    }
}

/// <summary>
/// Twinkling that repeats: every LED runs its own slow cycle, and the cycles are staggered so the
/// run never settles.
/// <para>
/// The trick is that it is not random at all, despite looking it. Each frame starts the same
/// pseudo-random sequence from the same seed and walks it once per LED, so every LED gets the same
/// offset and speed it got last frame without anything being stored. That makes this one of the
/// few effects of its kind that a preview can match exactly.
/// </para>
/// <para>
/// The two variants differ in one curve. Twinklecat snaps each twinkle to full and fades it out;
/// Twinklefox ramps up over a third of the cycle and down over the other two, which is a filament
/// warming rather than a switch closing. The two curves enclose the same area, so the run averages
/// the same brightness either way - what changes is whether any LED is ever on its way up.
/// </para>
/// </summary>
internal static class TwinkleFox
{
    public static void Render(EffectSegment segment, uint now, bool cat)
    {
        // Reset every frame on purpose: the sequence has to come out the same each time or every
        // LED would be somewhere different from one frame to the next.
        ushort random = 11337;

        segment.Aux0 = segment.Speed > 100
            ? (uint)(3 + ((255 - segment.Speed) >> 3))
            : (uint)(22 + ((100 - segment.Speed) >> 1));

        RgbColor background = segment.Colors[1];
        byte backgroundLight = EffectSegment.AverageLight(background);

        // The background is dimmed hard so the twinkles read against it rather than competing.
        background = EffectSegment.Fade(
            background, backgroundLight > 64 ? (byte)16 : backgroundLight > 16 ? (byte)64 : (byte)86);

        byte backgroundBrightness = EffectSegment.AverageLight(background);
        bool hasBackground = background.R + background.G + background.B > 0;

        for (int i = 0; i < segment.Length; i++)
        {
            unchecked
            {
                random = (ushort)((random * 2053) + 1384);
            }

            ushort clockOffset = random;

            unchecked
            {
                random = (ushort)((random * 2053) + 1384);
            }

            // Each LED runs its clock somewhere between one and just under three times normal.
            uint speedMultiplier = (uint)(((((random & 0xFF) >> 4) + (random & 0x0F)) & 0x0F) + 0x08);

            uint clock;
            unchecked
            {
                clock = ((now * speedMultiplier) >> 3) + clockOffset;
            }

            RgbColor twinkle = OneTwinkle(segment, clock, (byte)(random >> 8), cat);

            int delta = EffectSegment.AverageLight(twinkle) - backgroundBrightness;

            segment.Pixels[i] = delta >= 32 || !hasBackground
                ? twinkle
                : delta > 0
                    ? EffectSegment.Blend(background, twinkle, (byte)Math.Min(255, delta * 8))
                    : background;
        }
    }

    /// <summary>What one LED is doing at its own private time.</summary>
    private static RgbColor OneTwinkle(EffectSegment segment, uint clock, byte salt, bool cat)
    {
        uint ticks = clock / Math.Max(1, segment.Aux0);
        var fastCycle = (byte)ticks;

        var slowCycle = (ushort)((ticks >> 8) + salt);
        slowCycle += FastLed.Sin8((byte)slowCycle);

        unchecked
        {
            slowCycle = (ushort)((slowCycle * 2053) + 1384);
        }

        var slow = (byte)((slowCycle & 0xFF) + (slowCycle >> 8));

        // Intensity is how many are lit at once, from none to all.
        int density = (segment.Intensity >> 5) + 1;

        int bright = 0;

        if (((slow & 0x0E) / 2) < density)
        {
            if (cat)
            {
                // Full brightness the instant it starts, then straight back down.
                bright = 255 - fastCycle;
            }
            else
            {
                // Up over the first third and down over the rest - a triangle with a fast attack and
                // a slow decay, which is the difference between the two effects.
                bright = fastCycle < 86
                    ? fastCycle * 3
                    : 255 - ((fastCycle - 86) + ((fastCycle - 86) / 2));
            }
        }

        if (bright <= 0)
        {
            return RgbColor.Black;
        }

        var hue = (byte)(slow - salt);
        RgbColor color = segment.ColorFromPalette(hue, brightness: (byte)bright, blend: false);

        if (segment.Option1)
        {
            return color;
        }

        // Fading twinkles drift toward red, the way a filament bulb does on its way out.
        if (fastCycle >= 128)
        {
            int cooling = (fastCycle - 128) >> 4;

            color = new RgbColor(
                color.R,
                FastLed.QSub8(color.G, cooling),
                FastLed.QSub8(color.B, cooling * 2));
        }

        return color;
    }
}

/// <summary>Twinkles that ramp up and fade down, each LED on its own slow clock.</summary>
public sealed class TwinkleFoxEffect : IWledEffect
{
    public string Name => "Twinklefox";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        TwinkleFox.Render(segment, now, cat: false);
    }
}

/// <summary>The same twinkles snapping to full and fading out rather than easing up.</summary>
public sealed class TwinkleCatEffect : IWledEffect
{
    public string Name => "Twinklecat";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        TwinkleFox.Render(segment, now, cat: true);
    }
}

/// <summary>
/// Rings spreading outward from points along the run, like rain on water.
/// <para>
/// Each ripple is two waves travelling in opposite directions from where it started, fading as they
/// go. They are kept in a small fixed pool - one per four LEDs, capped - and a slot that has
/// finished waits to be started again somewhere else.
/// </para>
/// </summary>
internal static class Ripples
{
    /// <summary>The most ripples at once, the same ceiling the firmware uses.</summary>
    private const int MaxRipples = 100;

    private sealed class Ripple
    {
        public int State;
        public int Position;
        public byte Color;
    }

    /// <summary>
    /// Draws the ripples over whatever the caller has already put down - the two effects that use this
    /// differ only in what that background is.
    /// </summary>
    public static void Render(EffectSegment segment)
    {
        int pool = Math.Min(1 + (segment.Length >> 2), MaxRipples);
        Ripple[] ripples = segment.Scratch(() => CreatePool(pool));

        foreach (Ripple ripple in ripples)
        {
            if (ripple.State == 0)
            {
                // Intensity decides how often a new one starts. The odds are per slot per frame,
                // so more slots means more ripples at the same setting.
                if (segment.Random.Next(5100 + 10000) <= segment.Intensity)
                {
                    ripple.State = 1;
                    ripple.Position = segment.Random.Next(segment.Length);
                    ripple.Color = (byte)segment.Random.Next(256);
                }

                continue;
            }

            int decay = (segment.Speed >> 4) + 1;
            RgbColor color = segment.ColorFromPalette(ripple.Color, brightness: 255);

            int propagation = ((ripple.State / decay) - 1) * (segment.Speed + 1);
            int spread = propagation >> 8;
            int within = propagation & 0xFF;

            // Rises quickly at the start, then fades over the rest of its life.
            int amplitude = ripple.State < 17
                ? FastLed.TriWave8((byte)((ripple.State - 1) * 8))
                : Map(ripple.State, 17, 255, 255, 2);

            int left = ripple.Position - spread - 1;
            int right = ripple.Position + spread + 2;

            for (int v = 0; v < 4; v++)
            {
                byte magnitude = FastLed.Scale8(
                    FastLed.CubicWave8((byte)((within >> 2) + (v * 64))), (byte)amplitude);

                Mix(segment, left + v, color, magnitude);
                Mix(segment, right - v, color, magnitude);
            }

            ripple.State += decay;

            if (ripple.State > 254)
            {
                ripple.State = 0;
            }
        }
    }

    private static Ripple[] CreatePool(int size)
    {
        var pool = new Ripple[size];

        for (int i = 0; i < size; i++)
        {
            pool[i] = new Ripple();
        }

        return pool;
    }

    private static void Mix(EffectSegment segment, int at, RgbColor color, byte amount)
    {
        if ((uint)at >= (uint)segment.Length)
        {
            return;
        }

        segment.Pixels[at] = EffectSegment.Blend(segment.Pixels[at], color, amount);
    }

    private static int Map(int value, int fromLow, int fromHigh, int toLow, int toHigh) =>
        fromHigh == fromLow
            ? toLow
            : ((value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow)) + toLow;
}

/// <summary>
/// Rings spreading out from points along the run, over the secondary color or over what was there.
/// </summary>
public sealed class RippleEffect : IWledEffect
{
    public string Name => "Ripple";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        // Overlay leaves what was there fading underneath; otherwise the run is cleared each frame.
        if (segment.Option2)
        {
            segment.FadeOut(250);
        }
        else
        {
            segment.Fill(segment.Colors[1]);
        }

        Ripples.Render(segment);
    }
}

/// <summary>
/// The same rings over a background that wanders round the color wheel one step at a time.
/// <para>
/// The wander is a random walk rather than a cycle: it picks a target off the wheel and creeps toward
/// it a step per frame, then picks another when it arrives - so the background drifts unevenly and
/// sometimes doubles back. And it is knocked down to a twentieth of its brightness, because it is a
/// background and the ripples have to show over it.
/// </para>
/// </summary>
public sealed class RippleRainbowEffect : IWledEffect
{
    public string Name => "Ripple Rainbow";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        if (segment.Call == 0)
        {
            segment.Aux0 = segment.Random8();
            segment.Aux1 = segment.Random8();
        }

        if (segment.Aux0 == segment.Aux1)
        {
            segment.Aux1 = segment.Random8();
        }
        else if (segment.Aux1 > segment.Aux0)
        {
            segment.Aux0++;
        }
        else
        {
            segment.Aux0--;
        }

        segment.Fill(EffectSegment.Blend(
            segment.ColorWheel((byte)segment.Aux0), RgbColor.Black, 235));

        Ripples.Render(segment);
    }
}
