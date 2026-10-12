using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the CBoneCache cluster (<see cref="BoneCache"/>) and the
/// injected <see cref="BoneCacheStore"/> (stock's g_StudioBoneCache):
/// the mask gate of CreateResource, the index-map direction of UpdateBones,
/// the cached reads, the CDataManager handle/serial/free-list/LRU semantics,
/// and the store-side half of Studio_InvalidateBoneCache. Expectations are
/// hand-derived from the stock text at the cited ranges (our
/// bone_setup.cpp 115-309), never read off the port.
/// </summary>
public class BonesCacheTests
{
    private const int Hitbox = StudioConstants.BONE_USED_BY_HITBOX; // 0x100

    private static StudioHdr Build(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    /// <summary>
    /// Four bones: b0 root (unflagged), b1 and b3 BONE_USED_BY_HITBOX, b2
    /// unflagged. With boneMask = BONE_USED_BY_HITBOX the cached set is
    /// {0, 1, 3} (root always, per stock's <c>i != 0</c> gate), so
    /// studioToCached = [0, 1, -1, 2], cachedToStudio = [0, 1, 3],
    /// cachedBoneCount = 3.
    /// </summary>
    private static StudioHdr CacheHdr()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, Flags = 0 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Flags = Hitbox });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b2", Parent = 1, Flags = 0 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b3", Parent = 1, Flags = Hitbox });
        return Build(b);
    }

    /// <summary>Distinct per-bone marker: rows are basis vectors with a
    /// per-bone translation, so any index mix-up changes an observable
    /// coordinate.</summary>
    private static BoneMatrix3x4 Marker(int i) => new()
    {
        Row0 = new Vector4(1f, 0f, 0f, i * 7 + 1),
        Row1 = new Vector4(0f, 1f, 0f, i * 7 + 2),
        Row2 = new Vector4(0f, 0f, 1f, i * 7 + 3),
    };


    /// <summary>Stock's Init always UpdateBones's from the caller's full
    /// boneToWorld array; every fixture hands one.</summary>
    private static BoneMatrix3x4[] World4() =>
        [Marker(0), Marker(1), Marker(2), Marker(3)];

    private static void AssertMatrix(BoneMatrix3x4 expected, BoneMatrix3x4 actual)
    {
        Assert.Equal(expected.Row0, actual.Row0);
        Assert.Equal(expected.Row1, actual.Row1);
        Assert.Equal(expected.Row2, actual.Row2);
    }

    private static BoneCache Create(
        StudioHdr hdr, BoneMatrix3x4[]? world = null,
        float curtime = 0f, int boneMask = Hitbox)
    {
        var parms = new BoneCacheParams(
            hdr, world ?? World4(), curtime, boneMask);
        var store = new BoneCacheStore();
        int handle = store.CreateBoneCache(in parms);
        BoneCache? cache = store.GetBoneCache(handle);
        Assert.NotNull(cache);
        return cache;
    }

    // ---- CreateResource: the mask gate and the maps (our 115-141) ---------

    /// <summary>Mask gate: exactly the masked bones plus the root get a
    /// slot. Bone 2 (unmasked, not root) answers false; bones 0/1/3 answer
    /// true with the matrix their cached slot holds after UpdateBones.</summary>
    [Fact]
    public void CreateResource_CachedMapCoversExactlyMaskedBones()
    {
        StudioHdr hdr = CacheHdr();
        BoneMatrix3x4[] world = [Marker(0), Marker(1), Marker(2), Marker(3)];
        BoneCache cache = Create(hdr, world);

        Assert.True(cache.TryGetCachedBone(0, out BoneMatrix3x4 m0));
        AssertMatrix(world[0], m0);          // cached slot 0 <- world[0]
        Assert.True(cache.TryGetCachedBone(1, out BoneMatrix3x4 m1));
        AssertMatrix(world[1], m1);          // cached slot 1 <- world[1]
        Assert.False(cache.TryGetCachedBone(2, out _)); // unmasked: never cached
        Assert.True(cache.TryGetCachedBone(3, out BoneMatrix3x4 m3));
        AssertMatrix(world[3], m3);          // cached slot 2 <- world[3]
    }

    /// <summary>Stock's <c>i != 0</c> arm: a zero mask still caches the
    /// root, so exactly one slot exists; the unmasked bones stay uncached
    /// (even the masked-by-another-mask ones).</summary>
    [Fact]
    public void CreateResource_AllZeroMaskStillCachesRoot()
    {
        StudioHdr hdr = CacheHdr();
        BoneMatrix3x4[] world = [Marker(0), Marker(1), Marker(2), Marker(3)];
        BoneCache cache = Create(hdr, world, boneMask: 0);

        Assert.True(cache.TryGetCachedBone(0, out BoneMatrix3x4 m0));
        AssertMatrix(world[0], m0);
        Assert.False(cache.TryGetCachedBone(1, out _));
        Assert.False(cache.TryGetCachedBone(2, out _));
        Assert.False(cache.TryGetCachedBone(3, out _));
    }

    /// <summary>Init (our 169-174): the constructor stamps
    /// m_timeValid = params.curtime and m_boneMask = params.boneMask.</summary>
    [Fact]
    public void CreateResource_StampsTimeValidAndBoneMask()
    {
        StudioHdr hdr = CacheHdr();
        BoneCache cache = Create(hdr, curtime: 3.5f, boneMask: Hitbox);

        Assert.Equal(3.5f, cache.TimeValid);
        Assert.Equal(Hitbox, cache.BoneMask);
    }

    // ---- UpdateBones: index-map direction (our 190-201) -------------------

    /// <summary>UpdateBones copies slot i &lt;- world[cachedToStudio[i]] in
    /// index-map order: slot 2 (bone 3) takes world[3], not world[2].
    /// ReadCachedBones then writes each slot back over its studio index, so
    /// dst[3] == m3 is the direction witness (a studioToCached-direction
    /// bug would leave dst[3] == m2), and dst[2] keeps its sentinel because
    /// bone 2 has no cached slot (our 217-225: stock never touches uncached
    /// entries).</summary>
    [Fact]
    public void UpdateBones_CopiesThroughIndexMapInCacheOrder()
    {
        StudioHdr hdr = CacheHdr();
        BoneMatrix3x4[] world = [Marker(0), Marker(1), Marker(2), Marker(3)];
        BoneCache cache = Create(hdr, world);

        BoneMatrix3x4[] different = [Marker(9), Marker(8), Marker(7), Marker(6)];
        cache.UpdateBones(different, 1.0f);

        BoneMatrix3x4[] dst = new BoneMatrix3x4[4];
        foreach (ref BoneMatrix3x4 d in dst.AsSpan())
        {
            d = Marker(99);   // sentinel
        }
        cache.ReadCachedBones(dst);

        AssertMatrix(different[0], dst[0]);
        AssertMatrix(different[1], dst[1]);
        AssertMatrix(Marker(99), dst[2]);    // uncached: sentinel untouched
        AssertMatrix(different[3], dst[3]);  // the index-direction witness
    }

    // ---- IsValid (our 242-247) ---------------------------------------------

    /// <summary>IsValid: curtime - m_timeValid &lt;= dt, boundary inclusive.
    /// Fresh cache (timeValid 0): at 0.1f the difference is exactly dt -
    /// stock's &lt;= (not &lt;) still answers true; at 0.2f it answers false.
    /// Curtime 0 keeps the subtraction exact in f32.</summary>
    [Fact]
    public void IsValid_WindowIsInclusiveAtTheBoundary()
    {
        StudioHdr hdr = CacheHdr();
        BoneCache cache = Create(hdr, curtime: 0f);

        Assert.True(cache.IsValid(0f, 0.1f));
        Assert.True(cache.IsValid(0.1f, 0.1f));  // boundary: <= dt
        Assert.False(cache.IsValid(0.2f, 0.1f));
    }

    /// <summary>Stock's default dt argument is 0.1 s
    /// (bool IsValid( float curtime, float dt = 0.1f )): with the default,
    /// 0.1f passes and 0.11f does not (both exact against the zero stamp).</summary>
    [Fact]
    public void IsValid_DefaultDtIsATenthSecond()
    {
        StudioHdr hdr = CacheHdr();
        BoneCache cache = Create(hdr, curtime: 0f);

        Assert.True(cache.IsValid(0.1f));
        Assert.False(cache.IsValid(0.11f));
    }

    /// <summary>UpdateBones stamps m_timeValid = curtime (our 200), which is
    /// what IsValid then measures from.</summary>
    [Fact]
    public void UpdateBones_StampsTimeValid()
    {
        StudioHdr hdr = CacheHdr();
        BoneMatrix3x4[] world = [Marker(0), Marker(1), Marker(2), Marker(3)];
        BoneCache cache = Create(hdr, world, curtime: 0f);

        cache.UpdateBones(world, 5.0f);
        Assert.Equal(5.0f, cache.TimeValid);
        Assert.True(cache.IsValid(5.05f, 0.1f));
        Assert.False(cache.IsValid(5.2f, 0.1f));
    }

    // ---- GetCachedBone out-of-range arm (our 205-213) ---------------------

    /// <summary>Stock indexes StudioToCached()[studioIndex] unguarded (UB for
    /// an out-of-hdr index); the port answers false + default matrix for
    /// indices outside the hdr (NC-05, malformed-data only).</summary>
    [Fact]
    public void TryGetCachedBone_OutOfRangeAnswersFalse()
    {
        StudioHdr hdr = CacheHdr();
        BoneMatrix3x4[] world = [Marker(0), Marker(1), Marker(2), Marker(3)];
        BoneCache cache = Create(hdr, world);

        Assert.False(cache.TryGetCachedBone(999, out BoneMatrix3x4 m));
        Assert.Equal(default, m);
        Assert.False(cache.TryGetCachedBone(-1, out m));
    }

    // ---- ReadCachedBonePointers (our 229-238) ------------------------------

    /// <summary>Stock memsets the whole pointer array to NULL, then aims an
    /// entry at the block for each cached bone. The port zeroes every matrix
    /// in the span (including uncached ones — the memset is the observable
    ///), copies the cached matrices to their studio indices, and marks
    /// exactly the cached bones in the bit list (pointer identity cannot
    /// survive the GC, NC-05).</summary>
    [Fact]
    public void ReadCachedBonePointers_ZeroesAllMarksCachedOnly()
    {
        StudioHdr hdr = CacheHdr();
        BoneMatrix3x4[] world = [Marker(0), Marker(1), Marker(2), Marker(3)];
        BoneCache cache = Create(hdr, world);

        BoneMatrix3x4[] bones = new BoneMatrix3x4[4];
        foreach (ref BoneMatrix3x4 d in bones.AsSpan())
        {
            d = Marker(99);   // prefilled garbage; stock's memset must clear it
        }
        var cached = new BoneSetupExtract.BoneBitList();
        cache.ReadCachedBonePointers(bones, cached);

        AssertMatrix(world[0], bones[0]);
        AssertMatrix(world[1], bones[1]);
        Assert.Equal(default, bones[2]);     // memset arm: zeroed, not garbage
        AssertMatrix(world[3], bones[3]);
        Assert.True(cached.IsBoneMarked(0));
        Assert.True(cached.IsBoneMarked(1));
        Assert.False(cached.IsBoneMarked(2));
        Assert.True(cached.IsBoneMarked(3));
    }

    // ---- handle lifecycle and serials (datamanager.cpp mirror) ------------

    /// <summary>Destroy then get: the slot drops its reference and the old
    /// handle answers null (Studio_DestroyBoneCache + FromHandle gate).</summary>
    [Fact]
    public void DestroyThenGet_ReturnsNull()
    {
        var store = new BoneCacheStore();
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int handle = store.CreateBoneCache(in parms);
        Assert.NotNull(store.GetBoneCache(handle));

        store.DestroyBoneCache(handle);
        Assert.Null(store.GetBoneCache(handle));
    }

    /// <summary>INVALID_MEMHANDLE (-1) and handle 0 answer null and are
    /// no-ops for destroy: stock's FromHandle gate (the -1 word splits to
    /// serial 0xFFFF, index 0xFFFE — out of the slot range; handle 0's low
    /// word 0 names index -1).</summary>
    [Fact]
    public void GetBoneCache_InvalidHandles_AnswerNull()
    {
        var store = new BoneCacheStore();
        Assert.Null(store.GetBoneCache(BoneCacheStore.InvalidHandle));
        Assert.Null(store.GetBoneCache(0));
        store.DestroyBoneCache(BoneCacheStore.InvalidHandle);   // no throw
        store.DestroyBoneCache(0);                              // no throw
    }

    /// <summary>The serial-reuse guard: destroy frees the slot with
    /// serial++ (GetForFreeByIndex), the next create reuses it from the
    /// free-list head, so the fresh handle carries serial 2 at the same
    /// index while the stale handle still says serial 1. FromHandle rejects
    /// the serial mismatch — the stale handle must NOT alias the new
    /// cache.</summary>
    [Fact]
    public void StaleHandle_DoesNotAliasRecreatedCacheAtSameSlot()
    {
        var store = new BoneCacheStore();
        StudioHdr hdr = CacheHdr();
        var parms = new BoneCacheParams(hdr, World4(), 0f, Hitbox);
        int oldHandle = store.CreateBoneCache(in parms);
        store.DestroyBoneCache(oldHandle);

        BoneMatrix3x4[] world = [Marker(4), Marker(5), Marker(6), Marker(7)];
        var freshParms = new BoneCacheParams(hdr, world, 0f, Hitbox);
        int newHandle = store.CreateBoneCache(in freshParms);

        // Same slot: low 16 bits are the shared index field (index + 1).
        Assert.Equal(oldHandle & 0xFFFF, newHandle & 0xFFFF);
        Assert.Null(store.GetBoneCache(oldHandle));   // stale serial rejected
        Assert.NotNull(store.GetBoneCache(newHandle));
    }

    /// <summary>Free-list reuse is FIFO: destroy A then B, and the next two
    /// creates land on A's slot first, then B's (stock pops the free-list
    /// HEAD, GetForFreeByIndex links to the tail).</summary>
    [Fact]
    public void FreeList_ReuseIsFifo()
    {
        var store = new BoneCacheStore();
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int a = store.CreateBoneCache(in parms);
        int b = store.CreateBoneCache(in parms);
        store.DestroyBoneCache(a);
        store.DestroyBoneCache(b);

        int d = store.CreateBoneCache(in parms);
        int e = store.CreateBoneCache(in parms);
        Assert.Equal(a & 0xFFFF, d & 0xFFFF);   // A's slot first
        Assert.Equal(b & 0xFFFF, e & 0xFFFF);   // then B's
        Assert.NotEqual(a, d);                   // ...under a new serial
        Assert.NotNull(store.GetBoneCache(d));
        Assert.NotNull(store.GetBoneCache(e));
    }

    // ---- store accounting + eviction (EnsureCapacity, datamanager 345-364)

    /// <summary>The store books the REAL block size, not the conservative
    /// estimate: four bones give (20 + 2*4 + 2*3 + 48*3 + 3) &amp; ~3 =
    /// 181 &amp; ~3 = 180, while EstimatedSize's
    /// (4 * (2 + 2 + 48) + 3) &amp; ~3 = 211 &amp; ~3 = 208. Destroying
    /// subtracts the same 180.</summary>
    [Fact]
    public void UsedSize_UsesRealBlockSize_NotEstimate()
    {
        var store = new BoneCacheStore();
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int handle = store.CreateBoneCache(in parms);
        Assert.Equal(180u, store.UsedSize);   // real size, not the 208 estimate

        store.DestroyBoneCache(handle);
        Assert.Equal(0u, store.UsedSize);
    }

    /// <summary>EnsureCapacity runs off the ESTIMATE (208) while the books
    /// run off the real size (180). Target 720: after A, B, C the books read
    /// 540 and the room is 180 — under the 208 estimate, so the LRU HEAD (A,
    /// the least-recently-touched) is freed; the check then passes
    /// (720 - 360 = 360 &gt;= 208) and D lands. An implementation that sized
    /// the check off the real 180 would not evict (180 &gt;= 180) and this
    /// fact goes red.</summary>
    [Fact]
    public void CreateEvictsLruHead_WhenEstimateDoesNotFit()
    {
        var store = new BoneCacheStore(targetSize: 720);
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int a = store.CreateBoneCache(in parms);
        int b = store.CreateBoneCache(in parms);
        int c = store.CreateBoneCache(in parms);

        int d = store.CreateBoneCache(in parms);

        Assert.Null(store.GetBoneCache(a));       // evicted LRU head
        Assert.NotNull(store.GetBoneCache(b));
        Assert.NotNull(store.GetBoneCache(c));
        Assert.NotNull(store.GetBoneCache(d));
        Assert.Equal(540u, store.UsedSize);       // 3 * 180
    }

    /// <summary>Touching a cache moves it to the LRU tail (stock's
    /// GetResource_NoLock), so the next eviction takes the other head
    /// instead: get A (A becomes newest), then create D evicts B.</summary>
    [Fact]
    public void GetBoneCache_TouchMovesToLruTail()
    {
        var store = new BoneCacheStore(targetSize: 720);
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int a = store.CreateBoneCache(in parms);
        int b = store.CreateBoneCache(in parms);
        int c = store.CreateBoneCache(in parms);

        Assert.NotNull(store.GetBoneCache(a));   // touch: LRU now B, C, A
        int d = store.CreateBoneCache(in parms); // evicts head B, not A

        Assert.Null(store.GetBoneCache(b));
        Assert.NotNull(store.GetBoneCache(a));
        Assert.NotNull(store.GetBoneCache(c));
        Assert.NotNull(store.GetBoneCache(d));
    }

    // ---- Studio_InvalidateBoneCache store half (our 301-309) --------------

    /// <summary>Invalidate stamps m_timeValid = -1.0f: the cache reads
    /// invalid at any ordinary curtime, and — stock's quirk, ported
    /// faithfully — becomes revalid again near curtime -1
    /// (-0.95f - (-1) = 0.05f &lt;= 0.1f).</summary>
    [Fact]
    public void InvalidateBoneCache_SetsTimeValidToNegativeOne()
    {
        var store = new BoneCacheStore();
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int handle = store.CreateBoneCache(in parms);
        BoneCache cache = store.GetBoneCache(handle)!;
        Assert.True(cache.IsValid(0f, 0.1f));

        store.InvalidateBoneCache(handle);
        Assert.False(cache.IsValid(0f, 0.1f));
        Assert.True(cache.IsValid(-0.95f, 0.1f));   // curtime - (-1) = 0.05
    }

    /// <summary>Stock gates on the lookup: an unreachable handle (invalid,
    /// or of a destroyed cache) leaves the store untouched and must not
    /// throw.</summary>
    [Fact]
    public void InvalidateBoneCache_UnknownHandle_IsANoOp()
    {
        var store = new BoneCacheStore();
        var parms = new BoneCacheParams(CacheHdr(), World4(), 0f, Hitbox);
        int handle = store.CreateBoneCache(in parms);
        store.DestroyBoneCache(handle);

        store.InvalidateBoneCache(BoneCacheStore.InvalidHandle);
        store.InvalidateBoneCache(handle);   // destroyed: stock's NULL gate
        store.DestroyBoneCache(handle);      // double destroy stays silent
        Assert.Null(store.GetBoneCache(handle));
    }

    // ---- BuildBoneChain convenience overload (our 316-329) ----------------

    /// <summary>The client-only 6-arg overload wraps the core with a FRESH
    /// CBoneBitList per call: the second call — same bone, different pos/q —
    /// recomputes everything instead of honouring stale "already computed"
    /// bits, and matches an independent core call with its own fresh list.
    /// (A reused list would skip the bones and keep the first answer; the
    /// mutation proof pre-marks the list to show this fact sees the
    /// difference.)</summary>
    [Fact]
    public void BuildBoneChain_ConvenienceOverload_RentsFreshComputedListPerCall()
    {
        StudioHdr hdr = CacheHdr();
        var rootxform = BoneMatrix3x4.Identity;

        Vector[] pos1 = [new Vector(1f, 0f, 0f), new Vector(2f, 0f, 0f), new Vector(3f, 0f, 0f), new Vector(4f, 0f, 0f)];
        Quaternion[] q1 = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity, Quaternion.Identity];
        Vector[] pos2 = [new Vector(5f, 0f, 0f), new Vector(6f, 0f, 0f), new Vector(7f, 0f, 0f), new Vector(8f, 0f, 0f)];
        Quaternion[] q2 = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity, Quaternion.Identity];

        BoneMatrix3x4[] first = new BoneMatrix3x4[4];
        BoneSetupChain.BuildBoneChain(hdr, rootxform, pos1, q1, 1, first);

        BoneMatrix3x4[] second = new BoneMatrix3x4[4];
        BoneSetupChain.BuildBoneChain(hdr, rootxform, pos2, q2, 1, second);

        // Reference: the core with an independently fresh list on the same
        // inputs as the second call.
        BoneMatrix3x4[] reference = new BoneMatrix3x4[4];
        var fresh = new BoneSetupExtract.BoneBitList();
        BoneSetupExtract.BuildBoneChain(hdr, rootxform, pos2, q2, 1, reference, fresh);

        AssertMatrix(reference[1], second[1]);
        AssertMatrix(reference[0], second[0]);
        Assert.NotEqual(first[1].Position, second[1].Position);
    }
}
