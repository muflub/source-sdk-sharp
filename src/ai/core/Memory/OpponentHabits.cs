namespace SourceSharp.Ai;

/// <summary>What an opponent did while reloading, as the bot saw it (§5.1, the reload style).</summary>
public enum ReloadBehaviour : byte
{
    /// <summary>Went out of sight.</summary>
    Retreated = 0,

    /// <summary>Stayed where it was.</summary>
    Held,

    /// <summary>Kept closing in.</summary>
    Closed,
}

/// <summary>
/// One opponent's habits as one bot has learned them (plan_hl2mp_ai §5.1):
/// where it goes on this map (the route prior, a saturating count per nav
/// cell), where it camps, which pickups it goes for, at what range it
/// fights, how often it retreats when hurt, how often it closes in, and how
/// it reloads. Counters halve every habit half-life. Kept for the server
/// process only (§5.2, ruling 2026-11-01): nothing here is ever written to
/// disk.
/// </summary>
public sealed class OpponentHabits
{
    public const int CampSpots = 8, Pickups = 8, RangeBins = 4;

    /// <summary>The engagement-range bins' upper edges: close, mid, far, beyond.</summary>
    public static ReadOnlySpan<float> RangeEdges => [256f, 768f, 1536f, float.PositiveInfinity];

    private readonly byte[] _route;
    private readonly (CellId Cell, float Count)[] _camps = new (CellId, float)[CampSpots];
    private readonly (int Item, float Count)[] _pickups = new (int, float)[Pickups];
    private readonly float[] _range = new float[RangeBins];
    private readonly float[] _reloadBehaviour = new float[3];

    public OpponentHabits(string opponent, int cells)
    {
        Opponent = opponent;
        _route = new byte[cells];
        for (int i = 0; i < CampSpots; i++)
            _camps[i] = (CellId.None, 0f);
        for (int i = 0; i < Pickups; i++)
            _pickups[i] = (-1, 0f);
    }

    public string Opponent { get; }

    public ReadOnlySpan<byte> Route => _route;

    public ReadOnlySpan<(CellId Cell, float Count)> Camps => _camps;

    public ReadOnlySpan<(int Item, float Count)> FavouritePickups => _pickups;

    public ReadOnlySpan<float> EngagementRange => _range;

    public ReadOnlySpan<float> ReloadBehaviours => _reloadBehaviour;

    public float RetreatsWhenHurt { get; private set; }

    public float SightLosses { get; private set; }

    public float Approaches { get; private set; }

    public float Withdrawals { get; private set; }

    public float ReloadSpentSum { get; private set; }

    public float Reloads { get; private set; }

    /// <summary>The share of losses of sight that followed the opponent being hurt (§5.1).</summary>
    public float RetreatTendency => SightLosses <= 0f ? 0.5f : RetreatsWhenHurt / SightLosses;

    /// <summary>The share of contacts it closed in on.</summary>
    public float Aggression => Approaches + Withdrawals <= 0f ? 0.5f : Approaches / (Approaches + Withdrawals);

    /// <summary>The mean share of its clip spent at a reload: near 1, it runs dry; low, it reloads early.</summary>
    public float ReloadStyle => Reloads <= 0f ? float.NaN : ReloadSpentSum / Reloads;

    /// <summary>The opponent was seen in <paramref name="cell"/>: one more visit there (saturating).</summary>
    public void Visit(CellId cell)
    {
        if (cell.IsValid && cell.Index < _route.Length && _route[cell.Index] < byte.MaxValue)
            _route[cell.Index]++;
    }

    public void Camped(CellId cell) => Bump(_camps, cell);

    public void PickedUp(int item)
    {
        for (int i = 0; i < _pickups.Length; i++)
        {
            if (_pickups[i].Item == item)
            {
                _pickups[i].Count++;
                return;
            }
        }

        int least = 0;
        for (int i = 1; i < _pickups.Length; i++)
        {
            if (_pickups[i].Count < _pickups[least].Count)
                least = i;
        }

        _pickups[least] = (item, 1f);
    }

    public void Engaged(float distance)
    {
        for (int b = 0; b < RangeBins; b++)
        {
            if (distance <= RangeEdges[b])
            {
                _range[b]++;
                return;
            }
        }
    }

    /// <summary>The bot lost sight of it; <paramref name="afterHurt"/> when the bot had just hurt it.</summary>
    public void LostSight(bool afterHurt)
    {
        SightLosses++;
        if (afterHurt)
            RetreatsWhenHurt++;
    }

    public void Contact(bool closed)
    {
        if (closed)
            Approaches++;
        else
            Withdrawals++;
    }

    /// <summary>A perceived reload: the share of the clip spent before it, and (once known) what it did meanwhile.</summary>
    public void Reloaded(float spent)
    {
        if (float.IsNaN(spent))
            return;
        ReloadSpentSum += spent;
        Reloads++;
    }

    public void WhileReloading(ReloadBehaviour b) => _reloadBehaviour[(int)b]++;

    private double _decaySeconds;

    /// <summary>Time passes: every counter halves each <paramref name="halfLife"/> seconds (§5.1).</summary>
    public void Decay(float seconds, float halfLife)
    {
        if (halfLife <= 0f || seconds <= 0f)
            return;
        float k = MathF.Pow(2f, -seconds / halfLife);
        for (int i = 0; i < _camps.Length; i++)
            _camps[i].Count *= k;
        for (int i = 0; i < _pickups.Length; i++)
            _pickups[i].Count *= k;
        for (int i = 0; i < _range.Length; i++)
            _range[i] *= k;
        for (int i = 0; i < _reloadBehaviour.Length; i++)
            _reloadBehaviour[i] *= k;
        RetreatsWhenHurt *= k;
        SightLosses *= k;
        Approaches *= k;
        Withdrawals *= k;
        ReloadSpentSum *= k;
        Reloads *= k;

        // The route prior's bytes halve whole: once per half-life passed.
        _decaySeconds += seconds;
        while (_decaySeconds >= halfLife)
        {
            _decaySeconds -= halfLife;
            for (int i = 0; i < _route.Length; i++)
                _route[i] >>= 1;
        }
    }

    private static void Bump((CellId Cell, float Count)[] spots, CellId cell)
    {
        if (!cell.IsValid)
            return;
        int least = 0;
        for (int i = 0; i < spots.Length; i++)
        {
            if (spots[i].Cell == cell)
            {
                spots[i].Count++;
                return;
            }

            if (spots[i].Count < spots[least].Count)
                least = i;
        }

        spots[least] = (cell, 1f);
    }
}

/// <summary>
/// Every opponent's habits as one bot learned them, across its lives and the
/// server's maps (§5.2): keyed by the opponent's identity and the map, the
/// least recently used map dropped past <see cref="MaxMaps"/>. In memory
/// only, for the server process (ruling 2026-11-01).
/// </summary>
public sealed class HabitStore
{
    public const int MaxMaps = 64;

    private readonly Dictionary<(string Map, string Opponent), OpponentHabits> _habits = new();
    private readonly LinkedList<string> _maps = new();

    public int Count => _habits.Count;

    public IEnumerable<string> Maps => _maps;

    /// <summary>The habits of <paramref name="opponent"/> on <paramref name="map"/> (a map's graph of <paramref name="cells"/> cells), made on first ask.</summary>
    public OpponentHabits For(string map, string opponent, int cells)
    {
        Touch(map);
        if (!_habits.TryGetValue((map, opponent), out OpponentHabits? h))
            _habits[(map, opponent)] = h = new OpponentHabits(opponent, cells);
        return h;
    }

    public bool TryGet(string map, string opponent, out OpponentHabits? habits) => _habits.TryGetValue((map, opponent), out habits);

    private void Touch(string map)
    {
        _maps.Remove(map);
        _maps.AddFirst(map);
        while (_maps.Count > MaxMaps)
        {
            string old = _maps.Last!.Value;
            _maps.RemoveLast();
            foreach ((string, string) k in _habits.Keys.Where(k => k.Map == old).ToArray())
                _habits.Remove(k);
        }
    }
}
