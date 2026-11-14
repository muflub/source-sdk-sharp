using System.Runtime.InteropServices;

namespace SourceSharp.Ai;

/// <summary>
/// What a stretch of play added to a map's experience (G.3): additive counts
/// by row key, and the rounds that ended in it. The accumulator fills one and
/// hands it to a store, which sums it into what it holds; a snapshot's rows
/// have the same shape. It holds places, classes and counts and no player:
/// no name, id or slot can be put in one.
/// </summary>
public sealed class ExperienceDelta
{
    private readonly Dictionary<CellKey, CellStat> _cells = new();
    private readonly Dictionary<SightKey, Tally> _sights = new();
    private readonly Dictionary<LookKey, Tally> _looks = new();
    private readonly Dictionary<SpotKey, SpotStat> _spots = new();
    private readonly Dictionary<LinkKey, LinkStat> _links = new();
    private readonly Dictionary<OutcomeKey, OutcomeStat> _outcomes = new();
    private readonly List<RoundRow> _rounds = new();

    public IReadOnlyDictionary<CellKey, CellStat> Cells => _cells;

    public IReadOnlyDictionary<SightKey, Tally> Sights => _sights;

    public IReadOnlyDictionary<LookKey, Tally> Looks => _looks;

    public IReadOnlyDictionary<SpotKey, SpotStat> Spots => _spots;

    public IReadOnlyDictionary<LinkKey, LinkStat> Links => _links;

    public IReadOnlyDictionary<OutcomeKey, OutcomeStat> Outcomes => _outcomes;

    /// <summary>The rounds that ended, oldest first.</summary>
    public IReadOnlyList<RoundRow> Rounds => _rounds;

    /// <summary>How many rows it holds, the rounds among them.</summary>
    public int Rows => _cells.Count + _sights.Count + _looks.Count + _spots.Count + _links.Count + _outcomes.Count + _rounds.Count;

    public bool IsEmpty => Rows == 0;

    public void Add(CellKey key, CellStat stat) => Sum(_cells, key, stat);

    public void Add(SightKey key, Tally count) => Sum(_sights, key, count);

    public void Add(LookKey key, Tally seconds) => Sum(_looks, key, seconds);

    public void Add(SpotKey key, SpotStat stat) => Sum(_spots, key, stat);

    public void Add(LinkKey key, LinkStat stat)
    {
        ArgumentNullException.ThrowIfNull(key.Recipe, nameof(key));
        Sum(_links, key, stat);
    }

    public void Add(OutcomeKey key, OutcomeStat stat) => Sum(_outcomes, key, stat);

    public void Add(RoundRow round) => _rounds.Add(round);

    /// <summary>Adds every row of <paramref name="other"/>, its rounds after this one's.</summary>
    public void Add(ExperienceDelta other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach ((CellKey k, CellStat v) in other._cells)
            Sum(_cells, k, v);
        foreach ((SightKey k, Tally v) in other._sights)
            Sum(_sights, k, v);
        foreach ((LookKey k, Tally v) in other._looks)
            Sum(_looks, k, v);
        foreach ((SpotKey k, SpotStat v) in other._spots)
            Sum(_spots, k, v);
        foreach ((LinkKey k, LinkStat v) in other._links)
            Sum(_links, k, v);
        foreach ((OutcomeKey k, OutcomeStat v) in other._outcomes)
            Sum(_outcomes, k, v);
        _rounds.AddRange(other._rounds);
    }

    private static void Sum<TKey, TStat>(Dictionary<TKey, TStat> rows, TKey key, TStat stat)
        where TKey : notnull
        where TStat : struct, IExperienceStat<TStat>
    {
        ref TStat row = ref CollectionsMarshal.GetValueRefOrAddDefault(rows, key, out _);
        row = row.Plus(stat);
    }
}
