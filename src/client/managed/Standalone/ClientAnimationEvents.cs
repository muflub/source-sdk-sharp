using SourceSharp.Abi;
using SourceSharp.Shared;

using static SourceSharp.Shared.AnimationEventClock;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// One animation event that actually happened, on one entity, this frame.
/// </summary>
/// <param name="Entity">Who fired it.</param>
/// <param name="ClassName">What that entity is, for the report.</param>
/// <param name="Sequence">The sequence that carried it.</param>
/// <param name="Carried">The event, as the model holds it.</param>
public readonly record struct AnimationEventFired(EntityHandle Entity, string ClassName,
                                                  int Sequence, ModelAnimationEvent Carried);

/// <summary>
/// What a fired animation event DOES. Separated from the noticing so that the
/// noticing can be tested without an engine, and so that a second consumer
/// (the report command) can watch without becoming one.
/// </summary>
public interface IAnimationEventSink
{
    /// <summary>Act on one event.</summary>
    void Fire(in AnimationEventFired fired);
}

/// <summary>
/// The half of the animation-event system that NOTICES: once a frame, for
/// every live client entity, which of its sequence's events the cycle has just
/// crossed.
///
/// <para>
/// Phase 5 built the half that CHECKS — <c>cs_animcheck</c> compares a managed
/// declaration against the model's real event table and says MISSING SEQUENCE
/// or MISSING EVENT — and nothing at all fired. The fire animation played, the
/// model carried <c>AE_MUZZLEFLASH</c> at cycle 0, and the screen stayed dark.
/// </para>
///
/// <para>
/// Driven from the client module's own frame hook rather than from
/// <c>OnDraw</c>, for the reason the dynamic lights are: OnDraw runs only for
/// an entity the leaf system thinks is visible, and an animation keeps its own
/// time whether or not anybody is looking at it. An entity that went off
/// screen mid-reload and came back would otherwise return with its cursor
/// where it was left, and fire the whole rest of the reload in one frame.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 3, the client's. What an event MEANS is a shared
/// declaration (<see cref="AnimationEventCatalog"/>, bucket 5) and the table
/// it is read out of is a fact of the model file (bucket 5 as well) — but the
/// cycle this walks is client-local and deliberately off the wire, and every
/// consumer below is presentation for one viewer. Nothing here feeds
/// prediction. The events that DO have server consequences are the ones this
/// refuses: <see cref="AnimationEventCatalog.IsClientEvent"/> drops everything
/// the event list marks <c>AE_TYPE_SERVER</c>, and drops the old-system events
/// below 5000 — which is why the grenade's throw, a bare 3005, is not fired
/// here even though the throw animation plays here.
/// </para>
/// </summary>
public sealed class AnimationEventPump
{
    private readonly IEngineEntityRender _render;
    private readonly IClientStandalones _standalones;
    private readonly IAnimationEventSink _sink;

    /// <summary>
    /// One cursor per entity, keyed by the handle's raw value.
    ///
    /// <para>
    /// Keyed rather than held on the entity because the pump is what owns the
    /// question. An entity subclass that forgot to carry a cursor would look
    /// exactly like an entity whose animation never moved, which is the
    /// failure this whole file exists to make impossible.
    /// </para>
    /// </summary>
    private readonly Dictionary<uint, AnimationEventCursor> _cursors = new();

    /// <summary>
    /// One entity's event tables, by sequence — plan_crossings.md Phase X5.
    ///
    /// <para>
    /// <b>What it replaces.</b> <c>EntitySequenceEventCount</c> was 2.000
    /// crossings a client frame (A11) and A12 named it a re-ask: the event
    /// table is read out of a <c>CStudioHdr</c> from the engine's model cache
    /// and is <b>immutable per (model, sequence)</b>. The whole table is
    /// memoised rather than only the count, because every entry in it comes
    /// from the same immutable header through the same two crossings
    /// (<c>SequenceEventCount</c> then <c>SequenceEvent</c> per event) — a memo
    /// that kept only the count would leave the per-event reads in place and
    /// win the smaller half of its own argument.
    /// </para>
    ///
    /// <para>
    /// <b>Nested rather than keyed on a composite, so invalidation is O(1).</b>
    /// The unit that goes stale is an ENTITY (its model changed, or it died and
    /// its number was recycled), never one sequence of one entity, and a
    /// flat <c>(handle, sequence)</c> dictionary would have to be scanned to
    /// drop one entity's rows.
    /// </para>
    ///
    /// <para>
    /// <b>Keyed on the entity and NOT on the model, which is a compromise the
    /// invalidation has to pay for.</b> The model index is networked straight
    /// into the shell's own C++ member and is never pushed to managed
    /// (<c>standalone_nettable.h:130-137</c>), and client-side <c>SetModel</c>
    /// does not exist — the ABI slot is filled only under
    /// <c>#ifndef CLIENT_DLL</c> — so managed cannot name this entity's model
    /// without a crossing, which is the thing being removed. What it CAN do is
    /// know when the model may have moved: <see cref="NoteDataChanged"/> is
    /// called from every <c>OnStandaloneDataChanged</c> push, and
    /// <c>C_StandaloneEntity::OnDataChanged</c>
    /// (<c>shell_standalone_client.cpp:436-443</c>) issues that push on EVERY
    /// update, for exactly the reason recorded three lines below it in the C++:
    /// "the model index is networked: an entity created with no model and given
    /// one three updates later". A model cannot change without a net update,
    /// and a net update cannot happen without that push, so this is a closure
    /// rather than a hope. The cost is that the memo is also dropped on updates
    /// that changed something else; it still spans every frame between updates,
    /// which is where the 2.000/frame was being spent.
    /// </para>
    /// </summary>
    private readonly Dictionary<uint, Dictionary<int, IReadOnlyList<ModelAnimationEvent>>> _tables = new();

    public AnimationEventPump(IEngineEntityRender render, IClientStandalones standalones,
                              IAnimationEventSink sink)
    {
        _render = render;
        _standalones = standalones;
        _sink = sink;
    }

    /// <summary>How many events have been fired since the level loaded. For the report.</summary>
    public int FiredCount { get; private set; }

    /// <summary>
    /// How many have fired on EACH entity, keyed by the handle's raw value.
    ///
    /// <para>
    /// <b>Per entity, because the whole-client total cannot answer the question
    /// step 6d is gated on.</b> "An event at a known cycle fires once" is a
    /// statement about one animation on one entity, and the client total moves
    /// for every muzzle flash on every viewmodel in the map — a player standing
    /// still with an idling weapon is already adding to it. A gate built on the
    /// total would be reading noise and calling it evidence, which is how a
    /// check that cannot fail gets written.
    /// </para>
    ///
    /// <para>
    /// It is a counter and not the oracle, on the same terms as
    /// <c>cs_animevents</c>: a count is a log line, and the oracle for a muzzle
    /// flash is a screenshot. What a count CAN settle is whether the same event
    /// fired twice, which no screenshot can.
    /// </para>
    /// </summary>
    private readonly Dictionary<uint, int> _firedPerEntity = new();

    /// <summary>How many events have fired on one entity. Zero for one nothing has.</summary>
    public int FiredOn(EntityHandle entity)
        => _firedPerEntity.TryGetValue(entity.Value, out int n) ? n : 0;

    /// <summary>How many entities are being watched. For the report and the tests.</summary>
    public int WatchedCount => _cursors.Count;

    /// <summary>
    /// How many event tables have been read out of a model since the level
    /// loaded. For the gate: it counts the crossings the memo did NOT save, so
    /// a memo that had quietly stopped memoising would show as this number
    /// tracking the frame count instead of settling.
    /// </summary>
    public int MemoisedCount { get; private set; }

    /// <summary>Every live entity, advanced. Called once a frame by the client module.</summary>
    public void Frame()
    {
        var live = _standalones.Live();

        for (int i = live.Count - 1; i >= 0; i--)
            Advance(live[i].Handle, live[i].ClassName);
    }

    /// <summary>
    /// One entity, advanced by whatever its clock has done since the last look.
    ///
    /// <para>
    /// Public because it is the unit worth testing: a test drives this with a
    /// stubbed clock and event table and asserts what reached the sink,
    /// without a frame or a registry anywhere near it.
    /// </para>
    /// </summary>
    public void Advance(EntityHandle entity, string className)
    {
        // A dormant entity answers the last values that reached it, without
        // complaint, and its cycle is derived from a start time that keeps
        // ticking against a live curtime. Left to run it would cross event
        // after event on an entity the server stopped sending — a muzzle flash
        // every half second from a weapon nobody is holding.
        if (_render.IsDormant(entity))
            return;

        // Fresh rather than default(), and the difference is the whole reason
        // AnimationEventCursor carries Established: a defaulted cursor claims
        // sequence 0, which is a real animation that a great many entities
        // really are playing, so an unseen entity would look like one that had
        // been watched all along and had simply not moved.
        AnimationEventCursor cursor = _cursors.TryGetValue(entity.Value, out var seen)
            ? seen
            : AnimationEventCursor.Fresh;

        bool alive = _render.SequenceState(entity, out int sequence, out float cycle,
                                           out float duration, out _, out int starts);

        // No clock at all: no entity, or a model with no sequence to run — a
        // static prop, which is most of this hierarchy. A duration of zero is
        // the honest way the shell says so, and it is NOT the same as a cycle
        // of zero. Treated as "no animation", so that a prop whose sequence 0
        // happens to carry events never fires one.
        if (!alive || duration <= 0f)
            sequence = -1;

        AnimationEventStep step = AnimationEventClock.Advance(cursor, sequence, starts, cycle);

        _cursors[entity.Value] = step.Cursor;

        if (!step.Any)
            return;

        // Read the table only when something was crossed. On the overwhelming
        // majority of frames nothing was, and the table read is two ABI
        // crossings per event.
        var carried = Table(entity, sequence);

        if (carried.Count == 0)
            return;

        // The TAIL first and the head second, both in the model's own order.
        // On a wrap the events at the end of the lap that finished happened
        // before the events at the start of the one that began, and a reload
        // whose last sound landed after its first would be audibly wrong.
        FireWindow(entity, className, sequence, carried, step.Tail);
        FireWindow(entity, className, sequence, carried, step.Head);
    }

    /// <summary>
    /// This sequence's event table, from the memo if it is there and from the
    /// model if it is not. See <see cref="_tables"/>.
    /// </summary>
    private IReadOnlyList<ModelAnimationEvent> Table(EntityHandle entity, int sequence)
    {
        if (!_tables.TryGetValue(entity.Value, out var bySequence))
        {
            bySequence = new Dictionary<int, IReadOnlyList<ModelAnimationEvent>>();
            _tables[entity.Value] = bySequence;
        }

        if (bySequence.TryGetValue(sequence, out var memoised))
            return memoised;

        // An empty table is memoised like any other. Most sequences carry no
        // events at all, so "nothing here" is the answer worth keeping most:
        // not keeping it would leave the count crossing in place for exactly
        // the entities that ask for it most often.
        var read = ModelAnimationCheck.Read(_render, entity, sequence);

        bySequence[sequence] = read;
        MemoisedCount++;
        return read;
    }

    /// <summary>
    /// This entity got a network update, so its model may have moved under its
    /// handle. Its memoised tables go. See <see cref="_tables"/>.
    ///
    /// <para>
    /// The cursor is NOT dropped here. A net update is not a discontinuity in
    /// the animation clock — the clock is client-local and keeps running across
    /// updates — and dropping the cursor would re-establish it as
    /// <c>AnimationEventCursor.Fresh</c>, which claims sequence 0 and would
    /// fire that sequence's whole event list on the next frame.
    /// </para>
    /// </summary>
    public void NoteDataChanged(EntityHandle entity) => _tables.Remove(entity.Value);

    /// <summary>Forget one entity — it has gone away.</summary>
    public void Forget(EntityHandle entity)
    {
        _cursors.Remove(entity.Value);
        _tables.Remove(entity.Value);

        // The per-entity count goes too, and it MUST: handles are recycled, so
        // a count left behind is not stale, it is inherited by whatever entity
        // next wears the number — and a gate that asked "how many have fired on
        // this one" would be told about its predecessor.
        _firedPerEntity.Remove(entity.Value);
    }

    /// <summary>Forget everything. The level is going.</summary>
    public void Reset()
    {
        _cursors.Clear();
        _tables.Clear();
        _firedPerEntity.Clear();
        FiredCount = 0;
        MemoisedCount = 0;
    }

    private void FireWindow(EntityHandle entity, string className, int sequence,
                            IReadOnlyList<ModelAnimationEvent> carried,
                            AnimationEventWindow? window)
    {
        if (window is not AnimationEventWindow band)
            return;

        foreach (var ev in carried)
        {
            if (!band.Contains(ev.Cycle))
                continue;

            // The side filter, and it is not the obvious one. Stock tests
            // `type & AE_TYPE_CLIENT`, and that bit is NOT in the .mdl — it is
            // OR-ed in by SetEventIndexForSequence, which this shell
            // deliberately does not run because it writes back into the model
            // it is reading. Tested here, a dispatcher would fire nothing at
            // all, forever, with every log line agreeing that it worked. See
            // AnimationEventCatalog.
            if (!AnimationEventCatalog.IsClientEvent(ev))
                continue;

            FiredCount++;
            _firedPerEntity[entity.Value] = FiredOn(entity) + 1;
            _sink.Fire(new AnimationEventFired(entity, className, sequence, ev));
        }
    }
}
