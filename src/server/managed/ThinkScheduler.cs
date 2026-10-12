namespace SourceSharp.Abi;

/// <summary>
/// One scheduled think: WHO thinks, in WHICH context, WHEN, and the order it
/// was booked in.
/// </summary>
/// <param name="Entity">The entity that asked.</param>
/// <param name="Context">
/// The named context, or <c>null</c> for the default one — the schedule stock
/// calls <c>SetNextThink</c> as opposed to <c>SetContextThink</c>.
/// </param>
/// <param name="Time">Absolute <c>curtime</c> the think comes due at.</param>
/// <param name="Sequence">
/// A booking number, monotonic across the whole scheduler. It is the tie-break
/// between two thinks due at the SAME time, and it is what makes equal-time
/// ordering a fact rather than whatever the heap happens to do: two thinks
/// booked for one instant run in the order they were booked.
/// </param>
public readonly record struct ScheduledThink(EntityHandle Entity, string? Context,
                                             float Time, long Sequence);

/// <summary>
/// The think heap — <c>plan_entity.md</c> §3.4 and step 1g.
///
/// <para>
/// <b>WHY THIS EXISTS AT ALL.</b> Before it, the schedule lived in the native
/// shell (<c>CStandaloneEntity::m_flNextThink</c>) and the native frame walked
/// every shell asking whether its time had come; each one whose had crossed
/// into managed code on its own. That is one crossing PER THINKING ENTITY PER
/// FRAME, and §3.4's rule is that per-frame work is never one crossing per
/// entity. With the schedule here, the whole of think is inside the one
/// <c>GameFrame</c> crossing the server frame already costs, and the crossing
/// count no longer moves when the number of thinking entities does.
/// </para>
///
/// <para>
/// <b>LAZY DELETION, NOT A HEAP THAT SUPPORTS REMOVE.</b> Rescheduling and
/// cancelling are far more common than a think coming due — an entity that
/// thinks every tick reschedules every tick — so the heap is append-only and
/// <see cref="_default"/>/<see cref="_named"/> name the ONE booking that is
/// still live for a given (entity, context). A pop whose sequence is not the
/// live one is a superseded booking and is dropped. That makes
/// <see cref="Schedule"/> and <see cref="Cancel"/> O(log n) and O(1), and it
/// is why every entry carries its sequence.
/// </para>
///
/// <para>
/// <b>A THINK THAT RESCHEDULES ITSELF FOR NOW RUNS NEXT FRAME.</b>
/// <see cref="TakeDue"/> takes a SNAPSHOT: it drains what is due, unbooks it,
/// and hands it back, and only then does the caller run any of it. So a
/// booking made from inside a think — the ordinary case, since a repeating
/// think reschedules itself — was made after the snapshot and cannot be in it.
/// The native loop this replaces had the same property for the same reason
/// (its <c>for</c> index had already passed the entity), and losing it would
/// turn <c>SetNextThink(0)</c> into an infinite loop inside one frame rather
/// than a think that runs every tick.
/// </para>
///
/// <para>
/// <b>THE BOOKING IS CLEARED BEFORE THE THINK RUNS, NOT AFTER.</b> Same
/// reason the native loop cleared <c>m_flNextThink</c> before calling
/// <c>Think()</c>: a think that schedules its own next one is the ordinary
/// case, and clearing afterwards would throw that away — the entity would run
/// once and stop, with nothing to say why. <see cref="TakeDue"/> unbooks as it
/// drains, so by the time anything runs the slot is already free.
/// </para>
///
/// <para>
/// <b>NOT A STATIC.</b> One scheduler per <see cref="EntityDirectory"/>, one
/// directory per side module (<c>plan.md</c> §1a).
/// </para>
/// </summary>
public sealed class ThinkScheduler
{
    /// <summary>
    /// (time, sequence) ascending — the whole ordering policy, in one named
    /// place so a test can assert it rather than infer it from two thinks that
    /// happened to come out in the right order.
    /// </summary>
    private sealed class DueOrder : IComparer<(float Time, long Sequence)>
    {
        public static readonly DueOrder Instance = new();

        public int Compare((float Time, long Sequence) a, (float Time, long Sequence) b)
        {
            int byTime = a.Time.CompareTo(b.Time);
            return byTime != 0 ? byTime : a.Sequence.CompareTo(b.Sequence);
        }
    }

    /// <summary>One live booking: when it comes due, and its booking number.</summary>
    private readonly record struct Booking(float Time, long Sequence);

    private readonly PriorityQueue<ScheduledThink, (float Time, long Sequence)> _heap =
        new(DueOrder.Instance);

    /// <summary>
    /// The live booking for each entity's DEFAULT context, by handle value.
    ///
    /// <para>
    /// Separate from <see cref="_named"/> and not a one-entry case of it,
    /// because almost every thinking entity uses only this one: for the
    /// attributed classes that think, the default context IS the whole
    /// schedule, and giving each of them a nested dictionary to hold one
    /// booking would allocate one per thinking entity.
    /// </para>
    /// </summary>
    private readonly Dictionary<uint, Booking> _default = new();

    /// <summary>
    /// The live booking per named context, by handle value then context name.
    /// Allocated only for an entity that actually uses one.
    /// </summary>
    private readonly Dictionary<uint, Dictionary<string, Booking>> _named = new();

    private long _nextSequence;

    /// <summary>How many bookings are live. Superseded entries are not counted.</summary>
    public int Pending => _default.Count + NamedCount();

    /// <summary>
    /// How many entries the heap is carrying, live and superseded together.
    /// Diagnostics only — see <see cref="Pending"/> for the real answer.
    /// </summary>
    public int HeapDepth => _heap.Count;

    /// <summary>
    /// Every live booking, in no defined order.
    ///
    /// <para>
    /// <b>WHY A SCHEDULE HAS TO BE ENUMERABLE.</b> The heap is a module-scoped
    /// managed object, so a hot reload destroys it — and nothing rebuilds it,
    /// because the reload deliberately does not re-run <c>OnSpawn</c> and
    /// <c>OnSpawn</c> is where most classes make their first booking. Measured
    /// on <c>dm_lockdown</c>: <c>think.pending</c> read 8 before a
    /// <c>cs_sv_reload</c> and 0 after it, with the live gate green. Every timed
    /// entity in the world stopped, silently, with nothing about any of them
    /// looking wrong.
    /// </para>
    ///
    /// <para>
    /// LIVE ONLY, NOT THE HEAP. The heap carries superseded entries by design
    /// (see <see cref="HeapDepth"/>); carrying those across would re-book each
    /// entity at every time it had ever been booked at, and the earliest of them
    /// would win.
    /// </para>
    ///
    /// <para>
    /// The <c>Sequence</c> is reported and is NOT worth carrying back: it orders
    /// ties within one lifetime of this heap, and <see cref="Schedule"/> stamps a
    /// fresh one on the way in. What has to survive is the TIME.
    /// </para>
    /// </summary>
    public IEnumerable<ScheduledThink> Bookings()
    {
        foreach (var (handle, booking) in _default)
            yield return new ScheduledThink(new EntityHandle(handle), null,
                                            booking.Time, booking.Sequence);

        foreach (var (handle, contexts) in _named)
        {
            foreach (var (context, booking) in contexts)
            {
                yield return new ScheduledThink(new EntityHandle(handle), context,
                                                booking.Time, booking.Sequence);
            }
        }
    }

    private int NamedCount()
    {
        int n = 0;
        foreach (var contexts in _named.Values)
            n += contexts.Count;
        return n;
    }

    /// <summary>
    /// Books <paramref name="entity"/> to think at absolute time
    /// <paramref name="time"/> in <paramref name="context"/>, replacing whatever
    /// that (entity, context) had booked before.
    ///
    /// <para>
    /// A negative time is <see cref="Cancel"/>, spelt the way the ABI slot this
    /// replaced spelt it: a negative delay meant "stop", because scheduling in
    /// the past would run every frame forever.
    /// </para>
    /// </summary>
    public void Schedule(EntityHandle entity, string? context, float time)
    {
        if (time < 0.0f)
        {
            Cancel(entity, context);
            return;
        }

        long sequence = _nextSequence++;
        uint key = entity.Value;
        var booking = new Booking(time, sequence);

        if (context is null)
        {
            _default[key] = booking;
        }
        else
        {
            if (!_named.TryGetValue(key, out var contexts))
                _named[key] = contexts = new Dictionary<string, Booking>(StringComparer.Ordinal);

            contexts[context] = booking;
        }

        _heap.Enqueue(new ScheduledThink(entity, context, time, sequence), (time, sequence));
    }

    /// <summary>
    /// Unbooks one (entity, context) and answers whether there was anything to
    /// unbook. Every other context of the same entity is untouched — that
    /// independence is the whole point of a named context.
    /// </summary>
    public bool Cancel(EntityHandle entity, string? context)
    {
        uint key = entity.Value;

        if (context is null)
            return _default.Remove(key);

        if (!_named.TryGetValue(key, out var contexts))
            return false;

        bool had = contexts.Remove(context);

        if (contexts.Count == 0)
            _named.Remove(key);

        return had;
    }

    /// <summary>
    /// Unbooks every context of one entity. What removal calls: an entity that
    /// has gone must not be handed a think, and its bookings must not sit in
    /// the tables until their time comes.
    /// </summary>
    public void CancelAll(EntityHandle entity)
    {
        uint key = entity.Value;
        _default.Remove(key);
        _named.Remove(key);
    }

    /// <summary>Drops every booking. A level change.</summary>
    public void Clear()
    {
        _heap.Clear();
        _default.Clear();
        _named.Clear();
    }

    /// <summary>
    /// The absolute time one (entity, context) is booked for, or -1 when it is
    /// not booked. Negative means NOT SCHEDULED, which is the spelling the
    /// deleted <c>SetScriptedNextThink</c>/<c>EntityGetNextThink</c> pair used
    /// and which the save state still carries.
    /// </summary>
    public float NextThinkTime(EntityHandle entity, string? context)
    {
        uint key = entity.Value;

        if (context is null)
            return _default.TryGetValue(key, out var booking) ? booking.Time : -1.0f;

        return _named.TryGetValue(key, out var contexts) && contexts.TryGetValue(context, out var named)
                   ? named.Time
                   : -1.0f;
    }

    /// <summary>
    /// Whether this (entity, context) has a live booking.
    /// </summary>
    public bool IsScheduled(EntityHandle entity, string? context)
        => NextThinkTime(entity, context) >= 0.0f;

    /// <summary>
    /// Drains everything due at or before <paramref name="curTime"/> into
    /// <paramref name="into"/>, in (time, booking) order, unbooking each as it
    /// goes, and answers how many were taken.
    ///
    /// <para>
    /// <paramref name="into"/> is cleared first. The caller supplies it so the
    /// per-frame pass allocates nothing.
    /// </para>
    ///
    /// <para>
    /// NOTHING IS RUN HERE. See the type's remarks: taking the whole snapshot
    /// before anything runs is what stops a think that reschedules itself for
    /// now from running twice in one frame.
    /// </para>
    /// </summary>
    public int TakeDue(float curTime, List<ScheduledThink> into)
    {
        into.Clear();

        while (_heap.TryPeek(out var entry, out _) && entry.Time <= curTime)
        {
            _heap.Dequeue();

            if (!IsLive(entry))
                continue;   // superseded by a later Schedule, or cancelled

            Unbook(entry);
            into.Add(entry);
        }

        return into.Count;
    }

    private bool IsLive(in ScheduledThink entry)
    {
        uint key = entry.Entity.Value;

        if (entry.Context is null)
            return _default.TryGetValue(key, out var booking) && booking.Sequence == entry.Sequence;

        return _named.TryGetValue(key, out var contexts)
               && contexts.TryGetValue(entry.Context, out var named)
               && named.Sequence == entry.Sequence;
    }

    private void Unbook(in ScheduledThink entry) => Cancel(entry.Entity, entry.Context);
}
