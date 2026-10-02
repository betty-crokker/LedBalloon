namespace LedBalloon.Core.Effects;

/// <summary>
/// What a sound-reactive effect gets to see: one instant of what the controller is hearing.
/// </summary>
/// <remarks>
/// WLED's sound-reactive effects read none of this directly. They read a block the AudioReactive
/// usermod fills in, and this is that block's useful half - the fields the 1D effects actually
/// touch. Everything else in it is either for the 2D effects, which are out of scope, or is a raw
/// form of one of these.
/// </remarks>
/// <param name="Volume">
/// The smoothed volume, 0 to 255. What the usermod calls <c>volumeSmth</c>, and what most of the
/// sound-reactive effects are built on: a meter's height, a splash's width, how bright a pixel is.
/// </param>
/// <param name="Bins">
/// Sixteen frequency bands, 0 to 255, bass first. <c>fftResult</c>. GEQ draws one bar per band and
/// the others pick two or three bands out of it.
/// </param>
/// <param name="MajorPeakHz">
/// The loudest frequency, in hertz. Freqmap turns it into a position along the run and Freqwave
/// turns it into a hue, which is why a sound effect can look like it is ignoring the palette.
/// </param>
/// <param name="Magnitude">How strong that peak is, which several effects use for brightness.</param>
/// <param name="Beat">
/// True on the frame a beat is detected. <c>samplePeak</c>, which Ripple Peak and Puddlepeak fire
/// on - it is a flag rather than a level, so an effect reading it does nothing at all between beats.
/// </param>
public readonly record struct AudioFrame(
    double Volume,
    IReadOnlyList<byte> Bins,
    double MajorPeakHz,
    double Magnitude,
    bool Beat)
{
    /// <summary>How many bands the usermod reports, which is fixed in the firmware.</summary>
    public const int BinCount = 16;

    private static readonly byte[] Nothing = new byte[BinCount];

    /// <summary>
    /// A room with nothing in it, which is what both controllers here hear all the time.
    /// </summary>
    /// <remarks>
    /// Worth having as a value rather than as an absence. A sound effect fed silence is not an
    /// effect that fails to draw - it is an effect drawing the picture silence makes, which for most
    /// of them is a dark run and for a few is a dim resting state. That is a real answer to "what
    /// would this do on my house", and it is the answer the preview gives when nobody has asked for
    /// music.
    /// </remarks>
    public static AudioFrame Silence { get; } = new(0, Nothing, 0, 0, false);

    /// <summary>One band, or zero for an index outside the sixteen.</summary>
    public byte Bin(int index) =>
        index >= 0 && Bins is { } bins && index < bins.Count ? bins[index] : (byte)0;
}
