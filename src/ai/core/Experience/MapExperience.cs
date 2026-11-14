namespace SourceSharp.Ai;

/// <summary>
/// What players did on one map in one mode, as a decider reads it (G.3): an
/// immutable snapshot of the store's rows, each already faded to the round
/// count it was read at. It holds counts by place, nothing that says where
/// anyone is now. A decider asks through <see cref="For"/>, which sums the
/// rows of the stage, role and class it names. <see cref="Empty"/> stands in
/// wherever there is no store, so nothing that holds one has a null branch.
/// </summary>
public sealed class MapExperience
{
    private readonly Dictionary<CellKey, CellStat> _cells;
    private readonly Dictionary<SightKey, Tally> _sights;
    private readonly Dictionary<LookKey, Tally> _looks;
    private readonly Dictionary<SpotKey, SpotStat> _spots;
    private readonly Dictionary<LinkKey, LinkStat> _links;
    private readonly Dictionary<OutcomeKey, OutcomeStat> _outcomes;
    private readonly RoundRow[] _rounds;

    /// <summary>
    /// The snapshot of <paramref name="rows"/> for <paramref name="key"/> at
    /// <paramref name="rounds"/> rounds played; the rows are copied, so the
    /// caller's may change after.
    /// </summary>
    public MapExperience(ExperienceKey key, long rounds, ExperienceDelta rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Key = key;
        Rounds = rounds;
        _cells = new(rows.Cells);
        _sights = new(rows.Sights);
        _looks = new(rows.Looks);
        _spots = new(rows.Spots);
        _links = new(rows.Links);
        _outcomes = new(rows.Outcomes);
        _rounds = [.. rows.Rounds];
        ContentHash = Hash();
    }

    /// <summary>The experience of no map, with no rows: what a world holds when the store is off, refused, or not yet read.</summary>
    public static MapExperience Empty => new(ExperienceKey.None, 0, new ExperienceDelta());

    public ExperienceKey Key { get; }

    /// <summary>The rounds played on the key when this was read: what its rows are faded to.</summary>
    public long Rounds { get; }

    public IReadOnlyDictionary<CellKey, CellStat> Cells => _cells;

    public IReadOnlyDictionary<SightKey, Tally> Sights => _sights;

    public IReadOnlyDictionary<LookKey, Tally> Looks => _looks;

    public IReadOnlyDictionary<SpotKey, SpotStat> Spots => _spots;

    public IReadOnlyDictionary<LinkKey, LinkStat> Links => _links;

    public IReadOnlyDictionary<OutcomeKey, OutcomeStat> Outcomes => _outcomes;

    /// <summary>The rounds remembered, oldest first, each with what is left of it.</summary>
    public IReadOnlyList<RoundRow> RoundRows => _rounds;

    public int Rows => _cells.Count + _sights.Count + _looks.Count + _spots.Count + _links.Count + _outcomes.Count + _rounds.Length;

    public bool IsEmpty => Rows == 0;

    /// <summary>
    /// A hash of the key, the round count and every row, the same for the same
    /// content whatever order the rows were added in and in whatever process:
    /// what a recording names its snapshot by, so a replay can refuse another.
    /// </summary>
    public ulong ContentHash { get; }

    /// <summary>The rows <paramref name="context"/> takes, summed by place: what a decider of that stage, role and class reads.</summary>
    public ExperienceView For(ExperienceContext context) => new(this, context);

    private ulong Hash()
    {
        var all = new ExperienceHash(0x4D61_7045UL);
        all.Add(Key.Map);
        all.Add(Key.Checksum);
        all.Add(Key.Mode);
        all.Add(Rounds);

        ulong cells = 0, sights = 0, looks = 0, spots = 0, links = 0, outcomes = 0;
        foreach ((CellKey k, CellStat v) in _cells)
        {
            var h = Start(1, k.Context);
            h.Add(k.Bin.Packed);
            h.Add((long)k.Source);
            h.Add(v.Seconds);
            h.Add(v.Deaths);
            h.Add(v.Kills);
            h.Add(v.DamageTaken);
            h.Add(v.DamageDealt);
            cells += h.Value;
        }

        foreach ((SightKey k, Tally v) in _sights)
        {
            var h = Start(2, k.Context);
            h.Add(k.Victim.Packed);
            h.Add(k.Killer.Packed);
            h.Add(k.WeaponClass);
            h.Add(v.Count);
            sights += h.Value;
        }

        foreach ((LookKey k, Tally v) in _looks)
        {
            var h = Start(3, k.Context);
            h.Add(k.Bin.Packed);
            h.Add(k.Yaw);
            h.Add(k.Pitch);
            h.Add(v.Count);
            looks += h.Value;
        }

        foreach ((SpotKey k, SpotStat v) in _spots)
        {
            var h = Start(4, k.Context);
            h.Add(k.Kind);
            h.Add(k.Bin.Packed);
            h.Add(k.Yaw);
            h.Add(v.Uses);
            h.Add(v.SecondsHeld);
            h.Add(v.Kills);
            h.Add(v.Deaths);
            spots += h.Value;
        }

        foreach ((LinkKey k, LinkStat v) in _links)
        {
            var h = new ExperienceHash(5);
            h.Add(k.Class);
            h.Add(k.From.Packed);
            h.Add(k.To.Packed);
            h.Add(k.Kind);
            h.Add((long)k.Source);
            h.Add(k.Recipe.Bytes.AsSpan());
            h.Add(v.Tries);
            h.Add(v.Successes);
            h.Add(v.Seconds);
            h.Add(v.HealthCost);
            links += h.Value;
        }

        foreach ((OutcomeKey k, OutcomeStat v) in _outcomes)
        {
            var h = Start(6, k.Context);
            h.Add(k.Kind);
            h.Add(k.Choice);
            h.Add(v.Tries);
            h.Add(v.Score);
            outcomes += h.Value;
        }

        all.Add((long)cells);
        all.Add((long)sights);
        all.Add((long)looks);
        all.Add((long)spots);
        all.Add((long)links);
        all.Add((long)outcomes);
        foreach (RoundRow r in _rounds)
        {
            all.Add(r.Seconds);
            all.Add(r.WinningRole);
            all.Add(r.Humans);
            all.Add(r.Bots);
            all.Add(r.Composition);
            all.Add(r.Weight);
        }

        return all.Value;
    }

    private static ExperienceHash Start(ulong table, ExperienceContext context)
    {
        var h = new ExperienceHash(table);
        h.Add(context.Stage);
        h.Add(context.Role);
        h.Add(context.Class);
        return h;
    }
}

/// <summary>
/// A 64-bit hash that is the same in every process (a string's own hash is
/// not): each value is mixed in with splitmix64's finaliser, in order.
/// </summary>
internal struct ExperienceHash(ulong seed)
{
    private ulong _state = seed;

    public readonly ulong Value => Mix(_state);

    public void Add(long value) => _state = Mix(_state ^ (ulong)value);

    /// <summary>A count by its bits, with -0 as 0.</summary>
    public void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value == 0d ? 0d : value));

    public void Add(string text)
    {
        Add(text.Length);
        foreach (char c in text)
            Add(c);
    }

    public void Add(ReadOnlySpan<byte> bytes)
    {
        Add(bytes.Length);
        foreach (byte b in bytes)
            Add(b);
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E37_79B9_7F4A_7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>
/// Where a world's <see cref="MapExperience"/> is held: empty until a worker
/// has read the store, and replaced only at a round's start, so every decide
/// of a round reads one snapshot. Never null.
/// </summary>
public sealed class ExperienceSlot
{
    private volatile MapExperience _current = MapExperience.Empty;

    public MapExperience Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
