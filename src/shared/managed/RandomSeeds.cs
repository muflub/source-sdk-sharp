namespace SourceSharp.Abi;

/// <summary>
/// Where an entity's own random stream gets its seed: one source per side's
/// container, so no two entities built from it start their streams in the same
/// place and replay the same pattern.
/// </summary>
public interface IRandomSeeds
{
    /// <summary>The next seed. Every call answers a different draw.</summary>
    int Next();
}

/// <summary>
/// A seed source: a <see cref="Random"/> seeded ONCE, handing out one seed per
/// entity stream.
///
/// <para>
/// WHY A SOURCE AND NOT THE UNSEEDED CONSTRUCTOR. An entity that keeps its own
/// <see cref="Random"/> (logic_case, logic_timer, ambient_generic) is seeded
/// explicitly and differently per instance, so two cases built back to back can
/// never replay the same picks, and a test that builds its source from a fixed
/// seed gets the same seeds every run. The module seeds its source from the
/// engine's own uniform stream (<see cref="FromEngine"/>), which is the stream
/// stock draws these from and the one demo playback reproduces.
/// </para>
///
/// <para>
/// Game thread only, as every entity construction is.
/// </para>
/// </summary>
public sealed class RandomSeeds : IRandomSeeds
{
    private readonly Random _root;

    /// <summary>A source whose seeds are fixed by <paramref name="seed"/>.</summary>
    public RandomSeeds(int seed)
    {
        _root = new Random(seed);
    }

    /// <inheritdoc/>
    public int Next() => _root.Next();

    /// <summary>
    /// A source seeded once from the engine's uniform random stream, through
    /// the host area's <c>RandomFloat</c> (the <c>IUniformRandomStream</c> both
    /// sides bind). What each side's module registers.
    /// </summary>
    public static RandomSeeds FromEngine(IEngineHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new RandomSeeds((int)(host.RandomFloat(0f, 1f) * int.MaxValue));
    }
}
