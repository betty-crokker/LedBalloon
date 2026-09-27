using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// A lightning strike: a dim leader, then a burst of bright flashes over random stretches of the run,
/// then a long dark wait before the next strike.
/// <para>
/// The structure is one counter used two ways. It holds twice the number of flashes left, so its even
/// values are flashes and its odd values are the gaps between them - one counter that alternates
/// picture and pause without needing a second state. Counting down to two rather than to zero is what
/// makes the last gap the long one: at two, the delay is drawn from the speed slider and can run to
/// twenty-five seconds instead of the fifty to a hundred and fifty milliseconds between flashes.
/// </para>
/// <para>
/// Each flash covers a random stretch, from a random start to a random length, so no two look alike
/// and some of them cover the whole run. Brightness is 255 or 127 - <c>255 / random8(1, 3)</c>, which
/// has only two outcomes - except for the leader, which is fixed at 52 and is why a strike begins with
/// a faint glow before the bright part.
/// </para>
/// </summary>
public sealed class LightningEffect : IWledEffect
{
    public string Name => "Lightning";

    /// <summary>The faint leader stroke that starts a strike.</summary>
    private const byte LeaderBrightness = 52;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        int ledstart = segment.Random16(segment.Length);
        int ledlen = 1 + segment.Random16(segment.Length - ledstart);

        // Two outcomes, not a range: the divisor is one or two.
        var bri = (byte)(255 / segment.Random8(1, 3));

        if (segment.Aux1 == 0)
        {
            // A new strike. Between four and sixteen flashes depending on intensity, doubled so the
            // gaps between them have a count of their own.
            segment.Aux1 = (uint)segment.Random8(4, 4 + (segment.Intensity / 20)) * 2;

            bri = LeaderBrightness;
            segment.Aux0 = 200;
        }

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        if (segment.Aux1 > 3 && (segment.Aux1 & 1) == 0)
        {
            for (int i = ledstart; i < ledstart + ledlen; i++)
            {
                segment.SetPixel(i, segment.ColorFromPalette(
                    i, mapping: true, wrap: segment.SolidWrap, brightness: bri));
            }

            segment.Aux1--;
            segment.Step = now;
        }
        else if (now - segment.Step > segment.Aux0)
        {
            segment.Aux1--;

            if (segment.Aux1 < 2)
            {
                segment.Aux1 = 0;
            }

            segment.Aux0 = 50u + segment.Random8(100);

            if (segment.Aux1 == 2)
            {
                // The wait between strikes, which is what the speed slider actually sets - and at a
                // low speed it is long enough that the run sits dark for most of a minute.
                segment.Aux0 = (uint)segment.Random8(255 - segment.Speed) * 100u;
            }

            segment.Step = now;
        }
    }
}

/// <summary>
/// A pair of eyes opening somewhere along the run, blinking now and then, and closing again.
/// <para>
/// A proper state machine with five states, and the only effect here whose timing is written in
/// milliseconds throughout rather than in frames. The two sliders are both times: speed is how long
/// the eyes stay shut and intensity how long they stay open, which is why turning the speed up makes
/// this effect slower rather than faster.
/// </para>
/// <para>
/// The eyes are two blocks a thirty-second of the run wide with a gap of twice that between them, so
/// they grow with the run rather than staying a fixed size. They fade in over an eighth of their time
/// on, and while they are on they have a small chance each frame of blinking - but not in the first
/// second and not in the last, so a blink never swallows the opening or the closing.
/// </para>
/// </summary>
public sealed class HalloweenEyesEffect : IWledEffect
{
    public string Name => "Halloween Eyes";

    /// <summary>Not long enough open yet to blink, and not near enough the end to blink either.</summary>
    private const uint MinimumOnTime = 1024;

    private enum EyeState : byte
    {
        /// <summary>Choose a place, a color and a time, then open - all in one frame.</summary>
        InitializeOn,

        /// <summary>Open: fading in, then holding, and maybe deciding to blink.</summary>
        On,

        /// <summary>Shut for a moment, on a deadline rather than a duration.</summary>
        Blink,

        /// <summary>Choose how long to stay shut, then shut - again in one frame.</summary>
        InitializeOff,

        /// <summary>Shut, with nothing to draw.</summary>
        Off,
    }

    private sealed class State
    {
        public EyeState Eyes;
        public byte Color;
        public int StartPos;
        public ushort Duration;
        public uint StartTime;
        public uint BlinkEndTime;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 1)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        int space = Math.Max(2, segment.Length >> 5);
        int width = space / 2;
        int eyeLength = (2 * width) + space;

        // No room for a face, so show nothing but the primary.
        if (eyeLength >= segment.Length)
        {
            segment.Fill(segment.Colors[0]);
            return;
        }

        State data = segment.Scratch(() => new State());

        if (!segment.Option2)
        {
            segment.Fill(segment.Colors[1]);
        }

        // A duration of zero would divide by zero working out the fade, and it is zero on the very
        // first frame.
        uint duration = Math.Max(1u, data.Duration);
        uint elapsed = now - data.StartTime;

        switch (data.Eyes)
        {
            case EyeState.InitializeOn:
                data.StartPos = segment.Random16(segment.Length - eyeLength - 1);
                data.Color = segment.Random8();

                duration = 128u + segment.Random16(segment.Intensity * 64);
                data.Duration = (ushort)duration;
                data.Eyes = EyeState.On;

                goto case EyeState.On;

            case EyeState.On:
            {
                int second = data.StartPos + width + space;

                // Clamped again every frame, so pulling the slider down shortens the eyes that are
                // already open instead of only the next pair.
                duration = Math.Min(duration, 128u + ((uint)segment.Intensity * 64u));

                RgbColor background = segment.Colors[1];
                RgbColor eye = segment.ColorFromPalette(data.Color);
                RgbColor color = eye;

                // Eight steps of 256 across the whole time on, so the fade-in takes an eighth of it
                // however long that is.
                uint fadeIn = elapsed * 256u * 8u / duration;

                if (fadeIn < 256)
                {
                    color = EffectSegment.Blend(background, eye, (byte)fadeIn);
                }
                else if (elapsed > MinimumOnTime)
                {
                    uint remaining = elapsed >= duration ? 0 : duration - elapsed;

                    if (remaining > MinimumOnTime && segment.Random8() < 4)
                    {
                        color = background;
                        data.Eyes = EyeState.Blink;
                        data.BlinkEndTime = now + segment.Random8(8, 128);
                    }
                }

                if (color != background)
                {
                    for (int i = 0; i < width; i++)
                    {
                        segment.SetPixel(data.StartPos + i, color);
                        segment.SetPixel(second + i, color);
                    }
                }

                break;
            }

            case EyeState.Blink:
                // A deadline rather than a duration, because a blink is not supposed to stretch when
                // the slider moves.
                if (now >= data.BlinkEndTime)
                {
                    data.Eyes = EyeState.On;
                }

                break;

            case EyeState.InitializeOff:
            {
                uint shut = (uint)segment.Speed * 128u;

                duration = shut + segment.Random16((int)shut);
                data.Duration = (ushort)duration;
                data.Eyes = EyeState.Off;

                goto case EyeState.Off;
            }

            case EyeState.Off:
                duration = Math.Min(duration, 2u * (uint)segment.Speed * 128u);
                break;
        }

        if (elapsed > duration)
        {
            data.Eyes = data.Eyes switch
            {
                EyeState.InitializeOn or EyeState.On or EyeState.Blink => EyeState.InitializeOff,
                _ => EyeState.InitializeOn,
            };

            data.StartTime = now;
        }
    }
}
