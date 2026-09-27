using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// Perlin noise read through a palette that the effect invents for itself and then crawls toward.
/// <para>
/// Every few seconds it builds a new four-color palette out of one random hue and three offsets from it,
/// so the four colors always belong together - and then it never switches to it. What is on the strip
/// crawls toward the new palette one channel-step at a time, which takes a couple of hundred frames, so
/// the colors are always somewhere between the last two ideas the effect had.
/// </para>
/// <para>
/// Set a palette and all of that stops mattering: the crawl still runs but its result is overwritten by
/// the chosen palette every frame. So on any palette but Default this is simply noise, and the effect it
/// was written to be only exists on Default.
/// </para>
/// </summary>
public sealed class NoisePalEffect : IWledEffect
{
    public string Name => "Noise Pal";

    /// <summary>How much of the palette can move in one frame - 48 of its 48 channels.</summary>
    private const int ChangesPerFrame = 48;

    private sealed class State
    {
        /// <summary>What is on the strip, which is always on its way somewhere.</summary>
        public RgbColor[] Current = new RgbColor[Palette16.Entries];

        /// <summary>Where it is going.</summary>
        public RgbColor[] Target = new RgbColor[Palette16.Entries];
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        State state = segment.Scratch(() => new State());

        int scale = 15 + (segment.Intensity >> 2);
        uint changeEvery = 4000u + ((uint)segment.Speed * 10u);

        if (now - segment.Step > changeEvery)
        {
            segment.Step = now;

            // One hue, and everything else placed relative to it - which is why the palettes it invents
            // always look deliberate rather than like four random colors.
            byte baseHue = segment.Random8();

            state.Target = Palette16.FromFour(
                FastLed.Hsv2Rgb((byte)(baseHue + segment.Random8(64)), 255, segment.Random8(128, 255)),
                FastLed.Hsv2Rgb((byte)(baseHue + 128), 255, segment.Random8(128, 255)),
                FastLed.Hsv2Rgb((byte)(baseHue + segment.Random8(92)), 192, segment.Random8(128, 255)),
                FastLed.Hsv2Rgb((byte)(baseHue + segment.Random8(92)), 255, segment.Random8(128, 255)));
        }

        Palette16.BlendToward(state.Current, state.Target, ChangesPerFrame);

        RgbColor[] palette = state.Current;

        // A chosen palette replaces the one being crawled toward every frame, so the crawl becomes
        // invisible and this turns into plain noise.
        if (segment.PaletteId > 0 && segment.Palette is not null)
        {
            palette = [.. Enumerable.Range(0, Palette16.Entries)
                .Select(k => segment.ColorFromPalette(k * 16, wrap: true, blend: false))];
        }

        for (int i = 0; i < segment.Length; i++)
        {
            // Both axes walk at the same rate, so the noise slides along the run rather than boiling in
            // place - the second axis is the first plus a drift.
            byte index = Perlin.Noise8(
                (ushort)(i * scale), (ushort)(segment.Aux0 + (uint)(i * scale)));

            segment.Pixels[i] = FastLed.ColorFromPalette16(palette, index, 255);
        }

        // One to four a frame on its own slow sine, so the drift is never quite steady.
        segment.Aux0 += FastLed.BeatSin8(10, 1, 4, now);
    }
}

/// <summary>
/// A television seen through a window: the whole run one color at a time, cutting and fading the way a
/// programme does.
/// <para>
/// The only effect here that lights every LED the same color, and the only one whose subject is timing
/// rather than shape. It picks a "scene" - a hue, a saturation and a brightness it will stay near for
/// tens of seconds - and then within that scene it changes color every quarter second to two and a half
/// seconds, sometimes fading and sometimes cutting outright. A hard cut three times in ten is what makes
/// it read as a television rather than as a lamp.
/// </para>
/// <para>
/// Its color arithmetic is nobody else's here. Rather than FastLED's rainbow hue it uses Adafruit's
/// constant-brightness conversion, which works the three channels out of a five entry table indexed by
/// which third of the hue circle it is in - and then puts the result through the gamma table, which is
/// the only place in this effect anything is done for the sake of how it looks rather than how it
/// behaves.
/// </para>
/// <para>
/// The scene length is meant to be five to fifteen minutes and is not. It is drawn with a call whose
/// bounds are sixteen bits wide, and at any speed above about a fifth of the slider the bounds it is
/// given overflow - at the middle of the slider the intended 150000 to 450000 milliseconds become 18928
/// to 56784, so scenes last twenty seconds to a minute. Kept, because it is what the strip does.
/// </para>
/// </summary>
public sealed class TvSimulatorEffect : IWledEffect
{
    public string Name => "TV Simulator";

    private sealed class State
    {
        public uint TotalTime;
        public uint FadeTime;
        public uint StartTime;
        public ushort SliderValues;
        public uint SceneStart;
        public uint SceneDuration;
        public ushort SceneHue;
        public byte SceneSaturation;
        public byte SceneBrightness;
        public RgbColor Color;

        /// <summary>The previous color, sixteen bits a channel, which a fade starts from.</summary>
        public ushort PreviousRed;
        public ushort PreviousGreen;
        public ushort PreviousBlue;
    }

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        State tv = segment.Scratch(() => new State());

        int colorSpeed = FastLed.Map(segment.Speed, 0, 255, 1, 20);
        int colorIntensity = FastLed.Map(segment.Intensity, 0, 255, 10, 30);

        // Both sliders packed into one word, so that moving either one starts a new scene.
        var sliders = (ushort)((segment.Speed << 8) | segment.Intensity);

        if (sliders != tv.SliderValues)
        {
            tv.SliderValues = sliders;
            segment.Aux1 = 0;
        }

        if (now - tv.SceneStart >= tv.SceneDuration || segment.Aux1 == 0)
        {
            tv.SceneStart = now;

            // The bounds are sixteen bits wide and these products are not, so what comes out is not
            // minutes but tens of seconds. Both the truncation and the width of the subtraction matter:
            // at some slider positions the truncated upper bound comes out below the lower one, and the
            // gap between them wraps rather than going negative.
            var shortest = (ushort)(60 * 250 * colorSpeed);
            var longest = (ushort)(60 * 750 * colorSpeed);

            tv.SceneDuration = (ushort)(shortest + segment.Random16((ushort)(longest - shortest)));

            tv.SceneHue = segment.Random16(0, 768);
            tv.SceneSaturation = segment.Random8(100, 130 + colorIntensity);
            tv.SceneBrightness = segment.Random8(200, 240);

            segment.Aux1 = 1;
            segment.Aux0 = 0;
        }

        if (segment.Aux0 == 0)
        {
            tv.Color = SceneColor(segment, tv, colorIntensity);
        }

        // Gamma corrected and widened to sixteen bits a channel, which is what the fade runs in.
        int newRed = Gamma.Correct(tv.Color.R) * 257;
        int newGreen = Gamma.Correct(tv.Color.G) * 257;
        int newBlue = Gamma.Correct(tv.Color.B) * 257;

        int red = newRed, green = newGreen, blue = newBlue;

        if (segment.Aux0 == 0)
        {
            segment.Aux0 = 1;

            // A quarter second to two and a half, with the fade taking anywhere from none of it to all
            // of it - and forced to none three times in ten, which is the cut.
            tv.TotalTime = segment.Random16(250, 2500);
            tv.FadeTime = segment.Random16((int)tv.TotalTime);

            if (segment.Random8(10) < 3)
            {
                tv.FadeTime = 0;
            }

            tv.StartTime = now;
        }

        uint elapsed = now - tv.StartTime;

        if (elapsed < tv.FadeTime)
        {
            red = FastLed.Map((int)elapsed, 0, (int)tv.FadeTime, tv.PreviousRed, newRed);
            green = FastLed.Map((int)elapsed, 0, (int)tv.FadeTime, tv.PreviousGreen, newGreen);
            blue = FastLed.Map((int)elapsed, 0, (int)tv.FadeTime, tv.PreviousBlue, newBlue);
        }

        segment.Fill(new RgbColor((byte)(red >> 8), (byte)(green >> 8), (byte)(blue >> 8)));

        if (elapsed >= tv.TotalTime)
        {
            // Remember where the next fade starts from, gamma corrected rather than raw - so a fade runs
            // between two corrected colors and is not corrected again. And it is the new color that is
            // remembered, not what is on the strip, which during a fade are not the same thing.
            tv.PreviousRed = (ushort)newRed;
            tv.PreviousGreen = (ushort)newGreen;
            tv.PreviousBlue = (ushort)newBlue;

            segment.Aux0 = 0;
        }
    }

    /// <summary>
    /// A color near this scene's, by Adafruit's constant-brightness conversion rather than by a hue
    /// wheel.
    /// </summary>
    private static RgbColor SceneColor(EffectSegment segment, State tv, int colorIntensity)
    {
        // The hue wanders either way, and the wrap at 767 is done differently in each direction - which
        // is not symmetrical and is what the firmware does.
        byte step = segment.Random8(4 * colorIntensity);

        int hue = segment.Random8() < 128
            ? step < tv.SceneHue ? tv.SceneHue - step : 767 - tv.SceneHue - step
            : step + tv.SceneHue < 767 ? tv.SceneHue + step : tv.SceneHue + step - 767;

        step = segment.Random8(2 * colorIntensity);
        var saturation = (byte)Math.Max(0, tv.SceneSaturation - step);

        step = segment.Random8(100);
        var brightness = (byte)Math.Max(0, tv.SceneBrightness - step);

        // Which third of the circle, and how far into it. The five entry table holds the three channel
        // values twice over so that one offset picks all three.
        int third = (hue >> 8) % 3;

        var x = (byte)((((hue & 255) * saturation >> 8) * brightness) >> 8);
        var s = (byte)((256 - saturation) * brightness >> 8);

        var temp = new byte[5];
        temp[0] = temp[3] = s;
        temp[1] = temp[4] = (byte)(x + s);
        temp[2] = (byte)(brightness - x);

        return new RgbColor(temp[third + 2], temp[third + 1], temp[third]);
    }
}
