namespace SourceSharp.Ai;

/// <summary>One node visited in a decide: its id, what it answered, its value (a condition's 1 or 0, a utility, else NaN).</summary>
public readonly record struct BtTraceEntry(short NodeId, BtStatus Status, float Value);

/// <summary>
/// The behaviour-tree trace (plan_hl2mp_ai §7.1): a ring of the last
/// <see cref="Decides"/> decides, each the nodes it visited in order (up to
/// <see cref="EntriesPerDecide"/>) and the decision's reason. Fixed arrays,
/// written in place: a decide allocates nothing.
/// </summary>
public sealed class BtTrace
{
    public const int Decides = 64;

    public const int EntriesPerDecide = 64;

    private readonly BtTraceEntry[] _entries = new BtTraceEntry[Decides * EntriesPerDecide];
    private readonly int[] _counts = new int[Decides];
    private readonly Tick[] _ticks = new Tick[Decides];
    private readonly DecisionReason[] _reasons = new DecisionReason[Decides];
    private int _head = -1;

    /// <summary>Decides recorded, at most <see cref="Decides"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Entries a decide could not keep (past <see cref="EntriesPerDecide"/>).</summary>
    public long Overflowed { get; private set; }

    public void Begin(Tick tick)
    {
        _head = (_head + 1) % Decides;
        _counts[_head] = 0;
        _ticks[_head] = tick;
        _reasons[_head] = default;
        Count = Math.Min(Count + 1, Decides);
    }

    /// <summary>Records a node's visit; the answer is the entry's handle for <see cref="Set"/>, -1 when it was not kept.</summary>
    public int Add(short node, BtStatus status, float value)
    {
        if (_head < 0)
            return -1;
        int n = _counts[_head];
        if (n >= EntriesPerDecide)
        {
            Overflowed++;
            return -1;
        }

        _entries[_head * EntriesPerDecide + n] = new BtTraceEntry(node, status, value);
        _counts[_head] = n + 1;
        return _head * EntriesPerDecide + n;
    }

    public void Set(int entry, BtStatus status, float value)
    {
        if (entry >= 0)
            _entries[entry] = _entries[entry] with { Status = status, Value = value };
    }

    /// <summary>The reason the decide ended with.</summary>
    public void End(in DecisionReason reason)
    {
        if (_head >= 0)
            _reasons[_head] = reason;
    }

    private int Slot(int back)
    {
        if ((uint)back >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(back), back, $"the trace holds {Count} decides");
        return ((_head - back) % Decides + Decides) % Decides;
    }

    /// <summary>The tick of the decide <paramref name="back"/> before the latest (0 the latest).</summary>
    public Tick TickAt(int back) => _ticks[Slot(back)];

    public ReadOnlySpan<BtTraceEntry> EntriesAt(int back)
    {
        int s = Slot(back);
        return _entries.AsSpan(s * EntriesPerDecide, _counts[s]);
    }

    public DecisionReason ReasonAt(int back) => _reasons[Slot(back)];
}
