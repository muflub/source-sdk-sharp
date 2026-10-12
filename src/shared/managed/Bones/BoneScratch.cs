//=============================================================================//
//
// The skeleton build's scratch, kept between builds (plan_physics_bones_perf
// A3: the hot path allocation-free).
//
// A build rents MAXSTUDIOBONES-long arrays for every pose it blends: a pose
// pass's private pos/q, CalcPoseSingle's three blend partners and its matrix
// table, the world-space slerp's three tables, the merge source's skeleton.
// Renting a fresh array per call was 140 KB of garbage per Male_01 build
// (cs_cl_bonebench, 2026-10-12), and the collections it caused landed in
// whichever frame drew next.
//
// The rents nest (a pose pass accumulates its sequence's layers, each a pose
// pass of its own), so the scratch is a stack: Save() marks the depth,
// Restore() hands everything rented since back, and the next rent reuses it.
// Every rent comes back ZEROED, which is what `new T[n]` gave the code this
// replaces, so a build reads exactly what it read before (a bone outside the
// mask is zero, never the last build's leftovers).
//
// An instance, never a static (SS0001): each side's skeleton service owns one
// and hands it down through BoneBuildInputs / CBoneSetup. Not thread-safe;
// a side builds its skeletons on one thread. A caller with no scratch passes
// null and every rent is a fresh array, as before.
//=============================================================================//

using System;
using System.Collections.Generic;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>The skeleton build's reusable scratch: LIFO, zeroed rents.</summary>
public sealed class BoneScratch
{
    private readonly Pool<Vector> _vectors = new();
    private readonly Pool<Quaternion> _quaternions = new();
    private readonly Pool<BoneMatrix3x4> _matrices = new();
    private readonly List<BoneSetupExtract.BoneBitList> _bitLists = [];
    private int _bitListDepth;
    private readonly List<CIKContext> _ikContexts = [];
    private int _ikDepth;

    /// <summary>The rent depth, to hand back with <see cref="Restore"/>.</summary>
    public readonly struct Mark
    {
        internal Mark(int vectors, int quaternions, int matrices, int bitLists, int ikContexts)
        {
            Vectors = vectors;
            Quaternions = quaternions;
            Matrices = matrices;
            BitLists = bitLists;
            IkContexts = ikContexts;
        }

        internal int Vectors { get; }
        internal int Quaternions { get; }
        internal int Matrices { get; }
        internal int BitLists { get; }
        internal int IkContexts { get; }
    }

    /// <summary>How many arrays of each kind exist (the facts: reuse, not growth).</summary>
    public int Allocated => _vectors.Count + _quaternions.Count + _matrices.Count + _bitLists.Count + _ikContexts.Count;

    /// <summary>The depth now.</summary>
    public Mark Save() => new(_vectors.Depth, _quaternions.Depth, _matrices.Depth, _bitListDepth, _ikDepth);

    /// <summary>Everything rented since <paramref name="mark"/> is free again.</summary>
    public void Restore(Mark mark)
    {
        _vectors.Depth = mark.Vectors;
        _quaternions.Depth = mark.Quaternions;
        _matrices.Depth = mark.Matrices;
        _bitListDepth = mark.BitLists;
        _ikDepth = mark.IkContexts;
    }

    /// <summary>A zeroed <c>Vector[MAXSTUDIOBONES]</c>.</summary>
    public Vector[] Vectors() => _vectors.Rent();

    /// <summary>A zeroed <c>Quaternion[MAXSTUDIOBONES]</c>.</summary>
    public Quaternion[] Quaternions() => _quaternions.Rent();

    /// <summary>A zeroed <c>BoneMatrix3x4[MAXSTUDIOBONES]</c>.</summary>
    public BoneMatrix3x4[] Matrices() => _matrices.Rent();

    /// <summary>
    /// A <c>BoneMatrix3x4[MAXSTUDIOBONES]</c> NOT zeroed: only for a table
    /// every read of which a bone bit list guards (CalcAnimation's
    /// BuildBoneChain memo writes a bone before it marks it, and reads only
    /// marked bones), so no read sees what the last renter left. Six
    /// kilobytes of memset a pose pass otherwise.
    /// </summary>
    public BoneMatrix3x4[] MemoMatrices() => _matrices.RentUncleared();

    /// <summary>An empty bone bit list (a fresh <c>CBoneBitList</c>).</summary>
    public BoneSetupExtract.BoneBitList BitList()
    {
        if (_bitListDepth == _bitLists.Count)
        {
            _bitLists.Add(new BoneSetupExtract.BoneBitList());
        }

        BoneSetupExtract.BoneBitList list = _bitLists[_bitListDepth++];
        list.Clear();
        return list;
    }

    /// <summary>
    /// A <c>CIKContext</c> as <c>new</c> makes it (the autoplay pass's, a
    /// sequence's lock context: stock's stack locals), its storage kept.
    /// </summary>
    public CIKContext IkContext()
    {
        if (_ikDepth == _ikContexts.Count)
        {
            _ikContexts.Add(new CIKContext());
            return _ikContexts[_ikDepth++];
        }

        CIKContext ik = _ikContexts[_ikDepth++];
        ik.ResetToNew();
        return ik;
    }

    // ---- the null-scratch forms: a fresh array, as before -----------------------

    internal static CIKContext IkContext(BoneScratch? scratch) => scratch?.IkContext() ?? new CIKContext();

    internal static Vector[] Vectors(BoneScratch? scratch) =>
        scratch?.Vectors() ?? new Vector[StudioConstants.MAXSTUDIOBONES];

    internal static Quaternion[] Quaternions(BoneScratch? scratch) =>
        scratch?.Quaternions() ?? new Quaternion[StudioConstants.MAXSTUDIOBONES];

    internal static BoneMatrix3x4[] Matrices(BoneScratch? scratch) =>
        scratch?.Matrices() ?? new BoneMatrix3x4[StudioConstants.MAXSTUDIOBONES];

    internal static BoneMatrix3x4[] MemoMatrices(BoneScratch? scratch) =>
        scratch?.MemoMatrices() ?? new BoneMatrix3x4[StudioConstants.MAXSTUDIOBONES];

    internal static BoneSetupExtract.BoneBitList BitList(BoneScratch? scratch) =>
        scratch?.BitList() ?? new BoneSetupExtract.BoneBitList();

    private sealed class Pool<T>
    {
        private readonly List<T[]> _held = [];

        public int Depth;

        public int Count => _held.Count;

        public T[] Rent()
        {
            if (Depth == _held.Count)
            {
                _held.Add(new T[StudioConstants.MAXSTUDIOBONES]);
                return _held[Depth++];
            }

            T[] array = _held[Depth++];
            Array.Clear(array);
            return array;
        }

        public T[] RentUncleared()
        {
            if (Depth == _held.Count)
            {
                _held.Add(new T[StudioConstants.MAXSTUDIOBONES]);
            }

            return _held[Depth++];
        }
    }
}
