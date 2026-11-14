using System.Text;

namespace TF2.Descent.Rules.Furniture;

/// <summary>
/// A deterministic stream of rolls from a seed and the names of what is being rolled
/// (SplitMix64 over an FNV-1a hash of the parts). The same parts give the same stream on
/// every machine and every run, because nothing in it reads a clock or the runtime's
/// randomised string hash, so the host replays a roll and a failing fact reproduces.
/// A value type the caller owns: no state outside it.
/// </summary>
public struct Seeded
{
    private ulong _state;

    public Seeded(ulong seed) => _state = seed;

    /// <summary>A stream keyed by a seed and any number of names or numbers ("level", 7, "room_a").</summary>
    public static Seeded From(ulong seed, params object[] parts)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in BitConverter.GetBytes(seed)) h = (h ^ b) * 1099511628211UL;
        foreach (var part in parts)
        {
            foreach (var b in Encoding.UTF8.GetBytes(part switch
                     {
                         string s => s,
                         IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                         _ => part.ToString() ?? "",
                     }))
                h = (h ^ b) * 1099511628211UL;
            h = (h ^ 0x1F) * 1099511628211UL;   // a separator: ("ab", "c") is not ("a", "bc")
        }
        return new Seeded(h);
    }

    public ulong NextULong()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>A whole number in [0, <paramref name="n"/>).</summary>
    public int Next(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        return (int)(NextULong() % (ulong)n);
    }

    /// <summary>A double in [<paramref name="low"/>, <paramref name="high"/>].</summary>
    public double Range(double low, double high) => low + (high - low) * NextDouble();

    /// <summary>True with probability <paramref name="pct"/> percent.</summary>
    public bool Chance(double pct) => NextDouble() * 100 < pct;

    /// <summary>An index chosen in proportion to <paramref name="weights"/> (all ≥ 0, at least one &gt; 0).</summary>
    public int Pick(IReadOnlyList<double> weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;
        if (!(total > 0)) throw new ArgumentException("no weight to pick from", nameof(weights));
        double at = NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            if (at < weights[i]) return i;
            at -= weights[i];
        }
        for (int i = weights.Count - 1; i >= 0; i--)   // rounding left a crumb past the last weight
            if (weights[i] > 0) return i;
        return 0;
    }

    /// <summary>The list in a seeded order (Fisher-Yates).</summary>
    public List<T> Shuffle<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
