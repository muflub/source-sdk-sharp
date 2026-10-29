//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The managed side's own table of live entities, indexed by HANDLE ENTRY —
/// <c>plan_entity.md</c> §3.5, built by step 1g.
///
/// <para>
/// <b>AN ARRAY, NOT A DICTIONARY, AND THAT IS THE POINT.</b> §3.4: "The
/// per-callback dictionary lookup the registries do today becomes an array
/// index, because the directory is an array indexed by handle entry." Every
/// crossing that names an entity resolves it exactly once, and that resolution
/// is now a bounds check and two array reads.
/// </para>
///
/// <para>
/// <b>THE SERIAL IS CHECKED, AND IT IS CHECKED BY STORING THE WHOLE HANDLE.</b>
/// A slot holds the handle it was bound with; a resolve compares the caller's
/// handle against it and fails closed when they differ. That gets the serial
/// check for nothing and with no second table to keep in step: the serial is
/// already in the handle, and the engine bumps it on unbind
/// (<c>slot_table.h</c>, "BUMP ON UNBIND"), so a stale handle differs from the
/// live one in the bits that say WHICH OCCUPANT.
/// </para>
///
/// <para>
/// <b>THE ENTRY DECOMPOSITION IS STOCK'S, INCLUDING ITS ODDITY.</b>
/// <c>public/const.h</c> in this tree makes <c>ENT_ENTRY_MASK</c> from the
/// SERIAL width (16 bits) and not from the entry width (13), and
/// <c>basehandle.h:143-153</c> answers <c>NUM_ENT_ENTRIES-1</c> for the
/// invalid handle on purpose — its own comment calls that "bug-submarining".
/// Both are mirrored here rather than tidied, because
/// <c>shell_standalone_base.cpp:185</c> records what tidying it cost last time:
/// "decomposing by hand does not round-trip … Getting it wrong is silent — the
/// handle simply never resolves, and the symptom was a think that never ran."
/// </para>
///
/// <para>
/// <b>THE ENGINE-INVISIBLE RANGE IS IN HERE TOO.</b> Entries 2048..8191 are
/// not edicts (<c>slot_table.h</c>, §3.3): they belong to entities the engine
/// has never heard of. Sizing this to <c>MAX_EDICTS</c> would make every one of
/// them unaddressable, and the failure would be a silent null from a resolve.
/// </para>
///
/// <para>
/// <b>NOT A STATIC.</b> One directory per side module (<c>plan.md</c> §1a);
/// <see cref="ScriptedEntityRegistry"/> owns the server's.
/// </para>
/// </summary>
public sealed class EntityDirectory : EntityDirectoryCore<ScriptedEntity>
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    //=========================================================================
    // THE STORAGE IS <see cref="EntityDirectoryCore{TEntity}"/> -- lane
    // sub-entitylist, answering entlist-client's orphaned fold request.
    //
    // EntryCount, EntryMask, EntryIndex, the handle array, the entity array,
    // the dense list with its inverse, the classname index, the name index and
    // the guarded Detach all live there now and are inherited, so
    // EntityDirectory.EntryCount and EntityDirectory.EntryIndex( h ) still
    // resolve exactly as before. What is below this line is what is genuinely
    // the SERVER's: the think heap, the touch link table, the I/O queue and its
    // connections, the removal marks and the engine-invisible allocator.
    //
    // TracksEntityIndex is FALSE here, deliberately, and the core says why: a
    // server shell binds the entry the engine gave it, so index -> entity is
    // the identity map and a table for it would cost 32KB to answer a question
    // Resolve( HandleOf( i ) ) already answers.
    //=========================================================================

    /// <summary>The classname index's key — the name the class was registered under.</summary>
    protected override string? ClassNameOf(ScriptedEntity entity) => entity.ClassName;

    /// <summary>
    /// CBaseEntity::SetClassname: <paramref name="entity"/> reports
    /// <paramref name="className"/> from here on, and is found by it.
    /// </summary>
    public void SetClassName(ScriptedEntity entity, string className)
    {
        string was = entity.ClassName;
        entity.ClassName = className;
        Reclass(entity.Handle, was);
    }

    /// <summary>The think heap this directory runs — <c>plan_entity.md</c> §3.5.</summary>
    public ThinkScheduler Think { get; } = new();

    /// <summary>Scratch for <see cref="RunFrame"/>, so the per-frame pass allocates nothing.</summary>
    private readonly List<ScheduledThink> _due = new();

    /// <summary>
    /// WHICH ENTRIES ARE MARKED FOR REMOVAL, by the handle that marked them —
    /// step 2c, and see the removal region below for why it is a handle table
    /// and not a <c>bool[]</c>.
    /// </summary>
    private readonly EntityHandle[] _removing = new EntityHandle[EntryCount];

    /// <summary>The marks in mark order, which is the order they are freed in.</summary>
    private readonly List<EntityHandle> _pendingRemoval = new();

    /// <summary>One pass's worth of <see cref="_pendingRemoval"/>, reused.</summary>
    private readonly List<EntityHandle> _batch = new();

    /// <summary>
    /// How many thinks the last <see cref="RunFrame"/> ran, and how many have
    /// run since the directory was made.
    ///
    /// <para>
    /// <b>INSTRUMENTATION, and it is what keeps this step's crossing claim from
    /// being vacuous.</b> "Think costs zero crossings now" is trivially true of
    /// a build that stopped thinking, and nothing else in <c>debug-api status</c>
    /// can tell the two apart: the op counter this replaced reads zero either
    /// way. These two say how much work the one frame crossing is carrying, so
    /// the before/after is a comparison and not an absence. Surfaced as
    /// <c>status.think</c>. Named as debt: the performance pass owns dropping
    /// them.
    /// </para>
    /// </summary>
    public int ThinksRanLastFrame { get; private set; }

    private readonly EventIdStamps _stamps;

    /// <inheritdoc cref="ThinksRanLastFrame"/>
    public long ThinksRunTotal { get; private set; }

    /// <param name="stamps">
    /// The side's connection id stamps (statics to zero): the registry hands
    /// in its side state's, which outlives a reload; a directory built alone
    /// (a test) counts from its own.
    /// </param>
    public EntityDirectory(ILog log, EventIdStamps? stamps = null)
    {
        _log = log;
        _stamps = stamps ?? new EventIdStamps();
        Io = new EntityIoQueue(log);

        // THE REMOVAL MARKS, AND default(EntityHandle) WOULD BE A BUG -- step
        // 2c. See the removal region for the whole of it: entry 0 is a real,
        // bindable slot and a zeroed array would read as "entry 0 is marked".
        // The core's own handle arrays are filled for the same reason, in its
        // constructor, which runs first.
        Array.Fill(_removing, EntityHandle.None);
    }

    /// <summary>
    /// Puts <paramref name="entity"/> in <paramref name="handle"/>'s entry,
    /// replacing whatever occupied it, and answers whether it took.
    ///
    /// <para>
    /// Refuses the invalid handle and an entry outside the table. Both are
    /// refusals rather than clamps: <see cref="EntryMask"/> is wider than the
    /// table by stock's own arithmetic, so a handle can name an entry that does
    /// not exist, and binding it somewhere else would be a silent
    /// mis-addressing rather than a failure.
    /// </para>
    /// </summary>
    public bool Bind(EntityHandle handle, ScriptedEntity entity)
        => BindCore(handle, -1, entity);

    /// <summary>
    /// An entry is being taken from <paramref name="evicted"/> by a different
    /// handle, and its bookings go with it.
    ///
    /// <para>
    /// The evicted handle can never be unbound by its owner afterwards, so its
    /// thinks are cancelled here or they sit in the heap until their time
    /// comes; its I/O goes for the same reason, or its connections and its name
    /// would answer for the entity that replaced it; and its touch links go
    /// because a link keyed by the evicted handle can never be renewed, so it
    /// would report a departure on the next frame and name an entity that is
    /// gone (step 2b).
    /// </para>
    /// </summary>
    protected override void OnEvicting(EntityHandle evicted)
    {
        Think.CancelAll(evicted);
        ForgetIo(evicted);
        ForgetTouches(evicted);
    }

    /// <summary>
    /// Empties <paramref name="handle"/>'s entry, IF that entry still holds
    /// this handle, and cancels every think the entity had booked. Answers
    /// whether anything was there.
    ///
    /// <para>
    /// The "if it still holds this handle" is not belt and braces: an entry
    /// that has been rebound belongs to its new occupant, and unbinding it on a
    /// stale handle would evict a live entity.
    /// </para>
    /// </summary>
    public bool Unbind(EntityHandle handle)
    {
        // THE SAME REFUSALS UnbindCore MAKES, RESTATED, and that is deliberate
        // rather than sloppy: everything between here and the UnbindCore call
        // has to happen BEFORE the entry is emptied and must not happen at all
        // for a handle the entry does not hold. The core re-checks, so the two
        // cannot disagree about whether the unbind took -- what this buys is
        // that the five steps below are never run against a stale handle.
        if (handle == EntityHandle.None)
            return false;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount || HandleOf(entry) != handle)
            return false;

        Think.CancelAll(handle);

        // THE I/O HALF'S LINE, beside the think one and for the same reason.
        // stock's CBaseEntity::UpdateOnRemove calls g_EventQueue.CancelEvents(
        // this ), so an entity that is going takes the events it posted with
        // it; without this a relay removed one frame delivers its two-second
        // output anyway, from an entity that no longer exists. The entity's
        // CONNECTIONS and its NAME go too -- see the I/O region below.
        ForgetIo(handle);

        // AND THE REMOVAL MARK -- step 2c's line outside its region. An entry
        // unbound by any path other than the free pass (an eviction, a level
        // change, a native teardown) must not leave a mark behind: the next
        // entity to take the entry would be freed at the end of the frame it
        // spawned in, and nothing anywhere would say why.
        _removing[entry] = EntityHandle.None;

        // AND THE TOUCH HALF'S -- step 2b. Every pair this entity is in goes,
        // and the entity at the OTHER end of each is told it has left, which is
        // what CBaseEntity::UpdateOnRemove does through
        // PhysicsLink_RemoveTouchedList. See ForgetTouches.
        ForgetTouches(handle);

        // THE STORAGE, THE INDEXES AND THE DENSE LIST -- the core's, and the
        // one place either side empties an entry.
        if (!UnbindCore(handle, out _))
            return false;

        // AND THE INVISIBLE ENTRY, IF THIS WAS A SHELL-LESS ENTITY -- step 2e.
        // Unbind is the ONE place an entry leaves this directory on every path
        // that is not a level change (a free, an eviction, a native teardown),
        // so it is where the entry goes back to the allocator and its serial is
        // bumped. Doing it in the removal drain instead would leak an entry per
        // eviction, silently, and the symptom would be the invisible range
        // filling up over a long session.
        //
        // AFTER the core's clear, which is the position it had, and NOT in
        // OnDetaching: Detach also runs on Bind's eviction path, where stock's
        // shape releases nothing -- an evicted entry's shell-less state belongs
        // to whatever is really there.
        ReleaseInvisible(entry);

        return true;
    }

    //=========================================================================
    // THE TOUCH HALF -- plan_entity.md step 2b, and §3.5's "the touch link
    // table: which pairs are currently touching, so StartTouch and EndTouch
    // edges are computed in managed code from the per-frame overlap report".
    //
    // A REGION OF ITS OWN, after 2a's I/O banner and before 2c's removal one.
    // The only lines 2b adds OUTSIDE it are one call in Bind's eviction arm,
    // one in Unbind and one in Clear, each with a comment naming the step --
    // the shape 2a asked the concurrent lanes to keep.
    //
    // WHAT THE NATIVE SIDE STILL DOES, and it is one sentence: it says which
    // pairs overlap, right now. Everything that follows from that -- is this an
    // arrival, is it the same overlap as last frame, has one gone, does this
    // class hear about every tick or only the edges -- was C++ and is here.
    //=========================================================================

    /// <summary>
    /// Which end of a touch is being reported.
    ///
    /// <para>
    /// <b>THREE, and the middle one is not an edge at all.</b>
    /// <see cref="TouchEdge.Start"/> and <see cref="TouchEdge.End"/> are the
    /// transitions; <see cref="TouchEdge.Stay"/> is the LEVEL between them, and
    /// it is raised on every frame of an overlap for the classes that ask for
    /// it. That is the same distinction stock draws between
    /// <c>StartTouch</c>/<c>EndTouch</c> and <c>Touch</c>, and for the same
    /// reason: a trigger cares about the edges and a pickup cares about the
    /// level.
    /// </para>
    /// </summary>
    public enum TouchEdge
    {
        /// <summary>These two were not touching last frame and are now.</summary>
        Start,

        /// <summary>They were, and still are.</summary>
        Stay,

        /// <summary>They were, and are not any more.</summary>
        End,
    }

    /// <summary>
    /// Raises one touch edge on one entity.
    ///
    /// <para>
    /// A DELEGATE for <see cref="EntityInputDelivery"/>'s reason: the directory
    /// does not know what a touch MEANS. Which handler on the class runs, the
    /// "players only unless the class says otherwise" rule, and the pickup
    /// service the map items fall through to are the side module's. What is
    /// here is which pair, on which frame, and which of the three edges.
    /// </para>
    /// </summary>
    public delegate void TouchDelivery(EntityHandle receiver, EntityHandle toucher, TouchEdge edge);

    /// <summary>How an edge reaches an entity. Set by the side module that owns this directory.</summary>
    public TouchDelivery? TouchRouting { get; set; }

    /// <summary>One remembered overlap: the two ends, and the frame it was last reported on.</summary>
    private readonly struct TouchLink
    {
        public TouchLink(EntityHandle a, EntityHandle b, int frame)
        {
            A = a;
            B = b;
            Frame = frame;
        }

        public EntityHandle A { get; }
        public EntityHandle B { get; }
        public int Frame { get; }

        public TouchLink SeenOn(int frame) => new(A, B, frame);

        /// <summary>The other end, given one of them.</summary>
        public EntityHandle Other(EntityHandle end) => end == A ? B : A;

        public bool Names(EntityHandle end) => end == A || end == B;
    }

    /// <summary>
    /// Which pairs are currently touching, keyed by the pair.
    ///
    /// <para>
    /// <b>ONE ENTRY PER PAIR, NOT ONE PER SIDE.</b> A touch is symmetric — the
    /// native shells each kept their own list naming the other, which is two
    /// records of one fact and two chances for them to disagree. Both ends are
    /// told from the single entry, which is what makes "A entered B" and
    /// "B entered A" impossible to observe separately.
    /// </para>
    ///
    /// <para>
    /// The key is the two handle VALUES, low first, so the pair is the same key
    /// whichever order the report names it in — and it is the whole handle, so
    /// a stale serial is a different pair rather than the same one. That is the
    /// directory's own rule (see the type's remarks) applied to a two-ended
    /// fact.
    /// </para>
    /// </summary>
    private readonly Dictionary<ulong, TouchLink> _touching = new();

    /// <summary>Scratch for the expiry sweep, so a frame with no departures allocates nothing.</summary>
    private readonly List<ulong> _expired = new();

    /// <summary>
    /// Which frame the table is on. The directory's OWN count, incremented once
    /// per report, rather than the engine's — a frame number is only ever
    /// compared with itself here, and taking it from the engine would make
    /// every fact below need a clock.
    /// </summary>
    private int _touchFrame;

    /// <summary>How many touch edges the last <see cref="TouchFrame"/> raised, and how many since the directory was made.</summary>
    public int TouchEdgesLastFrame { get; private set; }

    /// <inheritdoc cref="TouchEdgesLastFrame"/>
    public long TouchEdgesTotal { get; private set; }

    /// <summary>How many pairs are touching right now.</summary>
    public int TouchLinkCount => _touching.Count;

    /// <summary>Whether these two are currently linked. For a report and a test.</summary>
    public bool IsTouching(EntityHandle a, EntityHandle b)
        => _touching.ContainsKey(PairKey(a, b));

    /// <summary>
    /// Every touching pair, in no defined order.
    ///
    /// <para>
    /// <b>WHY A TOUCH TABLE HAS TO SURVIVE A RELOAD.</b> This dictionary is
    /// module-scoped, so the unload destroys it, and the NEXT frame's overlap
    /// report re-derives it — which sounds like the problem solving itself and
    /// is not. Every still-overlapping pair takes the START arm above rather
    /// than the STAY arm, so every trigger a player is standing in re-fires
    /// <c>OnStartTouch</c>: a <c>trigger_hurt</c> re-applies its entry damage, a
    /// counter double-counts, a door re-opens. And the mirror is worse — a pair
    /// that SEPARATED while the assembly was swapping is never in the new table
    /// at all, so it never gets its <c>OnEndTouch</c>, and the "every start
    /// touch gets an end touch" invariant this file states is broken for the
    /// rest of the map.
    /// </para>
    /// </summary>
    public IEnumerable<(EntityHandle A, EntityHandle B)> TouchingPairs()
    {
        foreach (var (_, link) in _touching)
            yield return (link.A, link.B);
    }

    /// <summary>
    /// Puts a pair back into the table WITHOUT raising an edge.
    ///
    /// <para>
    /// <b>NO EDGE IS THE ENTIRE POINT.</b> This runs after a hot reload, and the
    /// pair was already touching before it — the entities never separated, so
    /// there is no arrival to report and reporting one is precisely the
    /// double-fire this exists to stop. What the restored link buys is the NEXT
    /// report: a pair the report still names takes the Stay arm, and a pair it
    /// does not takes the departure sweep and gets the <c>OnEndTouch</c> it is
    /// owed.
    /// </para>
    ///
    /// <para>
    /// SEEDED AT <c>_touchFrame</c>, WHICH IS THE FRAME BEFORE THE NEXT REPORT
    /// — <see cref="TouchFrame"/> pre-increments. That is what makes the next
    /// report do the right thing in both directions with no special case: a
    /// pair the report still names has a stale frame, so it takes the STAY arm
    /// and raises the tick edge an occupant is owed; a pair it does not name
    /// also has a stale frame, so the departure sweep expires it and raises the
    /// <c>OnEndTouch</c> that the reload would otherwise have swallowed.
    /// Seeding at the report's OWN frame would have made a departed pair look
    /// present forever.
    /// </para>
    ///
    /// <para>
    /// Refuses the same two things <see cref="TouchFrame"/> refuses, for the
    /// same reasons: the invalid handle and a self-pair. Answers whether the
    /// link was added.
    /// </para>
    /// </summary>
    public bool RestoreTouch(EntityHandle a, EntityHandle b)
    {
        if (a.Value == uint.MaxValue || b.Value == uint.MaxValue || a == b)
            return false;

        ulong key = PairKey(a, b);

        if (_touching.ContainsKey(key))
            return false;

        _touching[key] = new TouchLink(a, b, _touchFrame);
        return true;
    }

    private static ulong PairKey(EntityHandle a, EntityHandle b)
        => a.Value <= b.Value
            ? ((ulong)a.Value << 32) | b.Value
            : ((ulong)b.Value << 32) | a.Value;

    /// <summary>
    /// THE DIFF, WHICH IS THE WHOLE STEP — one frame's overlap report against
    /// the link table, turned into arrivals, stays and departures.
    ///
    /// <para>
    /// <b>The report says what OVERLAPS, and nothing else.</b> The engine has
    /// never had a way to say "this one has left": it reports a pair while the
    /// two are inside each other and simply stops mentioning them afterwards,
    /// so leaving is the ABSENCE of a report and the only way to see an absence
    /// is to have remembered the presence. That memory is
    /// <see cref="_touching"/> and this is the sweep over it.
    /// </para>
    ///
    /// <para>
    /// <b>A pair named twice in one report is one pair.</b> The same two
    /// entities reach the report by more than one route — a trigger that moved
    /// and a solid that moved into it are both asked, and the native side
    /// deduplicates what it can see — so the frame stamp is what makes a second
    /// mention a no-op rather than a second arrival.
    /// </para>
    ///
    /// <para>
    /// <b>The sweep runs after every arrival has been recorded</b>, not
    /// interleaved with them. A departure decided halfway through the report
    /// would fire for a pair the rest of the report was about to renew.
    /// </para>
    ///
    /// <para>Answers how many edges were raised.</para>
    /// </summary>
    public int TouchFrame(ReadOnlySpan<OverlapPair> report)
    {
        int frame = ++_touchFrame;
        int edges = 0;

        foreach (var pair in report)
        {
            // The invalid handle and a self-pair are refused rather than
            // recorded. Entry 0xFFFF is a bindable entry (see EntryIndex), so a
            // pair carrying the invalid handle would key a link nothing can
            // ever resolve and the sweep would report a departure for it every
            // frame after the first.
            if (pair.A.Value == uint.MaxValue || pair.B.Value == uint.MaxValue || pair.A == pair.B)
                continue;

            ulong key = PairKey(pair.A, pair.B);

            if (_touching.TryGetValue(key, out var link))
            {
                if (link.Frame == frame)
                    continue;

                _touching[key] = link.SeenOn(frame);
                edges += Stay(link.A, link.B);
                continue;
            }

            _touching[key] = new TouchLink(pair.A, pair.B, frame);

            // BOTH ENDS, because a touch is symmetric and stock has always
            // treated it so: PhysicsMarkEntitiesAsTouching gives each of the
            // two a link naming the other and calls Touch on both. Half of that
            // went missing the moment one side stopped being a CBaseEntity;
            // here neither side is one and the symmetry costs nothing.
            Raise(pair.A, pair.B, TouchEdge.Start);
            Raise(pair.B, pair.A, TouchEdge.Start);
            edges += 2;

            // AND THE LEVEL, on the same frame as the edge. The native shell
            // did exactly this -- OnManagedStartTouch, then OnManagedTouch if
            // the class asked for every tick -- and an item that only heard the
            // arrival would be a pickup that cannot be collected by somebody
            // who was already standing on it.
            edges += Stay(pair.A, pair.B);
        }

        edges += SweepDepartures(frame);

        TouchEdgesLastFrame = edges;
        TouchEdgesTotal += edges;
        return edges;
    }

    /// <summary>
    /// Everything the report did not mention this frame, reported as gone.
    ///
    /// <para>
    /// KEYS COLLECTED FIRST, then removed, then raised. Raising an edge runs
    /// game code, and game code removes entities — which comes back here
    /// through <see cref="ForgetTouches"/> and writes to the very dictionary a
    /// live enumeration would be walking.
    /// </para>
    /// </summary>
    private int SweepDepartures(int frame)
    {
        _expired.Clear();

        foreach (var pair in _touching)
        {
            if (pair.Value.Frame != frame)
                _expired.Add(pair.Key);
        }

        int edges = 0;

        foreach (ulong key in _expired)
        {
            // TryGetValue and not the indexer: an edge raised earlier in this
            // loop may already have taken this link away with the entity it
            // belonged to.
            if (!_touching.TryGetValue(key, out var link))
                continue;

            _touching.Remove(key);

            Raise(link.A, link.B, TouchEdge.End);
            Raise(link.B, link.A, TouchEdge.End);
            edges += 2;
        }

        return edges;
    }

    /// <summary>
    /// The level, for whichever of the two asked to hear it every frame.
    ///
    /// <para>
    /// <b><c>bTouchEveryTick</c> IS A MANAGED PER-CLASS CHOICE NOW.</b> It was
    /// a field on the native class descriptor, filled in from the entity's KIND
    /// in C++ — an item was told every tick and everything else was told only
    /// about the edges, for every entity in the mod and with no way for a class
    /// to say otherwise. It is <see cref="ScriptedEntity.TouchEveryTick"/>, and
    /// the default reproduces exactly what the C++ table said so that nothing
    /// moved when the decision did.
    /// </para>
    ///
    /// <para>
    /// A handle this directory cannot resolve gets NO level. That is not the
    /// "deliver when the guard cannot be evaluated" case: the guard can be
    /// evaluated, and its answer is that there is no managed class here to have
    /// an opinion — a native entity, a player on the SDK hierarchy, an entity
    /// removed earlier in this same pass. The arrival and the departure are
    /// still raised for it, because those are facts about the PAIR.
    /// </para>
    /// </summary>
    private int Stay(EntityHandle a, EntityHandle b)
    {
        int edges = 0;

        if (Resolve(a)?.TouchEveryTick == true)
        {
            Raise(a, b, TouchEdge.Stay);
            edges++;
        }

        if (Resolve(b)?.TouchEveryTick == true)
        {
            Raise(b, a, TouchEdge.Stay);
            edges++;
        }

        return edges;
    }

    private void Raise(EntityHandle receiver, EntityHandle toucher, TouchEdge edge)
        => TouchRouting?.Invoke(receiver, toucher, edge);

    /// <summary>
    /// An entity is going: every link it is in goes with it, and the OTHER end
    /// is told.
    ///
    /// <para>
    /// <b>THE SURVIVOR GETS ITS <c>End</c>, and that is stock's rule rather
    /// than a kindness.</b> <c>PhysicsLink_RemoveTouchedList</c> —
    /// <c>physics_main_shared.cpp</c>, reached from
    /// <c>CBaseEntity::UpdateOnRemove</c> — walks the dying entity's links and
    /// calls <c>PhysicsLink_NotifyOtherOfUntouch</c> on each, and
    /// <c>PhysicsLink_RemoveToucher</c>'s own comment states the invariant:
    /// "Every start Touch gets a corresponding end touch". A trigger that
    /// counts what is inside it leaks one for every entity removed in its
    /// volume otherwise, and the count is what fires <c>OnEndTouchAll</c>.
    /// </para>
    ///
    /// <para>
    /// The DYING end is told nothing. There is nothing left to tell: its
    /// managed instance is being unbound in the same breath, and the native
    /// shell's own sweep dropped a link whose other end no longer resolved
    /// without a report for the same reason.
    /// </para>
    /// </summary>
    private void ForgetTouches(EntityHandle handle)
    {
        if (_touching.Count == 0)
            return;

        // A LIST OF ITS OWN rather than the sweep's scratch, and re-entrancy is
        // why. This is reached from Unbind, Unbind is reached from a removal,
        // and a removal is one of the things an End edge raised by
        // SweepDepartures does -- so a shared buffer would be cleared out from
        // under the loop that is walking it. Allocated only when an entity that
        // was touching something goes, which is a removal and not a frame.
        List<ulong>? going = null;

        foreach (var pair in _touching)
        {
            if (!pair.Value.Names(handle))
                continue;

            going ??= new List<ulong>();
            going.Add(pair.Key);
        }

        if (going is null)
            return;

        foreach (ulong key in going)
        {
            if (!_touching.TryGetValue(key, out var link))
                continue;

            _touching.Remove(key);

            Raise(link.Other(handle), handle, TouchEdge.End);
            TouchEdgesLastFrame++;
            TouchEdgesTotal++;
        }
    }

    //-------------------------------------------------------------------------
    // Resolve, Entities, Bindings, Snapshot, HandleOf, OfClass, OfName and
    // ByIndex are EntityDirectoryCore<TEntity>'s -- lane sub-entitylist. Only
    // ByIndex answers differently here, and it answers null, because this side
    // keeps no index table: see the core's TracksEntityIndex.
    //-------------------------------------------------------------------------

    /// <summary>Drops every binding and every booking. A level change.</summary>
    public void Clear()
    {
        // THE INVISIBLE ENTRIES, AND OVER THE WHOLE RANGE RATHER THAN OVER THE
        // DENSE LIST -- step 2e. This walk deliberately does not call Unbind,
        // which is what releases an entry on every other path; and an entry can
        // hold state without being bound at all (a class that failed to build),
        // so a walk of the bound entries would leak exactly those. Without it a
        // level change leaks every shell-less entry the map used and the next
        // map starts that much closer to a full range.
        for (int entry = FirstInvisibleEntry; entry < EntryCount; entry++)
            ReleaseInvisible(entry);

        // THE STORAGE, THE DENSE LIST AND THE THREE INDEXES -- the core's
        // silent form, because this side has no listeners to tell. The client's
        // Clear unbinds each entry instead, and its own doc says why.
        ClearCore();

        Think.Clear();

        // The removal marks and the queue, for Think.Clear()'s reason -- step
        // 2c. A handle still queued across a level change would be freed on the
        // next map's first frame, and by then it names whatever took the entry.
        Array.Fill(_removing, EntityHandle.None);
        _pendingRemoval.Clear();

        // The I/O half's three tables, for the reason Think.Clear() is here: a
        // queued event, a connection or a name that outlived a level change
        // would be answered by whatever took the handle in the next map.
        Io.Clear();
        _connections.Clear();
        // The two name tables went with ClearCore() above, which empties every
        // index this directory keeps.

        // The reported-pairs set goes with the names it is keyed on. A pair
        // reported on the last map must be reportable again on this one:
        // "nothing answers to door_1" is a different fact about a different
        // map, and remembering it across the change is how the second map's
        // copy of the defect stays silent. IoUnhandled is a LIFETIME total and
        // deliberately does not reset with it.
        _unhandledSeen.Clear();

        // And the touch table -- step 2b. A link that outlived a level change
        // would be swept on the next map's first frame and report a departure
        // for two entities that no longer exist. Cleared rather than swept: a
        // level change is not something anything is told it has left.
        _touching.Clear();
        _expired.Clear();
        _touchFrame = 0;
    }

    /// <summary>
    /// THE SERVER FRAME'S ONE THINK PASS — <c>plan_entity.md</c> §3.4 and step
    /// 1g. Runs every think due at or before <paramref name="curTime"/>, in
    /// (time, booking) order.
    ///
    /// <para>
    /// <b>THIS IS WHERE THE CROSSING BUDGET IS PAID BACK.</b> It runs inside the
    /// one <c>GameFrame</c> crossing the frame already costs, so the number of
    /// managed↔native transitions a frame spends on think is the same whether
    /// one entity is thinking or four hundred. Before this, the native frame
    /// walked the shells and each due one crossed on its own.
    /// </para>
    ///
    /// <para>
    /// A think whose entity has gone since it was booked is skipped, not
    /// dispatched to a null: <see cref="Unbind"/> cancels on removal, and this
    /// re-resolves anyway because an entity removed by an EARLIER think in the
    /// same pass was already in the snapshot.
    /// </para>
    ///
    /// <para>Answers how many thinks ran.</para>
    /// </summary>
    public int RunFrame(float curTime)
    {
        // THE THINKS, AND ONLY THE THINKS -- the sub-ioqueue lane.
        //
        // Step 2a serviced the I/O queue here as well, one line below the
        // thinks, and argued the placement from stock's order:
        // CServerGameDLL::GameFrame runs Physics_RunThinkFunctions and then
        // ServiceEventQueue. Both halves of that are true and the conclusion
        // was still wrong, because THIS METHOD IS NOT WHERE STOCK'S THINKS
        // RUN. It is a row of ServerSystemsPostThink, and stock's
        // ServiceEventQueue is a SEPARATE STEP OF GameFrame that comes after
        // every one of those rows.
        //
        // The cost of the difference was measured: the managed queue was
        // serviced there and the NATIVE one at stock's position immediately
        // after, so an input the native pass delivered fired a managed output
        // whose event arrived AFTER the managed pass had finished and waited a
        // whole frame -- which stock cannot do, because ServiceEvents re-reads
        // its head. EntityIoQueue.PostedAfterThePass is the counter for it.
        //
        // The pass is now driven from ServerGameDll.GameFrame, at stock's
        // position, through EntityIoRoutes.ServiceQueue. It costs NO extra
        // crossing: GameFrame is itself inside the one ServerGameFrame
        // crossing, exactly as this method is.
        return RunThinks(curTime);
    }

    /// <summary>The think half of <see cref="RunFrame"/>, split out so the I/O half reads.</summary>
    private int RunThinks(float curTime)
    {
        int taken = Think.TakeDue(curTime, _due);
        if (taken == 0)
        {
            ThinksRanLastFrame = 0;
            return 0;
        }

        int ran = 0;

        for (int i = 0; i < _due.Count; i++)
        {
            var entity = Resolve(_due[i].Entity);
            if (entity is null)
                continue;

            entity.RunThink(_due[i].Context);
            ran++;
        }

        ThinksRanLastFrame = ran;
        ThinksRunTotal += ran;

        return ran;
    }

    //=========================================================================
    // THE I/O HALF -- plan_entity.md step 2a, and §3.5's "the I/O queue:
    // outputs are fields holding connection lists parsed from the map, delays
    // are queued here, and !activator, !caller, !self, !player resolve here".
    //
    // A REGION OF ITS OWN, because EntityDirectory is shared with step 2b (the
    // touch link table) and step 2c (deferred removal), which land after this.
    // Everything 2a adds is between this banner and the end of the class; the
    // only lines it added OUTSIDE the region are one call in Bind's eviction
    // arm, one in Unbind, four in Clear and one in RunFrame, each with a
    // comment naming the step. 2b and 2c should open their own banners rather
    // than interleave.
    //=========================================================================

    /// <summary>How an event reaches an entity. Set by the side module that owns this directory.</summary>
    public EntityInputDelivery? InputDelivery { get; set; }

    /// <summary>
    /// THE SDK HIERARCHY'S HALF OF ONE DELIVERY — the sub-ioqueue lane.
    ///
    /// <para>
    /// The managed directory holds every managed entity, shelled and
    /// shell-less. It does NOT hold the SDK's own: <c>CEntityDissolve</c>,
    /// <c>CRagdollProp</c>, <c>CRagdollBoogie</c>,
    /// <c>CPhysicsEntitySolver</c> — the classes the compiled stock TUs still
    /// construct at runtime. One queue means one place those walks can still be
    /// asked from, and this is it.
    /// </para>
    ///
    /// <para>
    /// <b>Asked ONLY when the managed directory answered nothing</b>, so it
    /// cannot deliver an input twice. Answers how many entities took the
    /// input, and a null route answers zero — which is what the shipped game
    /// has, since nothing binds it yet.
    /// </para>
    ///
    /// <para>
    /// This directory's own (plan_abi_hotreload D7): it was the static
    /// <c>EntityIoRoutes.SdkDelivery</c>.
    /// </para>
    /// </summary>
    public SdkDeliveryRoute? SdkDelivery { get; set; }

    /// <inheritdoc cref="SdkDelivery"/>
    public delegate int SdkDeliveryRoute(string target, string input, string? value,
                                         EntityHandle activator);

    /// <summary>
    /// Who <c>!player</c> is. <c>UTIL_PlayerByIndex(1)</c>, which is what
    /// <c>CGlobalEntityList::FindEntityProcedural</c> answers for it and for
    /// <c>!pvsplayer</c>. A delegate for the same reason as
    /// <see cref="InputDelivery"/>: the directory has no idea what a player is.
    /// </summary>
    public Func<EntityHandle>? PlayerProvider { get; set; }

    /// <summary>The event queue this directory runs.</summary>
    public EntityIoQueue Io { get; }

    /// <summary>Every entity's connections, by handle entry.</summary>
    private readonly Dictionary<EntityHandle, EntityConnections> _connections = new();

    /// <summary>Scratch for <see cref="Deliver"/>, so a delivery allocates nothing per event.</summary>
    private readonly List<EntityHandle> _targets = new();

    /// <summary>How many events the last <see cref="RunFrame"/> delivered.</summary>
    public int IoDeliveredLastFrame => Io.DeliveredLastPass;

    /// <summary>How many events found no target at all, over this directory's life.</summary>
    public long IoUnhandled { get; private set; }

    /// <summary>
    /// HOW MANY TIMES THE SDK WALK WAS ASKED, and how many of those it took —
    /// the sub-ioqueue lane's crossing instrument.
    ///
    /// <para>
    /// <see cref="SdkDelivery"/> is an OUTBOUND crossing, and
    /// it is the only one this lane added. It is not a per-frame cost: it is
    /// asked once per event that the managed directory could not place, so an
    /// idle server and a frame in which every input found a managed target both
    /// read zero. <c>Crossings</c> counts inbound ops and cannot see it, which
    /// is why it is counted here rather than left to be inferred from
    /// <see cref="IoUnhandled"/> — that number is the asks that came back
    /// EMPTY, and the difference is what the SDK hierarchy took.
    /// </para>
    /// </summary>
    public long IoSdkAsks { get; private set; }

    /// <inheritdoc cref="IoSdkAsks"/>
    public long IoSdkTook { get; private set; }

    /// <summary>
    /// How many DISTINCT (target, input) pairs have found no target at all.
    ///
    /// <para>
    /// The number that matters, and the reason the reporting rule below is not
    /// "the first eight events". A map with one dead connection on a repeating
    /// relay produces thousands of unhandled events and EIGHT of them are the
    /// same pair; under a first-eight rule a genuinely new misdelivery arriving
    /// afterwards is silent. Counting and reporting per PAIR means the first
    /// time anything new goes nowhere, it says so.
    /// </para>
    /// </summary>
    public int IoUnhandledDistinct => _unhandledSeen.Count;

    /// <summary>
    /// The (target, input) pairs already reported. See
    /// <see cref="IoUnhandledDistinct"/>.
    /// </summary>
    private readonly HashSet<string> _unhandledSeen =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The most pairs <see cref="_unhandledSeen"/> will hold.
    ///
    /// <para>
    /// A bound, not a reporting budget: the pairs come from map data and from a
    /// console, both finite, but a script firing at a generated name could
    /// invent one per event and the set is the only thing that would grow. Past
    /// the cap the counter still counts and the message stops, which is the same
    /// trade the old first-eight rule made — and at a number no map reaches.
    /// </para>
    /// </summary>
    private const int UnhandledPairsRemembered = 256;

    //-------------------------------------------------------------------------
    // Names and classnames -- BOTH INDEXES ARE
    // EntityDirectoryCore<TEntity>'s NOW (lane sub-entitylist).
    //
    // SetName, NameOf, NamedCount, FindByName, OfName, FindByClassName,
    // OfClass and ClassNames are inherited, along with the two dictionaries
    // behind them. Their bodies moved verbatim except for FindByClassName --
    // see below.
    //
    // WHY THE NAME INDEX MATTERS AT ALL, kept here because it is the reason a
    // per-class elision would be a bug: THIS IS WHY ENTITY_OP_KEY_VALUE CANNOT
    // BE ELIDED PER CLASS. The index an output resolves its target against is
    // fed by SetName, and SetName is fed by the `targetname` keyvalue. A class
    // whose KeyValue crossing were switched off by an override bit would have
    // no name in that table, so no connection in any map could ever reach it --
    // and nothing would report it, because "no entity answers to that name" is
    // exactly what a map with a typo looks like.
    //
    // FindByClassName GAINED AN INDEX AND KEPT ITS ANSWER. This side used to
    // walk the dense list for every query. The core answers an EXACT query from
    // the classname bucket, whose comparer is OrdinalIgnoreCase -- which is
    // precisely the case-insensitive equality EntityNameMatch.Match performs on
    // a query with no '*', so the two select the same entities. A WILDCARD
    // query still walks the dense list, deliberately: Match says a query
    // beginning '*' matches an entity with NO classname at all
    // (EntityIo.cs:316), and an entity with no classname is in no bucket, so a
    // bucket walk would quietly drop it. The client's OfClass walked buckets
    // for '*' and therefore did drop it; the fold fixes that side rather than
    // spreading it to this one.
    //
    // The ORDER of the exact arm is the bucket's (bind order within a
    // classname) rather than the dense list's. Both were undefined before --
    // the dense list reorders on every unbind, because a removal swaps with the
    // tail -- and no caller of either has ever asked for one.
    //-------------------------------------------------------------------------

    //-------------------------------------------------------------------------
    // Connections.
    //-------------------------------------------------------------------------

    /// <summary>The connections an entity carries, created on first use.</summary>
    public EntityConnections ConnectionsOf(EntityHandle handle)
    {
        if (!_connections.TryGetValue(handle, out var connections))
        {
            connections = new EntityConnections();
            _connections[handle] = connections;
        }

        return connections;
    }

    /// <summary>The connections an entity carries, or null if it has none.</summary>
    public EntityConnections? PeekConnections(EntityHandle handle)
        => _connections.TryGetValue(handle, out var connections) ? connections : null;

    /// <summary>
    /// Parses one map connection and hangs it on an output —
    /// <c>CBaseEntityOutput::ParseEventAction</c>, from managed keyvalue
    /// handling.
    /// </summary>
    public EventAction Connect(EntityHandle handle, string outputName, string actionData)
    {
        var action = EventAction.Parse(actionData, _stamps);
        ConnectionsOf(handle).Add(outputName, action);
        return action;
    }

    /// <summary>How many connections are hung on this directory in total.</summary>
    public int ConnectionCount
    {
        get
        {
            int total = 0;

            foreach (var connections in _connections.Values)
                total += connections.Count;

            return total;
        }
    }

    /// <summary>
    /// Fires one entity's output: posts an event for every connection on it.
    ///
    /// <para>
    /// The CALLER is the entity whose output this is, always. The ACTIVATOR is
    /// whoever caused the chain, and an invalid one means "nobody named it", in
    /// which case the caller is used — which is what the SDK writes as
    /// <c>FireOutput( this, this )</c> and what <c>FireNamedOutput</c>'s own
    /// note in the shell said it was doing.
    /// </para>
    /// </summary>
    /// <returns>How many events were posted.</returns>
    public int FireOutput(EntityHandle handle, string outputName, string? value,
                          EntityHandle activator, float curTime, float extraDelay = 0f)
    {
        var connections = PeekConnections(handle);

        if (connections is null)
            return 0;

        EntityHandle cause = activator.Value != uint.MaxValue ? activator : handle;

        return connections.Fire(Io, outputName, curTime, value, cause, handle, extraDelay);
    }

    //-------------------------------------------------------------------------
    // The queue.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Posts one input at a target by name, with a delay — what <c>ent_fire</c>
    /// does, and what managed code that wants to fire an input asks for.
    /// </summary>
    public void FireInput(string target, string input, string? value, float curTime,
                          float delay = 0f, EntityHandle activator = default,
                          EntityHandle caller = default)
    {
        Io.Add(curTime + delay, target, EntityHandle.None, input, value,
               activator, caller, 0);
    }

    /// <summary>Posts one input at ONE entity, ignoring names. Stock's direct-pointer event.</summary>
    public void FireInputAt(EntityHandle target, string input, string? value, float curTime,
                            float delay = 0f, EntityHandle activator = default,
                            EntityHandle caller = default)
    {
        Io.Add(curTime + delay, string.Empty, target, input, value,
               activator, caller, 0);
    }

    /// <summary>
    /// Runs the queue for this frame. Split out of <see cref="RunFrame"/> so a
    /// test can drive it without a think heap.
    /// </summary>
    public int ServiceIo(float curTime) => Io.Service(curTime, Deliver);

    /// <summary>
    /// Delivers one due event, resolving its target the way
    /// <c>CEventQueue::ServiceEvents</c> does: the direct target if it has one,
    /// then the name, then — only if the name found nothing — the classname.
    /// </summary>
    private void Deliver(EntityIoEvent e)
    {
        _targets.Clear();

        if (e.DirectTarget.Value != uint.MaxValue)
        {
            if (Resolve(e.DirectTarget) is not null)
                _targets.Add(e.DirectTarget);
        }
        else
        {
            switch (EntityIoTargets.Classify(e.Target))
            {
                case EntityIoTarget.ByName:
                    // The name first and the classname only as a fallback, which
                    // is stock's order and its `targetFound` flag. Doing both
                    // would deliver twice to an entity whose targetname and
                    // classname are the same string.
                    if (FindByName(e.Target, _targets) == 0)
                        FindByClassName(e.Target, _targets);
                    break;

                case EntityIoTarget.Activator:
                    AddIfLive(e.Activator);
                    break;

                case EntityIoTarget.Caller:
                    AddIfLive(e.Caller);
                    break;

                case EntityIoTarget.Self:
                    // The SEARCHING entity, which for an event posted by an
                    // output is the entity whose output it was.
                    AddIfLive(e.Caller);
                    break;

                case EntityIoTarget.Player:
                    AddIfLive(PlayerProvider?.Invoke() ?? EntityHandle.None);
                    break;

                default:
                    _log.Warning($"[SourceSharp] invalid entity search name {e.Target}\n");
                    break;
            }
        }

        string value = e.Value ?? string.Empty;

        if (_targets.Count == 0)
        {
            // THE SDK HIERARCHY, LAST — the sub-ioqueue lane. Its walks used to
            // run inside CEventQueue::ServiceEvents, which is not the queue any
            // more; this is where they are asked from now. Only reached when
            // the managed directory found nothing, so nothing can be delivered
            // twice, and only for a name/classname target: a procedural name
            // (!activator and friends) resolves against managed handles and has
            // no SDK meaning here.
            if (e.DirectTarget.Value == uint.MaxValue &&
                EntityIoTargets.Classify(e.Target) == EntityIoTarget.ByName)
            {
                IoSdkAsks++;

                int took = SdkDelivery is null ? 0
                         : SdkDelivery(e.Target, e.TargetInput, value, e.Activator);

                if (took > 0)
                {
                    IoSdkTook += took;
                    return;
                }
            }

            IoUnhandled++;

            // NOTHING TOOK IT, AND THAT IS SAID OUT LOUD. This is the branch
            // status/entlist-server.md defect 3 lived in: inputs queued,
            // serviced and delivered to NOBODY while `make check` stayed green,
            // because stock reports it with DevMsg( 2, ... ) — silent unless
            // somebody raised `developer` — and ent_fire's own "nothing is
            // called that" line is a message rather than a refusal. A build
            // whose entity list had stopped answering looked exactly like a
            // working one from a console.
            //
            // Warning, not Msg: this is a refusal, and plan_entity.md §7 says a
            // refusal is declared and PRINTED.
            //
            // ONE MESSAGE PER (target, input) PAIR, not per event and not the
            // first eight events. A firehose is how a real message gets ignored
            // and a first-eight budget is how a NEW failure gets hidden behind
            // an old one; per pair, the first time anything goes nowhere it
            // says so, and a repeating relay says it once.
            string pair = e.Target + "\u0000" + e.TargetInput;

            if (_unhandledSeen.Count < UnhandledPairsRemembered && _unhandledSeen.Add(pair))
            {
                _log.Warning($"[SourceSharp] unhandled input: ({e.TargetInput}) -> " +
                               $"({e.Target}), from ('{NameOf(e.Caller)}'); target entity not " +
                               $"found — nothing took it (distinct #{_unhandledSeen.Count}, " +
                               $"{IoUnhandled} unhandled in all)\n");
            }

            return;
        }

        // ONCE, HERE, rather than per target. A map sends "3" as a STRING and
        // variant_t::Float() answers zero for one, which is the bug
        // OnManagedInput's own note records: a counter told to add three added
        // zero and said so to nobody.
        float number = EventAction.ParseFloat(value);

        for (int i = 0; i < _targets.Count; i++)
            InputDelivery?.Invoke(_targets[i], e.TargetInput, value, number, e.Activator, e.Caller);
    }

    private void AddIfLive(EntityHandle handle)
    {
        if (handle.Value != uint.MaxValue && Resolve(handle) is not null)
            _targets.Add(handle);
    }

    /// <summary>
    /// Drops everything the I/O half holds for one entity: its queued events,
    /// its connections and its name.
    /// </summary>
    private void ForgetIo(EntityHandle handle)
    {
        Io.CancelFrom(handle);
        _connections.Remove(handle);

        // The name index is the core's now -- DropName is DropNameIndex and the
        // _names.Remove that used to follow it, in one call.
        DropName(handle);
    }

    //-------------------------------------------------------------------------
    // The reload record.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The whole I/O half as records that outlive this assembly: every pending
    /// event, and every connection that is still on an entity.
    ///
    /// <para>
    /// <b>THIS IS STEP 2a's GATE.</b> Before it the queue was
    /// <c>g_EventQueue</c> and the connections were <c>CEventAction</c> lists,
    /// both native, and <see cref="Output.Restore"/> said so in as many words:
    /// "that list — including how many fires a one-shot has left — is native
    /// memory a managed reload does not reach". A delayed output fired before a
    /// reload and due after it was delivered by code that no longer existed,
    /// with the wrong idea of what its target's input meant.
    /// </para>
    /// </summary>
    public SavedIo SaveIo(float curTime)
    {
        var connections = new List<SavedConnection>();

        foreach (var (handle, list) in _connections)
        {
            foreach (var (outputName, actions) in list.All)
            {
                foreach (var action in actions)
                {
                    connections.Add(new SavedConnection(handle.Value, outputName, action.Target,
                                                        action.TargetInput, action.Parameter,
                                                        action.Delay, action.TimesToFire,
                                                        action.IdStamp));
                }
            }
        }

        return new SavedIo(Io.Save(curTime), connections);
    }

    /// <summary>
    /// Puts a saved I/O half back, rebasing every delay on the clock as it is
    /// now. Answers how many events and how many connections were restored.
    /// </summary>
    public (int Events, int Connections) RestoreIo(SavedIo saved, float curTime)
    {
        if (saved is null)
            return (0, 0);

        int events = Io.Restore(saved.Events, curTime);

        foreach (var c in saved.Connections)
        {
            var handle = new EntityHandle(c.Owner);

            ConnectionsOf(handle).Add(c.OutputName,
                new EventAction(c.Target, c.TargetInput, c.Parameter, c.Delay,
                                c.TimesToFire, c.IdStamp));
        }

        return (events, saved.Connections.Count);
    }

    //=========================================================================
    // THE REMOVAL HALF -- plan_entity.md step 2c, and §3.5's "removal:
    // Remove(handle) marks; the directory frees at end of frame, as UTIL_Remove
    // does, so a handle stays valid for the rest of the frame".
    //
    // A REGION OF ITS OWN, for the reason the I/O banner above gives: this file
    // is shared with steps 2a (I/O) and 2b (touch links). Everything 2c adds is
    // between this banner and the end of the class. The lines it added OUTSIDE
    // the region are FOUR, each with a comment naming the step: the fill in the
    // constructor, one block in Unbind, one block in Clear, and the three
    // fields beside the think scratch.
    //
    // WHAT THIS REPLACES. CStandaloneEntity::Remove() appended `this` to a
    // native CUtlVector and StandaloneEntities_Finish drained it after the
    // frame's thinks. The list is here now; what is left native is the delete
    // itself, which is mechanism -- a shell is a native object and only native
    // code can free one -- and it costs ONE crossing for the whole frame's
    // removals rather than one per entity (§3.4).
    //=========================================================================

    /// <summary>
    /// The shells behind a whole frame's removals, freed — the ONE crossing
    /// this half costs. Set by the side module that owns this directory.
    /// </summary>
    /// <param name="batch">
    /// The handles that were marked, in mark order, already unbound from this
    /// directory. Not retained by the callee.
    /// </param>
    public delegate void ShellBatchDestroyer(ReadOnlySpan<EntityHandle> batch);

    /// <summary>
    /// FIRST: the links. An entity that is going takes its children with it —
    /// <c>CBaseEntity::UpdateOnRemove</c>'s behaviour, and what
    /// <c>~CStandaloneEntity</c> reached <c>StandaloneHierarchy_EntityRemoved</c>
    /// for before this step.
    ///
    /// <para>
    /// It runs while every handle in the batch still resolves, because that is
    /// what a detach needs to read, and the children it marks are picked up by
    /// the next pass of the drain — <b>inside the same frame</b>, which is step
    /// 2h's gate ("removal of a parent detaches children in one frame") kept.
    /// </para>
    /// </summary>
    public Action<EntityHandle>? RemovalDetach { get; set; }

    /// <summary>
    /// SECOND: the entity's own last look — <c>OnManagedRemoved</c>'s crossing,
    /// which is a managed call now.
    ///
    /// <para>
    /// Ordered after <see cref="RemovalDetach"/> because that is the order the
    /// native destructor kept, and the reason is readable: a class's teardown is
    /// entitled to ask what was hanging off it, and the answer has to be the
    /// same one the detach acted on.
    /// </para>
    /// </summary>
    public Action<EntityHandle>? RemovalNotify { get; set; }

    /// <summary>
    /// SECOND AND A HALF: what this side remembers about the entity beyond the
    /// directory goes -- the managed skeleton (plan physics_bones_native_zero
    /// step 1.2), which was CStandaloneEntity's own members and went with its
    /// destructor. After <see cref="RemovalNotify"/>, so a class's last look
    /// can still ask where its attachments are.
    /// </summary>
    public Action<EntityHandle>? RemovalForget { get; set; }

    /// <summary>THIRD: the shells. <see cref="ShellBatchDestroyer"/>.</summary>
    public ShellBatchDestroyer? ShellDestroyer { get; set; }

    /// <summary>How many entities the last <see cref="FreeRemoved"/> freed.</summary>
    /// <remarks>
    /// INSTRUMENTATION, and named as debt for the same reason
    /// <see cref="ThinksRanLastFrame"/> is: "removal costs one crossing a frame
    /// now" is trivially true of a build that stopped removing anything.
    /// </remarks>
    public int RemovedLastFrame { get; private set; }

    /// <inheritdoc cref="RemovedLastFrame"/>
    public long RemovedTotal { get; private set; }

    /// <summary>How many marks are waiting for the end of the frame.</summary>
    public int PendingRemovalCount => _pendingRemoval.Count;

    /// <summary>
    /// How many times <see cref="FreeRemoved"/> will let one call's cascade go
    /// round before it gives up and says so.
    ///
    /// <para>
    /// A removal can queue removals — a parent takes its children — so the
    /// drain is a loop and not a pass. The loop terminates on its own because
    /// every pass unbinds what it freed and an unbound handle can never be
    /// marked again; this is the guard for the case where that reasoning is
    /// WRONG, and it prints rather than spinning the server frame for ever.
    /// Deeper than any hierarchy a map can build: §3.5's cycle refusal already
    /// bounds parenting depth, and dm_lockdown's deepest chain is 2.
    /// </para>
    /// </summary>
    public const int MaxRemovalPasses = 64;

    /// <summary>
    /// MARKS <paramref name="handle"/> FOR REMOVAL. It is freed at the end of
    /// the frame, by <see cref="FreeRemoved"/>, and until then it resolves
    /// exactly as it did before.
    ///
    /// <para>
    /// <b>DEFERRED, ALWAYS, AND THAT IS THE WHOLE SAFETY PROPERTY.</b> Freeing
    /// an entity inside its own damage handler or its own think frees it while
    /// the caller still holds it — the bullet path is still holding the trace
    /// that names it. <c>UTIL_Remove</c> exists for exactly this reason and this
    /// is its managed counterpart.
    /// </para>
    ///
    /// <para>
    /// <b>THE MARK IS A HANDLE TABLE AND NOT A <c>bool[]</c>, and that is not
    /// tidiness.</b> The entry a mark sits in can be rebound in the same frame —
    /// the engine bumps the serial and hands the entry to something else — and a
    /// bare bit would then say "the thing in entry 12 is going" about an entity
    /// that has only just arrived. Storing the whole handle makes the mark fail
    /// closed against the serial, for free, the same way
    /// <see cref="Resolve"/> does. The empty value is
    /// <see cref="EntityHandle.None"/> (which a zeroed table also reads as,
    /// since 2026-10-05) and never entry 0, which is a real, bindable slot.
    /// </para>
    ///
    /// <para>
    /// <b>A SECOND CALL IS IDEMPOTENT</b> and says so:
    /// <see cref="RemovalMark.AlreadyMarked"/> queues nothing, so the entity is
    /// freed once and appears in one batch. See <see cref="RemovalMark"/> for
    /// why that is a distinct answer rather than a <c>false</c>.
    /// </para>
    /// </summary>
    public RemovalMark Remove(EntityHandle handle)
    {
        if (handle.Value == uint.MaxValue)
            return RemovalMark.NotMine;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount)
            return RemovalMark.NotMine;

        // The whole handle, so a stale serial names nobody -- Resolve's rule.
        if (HandleOf(entry) != handle)
            return RemovalMark.NotMine;

        if (_removing[entry] == handle)
            return RemovalMark.AlreadyMarked;

        _removing[entry] = handle;
        _pendingRemoval.Add(handle);
        return RemovalMark.Marked;
    }

    /// <summary>
    /// Whether <paramref name="handle"/> is marked and waiting for the end of
    /// the frame. False for a handle this directory does not hold, and false for
    /// a stale handle whose entry has been rebound.
    /// </summary>
    public bool IsRemoved(EntityHandle handle)
    {
        if (handle.Value == uint.MaxValue)
            return false;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount)
            return false;

        return _removing[entry] == handle;
    }

    /// <summary>
    /// Marks every bound entity. Answers how many marks it made.
    ///
    /// <para>
    /// <b>THE LEVEL SHUTDOWN, AND IT IS THIS STEP'S LIVE GATE.</b> The native
    /// teardown deleted every shell in its own vector; that walk still runs, as
    /// belt and braces, but it can no longer be the thing that tells managed
    /// code an entity has gone — the destructor stopped announcing it when the
    /// announcement became a managed call. So a level shutdown goes through the
    /// SAME path as any other removal: mark everything, then free it. The live
    /// gate's "clean shutdown with every entity destroyed" step is the check,
    /// and a world that outlived its level reads there as a census that will not
    /// go to zero.
    /// </para>
    /// </summary>
    public int RemoveAll()
    {
        int marked = 0;

        // By index over the dense list, backwards: Remove() marks and does not
        // unbind, so the list does not move under this walk -- but a backward
        // walk costs nothing and is what the next reader will expect beside
        // Clear()'s.
        for (int i = DenseEntries.Count - 1; i >= 0; i--)
        {
            if (Remove(HandleOf(DenseEntries[i])) == RemovalMark.Marked)
                marked++;
        }

        return marked;
    }

    /// <summary>
    /// THE END OF THE FRAME: every marked entity is told, unbound and freed, in
    /// batches of one crossing each. Answers how many were freed.
    ///
    /// <para>
    /// <b>WHERE IT RUNS IS A DECISION, and step 1g's frame split exists for
    /// it.</b> After the thinks, because an entity that decides to die inside
    /// its own think must not be freed under the caller; after the parenting
    /// sweep, so a child is not composed against a parent that is already gone;
    /// and before the frame ends, because a handle must not survive into the
    /// next frame. <c>SourceSharp.Server.StandaloneHierarchy.OnThink</c> is
    /// where those three meet, and it is exactly where
    /// <c>StandaloneEntities_Finish</c> drained the native list.
    /// </para>
    ///
    /// <para>
    /// <b>A LOOP AND NOT A PASS</b>, for the reason the native drain took the
    /// tail each time: removing a parent removes its children, so servicing the
    /// batch can lengthen the queue. Each round is its own batch and its own
    /// crossing; the whole cascade still resolves inside this frame.
    /// </para>
    /// </summary>
    public int FreeRemoved()
    {
        int freed = 0;
        int passes = 0;

        while (_pendingRemoval.Count > 0)
        {
            if (++passes > MaxRemovalPasses)
            {
                // PRINTED, never a silent drop (§7). The queue is cleared
                // because leaving it standing would spin the next frame too.
                _log.Warning($"[SourceSharp] entity removal did not settle in {MaxRemovalPasses} " +
                            $"passes; {_pendingRemoval.Count} mark(s) dropped\n");

                foreach (var stuck in _pendingRemoval)
                    ClearMark(stuck);

                _pendingRemoval.Clear();
                break;
            }

            _batch.Clear();
            _batch.AddRange(_pendingRemoval);
            _pendingRemoval.Clear();

            // 1. THE LINKS, while every handle in the batch still resolves.
            if (RemovalDetach is not null)
            {
                for (int i = 0; i < _batch.Count; i++)
                    RemovalDetach(_batch[i]);
            }

            // 2. THE ENTITY'S OWN LAST LOOK, still with everything in the batch
            //    resolving: the unbinds are a separate walk BELOW rather than
            //    interleaved here, so that what one entity's teardown can see
            //    does not depend on where it sits in the batch.
            if (RemovalNotify is not null)
            {
                for (int i = 0; i < _batch.Count; i++)
                    RemovalNotify(_batch[i]);
            }

            // 2b. WHAT THE SIDE REMEMBERS OF IT, last of the reads.
            if (RemovalForget is not null)
            {
                for (int i = 0; i < _batch.Count; i++)
                    RemovalForget(_batch[i]);
            }

            // 3. THE BINDINGS. Which of them had a shell is read HERE, before
            //    the unbinds, because Unbind is what releases the invisible
            //    entry -- afterwards every handle in the batch looks shell-less
            //    and the batch handed to native would be empty. Step 2e.
            _shellBatch.Clear();

            for (int i = 0; i < _batch.Count; i++)
            {
                if (!IsShellLess(_batch[i]))
                    _shellBatch.Add(_batch[i]);

                ClearMark(_batch[i]);
                Unbind(_batch[i]);
            }

            // 4. THE SHELLS -- ONE crossing for the whole batch (§3.4), and
            //    only for the entities that HAVE one (step 2e). A batch of
            //    nothing but shell-less entities does not cross at all, which
            //    is what Engine.DestroyShells' empty-span arm already does.
            ShellDestroyer?.Invoke(CollectionsMarshal.AsSpan(_shellBatch));

            freed += _batch.Count;
            _batch.Clear();
        }

        RemovedLastFrame = freed;
        RemovedTotal += freed;
        return freed;
    }


    //=========================================================================
    // THE ENGINE-INVISIBLE RANGE -- plan_entity.md step 2e, and §3.3:
    //
    //   "Entities the engine cannot see have no shell at all. A logic_relay, a
    //    filter, a math_counter, a point_template, a phys_constraint are managed
    //    objects that exist only in the directory. They take handles from the
    //    2048-8191 range, with serials the directory owns, so an !activator or a
    //    targetname lookup works uniformly."
    //
    // Everything above this banner already treats the two halves of the handle
    // space identically -- 1c sized the slot table for the whole range from the
    // start and its gtests assert that both halves bind, resolve and go stale
    // the same way. So this region is ONLY the allocator and the state a
    // shell-less entity keeps in place of a shell's block. Bind, Unbind,
    // Resolve, the name index, the think heap, the I/O queue and the removal
    // marks all work on a shell-less handle with no change at all.
    //=========================================================================

    /// <summary>
    /// <c>MAX_EDICTS</c> — <c>public/const.h</c>: <c>1 &lt;&lt; MAX_EDICT_BITS</c>.
    /// Entries below it are edicts; entries from here to
    /// <see cref="EntryCount"/> are the engine-invisible range.
    /// </summary>
    public const int FirstInvisibleEntry = EntityHandle.FirstInvisibleEntry;

    /// <summary>
    /// The one entity <c>FindEntityByName</c> answers for <paramref name="query"/>
    /// (which may end in '*'), in the order it has always answered: an entity
    /// the engine can see (a standalone with a shell) before a shell-less one,
    /// and within each in the order that half was walked: shelled entities in
    /// creation order, shell-less ones by ascending entry.
    /// <paramref name="isShellLess"/> is the registry's per-class answer. That
    /// is the native standalone
    /// list's order (push_back at creation, erase in place) followed by the
    /// shell-less half, which Engine.FindEntityByName asked second so a name a
    /// map used twice keeps resolving to the engine-visible entity.
    /// <see cref="EntityDirectoryCore{TEntity}.FindByName"/> keeps the index's
    /// order for the callers that want every match (outputs fire at all of
    /// them); this is the single answer.
    /// </summary>
    public EntityHandle FirstByName(string? query, Func<ScriptedEntity, bool> isShellLess)
    {
        EntityHandle[] buffer = System.Buffers.ArrayPool<EntityHandle>.Shared.Rent(16);
        int count = CollectByName(query, isShellLess, ref buffer, 1);
        EntityHandle first = count > 0 ? buffer[0] : EntityHandle.None;

        System.Buffers.ArrayPool<EntityHandle>.Shared.Return(buffer);
        return first;
    }

    /// <summary>
    /// Every entity answering to <paramref name="query"/>, in
    /// <see cref="FirstByName"/>'s order, appended to <paramref name="into"/>
    /// (<see cref="CollectByName"/>'s answer as a list). Answers how many.
    /// </summary>
    public int AllByName(string? query, Func<ScriptedEntity, bool> isShellLess,
                         List<EntityHandle> into, int limit)
    {
        ArgumentNullException.ThrowIfNull(into);

        EntityHandle[] buffer = System.Buffers.ArrayPool<EntityHandle>.Shared.Rent(16);
        int count = CollectByName(query, isShellLess, ref buffer, limit);

        for (int i = 0; i < count; i++)
            into.Add(buffer[i]);

        System.Buffers.ArrayPool<EntityHandle>.Shared.Return(buffer);
        return count;
    }

    /// <summary>
    /// THE ORDER, in one place. Every entity answering to <paramref name="query"/>
    /// into a pooled <paramref name="buffer"/>: shelled entities in creation
    /// order up to <paramref name="limit"/>, then, if that left room, EVERY
    /// shell-less one by ascending entry -- Engine.FindEntitiesByName's old
    /// composition exactly (the bounded native walk, then the unbounded
    /// shell-less walk). Allocates nothing: the sort is over pooled keys.
    /// Answers how many of the buffer's entries are the answer.
    /// </summary>
    public int CollectByName(string? query, Func<ScriptedEntity, bool> isShellLess,
                             ref EntityHandle[] buffer, int limit)
    {
        ArgumentNullException.ThrowIfNull(isShellLess);

        // A PROCEDURAL NAME IS NOT A NAME. !activator, !caller, !self, !player
        // resolve against a caller, and a lookup has none: stock's
        // FindGameEntityByName refuses them at the top (entitylist.cpp:686), as
        // the native walk did. Refused here, where every name lookup passes,
        // so an entity a map literally named "!player" is never matched by
        // string.
        if (!string.IsNullOrEmpty(query) && query[0] == '!')
            return 0;

        int count = CollectNamed(query, ref buffer);

        if (count == 0)
            return 0;

        long[] keys = System.Buffers.ArrayPool<long>.Shared.Rent(count);
        int shelled = 0;

        for (int i = 0; i < count; i++)
        {
            EntityHandle handle = buffer[i];

            // The KIND, not the handle range: a server-only standalone (an
            // info_target, a math_counter) has a shell yet sits in the high,
            // engine-invisible half of the handle range, so the range cannot
            // tell the two apart. The live oracle showed it. Within the kind,
            // the order each half of the old lookup walked: the native
            // standalone list is creation order (bind order here); the
            // shell-less walk goes up the entries (ShellLessEntities).
            bool shellLess = Resolve(handle) is { } entity && isShellLess(entity);

            keys[i] = shellLess ? ShellLessKeyBase + EntryIndex(handle) : BindOrderOf(handle);

            if (!shellLess)
                shelled++;
        }

        Array.Sort(keys, buffer, 0, count);
        System.Buffers.ArrayPool<long>.Shared.Return(keys);

        return shelled >= limit ? limit : count;
    }

    // Shelled keys are bind orders (a running count); shell-less keys sit above
    // every one of them.
    private const long ShellLessKeyBase = 1L << 62;

    /// <summary>Whether an entry is in the engine-invisible half of the table.</summary>
    public static bool IsInvisibleEntry(int entry) => EntityHandle.IsInvisibleEntry(entry);

    /// <summary>
    /// <c>SERIAL_MASK</c> — <c>entitylist_base.cpp:16</c>,
    /// <c>(1 &lt;&lt; (NUM_SERIAL_NUM_BITS - 1)) - 1</c>. The top serial bit is
    /// reserved for static props, so a serial counts to 0x7fff and rolls to 0.
    /// </summary>
    public const uint SerialMask = 0x7FFF;

    /// <summary>
    /// <c>NUM_SERIAL_NUM_SHIFT_BITS</c> — the serial sits in the top 16 bits of
    /// the packed handle, exactly as <c>CBaseHandle::Init</c> puts it.
    /// </summary>
    private const int SerialShift = 16;

    /// <summary>
    /// The serial each invisible entry will hand out NEXT. Bumped when the
    /// entry is released, which is what makes a handle to a freed shell-less
    /// entity fail to resolve rather than name its successor —
    /// <c>CBaseEntityList::RemoveEntityAtSlot</c>'s rule.
    /// </summary>
    private readonly ushort[] _invisibleSerial = new ushort[EntryCount];

    /// <summary>The shell-less entity in each entry, or null. Never in an edict entry.</summary>
    private readonly ShellLessEntity?[] _shellLess = new ShellLessEntity?[EntryCount];

    /// <summary>
    /// Where the next search for a free entry starts, and it walks DOWNWARD
    /// from 8191.
    ///
    /// <para>
    /// <b>The direction is the whole of the collision argument.</b> The engine's
    /// own free list for this range is built ascending and handed out from its
    /// head — <c>CBaseEntityList::CBaseEntityList</c> adds entries
    /// <c>MAX_EDICTS+1 .. NUM_ENT_ENTRIES</c> to the TAIL and
    /// <c>AddNonNetworkableEntity</c> takes the HEAD — so a stock server-only
    /// <c>CBaseEntity</c> gets 2049, then 2050, and so on upward. Allocating
    /// downward from the top puts the two allocators as far apart as the range
    /// allows: they would have to meet in the middle, which needs 6,143 live
    /// entities in the invisible range at once.
    /// </para>
    ///
    /// <para>
    /// It is a distance and not a proof, which is why it is not the only
    /// defence: <see cref="EngineSide.InvisibleRangeReach"/> counts a handle from
    /// this range arriving at native code, and a collision is one of the ways
    /// that counter goes up.
    /// </para>
    /// </summary>
    private int _invisibleCursor = EntryCount - 1;

    /// <summary>How many shell-less entities are bound right now.</summary>
    public int ShellLessCount { get; private set; }

    /// <summary>How many this directory has allocated since it was made.</summary>
    public long ShellLessAllocated { get; private set; }

    /// <summary>How many it has released.</summary>
    public long ShellLessReleased { get; private set; }

    /// <summary>
    /// How many times an entry could not be found because the whole invisible
    /// range was occupied. Stock's answer to the same condition is a
    /// <c>Warning</c> and an <c>Assert</c>; this is countable.
    /// </summary>
    public long ShellLessExhausted { get; private set; }

    /// <summary>
    /// <b>WHERE THE SAFETY COUNTER IS NOT.</b> 2e's third gate clause — "a
    /// handle from the invisible range never reaches native" — is counted on
    /// the NATIVE side, in <c>CServerEntityResolver::Resolve</c>, and read back
    /// through <see cref="EngineSide.InvisibleRangeReach"/>.
    ///
    /// <para>
    /// It has to be. A managed counter could only count the routes this side
    /// already knows it has to take, so it would answer zero for exactly the
    /// case it exists to catch: an accessor nobody thought to route. The native
    /// resolver sees every handle that arrives however it got there.
    /// </para>
    ///
    /// <para>
    /// The zero is read beside <see cref="ShellLessCount"/> in
    /// <c>status.logical</c>, and that pairing is the point: a build that
    /// created no shell-less entities would also never leak one of their
    /// handles. Same shape as 1g's <c>status.think</c> and 6a's
    /// <c>cs_cl_interp</c> readout.
    /// </para>
    /// </summary>
    /// <summary>
    /// Takes an entry from the engine-invisible range and packs it with a
    /// serial this directory owns. <see cref="EntityHandle.None"/> when the
    /// range is full.
    ///
    /// <para>
    /// <b>IT CANNOT PRODUCE ENTRY ZERO, BY CONSTRUCTION</b>, which is 3b's
    /// pattern and is why it is stated here rather than tested for by a caller.
    /// Entry 0 is <c>worldspawn</c> and a REAL, bindable slot; the invalid
    /// sentinel is <c>INVALID_EHANDLE_INDEX</c>, not <c>default</c>. Two lanes
    /// have already measured the wrong path by treating a default handle as
    /// nothing. Every entry this hands out is at least
    /// <see cref="FirstInvisibleEntry"/>, so the packed value can be neither 0
    /// nor <c>uint.MaxValue</c>.
    /// </para>
    /// </summary>
    public EntityHandle AllocateInvisible()
    {
        int span = EntryCount - FirstInvisibleEntry;

        for (int i = 0; i < span; i++)
        {
            int entry = _invisibleCursor;

            _invisibleCursor--;
            if (_invisibleCursor < FirstInvisibleEntry)
                _invisibleCursor = EntryCount - 1;

            // BOTH TABLES, because they are occupied at different moments: the
            // state goes in before the instance is built and the binding after,
            // so an entry can legitimately hold one and not the other.
            if (HandleOf(entry) != EntityHandle.None || _shellLess[entry] is not null)
                continue;

            uint serial = _invisibleSerial[entry];
            uint packed = (uint)entry | (serial << SerialShift);

            ShellLessAllocated++;
            return new EntityHandle(packed);
        }

        ShellLessExhausted++;
        _log.Warning("[SourceSharp] the engine-invisible entity range is full; " +
                    $"{ShellLessCount} shell-less entities are live\n");

        return EntityHandle.None;
    }

    /// <summary>
    /// Records the state a shell would have carried, against the handle
    /// <see cref="AllocateInvisible"/> just handed out.
    ///
    /// <para>
    /// <b>Before the instance is built and not after</b>, which is the same
    /// ordering rule the rest of this port keeps: an accessor reached while the
    /// class's constructor runs must already be answered from managed memory,
    /// or it crosses with a handle native code has never heard of — and that is
    /// the one thing this step promises does not happen.
    /// </para>
    /// </summary>
    public bool AttachShellLess(EntityHandle handle, ShellLessEntity state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (handle.Value == uint.MaxValue || state.Handle != handle)
            return false;

        int entry = EntryIndex(handle);

        // REFUSED RATHER THAN CLAMPED, and refused for a REASON that is not
        // "out of range": an edict entry belongs to an entity the engine can
        // see, and putting shell-less state on one would make every accessor
        // answer from managed memory for something that has a shell.
        if (!IsInvisibleEntry(entry) || _shellLess[entry] is not null)
            return false;

        _shellLess[entry] = state;

        // THE ENTRY ADOPTS THE HANDLE'S SERIAL, which is a no-op at creation
        // (the allocator packed that serial a moment ago) and is the whole of
        // the reload's safety: a directory rebuilt by a hot reload adopts
        // handles it did not allocate, and its serial counters start at zero.
        // Without this, the entry would later be released, bump from 0, and
        // hand out a handle EQUAL to one the map's entity had already used.
        // BindAt's rule in the slot table, arrived at from the other side.
        _invisibleSerial[entry] = (ushort)((handle.Value >> SerialShift) & SerialMask);

        ShellLessCount++;
        return true;
    }

    /// <summary>
    /// Gives an entry back without going through <see cref="Unbind"/> — the arm
    /// for a creation that got as far as an entry and no further, because the
    /// class could not be built.
    /// </summary>
    public bool DetachShellLess(EntityHandle handle)
    {
        if (ShellLessAt(handle) is null)
            return false;

        ReleaseInvisible(EntryIndex(handle));
        return true;
    }

    /// <summary>
    /// The shell-less state behind a handle, or null — for an edict entity, for
    /// an entry that holds none, and for a stale handle whose entry has been
    /// reused. The serial is checked, exactly as <see cref="Resolve"/> checks it.
    /// </summary>
    /// <remarks>
    /// It checks the state's OWN handle rather than the directory's binding,
    /// because the state exists for a moment before the instance does — see
    /// <see cref="AttachShellLess"/>.
    /// </remarks>
    public ShellLessEntity? ShellLessAt(EntityHandle handle)
    {
        if (handle.Value == uint.MaxValue)
            return null;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount)
            return null;

        var state = _shellLess[entry];
        return (state is not null && state.Handle == handle) ? state : null;
    }

    /// <summary>
    /// Every shell-less entity of this directory, in entry order — what a
    /// find-by-class and a find-by-name walk.
    /// </summary>
    public IEnumerable<ShellLessEntity> ShellLessEntities
    {
        get
        {
            for (int entry = FirstInvisibleEntry; entry < EntryCount; entry++)
            {
                if (_shellLess[entry] is { } state)
                    yield return state;
            }
        }
    }

    /// <summary>
    /// Whether this handle names a shell-less entity — the question every
    /// engine accessor asks before it crosses.
    /// </summary>
    public bool IsShellLess(EntityHandle handle) => ShellLessAt(handle) is not null;

    /// <summary>
    /// Releases an invisible entry: forgets the state and BUMPS THE SERIAL, so
    /// every handle to the entity that has just gone fails to resolve rather
    /// than naming whatever takes the entry next.
    ///
    /// <para>
    /// <c>CBaseEntityList::RemoveEntityAtSlot</c> does exactly this and masks
    /// with <see cref="SerialMask"/> for the reason
    /// <c>entitylist_base.cpp:16</c> gives.
    /// </para>
    /// </summary>
    private void ReleaseInvisible(int entry)
    {
        if (_shellLess[entry] is null)
            return;

        _shellLess[entry] = null;
        _invisibleSerial[entry] = (ushort)((_invisibleSerial[entry] + 1) & SerialMask);

        ShellLessCount--;
        ShellLessReleased++;
    }

    /// <summary>
    /// The handles of one removal batch that actually HAVE a shell, decided
    /// before the unbinds and reused across frames.
    ///
    /// <para>
    /// <b>This is the crossing 2e removes and the leak it prevents at once.</b>
    /// A shell-less handle left in the batch would arrive at
    /// <c>SourceSharp_DestroyShell</c>, fail to resolve, and cost a crossing to
    /// do nothing — and it would be a handle from the invisible range reaching
    /// native code, which is the thing this step's gate says never happens.
    /// </para>
    /// </summary>
    private readonly List<EntityHandle> _shellBatch = new();

    /// <summary>
    /// Drops one mark, IF the entry still carries that handle's. The guard is
    /// the same one <see cref="Unbind"/> carries and for the same reason: an
    /// entry rebound since the mark was made belongs to its new occupant, and
    /// clearing blindly would drop a mark that entity had made for itself.
    /// </summary>
    private void ClearMark(EntityHandle handle)
    {
        if (handle.Value == uint.MaxValue)
            return;

        int entry = EntryIndex(handle);
        if ((uint)entry < EntryCount && _removing[entry] == handle)
            _removing[entry] = EntityHandle.None;
    }
}
