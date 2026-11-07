namespace CorsixNet.Sim;

/// <summary>
/// Deterministic pseudo-random number generator for the simulation (PCG32, XSH-RR variant).
/// </summary>
/// <remarks>
/// The algorithm is fixed by this implementation, so every machine in a lockstep
/// game produces the same sequence from the same state. The full state is exposed
/// through <see cref="State"/> and <see cref="Increment"/> so it can be saved and
/// included in desync checks.
/// </remarks>
public sealed class SimRandom
{
    private const ulong Multiplier = 6364136223846793005UL;

    /// <summary>Creates a generator positioned at the start of stream <paramref name="sequence"/>.</summary>
    public SimRandom(ulong seed, ulong sequence = 0)
    {
        Increment = (sequence << 1) | 1UL;
        State = 0;
        Step();
        State += seed;
        Step();
    }

    private SimRandom(ulong state, ulong increment, bool _)
    {
        State = state;
        Increment = increment;
    }

    /// <summary>Current internal state.</summary>
    public ulong State { get; private set; }

    /// <summary>Stream selector; always odd.</summary>
    public ulong Increment { get; }

    /// <summary>Restores a generator from values previously read from <see cref="State"/> and <see cref="Increment"/>.</summary>
    public static SimRandom FromState(ulong state, ulong increment)
    {
        if ((increment & 1UL) == 0)
        {
            throw new ArgumentException("The increment of a PCG32 generator must be odd.", nameof(increment));
        }

        return new SimRandom(state, increment, false);
    }

    /// <summary>Returns the next 32 random bits.</summary>
    public uint NextUInt32()
    {
        var old = State;
        Step();
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Returns a uniformly distributed integer in [0, <paramref name="maxExclusive"/>).</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)NextBounded((uint)maxExclusive);
    }

    /// <summary>Returns a uniformly distributed integer in [<paramref name="minInclusive"/>, <paramref name="maxInclusive"/>].</summary>
    public int NextInclusive(int minInclusive, int maxInclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minInclusive, maxInclusive);
        var range = (uint)((long)maxInclusive - minInclusive + 1);
        // A range of 2^32 wraps to zero: every 32-bit value is acceptable.
        var offset = range == 0 ? NextUInt32() : NextBounded(range);
        return (int)(minInclusive + (long)offset);
    }

    /// <summary>Returns true with probability <paramref name="numerator"/>/<paramref name="denominator"/>.</summary>
    public bool Chance(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        return Next(denominator) < numerator;
    }

    /// <summary>Unbiased bounded generation (Lemire's multiply-and-reject method).</summary>
    private uint NextBounded(uint bound)
    {
        var product = (ulong)NextUInt32() * bound;
        var low = (uint)product;
        if (low < bound)
        {
            var threshold = (0u - bound) % bound;
            while (low < threshold)
            {
                product = (ulong)NextUInt32() * bound;
                low = (uint)product;
            }
        }

        return (uint)(product >> 32);
    }

    private void Step()
    {
        State = unchecked((State * Multiplier) + Increment);
    }
}
