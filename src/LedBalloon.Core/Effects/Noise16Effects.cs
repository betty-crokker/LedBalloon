using LedBalloon.Core.Models;

namespace LedBalloon.Core.Effects;

/// <summary>
/// The four Noise effects, which walk a sixteen-bit Perlin field and read the palette through a
/// sine.
/// <para>
/// All four take the top byte of the noise, pass it through <c>sin8(noise * 3)</c> and use that as
/// the palette index. Tripling before the sine is what turns a smooth field into banding: the sine
/// wraps three times over the noise's range, so neighboring noise values can land at opposite ends
/// of the palette and the run breaks into ribbons.
/// </para>
/// <para>
/// What separates them is the path through the field. One drifts along two axes at once with a third
/// moving quickly; two moves along one axis only; three holds still in space and moves through time
/// eight times faster; four is the only one that does not accumulate, so at speed zero it stands
/// perfectly still.
/// </para>
/// </summary>
internal static class Noise16
{
    /// <summary>Noise to palette index, which all four share.</summary>
    public static byte Index(ushort noise) => FastLed.Sin8((byte)((noise >> 8) * 3));
}

/// <summary>
/// A field drifting diagonally with time running through it quickly.
/// <para>
/// The x axis swings on a sine at eleven beats a minute while the y axis creeps, so the pattern never
/// quite repeats: two slow movements at different rates over a third that is fast.
/// </para>
/// </summary>
public sealed class Noise16_1Effect : IWledEffect
{
    public string Name => "Noise 1";

    private const uint Scale = 320;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        segment.Step += (uint)(1 + (segment.Speed / 16));

        uint shiftY = segment.Step / 42;

        for (int i = 0; i < segment.Length; i++)
        {
            // Inside the loop as the source has it, though it does not vary with the LED - the
            // effect is the same and the arithmetic is the same, so it is left where it was.
            byte shiftX = FastLed.BeatSin8(11, 0, 255, now);

            ushort noise = Perlin.Noise16(
                ((uint)i + shiftX) * Scale,
                ((uint)i + shiftY) * Scale,
                segment.Step);

            segment.Pixels[i] = segment.ColorFromPalette(
                Noise16.Index(noise), wrap: segment.SolidWrap);
        }
    }
}

/// <summary>
/// A field sliding along one axis, with the noise doubling as a brightness - so the ribbons fade in
/// and out rather than only changing color.
/// </summary>
public sealed class Noise16_2Effect : IWledEffect
{
    public string Name => "Noise 2";

    private const uint Scale = 1000;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        segment.Step += (uint)(1 + (segment.Speed >> 1));

        uint shiftX = segment.Step >> 6;

        for (int i = 0; i < segment.Length; i++)
        {
            ushort noise = Perlin.Noise16(((uint)i + shiftX) * Scale, 0, 4223);

            segment.Pixels[i] = segment.ColorFromPalette(
                Noise16.Index(noise), wrap: segment.SolidWrap, brightness: (byte)(noise >> 8));
        }
    }
}

/// <summary>
/// The field held still in space while time runs through it eight times faster than anything else
/// here - so the pattern boils in place rather than travelling.
/// </summary>
public sealed class Noise16_3Effect : IWledEffect
{
    public string Name => "Noise 3";

    private const uint Scale = 800;

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        segment.Step += (uint)(1 + segment.Speed);

        for (int i = 0; i < segment.Length; i++)
        {
            ushort noise = Perlin.Noise16(
                ((uint)i + 4223) * Scale,
                ((uint)i + 1234) * Scale,
                segment.Step * 8);

            segment.Pixels[i] = segment.ColorFromPalette(
                Noise16.Index(noise), wrap: segment.SolidWrap, brightness: (byte)(noise >> 8));
        }
    }
}

/// <summary>
/// The plainest of the four and the only one that is a function of the clock rather than of what it
/// did last - which makes it the one that can be checked exactly.
/// <para>
/// At speed zero it does not move at all, so a captured frame is nothing but
/// <c>inoise16(i &lt;&lt; 12, 0)</c> through the palette, with no accumulated state and no seed to
/// solve for. Either the sixteen-bit noise is right or it is not.
/// </para>
/// <para>
/// It also skips the sine the other three use, so the palette is walked smoothly rather than in
/// ribbons.
/// </para>
/// </summary>
public sealed class Noise16_4Effect : IWledEffect
{
    public string Name => "Noise 4";

    public void Render(EffectSegment segment, uint now)
    {
        ArgumentNullException.ThrowIfNull(segment);

        uint step = (now * segment.Speed) >> 7;

        for (int i = 0; i < segment.Length; i++)
        {
            ushort noise = Perlin.Noise16((uint)i << 12, step);

            segment.Pixels[i] = segment.ColorFromPalette(noise, wrap: segment.SolidWrap);
        }
    }
}
