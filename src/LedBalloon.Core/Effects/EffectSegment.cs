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
    /// <param name="wrap">
    /// False - the usual - stops the lookup short of the palette's end, so the last color does not
    /// blend back round into the first.
    /// </param>
    public RgbColor ColorFromPalette(int index, bool wrap = false)
    {
        if (PaletteId == 0 || Palette is null)
        {
            return Colors[0];
        }

        byte at = wrap ? (byte)(index & 0xFF) : FastLed.Scale8((byte)(index & 0xFF), 240);

        return Palette.ColorAt(at / 255d, Colors[0], Colors[1], Colors[2]);
    }

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

        Colors =
        [
            wled.Colors is { Length: > 0 } ? wled.PrimaryColor : RgbColor.White,
            wled.Colors is { Length: > 1 } ? wled.SecondaryColor : RgbColor.Black,
            wled.Colors is { Length: > 2 } ? RgbColor.FromWledArray(wled.Colors[2]) : RgbColor.Black,
        ];
    }
}
