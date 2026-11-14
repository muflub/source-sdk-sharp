using System.Text;

namespace TF2.Descent.Rules.Items;

/// <summary>
/// The rules' one source of randomness: a SplitMix64 stream from a seed mixed with the
/// strings that say what is being rolled (FNV-1a over their UTF-8 bytes). The same seed and
/// parts give the same stream in any process, which is what lets the host replay a roll.
/// Never seeded from a clock, never shared: each roll makes its own.
/// </summary>
public sealed class SeededRandom
{
    private ulong _state;

    public SeededRandom(ulong seed) => _state = seed;

    /// <summary>A stream for <paramref name="seed"/> and the parts naming the roll.</summary>
    public static SeededRandom For(ulong seed, params string[] parts) => new(Mix(seed, parts));

    /// <summary>FNV-1a of each part's UTF-8 bytes, with a separator, folded into <paramref name="seed"/>.</summary>
    public static ulong Mix(ulong seed, params string[] parts)
    {
        const ulong prime = 1099511628211UL;
        ulong h = 14695981039346656037UL ^ seed;
        h *= prime;
        foreach (var part in parts)
        {
            foreach (var b in Encoding.UTF8.GetBytes(part))
            {
                h ^= b;
                h *= prime;
            }
            h ^= 0x1F;
            h *= prime;
        }
        return h;
    }

    /// <summary>The next 64 bits (SplitMix64).</summary>
    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1), 53 bits.</summary>
    public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform in [0, <paramref name="n"/>); 0 when n is 1 or less.</summary>
    public int Below(int n) => n <= 1 ? 0 : (int)(((Next() >> 32) * (ulong)n) >> 32);

    /// <summary>Uniform in [<paramref name="low"/>, <paramref name="high"/>], both ends included.</summary>
    public int Between(int low, int high) => high <= low ? low : low + Below(high - low + 1);

    /// <summary>One of <paramref name="items"/> with chance proportional to its weight; the first when every weight is 0.</summary>
    public T Weighted<T>(IReadOnlyList<T> items, Func<T, int> weight)
    {
        if (items.Count == 0) throw new ArgumentException("nothing to pick from", nameof(items));
        long total = 0;
        foreach (var i in items) total += Math.Max(0, weight(i));
        if (total <= 0) return items[0];
        long pick = (long)(Unit() * total);
        foreach (var i in items)
        {
            pick -= Math.Max(0, weight(i));
            if (pick < 0) return i;
        }
        return items[^1];
    }
}
