namespace FlowTerminal.MarketData.Synthetic;

/// <summary>
/// A tiny, fully deterministic xorshift64* PRNG. We deliberately do not use
/// <see cref="System.Random"/> because its internal algorithm is not guaranteed
/// stable across runtimes; reproducible synthetic data and replay hashing require
/// a fixed, self-contained algorithm.
/// </summary>
public struct DeterministicRng
{
    private ulong _state;

    public DeterministicRng(ulong seed)
    {
        // Avoid the zero fixed-point; splitmix the seed for a good starting state.
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public ulong NextUInt64()
    {
        ulong x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform integer in [0, exclusiveMax).</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        return (int)(NextUInt64() % (ulong)exclusiveMax);
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
}
