//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CBoneCache + g_StudioBoneCache cluster of the bone_setup.cpp managed port
// (inventory rows 132-135; stock oracle src/public/bone_setup.cpp 82-261 at
// valve-sdk.lock 22288b91, our relocated copy src/shared/native/bone_setup.cpp
// 114-328). CBoneCache is the cached-bone block (index maps + matrices);
// BoneCacheStore is the CDataManager<CBoneCache, bonecacheparams_t, ...>
// singleton g_StudioBoneCache (stock 281) and the four Studio_* entry points
// (stock 283-309) recast as an injected service: SS0001 forbids the static,
// so the store is a per-side instance the consumer owns. Divergences and the
// pending rulings are NC rows in docs/plans/needs-cache.md; code comments
// name the ids.
//
// The CDataManager semantics are mirrored from the fork's pristine
// src/public/tier1/datamanager.h + src/tier1/datamanager.cpp (read-only):
//   - CreateResource  datamanager.h:181-187 — EnsureCapacity(EstimatedSize),
//     CreateHandle, STORAGE_TYPE::CreateResource, StoreResourceInHandle(real
//     Size).
//   - handles         datamanager.cpp:320-326 ToHandle = serial<<16 |
//     (index+1); datamanager.h:252-261 FromHandle truncates to 32 bits,
//     splits serial/index, and requires slot.serial == serial.
//   - free list       datamanager.cpp:276-297 CreateHandle pops the free-list
//     HEAD (FIFO reuse) and links to the LRU tail; empty free list appends a
//     new slot whose serial is 1 (resource_lru_element_t ctor,
//     datamanager.h:106-113).
//   - destroy         datamanager.cpp:131-150 DestroyResource unlinks from
//     the LRU and GetForFreeByIndex (366-388) subtracts the REAL size from
//     used, serial++ (ushort wrap), links to the free-list tail.
//   - eviction        datamanager.cpp:345-364 EnsureCapacity evicts the LRU
//     head while used > target or available < size, stopping on an empty LRU.
//   - LRU touch       datamanager.cpp:308-318 GetResource_NoLock moves the
//     touched node to the LRU tail (stock's lockCount is always 0 here: the
//     four Studio_* callers never lock, so the lock list is not ported —
//     NC-07).
// CThreadFastMutex (stock 281's MUTEX_TYPE) drops per the pattern the other
// managed bone services use (no locks anywhere; the engine calls from the
// game thread): NC-01.

using System;
using System.Collections.Generic;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// Stock's <c>bonecacheparams_t</c> (bone_setup.h:386-391): the four inputs
/// of <c>Studio_CreateBoneCache</c>. A <c>ref struct</c> because it carries
/// the caller's bone matrix span; stock's callers
/// (<c>C_BaseAnimating::GetBoneCache</c>, c_baseanimating.cpp:2384-2401)
/// build it on the stack and hand it over synchronously.
/// </summary>
public readonly ref struct BoneCacheParams
{
    public readonly StudioHdr PStudioHdr;
    public readonly ReadOnlySpan<BoneMatrix3x4> PBoneToWorld;
    public readonly float Curtime;
    public readonly int BoneMask;

    public BoneCacheParams(
        StudioHdr pStudioHdr, ReadOnlySpan<BoneMatrix3x4> pBoneToWorld,
        float curtime, int boneMask)
    {
        PStudioHdr = pStudioHdr;
        PBoneToWorld = pBoneToWorld;
        Curtime = curtime;
        BoneMask = boneMask;
    }
}

/// <summary>
/// Stock's <c>CBoneCache</c> (bone_setup.h:393-437; definitions our 114-248,
/// oracle 82-248 at valve-sdk.lock): one malloc'd block holding a studio→cached index map, its
/// reverse, and the cached bone matrices. The mask gate
/// (<see cref="CreateResource"/>) picks which bones get a slot;
/// <see cref="UpdateBones"/> copies fresh world matrices through the reverse
/// map; the read side answers the engine's cached reads
/// (GetCachedBone / ReadCachedBones / ReadCachedBonePointers).
///
/// <para>
/// Stock packs everything behind the object header at byte offsets
/// (<c>BoneArray()</c> / <c>StudioToCached()</c> / <c>CachedToStudio()</c>,
/// our 253-271). The port keeps ordinary arrays; the block
/// <see cref="Size"/> is computed with stock's formula and retained because
/// the store's used-memory accounting reads it exactly as stock's
/// <c>GetRealSize</c> does (NC-04: allocation shape diverges, the numbers
/// do not).
/// </para>
/// </summary>
public sealed class BoneCache
{
    // sizeof(CBoneCache) at the oracle layout: m_timeValid float(4) + m_boneMask
    // int(4) + m_size uint(4) + m_cachedBoneCount/m_matrixOffset/
    // m_cachedToStudioOffset/m_boneOutOffset 4 x ushort(8) = 20, align 4.
    // m_boneOutOffset is written and read nowhere in stock (NC-06).
    private const int SizeofCBoneCache = 20;

    // sizeof(matrix3x4_t) = 12 floats.
    private const int SizeofMatrix3x4 = 48;

    // Assigned by CreateResource (stock's Init fills the same fields after
    // placement-new; the GC allocates with them here).
    private short[] _studioToCached = [];
    private short[] _cachedToStudio = [];
    private BoneMatrix3x4[] _bones = [];
    private int _numBones;
    private int _cachedBoneCount;


    public float TimeValid;   // stock public m_timeValid (bone_setup.h:424)
    public int BoneMask;      // stock public m_boneMask (bone_setup.h:425);
                              // C_BaseAnimating::GetBoneCache reads it to
                              // decide a recreate (c_baseanimating.cpp:2370)

    public uint Size { get; private set; }   // stock Size() -> m_size, the
                                              // real block size

    private BoneCache(int numBones) => _numBones = numBones;

    /// <summary>
    /// <c>CBoneCache::CreateResource</c> — our 115-141, oracle 82-108 (the
    /// stock <c>Init</c> at our 169-186 folds in here: stock placement-new'd, the GC
    /// allocates with the fields instead). The mask gate skips bones that
    /// are neither the root (index 0, always cached) nor flagged in
    /// <c>boneMask</c>; skipped bones get map entry -1. Stock mallocs one
    /// block sized <c>(sizeof(CBoneCache) + 2*numbones + 2*cached +
    /// 48*cached + 3) &amp; ~3</c>; the port computes the same number for
    /// <see cref="Size"/> and allocates the arrays.
    /// </summary>
    internal static BoneCache CreateResource(in BoneCacheParams parms)
    {
        StudioHdr pHdr = parms.PStudioHdr;
        int numbones = pHdr.NumBones;
        if (numbones > StudioConstants.MAXSTUDIOBONES)
        {
            // Stock sizes its stack maps at MAXSTUDIOBONES and overruns the
            // stack here; the shared hdr reader caps validated hdrs at
            // MAXSTUDIOBONES, so this arm only sees a malformed caller.
            throw new ArgumentOutOfRangeException(nameof(parms), numbones, "numbones");
        }
        var studioToCached = new short[numbones];

        var cachedToStudio = new short[numbones];
        int cachedBoneCount = 0;
        for (int i = 0; i < numbones; i++)
        {
            // our 119-123: skip bones that aren't part of the boneMask
            // (and aren't the root bone)
            if (i != 0 && (pHdr.BoneFlags(i) & parms.BoneMask) == 0)
            {
                studioToCached[i] = -1;
                continue;
            }
            studioToCached[i] = (short)cachedBoneCount;
            cachedToStudio[cachedBoneCount] = (short)i;
            cachedBoneCount++;
        }

        // our 132-135: the block size, rounded up like stock's
        // (size + 3) & ~3.
        int tableSizeStudio = 2 * numbones;      // sizeof(short) * numbones
        int tableSizeCached = 2 * cachedBoneCount;
        int matrixSize = SizeofMatrix3x4 * cachedBoneCount;
        int size = (SizeofCBoneCache + tableSizeStudio + tableSizeCached + matrixSize + 3) & ~3;

        var cache = new BoneCache(numbones)
        {
            Size = (uint)size,
            TimeValid = parms.Curtime,   // Init, our 173
            BoneMask = parms.BoneMask,   // Init, our 174
        };
        cache._studioToCached = studioToCached;
        cache._cachedToStudio = cachedToStudio[..cachedBoneCount];
        cache._bones = new BoneMatrix3x4[cachedBoneCount];
        cache._cachedBoneCount = cachedBoneCount;

        cache.UpdateBones(parms.PBoneToWorld, parms.Curtime);
        return cache;
    }

    /// <summary>
    /// <c>CBoneCache::EstimatedSize</c> — our 145-149: the conservative
    /// max, all bones cached: <c>(numbones * (2 + 2 + 48) + 3) &amp; ~3</c>.
    /// The store feeds this to EnsureCapacity exactly as stock's
    /// <c>CDataManager::CreateResource</c> does.
    /// </summary>
    public static uint EstimatedSize(in BoneCacheParams parms)
    {
        return (uint)((parms.PStudioHdr.NumBones
            * (2 + 2 + SizeofMatrix3x4) + 3) & ~3);
    }

    /// <summary>
    /// <c>CBoneCache::UpdateBones</c> — our 190-201: copy through the
    /// reverse map in index order, slot <c>i</c> takes world matrix
    /// <c>cachedToStudio[i]</c>, then stamp <c>m_timeValid</c>. Stock's
    /// <c>numbones</c> argument is never read by the body (the loop is
    /// bounded by <c>m_cachedBoneCount</c>); the port derives the bound from
    /// the maps and requires the span to cover the hdr bone count the maps
    /// were built from — stock read out-of-bounds instead (NC-05,
    /// malformed-data only).
    /// </summary>
    public void UpdateBones(ReadOnlySpan<BoneMatrix3x4> pBoneToWorld, float curtime)
    {
        if (pBoneToWorld.Length < _numBones)
        {
            throw new ArgumentException(
                "pBoneToWorld must cover the hdr's numbones the maps were built from",
                nameof(pBoneToWorld));
        }
        for (int i = 0; i < _cachedBoneCount; i++)
        {
            _bones[i] = pBoneToWorld[_cachedToStudio[i]];
        }
        TimeValid = curtime;
    }

    /// <summary>
    /// <c>CBoneCache::GetCachedBone</c> — our 205-213: the map answers the
    /// cached slot, and stock returns <c>BoneArray() + cachedIndex</c> or
    /// NULL. The port copies the matrix out and reports presence; a false
    /// answer is stock's NULL, and the caller (c_baseanimating.cpp:1257)
    /// falls back to a fresh SetupBones read. An index outside the hdr is
    /// stock's unguarded map read; the port answers false (NC-05).
    /// </summary>
    public bool TryGetCachedBone(int studioIndex, out BoneMatrix3x4 matrix)
    {
        if (studioIndex >= 0 && studioIndex < _studioToCached.Length)
        {
            int cachedIndex = _studioToCached[studioIndex];
            if (cachedIndex >= 0)
            {
                matrix = _bones[cachedIndex];
                return true;
            }
        }
        matrix = default;
        return false;
    }

    /// <summary>
    /// <c>CBoneCache::ReadCachedBones</c> — our 217-225: write the cached
    /// slots back over their studio indices; bones the cache does not hold
    /// keep whatever the caller's array already had (stock never touches
    /// them). The span must cover the hdr bone count (NC-05).
    /// </summary>
    public void ReadCachedBones(Span<BoneMatrix3x4> pBoneToWorld)
    {
        if (pBoneToWorld.Length < _numBones)
        {
            throw new ArgumentException(
                "pBoneToWorld must cover the hdr's numbones the maps were built from",
                nameof(pBoneToWorld));
        }
        for (int i = 0; i < _cachedBoneCount; i++)
        {
            pBoneToWorld[_cachedToStudio[i]] = _bones[i];
        }
    }

    /// <summary>
    /// <c>CBoneCache::ReadCachedBonePointers</c> — our 229-238: stock
    /// memsets the caller's pointer array to NULL for its <c>numbones</c>
    /// entries and then aims an entry at the cached block for every cached
    /// bone. Stock hands out pointers into the live block (the hitbox-trace
    /// consumers read through them); the port copies the matrices out and
    /// marks the cached bones in <paramref name="cached"/>, which is what
    /// lets a consumer tell "points at a cache entry" from stock's NULL —
    /// pointer identity does not survive GC (NC-05). The span length plays
    /// stock's <c>numbones</c> argument; <paramref name="cached"/> is
    /// cleared first, mirroring the memset.
    /// </summary>
    public void ReadCachedBonePointers(
        Span<BoneMatrix3x4> bones, BoneSetupExtract.BoneBitList cached)
    {
        bones.Clear();
        cached.Clear();
        for (int i = 0; i < _cachedBoneCount; i++)
        {
            bones[_cachedToStudio[i]] = _bones[i];
            cached.MarkBone(_cachedToStudio[i]);
        }
    }

    /// <summary>
    /// <c>CBoneCache::IsValid</c> — our 242-247: the cache is good while
    /// <c>curtime - m_timeValid &lt;= dt</c> (boundary inclusive), default
    /// window 0.1 s. Stock reads <c>m_timeValid</c> without consulting this
    /// itself; callers gate on it (c_baseanimating.cpp:2364).
    /// </summary>
    public bool IsValid(float curtime, float dt = 0.1f)
        => curtime - TimeValid <= dt;
}

/// <summary>
/// Stock's <c>g_StudioBoneCache</c> singleton (our 280-281, oracle 216-244 —
/// <c>static CDataManager&lt;CBoneCache, bonecacheparams_t, CBoneCache *,
/// CThreadFastMutex&gt; g_StudioBoneCache( 128 * 1024L )</c>) plus the four
/// <c>Studio_*</c> entry points (oracle 216-244), recast as an injected
/// service: SS0001 forbids the static, so the client port news one of these
/// and passes it to its consumers (NC-01 drops the mutex, NC-02 covers the
/// entity-side half of InvalidateBoneCache, NC-03 the handle width).
///
/// <para>
/// Mirrors CDataManager's handle/free-list/LRU semantics (citations in the
/// file header). Nodes live in at most one list at a time (in LRU order, on
/// the free list, or nowhere), so one prev/next pair per slot serves every
/// list, as it does in stock's CUtlMultiList. Stock's lock list stays
/// unported: no Studio_* caller takes a lock, so lockCount is the constant 0
/// every stock branch already assumes (NC-07).
/// </para>
/// </summary>
public sealed class BoneCacheStore
{
    /// <summary>Stock's <c>INVALID_MEMHANDLE</c> (datamanager.h:25).</summary>
    public const int InvalidHandle = -1;

    /// <summary>Stock's constructor argument at our 281 (the 128 * 1024L).</summary>
    public const uint DefaultTargetSize = 128 * 1024;

    // CUtlMultiList's InvalidIndex.
    private const int InvalidIndex = 0xFFFF;

    // Usable slot indices are 0..65534 (0xFFFF is InvalidIndex). Unreachable
    // in practice: the 128 KB target caps live+free slots near 1800 (NC-03).
    private const int MaxSlots = 0xFFFF;

    private enum SlotState : byte { None, InLru, OnFreeList }


    private readonly List<SlotState> _membership = [];
    private readonly List<BoneCache?> _stores = [];
    private readonly List<int> _serials = [];          // ushort semantics

    private readonly List<int> _prev = [];
    private readonly List<int> _next = [];
    private int _lruHead = InvalidIndex;
    private int _lruTail = InvalidIndex;
    private int _freeHead = InvalidIndex;
    private int _freeTail = InvalidIndex;
    private uint _targetMemorySize;
    private uint _memUsed;

    public BoneCacheStore(uint targetSize = DefaultTargetSize)
        => _targetMemorySize = targetSize;

    /// <summary>Stock's <c>UsedSize()</c> — the accounting readers use.</summary>
    public uint UsedSize => _memUsed;

    /// <summary>Stock's <c>SetTargetSize</c> (tier1 only; kept so a test or
    /// phase-B owner can retune the budget without rebuilding the store).</summary>
    public void SetTargetSize(uint targetSize) => _targetMemorySize = targetSize;

    /// <summary>
    /// <c>Studio_CreateBoneCache</c> — our 289-293 over
    /// <c>CDataManager::CreateResource</c> (datamanager.h:181-187): reserve
    /// space against the conservative estimate, take a slot (free-list FIFO
    /// head or a fresh one), build the cache, store it under its real size,
    /// return the handle.
    /// </summary>
    public int CreateBoneCache(in BoneCacheParams parms)
    {
        EnsureCapacity(BoneCache.EstimatedSize(parms));
        int index = CreateHandle();
        BoneCache cache = BoneCache.CreateResource(parms);
        return StoreResourceInHandle(index, cache, cache.Size);
    }

    /// <summary>
    /// <c>Studio_GetBoneCache</c> — our 283-287 over
    /// <c>GetResource_NoLock</c> (datamanager.cpp:206-216): a serial match on
    /// the handle yields the cache and moves it to the LRU tail; anything
    /// else (destroyed slot, stale serial, foreign store) answers null.
    /// </summary>
    public BoneCache? GetBoneCache(int handle)
    {
        int index = FromHandle(handle);
        if (index != InvalidIndex)
        {
            TouchByIndex(index);
            return _stores[index];
        }
        return null;
    }

    /// <summary>
    /// <c>Studio_DestroyBoneCache</c> — our 295-299 over
    /// <c>DestroyResource</c> (datamanager.cpp:131-150): drop from the LRU,
    /// subtract the real size, bump the serial, hand the slot to the free
    /// list. Stock then frees the block; the GC owns it here. An invalid or
    /// foreign handle changes nothing (stock's FromHandle gate).
    /// </summary>
    public void DestroyBoneCache(int handle)
    {
        int index = FromHandle(handle);
        if (index == InvalidIndex)
        {
            return;
        }
        // Stock's Assert(lockCount == 0) + BreakLock fallback: lockCount is
        // the ported constant 0, so the assert holds by construction (NC-07).
        UnlinkFromLru(index);
        GetForFreeByIndex(index);
        // DestroyResourceStorage(pStore) -> CBoneCache::DestroyResource() ->
        // free(this): the managed cache block is garbage once the slot drops
        // its reference.
    }

    /// <summary>
    /// <c>Studio_InvalidateBoneCache</c> — our 301-309: the store-side
    /// half only (reach the cache through the handle and stamp
    /// <c>m_timeValid = -1.0f</c>; the lookup touches the LRU like stock's
    /// GetResource_NoLock). Stock's consumer half,
    /// <c>C_BaseAnimating::InvalidateBoneCache</c> (fork
    /// c_baseanimating.cpp:3175-3179: the bone-counter rewind and
    /// <c>m_flLastBoneSetupTime = -FLT_MAX</c>, plus
    /// <c>InvalidateBoneCaches</c>' leaf-library generation bump), is
    /// per-entity and engine-side state: not ported here — NC-02, ruling
    /// requested.
    /// </summary>
    public void InvalidateBoneCache(int handle)
    {
        BoneCache? pCache = GetBoneCache(handle);
        if (pCache is not null)
        {
            pCache.TimeValid = -1.0f;
        }
    }

    // ---- CDataManagerBase internals ----------------------------------------

    // CreateHandle, datamanager.cpp:276-297 (bCreateLocked is stock's false
    // arm only — NC-07): free-list head first (FIFO), else append a fresh
    // slot with serial 1. Either way link to the LRU tail.
    private int CreateHandle()
    {
        int index;
        if (_freeHead != InvalidIndex)
        {
            index = _freeHead;
            UnlinkFromFreeList(index);
        }
        else
        {
            index = _stores.Count;
            if (index >= MaxSlots)
            {
                throw new InvalidOperationException(
                    "BoneCacheStore slot space exhausted");
            }
            _stores.Add(null);
            _serials.Add(1);   // resource_lru_element_t ctor: serial = 1
            _membership.Add(SlotState.None);
            _prev.Add(InvalidIndex);
            _next.Add(InvalidIndex);
        }
        _membership[index] = SlotState.InLru;
        LinkToLruTail(index);
        return index;
    }

    // StoreResourceInHandle, datamanager.cpp:299-306.
    private int StoreResourceInHandle(int index, BoneCache store, uint realSize)
    {
        _stores[index] = store;
        _memUsed += realSize;
        return ToHandle(index);
    }

    // ToHandle, datamanager.cpp:320-326: serial<<16 | (index+1), both ushort
    // halves; the port keeps the bits in an int (NC-03).
    private int ToHandle(int index)
    {
        uint serial = (uint)_serials[index] & 0xFFFF;
        return unchecked((int)((serial << 16) | (uint)((index + 1) & 0xFFFF)));
    }

    // FromHandle, datamanager.h:252-261: truncate to 32 bits, split, and
    // accept only a live index whose serial still matches — the guard that
    // stops a stale handle aliasing a cache recreated in the same slot.
    private int FromHandle(int handle)
    {
        uint fullWord = unchecked((uint)handle);
        int serial = (int)(fullWord >> 16);
        int index = (int)(fullWord & 0xFFFF) - 1;   // ushort wrap is intended
        if (index >= 0 && index < _stores.Count && index < InvalidIndex
            && (_serials[index] & 0xFFFF) == serial)
        {
            return index;
        }
        return InvalidIndex;
    }

    // TouchByIndex, datamanager.cpp:308-318: only LRU-resident nodes move
    // (stock's lockCount test is the ported constant "not locked").
    private void TouchByIndex(int index)
    {
        if (index != InvalidIndex && _membership[index] == SlotState.InLru)
        {
            UnlinkFromLru(index);
            LinkToLruTail(index);
        }
    }

    // GetForFreeByIndex, datamanager.cpp:366-388. Stock's
    // size > m_memUsed ExecuteOnce Warning branch (used-memory accounting
    // corruption) clamps here without the log — debug output only (NC-08).
    private void GetForFreeByIndex(int index)
    {
        if (index == InvalidIndex || _stores[index] is null)
        {
            return;   // stock's GetResourceNoLock-gated Destroy can't get here;
                      // guard keeps a double destroy silent like stock's gate
        }
        BoneCache store = _stores[index]!;
        uint size = store.Size;   // GetRealSize = the block size from CreateResource
        if (size > _memUsed)
        {
            size = _memUsed;
        }
        _memUsed -= size;
        _stores[index] = null;
        _serials[index] = (_serials[index] + 1) & 0xFFFF;   // ushort wrap
        _membership[index] = SlotState.OnFreeList;
        LinkToFreeListTail(index);
    }

    // EnsureCapacity, datamanager.cpp:345-364: evict LRU heads until the
    // room exists; an empty LRU (every cache destroyed-but-slot-freed...
    // i.e. nothing reclaimable) ends the loop, and the create proceeds over
    // target just like stock's. The two-clause gate (estimate, not real
    // size) is stock's unsigned-arithmetic shape; see ledger NC-10.
    private void EnsureCapacity(uint size)
    {
        while (_memUsed > _targetMemorySize
               || (int)(_targetMemorySize - _memUsed) < (int)size)
        {
            int lruIndex = _lruHead;
            if (lruIndex == InvalidIndex)
            {
                break;
            }
            UnlinkFromLru(lruIndex);
            GetForFreeByIndex(lruIndex);
        }
    }

    // Doubly-linked-list plumbing: one prev/next pair per slot, the node in
    // exactly one list at a time (stock's CUtlMultiList usage).
    private void LinkToLruTail(int index)
    {
        _prev[index] = _lruTail;
        _next[index] = InvalidIndex;
        if (_lruTail != InvalidIndex)
        {
            _next[_lruTail] = index;
        }
        else
        {
            _lruHead = index;
        }
        _lruTail = index;
    }

    private void UnlinkFromLru(int index)
    {
        if (_membership[index] != SlotState.InLru)
        {
            return;   // stock Unlink of a node not in the list is a no-op
                      // on its links; the membership stamp keeps ours honest
        }
        SpliceOut(index);
        _membership[index] = SlotState.None;
    }

    private void LinkToFreeListTail(int index)
    {
        _prev[index] = _freeTail;
        _next[index] = InvalidIndex;
        if (_freeTail != InvalidIndex)
        {
            _next[_freeTail] = index;
        }
        else
        {
            _freeHead = index;
        }
        _freeTail = index;
    }

    private void UnlinkFromFreeList(int index)
    {
        SpliceOut(index);
    }

    private void SpliceOut(int index)
    {
        int p = _prev[index];
        int n = _next[index];
        if (p != InvalidIndex)
        {
            _next[p] = n;
        }
        if (n != InvalidIndex)
        {
            _prev[n] = p;
        }
        else
        {
            if (_lruTail == index) _lruTail = p;
            if (_freeTail == index) _freeTail = p;
        }
        if (p == InvalidIndex)
        {
            if (_lruHead == index) _lruHead = n;
            if (_freeHead == index) _freeHead = n;
        }
        _prev[index] = InvalidIndex;
        _next[index] = InvalidIndex;
    }
}

/// <summary>
/// The client-only convenience overload <c>BuildBoneChain</c> — oracle
/// 250-261 (our 316-329): a stack <c>CBoneBitList boneComputed;</c> wrapped
/// around the 7-argument core. Stock's server arm keeps only the core (the
/// convenience form is a CLIENT_DLL row). The bit list is a fresh instance
/// per call — no reuse, so no carried-over "already computed" bits.
///
/// <para>
/// Placement: stock defines this free function in the same client-only
/// region as the Studio_* cache API; the core it wraps lives in
/// <see cref="BoneSetupExtract"/>. It sits here until the extract lane's
/// file-ownership window reopens — NC-09, PROMOTION.
/// </summary>
public static class BoneSetupChain
{
    public static void BuildBoneChain(
        StudioHdr pStudioHdr,
        in BoneMatrix3x4 rootxform,
        Vector[] pos,
        Quaternion[] q,
        int iBone,
        Span<BoneMatrix3x4> pBoneToWorld)
    {
        var boneComputed = new BoneSetupExtract.BoneBitList();
        BoneSetupExtract.BuildBoneChain(
            pStudioHdr, rootxform, pos, q, iBone, pBoneToWorld, boneComputed);
    }
}
