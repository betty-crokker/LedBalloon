using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Sparks of palette color appearing at random and fading, each one blurred into its neighbors so it
/// reads as a burst rather than a dot.
/// <para>
/// Intensity is the spawning rate, and it is odds rather than a count: one throw of the dice per
/// twenty LEDs per frame. The two most recent sparks are held out of the blur and put back
/// afterwards, so a new spark is a hard point with a soft halo rather than being smeared away the
/// instant it appears.
/// </para>
/// </summary>
public sealed class FireworksEffect : IWledEffect
{
    public string Name => "Fireworks";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Spawn(segment);
    }

    /// <summary>
    /// The shared half: fade, blur, and maybe spawn. Rain calls this after shifting the run along.
    /// <para>
    /// Whether it blurs at all is decided by the step field, which looks arbitrary and is how the two
    /// effects tell each other apart. Fireworks never touches it, so it is always zero and Fireworks
    /// always blurs. Rain uses it as its shift timer, so it is almost never zero and Rain almost never
    /// blurs - which is exactly right, because a blurred streak is not a raindrop. The strip has four
    /// times as many sharp edges under Rain as the blurred version would leave.
    /// </para>
    /// </summary>
    internal static void Spawn(EffectSegment segment)
    {
        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        Sparks state = segment.Scratch(() => new Sparks());

        segment.FadeOut(128);

        if (segment.Step == 0)
        {
            // Held out and put back, so the newest sparks stay sharp while everything else softens.
            RgbColor? latest = Held(segment, state.Latest);
            RgbColor? previous = Held(segment, state.Previous);

            segment.Blur(16);

            if (latest is { } a)
            {
                segment.Pixels[state.Latest] = a;
            }

            if (previous is { } b)
            {
                segment.Pixels[state.Previous] = b;
            }
        }

        for (int i = 0; i < Math.Max(1, segment.Length / 20); i++)
        {
            if (segment.Random8(129 - (segment.Intensity >> 1)) != 0)
            {
                continue;
            }

            int at = segment.Random16(segment.Length);

            segment.Pixels[at] = segment.ColorFromPalette(segment.Random8());

            state.Previous = state.Latest;
            state.Latest = at;
        }
    }

    private static RgbColor? Held(EffectSegment segment, int at) =>
        (uint)at < (uint)segment.Length ? segment.Pixels[at] : null;

    /// <summary>Where the last two sparks landed, so the blur can be kept off them.</summary>
    internal sealed class Sparks
    {
        /// <summary>Out of range until a spark has happened, which is how the firmware starts too.</summary>
        public int Latest { get; set; } = ushort.MaxValue;

        public int Previous { get; set; } = ushort.MaxValue;
    }
}

/// <summary>
/// The same sparks, with the whole run sliding along one LED at a time underneath them - so a spark
/// appears and then drifts, which reads as rain running down.
/// <para>
/// The slide is on its own clock rather than the frame rate, which is what keeps it looking the same
/// on a long run as on a short one: WLED's <c>SPEED_FORMULA_L</c>, 27 ms on this roofline.
/// </para>
/// </summary>
public sealed class RainEffect : IWledEffect
{
    public string Name => "Rain";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        FireworksEffect.Sparks state = segment.Scratch(() => new FireworksEffect.Sparks());

        segment.Step += (uint)segment.FrameTime;

        int between = 5 + (50 * (255 - segment.Speed) / segment.Length);

        if (segment.Call != 0 && segment.Step > between)
        {
            segment.Step = 1;

            RgbColor first = segment.Pixels[0];

            for (int i = 0; i < segment.Length - 1; i++)
            {
                segment.Pixels[i] = segment.Pixels[i + 1];
            }

            // Round the end rather than off it, so the run never empties.
            segment.Pixels[segment.Length - 1] = first;

            // The remembered spark positions move with the pixels they belong to.
            state.Latest++;
            state.Previous++;

            if (state.Latest >= segment.Length)
            {
                state.Latest = 0;
            }

            if (state.Previous >= segment.Length)
            {
                state.Previous = 0;
            }
        }

        FireworksEffect.Spawn(segment);
    }
}

/// <summary>
/// A traffic light every three LEDs, cycling red, red and amber, green, amber.
/// <para>
/// The colors are hard-coded rather than taken from the slots - red is 0xFF0000 and amber 0xEECC00 -
/// which is the point of it, and the palette shows only in the background between the lights.
/// Intensity above 140 skips the red-and-amber phase, giving the American sequence rather than the
/// British one.
/// </para>
/// <para>
/// Speed sets the phase lengths unevenly: the two long phases scale five times harder than the two
/// short ones, so the amber stays brief however slowly the rest of it runs.
/// </para>
/// </summary>
public sealed class TrafficLightEffect : IWledEffect
{
    public string Name => "Traffic Light";

    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor Amber = new(0xEE, 0xCC, 0x00);
    private static readonly RgbColor Green = new(0, 255, 0);

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        for (int i = 0; i < segment.Length; i++)
        {
            segment.Pixels[i] = segment.ColorFromPalette(
                i, mapping: true, wrap: segment.SolidWrap, colorSlot: 1);
        }

        uint hold = 500;

        for (int i = 0; i < segment.Length - 2; i += 3)
        {
            switch (segment.Aux0)
            {
                case 0:
                    segment.Pixels[i] = Red;
                    hold = 150u + (100u * (255u - segment.Speed));
                    break;

                case 1:
                    segment.Pixels[i] = Red;
                    segment.Pixels[i + 1] = Amber;
                    hold = 150u + (20u * (255u - segment.Speed));
                    break;

                case 2:
                    segment.Pixels[i + 2] = Green;
                    hold = 150u + (100u * (255u - segment.Speed));
                    break;

                case 3:
                    segment.Pixels[i + 1] = Amber;
                    hold = 150u + (20u * (255u - segment.Speed));
                    break;
            }
        }

        if (now - segment.Step <= hold)
        {
            return;
        }

        segment.Aux0++;

        if (segment.Aux0 == 1 && segment.Intensity > 140)
        {
            segment.Aux0 = 2;
        }

        if (segment.Aux0 > 3)
        {
            segment.Aux0 = 0;
        }

        segment.Step = now;
    }
}

/// <summary>
/// A bar graph: intensity says what fraction of the run to light, and it fills or empties toward it
/// rather than jumping.
/// <para>
/// The slider runs to 200, not 100. Past the halfway mark it counts back down <em>and</em> fills from
/// the far end instead, so 150 lights half the run from the other side. Speed is how fast it catches
/// up with the target, in LEDs a frame, and at the top of the slider it jumps there at once.
/// </para>
/// </summary>
public sealed class PercentEffect : IWledEffect
{
    public string Name => "Percent";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        int percent = Math.Clamp((int)segment.Intensity, 0, 200);

        int target = percent < 100
            ? (int)Math.Round(segment.Length * percent / 100.0)
            : (int)Math.Round(segment.Length * (200 - percent) / 100.0);

        int step = segment.Speed == 255 ? 255 : 1 + ((segment.Speed * segment.Length) >> 11);

        var filled = (int)segment.Aux1;

        for (int i = 0; i < segment.Length; i++)
        {
            bool on = percent <= 100 ? i < filled : i >= segment.Length - filled;

            // One color for the whole bar rather than a gradient along it, which is what the option
            // checkbox switches to - and it takes its color from the reading itself.
            segment.Pixels[i] = !on
                ? segment.Colors[1]
                : segment.Option1
                    ? segment.ColorFromPalette(percent <= 100
                        ? FastLed.Map(percent, 0, 100, 0, 255)
                        : FastLed.Map(percent, 100, 200, 255, 0))
                    : segment.ColorFromPalette(i, mapping: true, wrap: segment.SolidWrap);
        }

        if (target > filled)
        {
            segment.Aux1 = (uint)Math.Min(target, filled + step);
        }
        else if (target < filled)
        {
            segment.Aux1 = (uint)Math.Max(target, filled - step);
        }
    }
}
