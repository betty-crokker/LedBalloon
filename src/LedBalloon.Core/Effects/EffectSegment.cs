using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// One run's worth of LEDs plus the settings an effect reads, which together are everything an
/// effect gets to see.
/// <para>
/// The pixels are the reason this exists. Drawing a run used to be a function of position - ask it
/// what color the LED two thirds along is and it answers - which works for anything that is a
/// gradient sliding along a strip and for nothing else. Plenty of effects read the frame they drew
/// last: trails, fades, sparks that decay. Those cannot be answered from a position, only
/// accumulated, so a run needs somewhere to accumulate.
/// </para>
/// </summary>
public sealed class EffectSegment
{
    public EffectSegment(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        Length = length;
        Pixels = new RgbColor[length];
    }

    /// <summary>How many LEDs. WLED calls this SEGLEN.</summary>
    public int Length { get; }

    /// <summary>What the run is showing right now, and what the next frame is drawn over.</summary>
    public RgbColor[] Pixels { get; }

    /// <summary>The three color slots, primary first. WLED's SEGCOLOR.</summary>
    public RgbColor[] Colors { get; set; } = [RgbColor.White, RgbColor.Black, RgbColor.Black];

    /// <summary>
    /// How long one frame lasts on the controller driving this run.
    /// <para>
    /// Effects read it: Blink measures its duty cycle in frames, and anything that fades reaches a
    /// given darkness after a number of them rather than after an amount of time.
    /// </para>
    /// </summary>
    public int FrameMilliseconds { get; set; } = 1000 / 42;

    /// <summary>
    /// Scratch that survives between frames, WLED's <c>SEGENV.step</c>. Effects that need to know
    /// what they did last time keep it here.
    /// </summary>
    public uint Step { get; set; }

    /// <summary>Frames drawn since this run started, WLED's <c>SEGENV.call</c>.</summary>
    public uint Call { get; set; }

    /// <summary>Two more scratch values that survive between frames, WLED's <c>aux0</c> and <c>aux1</c>.</summary>
    public uint Aux0 { get; set; }

    public uint Aux1 { get; set; }

    /// <summary>The effect option checkboxes, WLED's <c>check1</c> and <c>check2</c>.</summary>
    public bool Option1 { get; set; }

    public bool Option2 { get; set; }

    /// <summary>
    /// Randomness, seeded rather than free-running.
    /// <para>
    /// Some effects sprinkle pixels about at random, so a preview of one can never match the strip
    /// frame for frame - only in how much it lights and how often. Seeding it at least makes the
    /// preview repeatable, so a test that measures those can be believed.
    /// </para>
    /// </summary>
    public Random Random { get; set; } = new(11337);

    private object? _scratch;

    /// <summary>
    /// Whatever this effect needs to remember between frames, created the first time it asks.
    /// <para>
    /// Stands in for WLED's <c>SEGENV.data</c>, which is a raw byte array an effect casts to
    /// whatever it likes. Typed here instead, because the reason the byte array exists - a
    /// microcontroller with no heap to spare - does not apply.
    /// </para>
    /// </summary>
    public T Scratch<T>(Func<T> create) where T : class
    {
        ArgumentNullException.ThrowIfNull(create);

        if (_scratch is not T existing)
        {
            existing = create();
            _scratch = existing;
        }

        return existing;
    }

    /// <summary>Whether the run is wired back to front, which some effects mirror themselves for.</summary>
    public bool Reverse { get; set; }

    public byte Speed { get; set; } = 128;
    public byte Intensity { get; set; } = 128;
    public byte Custom1 { get; set; } = 128;
    public byte Custom2 { get; set; } = 128;
    public byte Custom3 { get; set; } = 16;

    /// <summary>Which palette, by WLED's numbering. Zero means "use the color slots instead".</summary>
    public int PaletteId { get; set; }

    /// <summary>
    /// The gradient behind <see cref="PaletteId"/>, as the controller reports it.
    /// <para>
    /// Read from <c>/json/palx</c>, which already serves the sixteen-entry gamma-corrected form the
    /// effect samples rather than the raw palette file - so this needs no correcting on the way in.
    /// </para>
    /// </summary>
    public WledPalette? Palette { get; set; }

    /// <summary>
    /// The color an effect gets when it asks the palette for index <paramref name="index"/>.
    /// </summary>
    /// <param name="mapping">
    /// True when the index is an LED position rather than a palette position, so it is stretched
    /// across the palette first. That is how an effect paints the whole gradient along the run.
    /// </param>
    /// <param name="wrap">
    /// False - the usual - stops the lookup short of the palette's end, so the last color does not
    /// blend back round into the first.
    /// </param>
    /// <param name="colorSlot">Which color slot stands in when the run is not on a palette.</param>
    /// <param name="brightness">Scales the result, which some effects use to pulse.</param>
    /// <param name="blend">
    /// False lands on one of the palette's own stops instead of interpolating between them.
    /// </param>
    public RgbColor ColorFromPalette(
        int index,
        bool mapping = false,
        bool wrap = false,
        int colorSlot = 0,
        byte brightness = 255,
        bool blend = true)
    {
        if (PaletteId == 0 || Palette is null)
        {
            RgbColor flat = Colors[Math.Clamp(colorSlot, 0, Colors.Length - 1)];
            return brightness == 255 ? flat : Fade(flat, brightness);
        }

        int at = index;

        if (mapping && Length > 1)
        {
            at = index * 255 / (Length - 1);
        }

        byte wrapped = (byte)(at & 0xFF);
        if (!wrap)
        {
            wrapped = FastLed.Scale8(wrapped, 240);
        }

        // Without blending the lookup lands on one of the palette's sixteen stops rather than
        // between two of them, which is what gives a twinkle its distinct colors instead of a wash.
        double position = blend ? wrapped / 255d : (wrapped >> 4) / 15d;

        RgbColor color = Palette.ColorAt(position, Colors[0], Colors[1], Colors[2]);

        return brightness == 255 ? color : Fade(color, brightness);
    }

    /// <summary>How bright a color reads overall, which is how WLED decides what shows through what.</summary>
    public static byte AverageLight(RgbColor color) =>
        (byte)((color.R + color.G + color.B) / 3);

    /// <summary>Scales a color down with no floor, unlike <see cref="Fade"/>.</summary>
    public static RgbColor Scale(RgbColor color, byte amount) => new(
        (byte)(color.R * amount >> 8),
        (byte)(color.G * amount >> 8),
        (byte)(color.B * amount >> 8));

    /// <summary>Scales a color down, never quite to nothing while it is still lit.</summary>
    public static RgbColor Fade(RgbColor color, byte amount)
    {
        if (amount == 0)
        {
            return RgbColor.Black;
        }

        return new RgbColor(Down(color.R), Down(color.G), Down(color.B));

        byte Down(byte channel)
        {
            int scaled = channel * amount >> 8;

            // The "video" guard: a lit channel never scales all the way to black, so a dimmed
            // color keeps its hue instead of losing its weakest component first.
            return (byte)(scaled == 0 && channel != 0 ? 1 : scaled);
        }
    }

    /// <summary>Mixes two colors, where 0 is all of the first and 255 all of the second.</summary>
    public static RgbColor Blend(RgbColor first, RgbColor second, byte amount) =>
        amount switch
        {
            0 => first,
            255 => second,
            _ => new RgbColor(
                (byte)(((second.R * amount) + (first.R * (255 - amount))) >> 8),
                (byte)(((second.G * amount) + (first.G * (255 - amount))) >> 8),
                (byte)(((second.B * amount) + (first.B * (255 - amount))) >> 8)),
        };

    /// <summary>Lights one LED, ignoring one off the end rather than throwing.</summary>
    public void SetPixel(int index, RgbColor color)
    {
        if ((uint)index < (uint)Length)
        {
            Pixels[index] = color;
        }
    }

    /// <summary>Paints the whole run one color.</summary>
    public void Fill(RgbColor color) => Array.Fill(Pixels, color);

    /// <summary>
    /// Pulls every LED a fraction of the way toward the secondary color, which is what leaves a
    /// trail behind anything that moves.
    /// <para>
    /// Once per frame, so how long a trail looks depends on how fast the controller is drawing.
    /// That is not a detail worth smoothing away: it is why the same effect trails differently on
    /// a short run and a long one.
    /// </para>
    /// </summary>
    /// <param name="rate">WLED's rate, where larger fades slower. 254 halves the distance each frame.</param>
    public void FadeOut(byte rate)
    {
        int adjusted = (256 - rate) >> 1;
        int mappedRate = 256 / (adjusted + 1);

        RgbColor background = Colors[1];

        for (int i = 0; i < Pixels.Length; i++)
        {
            RgbColor pixel = Pixels[i];

            Pixels[i] = new RgbColor(
                FastLed.FadeChannel(pixel.R, background.R, mappedRate),
                FastLed.FadeChannel(pixel.G, background.G, mappedRate),
                FastLed.FadeChannel(pixel.B, background.B, mappedRate));
        }
    }

    /// <summary>Copies the settings a preset or live state describes onto this run.</summary>
    public void Adopt(WledSegment wled, WledPalette? palette)
    {
        ArgumentNullException.ThrowIfNull(wled);

        Speed = wled.Speed ?? 128;
        Intensity = wled.Intensity ?? 128;
        Custom1 = wled.Custom1 ?? 128;
        Custom2 = wled.Custom2 ?? 128;
        Custom3 = wled.Custom3 ?? 16;
        PaletteId = wled.Palette ?? 0;
        Palette = palette;
        Reverse = wled.Reverse ?? false;
        Option1 = wled.Option1 ?? false;
        Option2 = wled.Option2 ?? false;

        Colors =
        [
            wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White,
            wled.Colors is { Length: > 1 } ? wled.SecondaryColor : RgbColor.Black,
            wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]) : RgbColor.Black,
        ];
    }
}
