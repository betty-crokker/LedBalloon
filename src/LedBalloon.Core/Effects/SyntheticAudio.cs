namespace LedBalloon.Core.Effects;

/// <summary>
/// Music nobody is playing, so that a house with no microphone can still be shown what its
/// sound-reactive effects do.
/// </summary>
/// <remarks>
/// Two dozen of the effects a WLED controller offers draw from what it is hearing, and neither
/// controller here hears anything: both are configured for a microphone on the firmware's default
/// I2S pins and neither has one. So the effects are in the list, they are real, and the only honest
/// thing that could be said about them was that the run would stay dark.
/// <para>
/// This invents a signal instead. A kick on the beat, a bass line walking through four notes under
/// it, and a hat on the off-beat - enough structure that an effect reading the volume, an effect
/// reading the bass band and an effect reading the loudest frequency each have something different
/// to show.
/// </para>
/// <para>
/// A pure function of the time, which is the point. The same instant gives the same frame every
/// time it is asked, so the strip in the panel and the run on the house can be driven from one
/// signal and agree - the preview is not an impression of what the music might do, it is the same
/// music the controller is being sent.
/// </para>
/// </remarks>
public static class SyntheticAudio
{
    /// <summary>A tempo that reads as music rather than as a strobe or a pulse.</summary>
    public const double DefaultBeatsPerMinute = 120;

    /// <summary>
    /// The notes the bass line walks through, in hertz: A2, D3, E3, G3.
    /// </summary>
    /// <remarks>
    /// Four of them rather than one, because the effects that read the loudest frequency map it
    /// straight onto a position or a hue. A single note would hold those effects perfectly still
    /// and make them look broken in a different way.
    /// </remarks>
    private static readonly double[] BassLine = [110.0, 146.8, 164.8, 196.0];

    /// <summary>What the controller would be hearing <paramref name="at"/> seconds in.</summary>
    public static AudioFrame At(double at, double beatsPerMinute = DefaultBeatsPerMinute)
    {
        if (at < 0 || beatsPerMinute <= 0)
        {
            return AudioFrame.Silence;
        }

        double beat = 60.0 / beatsPerMinute;
        double into = at % beat;

        // The thump: loud on the beat and most of the way gone by the next one. Everything here
        // hangs off this one number, which is what makes the whole thing read as one piece of music
        // rather than as three unrelated signals that happen to share a tempo.
        double decay = Math.Exp(-6.0 * into / beat);

        var bins = new byte[AudioFrame.BinCount];
        bool hat = into > beat / 2 && into < (beat / 2) + 0.06;

        for (int i = 0; i < bins.Length; i++)
        {
            double low = Math.Exp(-0.6 * i) * 255.0 * decay;
            double mid = Math.Exp(-Math.Pow(i - 5, 2) / 4.0) * 160.0 * decay;
            double high = hat ? Math.Max(0, i - 9) * 28.0 : 0;

            bins[i] = (byte)Math.Clamp(low + mid + high, 0, 255);
        }

        int bar = (int)(at / beat) % BassLine.Length;

        return new AudioFrame(
            // Never all the way down between beats: a room with music in it has a floor, and an
            // effect that reads the volume should be alive rather than blinking.
            Math.Min(255.0, 40.0 + (190.0 * decay)),
            bins,

            // Bent upward on the attack, because a struck note is brightest above its fundamental
            // and the effects that read this are the ones that would otherwise never move.
            BassLine[bar] * (1 + (0.5 * decay)),
            (2000.0 * decay) + 200.0,
            into < 0.05);
    }

    /// <inheritdoc cref="At(double, double)"/>
    public static AudioFrame At(TimeSpan at, double beatsPerMinute = DefaultBeatsPerMinute) =>
        At(at.TotalSeconds, beatsPerMinute);
}
