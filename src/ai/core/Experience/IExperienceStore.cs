namespace SourceSharp.Ai;

/// <summary>How many rows a store holds for one key, by table, and the rounds played on it.</summary>
public readonly record struct ExperienceCounts(long Rounds, long Cells, long Sights, long Looks, long Spots, long Links, long Outcomes, long RoundRows)
{
    public long Rows => Cells + Sights + Looks + Spots + Links + Outcomes + RoundRows;
}

/// <summary>
/// Where maps' experience is kept between rounds and between runs (G.3). A
/// store never throws into its caller: one that cannot read or write says why
/// in <see cref="Refusal"/>, answers the empty experience and merges nothing.
/// Its calls may block on a file, so they are made on a worker, never on the
/// game's thread.
/// </summary>
public interface IExperienceStore : IDisposable
{
    /// <summary>Why the store cannot be used, as one line; null while it can.</summary>
    string? Refusal { get; }

    /// <summary>
    /// Why the store is not attached yet, as one line, while it retries in the background (another process held its
    /// file locked past the busy timeout as it opened): until then it answers as an empty store and merges nothing,
    /// and a caller keeps what it would have written. Null once attached, for a store that never waited, and for a
    /// refused one.
    /// </summary>
    string? Waiting { get; }

    /// <summary>
    /// The snapshot of <paramref name="key"/>: every row scaled by a half per
    /// <paramref name="halfLife"/> rounds played on the key since the row was
    /// last written (<see cref="ExperienceDecay"/>). Empty for a key never
    /// written, and for a refused store.
    /// </summary>
    MapExperience Load(ExperienceKey key, double halfLife);

    /// <summary>
    /// Sums <paramref name="delta"/> into <paramref name="key"/>'s rows: the
    /// key's round count grows by the delta's rounds, and each row it touches
    /// is first faded to that count, then added to. False when refused (the
    /// caller keeps the delta).
    /// </summary>
    bool Merge(ExperienceKey key, ExperienceDelta delta, double halfLife);

    /// <summary>The rows held for <paramref name="key"/>; zeros for a refused store.</summary>
    ExperienceCounts Counts(ExperienceKey key);
}

/// <summary>Opens the store of a file: what the assembly that knows a database registers, so the server's binding names none.</summary>
public interface IExperienceStores
{
    /// <summary>The store at <paramref name="path"/>, created if absent. Never throws: a path that cannot be used answers a store with a <see cref="IExperienceStore.Refusal"/>.</summary>
    IExperienceStore Open(string path);
}

/// <summary>
/// An <see cref="IExperienceStore"/> in memory: what the facts that are not
/// about a database run on, and what a server keeps its experience in for the
/// life of the process when its file is refused.
/// </summary>
public sealed class MemoryExperienceStore : IExperienceStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ExperienceKey, Kept> _maps = new();

    public string? Refusal => null;

    public string? Waiting => null;

    public MapExperience Load(ExperienceKey key, double halfLife)
    {
        lock (_gate)
        {
            if (!_maps.TryGetValue(key, out Kept? kept))
                return new MapExperience(key, 0, new ExperienceDelta());
            var rows = new ExperienceDelta();
            long now = kept.Rounds;
            kept.Cells.Read(now, halfLife, rows.Add);
            kept.Sights.Read(now, halfLife, rows.Add);
            kept.Looks.Read(now, halfLife, rows.Add);
            kept.Spots.Read(now, halfLife, rows.Add);
            kept.Links.Read(now, halfLife, rows.Add);
            kept.Outcomes.Read(now, halfLife, rows.Add);
            foreach ((RoundRow row, long round) in kept.RoundRows)
            {
                if (!ExperienceDecay.Forgotten(now - round, halfLife))
                    rows.Add(row with { Weight = ExperienceDecay.Factor(now - round, halfLife) });
            }

            return new MapExperience(key, now, rows);
        }
    }

    public bool Merge(ExperienceKey key, ExperienceDelta delta, double halfLife)
    {
        ArgumentNullException.ThrowIfNull(delta);
        lock (_gate)
        {
            if (!_maps.TryGetValue(key, out Kept? kept))
                _maps[key] = kept = new Kept();
            foreach (RoundRow round in delta.Rounds)
                kept.RoundRows.Add((round with { Weight = 1d }, ++kept.Rounds));
            long now = kept.Rounds;
            kept.Cells.Merge(delta.Cells, now, halfLife);
            kept.Sights.Merge(delta.Sights, now, halfLife);
            kept.Looks.Merge(delta.Looks, now, halfLife);
            kept.Spots.Merge(delta.Spots, now, halfLife);
            kept.Links.Merge(delta.Links, now, halfLife);
            kept.Outcomes.Merge(delta.Outcomes, now, halfLife);
            kept.RoundRows.RemoveAll(r => ExperienceDecay.Forgotten(now - r.Round, halfLife));
            return true;
        }
    }

    public ExperienceCounts Counts(ExperienceKey key)
    {
        lock (_gate)
        {
            return _maps.TryGetValue(key, out Kept? k)
                ? new ExperienceCounts(k.Rounds, k.Cells.Count, k.Sights.Count, k.Looks.Count, k.Spots.Count, k.Links.Count, k.Outcomes.Count, k.RoundRows.Count)
                : default;
        }
    }

    public void Dispose()
    {
    }

    private sealed class Kept
    {
        public long Rounds;
        public readonly Stamped<CellKey, CellStat> Cells = new();
        public readonly Stamped<SightKey, Tally> Sights = new();
        public readonly Stamped<LookKey, Tally> Looks = new();
        public readonly Stamped<SpotKey, SpotStat> Spots = new();
        public readonly Stamped<LinkKey, LinkStat> Links = new();
        public readonly Stamped<OutcomeKey, OutcomeStat> Outcomes = new();
        public readonly List<(RoundRow Row, long Round)> RoundRows = new();
    }

    /// <summary>A table's rows, each with the round count it was last written at.</summary>
    private sealed class Stamped<TKey, TStat>
        where TKey : notnull
        where TStat : struct, IExperienceStat<TStat>
    {
        private readonly Dictionary<TKey, (TStat Stat, long Round)> _rows = new();

        public int Count => _rows.Count;

        public void Merge(IReadOnlyDictionary<TKey, TStat> delta, long now, double halfLife)
        {
            foreach ((TKey key, TStat add) in delta)
            {
                _rows[key] = _rows.TryGetValue(key, out (TStat Stat, long Round) old)
                    ? (old.Stat.Scaled(ExperienceDecay.Factor(now - old.Round, halfLife)).Plus(add), now)
                    : (add, now);
            }

            foreach (TKey gone in _rows.Where(r => ExperienceDecay.Forgotten(now - r.Value.Round, halfLife)).Select(r => r.Key).ToArray())
                _rows.Remove(gone);
        }

        public void Read(long now, double halfLife, Action<TKey, TStat> into)
        {
            foreach ((TKey key, (TStat stat, long round)) in _rows)
            {
                if (!ExperienceDecay.Forgotten(now - round, halfLife))
                    into(key, stat.Scaled(ExperienceDecay.Factor(now - round, halfLife)));
            }
        }
    }
}
