//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The storage and the indexes both sides' entity directories are —
/// <c>plan_entity.md</c> §3.5, "the <c>EntityDirectory</c>, one per side
/// module".
///
/// <para>
/// <b>WHY THIS EXISTS.</b> §3.5 says <i>one per side module</i>, and the two
/// sides duly have one each. What it does not say is that each side gets its
/// own copy of the SHAPE, and until lane <c>sub-entitylist</c> they did: the
/// server's <see cref="EntityDirectory"/> and the client's
/// <c>ClientEntityDirectory</c> both carried a handle array, an entity array, a
/// dense list with its inverse, and a name index, written twice. Lane
/// <c>entlist-client</c> wrote the ask down — "the fold is a retype and not a
/// rewrite" — and it was orphaned when both lanes merged. This is it.
/// </para>
///
/// <para>
/// <b>WHAT IS PER SIDE AND WHAT IS NOT.</b> The ENTITY TYPE is per side and
/// there is no shared base for it: the server binds <see cref="ScriptedEntity"/>
/// and the client binds <c>ClientStandaloneEntity</c>. That is the whole reason
/// this is generic rather than a plain base class, and it is why the two things
/// the storage needs to know about an entity — its classname and the engine
/// index it carries — arrive through <see cref="ClassNameOf"/> and
/// <see cref="EntityIndexOf"/> rather than through an interface neither type
/// implements.
/// </para>
///
/// <para>
/// Everything else the server's directory does — the think heap, the touch link
/// table, the I/O queue and its connections, removal marks, the engine-invisible
/// allocator — is the SERVER's and stays there. This class is the
/// <c>CBaseEntityList</c> replacement's storage half and nothing more.
/// </para>
///
/// <para>
/// <b>THE TWO HARD-WON RULES <c>entlist-client</c> ASKED TO SURVIVE THE FOLD,
/// AND BOTH DO.</b> They are stated here because each was paid for once and a
/// fold is exactly where a rule gets quietly dropped:
/// </para>
///
/// <para>
/// 1. <see cref="_byIndex"/> IS AN ARRAY AND HOLDS THE HANDLE. The registry it
/// replaced erased an index by walking every live entity looking for the object
/// by reference — O(live) per removal, paid once per gib on a map full of them.
/// An engine index is an entry-sized number by construction, so the array costs
/// the same as the rest of this class and the erase is one store. It holds the
/// HANDLE and not the entity, so a stale index cannot answer: the handle is
/// re-resolved through the same compare every other lookup uses.
/// </para>
///
/// <para>
/// 2. <see cref="Detach"/> CLEARS AN INDEX ENTRY ONLY IF IT STILL NAMES THE
/// LEAVING HANDLE. Two entities can carry one engine index at different entries
/// — the index is asked of the engine once, at create, and cached — and an
/// unguarded clear makes a live entity unreachable by index, which for a trace
/// means the shot passes through it.
/// </para>
///
/// <para>
/// <b>AN EMPTY SLOT IS <see cref="EntityHandle.None"/>.</b> Every array of
/// handles here is filled with it in the constructor and every empty slot is
/// written back to it. Since 2026-10-05 a zeroed handle array already reads as
/// <c>None</c> (the handle is stored inverted), so the fills are belt and
/// braces; before then a zeroed array read as "entry 0 is occupied by serial
/// 0", a real binding and not an empty one.
/// </para>
/// </summary>
/// <typeparam name="TEntity">
/// The side's entity type. There is deliberately no constraint beyond
/// <c>class</c>: the two sides share no base, and inventing one to satisfy this
/// file would put a type in the shared assembly that neither side's hierarchy
/// wants.
/// </typeparam>
public abstract class EntityDirectoryCore<TEntity>
    where TEntity : class
{
    /// <summary>
    /// <c>NUM_ENT_ENTRIES</c> — <c>public/const.h</c>: <c>1 &lt;&lt;
    /// (MAX_EDICT_BITS + 2)</c>, 8,192. The whole handle space, edicts and the
    /// engine-invisible range together.
    /// </summary>
    public const int EntryCount = EntityHandle.EntryCount;

    /// <summary>
    /// <c>ENT_ENTRY_MASK</c> — <c>(1 &lt;&lt; NUM_SERIAL_NUM_BITS) - 1</c>,
    /// which is SIXTEEN bits and not the thirteen an entry needs. Stock's, not
    /// a simplification of stock's; see <see cref="EntityDirectory"/>'s remarks.
    /// </summary>
    public const uint EntryMask = EntityHandle.EntryMask;

    /// <summary>
    /// <c>CBaseHandle::GetEntryIndex</c>, transliterated including its
    /// deliberate answer for the invalid handle.
    ///
    /// <para>
    /// The invalid handle answers <c>EntryCount - 1</c>, which is a perfectly
    /// bindable entry — so a caller that has not already refused the invalid
    /// handle would address entry 8191 and get whatever lives there. Every
    /// caller below refuses it first, by handle and not by entry.
    /// </para>
    /// </summary>
    public static int EntryIndex(EntityHandle handle) => handle.EntryIndex;

    private readonly EntityHandle[] _handles = new EntityHandle[EntryCount];
    private readonly TEntity?[] _entities = new TEntity?[EntryCount];

    // When each entry got the handle it holds, as a running count: the order
    // entities were created in, which is the order the native standalone list
    // kept (push_back at creation, erase in place). FirstByName's tie-break.
    private readonly long[] _bindOrder = new long[EntryCount];
    private long _binds;

    /// <summary>
    /// The occupied entries, densely packed, so iteration costs the number of
    /// live entities and not 8,192. <see cref="_denseOf"/> is its inverse, so a
    /// removal is a swap with the tail rather than a search.
    /// </summary>
    private readonly List<int> _dense = new();
    private readonly int[] _denseOf = new int[EntryCount];

    /// <summary>
    /// The same entities by ENGINE ENTITY INDEX — see rule 1 in the type's
    /// remarks. NULL on a side that does not track one, so a directory whose
    /// entry already IS the engine index does not pay 32KB for an identity map.
    /// </summary>
    private readonly EntityHandle[]? _byIndex;

    /// <summary>Handles by classname — find-by-class without a walk.</summary>
    private readonly Dictionary<string, List<EntityHandle>> _byClassName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Handles by targetname, for the entities that have one.</summary>
    private readonly Dictionary<string, List<EntityHandle>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What each named handle is currently indexed under, so a rename can undo it.</summary>
    private readonly Dictionary<EntityHandle, string> _nameOf = new();

    protected EntityDirectoryCore()
    {
        Array.Fill(_handles, EntityHandle.None);
        Array.Fill(_denseOf, -1);

        if (TracksEntityIndex)
        {
            _byIndex = new EntityHandle[EntryCount];
            Array.Fill(_byIndex, EntityHandle.None);
        }
    }

    //=========================================================================
    // WHAT THE SIDE SUPPLIES
    //=========================================================================

    /// <summary>
    /// The classname the entity was registered as, for the classname index.
    /// </summary>
    protected abstract string? ClassNameOf(TEntity entity);

    /// <summary>
    /// The ENGINE entity index the entity carries, or -1 when it carries none.
    ///
    /// <para>
    /// Asked of the entity rather than remembered beside it, because
    /// <see cref="Detach"/> has to undo an index store on a path where the
    /// caller passed no index — an eviction, a level change — and the entity is
    /// the only thing that still knows which one it claimed. Answering -1 is
    /// how a side opts out; see <see cref="TracksEntityIndex"/>.
    /// </para>
    /// </summary>
    protected virtual int EntityIndexOf(TEntity entity) => -1;

    /// <summary>
    /// Whether this side keeps an index → handle table at all.
    ///
    /// <para>
    /// FALSE ON THE SERVER, and that is a statement about the two sides rather
    /// than a saving. A server shell's entry IS its edict index — the shell
    /// binds the entry the engine gave it — so <c>ByIndex(i)</c> would be
    /// <c>Resolve(HandleOf(i))</c>, an identity map costing 32KB. The client's
    /// shells cache an index they were told once at create, so there the table
    /// is real.
    /// </para>
    /// </summary>
    protected virtual bool TracksEntityIndex => false;

    /// <summary>
    /// An entry is about to be taken from <paramref name="evicted"/> by a
    /// different handle. The side cancels whatever it booked under it.
    ///
    /// <para>
    /// The evicted handle can never be unbound by its owner afterwards, so
    /// anything keyed on it that is not dropped here answers for the entity
    /// that replaced it.
    /// </para>
    /// </summary>
    protected virtual void OnEvicting(EntityHandle evicted) { }

    /// <summary>
    /// <paramref name="handle"/>'s entry is about to be emptied, on any path —
    /// an unbind, an eviction, a level change. Called BEFORE the storage is
    /// cleared, so the side can still resolve the entity.
    /// </summary>
    protected virtual void OnDetaching(EntityHandle handle, int entry) { }

    /// <summary>
    /// Whether <paramref name="handle"/> may be named. Refuses the invalid
    /// handle by default; a side that requires the handle to be BOUND first
    /// tightens it.
    /// </summary>
    protected virtual bool CanName(EntityHandle handle) => handle != EntityHandle.None;

    //=========================================================================
    // BIND / UNBIND / RESOLVE
    //=========================================================================

    /// <summary>How many entities are bound.</summary>
    public int Count => _dense.Count;

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
    ///
    /// <para>
    /// AN ENTRY HELD BY A DIFFERENT HANDLE IS EVICTED. The engine reuses an
    /// entry only after bumping the serial, so the occupant of an entry a NEW
    /// handle names is by construction dead; "last bind wins" is what recovers
    /// when a removal was missed, and refusing instead would leave the entity
    /// that is really there with no managed half at all.
    /// </para>
    /// </summary>
    /// <param name="entityIndex">
    /// The engine index to file this entity under, or -1 for none. Passed in
    /// rather than asked of the handle because asking costs an ABI crossing.
    /// Ignored entirely when <see cref="TracksEntityIndex"/> is false.
    /// </param>
    protected bool BindCore(EntityHandle handle, int entityIndex, TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (handle == EntityHandle.None)
            return false;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount)
            return false;

        EntityHandle sitting = _handles[entry];

        if (sitting != EntityHandle.None && sitting != handle)
        {
            OnEvicting(sitting);
            Detach(entry, sitting);
        }

        if (_denseOf[entry] < 0)
        {
            _denseOf[entry] = _dense.Count;
            _dense.Add(entry);
        }

        if (sitting != handle)
            _bindOrder[entry] = ++_binds;

        _handles[entry] = handle;
        _entities[entry] = entity;

        Index(_byClassName, ClassNameOf(entity), handle);

        if (_byIndex is not null && (uint)entityIndex < EntryCount)
            _byIndex[entityIndex] = handle;

        return true;
    }

    /// <summary>
    /// Empties <paramref name="handle"/>'s entry, IF that entry still holds
    /// this handle, and answers the entity that was there.
    ///
    /// <para>
    /// The "if it still holds this handle" is not belt and braces: an entry
    /// that has been rebound belongs to its new occupant, and unbinding it on a
    /// stale handle would evict a live entity.
    /// </para>
    /// </summary>
    protected bool UnbindCore(EntityHandle handle, out TEntity? leaving)
    {
        leaving = null;

        if (handle == EntityHandle.None)
            return false;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount || _handles[entry] != handle)
            return false;

        leaving = _entities[entry];
        Detach(entry, handle);
        return true;
    }

    /// <summary>
    /// The entity <paramref name="handle"/> names, or null — one bounds check
    /// and two array reads, with the serial checked by the handle compare.
    /// </summary>
    public TEntity? Resolve(EntityHandle handle)
    {
        if (handle == EntityHandle.None)
            return null;

        int entry = EntryIndex(handle);
        if ((uint)entry >= EntryCount)
            return null;

        return _handles[entry] == handle ? _entities[entry] : null;
    }

    /// <summary>
    /// The entity at an ENGINE ENTITY INDEX, or null if that index is not one
    /// of ours — which for a trace means it hit the world, a brush, or an
    /// entity this side does not own.
    ///
    /// <para>
    /// Always null on a side that keeps no index table
    /// (<see cref="TracksEntityIndex"/>), rather than silently answering about
    /// the ENTRY of the same number: the two coincide on the server today and a
    /// caller that relied on the coincidence would be right by accident.
    /// </para>
    /// </summary>
    public TEntity? ByIndex(int entityIndex)
    {
        if (_byIndex is null || (uint)entityIndex >= EntryCount)
            return null;

        return Resolve(_byIndex[entityIndex]);
    }

    /// <summary>The handle an entry currently holds, or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle HandleOf(int entry)
        => (uint)entry < EntryCount ? _handles[entry] : EntityHandle.None;

    //=========================================================================
    // ITERATE
    //=========================================================================

    /// <summary>
    /// Every bound entity, in no defined order.
    ///
    /// <para>
    /// LAZY, over the dense list. A caller that intends to CHANGE the directory
    /// while it walks wants <see cref="Snapshot"/> instead, and the difference
    /// is named rather than paid for by everyone.
    /// </para>
    /// </summary>
    public IEnumerable<TEntity> Entities
    {
        get
        {
            foreach (int entry in _dense)
            {
                var entity = _entities[entry];
                if (entity is not null)
                    yield return entity;
            }
        }
    }

    /// <summary>Every bound entity with the handle it is bound under.</summary>
    public IEnumerable<(EntityHandle Handle, TEntity Entity)> Bindings
    {
        get
        {
            foreach (int entry in _dense)
            {
                var entity = _entities[entry];
                if (entity is not null)
                    yield return (_handles[entry], entity);
            }
        }
    }

    /// <summary>
    /// The live entities copied out, for a caller that may create or remove one
    /// while it walks.
    ///
    /// <para>
    /// The obvious such caller is a diagnostic that asks each entity a question,
    /// and asking an entity a question is allowed to make one — a model
    /// resolving, a class deciding it is done. Enumerating the directory while
    /// it changes throws, and a debugging command that crashes the game is
    /// worse than no command.
    /// </para>
    /// </summary>
    public IReadOnlyList<TEntity> Snapshot()
    {
        var into = new List<TEntity>(_dense.Count);

        foreach (int entry in _dense)
        {
            var entity = _entities[entry];
            if (entity is not null)
                into.Add(entity);
        }

        return into;
    }

    //=========================================================================
    // FIND BY CLASS
    //=========================================================================

    /// <summary>
    /// Every entity whose classname matches <paramref name="query"/>, appended
    /// to <paramref name="into"/> as handles. Answers how many were added.
    ///
    /// <para>
    /// <b>THE EXACT CASE IS A DICTIONARY HIT AND THE WILDCARD CASE IS A WALK OF
    /// THE ENTITIES, and the asymmetry is deliberate.</b> An exact query is
    /// case-insensitive equality, which is exactly what the classname bucket's
    /// comparer already is, so the two answer the same set. A wildcard query is
    /// not: <c>EntityNameMatch.Match</c> says a query beginning '*' matches an
    /// entity with NO classname at all (<c>EntityIo.cs:316</c>), and an entity
    /// with no classname is in no bucket. Walking the buckets for '*' would
    /// therefore quietly drop it, which is the shape of bug this file exists to
    /// stop rather than to introduce.
    /// </para>
    /// </summary>
    public int FindByClassName(string? query, List<EntityHandle> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (string.IsNullOrEmpty(query))
            return 0;

        int found = 0;

        if (query.IndexOf('*') < 0)
        {
            if (_byClassName.TryGetValue(query, out var exact))
            {
                // OVER A COPY: a caller is entitled to act on what it is handed,
                // and that can edit this very bucket.
                foreach (EntityHandle handle in exact.ToArray())
                {
                    if (Resolve(handle) is not null)
                    {
                        into.Add(handle);
                        found++;
                    }
                }
            }

            return found;
        }

        foreach (int entry in _dense)
        {
            var entity = _entities[entry];

            if (entity is null || !EntityNameMatch.Match(query, ClassNameOf(entity)))
                continue;

            into.Add(_handles[entry]);
            found++;
        }

        return found;
    }

    /// <summary>
    /// Every live entity whose classname matches <paramref name="query"/>,
    /// which may end in '*'. <see cref="FindByClassName"/>'s answer as objects.
    /// </summary>
    public IEnumerable<TEntity> OfClass(string? query)
    {
        var handles = new List<EntityHandle>();
        FindByClassName(query, handles);

        foreach (EntityHandle handle in handles)
        {
            if (Resolve(handle) is { } entity)
                yield return entity;
        }
    }

    /// <summary>
    /// The classnames this directory currently holds at least one entity of.
    ///
    /// <para>
    /// A bucket is deleted when it empties, so this is "what is here now" and
    /// not "what has ever been here".
    /// </para>
    /// </summary>
    public IReadOnlyCollection<string> ClassNames => _byClassName.Keys;

    //=========================================================================
    // FIND BY NAME
    //=========================================================================

    /// <summary>
    /// Records the <c>targetname</c> a map gave an entity, or clears it when
    /// <paramref name="name"/> is null or empty. An entity may be renamed; the
    /// old index entry goes with it.
    /// </summary>
    public void SetName(EntityHandle handle, string? name)
    {
        if (!CanName(handle))
            return;

        DropName(handle);

        if (string.IsNullOrEmpty(name))
            return;

        _nameOf[handle] = name;
        Index(_byName, name, handle);
    }

    /// <summary>The name this directory has for an entity, or the empty string.</summary>
    public string NameOf(EntityHandle handle)
        => _nameOf.TryGetValue(handle, out string? name) ? name : string.Empty;

    /// <summary>How many entities carry a name.</summary>
    public int NamedCount => _nameOf.Count;

    /// <summary>
    /// Takes <paramref name="handle"/> out of the name index, whatever it was
    /// called. Idempotent, and the one way a name leaves.
    /// </summary>
    protected void DropName(EntityHandle handle)
    {
        if (!_nameOf.TryGetValue(handle, out string? previous))
            return;

        Unindex(_byName, previous, handle);
        _nameOf.Remove(handle);
    }

    /// <summary>
    /// Every live entity answering to <paramref name="query"/>, appended to
    /// <paramref name="into"/> as handles.
    ///
    /// <para>
    /// The exact-name case is a dictionary hit; a query carrying '*' is a walk
    /// of the NAMED entities, because a prefix is not a key. Stock walks the
    /// whole entity list for BOTH, so the wildcard case is no worse and the
    /// common case is far better.
    /// </para>
    ///
    /// <para>
    /// An empty query is not a wildcard here, deliberately, and stock agrees:
    /// <c>FindEntityByName</c>'s first line refuses a null or empty name. A
    /// connection whose target nobody filled in must not select every unnamed
    /// entity in the map — which is also why the wildcard arm walks the NAME
    /// index and not the entities, the opposite of
    /// <see cref="FindByClassName"/>.
    /// </para>
    /// </summary>
    public int FindByName(string? query, List<EntityHandle> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (string.IsNullOrEmpty(query))
            return 0;

        int found = 0;

        if (query.IndexOf('*') < 0)
        {
            if (_byName.TryGetValue(query, out var exact))
            {
                foreach (EntityHandle handle in exact.ToArray())
                {
                    if (Resolve(handle) is not null)
                    {
                        into.Add(handle);
                        found++;
                    }
                }
            }

            return found;
        }

        foreach (var (name, bucket) in _byName.ToArray())
        {
            if (!EntityNameMatch.Match(query, name))
                continue;

            foreach (EntityHandle handle in bucket.ToArray())
            {
                if (Resolve(handle) is not null)
                {
                    into.Add(handle);
                    found++;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Every live entity answering to <paramref name="query"/>, written into a
    /// pooled buffer (grown through <see cref="System.Buffers.ArrayPool{T}"/>)
    /// rather than a new list: the allocation-free half of name lookup. The
    /// buffer may be replaced by a larger one; the caller returns whichever it
    /// ends up holding. Order is the index's, as <see cref="FindByName"/>.
    /// </summary>
    protected int CollectNamed(string? query, ref EntityHandle[] buffer)
    {
        if (string.IsNullOrEmpty(query))
            return 0;

        int count = 0;

        if (query.IndexOf('*') < 0)
        {
            if (_byName.TryGetValue(query, out var exact))
            {
                foreach (EntityHandle handle in exact)
                {
                    if (Resolve(handle) is not null)
                        Append(ref buffer, ref count, handle);
                }
            }

            return count;
        }

        foreach (var pair in _byName)
        {
            if (!EntityNameMatch.Match(query, pair.Key))
                continue;

            foreach (EntityHandle handle in pair.Value)
            {
                if (Resolve(handle) is not null)
                    Append(ref buffer, ref count, handle);
            }
        }

        return count;
    }

    private static void Append(ref EntityHandle[] buffer, ref int count, EntityHandle handle)
    {
        if (count == buffer.Length)
        {
            EntityHandle[] larger = System.Buffers.ArrayPool<EntityHandle>.Shared.Rent(Math.Max(16, buffer.Length * 2));
            Array.Copy(buffer, larger, count);
            System.Buffers.ArrayPool<EntityHandle>.Shared.Return(buffer);
            buffer = larger;
        }

        buffer[count++] = handle;
    }

    /// <summary>
    /// When <paramref name="handle"/> was bound, as a running count (earlier is
    /// smaller), or <see cref="long.MaxValue"/> for a handle this directory does
    /// not hold.
    /// </summary>
    protected long BindOrderOf(EntityHandle handle)
    {
        int entry = EntryIndex(handle);

        return (uint)entry < EntryCount && _handles[entry] == handle ? _bindOrder[entry] : long.MaxValue;
    }

    /// <summary>
    /// Every live entity answering to <paramref name="query"/>, which may end
    /// in '*'. <see cref="FindByName"/>'s answer as objects.
    /// </summary>
    public IEnumerable<TEntity> OfName(string? query)
    {
        var handles = new List<EntityHandle>();
        FindByName(query, handles);

        foreach (EntityHandle handle in handles)
        {
            if (Resolve(handle) is { } entity)
                yield return entity;
        }
    }

    //=========================================================================
    // THE BOOKKEEPING THE MUTATORS SHARE
    //=========================================================================

    /// <summary>
    /// The occupied entries. For a side whose own level-change or removal pass
    /// has to walk them; <b>copy it before mutating</b>, because every unbind
    /// edits it.
    /// </summary>
    protected IReadOnlyList<int> DenseEntries => _dense;

    /// <summary>The entity at an ENTRY, without a handle compare. For a side's own walk.</summary>
    protected TEntity? EntityAt(int entry)
        => (uint)entry < EntryCount ? _entities[entry] : null;

    /// <summary>
    /// Drops every binding and every index, without notifying. A side that has
    /// listeners to tell walks <see cref="DenseEntries"/> and unbinds instead.
    /// </summary>
    protected void ClearCore()
    {
        foreach (int entry in _dense)
        {
            _handles[entry] = EntityHandle.None;
            _entities[entry] = null;
            _denseOf[entry] = -1;
        }

        _dense.Clear();
        _byClassName.Clear();
        _byName.Clear();
        _nameOf.Clear();

        if (_byIndex is not null)
            Array.Fill(_byIndex, EntityHandle.None);
    }

    /// <summary>Clears one entry and every index that named it.</summary>
    private void Detach(int entry, EntityHandle handle)
    {
        OnDetaching(handle, entry);

        TEntity? leaving = _entities[entry];

        if (leaving is not null)
            Unindex(_byClassName, ClassNameOf(leaving), handle);

        DropName(handle);

        // BY THE INDEX THE ENTITY CARRIES, not by a walk. Only if that index
        // still names THIS handle -- rule 2 in the type's remarks: indices are
        // recycled, and clearing one the next occupant has already claimed
        // would make a live entity unreachable by index while it is still
        // bound.
        if (_byIndex is not null && leaving is not null)
        {
            int index = EntityIndexOf(leaving);

            if ((uint)index < EntryCount && _byIndex[index] == handle)
                _byIndex[index] = EntityHandle.None;
        }

        int dense = _denseOf[entry];

        if (dense >= 0)
        {
            int last = _dense.Count - 1;
            int moved = _dense[last];

            _dense[dense] = moved;
            _denseOf[moved] = dense;
            _dense.RemoveAt(last);
            _denseOf[entry] = -1;
        }

        _handles[entry] = EntityHandle.None;
        _entities[entry] = null;
    }

    private static void Index(Dictionary<string, List<EntityHandle>> into, string? key, EntityHandle handle)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (!into.TryGetValue(key, out var bucket))
            into[key] = bucket = new List<EntityHandle>(1);

        if (!bucket.Contains(handle))
            bucket.Add(handle);
    }

    private static void Unindex(Dictionary<string, List<EntityHandle>> from, string? key, EntityHandle handle)
    {
        if (string.IsNullOrEmpty(key) || !from.TryGetValue(key, out var bucket))
            return;

        bucket.Remove(handle);

        // THE EMPTY BUCKET GOES. Left behind, the wildcard walks above would
        // grow monotonically over a session and ClassNames would report classes
        // with nothing in them.
        if (bucket.Count == 0)
            from.Remove(key);
    }
}
