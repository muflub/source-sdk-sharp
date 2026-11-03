namespace SourceSharp.Ai;

/// <summary>
/// Every bot's <see cref="PerceivedView"/> at one tick (plan_hl2mp_ai §2.2),
/// indexed by the bot's slot. Built on the main thread, published into the
/// <see cref="SnapshotRing"/>, then only read.
/// </summary>
public sealed class AiSnapshot
{
    private readonly PerceivedView[] _views;
    private readonly bool[] _present;

    public AiSnapshot(int slots, int sightedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots);
        _views = new PerceivedView[slots];
        _present = new bool[slots];
        for (int i = 0; i < slots; i++)
            _views[i] = new PerceivedView(sightedCapacity);
    }

    public Tick Tick { get; private set; }

    /// <summary>Whether the writer has finished this snapshot.</summary>
    public bool Published { get; internal set; }

    /// <summary>How many slots a snapshot holds (the highest bot slot is one less).</summary>
    public int Slots => _views.Length;

    /// <summary>Clears every view and starts <paramref name="tick"/>.</summary>
    internal void Begin(Tick tick)
    {
        Tick = tick;
        Published = false;
        Array.Clear(_present);
    }

    /// <summary>The view to fill for <paramref name="bot"/>, reset to <paramref name="self"/>.</summary>
    public PerceivedView Add(ActorId bot, in SelfState self)
    {
        if (Published)
            throw new InvalidOperationException($"the snapshot for {Tick} is published and read-only");
        if ((uint)bot.Slot >= (uint)_views.Length)
            throw new ArgumentOutOfRangeException(nameof(bot), bot.Slot, $"slots run 0..{_views.Length - 1}");

        _present[bot.Slot] = true;
        PerceivedView view = _views[bot.Slot];
        view.Reset(bot, Tick, self);
        return view;
    }

    /// <summary>The view for the bot in <paramref name="slot"/>, or null when it has none this tick.</summary>
    public PerceivedView? ViewFor(int slot)
        => (uint)slot < (uint)_views.Length && _present[slot] ? _views[slot] : null;
}

/// <summary>
/// The last <see cref="Capacity"/> snapshots, one per tick (§2.2: 8). A
/// tick's snapshot lives in slot <c>tick mod capacity</c>, so a step whose
/// snapshot has been overwritten finds a different tick there and is skipped
/// (§2.5), never run on a newer one.
/// </summary>
public sealed class SnapshotRing
{
    private readonly AiSnapshot[] _ring;

    public SnapshotRing(int capacity, int slots, int sightedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _ring = new AiSnapshot[capacity];
        for (int i = 0; i < capacity; i++)
            _ring[i] = new AiSnapshot(slots, sightedCapacity);
    }

    public int Capacity => _ring.Length;

    /// <summary>How many slots each snapshot holds.</summary>
    public int Slots => _ring[0].Slots;

    /// <summary>The newest published tick, or null before the first.</summary>
    public Tick? Latest { get; private set; }

    /// <summary>
    /// A new level: its ticks count from low again, so nothing of the last
    /// level's stays readable and the next tick written may be any.
    /// </summary>
    public void Reset()
    {
        foreach (AiSnapshot s in _ring)
            s.Published = false;
        Latest = null;
    }

    /// <summary>The snapshot to fill for <paramref name="tick"/>; it replaces whatever tick held its slot.</summary>
    public AiSnapshot BeginWrite(Tick tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick.Value, nameof(tick));
        if (Latest is { } latest && tick <= latest)
            throw new ArgumentOutOfRangeException(nameof(tick), tick.Value, $"snapshots are written in tick order; {latest} is already published");

        AiSnapshot s = _ring[tick.Value % _ring.Length];
        s.Begin(tick);
        return s;
    }

    /// <summary>Makes <paramref name="snapshot"/> readable.</summary>
    public void Publish(AiSnapshot snapshot)
    {
        if (!ReferenceEquals(_ring[snapshot.Tick.Value % _ring.Length], snapshot))
            throw new ArgumentException("the snapshot is not this ring's", nameof(snapshot));
        snapshot.Published = true;
        Latest = snapshot.Tick;
    }

    /// <summary>The published snapshot for <paramref name="tick"/>, if it is still in the ring.</summary>
    public bool TryGet(Tick tick, out AiSnapshot snapshot)
    {
        snapshot = _ring[(int)((uint)tick.Value % (uint)_ring.Length)];
        return snapshot.Published && snapshot.Tick == tick;
    }
}
