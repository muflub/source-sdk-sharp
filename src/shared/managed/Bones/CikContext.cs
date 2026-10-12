//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// ik cluster: CIKContext (oracle bone_setup.h:271-330 decl, bone_setup.cpp
// definitions): ctor 3216-3223, Init 3226-3251, AddDependencies 3253-3335,
// AddAutoplayLocks 3343-3391, AddSequenceLocks 3395-3450, BuildBoneChain
// thunk 3452-3461, ClearTargets 3699-3706, SolveAutoplayLocks 4272-4287,
// SolveSequenceLocks 4295-4311, SolveLock 4402-4451.
//
// Client-side cutback: CIKTarget is ported ONLY as far as the server rows
// need it. The TSV marks every CIKTarget::* member row (75-89) CLIENT_DLL,
// and the context members that consume targets for real (UpdateTargets,
// AutoIKRelease, SolveDependencies, Estimate) are client rows too. The two
// server-side uses are bookkeeping: the `m_target.Count() == 0` grow hack in
// Init/AddDependencies and ClearTargets stamping latched.iFramecounter. So
// the port keeps a CIKTarget with just that counter and nothing else; the
// rest of stock's CIKTarget (latched owner/origin/angles, error ramp, the
// IKFailed/chain/type fields, SetOwner/SetPose/SetQuaternion/...) stays
// native until a client lane ports it.
//
// The destructor (row 66, ~CIKContext) is trivial in C++ (CUtlVectorFixed /
// CUtlVector destructors); the GC owns the managed arrays, so it is not
// ported.
//
// Parameterisation, per the batch's no-hidden-state rule: stock's
// AddDependencies/AddAutoplayLocks/SolveSequenceLocks read seqdesc,
// poseParameter, flCycle and flTime from the owning CBoneSetup; those arrive
// as parameters here (the pinned shapes below). m_rootxform is handed to
// Init pre-multiplied by the caller (stock's Init takes angles+pos).
// Stock's CBoneSetup::AddSequenceLocks passes its own seqdesc; the port
// resolves it from iSequence, which is what CBoneSetup holds.
//
// g_MatrixPool (row 136/137) is NOT ported as a static pool — SS0001
// forbids mutable statics in runtime assemblies. The port rents the
// 128-slot boneToWorld buffer per call through ArrayPool<BoneMatrix3x4>.Shared
// via the private RentMatrices/ReturnMatrices helpers below; like stock's
// pool the buffer arrives uninitialised, which is safe here for the same
// reason: every matrix a reader touches was written by BuildBoneChain first.

using System;
using System.Buffers;
using System.Collections.Generic;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary><c>ikcontextikrule_t</c> (bone_setup.h:218-268): the processed
/// IK rule the context stores. A managed record class — byte layout is
/// irrelevant; fields carry stock's names. Stock's ctor memsets, so every
/// field defaults zero, INCLUDING <c>q</c> (Studio_IKSequenceError's
/// <c>q.Init()</c> re-zeroes it: Quaternion::Init defaults to (0,0,0,0),
/// vector.h:1577).</summary>
public sealed class IkContextIkRule
{
    public int Index;

    public int Type;
    public int Chain;

    public int Bone;

    public int Slot;        // iktarget slot. Usually same as chain.
    public float Height;
    public float Radius;
    public float Floor;
    public Vector Pos;
    public Quaternion Q;

    public float Start;     // beginning of influence
    public float Peak;      // start of full influence
    public float Tail;      // end of full influence
    public float End;       // end of all influence

    public float Top;
    public float Drop;

    public float Commit;    // frame footstep target should be committed
    public float Release;   // frame ankle should end rotation from latched orientation

    public float FlWeight;      // processed version of start-end cycle
    public float FlRuleWeight;  // blending weight
    public float Latched;       // does the IK rule use a latched value?
    public string? SzLabel;

    public Vector KneeDir;
    public Vector KneePos;
}

/// <summary><c>CIKTarget</c> (bone_setup.h:162-204). Stock groups the
/// fields into anonymous <c>x1..x6</c> blocks for byte layout; layout is
/// irrelevant here (same ruling as <see cref="IkContextIkRule"/>), so the
/// fields are flat with the block name as prefix and stock zero-init from
/// <c>memset</c> is the C# default — including <c>EstQ</c>/<c>IdealQ</c>
/// starting as <c>(0,0,0,0)</c>, not identity (a <c>q.Init()</c> is zero
/// too: Quaternion::Init defaults to (0,0,0,0), vector.h:1577).</summary>
public sealed class CikTarget
{
    // ---- public members (bone_setup.h:139-141) ----

    /// <summary><c>chain</c>: the ik chain this target belongs to; written
    /// by UpdateTargets from the surviving rule.</summary>
    public int Chain;

    /// <summary><c>type</c>: the IK rule type that produced the target
    /// (<c>StudioConstants.IK_*</c>).</summary>
    public int Type;

    // ---- struct x2 offset (public) ----

    /// <summary><c>offset.pAttachmentName</c>: szLabel of an
    /// IK_ATTACHMENT rule, else null.</summary>
    public string? OffsetPAttachmentName;

    public Vector OffsetPos;
    public Quaternion OffsetQ;

    // ---- struct x3 ideal (private in stock; CIKContext is a friend) ----

    public Vector IdealPos;
    public Quaternion IdealQ;

    // ---- struct x4 est: estimate contact position ----

    public float EstLatched;
    public float EstRelease;
    public float EstHeight;
    public float EstFloor;
    public float EstRadius;
    public float EstFlTime;
    public float EstFlWeight;
    public Vector EstPos;
    public Quaternion EstQ;
    public bool EstOnWorld;

    // ---- struct x5 trace ----

    /// <summary>Distance from hip to foot.</summary>
    public float TraceHipToFoot;

    /// <summary>Distance from hip to knee.</summary>
    public float TraceHipToKnee;

    /// <summary>Distance from knee to foot.</summary>
    public float TraceKneeToFoot;

    public Vector TraceHip;
    public Vector TraceClosest;
    public Vector TraceKnee;
    public Vector TraceFarthest;
    public Vector TraceLowest;

    // ---- struct x1 latched (private in stock): internally latched footset ----

    public bool LatchedBNeedsLatch;
    public bool LatchedBHasLatch;
    public float LatchedInfluence;

    /// <summary><c>latched.iFramecounter</c>: the frame the target was
    /// latched at; <c>-9999</c> means "not latched" (stock ClearTargets).</summary>
    public int LatchedIFramecounter;

    public int LatchedOwner;
    public Vector LatchedAbsOrigin;
    public BoneAngle LatchedAbsAngles;
    public Vector LatchedPos;
    public Quaternion LatchedQ;

    /// <summary>Accumulated error.</summary>
    public Vector LatchedDeltaPos;

    public Quaternion LatchedDeltaQ;
    public Vector LatchedDebouncePos;
    public Quaternion LatchedDebounceQ;

    // ---- struct x6 error ----

    /// <summary>Time last error was detected.</summary>
    public float ErrorFlTime;

    public float ErrorFlErrorTime;
    public float ErrorRamp;
    public bool ErrorBInError;

    // ---- CIKTarget members (3525-3691), CLIENT_DLL stock rows ----

    /// <summary><c>SetOwner( entindex, pos, angles )</c> (3525-3530):
    /// remember the owner entity and its world transform at latch time.</summary>
    /// <summary>
    /// Every field back to what <c>new CikTarget()</c> holds: a context
    /// reused from a build's scratch hands out targets indistinguishable from
    /// fresh ones (plan_physics_bones_perf A3). A fact compares every field
    /// with a new instance's, so a field added here is a field reset here.
    /// </summary>
    internal void ResetToNew()
    {
        Chain = 0;
        Type = 0;
        OffsetPAttachmentName = null;
        OffsetPos = default;
        OffsetQ = default;
        IdealPos = default;
        IdealQ = default;
        EstLatched = 0f;
        EstRelease = 0f;
        EstHeight = 0f;
        EstFloor = 0f;
        EstRadius = 0f;
        EstFlTime = 0f;
        EstFlWeight = 0f;
        EstPos = default;
        EstQ = default;
        EstOnWorld = false;
        TraceHipToFoot = 0f;
        TraceHipToKnee = 0f;
        TraceKneeToFoot = 0f;
        TraceHip = default;
        TraceClosest = default;
        TraceKnee = default;
        TraceFarthest = default;
        TraceLowest = default;
        LatchedBNeedsLatch = false;
        LatchedBHasLatch = false;
        LatchedInfluence = 0f;
        LatchedIFramecounter = 0;
        LatchedOwner = 0;
        LatchedAbsOrigin = default;
        LatchedAbsAngles = default;
        LatchedPos = default;
        LatchedQ = default;
        LatchedDeltaPos = default;
        LatchedDeltaQ = default;
        LatchedDebouncePos = default;
        LatchedDebounceQ = default;
        ErrorFlTime = 0f;
        ErrorFlErrorTime = 0f;
        ErrorRamp = 0f;
        ErrorBInError = false;
    }

    public void SetOwner(int entindex, Vector pos, BoneAngle angles)
    {
        LatchedOwner = entindex;
        LatchedAbsOrigin = pos;
        LatchedAbsAngles = angles;
    }

    /// <summary><c>ClearOwner()</c> (3536-3539).</summary>
    public void ClearOwner()
    {
        LatchedOwner = -1;
    }

    /// <summary><c>GetOwner()</c> (3545-3548).</summary>
    public int GetOwner()
    {
        return LatchedOwner;
    }

    /// <summary><c>UpdateOwner( entindex, pos, angles )</c> (3554-3568):
    /// re-express the latched contact pose in the owner's new frame. Stock's
    /// early-out is exact component equality (<c>Vector::operator==</c> and
    /// <c>QAngle</c>'s memberwise compare are exact). Stock does not touch
    /// <c>latched.owner</c> here — only SetOwner does.</summary>
    public void UpdateOwner(int entindex, Vector pos, BoneAngle angles)
    {
        if (pos == LatchedAbsOrigin && QAngleEquals(angles, LatchedAbsAngles))
        {
            return;
        }

        BoneMatrix3x4 in1 = BoneSetupMath.AngleMatrix(angles, pos);
        BoneMatrix3x4 out1 = AngleIMatrix(LatchedAbsAngles, LatchedAbsOrigin);

        BoneMatrix3x4 tmp1 = BoneSetupMath.QuaternionMatrix(LatchedQ, LatchedPos);
        BoneMatrix3x4 tmp2 = BoneSetupMath.ConcatTransforms(out1, tmp1);
        tmp1 = BoneSetupMath.ConcatTransforms(in1, tmp2);
        (LatchedQ, LatchedPos) = BoneSetupMath.MatrixAnglesQuaternion(tmp1);
    }

    /// <summary><c>SetPos( pos )</c> (3575-3578): sets the ground position of
    /// the target.</summary>
    public void SetPos(Vector pos)
    {
        EstPos = pos;
    }

    /// <summary><c>SetAngles( angles )</c> (3584-3587): sets the ground
    /// "identity" orientation.</summary>
    public void SetAngles(BoneAngle angles)
    {
        EstQ = BoneSetupMath.AngleQuaternion(angles);
    }

    /// <summary><c>SetQuaternion( q )</c> (3593-3596).</summary>
    public void SetQuaternion(Quaternion q)
    {
        EstQ = q;
    }

    /// <summary><c>SetNormal( normal )</c> (3603-3618): recalculate the foot
    /// angle from the slope of the surface — rebuild the footpad basis with
    /// forward/right orthogonal to the normal, then read the quaternion back
    /// (translation is a discarded <c>p1</c> in stock).</summary>
    public void SetNormal(Vector normal)
    {
        BoneMatrix3x4 m1 = BoneSetupMath.QuaternionMatrix(EstQ);
        // MatrixGetColumn( m1, 1, right ) on the row-major twin is column 1
        // = the Y components of the three rotation rows.
        Vector right = new(m1.Row0.Y, m1.Row1.Y, m1.Row2.Y);
        Vector forward = BoneSetupMath.CrossProduct(right, normal);
        right = BoneSetupMath.CrossProduct(normal, forward);
        // MatrixSetColumn( v, c, m ) writes the row .W-free basis slots:
        m1.Row0.X = forward.X; m1.Row1.X = forward.Y; m1.Row2.X = forward.Z;
        m1.Row0.Y = right.X; m1.Row1.Y = right.Y; m1.Row2.Y = right.Z;
        m1.Row0.Z = normal.X; m1.Row1.Z = normal.Y; m1.Row2.Z = normal.Z;
        (EstQ, _) = BoneSetupMath.MatrixAnglesQuaternion(m1);
    }

    /// <summary><c>SetPosWithNormalOffset( pos, normal )</c> (3626-3645):
    /// estimate the disc-centre ground impact — assume the edge of a
    /// Z-axis-aligned disc of <c>est.radius</c> touched the surface, so pull
    /// the centre back down the slope (clamped at 45 degrees).</summary>
    public void SetPosWithNormalOffset(Vector pos, Vector normal)
    {
        // assume it's a disc edge intersecting with the floor, so try to
        // estimate the z location of the center
        EstPos = pos;
        if (normal.Z > 0.9999f)
        {
            return;
        }
        // clamp at 45 degrees
        else if (normal.Z > 0.707f)
        {
            // tan == sin / cos
            float tan = MathF.Sqrt(1 - normal.Z * normal.Z) / normal.Z;
            EstPos.Z = EstPos.Z - EstRadius * tan;
        }
        else
        {
            EstPos.Z = EstPos.Z - EstRadius;
        }
    }

    /// <summary><c>SetOnWorld( bOnWorld )</c> (3651-3654).</summary>
    public void SetOnWorld(bool bOnWorld = true)
    {
        EstOnWorld = bOnWorld;
    }

    /// <summary><c>IsActive()</c> (3660-3663).</summary>
    public bool IsActive()
    {
        return EstFlWeight > 0.0f;
    }

    /// <summary><c>IKFailed()</c> (3669-3678): collapse the target back onto
    /// the ideal footplant and drop its influence.</summary>
    public void IKFailed()
    {
        LatchedDeltaPos = default;
        LatchedDeltaQ = default;
        LatchedPos = IdealPos;
        LatchedQ = IdealQ;
        EstLatched = 0.0f;
        EstFlWeight = 0.0f;
        EstOnWorld = false;
    }

    /// <summary><c>MoveReferenceFrame( deltaPos, deltaAngles )</c>
    /// (3685-3691): shift every accumulated position out of the moved frame.
    /// Stock reads only <c>deltaPos</c>; <c>deltaAngles</c> is an unused
    /// parameter (kept for parity).</summary>
    public void MoveReferenceFrame(Vector deltaPos, BoneAngle deltaAngles)
    {
        _ = deltaAngles; // stock ignores it
        EstPos -= deltaPos;
        LatchedPos -= deltaPos;
        OffsetPos -= deltaPos;
        IdealPos -= deltaPos;
    }

    /// <summary><c>AngleIMatrix( angles, position, mat )</c>
    /// (mathlib_base.cpp:1271-1280 + 1247-1268): the inverse of
    /// <see cref="BoneSetupMath.AngleMatrix(BoneAngle, Vector)"/>. Stock
    /// builds the TRANSPOSED AngleMatrix basis — its rows are the forward
    /// basis's columns — and only then sets the translation column to
    /// <c>-VectorRotate(position, mat)</c> over that transposed basis, i.e.
    /// <c>-Rᵀ·p</c>, the true inverse <c>[Rᵀ | -Rᵀ·p]</c>. The ported
    /// mathlib has no AngleIMatrix; this is the file-local twin (promotion
    /// requested — see needs-ik-client.md).</summary>
    private static BoneMatrix3x4 AngleIMatrix(BoneAngle angles, Vector position)
    {
        // AngleIMatrix's own basis (mathlib_base.cpp:1258-1267), same
        // SinCos extraction order as AngleMatrix; keeping stock's exact
        // products (cr*-sy etc.) so each float op is bit-identical.
        (float sy, float cy) = BoneSetupMath.SinCos(BoneSetupMath.DegToRad * angles.Yaw);
        (float sp, float cp) = BoneSetupMath.SinCos(BoneSetupMath.DegToRad * angles.Pitch);
        (float sr, float cr) = BoneSetupMath.SinCos(BoneSetupMath.DegToRad * angles.Roll);

        var m = new BoneMatrix3x4();
        m.Row0.X = cp * cy;
        m.Row0.Y = cp * sy;
        m.Row0.Z = -sp;
        m.Row1.X = sr * sp * cy + cr * -sy;
        m.Row1.Y = sr * sp * sy + cr * cy;
        m.Row1.Z = sr * cp;
        m.Row2.X = cr * sp * cy + -sr * -sy;
        m.Row2.Y = cr * sp * sy + -sr * cy;
        m.Row2.Z = cr * cp;

        // The translation comes AFTER the basis is built: stock's
        // VectorRotate(position, mat) dots the position with the (already
        // transposed) rows, giving Rᵀ·p, then negates it.
        Vector t = BoneSetupMath.VectorRotate(position, m);
        t = -t;
        m.Row0.W = t.X;
        m.Row1.W = t.Y;
        m.Row2.W = t.Z;
        return m;
    }

    /// <summary>Stock <c>QAngle::operator==</c> is the inherited
    /// <c>Vector</c> memberwise exact compare (vector.h:656-661); BoneAngle
    /// carries no ==, so compare the three fields.</summary>
    private static bool QAngleEquals(BoneAngle a, BoneAngle b) =>
        a.Pitch == b.Pitch && a.Yaw == b.Yaw && a.Roll == b.Roll;
}

/// <summary><c>class CIKContext</c> (bone_setup.h:271): accumulates the IK
/// rule and lock bookkeeping for one bone-setup pass so rules from many
/// blended sequences composite before the locks are solved.</summary>
public sealed class CIKContext
{
    /// <summary>Stock's <c>CUtlVectorFixed&lt;CIKTarget, 12&gt;</c> capacity:
    /// <c>m_target.SetSize(12)</c> is a hard size, not a growth — rules past
    /// the 12th share target slots (stock FIXME "Brutal hackery").</summary>
    private const int TargetCapacity = 12;

    private StudioHdr? _m_pStudioHdr;
    private BoneMatrix3x4 _m_rootxform;
    private int _m_iFramecounter;
    private float _m_flTime;
    private int _m_boneMask;

    /// <summary><c>m_ikChainRule</c>: per-chain stored rules. Server rows
    /// only write it (AddDependencies); the readers (UpdateTargets,
    /// SolveDependencies) are CLIENT_DLL.</summary>
    private List<IkContextIkRule>[] _m_ikChainRule = Array.Empty<List<IkContextIkRule>>();

    /// <summary><c>m_ikLock</c>: the accumulated lock rules, autoplay first
    /// then per sequence pass (stock appends; server never calls AddAllLocks
    /// so the two lock kinds never share an instance in the server flow).</summary>
    private readonly List<IkContextIkRule> _m_ikLock = new();

    /// <summary><c>m_target</c>: the 12-slot fixed target vector, empty
    /// until the first rule forces it into existence (stock's grow hack).</summary>
    private CikTarget[] _m_target = Array.Empty<CikTarget>();

    // The targets a reset context gave up, handed back (each reset to new)
    // the next time the stock grow hack would allocate twelve (ResetToNew).
    private CikTarget[]? _spareTargets;

    // The per-chain lists Init hands m_ikChainRule, kept for the next Init.
    private List<IkContextIkRule>[] _m_ikChainRuleHeld = Array.Empty<List<IkContextIkRule>>();

    /// <summary>The stock grow hack's twelve targets, fresh (as <c>new</c>
    /// made them) -- the spares a reset left when there are any.</summary>
    private CikTarget[] NewTargets()
    {
        CikTarget[]? spare = _spareTargets;

        if (spare is not null)
        {
            _spareTargets = null;

            for (int i = 0; i < spare.Length; i++)
            {
                spare[i].ResetToNew();
            }

            return spare;
        }

        var targets = new CikTarget[TargetCapacity]; // fresh array is memset-zero
        for (int i = 0; i < TargetCapacity; i++)
        {
            targets[i] = new CikTarget();
        }

        return targets;
    }

    /// <summary>
    /// The context as <c>new CIKContext()</c> left it, keeping its storage:
    /// a context a build rents from its scratch (the autoplay pass's, a
    /// sequence's lock context) instead of constructing one per pass. Every
    /// field the constructor and Init write is written back.
    /// </summary>
    internal void ResetToNew()
    {
        _m_pStudioHdr = null;
        _m_rootxform = default;
        _m_iFramecounter = -1;
        _m_flTime = -1.0f;
        _m_boneMask = 0;
        _m_ikChainRule = Array.Empty<List<IkContextIkRule>>();
        _m_ikLock.Clear();

        if (_m_target.Length != 0)
        {
            _spareTargets = _m_target;
        }

        _m_target = Array.Empty<CikTarget>();
    }

    /// <summary><c>CIKContext()</c> (3216-3223): EnsureCapacity(12) is a
    /// managed no-op; framecounter -1, hdr null, flTime -1, target size 0.</summary>
    public CIKContext()
    {
        // m_target.EnsureCapacity( 12 );  // FIXME (stock): shouldn't it be grown?
        _m_iFramecounter = -1;
        _m_pStudioHdr = null;
        _m_flTime = -1.0f;
        _m_target = Array.Empty<CikTarget>();
    }

    /// <summary><c>Init( pStudioHdr, angles, pos, flTime, iFramecounter,
    /// boneMask )</c> (3226-3251). The pinned port shape takes the root
    /// transform pre-multiplied (stock: <c>AngleMatrix(angles, pos,
    /// m_rootxform)</c>). Stock binds flTime/iFramecounter, which
    /// AddDependencies' STUDIO_REALTIME arm reads; callers pass the
    /// CBoneSetup's values (default -1 covers the stock no-IK path).</summary>
    public void Init(
        StudioHdr hdr, in BoneMatrix3x4 rootXform, int boneMask,
        float flTime = -1f, int iFramecounter = -1)
    {
        _m_pStudioHdr = hdr;
        _m_ikChainRule = Array.Empty<List<IkContextIkRule>>(); // RemoveAll
        if (hdr.NumIKChains != 0)
        {
            // RemoveAll, then one empty list per chain: the last Init's lists
            // emptied when the chain count is the same, which is the same
            // empty lists without the garbage.
            if (_m_ikChainRuleHeld.Length != hdr.NumIKChains)
            {
                _m_ikChainRuleHeld = new List<IkContextIkRule>[hdr.NumIKChains];
                for (int i = 0; i < _m_ikChainRuleHeld.Length; i++)
                {
                    _m_ikChainRuleHeld[i] = new List<IkContextIkRule>();
                }
            }
            else
            {
                for (int i = 0; i < _m_ikChainRuleHeld.Length; i++)
                {
                    _m_ikChainRuleHeld[i].Clear();
                }
            }

            _m_ikChainRule = _m_ikChainRuleHeld;

            // FIXME (stock): Brutal hackery to prevent a crash
            if (_m_target.Length == 0)
            {
                _m_target = NewTargets();
                ClearTargets();
            }
        }
        else
        {
            _m_target = Array.Empty<CikTarget>();
        }

        _m_rootxform = rootXform;
        _m_iFramecounter = iFramecounter;
        _m_flTime = flTime;
        _m_boneMask = boneMask;
    }

    /// <summary><c>AddDependencies( seqdesc, iSequence, flCycle,
    /// poseParameters, flWeight )</c> (3253-3335). Normalises the cycle
    /// (realtime clock, looping wrap, or non-looping clamp), builds the blend
    /// cell, and for each IK rule that survives
    /// <see cref="BoneSetupIk.StudioIkSequenceError"/> and the boneMask
    /// filters, stores it on its chain — a rule blending to full weight
    /// first clears the chain (and an IK_RELEASE at full weight drops it
    /// instead). flCycle defaults to 0 (stock's callers always pass a cycle;
    /// the default eases pinned-shape callers with no cycle to report).</summary>
    public void AddDependencies(
        int iSequence, float[] poseParameter, float flWeight, float flCycle = 0f)
    {
        StudioHdr hdr = RequireHdr();

        if (hdr.NumIKChains == 0)
        {
            return;
        }

        MstudioSeqDesc seqdesc = hdr.SeqDesc(iSequence);

        if (seqdesc.NumIKRules == 0)
        {
            return;
        }

        // Assert( flWeight >= 0 && <= 1 ); this shouldn't be necessary, but
        // the Assert should help us catch whoever is screwing this up.
        flWeight = Math.Clamp(flWeight, 0.0f, 1.0f);

        // unify this
        if ((seqdesc.Flags & StudioConstants.STUDIO_REALTIME) != 0)
        {
            float cps = BoneSetupHdrRead.Cps(hdr, seqdesc, iSequence, poseParameter);
            flCycle = _m_flTime * cps;
            flCycle = flCycle - (int)flCycle;
        }
        else if (flCycle < 0 || flCycle >= 1)
        {
            if ((seqdesc.Flags & StudioConstants.STUDIO_LOOPING) != 0)
            {
                flCycle = flCycle - (int)flCycle;
                if (flCycle < 0)
                {
                    flCycle += 1;
                }
            }
            else
            {
                flCycle = Math.Clamp(flCycle, 0.0f, 0.9999f);
            }
        }

        BoneSetupHdrRead.SeqAnims(hdr, seqdesc, iSequence, poseParameter,
            out MstudioAnimDesc[] panim, out float[] weight);

        // FIXME (stock): add proper number of rules!!!
        for (int i = 0; i < seqdesc.NumIKRules; i++)
        {
            if (!BoneSetupIk.StudioIkSequenceError(
                    hdr, seqdesc, iSequence, flCycle, i, poseParameter, panim, weight,
                    out IkContextIkRule ikrule))
            {
                continue;
            }

            // don't add rule if the bone isn't going to be calculated
            int bone = hdr.IkChain(ikrule.Chain).Link(2).Bone;
            if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
            {
                continue;
            }

            // or if its relative bone isn't going to be calculated
            if (ikrule.Bone >= 0 && (hdr.BoneFlags(ikrule.Bone) & _m_boneMask) == 0)
            {
                continue;
            }

            // FIXME (stock): Brutal hackery to prevent a crash
            if (_m_target.Length == 0)
            {
                _m_target = NewTargets();
                ClearTargets();
            }

            ikrule.FlRuleWeight = flWeight;

            if (ikrule.FlRuleWeight * ikrule.FlWeight > 0.999f)
            {
                if (ikrule.Type != StudioConstants.IK_UNLATCH)
                {
                    // clear out chain if rule is 100%
                    _m_ikChainRule[ikrule.Chain].Clear();
                    if (ikrule.Type == StudioConstants.IK_RELEASE)
                    {
                        continue;
                    }
                }
            }

            _m_ikChainRule[ikrule.Chain].Add(ikrule);
        }
    }

    /// <summary><c>AddAutoplayLocks( pos, q )</c> (3343-3391): for every
    /// autoplay iklock that survives the boneMask, record the current world
    /// pose of the chain's end bone (from the pos/q arrays) as an IK_WORLD
    /// lock rule, plus the knee direction (and knee position, which the
    /// autoplay variant does store).</summary>
    public void AddAutoplayLocks(Vector[] pos, Quaternion[] q)
    {
        StudioHdr hdr = RequireHdr();

        // skip all array access if no autoplay locks.
        if (hdr.NumAutoPlayLocks == 0)
        {
            return;
        }

        BoneMatrix3x4[] boneToWorld = RentMatrices();
        BoneSetupExtract.BoneBitList boneComputed = new();
        try
        {
            int ikOffset = _m_ikLock.Count;
            for (int i = 0; i < hdr.NumAutoPlayLocks; i++)
            {
                _m_ikLock.Add(new IkContextIkRule()); // memset of the tail block
            }

            for (int i = 0; i < hdr.NumAutoPlayLocks; i++)
            {
                MstudioIkLock lock_ = hdr.AutoPlayLock(i);
                MstudioIkChain pchain = hdr.IkChain(lock_.Chain);
                int bone = pchain.Link(2).Bone;

                // don't bother with iklock if the bone isn't going to be calculated
                if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
                {
                    continue;
                }

                // eval current ik'd bone
                BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);

                IkContextIkRule ikrule = _m_ikLock[i + ikOffset];

                ikrule.Chain = lock_.Chain;
                ikrule.Slot = i;
                ikrule.Type = StudioConstants.IK_WORLD;

                (ikrule.Q, ikrule.Pos) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[bone]);

                // save off current knee direction
                if (BoneSetupMath.VectorLengthSqr(pchain.Link(0).KneeDir) > 0.0f)
                {
                    ikrule.KneeDir = BoneSetupMath.VectorRotate(
                        pchain.Link(0).KneeDir, boneToWorld[pchain.Link(0).Bone]);
                    ikrule.KneePos = boneToWorld[pchain.Link(1).Bone].Position;
                }
                else
                {
                    ikrule.KneeDir = default;
                }
            }
        }
        finally
        {
            ReturnMatrices(boneToWorld);
        }
    }

    /// <summary><c>AddSequenceLocks( seqdesc, pos, q )</c> (3395-3450). The
    /// sequence twin; the port resolves <c>seqdesc</c> from
    /// <c>iSequence</c>. Two stock quirks are reproduced verbatim: the rule
    /// stores <c>chain = i</c> (the loop index, not <c>plock-&gt;chain</c> —
    /// stock line 3430), and unlike the autoplay variant this one never
    /// stores <c>kneePos</c>, which stays zero.</summary>
    public void AddSequenceLocks(int iSequence, Vector[] pos, Quaternion[] q)
    {
        StudioHdr hdr = RequireHdr();

        if (hdr.NumIKChains == 0)
        {
            return;
        }

        MstudioSeqDesc seqdesc = hdr.SeqDesc(iSequence);

        if (seqdesc.NumIKLocks == 0)
        {
            return;
        }

        BoneMatrix3x4[] boneToWorld = RentMatrices();
        BoneSetupExtract.BoneBitList boneComputed = new();
        try
        {
            int ikOffset = _m_ikLock.Count;
            for (int i = 0; i < seqdesc.NumIKLocks; i++)
            {
                _m_ikLock.Add(new IkContextIkRule());
            }

            for (int i = 0; i < seqdesc.NumIKLocks; i++)
            {
                MstudioIkLock? plock = seqdesc.PIKLock(i);
                if (plock is null)
                {
                    // stock NULL-derefs here; a well-formed model with
                    // numiklocks != 0 has the table. Skip (documented).
                    continue;
                }

                MstudioIkChain pchain = hdr.IkChain(plock.Chain);
                int bone = pchain.Link(2).Bone;

                // don't bother with iklock if the bone isn't going to be calculated
                if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
                {
                    continue;
                }

                // eval current ik'd bone
                BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);

                IkContextIkRule ikrule = _m_ikLock[i + ikOffset];
                ikrule.Chain = i;   // stock quirk: the index, not plock->chain
                ikrule.Slot = i;
                ikrule.Type = StudioConstants.IK_WORLD;

                (ikrule.Q, ikrule.Pos) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[bone]);

                // save off current knee direction
                if (BoneSetupMath.VectorLengthSqr(pchain.Link(0).KneeDir) > 0.0f)
                {
                    ikrule.KneeDir = BoneSetupMath.VectorRotate(
                        pchain.Link(0).KneeDir, boneToWorld[pchain.Link(0).Bone]);
                }
                else
                {
                    ikrule.KneeDir = default;
                }
            }
        }
        finally
        {
            ReturnMatrices(boneToWorld);
        }
    }

    /// <summary>Member <c>BuildBoneChain</c> thunk (3452-3461) over the
    /// extract cluster's core: asserts the bone is in the mask (no-op in the
    /// port) and forwards with the bound root transform and a temporary
    /// computed-list. The pinned public shape; internal callers that loop
    /// use the private overload to share one list like stock.</summary>
    public void BuildBoneChain(
        Vector[] pos, Quaternion[] q, Span<BoneMatrix3x4> boneToWorld, int bone)
    {
        BuildBoneChain(pos, q, bone, boneToWorld, new BoneSetupExtract.BoneBitList());
    }

    private void BuildBoneChain(
        Vector[] pos, Quaternion[] q, int bone,
        Span<BoneMatrix3x4> boneToWorld, BoneSetupExtract.BoneBitList boneComputed)
    {
        StudioHdr hdr = RequireHdr();
        // Assert( hdr->boneFlags( iBone ) & m_boneMask );
        BoneSetupExtract.BuildBoneChain(
            hdr, _m_rootxform, pos, q, bone, boneToWorld, boneComputed);
    }

    /// <summary><c>ClearTargets()</c> (3699-3706): invalidate every target by
    /// stamping its latch frame far into the past.</summary>
    public void ClearTargets()
    {
        for (int i = 0; i < _m_target.Length; i++)
        {
            _m_target[i].LatchedIFramecounter = -9999;
        }
    }

    // ---- client cluster (CLIENT_DLL stock rows; oracle 3949-4394) --------
    // Stock guards these bodies with #ifdef CLIENT_DLL ("server gc cull:
    // server.so never reaches this"); the port exposes them unconditionally
    // like the rest of the managed cluster.

    /// <summary><c>ikchainresult_t</c> (bone_setup.h:206-213): the per-chain
    /// solve accumulator of SolveDependencies.</summary>
    private sealed class IkChainResult
    {
        public int Target = -1;
        public Vector Pos;
        public Quaternion Q;
        public float FlWeight;
    }

    /// <summary><c>AutoIKRelease()</c> (oracle 3949-4042): for every target
    /// whose error timer is young (<c>dt = flTime - error.flTime &lt; 0.5</c>)
    /// or flagged, ramp <c>error.ramp</c> toward 1 (dt &lt; 0.25, at 4/sec,
    /// clamped) or toward 0 (else, 4/sec), and while the ramp is up append an
    /// IK_RELEASE rule with a SimpleSpline(ramp) weight to the target's
    /// chain — unless the chain's foot bone is outside the boneMask, which
    /// clears bInError and skips the timestamp stamp (stock's <c>continue</c>
    /// sits above the tail assignment). Stock's four DevWarning arms (NULL
    /// pchain / NULL pBone / out-of-range bone / out-of-range chain —
    /// malformed hdr only) add no rule and fall through to the tail.</summary>
    public void AutoIKRelease()
    {
        StudioHdr hdr = RequireHdr();

        for (int i = 0; i < _m_target.Length; i++)
        {
            CikTarget pTarget = _m_target[i];

            float dt = _m_flTime - pTarget.ErrorFlTime;
            if (!(pTarget.ErrorBInError || dt < 0.5f))
            {
                continue;
            }

            if (!pTarget.ErrorBInError)
            {
                pTarget.ErrorRamp = 0.0f;
                pTarget.ErrorFlErrorTime = pTarget.ErrorFlTime;
                pTarget.ErrorBInError = true;
            }

            float ft = _m_flTime - pTarget.ErrorFlErrorTime;
            if (dt < 0.25f)
            {
                pTarget.ErrorRamp = MathF.Min(pTarget.ErrorRamp + ft * 4.0f, 1.0f);
            }
            else
            {
                pTarget.ErrorRamp = MathF.Max(pTarget.ErrorRamp - ft * 4.0f, 0.0f);
            }

            if (pTarget.ErrorRamp > 0.0f)
            {
                IkContextIkRule ikrule = new()
                {
                    Chain = pTarget.Chain,
                    Bone = 0,
                    Type = StudioConstants.IK_RELEASE,
                    Slot = i,
                    FlWeight = BoneSetupMath.SimpleSpline(pTarget.ErrorRamp),
                    FlRuleWeight = 1.0f,
                    Latched = dt < 0.25f ? 0.0f : BoneSetupMath.SimpleSpline(pTarget.ErrorRamp),
                };

                // don't bother with AutoIKRelease if the bone isn't going to
                // be calculated; this code is crashing for some unknown reason.
                if (pTarget.Chain >= 0 && pTarget.Chain < hdr.NumIKChains)
                {
                    MstudioIkChain pchain = hdr.IkChain(pTarget.Chain);
                    int bone = pchain.Link(2).Bone;
                    if (bone >= 0 && bone < hdr.NumBones)
                    {
                        if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
                        {
                            pTarget.ErrorBInError = false;
                            continue; // stock skips the flErrorTime stamp too
                        }

                        _m_ikChainRule[pTarget.Chain].Add(ikrule);
                    }
                    // else: stock DevWarning "out of range bone" — no rule.
                }
                // else: stock DevWarning "NULL pchain" / "out of range chain"
                // (managed readers make pchain/pBone non-null or throw).
            }
            else
            {
                pTarget.ErrorBInError = false;
            }

            pTarget.ErrorFlErrorTime = _m_flTime;
        }
    }

    /// <summary><c>UpdateTargets( pos, q, boneToWorld, boneComputed )</c>
    /// (oracle 3713-3942): reset every target's estimate, run AutoIKRelease,
    /// fold the stored rules into the targets (ATTACHMENT/GROUND overwrite or
    /// slerp/lerp-blend, UNLATCH/RELEASE decay latched and flWeight), then
    /// for each live target build its chain foot, compute the world footpad,
    /// run the latch bookkeeping (needsLatch, the framecounter staleness
    /// gate, ideal/est pose, the influence-decay ramp of the latched delta),
    /// the too-far / too-twisted error flags, and the trace reachability
    /// bookkeeping. <c>boneToWorld</c>/<c>boneComputed</c> arrive from the
    /// caller exactly as in stock (the future CBoneSetup client pass and the
    /// facts own the scratch).</summary>
    public void UpdateTargets(
        Vector[] pos, Quaternion[] q,
        BoneMatrix3x4[] boneToWorld, BoneSetupExtract.BoneBitList boneComputed)
    {
        StudioHdr hdr = RequireHdr();

        for (int i = 0; i < _m_target.Length; i++)
        {
            CikTarget t = _m_target[i];
            t.EstFlWeight = 0.0f;
            t.EstLatched = 1.0f;
            t.EstRelease = 1.0f;
            t.EstHeight = 0.0f;
            t.EstFloor = 0.0f;
            t.EstRadius = 0.0f;
            t.OffsetPos = default;
            t.OffsetQ = default;
        }

        AutoIKRelease();

        for (int j = 0; j < _m_ikChainRule.Length; j++)
        {
            List<IkContextIkRule> rules = _m_ikChainRule[j];
            for (int i = 0; i < rules.Count; i++)
            {
                IkContextIkRule pRule = rules[i];
                switch (pRule.Type)
                {
                    case StudioConstants.IK_ATTACHMENT:
                    case StudioConstants.IK_GROUND:
                    {
                        CikTarget pTarget = _m_target[pRule.Slot];
                        pTarget.Chain = pRule.Chain;
                        pTarget.Type = pRule.Type;

                        pTarget.OffsetPAttachmentName =
                            pRule.Type == StudioConstants.IK_ATTACHMENT ? pRule.SzLabel : null;

                        if (pRule.FlRuleWeight == 1.0f || pTarget.EstFlWeight == 0.0f)
                        {
                            pTarget.OffsetQ = pRule.Q;
                            pTarget.OffsetPos = pRule.Pos;
                            pTarget.EstHeight = pRule.Height;
                            pTarget.EstFloor = pRule.Floor;
                            pTarget.EstRadius = pRule.Radius;
                            pTarget.EstLatched = pRule.Latched * pRule.FlRuleWeight;
                            pTarget.EstRelease = pRule.Release;
                            pTarget.EstFlWeight = pRule.FlWeight * pRule.FlRuleWeight;
                        }
                        else
                        {
                            pTarget.OffsetQ = BoneSetupMath.QuaternionSlerp(
                                pTarget.OffsetQ, pRule.Q, pRule.FlRuleWeight);
                            pTarget.OffsetPos = BoneSetupMath.VectorLerp(
                                pTarget.OffsetPos, pRule.Pos, pRule.FlRuleWeight);
                            pTarget.EstHeight = BoneSetupMath.Lerp(
                                pRule.FlRuleWeight, pTarget.EstHeight, pRule.Height);
                            pTarget.EstFloor = BoneSetupMath.Lerp(
                                pRule.FlRuleWeight, pTarget.EstFloor, pRule.Floor);
                            pTarget.EstRadius = BoneSetupMath.Lerp(
                                pRule.FlRuleWeight, pTarget.EstRadius, pRule.Radius);
                            //pTarget->est.latched = Lerp( flRuleWeight, est.latched, rule.latched );
                            pTarget.EstLatched = MathF.Min(pTarget.EstLatched, pRule.Latched);
                            pTarget.EstRelease = BoneSetupMath.Lerp(
                                pRule.FlRuleWeight, pTarget.EstRelease, pRule.Release);
                            pTarget.EstFlWeight = BoneSetupMath.Lerp(
                                pRule.FlRuleWeight, pTarget.EstFlWeight, pRule.FlWeight);
                        }

                        if (pRule.Type == StudioConstants.IK_GROUND)
                        {
                            pTarget.LatchedDeltaPos.Z = 0.0f;
                            pTarget.EstPos.Z = pTarget.EstFloor + _m_rootxform.Row2.W;
                        }
                    }
                    break;

                    case StudioConstants.IK_UNLATCH:
                    {
                        CikTarget pTarget = _m_target[pRule.Slot];
                        if (pRule.Latched > 0.0f)
                        {
                            pTarget.EstLatched = 0.0f;
                        }
                        else
                        {
                            pTarget.EstLatched = MathF.Min(pTarget.EstLatched, 1.0f - pRule.FlWeight);
                        }
                    }
                    break;

                    case StudioConstants.IK_RELEASE:
                    {
                        CikTarget pTarget = _m_target[pRule.Slot];
                        if (pRule.Latched > 0.0f)
                        {
                            pTarget.EstLatched = 0.0f;
                        }
                        else
                        {
                            pTarget.EstLatched = MathF.Min(pTarget.EstLatched, 1.0f - pRule.FlWeight);
                        }

                        pTarget.EstFlWeight = pTarget.EstFlWeight *
                            (1.0f - pRule.FlWeight * pRule.FlRuleWeight);
                    }
                    break;
                }
            }
        }

        for (int i = 0; i < _m_target.Length; i++)
        {
            CikTarget pTarget = _m_target[i];
            if (pTarget.EstFlWeight <= 0.0f)
            {
                continue;
            }

            MstudioIkChain pchain = hdr.IkChain(pTarget.Chain);
            int bone = pchain.Link(2).Bone;

            // eval current ik'd bone
            BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);

            // xform IK target error into world space
            BoneMatrix3x4 local = BoneSetupMath.QuaternionMatrix(pTarget.OffsetQ, pTarget.OffsetPos);
            local = BoneSetupMath.MatrixInvert(local);
            BoneMatrix3x4 worldFootpad = BoneSetupMath.ConcatTransforms(boneToWorld[bone], local);

            pTarget.LatchedBNeedsLatch = pTarget.EstLatched == 1.0f;

            // disable latched position if it looks invalid
            if (_m_iFramecounter < 0
                || pTarget.LatchedIFramecounter < _m_iFramecounter - 1
                || pTarget.LatchedIFramecounter > _m_iFramecounter)
            {
                pTarget.LatchedBHasLatch = false;
                pTarget.LatchedInfluence = 0.0f;
            }
            pTarget.LatchedIFramecounter = _m_iFramecounter;

            // find ideal contact position
            (pTarget.IdealQ, pTarget.IdealPos) = BoneSetupMath.MatrixAnglesQuaternion(worldFootpad);
            pTarget.EstQ = pTarget.IdealQ;
            pTarget.EstPos = pTarget.IdealPos;

            float latched = pTarget.EstLatched;

            if (pTarget.LatchedBHasLatch)
            {
                if (pTarget.EstLatched == 1.0f)
                {
                    // keep track of latch position error from ideal contact position
                    pTarget.LatchedDeltaPos = pTarget.LatchedPos - pTarget.EstPos;
                    pTarget.LatchedDeltaQ = BoneSetupMath.QuaternionSM(
                        -1.0f, pTarget.EstQ, pTarget.LatchedQ);
                    pTarget.EstQ = pTarget.LatchedQ;
                    pTarget.EstPos = pTarget.LatchedPos;
                }
                else if (pTarget.EstLatched > 0.0f)
                {
                    // ramp out latch differences during decay phase of rule
                    if (latched > 0.0f && latched < pTarget.LatchedInfluence)
                    {
                        // latching has decreased
                        float decay = pTarget.LatchedInfluence - latched;
                        if (pTarget.LatchedInfluence > 0.0f)
                        {
                            decay = decay / pTarget.LatchedInfluence;
                        }

                        pTarget.LatchedDeltaPos = BoneSetupMath.VectorScale(
                            pTarget.LatchedDeltaPos, 1.0f - decay);
                        pTarget.LatchedDeltaQ = BoneSetupMath.QuaternionScale(
                            pTarget.LatchedDeltaQ, 1.0f - decay);
                    }

                    // move ideal contact position by latched error factor
                    pTarget.EstPos = pTarget.EstPos + pTarget.LatchedDeltaPos;
                    pTarget.EstQ = BoneSetupMath.QuaternionMA(
                        pTarget.EstQ, 1.0f, pTarget.LatchedDeltaQ);
                    pTarget.LatchedQ = pTarget.EstQ;
                    pTarget.LatchedPos = pTarget.EstPos;
                }
                else
                {
                    pTarget.LatchedBHasLatch = false;
                    pTarget.LatchedQ = pTarget.EstQ;
                    pTarget.LatchedPos = pTarget.EstPos;
                    pTarget.LatchedDeltaPos = default;
                    pTarget.LatchedDeltaQ = default;
                }
                pTarget.LatchedInfluence = latched;
            }

            // check for illegal requests
            Vector p1 = boneToWorld[pchain.Link(0).Bone].Position; // hip
            Vector p2 = boneToWorld[pchain.Link(1).Bone].Position; // knee
            Vector p3 = boneToWorld[pchain.Link(2).Bone].Position; // foot

            float d1 = (p2 - p1).Length();
            float d2 = (p3 - p2).Length();

            if (pTarget.LatchedBHasLatch)
            {
                float d4 = (p3 + pTarget.LatchedDeltaPos - p1).Length();

                // unstick feet when distance is too great
                if ((d4 < MathF.Abs(d1 - d2) || d4 * 0.95f > d1 + d2) && pTarget.EstLatched > 0.2f)
                {
                    pTarget.ErrorFlTime = _m_flTime;
                }

                // unstick feet when angle is too great
                if (pTarget.EstLatched > 0.2f)
                {
                    // QuaternionDotProduct( latched.q, est.q ): stock takes the
                    // |w| shortcut instead.
                    float d = MathF.Abs(pTarget.LatchedDeltaQ.W) * 2.0f - 1.0f;

                    // FIXME (stock): cos(45), make property of chain
                    if (d < 0.707f)
                    {
                        pTarget.ErrorFlTime = _m_flTime;
                    }
                }
            }

            Vector footDir = pTarget.EstPos - p1;
            // stock's VectorNormalize returns the pre-normalise length; the
            // port returns the vector, so read the length separately (same
            // scalar-sqrt twin, zero-vector -> 0 like stock's len2 == 0 arm).
            pTarget.TraceHipToFoot = BoneSetupMath.VectorLength(footDir);
            footDir = BoneSetupMath.VectorNormalize(footDir);
            pTarget.TraceHipToKnee = d1;
            pTarget.TraceKneeToFoot = d2;
            pTarget.TraceHip = p1;
            pTarget.TraceKnee = p2;
            pTarget.TraceClosest = p1 + footDir * (MathF.Abs(d1 - d2) * 1.01f);
            pTarget.TraceFarthest = p1 + footDir * (d1 + d2) * 0.99f;
            pTarget.TraceLowest = p1 + new Vector(0, 0, -1) * (d1 + d2) * 0.99f;
            // pTarget->trace.endpos = pTarget->est.pos;
        }
    }

    /// <summary><c>SolveDependencies( pos, q, boneToWorld, boneComputed )</c>
    /// (oracle 4046-4264): seed one accumulator per chain from the current
    /// pose, fold in IK_SELF rules (world target = rule frame under its
    /// relative bone or the root transform, blended by
    /// <c>flWeight * flRuleWeight</c>) and IK_RELEASE rules (blend back
    /// toward the pose), then the live targets (worldTarget = footpad *
    /// offset, est.flWeight replaces the accumulator weight outright), and
    /// finally run the two-bone solver for every chain the accumulated
    /// weight is non-zero for — on accept slam the solved quaternion onto
    /// the solved position and rebuild the chain bottom-up, on reject scale
    /// the (never-assigned, so never-referenced-nonzero) target's latched
    /// deltas by 0.8. WORLD asserts and drops out; ATTACHMENT/GROUND/UNLATCH
    /// contribute nothing here.</summary>
    public void SolveDependencies(
        Vector[] pos, Quaternion[] q,
        BoneMatrix3x4[] boneToWorld, BoneSetupExtract.BoneBitList boneComputed)
    {
        StudioHdr hdr = RequireHdr();

        // ASSERT_NO_REENTRY();
        // stock's stack array of 32 ("allocate!!!") indexed by chain; the
        // managed port sizes it to the chain table (DIVERGENCE malformed-data
        // only: numikchains > 32 is stock's stack overrun — see NA ledger).
        IkChainResult[] chainResult = new IkChainResult[hdr.NumIKChains];

        // init chain rules
        for (int i = 0; i < hdr.NumIKChains; i++)
        {
            MstudioIkChain pchain = hdr.IkChain(i);
            IkChainResult pChainResult = new();
            chainResult[i] = pChainResult;
            int bone = pchain.Link(2).Bone;

            pChainResult.Target = -1;
            pChainResult.FlWeight = 0.0f;

            // don't bother with chain if the bone isn't going to be calculated
            if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
            {
                continue;
            }

            // eval current ik'd bone
            BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);

            (pChainResult.Q, pChainResult.Pos) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[bone]);
        }

        for (int j = 0; j < _m_ikChainRule.Length; j++)
        {
            List<IkContextIkRule> rules = _m_ikChainRule[j];
            for (int i = 0; i < rules.Count; i++)
            {
                IkContextIkRule pRule = rules[i];
                if (pRule.Chain < 0 || pRule.Chain >= chainResult.Length)
                {
                    continue; // stock's OOB stack read, hardened (NA ledger)
                }
                IkChainResult pChainResult = chainResult[pRule.Chain];
                pChainResult.Target = -1;

                switch (pRule.Type)
                {
                    case StudioConstants.IK_SELF:
                    {
                        // xform IK target error into world space
                        BoneMatrix3x4 local = BoneSetupMath.QuaternionMatrix(pRule.Q, pRule.Pos);
                        // eval target bone space
                        BoneMatrix3x4 worldTarget;
                        if (pRule.Bone != -1)
                        {
                            BuildBoneChain(pos, q, pRule.Bone, boneToWorld, boneComputed);
                            worldTarget = BoneSetupMath.ConcatTransforms(boneToWorld[pRule.Bone], local);
                        }
                        else
                        {
                            worldTarget = BoneSetupMath.ConcatTransforms(_m_rootxform, local);
                        }

                        float flWeight = pRule.FlWeight * pRule.FlRuleWeight;
                        pChainResult.FlWeight = pChainResult.FlWeight * (1.0f - flWeight) + flWeight;

                        // target p and q
                        (Quaternion q2, Vector p2) = BoneSetupMath.MatrixAnglesQuaternion(worldTarget);

                        // blend in position and angles
                        pChainResult.Pos = pChainResult.Pos * (1.0f - flWeight) + p2 * flWeight;
                        pChainResult.Q = BoneSetupMath.QuaternionSlerp(pChainResult.Q, q2, flWeight);
                    }
                    break;

                    case StudioConstants.IK_WORLD:
                        // Assert( 0 )
                        break;

                    case StudioConstants.IK_ATTACHMENT:
                    case StudioConstants.IK_GROUND:
                        break;

                    case StudioConstants.IK_RELEASE:
                    {
                        // move target back towards original location
                        float flWeight = pRule.FlWeight * pRule.FlRuleWeight;
                        MstudioIkChain pchain = hdr.IkChain(pRule.Chain);
                        int bone = pchain.Link(2).Bone;

                        BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);
                        (Quaternion q2, Vector p2) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[bone]);

                        // blend in position and angles
                        pChainResult.Pos = pChainResult.Pos * (1.0f - flWeight) + p2 * flWeight;
                        pChainResult.Q = BoneSetupMath.QuaternionSlerp(pChainResult.Q, q2, flWeight);
                    }
                    break;

                    case StudioConstants.IK_UNLATCH:
                        // stock body is commented out entirely
                        break;
                }
            }
        }

        for (int i = 0; i < _m_target.Length; i++)
        {
            CikTarget pTarget = _m_target[i];

            if (pTarget.EstFlWeight > 0.0f)
            {
                //mstudioikchain_t *pchain = hdr->pIKChain( target->chain );
                IkChainResult pChainResult = chainResult[pTarget.Chain];

                BoneMatrix3x4 local = BoneSetupMath.AngleMatrix(
                    BoneSetupMath.QuaternionAngles(pTarget.OffsetQ), pTarget.OffsetPos);

                BoneMatrix3x4 worldFootpad = BoneSetupMath.AngleMatrix(
                    BoneSetupMath.QuaternionAngles(pTarget.EstQ), pTarget.EstPos);

                BoneMatrix3x4 worldTarget = BoneSetupMath.ConcatTransforms(worldFootpad, local);

                // target p and q
                (Quaternion q2, Vector p2) = BoneSetupMath.MatrixAnglesQuaternion(worldTarget);

                // blend in position and angles
                pChainResult.FlWeight = pTarget.EstFlWeight;
                pChainResult.Pos = pChainResult.Pos * (1.0f - pChainResult.FlWeight) + p2 * pChainResult.FlWeight;
                pChainResult.Q = BoneSetupMath.QuaternionSlerp(pChainResult.Q, q2, pChainResult.FlWeight);
            }

            if (pTarget.LatchedBNeedsLatch)
            {
                // keep track of latch position
                pTarget.LatchedBHasLatch = true;
                pTarget.LatchedQ = pTarget.EstQ;
                pTarget.LatchedPos = pTarget.EstPos;
            }
        }

        for (int i = 0; i < hdr.NumIKChains; i++)
        {
            IkChainResult pChainResult = chainResult[i];
            MstudioIkChain pchain = hdr.IkChain(i);

            if (pChainResult.FlWeight <= 0.0f)
            {
                continue;
            }

            // do exact IK solution (FIXME (stock): once per link!)
            if (BoneSetupIk.StudioSolveIk(pchain, pChainResult.Pos, boneToWorld))
            {
                Vector p3 = boneToWorld[pchain.Link(2).Bone].Position; // MatrixGetColumn 3
                boneToWorld[pchain.Link(2).Bone] = BoneSetupMath.QuaternionMatrix(pChainResult.Q, p3);

                // rebuild chain (FIXME (stock): needed if everyone past this
                // uses the boneToWorld array?)
                BoneSetupIk.SolveBone(hdr, pchain.Link(2).Bone, boneToWorld, pos, q);
                BoneSetupIk.SolveBone(hdr, pchain.Link(1).Bone, boneToWorld, pos, q);
                BoneSetupIk.SolveBone(hdr, pchain.Link(0).Bone, boneToWorld, pos, q);
            }
            else
            {
                // FIXME (stock): need to invalidate the targets that forced
                // this... `target` is never assigned anything but -1, so this
                // arm is dead in stock too; ported verbatim.
                if (pChainResult.Target != -1)
                {
                    CikTarget pTarget = _m_target[pChainResult.Target];
                    pTarget.LatchedDeltaPos = BoneSetupMath.VectorScale(pTarget.LatchedDeltaPos, 0.8f);
                    pTarget.LatchedDeltaQ = BoneSetupMath.QuaternionScale(pTarget.LatchedDeltaQ, 0.8f);
                }
            }
        }
    }

    /// <summary><c>AddAllLocks( pos, q )</c> (oracle 4318-4365): append one
    /// zeroed lock per chain and record the current pose of each chain's end
    /// bone as an IK_WORLD lock (chain = slot = the loop index); a bone
    /// outside the boneMask leaves its slot at the memset-zero rule — type 0,
    /// zero quaternion (not identity). Unlike the autoplay/sequence twins
    /// this one stores <c>kneePos</c>.</summary>
    public void AddAllLocks(Vector[] pos, Quaternion[] q)
    {
        StudioHdr hdr = RequireHdr();

        // skip all array access if no autoplay locks.
        if (hdr.NumIKChains == 0)
        {
            return;
        }

        BoneMatrix3x4[] boneToWorld = RentMatrices();
        BoneSetupExtract.BoneBitList boneComputed = new();
        try
        {
            int ikOffset = _m_ikLock.Count;
            for (int i = 0; i < hdr.NumIKChains; i++)
            {
                _m_ikLock.Add(new IkContextIkRule()); // memset of the tail block
            }

            for (int i = 0; i < hdr.NumIKChains; i++)
            {
                MstudioIkChain pchain = hdr.IkChain(i);
                int bone = pchain.Link(2).Bone;

                // don't bother with iklock if the bone isn't going to be calculated
                if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
                {
                    continue;
                }

                // eval current ik'd bone
                BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);

                IkContextIkRule ikrule = _m_ikLock[i + ikOffset];

                ikrule.Chain = i;
                ikrule.Slot = i;
                ikrule.Type = StudioConstants.IK_WORLD;

                (ikrule.Q, ikrule.Pos) = BoneSetupMath.MatrixAnglesQuaternion(boneToWorld[bone]);

                // save off current knee direction
                if (BoneSetupMath.VectorLengthSqr(pchain.Link(0).KneeDir) > 0.0f)
                {
                    // stock's unused `Vector tmp` copy elided (no observable).
                    ikrule.KneeDir = BoneSetupMath.VectorRotate(
                        pchain.Link(0).KneeDir, boneToWorld[pchain.Link(0).Bone]);
                    ikrule.KneePos = boneToWorld[pchain.Link(1).Bone].Position;
                }
                else
                {
                    ikrule.KneeDir = default;
                }
            }
        }
        finally
        {
            ReturnMatrices(boneToWorld);
        }
    }

    /// <summary><c>SolveAllLocks( pos, q )</c> (oracle 4373-4394): solve every
    /// accumulated lock slot against a synthesised lock — chain = the loop
    /// index (not the stored rule's chain, stock's synthetic <c>mstudioiklock_t
    /// lock</c>), full position weight, zero local-Q weight, no flags.</summary>
    public void SolveAllLocks(Vector[] pos, Quaternion[] q)
    {
        BoneMatrix3x4[] boneToWorld = RentMatrices();
        BoneSetupExtract.BoneBitList boneComputed = new();
        try
        {
            for (int i = 0; i < _m_ikLock.Count; i++)
            {
                // stock's stack mstudioiklock_t, field by field per slot.
                SolveLockParams(chain: i, flPosWeight: 1.0f, flLocalQWeight: 0.0f,
                    i, pos, q, boneToWorld, boneComputed);
            }
        }
        finally
        {
            ReturnMatrices(boneToWorld);
        }
    }

    /// <summary>Body of <see cref="SolveLock"/> over the lock parameters the
    /// three solvers materialise (autoplay table / sequence table / SolveAllLocks'
    /// synthetic). The <see cref="MstudioIkLock"/> overload forwards here.</summary>
    private void SolveLockParams(
        int chain, float flPosWeight, float flLocalQWeight,
        int i, Vector[] pos, Quaternion[] q,
        BoneMatrix3x4[] boneToWorld, BoneSetupExtract.BoneBitList boneComputed)
    {
        StudioHdr hdr = RequireHdr();
        MstudioIkChain pchain = hdr.IkChain(chain);
        int bone = pchain.Link(2).Bone;

        // don't bother with iklock if the bone isn't going to be calculated
        if ((hdr.BoneFlags(bone) & _m_boneMask) == 0)
        {
            return;
        }

        // eval current ik'd bone
        BuildBoneChain(pos, q, bone, boneToWorld, boneComputed);

        // current p and q
        Vector p1 = boneToWorld[bone].Position;

        // blend in position
        Vector p3 = p1 * (1.0f - flPosWeight) + _m_ikLock[i].Pos * flPosWeight;

        // do exact IK solution
        if (BoneSetupMath.VectorLengthSqr(_m_ikLock[i].KneeDir) > 0.0f)
        {
            BoneSetupIk.StudioSolveIk(
                pchain.Link(0).Bone, pchain.Link(1).Bone, pchain.Link(2).Bone,
                p3, _m_ikLock[i].KneePos, _m_ikLock[i].KneeDir, boneToWorld);
        }
        else
        {
            BoneSetupIk.StudioSolveIk(pchain, p3, boneToWorld);
        }

        // slam orientation
        p3 = boneToWorld[bone].Position;
        boneToWorld[bone] = BoneSetupMath.QuaternionMatrix(_m_ikLock[i].Q, p3);

        // rebuild chain
        Quaternion q2 = q[bone];
        BoneSetupIk.SolveBone(hdr, pchain.Link(2).Bone, boneToWorld, pos, q);
        q[bone] = BoneSetupMath.QuaternionSlerp(q[bone], q2, flLocalQWeight);

        BoneSetupIk.SolveBone(hdr, pchain.Link(1).Bone, boneToWorld, pos, q);
        BoneSetupIk.SolveBone(hdr, pchain.Link(0).Bone, boneToWorld, pos, q);
    }

    // ---- test-visible seams over the write-only bookkeeping ----------------
    // The stock readers of m_ikChainRule / m_target (UpdateTargets,
    // SolveDependencies) are CLIENT_DLL rows, so in the server arm these
    // collections have no production reader; the Facts need to see the
    // AddDependencies chain-clear and the ClearTargets stamp at all.
    // InternalsVisibleTo(SourceSharp.*.Tests) is already granted via
    // SourceSharp.Server.csproj (the shared source compiles into it).

    /// <summary><c>m_ikChainRule[chain].Count()</c>.</summary>
    internal int IkChainRuleCount(int chain) => _m_ikChainRule[chain].Count;

    /// <summary><c>m_ikChainRule[chain][i]</c>.</summary>
    internal IkContextIkRule IkChainRuleAt(int chain, int i) => _m_ikChainRule[chain][i];

    /// <summary><c>m_ikLock.Count()</c>.</summary>
    internal int LockCount => _m_ikLock.Count;

    /// <summary><c>m_ikLock[i]</c>.</summary>
    internal IkContextIkRule LockAt(int i) => _m_ikLock[i];

    /// <summary><c>m_target.Count()</c>.</summary>
    internal int TargetCount => _m_target.Length;
    /// <summary><c>m_target[i]</c>: the only server-observed field is
    /// <see cref="CikTarget.LatchedIFramecounter"/>.</summary>
    internal CikTarget TargetAt(int i) => _m_target[i];


    /// <summary><c>SolveAutoplayLocks( pos, q )</c> (4272-4287): solve every
    /// accumulated lock slot against the autoplay lock table. Stock indexes
    /// <c>pIKAutoplayLock(i)</c> with the same i that walks <c>m_ikLock</c> —
    /// correct only because the server flow never mixes sequence locks into
    /// an autoplay context; the bounds check hardens the malformed case
    /// (stock reads off the end).</summary>
    public void SolveAutoplayLocks(Vector[] pos, Quaternion[] q)
    {
        StudioHdr hdr = RequireHdr();

        BoneMatrix3x4[] boneToWorld = RentMatrices();
        BoneSetupExtract.BoneBitList boneComputed = new();
        try
        {
            for (int i = 0; i < _m_ikLock.Count; i++)
            {
                if (i >= hdr.NumAutoPlayLocks)
                {
                    continue; // stock's OOB read, hardened
                }

                MstudioIkLock lock_ = hdr.AutoPlayLock(i);
                SolveLock(lock_, i, pos, q, boneToWorld, boneComputed);
            }
        }
        finally
        {
            ReturnMatrices(boneToWorld);
        }
    }

    /// <summary><c>SolveSequenceLocks( seqdesc, pos, q )</c> (4295-4311): the
    /// sequence twin; <c>seqdesc</c> resolved from <c>iSequence</c>. Same
    /// stock indexing (i walks <c>m_ikLock</c> and the sequence's lock
    /// table); a missing table entry is stock's NULL deref, skipped here.</summary>
    public void SolveSequenceLocks(int iSequence, Vector[] pos, Quaternion[] q)
    {
        StudioHdr hdr = RequireHdr();
        MstudioSeqDesc seqdesc = hdr.SeqDesc(iSequence);

        BoneMatrix3x4[] boneToWorld = RentMatrices();
        BoneSetupExtract.BoneBitList boneComputed = new();
        try
        {
            for (int i = 0; i < _m_ikLock.Count; i++)
            {
                MstudioIkLock? plock = seqdesc.PIKLock(i);
                if (plock is null)
                {
                    continue;
                }

                SolveLock(plock, i, pos, q, boneToWorld, boneComputed);
            }
        }
        finally
        {
            ReturnMatrices(boneToWorld);
        }
    }

    /// <summary><c>SolveLock( plock, i, pos, q, boneToWorld, boneComputed )</c>
    /// (4402-4451): build the chain's end-bone transform, blend the recorded
    /// lock position over the current foot by the lock's position weight, run
    /// the two-bone IK (kneeDir arm when the lock recorded a knee direction,
    /// else the chain overload's current-pose arm), slam the recorded lock
    /// orientation onto the solved foot, then rebuild the chain bottom-up
    /// through <see cref="BoneSetupIk.SolveBone"/>, slerping the end bone's
    /// orientation back toward its pre-lock pose by the lock's local-Q
    /// weight.</summary>
    private void SolveLock(
        MstudioIkLock plock, int i, Vector[] pos, Quaternion[] q,
        BoneMatrix3x4[] boneToWorld, BoneSetupExtract.BoneBitList boneComputed) =>
        SolveLockParams(plock.Chain, plock.FlPosWeight, plock.FlLocalQWeight,
            i, pos, q, boneToWorld, boneComputed);

    private StudioHdr RequireHdr() =>
        _m_pStudioHdr ?? throw new InvalidOperationException(
            "CIKContext used before Init");

    // g_MatrixPool.Alloc()/Free() per call; see the file header for why the
    // pool is not a static.
    private static BoneMatrix3x4[] RentMatrices() =>
        ArrayPool<BoneMatrix3x4>.Shared.Rent(StudioConstants.MAXSTUDIOBONES);

    private static void ReturnMatrices(BoneMatrix3x4[] matrices) =>
        ArrayPool<BoneMatrix3x4>.Shared.Return(matrices);
}
