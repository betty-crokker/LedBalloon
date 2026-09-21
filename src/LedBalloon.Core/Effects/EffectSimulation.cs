namespace LedBalloon.Core.Effects;

/// <summary>One of WLED's effects, drawing one frame onto a run.</summary>
public interface IWledEffect
{
    /// <summary>The name the controller lists it under, which is how it is matched.</summary>
    string Name { get; }

    /// <summary>
    /// Draws one frame.
    /// </summary>
    /// <param name="segment">The run, carrying both its settings and the frame it drew last.</param>
    /// <param name="now">
    /// Milliseconds since the controller started, wrapping like the 32-bit counter it stands in for.
    /// Most effects are a function of this and nothing else.
    /// </param>
    void Render(EffectSegment segment, uint now);
}

/// <summary>
/// Runs an effect forward in the same fixed steps the controller would, so a run on the photo
/// arrives at the same picture the run on the house is showing.
/// <para>
/// Fixed steps, not "once per repaint". Anything that fades reaches a given darkness after a
/// number of frames rather than after an amount of time, so a preview stepped at the screen's
/// refresh rate would trail visibly differently on a fast monitor than on a slow one - and
/// differently again from the wall. Stepping at the controller's own frame time makes the trail
/// right and makes it reproducible.
/// </para>
/// </summary>
public sealed class EffectSimulation
{
    /// <summary>
    /// WLED's default frame time: 42 frames a second, rounded down by integer division exactly as
    /// the firmware rounds it. Real controllers report their own, which may be lower.
    /// </summary>
    public const int DefaultFrameMilliseconds = 1000 / 42;

    /// <summary>
    /// How many frames to run in one go before giving up and skipping ahead.
    /// <para>
    /// A preview that has been off screen for a minute must not try to draw a minute of frames to
    /// catch up. Trails are short, so the picture a few frames from now is indistinguishable from
    /// the one two thousand frames from now; arriving there slowly is the only difference.
    /// </para>
    /// </summary>
    private const int MaxFramesPerAdvance = 8;

    private readonly IWledEffect _effect;
    private readonly int _frameMilliseconds;

    private double _owed;
    private uint _now;

    public EffectSimulation(
        IWledEffect effect,
        EffectSegment segment,
        int frameMilliseconds = DefaultFrameMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameMilliseconds, 1);

        _effect = effect;
        _frameMilliseconds = frameMilliseconds;
        Segment = segment;
    }

    /// <summary>The run being drawn, whose pixels are the output.</summary>
    public EffectSegment Segment { get; }

    /// <summary>How far the simulation has run, in the controller's milliseconds.</summary>
    public uint ElapsedMilliseconds => _now;

    /// <summary>Frames drawn so far. Mostly useful for showing that it is running at all.</summary>
    public long Frames { get; private set; }

    /// <summary>
    /// Runs the effect forward by <paramref name="elapsedMilliseconds"/> of wall-clock time, in
    /// whole frames.
    /// </summary>
    public void Advance(double elapsedMilliseconds)
    {
        if (elapsedMilliseconds <= 0)
        {
            return;
        }

        _owed += elapsedMilliseconds;

        int drawn = 0;

        while (_owed >= _frameMilliseconds && drawn < MaxFramesPerAdvance)
        {
            _owed -= _frameMilliseconds;
            drawn++;

            // Wraps at 2^32 ms, about 49 days, exactly where the controller's own clock wraps.
            // Effects are written to survive that, so the simulation has to reach it the same way.
            unchecked
            {
                _now += (uint)_frameMilliseconds;
            }

            _effect.Render(Segment, _now);
            Frames++;
        }

        if (drawn == MaxFramesPerAdvance)
        {
            _owed = 0;
        }
    }

    /// <summary>
    /// Draws enough frames for the picture to settle, so a preview opens on what the run looks
    /// like rather than on one frame's worth of dots over a black strip.
    /// </summary>
    public void Prime(int frames = 24)
    {
        for (int i = 0; i < frames; i++)
        {
            Advance(_frameMilliseconds);
        }
    }
}
