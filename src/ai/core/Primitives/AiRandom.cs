namespace SourceSharp.Ai;

/// <summary>
/// A bot's own random stream: SplitMix64, so a seed means the same numbers on
/// every runtime (System.Random's algorithms are the runtime's to change) and
/// a replay draws exactly what the live run drew (plan_hl2mp_ai §2.6).
/// </summary>
public sealed class AiRandom(ulong seed)
{
    private ulong _state = seed;

    /// <summary>The stream's seed for <paramref name="slot"/> in a match: <c>hash(matchSeed, botSlot)</c> (§2.7).</summary>
    public static ulong SeedFor(ulong matchSeed, int slot) => Mix(matchSeed ^ Mix((ulong)(uint)slot + 0x9E3779B97F4A7C15UL));

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>A float in [0, 1).</summary>
    public float NextFloat() => (NextUInt64() >> 40) * (1f / (1 << 24));

    /// <summary>An int in [0, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)((NextUInt64() >> 33) % (ulong)maxExclusive);
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
