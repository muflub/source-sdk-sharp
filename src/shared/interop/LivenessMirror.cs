using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// Which entity handles are alive, as managed code's own record —
/// plan_crossings.md Phase X5.
///
/// <para>
/// <b>What it replaces.</b> <c>EntityIsValid</c> was 38.578 crossings a SERVER
/// frame at rest (A11), the largest single row on that side, and every one of
/// them was a poll of something managed is already TOLD about: a standalone
/// entity announces its spawn (<c>OnScriptedSpawn</c>) and announces its death
/// (<c>OnScriptedRemoved</c>). A12 already said so in the row's own `why`
/// column — "managed is TOLD of every death, so 38.6 liveness pulls a frame is
/// a cache it does not keep". This is that cache.
/// </para>
///
/// <para>
/// <b>A DEAD handle is kept, not dropped, and that is the whole prize.</b> X0
/// measured 83.0% of those calls on handles that no longer resolve — managed
/// holding an <see cref="EntityHandle"/> to something that has since died and
/// asking, frame after frame, whether it is still there. If removal simply
/// erased the entry, every one of those asks would fall through to the slot and
/// the phase would win 17% of its row. So <see cref="NoteRemoved"/> writes
/// <c>false</c> and KEEPS it. Handles are (entry, serial) pairs and a recycled
/// slot arrives as a different <see cref="EntityHandle.Value"/>, so a kept
/// <c>false</c> is never inherited by the next entity to wear the number — the
/// hazard <see cref="StandaloneMirror.Forget"/> exists to avoid does not arise
/// here, because that mirror is keyed on a value that must not outlive its
/// entity and this one is keyed on the death itself.
/// </para>
///
/// <para>
/// <b>THE FALLBACK IS REAL, NOT AN ASSUMPTION.</b> <c>EntityIsValid</c> answers
/// for three hierarchies — <c>pBase || pStandalone || pWorld</c>
/// (<c>shell_standalone_base.cpp:171-210</c>) — and only the standalone one
/// pushes. X0 measured the <c>pBase</c> arm at 0 in every clean window (a full
/// enumeration of 1006 entities returned exactly one non-standalone,
/// worldspawn) but the arm still COMPILES, so this mirror never answers for a
/// handle it was not told about: <see cref="TryLookup"/> returns false and the
/// caller crosses. A missed push therefore costs a crossing, never a wrong
/// answer, and that is the direction this failure has to fall.
/// </para>
///
/// <para>
/// <b>THE WORLD IS DELIBERATELY NOT MIRRORED.</b> <c>CWorld</c>
/// (<c>world.h:95</c>) is not on the standalone hierarchy and never pushes, so
/// it falls through by construction — and it is left that way ON PURPOSE
/// rather than given a cheap map-lifetime record. A liveness record for the
/// world is precisely the record that would have answered "valid" for a world
/// that no longer existed: a <c>CWorld::Release()</c> that was <c>{}</c> left
/// the world undeleted across a level change and every single-load gate in this
/// project passed over the resulting segfault for six phases. The world is one
/// entity and its slot is answered by the engine's own table; buying that back
/// for a fraction of a crossing a frame is not a trade this phase will make.
/// <see cref="Refuses"/> states it as a testable fact.
/// </para>
///
/// <para>
/// <b>Handle 0 is refused too, and for a sharper reason than tidiness.</b>
/// <c>CStandaloneEntity::ManagedHandle</c>
/// (<c>shell_standalone_base.cpp:818</c>) returns <b>0</b> — not the invalid
/// sentinel — when its <c>CBaseHandle</c> is not valid, so a push can carry 0.
/// Native's resolver returns early only for <c>INVALID_EHANDLE_INDEX</c>
/// (0xFFFFFFFF); handle 0 is entry 0, serial 0, which it looks up for real and
/// which is where worldspawn lives. Recording a <c>false</c> under that key
/// would answer "the world is gone" to every later reader. Both values are
/// refused on the way in AND on the way out.
/// </para>
///
/// <para>
/// <b>One instance per server composition (plan_abi_hotreload D7).</b> The
/// server's module owns it, hands it to the writers (the entity dispatch, the
/// registry's removal, the level shutdown) through their constructors,
/// registers it for the gate commands, and attaches it to its side's
/// <c>EngineSide</c>, whose <c>EntityIsValid</c> is the reader, until
/// <c>Shutdown</c> takes it back. Nothing static holds one. A hot reload
/// carries the records across as bytes (<see cref="Save"/>,
/// <see cref="Restore"/>): the entities outlive the module, and without them
/// every handle from before the reload would cross to the slot again.
/// </para>
/// </summary>
public sealed class LivenessMirror
{
    /// <summary>
    /// The invalid handle native returns early for —
    /// <c>INVALID_EHANDLE_INDEX</c>. Refused rather than recorded.
    /// </summary>
    public const uint InvalidHandle = 0xFFFFFFFFu;

    /// <summary>
    /// How many records this mirror will hold before it starts forgetting dead
    /// ones.
    ///
    /// <para>
    /// A map that never stops spawning — gibs, projectiles, effects — would
    /// otherwise grow this dictionary for the whole life of the level, because
    /// dead entries are kept on purpose. Eviction drops the OLDEST DEAD entries
    /// only; a live entry is never evicted, so the reroute cannot start
    /// answering "gone" for something that is standing in front of the player.
    /// An evicted handle falls through to the slot, which is a crossing rather
    /// than a wrong answer — the same direction every other refusal in this
    /// class falls.
    /// </para>
    /// </summary>
    public const int MaxRecords = 32768;

    private readonly Dictionary<uint, bool> _known = new();

    /// <summary>Dead handles in the order they died, for eviction.</summary>
    private readonly Queue<uint> _deadOrder = new();

    /// <summary>
    /// Whether readers are answered from this mirror or fall through to the
    /// ABI slot.
    ///
    /// <para>
    /// <b>The census control, and it is a READING control only.</b> Phase
    /// X2/X3's divergence gate found the bug that makes this worth spelling
    /// out: <c>PlaySequence</c> gated its WRITE on the same flag its readers
    /// used, so switching the reroute off to take the census's "before" arm
    /// stopped feeding the mirror, and the mirror it was measuring drifted
    /// while it measured it. <see cref="NoteCreated"/> and
    /// <see cref="NoteRemoved"/> do not consult this flag. The mirror stays
    /// correct while it is switched off and is not stale when it comes back.
    /// </para>
    /// </summary>
    public bool Rerouted { get; set; } = true;

    /// <summary>How many handles this mirror has a record for. For the gate.</summary>
    public int RecordCount => _known.Count;

    /// <summary>How many of them are alive. For the gate.</summary>
    public int LiveCount { get; private set; }

    /// <summary>How many dead records have been evicted this level. For the gate.</summary>
    public int EvictedCount { get; private set; }

    /// <summary>
    /// Whether this mirror declines to hold any record for a handle at all.
    /// The world's slot and the invalid sentinel. See the class remarks.
    /// </summary>
    public static bool Refuses(EntityHandle entity)
        => entity.Value == 0u || entity.Value == InvalidHandle;

    /// <summary>
    /// A standalone entity announced its spawn. It is alive.
    ///
    /// <para>
    /// Fired from <c>OnScriptedSpawn</c>, which arrives at Spawn rather than at
    /// construction. The window between the two is not a correctness gap: a
    /// handle this mirror has not been told about is not claimed, so an ask
    /// inside that window crosses and gets native's answer.
    /// </para>
    /// </summary>
    public void NoteCreated(EntityHandle entity)
    {
        if (Refuses(entity))
            return;

        if (_known.TryGetValue(entity.Value, out bool was) && was)
            return;

        _known[entity.Value] = true;
        LiveCount++;
    }

    /// <summary>
    /// A standalone entity announced its death. It is gone, and the record of
    /// its being gone is what this mirror is FOR.
    ///
    /// <para>
    /// Fired from the <c>Removed</c> arm of <c>ServerDispatch</c>, which
    /// <c>~CStandaloneEntity</c> reaches through its own
    /// <c>OnManagedRemoved</c>.
    /// </para>
    ///
    /// <para>
    /// THIS USED TO NEED A SUBCLASS. A virtual called from a BASE destructor
    /// dispatches to the base, because the derived part is already gone and the
    /// vtable has been demoted with it — so while <c>OnManagedRemoved</c> was
    /// an empty body on <c>CStandaloneEntity</c> and an override on
    /// <c>CStandaloneScriptedEntity</c>, the destructor's call told managed code
    /// nothing, and that subclass carried a destructor whose whole content was
    /// one call to work around it. plan_entity.md step 1f deleted the subclass
    /// and the body is the base's, so the base destructor's own call reaches it.
    /// </para>
    ///
    /// <para>
    /// <b>Native still says "valid" at this instant</b>, and that is not a
    /// divergence: the push happens inside the destructor, BEFORE
    /// <c>m_RefEHandle.Term()</c> at <c>shell_standalone_base.cpp:753</c>, so
    /// the entity is still in the engine's table while it announces that it is
    /// leaving. Managed's answer for an entity that has said goodbye is "gone",
    /// which is the one the callers want. Nothing walks an entity mid-destructor,
    /// so the divergence gate never sees the window.
    /// </para>
    /// </summary>
    public void NoteRemoved(EntityHandle entity)
    {
        if (Refuses(entity))
            return;

        if (_known.TryGetValue(entity.Value, out bool was))
        {
            if (!was)
                return;

            LiveCount--;
        }

        _known[entity.Value] = false;
        _deadOrder.Enqueue(entity.Value);

        Evict();
    }

    /// <summary>
    /// What this mirror knows about a handle, if anything.
    ///
    /// <para>
    /// Returns false — "ask native" — for every handle it was not told about.
    /// That is the <c>pBase</c> arm's fallback and the world's, and it is a real
    /// fallback rather than a claim that those arms are empty.
    /// </para>
    /// </summary>
    public bool TryLookup(EntityHandle entity, out bool alive)
    {
        alive = false;

        // THE SENTINEL IS ANSWERED, NOT DECLINED, and it is not a guess: it is
        // native's own early return moved one side of the boundary.
        // CServerEntityResolver::Resolve opens with
        //
        //     if ( hEntity == INVALID_EHANDLE_INDEX )
        //         return ent;      // all three arms NULL -> IsValid() false
        //
        // so this handle can never resolve, whatever the entity list holds.
        // EntityHandle.None IS that value (uint.MaxValue), and managed code
        // polls `.IsValid` on defaulted and cleared handles constantly.
        //
        // MEASURED, not assumed: with this declined instead of answered, the
        // reroute left 32.00 crossings a server frame standing and the mirror's
        // own counters read answered=1524 refused=6432 unknown=0 over three
        // seconds -- 81% of every lookup was this one value going to the slot
        // to be told what the first line of the callee already knew.
        if (entity.Value == InvalidHandle)
        {
            SentinelLookups++;
            return true;
        }

        // Handle 0 is NOT answered here, and the difference is deliberate.
        // It is not the sentinel: native looks it up for real, as entry 0 with
        // serial 0. Entry 0 is worldspawn's, and whether that resolves depends
        // on a serial this side cannot see. Declined, and it crosses.
        if (entity.Value == 0u)
        {
            ZeroLookups++;
            return false;
        }

        if (_known.TryGetValue(entity.Value, out alive))
        {
            AnsweredLookups++;
            return true;
        }

        UnknownLookups++;

        if (UnknownSamples.Count < 16 && !UnknownSamples.Contains(entity.Value))
            UnknownSamples.Add(entity.Value);

        return false;
    }

    /// <summary>Lookups this mirror answered. Diagnostic; see the reroute command.</summary>
    public long AnsweredLookups { get; private set; }

    /// <summary>Lookups answered false as the invalid sentinel, without crossing.</summary>
    public long SentinelLookups { get; private set; }

    /// <summary>Lookups declined as handle 0, which crosses. See TryLookup.</summary>
    public long ZeroLookups { get; private set; }

    /// <summary>Lookups for a handle this mirror was never told about.</summary>
    public long UnknownLookups { get; private set; }

    /// <summary>A few of the handles it did not know, so a reader can name them.</summary>
    public List<uint> UnknownSamples { get; } = new();

    /// <summary>Zeroes the diagnostic counters.</summary>
    public void ResetCounters()
    {
        AnsweredLookups = 0;
        SentinelLookups = 0;
        ZeroLookups = 0;
        UnknownLookups = 0;
        UnknownSamples.Clear();
    }

    /// <summary>
    /// Every handle this mirror currently claims is ALIVE.
    ///
    /// <para>
    /// <b>For the divergence gate, and it is the half that matters.</b> Walking
    /// the world's live entities catches a creation push that did not happen;
    /// it cannot catch a REMOVAL push that did not happen, because the entity
    /// is not in the world to be walked. That is the dangerous direction — the
    /// mirror would answer "still there" for something freed, and managed would
    /// go on addressing it — so the gate walks this too and asks the slot about
    /// each one. Materialised into a list by the caller before it is used: the
    /// slot call is a boundary crossing and nothing may mutate the dictionary
    /// under an open enumerator.
    /// </para>
    /// </summary>
    public IEnumerable<uint> LiveHandles()
    {
        foreach (var pair in _known)
        {
            if (pair.Value)
                yield return pair.Key;
        }
    }

    /// <summary>Everything goes. The level is going.</summary>
    public void Clear()
    {
        _known.Clear();
        _deadOrder.Clear();
        LiveCount = 0;
        EvictedCount = 0;
    }

    /// <summary>
    /// Drop the oldest dead records once the dictionary is over its bound.
    /// Live records are never dropped. See <see cref="MaxRecords"/>.
    /// </summary>
    /// <summary>The <see cref="ReloadStore"/> section a hot reload carries this mirror in.</summary>
    public const string ReloadSection = "liveness-mirror";

    private const uint SaveMagic = 0x3152_4D4C; // "LMR1"
    private const int HeaderBytes = 4 + 1 + 4 + 4;

    /// <summary>
    /// The records, as bytes for the reload carrier: the live handles, then
    /// the dead ones in the order they died, so the restored mirror evicts in
    /// the same order this one would have. <see cref="Rerouted"/> goes too;
    /// the lookup counters do not (they are a census window, not state).
    /// </summary>
    public byte[] Save()
    {
        var dead = new List<uint>(_deadOrder.Count);

        foreach (uint handle in _deadOrder)
        {
            if (_known.TryGetValue(handle, out bool alive) && !alive)
                dead.Add(handle);
        }

        var bytes = new byte[HeaderBytes + (LiveCount + dead.Count) * 4];
        var span = bytes.AsSpan();
        int at = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(span[at..], SaveMagic); at += 4;
        span[at++] = Rerouted ? (byte)1 : (byte)0;

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], LiveCount); at += 4;
        foreach (var (handle, alive) in _known)
        {
            if (!alive)
                continue;

            BinaryPrimitives.WriteUInt32LittleEndian(span[at..], handle); at += 4;
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], dead.Count); at += 4;
        foreach (uint handle in dead)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[at..], handle); at += 4;
        }

        return bytes;
    }

    /// <summary>
    /// Takes back what <see cref="Save"/> wrote, and answers how many records
    /// it adopted, or -1 when the bytes are not a whole save (nothing is
    /// adopted then). A record already here wins: a push that reached the new
    /// mirror first is newer than anything the old one held.
    /// </summary>
    public int Restore(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != SaveMagic)
            return -1;

        int at = 4;
        bool rerouted = bytes[at++] != 0;

        int live = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]); at += 4;
        if (live < 0 || (long)live * 4 > bytes.Length - at - 4)
            return -1;

        int liveStart = at;
        at += live * 4;

        int dead = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]); at += 4;
        if (dead < 0 || (long)dead * 4 != bytes.Length - at)
            return -1;

        int adopted = 0;
        Rerouted = rerouted;

        for (int i = 0; i < live; i++)
        {
            uint handle = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(liveStart + i * 4)..]);

            if (Refuses(new EntityHandle(handle)) || !_known.TryAdd(handle, true))
                continue;

            LiveCount++;
            adopted++;
        }

        for (int i = 0; i < dead; i++, at += 4)
        {
            uint handle = BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);

            if (Refuses(new EntityHandle(handle)) || !_known.TryAdd(handle, false))
                continue;

            _deadOrder.Enqueue(handle);
            adopted++;
        }

        Evict();

        return adopted;
    }

    private void Evict()
    {
        while (_known.Count > MaxRecords && _deadOrder.Count > 0)
        {
            uint oldest = _deadOrder.Dequeue();

            // Still dead? A handle cannot come back — the value carries the
            // serial — but the record may already have been cleared, so this
            // checks rather than assumes.
            if (_known.TryGetValue(oldest, out bool alive) && !alive)
            {
                _known.Remove(oldest);
                EvictedCount++;
            }
        }
    }
}

/// <summary>
/// The ABI slot <see cref="LivenessMirror"/> replaced, still callable — for the
/// divergence gate and for nothing else.
///
/// <para>
/// <b>The slot is KEPT, and this is half of why.</b> A push mirror's failure
/// mode is not staleness — push-on-event makes that impossible — but a lifetime
/// path nobody pushed from, which does not go stale: it diverges and then
/// answers confidently forever, with no log line and no crash. Holding that
/// shut needs an INDEPENDENT oracle, and the slot is one: it resolves through
/// the engine's own handle table, the table the trace filters and the event
/// queue resolve through. A gate that compared the mirror against something
/// managed had also written would agree with itself whatever the shells did,
/// which is the shape of check this project has been misled by more than forty
/// times.
/// </para>
///
/// <para>
/// <b>Game code must not call this.</b> Every reader goes through
/// <c>Engine.EntityIsValid</c> or the entity binding, which is what makes the
/// reroute one change in one place. This is here because
/// <c>cs_sv_liveness_check</c> lives in the server assembly, which cannot see
/// <c>Engine</c> — and calling the binding instead would compare the mirror
/// against itself.
/// </para>
/// </summary>
public sealed class LivenessSlotReference
{
    // ONE side's engine (plan_abi_hotreload D8): the composition registers one
    // over its own ModuleEngine, so the oracle asks the slot of the side whose
    // mirror it checks, never whichever side is active.
    private readonly EngineSide _side;

    public static LivenessSlotReference For(ModuleEngine engine)
        => new((engine ?? throw new ArgumentNullException(nameof(engine))).Api);

    internal LivenessSlotReference(EngineSide side) => _side = side;

    /// <summary>The side this oracle asks, for a fact.</summary>
    internal EngineSide Side => _side;

    /// <inheritdoc cref="EngineSide.EntityIsValidBySlot"/>
    public bool IsValid(EntityHandle entity) => _side.EntityIsValidBySlot(entity);
}
