namespace LedBalloon.Core.Effects;

/// <summary>
/// FastLED's own pseudo-random generator, ported exactly, with its seed exposed.
/// <para>
/// A second generator alongside <see cref="EffectSegment.Random8()"/>, and the two are not
/// interchangeable. That one exists to make a preview repeatable and is deliberately not the
/// controller's: an effect that sprinkles pixels about can only be checked in aggregate anyway, so
/// what matters is that the same scene drawn twice looks the same.
/// </para>
/// <para>
/// This one exists for the handful of effects that use randomness as a <em>pattern</em> rather than
/// as noise. They set the seed to a fixed number, walk the sequence once per LED, and so give every
/// LED the same offset it had last frame without storing anything - Twinkleup resets it to 535 every
/// frame for exactly that reason. For those the sequence is the effect, so it has to be the same
/// sequence the firmware walks, down to the low bits.
/// </para>
/// <para>
/// Sixteen bits of state, multiply by 2053, add 13849. The byte draw returns the sum of the two
/// halves of that state rather than either half, which mixes it enough that consecutive draws are
/// not obviously related.
/// </para>
/// </summary>
public sealed class SeededSequence
{
    /// <summary>FastLED's own starting value, for a generator nobody has seeded.</summary>
    public const ushort DefaultSeed = 1337;

    /// <summary>
    /// The state, readable and writable - which is the whole point of this class existing. WLED's
    /// <c>random16_get_seed</c> and <c>random16_set_seed</c>.
    /// </summary>
    public ushort Seed { get; set; } = DefaultSeed;

    /// <summary>The next sixteen-bit draw, which is the state itself once advanced.</summary>
    public ushort Word()
    {
        Advance();
        return Seed;
    }

    /// <summary>A sixteen-bit draw below <paramref name="limit"/>.</summary>
    public ushort Word(int limit) => (ushort)((uint)limit * Word() >> 16);

    /// <summary>
    /// The next byte: the two halves of the state added together, which is not the same as either
    /// half and is why consecutive bytes do not march.
    /// </summary>
    public byte Byte()
    {
        Advance();
        return (byte)((byte)(Seed & 0xFF) + (byte)(Seed >> 8));
    }

    /// <summary>A byte below <paramref name="limit"/>, scaled rather than taken modulo.</summary>
    public byte Byte(int limit) => (byte)(Byte() * limit >> 8);

    private void Advance()
    {
        unchecked
        {
            Seed = (ushort)((Seed * 2053) + 13849);
        }
    }
}
