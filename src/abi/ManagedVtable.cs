using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One managed-built native object: the block native holds as an
/// <c>I&lt;Area&gt; *</c>. plan_engineapi_vtables Phase D, §1.3; owned by
/// <see cref="ManagedVtables"/> since plan_managed_vtable_reload.md L3.
///
/// <para>
/// <b>THE BLOCK.</b> <c>NativeMemory</c>, never the GC heap, so a compaction
/// cannot move it under native: word 0 is the vtable pointer, word 1 the
/// <see cref="GCHandle{T}"/> of the target, word 2 the handle of the registry
/// that owns the block, word 3 the side it serves (plan_client_vtables CD; a
/// label only since plan_side_isolation P5, when each side's thunks run in
/// that side's own copy of the Abi and mark nothing), and the vtable itself
/// follows at word 4.
/// Each object carries its own table.
/// </para>
///
/// <para>
/// <b>RESERVED WORDS.</b> A Valve base class with data members after its vptr
/// (CCallbackBase: flags and callback id, which Steam writes) gets those words
/// kept free: <c>[vptr][R caller words][handle][registry][side][vtable]</c>.
/// Every reader finds the header BACK from the vtable word 0 points at
/// (<see cref="Header"/>), so one rule reads both shapes and R is never stored
/// where the caller could overwrite it.
/// </para>
///
/// <para>
/// <b>RELEASE FREES.</b> The registry frees the handle and the block on the
/// one release (native's slot 0, or the managed safety net for an object
/// native never released). Native nulls its holder before it calls Release
/// (managed_accept.h's ManagedRelease), so nothing calls a freed block; the
/// registry, not the block, answers whether an object is still live.
/// </para>
/// </summary>
public sealed unsafe class ManagedVtable
{
    internal const int HeaderWords = 4;

    /// <summary>
    /// The header of the block at <paramref name="self"/>: the three words
    /// just before its vtable, so [0] is the target's handle, [1] the
    /// registry, [2] the side, whatever the block reserves after its vptr.
    /// </summary>
    internal static nint* Header(void* self) => (nint*)((nint*)self)[0] - (HeaderWords - 1);

    /// <summary>
    /// Every generated thunk's first act: count the depth (plan_managed_vtable_reload.md
    /// L6) on the registry that owns the block (its header's registry word), and hand that
    /// registry back for <see cref="Leave"/>. The depth is the registry's own,
    /// not a static: two registries in one process (two game instances) each
    /// count only their own thunks. Null for a block with no owner, which the
    /// thunk's body then refuses.
    /// </summary>
    public static ManagedVtables? Enter(void* self)
    {
        nint owner = self == null ? 0 : Header(self)[1];
        if (owner == 0)
            return null;

        ManagedVtables registry = GCHandle<ManagedVtables>.FromIntPtr(owner).Target;
        registry.Depth++;
        return registry;
    }

    /// <summary>
    /// Every generated thunk's last act, in a finally, on the registry
    /// <see cref="Enter"/> returned: never read back from the block, which a
    /// Release slot has freed by then.
    /// </summary>
    public static void Leave(ManagedVtables? owner)
    {
        if (owner is not null)
            owner.Depth--;
    }

    private readonly ManagedVtables _owner;
    private readonly nint* _block;

    internal ManagedVtable(ManagedVtables owner, string contract, nint* block, int entries, int reservedWords)
    {
        _owner = owner;
        _block = block;
        Contract = contract;
        Entries = entries;
        ReservedWords = reservedWords;
    }

    /// <summary>The object native holds: <c>{ vtbl, handle, registry, side }</c>.</summary>
    public void* Pointer => _block;

    /// <summary>The same address, for a caller compiled without /unsafe (SourceSharp.Client).</summary>
    public nint Address => (nint)_block;

    /// <summary>The native interface this block implements, the registry's key.</summary>
    public string Contract { get; }

    /// <summary>The table's slot count.</summary>
    public int Entries { get; }

    /// <summary>The caller's words between the vptr and the header (<see cref="ImplementedByManagedAttribute.ReservedWords"/>).</summary>
    public int ReservedWords { get; }

    /// <summary>Whether the block has been released (and freed). Asks the registry, never the block.</summary>
    public bool Released => !_owner.IsLive((nint)_block);

    /// <summary>
    /// Releases from the managed side, for an object native never released:
    /// the net under native's own Release, so no handle outlives the side.
    /// False, touching nothing, for one already released.
    /// </summary>
    public bool Release() => _owner.Free((nint)_block);

    /// <summary>
    /// The target a thunk was called on. Throws for a block with no handle,
    /// and the thunk's catch turns that into a loud line.
    /// </summary>
    public static T Target<T>(void* self) where T : class
    {
        nint handle = Header(self)[0];
        if (handle == 0)
            throw new ObjectDisposedException(typeof(T).Name,
                $"native called a {typeof(T).Name} through a managed vtable with no target");

        return GCHandle<T>.FromIntPtr(handle).Target;
    }

    /// <summary>
    /// The generated <c>Release</c> slot: the owning registry frees the handle
    /// and the block. A block the registry no longer holds is refused.
    /// </summary>
    public static void ReleaseBlock<T>(void* self) where T : class
    {
        nint owner = Header(self)[1];
        if (owner == 0 || !GCHandle<ManagedVtables>.FromIntPtr(owner).Target.Free((nint)self))
            throw new ObjectDisposedException(typeof(T).Name, $"{typeof(T).Name} was released twice");
    }

    /// <summary>
    /// The generated Validate slot (plan_managed_vtable_reload.md L1): -1 when
    /// native's per-slot hashes equal this table's, else the first slot that
    /// differs; a count mismatch reports at the shorter count. Never -1 for a
    /// null array or a non-positive count.
    /// </summary>
    public static int ValidateSlots(ReadOnlySpan<uint> mine, uint* theirs, int count)
    {
        if (theirs == null || count <= 0)
            return 0;

        int shared = Math.Min(count, mine.Length);
        for (int i = 0; i < shared; i++)
        {
            if (theirs[i] != mine[i])
                return i;
        }

        return count == mine.Length ? -1 : shared;
    }

    /// <summary>
    /// An exception must never unwind into native frames: a thunk catches it,
    /// reports it here with the slot it came from, and answers the default.
    /// </summary>
    /// <param name="log">
    /// Where the report goes: the owning registry's <see cref="ManagedVtables.Log"/>
    /// for a generated thunk, the composition's own for a binding. Null prints
    /// to the process's standard error, which is all there is before a side
    /// has a console.
    /// </param>
    public static void Fail(ILog? log, string where, Exception e)
    {
        string head = $"[SourceSharp] ===== EXCEPTION IN {where} (managed vtable) =====\n";
        string body = ManagedDiagnostics.Describe(e);
        if (log is null)
        {
            Console.Error.Write(head + body);
            return;
        }

        log.Warning(head);
        log.Warning(body);
    }
}

/// <summary>
/// The registry of one side's managed-built blocks (plan_managed_vtable_reload.md
/// L3). It lives in SourceSharp.Abi, which loads once into the Bootstrap's
/// context and never unloads, and the Bootstrap holds one per side as an opaque
/// handle (plan_side_entries SE2), so it outlives the side's load context. Keyed by native interface name: for each, the live
/// block, its slot count and its slot hashes (which L5's reload check reads).
///
/// <para>
/// <b>A RELOAD PATCHES IN PLACE (L4).</b> Native holds each block for the whole
/// process. Before the side's load context unloads, <see cref="Detach"/> frees
/// every target handle (a handle into the outgoing context would pin it) and
/// points every slot at the trap table; the next build's <see cref="Create{T}"/>
/// for the same contract writes its handle and thunks into the SAME block. A
/// slot reached in between is a bug in the reload's ordering and fails loudly,
/// naming the contract and slot.
/// </para>
/// </summary>
public sealed unsafe class ManagedVtables : IDisposable
{
    private readonly Dictionary<nint, Entry> _live = new();
    private readonly Dictionary<string, nint> _byKey = new(StringComparer.Ordinal);
    private readonly Action<string, int> _onTrap;
    private readonly Side _side;
    private GCHandle<ManagedVtables> _self;

    private sealed class Entry(string contract, string key, int entries, int reservedWords, uint[] hashes)
    {
        public string Contract { get; } = contract;
        public string Key { get; } = key;   // the contract, or contract#instance
        public int Entries { get; } = entries;
        public int ReservedWords { get; } = reservedWords;
        public uint[] Hashes { get; } = hashes;   // fixed for the process: a reload must match them (L5)
        public bool Attached { get; set; } = true;
    }

    /// <param name="onTrap">
    /// What a trapped slot does, given the contract and slot index. The default
    /// fails the process: a call that reaches a detached block has no target.
    /// </param>
    /// <param name="side">The side whose areas this registry builds; each block's thunks make it active.</param>
    public ManagedVtables(Action<string, int>? onTrap = null, Side side = Side.Server)
    {
        _side = side;
        _onTrap = onTrap ?? FailFast;
        _self = new GCHandle<ManagedVtables>(this);
    }

    private void FailFast(string contract, int slot)
    {
        string why = $"[SourceSharp] {contract} slot {slot} was called while its managed vtable was detached " +
                     "for a reload: nothing may call managed between the unload and the load";
        if (Log is { } log)
            log.Warning(why + "\n");
        else
            Console.Error.Write(why + "\n");
        Environment.FailFast(why);
    }

    /// <summary>The side this registry's blocks serve.</summary>
    public Side Side => _side;

    /// <summary>
    /// Where this registry's thunks report a fault (statics to zero): the side's
    /// console for the current load, which the side's load sets. Null before
    /// any load has one; a report then goes to standard error.
    /// </summary>
    public ILog? Log { get; set; }

    /// <summary>
    /// How many of this registry's thunks are on the stack (plan_managed_vtable_reload.md
    /// L6); a reload is refused unless 0. A plain int: every crossing is on the
    /// engine thread. Raised and lowered by <see cref="ManagedVtable.Enter"/>
    /// and <see cref="ManagedVtable.Leave"/>.
    /// </summary>
    public int Depth { get; internal set; }

    /// <summary>Blocks allocated and not yet freed, attached or detached.</summary>
    public int Live => _live.Count;

    /// <summary>Blocks detached for a reload and not yet patched by the next build.</summary>
    public int Detached => _live.Values.Count(e => !e.Attached);

    /// <summary>The contracts still detached, for the load's refusal.</summary>
    public IEnumerable<string> DetachedContracts => _live.Values.Where(e => !e.Attached).Select(e => e.Contract);

    /// <summary>
    /// The registry's key for one block: the contract alone, or the contract
    /// and an instance name for a contract with many live blocks (one per
    /// Steam callback). The unnamed instance is a key of its own.
    /// </summary>
    private static string KeyOf(string contract, string? instance)
        => instance is null ? contract : contract + "#" + instance;

    /// <summary>Whether <paramref name="contract"/> (its <paramref name="instance"/>) has a block, attached or detached.</summary>
    public bool Holds(string contract, string? instance = null) => _byKey.ContainsKey(KeyOf(contract, instance));

    /// <summary>Whether <paramref name="contract"/> has a block waiting to be patched.</summary>
    public bool IsDetached(string contract)
        => _byKey.TryGetValue(contract, out var block) && !_live[block].Attached;

    /// <summary>Blocks freed over the registry's life: native releases and the safety net together.</summary>
    public int Freed { get; private set; }

    /// <summary>
    /// Builds the block for <paramref name="contract"/> around
    /// <paramref name="target"/>. Called by generated code only, with the thunk
    /// addresses in slot order (Release at 0, Validate at 1) and each slot's
    /// hash. A contract detached for a reload is PATCHED in place (L4); one
    /// with an attached block is refused. <paramref name="valveLayout"/> is a
    /// table in a Valve caller's own layout (vphysics' callbacks): it has no
    /// Release/Validate pair, and one slot is a whole table
    /// (IPhysicsConstraintEvent, IMotionEvent).
    /// </summary>
    /// <param name="reservedWords">
    /// The caller's words after the vptr (<see cref="ImplementedByManagedAttribute.ReservedWords"/>),
    /// zeroed here and never written again; a patch must ask for the same count.
    /// </param>
    /// <param name="instance">
    /// Names one of many live blocks of <paramref name="contract"/>; the
    /// reload patches each by its name. Null for the contract's one block.
    /// </param>
    public ManagedVtable Create<T>(string contract, T target, ReadOnlySpan<nint> slots, ReadOnlySpan<uint> hashes,
                                   int reservedWords = 0, string? instance = null, bool valveLayout = false)
        where T : class
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reservedWords);
        string key = KeyOf(contract, instance);
        ArgumentNullException.ThrowIfNull(target);
        if (valveLayout ? slots.Length < 1 : slots.Length < 2)
            throw new ArgumentException(valveLayout
                ? "a Valve-layout managed vtable has at least one slot"
                : "a managed vtable has at least its Release and Validate slots", nameof(slots));
        if (hashes.Length != slots.Length)
            throw new ArgumentException($"{contract}: {slots.Length} slot(s) but {hashes.Length} hash(es)", nameof(hashes));
        if (_byKey.TryGetValue(key, out nint held))
        {
            if (_live[held].Attached)
                throw new InvalidOperationException($"{key} already has a live managed vtable");
            return Patch(contract, (nint*)held, target, slots, hashes, reservedWords);
        }

        int table = reservedWords + ManagedVtable.HeaderWords;
        var block = (nint*)NativeMemory.AllocZeroed((nuint)((table + slots.Length) * sizeof(nint)));
        block[0] = (nint)(block + table);
        nint* header = ManagedVtable.Header(block);
        header[0] = GCHandle<T>.ToIntPtr(new GCHandle<T>(target));
        header[1] = GCHandle<ManagedVtables>.ToIntPtr(_self);
        header[2] = (nint)_side;
        slots.CopyTo(new Span<nint>(block + table, slots.Length));

        _live.Add((nint)block, new Entry(contract, key, slots.Length, reservedWords, hashes.ToArray()));
        _byKey.Add(key, (nint)block);
        return new ManagedVtable(this, contract, block, slots.Length, reservedWords);
    }

    /// <summary>
    /// Writes the next build's target and thunks into a detached block: the
    /// block native holds keeps its address (and the caller's reserved
    /// words), the header gets the new handle and every slot the new thunk.
    /// </summary>
    private ManagedVtable Patch<T>(string contract, nint* block, T target, ReadOnlySpan<nint> slots, ReadOnlySpan<uint> hashes,
                                   int reservedWords)
        where T : class
    {
        var entry = _live[(nint)block];
        if (Differs(contract, entry.Hashes, hashes) is { } why)
            throw new InvalidOperationException(why);
        if (entry.ReservedWords != reservedWords)
            throw new InvalidOperationException(
                $"{entry.Key}: the new build reserves {reservedWords} word(s) after the vptr and native holds a block with {entry.ReservedWords}");

        slots.CopyTo(new Span<nint>((nint*)block[0], slots.Length));
        ManagedVtable.Header(block)[0] = GCHandle<T>.ToIntPtr(new GCHandle<T>(target));
        entry.Attached = true;
        return new ManagedVtable(this, contract, block, slots.Length, reservedWords);
    }

    /// <summary>
    /// plan_managed_vtable_reload.md L5, run BEFORE anything is unloaded: every
    /// contract native holds must be in <paramref name="next"/> with exactly the
    /// hashes it was registered with. Null when the reload may go ahead, else
    /// the first difference, naming the contract and slot. Changes nothing.
    /// </summary>
    public string? CheckReload(IReadOnlyDictionary<string, uint[]> next)
    {
        // L6: a reload unloads the code of every frame on the stack.
        if (Depth != 0)
            return $"a reload was asked for from inside a managed call (depth {Depth}); " +
                   "reloads are requested from native, at a frame boundary";

        foreach (var entry in _live.Values.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!next.TryGetValue(entry.Contract, out var hashes))
                return $"{entry.Contract}: the new build does not implement it, and native holds it";
            if (Differs(entry.Contract, entry.Hashes, hashes) is { } why)
                return why;
        }
        return null;
    }

    /// <summary>The first difference between the registered and the new hashes, or null.</summary>
    private static string? Differs(string contract, ReadOnlySpan<uint> held, ReadOnlySpan<uint> next)
    {
        int shared = Math.Min(held.Length, next.Length);
        for (int i = 0; i < shared; i++)
        {
            if (held[i] != next[i])
                return $"{contract} slot {i}: the new build's hash 0x{next[i]:x8} is not the registered 0x{held[i]:x8}";
        }
        return held.Length == next.Length
            ? null
            : $"{contract}: the new build has {next.Length} slot(s) and native holds {held.Length}";
    }

    /// <summary>
    /// Before the side's load context unloads (L4): frees every target handle,
    /// so none pins the outgoing context, and points every slot at the trap
    /// table. The blocks stay, at the addresses native holds. Returns how many.
    /// </summary>
    public int Detach()
    {
        int detached = 0;
        foreach (var (address, entry) in _live)
        {
            if (!entry.Attached)
                continue;

            var table = (nint*)((nint*)address)[0];
            for (int i = 0; i < entry.Entries; i++)
                table[i] = ManagedVtableTraps.Address(i);

            nint* header = ManagedVtable.Header((void*)address);
            nint handle = header[0];
            header[0] = 0;
            if (handle != 0)
                GCHandle.FromIntPtr(handle).Free();

            entry.Attached = false;
            detached++;
        }
        return detached;
    }

    /// <summary>A trapped slot was called: names the contract and slot to the trap handler.</summary>
    internal void Trapped(nint block, int slot)
        => _onTrap(_live.TryGetValue(block, out var entry) ? entry.Contract : "an unregistered block", slot);

    /// <summary>Whether <paramref name="block"/> is a block this registry holds and has not freed.</summary>
    public bool IsLive(nint block) => _live.ContainsKey(block);

    /// <summary>The slot hashes the live block for <paramref name="contract"/> was built with, or null.</summary>
    public uint[]? HashesOf(string contract)
        => _byKey.TryGetValue(contract, out var block) ? _live[block].Hashes : null;

    /// <summary>
    /// Frees the target's handle and the block. False, touching nothing, for a
    /// block this registry does not hold (already freed, or never its own).
    /// </summary>
    public bool Free(nint block)
    {
        if (!_live.Remove(block, out var entry))
            return false;

        _byKey.Remove(entry.Key);

        nint* header = ManagedVtable.Header((void*)block);
        nint handle = header[0];
        header[0] = 0;
        header[1] = 0;
        if (handle != 0)
            GCHandle.FromIntPtr(handle).Free();

        NativeMemory.Free((void*)block);
        Freed++;
        return true;
    }

    /// <summary>
    /// Frees blocks a Valve caller holds (<c>ImplementedByManaged</c>'s
    /// ValveLayout tables, which have no Release slot because vphysics never
    /// releases its handlers) once native has let go of the side: when
    /// <paramref name="root"/> has been released, which native does at the
    /// final unload and never across a reload. Until then each is kept, to be
    /// detached and re-patched with the root. Says how many it freed.
    /// </summary>
    public int ReleaseValveHeld(ManagedVtable? root, params ManagedVtable?[] blocks)
    {
        if (root is null || !root.Released)
            return 0;

        int freed = 0;
        foreach (ManagedVtable? block in blocks)
        {
            if (block is not null && block.Release())
                freed++;
        }
        return freed;
    }

    /// <summary>Frees every block still live, and says how many.</summary>
    public int FreeRemaining()
    {
        int freed = 0;
        foreach (nint block in _live.Keys.ToArray())
        {
            if (Free(block))
                freed++;
        }
        return freed;
    }

    /// <summary>Frees what is left and the registry's own handle.</summary>
    public void Dispose()
    {
        FreeRemaining();
        if (_self.IsAllocated)
            _self.Dispose();
    }
}

/// <summary>
/// The trap table (plan_managed_vtable_reload.md L4): one native-callable stub
/// per slot index, which a detached block's slots point at for the length of a
/// reload. Each finds its block's registry through its header and reports the
/// contract and its own index. The stubs never return normally from the
/// default handler, so one signature serves every slot's.
/// </summary>
public static unsafe class ManagedVtableTraps
{
    /// <summary>Slot indices the table covers; every managed-built layout must fit (a fact checks).</summary>
    public const int Count = 64;

    /// <summary>The stub for <paramref name="slot"/>.</summary>
    public static nint Address(int slot) => slot switch
    {
        0 => (nint)(delegate* unmanaged<void*, void>)&T00, 1 => (nint)(delegate* unmanaged<void*, void>)&T01,
        2 => (nint)(delegate* unmanaged<void*, void>)&T02, 3 => (nint)(delegate* unmanaged<void*, void>)&T03,
        4 => (nint)(delegate* unmanaged<void*, void>)&T04, 5 => (nint)(delegate* unmanaged<void*, void>)&T05,
        6 => (nint)(delegate* unmanaged<void*, void>)&T06, 7 => (nint)(delegate* unmanaged<void*, void>)&T07,
        8 => (nint)(delegate* unmanaged<void*, void>)&T08, 9 => (nint)(delegate* unmanaged<void*, void>)&T09,
        10 => (nint)(delegate* unmanaged<void*, void>)&T10, 11 => (nint)(delegate* unmanaged<void*, void>)&T11,
        12 => (nint)(delegate* unmanaged<void*, void>)&T12, 13 => (nint)(delegate* unmanaged<void*, void>)&T13,
        14 => (nint)(delegate* unmanaged<void*, void>)&T14, 15 => (nint)(delegate* unmanaged<void*, void>)&T15,
        16 => (nint)(delegate* unmanaged<void*, void>)&T16, 17 => (nint)(delegate* unmanaged<void*, void>)&T17,
        18 => (nint)(delegate* unmanaged<void*, void>)&T18, 19 => (nint)(delegate* unmanaged<void*, void>)&T19,
        20 => (nint)(delegate* unmanaged<void*, void>)&T20, 21 => (nint)(delegate* unmanaged<void*, void>)&T21,
        22 => (nint)(delegate* unmanaged<void*, void>)&T22, 23 => (nint)(delegate* unmanaged<void*, void>)&T23,
        24 => (nint)(delegate* unmanaged<void*, void>)&T24, 25 => (nint)(delegate* unmanaged<void*, void>)&T25,
        26 => (nint)(delegate* unmanaged<void*, void>)&T26, 27 => (nint)(delegate* unmanaged<void*, void>)&T27,
        28 => (nint)(delegate* unmanaged<void*, void>)&T28, 29 => (nint)(delegate* unmanaged<void*, void>)&T29,
        30 => (nint)(delegate* unmanaged<void*, void>)&T30, 31 => (nint)(delegate* unmanaged<void*, void>)&T31,
        32 => (nint)(delegate* unmanaged<void*, void>)&T32, 33 => (nint)(delegate* unmanaged<void*, void>)&T33,
        34 => (nint)(delegate* unmanaged<void*, void>)&T34, 35 => (nint)(delegate* unmanaged<void*, void>)&T35,
        36 => (nint)(delegate* unmanaged<void*, void>)&T36, 37 => (nint)(delegate* unmanaged<void*, void>)&T37,
        38 => (nint)(delegate* unmanaged<void*, void>)&T38, 39 => (nint)(delegate* unmanaged<void*, void>)&T39,
        40 => (nint)(delegate* unmanaged<void*, void>)&T40, 41 => (nint)(delegate* unmanaged<void*, void>)&T41,
        42 => (nint)(delegate* unmanaged<void*, void>)&T42, 43 => (nint)(delegate* unmanaged<void*, void>)&T43,
        44 => (nint)(delegate* unmanaged<void*, void>)&T44, 45 => (nint)(delegate* unmanaged<void*, void>)&T45,
        46 => (nint)(delegate* unmanaged<void*, void>)&T46, 47 => (nint)(delegate* unmanaged<void*, void>)&T47,
        48 => (nint)(delegate* unmanaged<void*, void>)&T48, 49 => (nint)(delegate* unmanaged<void*, void>)&T49,
        50 => (nint)(delegate* unmanaged<void*, void>)&T50, 51 => (nint)(delegate* unmanaged<void*, void>)&T51,
        52 => (nint)(delegate* unmanaged<void*, void>)&T52, 53 => (nint)(delegate* unmanaged<void*, void>)&T53,
        54 => (nint)(delegate* unmanaged<void*, void>)&T54, 55 => (nint)(delegate* unmanaged<void*, void>)&T55,
        56 => (nint)(delegate* unmanaged<void*, void>)&T56, 57 => (nint)(delegate* unmanaged<void*, void>)&T57,
        58 => (nint)(delegate* unmanaged<void*, void>)&T58, 59 => (nint)(delegate* unmanaged<void*, void>)&T59,
        60 => (nint)(delegate* unmanaged<void*, void>)&T60, 61 => (nint)(delegate* unmanaged<void*, void>)&T61,
        62 => (nint)(delegate* unmanaged<void*, void>)&T62, 63 => (nint)(delegate* unmanaged<void*, void>)&T63,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot,
                 $"the trap table covers {Count} slots; a managed-built layout outgrew it"),
    };

    private static void Trapped(void* self, int slot)
    {
        ManagedVtables? registry = null;
        try
        {
            nint owner = ManagedVtable.Header(self)[1];
            if (owner == 0)
                Environment.FailFast($"[SourceSharp] trapped slot {slot} on a block with no registry");
            registry = GCHandle<ManagedVtables>.FromIntPtr(owner).Target;
            registry.Trapped((nint)self, slot);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(registry?.Log, $"trapped slot {slot}", e);
        }
    }

    [UnmanagedCallersOnly] private static void T00(void* self) => Trapped(self, 0);
    [UnmanagedCallersOnly] private static void T01(void* self) => Trapped(self, 1);
    [UnmanagedCallersOnly] private static void T02(void* self) => Trapped(self, 2);
    [UnmanagedCallersOnly] private static void T03(void* self) => Trapped(self, 3);
    [UnmanagedCallersOnly] private static void T04(void* self) => Trapped(self, 4);
    [UnmanagedCallersOnly] private static void T05(void* self) => Trapped(self, 5);
    [UnmanagedCallersOnly] private static void T06(void* self) => Trapped(self, 6);
    [UnmanagedCallersOnly] private static void T07(void* self) => Trapped(self, 7);
    [UnmanagedCallersOnly] private static void T08(void* self) => Trapped(self, 8);
    [UnmanagedCallersOnly] private static void T09(void* self) => Trapped(self, 9);
    [UnmanagedCallersOnly] private static void T10(void* self) => Trapped(self, 10);
    [UnmanagedCallersOnly] private static void T11(void* self) => Trapped(self, 11);
    [UnmanagedCallersOnly] private static void T12(void* self) => Trapped(self, 12);
    [UnmanagedCallersOnly] private static void T13(void* self) => Trapped(self, 13);
    [UnmanagedCallersOnly] private static void T14(void* self) => Trapped(self, 14);
    [UnmanagedCallersOnly] private static void T15(void* self) => Trapped(self, 15);
    [UnmanagedCallersOnly] private static void T16(void* self) => Trapped(self, 16);
    [UnmanagedCallersOnly] private static void T17(void* self) => Trapped(self, 17);
    [UnmanagedCallersOnly] private static void T18(void* self) => Trapped(self, 18);
    [UnmanagedCallersOnly] private static void T19(void* self) => Trapped(self, 19);
    [UnmanagedCallersOnly] private static void T20(void* self) => Trapped(self, 20);
    [UnmanagedCallersOnly] private static void T21(void* self) => Trapped(self, 21);
    [UnmanagedCallersOnly] private static void T22(void* self) => Trapped(self, 22);
    [UnmanagedCallersOnly] private static void T23(void* self) => Trapped(self, 23);
    [UnmanagedCallersOnly] private static void T24(void* self) => Trapped(self, 24);
    [UnmanagedCallersOnly] private static void T25(void* self) => Trapped(self, 25);
    [UnmanagedCallersOnly] private static void T26(void* self) => Trapped(self, 26);
    [UnmanagedCallersOnly] private static void T27(void* self) => Trapped(self, 27);
    [UnmanagedCallersOnly] private static void T28(void* self) => Trapped(self, 28);
    [UnmanagedCallersOnly] private static void T29(void* self) => Trapped(self, 29);
    [UnmanagedCallersOnly] private static void T30(void* self) => Trapped(self, 30);
    [UnmanagedCallersOnly] private static void T31(void* self) => Trapped(self, 31);
    [UnmanagedCallersOnly] private static void T32(void* self) => Trapped(self, 32);
    [UnmanagedCallersOnly] private static void T33(void* self) => Trapped(self, 33);
    [UnmanagedCallersOnly] private static void T34(void* self) => Trapped(self, 34);
    [UnmanagedCallersOnly] private static void T35(void* self) => Trapped(self, 35);
    [UnmanagedCallersOnly] private static void T36(void* self) => Trapped(self, 36);
    [UnmanagedCallersOnly] private static void T37(void* self) => Trapped(self, 37);
    [UnmanagedCallersOnly] private static void T38(void* self) => Trapped(self, 38);
    [UnmanagedCallersOnly] private static void T39(void* self) => Trapped(self, 39);
    [UnmanagedCallersOnly] private static void T40(void* self) => Trapped(self, 40);
    [UnmanagedCallersOnly] private static void T41(void* self) => Trapped(self, 41);
    [UnmanagedCallersOnly] private static void T42(void* self) => Trapped(self, 42);
    [UnmanagedCallersOnly] private static void T43(void* self) => Trapped(self, 43);
    [UnmanagedCallersOnly] private static void T44(void* self) => Trapped(self, 44);
    [UnmanagedCallersOnly] private static void T45(void* self) => Trapped(self, 45);
    [UnmanagedCallersOnly] private static void T46(void* self) => Trapped(self, 46);
    [UnmanagedCallersOnly] private static void T47(void* self) => Trapped(self, 47);
    [UnmanagedCallersOnly] private static void T48(void* self) => Trapped(self, 48);
    [UnmanagedCallersOnly] private static void T49(void* self) => Trapped(self, 49);
    [UnmanagedCallersOnly] private static void T50(void* self) => Trapped(self, 50);
    [UnmanagedCallersOnly] private static void T51(void* self) => Trapped(self, 51);
    [UnmanagedCallersOnly] private static void T52(void* self) => Trapped(self, 52);
    [UnmanagedCallersOnly] private static void T53(void* self) => Trapped(self, 53);
    [UnmanagedCallersOnly] private static void T54(void* self) => Trapped(self, 54);
    [UnmanagedCallersOnly] private static void T55(void* self) => Trapped(self, 55);
    [UnmanagedCallersOnly] private static void T56(void* self) => Trapped(self, 56);
    [UnmanagedCallersOnly] private static void T57(void* self) => Trapped(self, 57);
    [UnmanagedCallersOnly] private static void T58(void* self) => Trapped(self, 58);
    [UnmanagedCallersOnly] private static void T59(void* self) => Trapped(self, 59);
    [UnmanagedCallersOnly] private static void T60(void* self) => Trapped(self, 60);
    [UnmanagedCallersOnly] private static void T61(void* self) => Trapped(self, 61);
    [UnmanagedCallersOnly] private static void T62(void* self) => Trapped(self, 62);
    [UnmanagedCallersOnly] private static void T63(void* self) => Trapped(self, 63);
}
