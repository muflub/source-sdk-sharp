//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// The legacy temp entity pool — <c>c_te_legacytempents.cpp</c>'s
/// <c>CTempEnts</c>, from the list up (plan_tmp.md Part 5).
///
/// <para>
/// A pooled temp entity is a gib, a shell casing, a sprite or a piece of
/// debris: it bounces, animates, makes noise and expires, and eleven of the
/// forty-four networked temp entities are requests into this pool rather than
/// effects of their own. <c>docs/te-legacy-pool-survey.md</c> is the map of the
/// 3,869 lines it came out of.
/// </para>
///
/// <para>
/// <b>WHAT THIS OWNS.</b> The active list, the free list, the 500-slot capacity,
/// the priority classes and their eviction rule, the lifetime and expiry
/// (<c>CTempEnts::IsActive</c>, including the <c>FTENT_FADEOUT</c> alpha ramp
/// and <c>FTENT_NEVERDIE</c>), the per-frame update loop and its
/// not-visible-so-drop policy, and <c>KillAttachedTents</c>.
/// </para>
///
/// <para>
/// <b>WHAT IT DOES NOT, AND WHY NONE OF IT IS AN OVERSIGHT.</b> The OBJECT stays
/// native, because a <c>C_LocalTempEntity</c> is already the draw shell — it was
/// de-entified onto <c>CShellRenderable</c> and
/// <c>src/sourcesharp/test/tempent_verdict_test.cpp</c> records that verdict —
/// and its thirty members are the drawable's state. <c>Frame()</c>'s physics
/// stays with it: the collision arm needs <c>UTIL_TraceLine</c> and a whole
/// <c>trace_t</c>, and this build's client has no trace at all, which is what
/// holds ledger rows C4 and C5 open. <c>PlaySound</c> is called from inside that
/// arm and moves with it, not before it.
/// </para>
///
/// <para>
/// <b>NOTHING HERE MIRRORS THE TEMPENT'S STATE.</b> Every read is a fresh
/// <see cref="TempEntLife"/> out of the live object and every write goes
/// straight back into it, because native <c>Frame()</c> mutates <c>die</c> and
/// <c>flags</c> every tick: a gib that lands clears <c>FTENT_ROTATE</c>, a
/// <c>FTENT_COLLIDEKILL</c> one sets <c>die = curtime</c>. A managed copy of
/// those fields would be stale within one frame of a bounce, and the symptom
/// would be a gib that never stops rotating rather than anything that fails.
/// The one thing this class holds per slot is whether the slot is IN THE LIST,
/// which is the fact native does not have.
/// </para>
///
/// <para>
/// <b>A SLOT WHOSE OBJECT HAS GONE IS DROPPED, NOT A FAULT.</b> Every ABI slot
/// answers false for an empty slot, and this class treats that as "it is no
/// longer in the pool" everywhere. That is what recovers a reservation the
/// memory pool could not fill, and a level torn down under a live list.
/// </para>
/// </summary>
public sealed class ClientTempEntPool
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineEffects _effects;
    private readonly IGameClock _clock;

    /// <summary>
    /// One tempent's physics for a frame — <c>C_LocalTempEntity::Frame</c>,
    /// which is C# now (behaviour ledger rows C4 and C5).
    ///
    /// <para>
    /// A SEAM RATHER THAN CODE IN THIS CLASS. See
    /// <see cref="IClientTempEntSimulation"/>: the pool is slots, eviction and
    /// expiry, and the physics is what a slot's contents DO. It also keeps this
    /// class's own tests about the list rather than about a gib bouncing.
    /// </para>
    /// </summary>
    private readonly IClientTempEntSimulation _simulation;

    /// <summary>
    /// The live tempents, by slot, IN ALLOCATION ORDER.
    ///
    /// <para>
    /// Order is not decoration: stock's <c>FreeLowPriorityTempEnt</c> gives up
    /// the FIRST low-priority tempent in its list, so "first" has to mean the
    /// same thing here — the oldest — or a full pool would start sacrificing
    /// the gib that just spawned instead of the one about to expire.
    /// </para>
    /// </summary>
    private readonly List<int> _active = new();

    /// <summary>Slots not in <see cref="_active"/>, newest first.</summary>
    private readonly Stack<int> _free = new();

    private int _capacity = -1;

    /// <summary>
    /// Stock's <c>gTempEntFrame</c>, a 0..31 counter handed to
    /// <c>C_LocalTempEntity::Frame</c> so that <c>FTENT_FLICKER</c> tempents
    /// spawn their dlight on one frame in thirty-two.
    ///
    /// <para>
    /// It was a function static inside <c>CTempEnts::Update</c> and it moved
    /// here with the loop, because it advances only on a frame the pool
    /// actually ran — stock steps it after the "no tempents or not in game"
    /// early-out, not before.
    /// </para>
    /// </summary>
    private int _frame;

    /// <summary>
    /// A monotonic count of the frames this pool has RUN, standing in for
    /// stock's <c>gpGlobals-&gt;framecount</c>.
    ///
    /// <para>
    /// Only the fast-collision rate limit reads it, and only ever as a
    /// difference — <c>m_iLastCollisionFrame + cl_fasttempentcollision &gt;
    /// framecount</c>. A tempent cannot exist across a frame this pool did not
    /// run, because if the list is empty there is nothing to have a last
    /// collision frame, so a pool-local counter and the engine's own answer the
    /// same question. It is stepped in the same place <see cref="_frame"/> is,
    /// which is stock's: after the empty-list early-out and BEFORE the paused
    /// early-out.
    /// </para>
    /// </summary>
    private int _frameCount;

    /// <summary>
    /// Whether <see cref="Update"/> is walking <see cref="_active"/> right now.
    ///
    /// <para>
    /// THE LOOP CAN BE RE-ENTERED, and the path is short: <c>Frame()</c> runs
    /// inside it, <c>Frame()</c> can <c>DispatchEffect</c> on a collision, and
    /// an effect can allocate a tempent. That is not reachable in THIS build —
    /// <c>m_pszImpactEffect</c> is only set by <c>ClientProjectile</c>, and both
    /// of its live callers pass six arguments so the parameter is always its
    /// NULL default, which <c>tempent_verdict_test.cpp</c> asserts — but the
    /// reachability of it is not this class's to guarantee, and a loop that
    /// corrupted its own list would be a pool that half works, which plan Part 5
    /// names as the worst outcome of this phase.
    /// </para>
    ///
    /// <para>
    /// So the loop is written to survive both things a reservation can do to
    /// the list: an APPEND (snapshotted, and moved down afterwards — stock's
    /// linked-list walk did not visit one either, because it captured
    /// <c>next</c> before the body ran) and an EVICTION, which would otherwise
    /// remove an element under an index walk. The eviction leaves a tombstone
    /// instead, and the loop's own compaction takes it out before anything
    /// reads the count again.
    /// </para>
    /// </summary>
    private bool _updating;

    /// <summary>
    /// The slot <see cref="Step"/> is in the middle of, or -1.
    ///
    /// <para>
    /// IT IS THE ONE SLOT AN EVICTION MUST NOT TAKE. Freeing the tempent the
    /// loop is standing on hands its slot straight back to the reservation that
    /// asked, so the loop finishes the step it started on a DIFFERENT tempent
    /// and then files the slot as a survivor — while the reservation has
    /// already put it in the list as well. One slot, twice, and it is freed
    /// twice on the frame the second copy expires.
    /// </para>
    ///
    /// <para>
    /// Stock has the same shape and does worse with it: its
    /// <c>FreeLowPriorityTempEnt</c> can free the very <c>C_LocalTempEntity</c>
    /// its caller's caller is holding, and the loop then reads it. Unreachable
    /// in both builds, for the reason <see cref="_updating"/> gives.
    /// </para>
    ///
    /// <para>
    /// The cost of skipping it is that a re-entered high-priority reservation
    /// is REFUSED when the only low-priority tempent in the pool is the one
    /// being stepped. That is a refusal in an unreachable path, which is the
    /// side to be wrong on.
    /// </para>
    /// </summary>
    private int _stepping = -1;

    /// <summary>Tempents this pool has handed out since load. Read by the report command.</summary>
    public int Allocated { get; private set; }

    /// <summary>Tempents this pool has refused because it was full.</summary>
    public int Refused { get; private set; }

    /// <summary>Low-priority tempents given up to make room for a high-priority one.</summary>
    public int Evicted { get; private set; }

    /// <summary>How many are live right now.</summary>
    public int Count => _active.Count;

    public ClientTempEntPool(ILog log, IEngineEffects effects, IGameClock clock,
                            IClientTempEntSimulation simulation)
    {

        _log = log;
        _effects = effects;
        _clock = clock;
        _simulation = simulation;
    }

    /// <summary>
    /// <c>CTempEnts::MAX_TEMP_ENTITIES</c>, asked of native once and remembered.
    ///
    /// <para>
    /// ASKED RATHER THAN RESTATED. A C# constant beside a C++ enum is the
    /// stand-in that drifts silently, and here the drift would be a pool that
    /// overflows the native slot table or one that refuses at 500 while native
    /// still has room. A zero answer means the slot is not wired, and is
    /// deliberately NOT cached: this is asked again next time rather than the
    /// pool being permanently dead because the first question came too early.
    /// </para>
    /// </summary>
    public int Capacity
    {
        get
        {
            if (_capacity > 0)
                return _capacity;

            int answer = _effects.TempEntPoolCapacity();

            if (answer <= 0)
                return 0;

            _capacity = answer;

            for (int slot = answer - 1; slot >= 0; slot--)
                _free.Push(slot);

            return _capacity;
        }
    }

    /// <summary>
    /// Take a slot off the free list, or -1. The pool's own reservation step:
    /// <see cref="Spawn"/> and <see cref="ReserveNamed"/> are its product
    /// callers, and nothing else reserves — the native-originated half died
    /// with the <c>SourceSharpTePool_Reserve/Release</c> exports and the
    /// <c>OnTempEntPoolAlloc</c> forwarding they were the reason for.
    ///
    /// <para>
    /// Stock <c>CTempEnts::TempEntAlloc</c> and <c>TempEntAllocHigh</c>, as one
    /// function, because the only difference between them was what happens when
    /// the pool is full: LOW gives up and HIGH sacrifices the oldest LOW. Two
    /// entry points that both had to agree on the word "full" is what made that
    /// worth folding.
    /// </para>
    /// </summary>
    internal int Reserve(int priority)
    {
        int capacity = Capacity;

        if (capacity <= 0)
        {
            Refused++;
            return -1;
        }

        if (_active.Count >= capacity && !MakeRoomFor(priority))
        {
            Refused++;
            return -1;
        }

        if (_free.Count == 0)
        {
            // The two bookkeeping structures disagreed, which they cannot do by
            // any path this class takes -- said out loud rather than throwing
            // inside the client's frame.
            _log.Warning("[SourceSharp/Client] temp entity pool: "
                      + $"{_active.Count} live of {capacity} and no free slot\n");
            Refused++;
            return -1;
        }

        int slot = _free.Pop();
        _active.Add(slot);
        Allocated++;
        return slot;
    }

    /// <summary>
    /// Reserve a slot, build the tempent in it, and write its whole spawn
    /// record. The slot, or -1 if nothing was made.
    ///
    /// <para>
    /// THE WHOLE OF WHAT A MANAGED SPAWN HELPER DOES TO THIS CLASS, in one
    /// call, so that no helper has to know that a spawn is three ABI crossings
    /// with an undo in the middle. Stock's seventeen helpers each call
    /// <c>TempEntAlloc( org, model )</c> and then assign fields; this is the
    /// first half of that, and <see cref="TempEntSpawn"/> is the second.
    /// </para>
    ///
    /// <para>
    /// THE RESERVATION IS UNDONE WHEN THE OBJECT IS NOT MADE, which native's
    /// own path deliberately does not do — <c>SourceSharpTePool_Release</c>
    /// only clears the native table and lets the managed pool notice on its
    /// next pass, because from native there is no way to say "un-reserve"
    /// without a fourth callback. From HERE there is, and the difference
    /// matters: a helper spawning a hundred gibs from a model index that does
    /// not resolve would otherwise leave a hundred slots occupied until the
    /// next frame, and the pool would look full to everything else that asked
    /// in between.
    /// </para>
    ///
    /// <para>
    /// <paramref name="priority"/> goes to <c>TempEntPoolCreate</c> as well as
    /// to the reservation, and is not a field of the record: the eviction rule
    /// reads priority off the LIVE OBJECT, so a high-priority tempent that was
    /// briefly low — between the create and the record write — could be
    /// sacrificed to make room for itself.
    /// </para>
    /// </summary>
    public int Spawn(Vector origin, int modelIndex, int priority, in TempEntSpawn spawn)
    {
        int slot = Reserve(priority);

        if (slot < 0)
            return -1;

        if (!_effects.TempEntPoolCreate(slot, origin, modelIndex, priority))
        {
            Unreserve(slot);
            return -1;
        }

        // THE ONE FLAG NATIVE CANNOT BE HANDED, and it is stock's own hole
        // rather than a transcription slip.
        //
        // c_te_legacytempents.cpp:1029 implements FTENT_SMOKETRAIL as
        // `Assert( !"FIXME:  Rework smoketrail to be client side" )` and nothing
        // else -- so that flag's only effect in this engine is to halt a debug
        // client. TempEntSpawns computes it exactly as stock does (BREAK_SMOKE
        // plus stock's own random(0,255) < 100 gate) and its tests assert that
        // computation; what stops here is only the handing over.
        //
        // WHY IT BECAME REACHABLE. Nothing in this build had ever sent a
        // TE_BreakModel -- lane C4 measured the pool at zero allocations -- so
        // the assert sat behind dead code. Rewiring IEngineEffects onto the
        // managed pool gave a breaking func_breakable a live route to it, and
        // about a hundred gibs in every two hundred and fifty-six take the flag.
        // A port that makes an unreachable crash reachable has broken the game
        // even when every line it wrote is faithful.
        //
        // Masked, not un-computed, and said once. The day someone does the
        // FIXME, deleting these four lines is the whole of the change.
        TempEntSpawn handed = spawn;

        if ((handed.Flags & (int)TempEntFlags.SmokeTrail) != 0)
        {
            handed.Flags &= ~(int)TempEntFlags.SmokeTrail;
            SaySmokeTrailIsUnimplemented();
        }

        if (!_effects.TempEntPoolWriteSpawn(slot, in handed))
        {
            // The object was made and then could not be written to, which by
            // any path this class takes cannot happen -- the slot it answers
            // for is the one it just filled. Destroyed rather than left half
            // built: a tempent carrying Prepare's defaults and none of its
            // helper's fields is a white 0.75-second sprite at the right place,
            // which is an effect that looks nearly right and is the hardest
            // kind of wrong to notice.
            _log.Warning($"[SourceSharp/Client] temp entity slot {slot} was created and "
                      + "then refused its spawn record; it is dropped\n");
            Release(slot);
            RemoveFromActive(slot);
            return -1;
        }

        JoinBreakGroup(slot, in handed);
        return slot;
    }

    /// <summary>
    /// Write a spawn record into a slot the managed side reserved and had
    /// native build into.
    ///
    /// <para>
    /// <see cref="Spawn"/>'s second half, without its first. The two exist
    /// because some spawns start from a native object the managed side cannot
    /// build: a <c>model_t *</c> out of <c>m_pShells[]</c> has no precache
    /// index for <c>TempEntPoolCreate</c> to resolve, so the slot is reserved
    /// here, filled by <c>CreateWithModel</c>, and only then gets its record.
    /// The DECISION is C#'s either way.
    /// </para>
    ///
    /// <para>
    /// IT DOES NOT RESERVE, DOES NOT CREATE AND DOES NOT UNDO. The caller
    /// reserved and owns the undo: <see cref="ReserveNamed"/> hands a failed
    /// create back to the free list, and a failed record write leaves an
    /// object that is built and carries Prepare's defaults, which
    /// <see cref="Update"/> expires on time like any other. Releasing the slot
    /// from here would free a tempent its reserving flow still points at,
    /// which is the one thing worse than a case that fades out looking wrong.
    /// </para>
    ///
    /// <para>
    /// The smoke-trail mask is <see cref="Spawn"/>'s, applied here for
    /// <see cref="Spawn"/>'s reason: the flag's only effect in this engine is
    /// to halt a debug client, and which flow reserved the slot does not
    /// change that.
    /// </para>
    /// </summary>
    public bool WriteSpawnInto(int slot, in TempEntSpawn spawn)
    {
        if (slot < 0 || !_active.Contains(slot))
        {
            // A slot this pool does not think is live. Said out loud rather
            // than written to: the write would land on whatever the native
            // table has at that index, which is the previous tenant.
            _log.Warning($"[SourceSharp/Client] a spawn record was written into slot {slot}, "
                      + "which this pool has not reserved; it is dropped\n");
            return false;
        }

        TempEntSpawn handed = spawn;

        if ((handed.Flags & (int)TempEntFlags.SmokeTrail) != 0)
        {
            handed.Flags &= ~(int)TempEntFlags.SmokeTrail;
            SaySmokeTrailIsUnimplemented();
        }

        if (!_effects.TempEntPoolWriteSpawn(slot, in handed))
            return false;

        JoinBreakGroup(slot, in handed);
        return true;
    }

    /// <summary>
    /// Give back a reservation whose object was never built.
    ///
    /// <para>
    /// The slot is always the LAST thing <see cref="Reserve"/> appended, so the
    /// ordinary path is a pop — which is safe even while <see cref="Update"/>
    /// is walking, because that walk indexes below the snapshot it took and
    /// this entry is above it. The search is the paranoid arm and leaves a
    /// tombstone for the same reason <see cref="MakeRoomFor"/> does.
    /// </para>
    /// </summary>
    private bool _saidSmokeTrailIsUnimplemented;

    /// <summary>
    /// Stock's <c>FTENT_SMOKETRAIL</c> is unimplemented and asserts. Said once
    /// per level, naming the line — a gib that quietly lost a trail nobody has
    /// ever seen is indistinguishable from one that never asked for it.
    /// </summary>
    private void SaySmokeTrailIsUnimplemented()
    {
        if (_saidSmokeTrailIsUnimplemented)
            return;

        _saidSmokeTrailIsUnimplemented = true;

        _log.Warning(
            "[SourceSharp/Client] a gib asked for FTENT_SMOKETRAIL and was not given it. "
          + "c_te_legacytempents.cpp:1029 implements that flag as an Assert naming a FIXME "
          + "and nothing else, so handing it over only halts a debug client. Stock's own "
          + "hole, not this port's -- but nothing in this build had ever sent a "
          + "TE_BreakModel, so it sat behind dead code until IEngineEffects was rewired onto "
          + "the managed pool. The flag is still COMPUTED exactly as stock computes it and "
          + "its tests still assert that; only the handing over stops here. "
          + "Said once per level.\n");
    }

    private void Unreserve(int slot)
    {
        Allocated--;
        RemoveFromActive(slot);

        // Nothing was ever built here, so there is nothing for the simulation
        // to have taken over -- but a slot handed back is a slot about to be
        // handed out, and the ONE bug this whole shape can have is a new
        // tempent inheriting the last tenant's transform. Cleared on every road
        // back to the free list rather than on the one that is known to matter.
        _simulation.Forget(slot);

        _free.Push(slot);
    }

    /// <summary>Take one slot out of the active list, wherever it is.</summary>
    private void RemoveFromActive(int slot)
    {
        if (_active.Count > 0 && _active[^1] == slot)
        {
            _active.RemoveAt(_active.Count - 1);
            return;
        }

        int at = _active.LastIndexOf(slot);

        if (at < 0)
            return;

        if (_updating)
            _active[at] = -1;
        else
            _active.RemoveAt(at);
    }

    /// <summary>
    /// Stock's <c>FreeLowPriorityTempEnt</c>: give up the FIRST low-priority
    /// tempent in the list, and only for a high-priority allocation.
    ///
    /// <para>
    /// THE PRIORITY IS READ OFF THE OBJECT, not off a managed copy of it. A
    /// cached priority would be a mirror, and the mirror this class refuses
    /// everywhere else; the cost is one ABI read per candidate on a path that
    /// only runs when the pool is completely full.
    /// </para>
    ///
    /// <para>
    /// A SLOT WHOSE OBJECT HAS GONE COUNTS AS ROOM. That is not a shortcut: it
    /// is the state a reservation that never got an object leaves behind, and
    /// treating it as occupied would let one failed allocation cost the pool a
    /// slot until the next level change.
    /// </para>
    /// </summary>
    private bool MakeRoomFor(int priority)
    {
        if (priority != TempEntPriority.High)
            return false;

        for (int i = 0; i < _active.Count; i++)
        {
            int slot = _active[i];

            // A tombstone from an earlier eviction in this same update pass.
            if (slot < 0)
                continue;

            // The one the loop is standing on. See _stepping.
            if (slot == _stepping)
                continue;

            if (_effects.TempEntPoolReadLife(slot, out TempEntLife life)
                && life.Priority != TempEntPriority.Low)
            {
                continue;
            }

            if (_updating)
            {
                // Re-entered from inside the update loop, which is walking this
                // list by index. A tombstone rather than a removal; the loop
                // drops it in the same pass. See _updating.
                _active[i] = -1;
            }
            else
            {
                _active.RemoveAt(i);
            }

            Release(slot);
            Evicted++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// One frame of the pool. Stock <c>CTempEnts::Update</c>'s body.
    /// </summary>
    public void Update(float frameTime)
    {
        // Stock's early-out is `count == 0 || !IsInGame()`. The IsInGame half
        // stayed native, where the engine pointer is; this is the other half,
        // and it is what keeps the frame counter from advancing on a frame the
        // pool did not run.
        if (_active.Count == 0)
            return;

        // !!!BUGBUG -- This needs to be time based. Stock's own comment, and
        // stock's own arithmetic.
        _frame = (_frame + 1) & 31;
        _frameCount++;

        // !!! Don't simulate while paused.... This is sort of a hack, revisit.
        // Stock's comment again: a paused client still has to put every tempent
        // back into the leaf system each frame or they stop being drawn, but
        // nothing expires and nothing is freed.
        if (frameTime == 0f)
        {
            foreach (int slot in _active)
            {
                if (slot >= 0)
                    _effects.TempEntPoolAddVisible(slot);
            }

            return;
        }

        // Compacted in place rather than removed one at a time: stock walked a
        // linked list and could unlink as it went, and this keeps the same
        // order at O(n) instead of O(n^2) on a frame where a grenade's worth of
        // gibs expires together.
        //
        // THE COUNT IS SNAPSHOTTED. A tempent allocated from inside this loop
        // is not stepped on the frame it spawned, which is stock's behaviour
        // too and for the same structural reason: its walk captured `next`
        // before running the body, so an AddToTail during a step was not
        // reached until the following frame. See _updating.
        int count = _active.Count;
        int keep = 0;

        _updating = true;

        try
        {
            for (int i = 0; i < count; i++)
            {
                int slot = _active[i];

                // A tombstone left by an eviction re-entered from a step.
                if (slot < 0)
                    continue;

                _stepping = slot;

                bool survived;

                try
                {
                    survived = Step(slot, frameTime);
                }
                finally
                {
                    _stepping = -1;
                }

                if (survived)
                    _active[keep++] = slot;
                else
                    Release(slot);
            }
        }
        finally
        {
            _updating = false;
        }

        // Anything appended while the loop ran, moved down to sit after the
        // survivors. Their order among themselves is the order they were
        // allocated in, which is what the eviction rule reads.
        for (int i = count; i < _active.Count; i++)
            _active[keep++] = _active[i];

        _active.RemoveRange(keep, _active.Count - keep);
    }

    /// <summary>
    /// One tempent's frame: expire it, step it, and cull it to the PVS.
    /// False means it was freed.
    /// </summary>
    private bool Step(int slot, float frameTime)
    {
        if (!_effects.TempEntPoolReadLife(slot, out TempEntLife life))
            return false;

        if (!IsActive(ref life, out bool changed))
        {
            // THE WRITE-BACK IS SKIPPED HERE ON PURPOSE. Stock's IsActive sets
            // the alpha to zero on the frame it answers false, and then the
            // caller frees the object without drawing it again -- so the write
            // is dead in stock too. Making the crossing anyway would be one ABI
            // call per expiring tempent to change a byte nothing reads.
            return false;
        }

        // THE PHYSICS IS C# NOW, and this is the one line that says so. It was
        // `_effects.TempEntPoolSimulate( slot, frameTime, _frame )` -- the
        // native C_LocalTempEntity::Frame -- and it could not be moved by
        // halves: that function integrates AND collides in one call, so a port
        // of only the collision arm would have left native integrating and
        // managed integrating and dropped every gib at twice gravity.
        //
        // The record goes in BY REFERENCE and comes back written: Frame mutates
        // flags and die every tick, and this method has already read the record
        // that carries them. The fade ramp's own write-back moved down past it
        // rather than staying above -- the two write the SAME record, and two
        // crossings to set two halves of one struct is one more than the ramp
        // used to cost on its own.
        int flagsBefore = life.Flags;
        float dieBefore = life.Die;

        bool survived = _simulation.Step(slot, frameTime, _frame, _frameCount, ref life);

        if (!survived)
        {
            // THE WRITE-BACK IS SKIPPED HERE FOR IsActive's REASON. Frame's own
            // `die = 0` on the way out is dead in stock too: the caller frees
            // the object on the next line and nothing reads the field again.
            return false;
        }

        // Only when something actually moved, which is the property the
        // conditional write above the physics used to have and is worth
        // keeping: `changed` is the fade ramp's, and Flags and Die are the only
        // two fields Frame writes. A MISSED write here would be a gib that goes
        // on rotating after it has landed -- native still reads `flags` when it
        // draws and when it culls -- so the comparison is over exactly the
        // fields that can have moved and not over a subset of them.
        if (changed || life.Flags != flagsBefore || life.Die != dieBefore)
            _effects.TempEntPoolWriteLife(slot, in life);

        if (_effects.TempEntPoolAddVisible(slot))
            return true;

        // Cull to PVS (not frustum cull, just PVS). FTENT_PERSIST is read off
        // the record Frame just wrote rather than re-read across the ABI: stock
        // re-read it because native Frame had mutated the object since, and now
        // Frame IS this record's writer. The re-read would be asking native to
        // hand back the word this line put there.
        //
        // If we can't draw it this frame, just dump it. Stock sets die and
        // clears FTENT_FADEOUT here before freeing; both writes are dead --
        // TempEntFree follows them unconditionally and nothing reads either
        // field again -- so they are not carried across the ABI.
        return (life.Flags & (int)TempEntFlags.Persist) != 0;
    }

    /// <summary>
    /// Stock <c>C_LocalTempEntity::IsActive</c>, whole.
    ///
    /// <para>
    /// <paramref name="changed"/> says whether anything in
    /// <paramref name="life"/> was written, so that the ordinary case — a
    /// tempent with time left — costs one ABI read and no write at all. Every
    /// live tempent runs this every frame, so "no change, no crossing" is the
    /// difference between one call per tempent per frame and two.
    /// </para>
    /// </summary>
    private bool IsActive(ref TempEntLife life, out bool changed)
    {
        changed = false;

        bool active = true;
        float remaining = life.Die - _clock.Now;

        if (remaining < 0f)
        {
            if ((life.Flags & (int)TempEntFlags.FadeOut) != 0)
            {
                // A kRenderNormal renderable draws opaque whatever its alpha
                // says, so the ramp has to move it into a translucent pass
                // before the first faded frame -- not after it.
                if (life.RenderMode == (int)RenderMode.Normal)
                {
                    life.RenderMode = (int)RenderMode.TransTexture;
                    changed = true;
                }

                // Truncated toward zero, as C's int-from-float is: the ramp
                // reaches zero a fraction of a frame earlier than a rounded one
                // would, which is stock's timing and not an approximation of it.
                int alpha = (int)(life.RenderAmt * (1f + remaining * life.FadeSpeed));

                if (alpha <= 0)
                {
                    active = false;
                    alpha = 0;
                }

                if (life.Alpha != alpha)
                {
                    life.Alpha = alpha;
                    changed = true;
                }
            }
            else
            {
                active = false;
            }
        }

        // Never die tempents only die when their die is cleared.
        if ((life.Flags & (int)TempEntFlags.NeverDie) != 0)
            active = life.Die != 0f;

        return active;
    }

    /// <summary>
    /// Stock <c>CTempEnts::KillAttachedTents</c>: everything stuck to that
    /// player dies on the next update.
    ///
    /// <para>
    /// This is what <c>CTEKillPlayerAttachments</c> asks for, and it is the one
    /// of the eleven pool-bound temp entities that needs no drawing primitive at
    /// all — the whole effect is a walk of a list this class now holds.
    /// </para>
    ///
    /// <para>
    /// STOCK'S UPPER GUARD IS NOT CARRIED, and that is measured rather than
    /// dropped. Stock refuses <c>client &gt; gpGlobals-&gt;maxClients</c>; no ABI
    /// slot answers <c>maxClients</c>, and the received path physically cannot
    /// violate it — <c>DT_TEKillPlayerAttachments</c> carries the player in FIVE
    /// UNSIGNED BITS, so the largest value that can arrive is 31 and
    /// <c>MAX_PLAYERS</c> is 33. What the guard actually protected against was
    /// stock's <c>cl_entitylist</c> lookup, and this function does not do one:
    /// a client index that matches nothing simply matches nothing.
    /// </para>
    /// </summary>
    public void KillAttachedTents(int client)
    {
        if (client <= 0)
        {
            _log.Warning($"[SourceSharp/Client] KillAttachedTents: bad client {client}\n");
            return;
        }

        float now = _clock.Now;

        foreach (int slot in _active)
        {
            if (slot < 0)
                continue;

            if (!_effects.TempEntPoolReadLife(slot, out TempEntLife life))
                continue;

            if ((life.Flags & (int)TempEntFlags.PlyrAttachment) == 0)
                continue;

            if (life.ClientIndex != client)
                continue;

            // good enough, it will die on next tent update.
            life.Die = now;
            _effects.TempEntPoolWriteLife(slot, in life);
        }
    }

    /// <summary>
    /// Free every live tempent. Stock <c>CTempEnts::Clear</c>'s body.
    ///
    /// <para>
    /// EVERY ONE GOES THROUGH THE FULL TEARDOWN, which stock's loop did not:
    /// it freed the memory straight out of the pool without
    /// <c>RemoveFromLeafSystem</c> or <c>OnRemoveTempEntity</c>, and got away
    /// with it because the break-model list is emptied on the next line and the
    /// leaf system is being torn down anyway. Doing it properly costs one call
    /// per live tempent at a level change and removes the assumption.
    /// </para>
    /// </summary>
    public void Clear()
    {
        foreach (int slot in _active)
        {
            if (slot >= 0)
                Release(slot);
        }

        _active.Clear();
        BreakGroups?.Clear();

        // THE POOL'S LEVEL SIGNAL, and it is the only one this side gets:
        // CTempEnts::Clear runs at level shutdown and from view.cpp's Init and
        // Shutdown, and nothing else tells this class a map has changed. The
        // simulation's once-per-level refusals hang off it, for the reason
        // IClientTempEntLevel gives -- a gap said once at load and never again
        // is a gap nobody reading a fresh console will see.
        _simulation.LevelInit();
    }

    /// <summary>
    /// The breakmodel lighting groups (CBreakableHelper's, lane c2-fx): a
    /// spawn that asks to join one does, by slot, and leaves when the slot is
    /// freed. Set by the composition; a pool without it keeps no groups.
    /// </summary>
    public TempEntBreakGroups? BreakGroups { get; set; }

    private void JoinBreakGroup(int slot, in TempEntSpawn spawn)
    {
        if (spawn.BreakHelper != 0)
            BreakGroups?.Insert(slot, spawn.BreakHelper == 2);
    }

    /// <summary>
    /// A tempent of a model the engine loads by name, in a fresh slot (the shell
    /// casings): the slot, or -1 when none could be had or made.
    /// </summary>
    public int ReserveNamed(Vector origin, string model, int priority, Interop.IFxTempEntModels models)
    {
        int slot = Reserve(priority);
        if (slot < 0)
            return -1;
        if (!models.CreateNamed(slot, origin, model, priority))
        {
            Unreserve(slot);
            return -1;
        }
        return slot;
    }

    /// <summary>Hand one slot back to native and to the free list.</summary>
    private void Release(int slot)
    {
        BreakGroups?.Remove(slot);
        _effects.TempEntPoolDestroy(slot);

        // The transform C# owns for this slot, and Frame's three private
        // working values, go with the tempent they described. Without this the
        // next tenant of the slot would be simulated from the last one's place
        // and velocity -- which draws as a gib that spawns already moving.
        _simulation.Forget(slot);

        _free.Push(slot);
    }
}
