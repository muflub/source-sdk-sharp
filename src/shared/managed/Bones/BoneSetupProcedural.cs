//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// Stock's <c>CBoneAccessor</c> (<c>bone_accessor.h</c>): a pointer wrapper
/// over the caller's array of bone-to-world matrices plus the
/// readable/writable bone masks. The C++ holds <c>matrix3x4_t *</c> straight
/// into the entity's bone array; the managed twin holds the caller's
/// <see cref="BoneMatrix3x4"/> array REFERENCE, so a write through
/// <see cref="GetBoneForWrite"/> mutates the caller's array exactly like the
/// reference-returning C++ (no copy, no dirty flags — the stock class has
/// none; the two <c>int</c> masks are the only state, opaque to this class).
///
/// <para>
/// <see cref="GetBone"/> returns a copy because C# structs are values; every
/// stock read site consumes it as an rvalue, so the difference from
/// <c>const matrix3x4_t&amp;</c> is invisible. <see cref="GetBoneForWrite"/>
/// returns a <c>ref</c> so stock's <c>MatrixCopy(src, GetBoneForWrite(i))</c>
/// and <c>ConcatTransforms(a, b, GetBoneForWrite(i))</c> land in place.
/// </para>
///
/// <para>
/// Stock's <c>Init(const C_BaseAnimating*, matrix3x4_t*)</c> and
/// <c>m_pAnimating</c> are CLIENT_DLL engine verification plumbing (the
/// <c>_DEBUG</c>-only <c>SanityCheckBone</c> reads the entity's bone mask);
/// omitted as an engine binding, not portable logic — ledger NP-04. The
/// flags-less ctor is stock's uninitialized-flags UB arm (bone_accessor.h:59
/// leaves <c>m_ReadableBones</c>/<c>m_WritableBones</c> alone); the port
/// zeroes them — DIVERGENCE (malformed-state only), ledger NP-05.
/// </para>
/// </summary>
public sealed class CBoneAccessor
{
    private readonly BoneMatrix3x4[]? _bones;
    private int _readableBones;
    private int _writableBones;

    /// <summary>Stock's default ctor: null array, both masks zero.</summary>
    public CBoneAccessor()
    {
    }

    /// <summary>
    /// Stock's <c>CBoneAccessor(matrix3x4_t *pBones)</c>: "can be used to
    /// allow access to all bones" (the DLL sets the masks itself). The mask
    /// fields start zeroed, not uninitialized (NP-05).
    /// </summary>
    public CBoneAccessor(BoneMatrix3x4[] bones)
    {
        _bones = bones;
    }

    public int GetReadableBones() => _readableBones;

    public void SetReadableBones(int flags) => _readableBones = flags;

    public int GetWritableBones() => _writableBones;

    public void SetWritableBones(int flags) => _writableBones = flags;

    /// <summary>Stock's <c>GetBone</c> (const reference → a value copy). The
    /// <c>_DEBUG</c>-only <c>SanityCheckBone</c> is compiled out of the
    /// shipped game, so the port carries it as nothing (NP-04).</summary>
    public BoneMatrix3x4 GetBone(int iBone) => Bones[iBone];

    /// <summary>Stock's <c>operator[]</c>, identical to the const GetBone.</summary>
    public BoneMatrix3x4 this[int iBone] => GetBone(iBone);

    /// <summary>Stock's write reference: index in place, no copy.</summary>
    public ref BoneMatrix3x4 GetBoneForWrite(int iBone) => ref Bones[iBone];

    /// <summary>Stock's <c>GetBoneArrayForWrite</c>: the raw array for bulk
    /// consumers (stock returns the pointer; the array reference is the
    /// managed twin and shares the caller's storage).</summary>
    public BoneMatrix3x4[] GetBoneArrayForWrite() => Bones;

    private BoneMatrix3x4[] Bones =>
        _bones ?? throw new InvalidOperationException(
            "CBoneAccessor used without a bone array (stock's null m_pBones deref)");
}

/// <summary><c>mstudioaxisinterpbone_t</c> (studio.h:143-154).</summary>
internal sealed class MstudioAxisInterpBone : StudioRecord
{
    internal MstudioAxisInterpBone(byte[] image, int @base) : base(image, @base)
    {
    }

    /// <summary>Local transformation of this bone used to calc the 3-point blend.</summary>
    public int Control => I32(0);

    /// <summary>Axis to check: the column of the control bone's world matrix.</summary>
    public int Axis => I32(4);

    /// <summary>X+, X-, Y+, Y-, Z+, Z-.</summary>
    public Vector Pos(int i) => Vec3(8 + 12 * i);

    /// <summary>X+, X-, Y+, Y-, Z+, Z- (<c>new</c>: deliberately hides the
    /// inherited <c>StudioRecord.Quat(int)</c> decoder; the call stays
    /// qualified to the base).</summary>
    public new Quaternion Quat(int i) => base.Quat(80 + 16 * i);
}

/// <summary>One <c>mstudioquatinterpinfo_t</c> (48 bytes, studio.h:158-163).
/// A value view (no allocation: the quat-interp bone reads every trigger of
/// every helper bone each build; plan_physics_bones_perf A3).</summary>
internal readonly struct MstudioQuatInterpInfo
{
    private readonly byte[] _image;
    private readonly int _base;

    internal MstudioQuatInterpInfo(byte[] image, int @base)
    {
        _image = image;
        _base = @base;
    }

    private float F32(int off) => BinaryPrimitives.ReadSingleLittleEndian(_image.AsSpan(_base + off, 4));

    /// <summary>1 / radian angle of trigger influence.</summary>
    public float InvTolerance => F32(0);

    /// <summary>Angle to match.</summary>
    public Quaternion Trigger => new(F32(4), F32(8), F32(12), F32(16));

    /// <summary>New position.</summary>
    public Vector Pos => new(F32(20), F32(24), F32(28));

    /// <summary>New angle.</summary>
    public Quaternion Quat => new(F32(32), F32(36), F32(40), F32(44));
}

/// <summary><c>mstudioquatinterpbone_t</c> (studio.h:165-179).</summary>
internal readonly struct MstudioQuatInterpBone
{
    private readonly byte[] _image;
    private readonly int _base;

    internal MstudioQuatInterpBone(byte[] image, int @base)
    {
        _image = image;
        _base = @base;
    }

    private int I32(int off) => BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(_base + off, 4));

    /// <summary>Local transformation to check.</summary>
    public int Control => I32(0);

    public int NumTriggers => I32(4);

    private int TriggerIndex => I32(8);

    /// <summary>Stock's <c>pTrigger(i)</c>: <c>(byte*)this + triggerindex</c>
    /// advanced by 48-byte <c>mstudioquatinterpinfo_t</c> records. Reads past
    /// the payload are malformed data and throw (stock reads whatever memory
    /// follows).</summary>
    public MstudioQuatInterpInfo Trigger(int i) =>
        new(_image, _base + TriggerIndex + 48 * i);
}

/// <summary><c>mstudioaimatbone_t</c> (studio.h:254-264).</summary>
internal sealed class MstudioAimatBone : StudioRecord
{
    internal MstudioAimatBone(byte[] image, int @base) : base(image, @base)
    {
    }

    public int Parent => I32(0);

    /// <summary>Might be a bone or an attachment (AIMATBONE vs AIMATATTACH).</summary>
    public int Aim => I32(4);

    public Vector AimVector => Vec3(8);

    public Vector UpVector => Vec3(20);

    public Vector BasePos => Vec3(32);
}

/// <summary>
/// The procedural-bone cluster of <c>bone_setup.cpp</c>, ported to C#:
/// <c>DoAxisInterpBone</c>, <c>DoQuatInterpBone</c>, <c>DoAimAtBone</c>,
/// <c>CalcProceduralBone</c> and <c>FindController</c> (inventory rows
/// 99-103, stock oracle 4586-4985; all five are CLIENT_DLL-gated in the
/// relocated stock — the server never reaches them).
///
/// <para>
/// Every function is pure over the <see cref="StudioHdr"/> image and the
/// caller-owned <see cref="CBoneAccessor"/> array: no clock, no convar, no
/// static state (SS0001). The stock <c>aim_constraint</c> convar is
/// commented-out dead code (bone_setup.cpp:4894-4900, and its gate body
/// inside <c>DoAimAtBone</c> at oracle 4770-4790 is likewise commented out);
/// <see cref="DoAimAtBone"/> takes it as the injected
/// <c>aimConstraintEnabled</c> parameter whose default <c>true</c> is the
/// shipped behaviour — DIVERGENCE (parameter-only; the <c>false</c> arm is
/// unreachable in shipped stock), ledger NP-06.
/// </para>
///
/// <para>
/// Stock has NO parent-first recursion inside <c>CalcProceduralBone</c> and
/// no MAXSTUDIOBONES depth guard (the inventory premise that it recurses on
/// the parent is wrong; ledger NP-02): the caller (Studio_CalcProceduralBones
/// / the sequence blend, not yet ported) walks bones in parent-before-child
/// order, so a child procedural reading its parent's result sees the already
/// computed parent. The port keeps stock's flat dispatch exactly.
/// </para>
/// </summary>
public static class BoneSetupProcedural
{
    // studio.h:103-107. Not in StudioConstants (and StudioHdr.cs belongs to
    // the query lane); file-local like needs-blend.md NB-01's promotion
    // pattern — ledger NP-01 proposes the promotion.
    internal const int STUDIO_PROC_AXISINTERP = 1;
    internal const int STUDIO_PROC_QUATINTERP = 2;
    internal const int STUDIO_PROC_AIMATBONE = 3;
    internal const int STUDIO_PROC_AIMATATTACH = 4;
    internal const int STUDIO_PROC_JIGGLE = 5;

    /// <summary>studio.h:409 <c>BONE_ALWAYS_PROCEDURAL</c>: the bone-flag bit
    /// gating <see cref="CalcProceduralBone"/>. Same file-local promotion
    //  note as the STUDIO_PROC_* block (NP-01). Note: STUDIO_PIVOT and
    /// STUDIO_ROTADJUST do not exist in this SDK's studio.h at all — the
    /// brief's pivot-recentering premise matches HL1/HL2SDK-mod eras, not
    /// Source 2013 (verified by grep over the pristine fork): NP-07.</summary>
    internal const int BONE_ALWAYS_PROCEDURAL = 0x04;

    /// <summary>
    /// Stock's <c>FindController</c> (bone_setup.cpp:5101-5111): the first
    /// <c>mstudiobonecontroller_t</c> whose <c>inputfield</c> equals
    /// <c>iController</c>, or null. The reader walks the table by index —
    /// no bounds checks beyond <c>NumBoneControllers</c>, exactly like
    /// <c>pBonecontroller(i)</c>.
    /// </summary>
    public static MstudioBoneController? FindController(StudioHdr pStudioHdr, int iController)
    {
        // find first controller that matches the index
        for (int i = 0; i < pStudioHdr.NumBoneControllers; i++)
        {
            MstudioBoneController c = pStudioHdr.BoneController(i);
            if (c.InputField == iController)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// Stock's <c>DoAxisInterpBone</c> (oracle 4586-4692): read the chosen
    /// column of the control bone's world matrix, undo the parent's rotation
    /// to get the control direction in the control bone's parent space, pick
    /// one of six (quat, pos) pairs by the signs of the three components,
    /// then a three-way slerp/pos blend parented onto the bone's parent.
    ///
    /// <para>
    /// Stock's first statement dereferences <c>pProc-&gt;control</c> BEFORE
    /// the <c>if (pProc &amp;&amp; ...)</c> guard, so a procedural bone whose
    /// <c>procindex</c> is 0 crashes on the spot (UB, malformed-data only).
    /// The port returns untouched instead — DIVERGENCE (malformed-data only),
    /// ledger NP-08 (the same safe-value pattern as needs-blend.md NB-02).
    /// A control bone that is a root (<c>parent == -1</c>) is stock's
    /// <c>else</c> arm: the raw world-space column, no inverse rotation —
    /// ported faithfully, not a divergence.
    /// </para>
    /// </summary>
    public static void DoAxisInterpBone(StudioHdr pStudioHdr, int ibone, CBoneAccessor bonetoworld)
    {
        int procBase = ProcedureAddress(pStudioHdr, ibone);
        if (procBase == 0)
        {
            // pProcedure() == NULL: stock derefs pProc->control first (UB);
            // the port leaves the bone untouched (NP-08).
            return;
        }

        MstudioAxisInterpBone pProc = new(pStudioHdr.RawImageForRows, procBase);
        int control = pProc.Control;
        BoneMatrix3x4 controlBone = bonetoworld.GetBone(control);
        Vector control1;

        if (pStudioHdr.Bone(control).Parent != -1)
        {
            // Stock's dead #if 0 block (our bone_setup.cpp 4725-4735) —
            // MatrixInvert + ConcatTransforms + column read — is commented-out
            // code, not the shipped arm. The live code pulls the column from
            // the control bone's WORLD matrix and VectorIRotates it by the
            // control's parent's world.
            Vector tmp = Column(controlBone, pProc.Axis);
            control1 = BoneSetupMath.VectorIRotate(tmp, bonetoworld.GetBone(pStudioHdr.Bone(control).Parent));
        }
        else
        {
            // pull out the control column
            control1 = Column(controlBone, pProc.Axis);
        }

        Quaternion q1, q2, q3;
        Vector p1, p2, p3;

        // find axial control inputs
        float a1 = control1.X;
        float a2 = control1.Y;
        float a3 = control1.Z;
        if (a1 >= 0)
        {
            q1 = pProc.Quat(0);
            p1 = pProc.Pos(0);
        }
        else
        {
            a1 = -a1;
            q1 = pProc.Quat(1);
            p1 = pProc.Pos(1);
        }

        if (a2 >= 0)
        {
            q2 = pProc.Quat(2);
            p2 = pProc.Pos(2);
        }
        else
        {
            a2 = -a2;
            q2 = pProc.Quat(3);
            p2 = pProc.Pos(3);
        }

        if (a3 >= 0)
        {
            q3 = pProc.Quat(4);
            p3 = pProc.Pos(4);
        }
        else
        {
            a3 = -a3;
            q3 = pProc.Quat(5);
            p3 = pProc.Pos(5);
        }

        // do a three-way blend
        Vector p;
        Quaternion v, tmp2;
        if (a1 + a2 > 0)
        {
            float t = 1.0f / (a1 + a2 + a3);
            // FIXME: do a proper 3-way Quat blend!
            tmp2 = BoneSetupMath.QuaternionSlerp(q2, q1, a1 / (a1 + a2));
            v = BoneSetupMath.QuaternionSlerp(tmp2, q3, a3 * t);
            p = BoneSetupMath.VectorScale(p1, a1 * t);
            p = BoneSetupMath.VectorMA(p, a2 * t, p2);
            p = BoneSetupMath.VectorMA(p, a3 * t, p3);
        }
        else
        {
            // stock writes QuaternionSlerp(*q3, *q3, 0, v) with the comment
            // "??? no quat copy?" — the aligning slerp call, not a copy
            // call: with p == q the align test (a = 0 <= b) can never flip,
            // and both scale arms give sclp = 1, sclq = 0 at t = 0, so the
            // result is the RAW q3 — un-normalised (stock's QuaternionNormalize
            // sits in a compiled-out release Assert). Pinned by the
            // else-arm fact.
            v = BoneSetupMath.QuaternionSlerp(q3, q3, 0);
            p = p3;
        }

        BoneMatrix3x4 bonematrix = BoneSetupMath.QuaternionMatrix(v, p);

        bonetoworld.GetBoneForWrite(ibone) = BoneSetupMath.ConcatTransforms(
            bonetoworld.GetBone(pStudioHdr.Bone(ibone).Parent), bonematrix);
    }

    /// <summary>
    /// Stock's <c>DoQuatInterpBone</c> (oracle 4700-4770): express the
    /// control bone's world relative to its parent, match it against the
    /// trigger list by angular distance, and accumulate the weighted triggers
    /// (with <c>QuaternionAlign</c> against the running sum, so antipodal
    /// triggers add instead of cancel).
    ///
    /// <para>
    /// Two stock arms leave <c>bonematrix</c> UNINITIALIZED and still run the
    /// final concat: guard fail when <c>pProc</c> is null, and guard fail
    /// when the control bone is a root (<c>parent == -1</c>). Both are UB on
    /// the malformed/degenerate side. The port's safe values
    /// (DIVERGENCE malformed-data only, ledger NP-09): a null <c>pProc</c>
    /// returns untouched; a root control bone takes the identity-inverse
    /// limit — inverting "the parent's world" of a bone with no parent is
    /// inverting nothing, so <c>tmpmatrix = identity</c> and
    /// <c>controlmatrix</c> is the control's world as-is. That is also
    /// exactly what <see cref="DoAxisInterpBone"/> does for a root control
    /// (its raw-column arm is the VectorIRotate-by-identity answer), so the
    /// two procedural readers agree on the degenerate case.
    /// </para>
    ///
    /// <para>
    /// Stock's <c>float weight[32]</c> stack-smashes when
    /// <c>numtriggers &gt; 32</c>; the port clamps both trigger loops to 32
    /// — DIVERGENCE (malformed-data only), ledger NP-09. The
    /// <c>Assert(QuaternionNormalize(quat) != 0)</c> is compiled away in the
    /// release game (Assert(x) does not evaluate x), so the port does NOT
    /// normalize the accumulated quat: <c>QuaternionMatrix</c> consumes the
    /// raw accumulation exactly as the shipped build does.
    /// </para>
    /// </summary>
    public static void DoQuatInterpBone(StudioHdr pStudioHdr, int ibone, CBoneAccessor bonetoworld)
    {
        int procBase = ProcedureAddress(pStudioHdr, ibone);
        if (procBase == 0)
        {
            // pProc == NULL: stock would fall through to the final concat
            // with an uninitialized bonematrix (UB); the port returns
            // untouched (NP-09).
            return;
        }

        QuatInterpTable pProc = pStudioHdr.QuatInterpTable(ibone, procBase);
        int control = pProc.Control;
        int controlParent = pStudioHdr.Bone(control).Parent;

        Quaternion src;
        Span<float> weight = stackalloc float[32];
        float scale = 0.0f;
        Quaternion quat;
        Vector pos;

        // Root control: identity inverse (NP-09 safe value; the stock guard
        // skips this block entirely and feeds garbage to the concat).
        BoneMatrix3x4 tmpmatrix = controlParent == -1
            ? BoneMatrix3x4.Identity
            : BoneSetupMath.MatrixInvert(bonetoworld.GetBone(controlParent));
        BoneMatrix3x4 controlmatrix = BoneSetupMath.ConcatTransforms(
            tmpmatrix, bonetoworld.GetBone(control));

        (src, pos) = BoneSetupMath.MatrixAnglesQuaternion(controlmatrix); // FIXME: make a version without pos
        pos = Vector.Zero; // stock's `pos.Init();` runs AFTER MatrixAngles overwrote it — the read value is dead

        int i;
        int count = pProc.Triggers.Length; // NumTriggers clamped to [0, 32] at decode
        for (i = 0; i < count; i++)
        {
            ref readonly QuatInterpTrigger t = ref pProc.Triggers[i];
            float dot = MathF.Abs(BoneSetupMath.QuaternionDotProduct(t.Trigger, src));
            // FIXME: a fast acos should be acceptable
            dot = Clamp(dot, -1.0f, 1.0f);
            // stock writes `acos` (double) here — the promotion is
            // reproduced: 2 * acos(double) * inv_tolerance computes in
            // double, the 1 - product too, and only the store rounds.
            weight[i] = (float)(1.0 - (2.0 * Math.Acos(dot) * t.InvTolerance));
            weight[i] = Math.Max(0.0f, weight[i]);
            scale += weight[i];
        }

        // EPSILON? stock's `scale <= 0.001` promotes the float scale to the
        // double literal; reproduced (0.001f would compare in float).
        if (scale <= 0.001)
        {
            // AngleMatrix(Quaternion, ...) again takes the implicit
            // RadianEuler(Quaternion) route — the NB-06 promotion from
            // docs/plans/needs-blend.md, mirrored like the blend lane's
            // WorldSpaceSlerp target.
            QuatInterpTrigger t0 = pProc.Trigger0;
            BoneMatrix3x4 bonematrix = BoneSetupMath.AngleMatrix(
                BoneSetupMath.QuaternionAngles(t0.Quat), t0.Pos);
            bonetoworld.GetBoneForWrite(ibone) = BoneSetupMath.ConcatTransforms(
                bonetoworld.GetBone(pStudioHdr.Bone(ibone).Parent), bonematrix);
            return;
        }

        scale = 1.0f / scale;

        quat = default; // quat.Init(0,0,0,0): all four fields zero
        pos = Vector.Zero; // pos.Init()

        for (i = 0; i < count; i++)
        {
            if (weight[i] != 0.0f)
            {
                float s = weight[i] * scale;
                ref readonly QuatInterpTrigger pTrigger = ref pProc.Triggers[i];

                // Stock's in-place aliasing `QuaternionAlign(pTrigger->quat,
                // quat, quat)` is the p/q pair the mirror returns adjusted.
                quat = BoneSetupMath.QuaternionAlign(pTrigger.Quat, quat);

                quat = new Quaternion(
                    quat.X + s * pTrigger.Quat.X,
                    quat.Y + s * pTrigger.Quat.Y,
                    quat.Z + s * pTrigger.Quat.Z,
                    quat.W + s * pTrigger.Quat.W);
                pos = new Vector(
                    pos.X + s * pTrigger.Pos.X,
                    pos.Y + s * pTrigger.Pos.Y,
                    pos.Z + s * pTrigger.Pos.Z);
            }
        }

        // Assert( QuaternionNormalize( quat ) != 0): release no-op, and
        // QuaternionNormalize would have mutated the quat — the shipped
        // game never ran it, so neither does this.
        BoneMatrix3x4 boneMatrixFinal = BoneSetupMath.QuaternionMatrix(quat, pos);

        bonetoworld.GetBoneForWrite(ibone) = BoneSetupMath.ConcatTransforms(
            bonetoworld.GetBone(pStudioHdr.Bone(ibone).Parent), boneMatrixFinal);
    }

    /// <summary>
    /// Stock's <c>DoAimAtBone</c> (oracle 4785-4925): rotate the bone so its
    /// <c>aimvector</c> axis points at the aim bone (or, for AIMATATTACH,
    /// the attachment's origin), optionally rolling about that axis so the
    /// <c>upvector</c> matches the parent's up.
    ///
    /// <para>
    /// <paramref name="pStudioHdr"/> mirrors stock's fourth parameter: non-null
    /// selects the AIMATATTACH arm (the target is
    /// <c>ConcatTransforms(boneWorld[attachment.localbone], attachment.local)</c>);
    /// null selects AIMATBONE (the target is the aim bone's world). It is the
    /// SAME header as <paramref name="pBonesHdr"/> in every stock call site;
    /// the split reproduces stock's NULL-as-selector shape.
    /// </para>
    ///
    /// <para>
    /// <paramref name="aimConstraintEnabled"/> injects the stock
    /// <c>aim_constraint</c> convar (<c>CManagedConVarRef
    /// aim_constraint("aim_constraint")</c>, bone_setup.cpp:4894-4900). In
    /// the shipped stock BOTH the convar and the gate that reads it are
    /// commented out, so the live behaviour is exactly the
    /// <c>aimConstraintEnabled: true</c> path; the <c>false</c> arm is the
    /// dead code (copy the parent transform, then overwrite the translation
    /// with <c>basepos</c> carried through the parent's rotation — stock's
    /// <c>VectorTransform</c> then <c>MatrixSetColumn</c> pair). DIVERGENCE
    /// (parameter-only; enabling dead code), ledger NP-06.
    /// </para>
    ///
    /// <para>
    /// Stock's three <c>Assert</c>s (the parent/basepos consistency checks
    /// and the <c>1 - |dot| &gt; FLT_EPSILON</c> aim-along-axis guard) compile
    /// away in the release build — they are doc-comments here, and the gates
    /// that DO gate an answer (the up-vector tests, and the inner
    /// pUp/pParentUp test that chooses the axis) are ported as written. The
    /// outer aim-angle uses stock's <c>acosf</c> (float); the inner up-angle
    /// uses stock's double <c>acos</c> — both promotions reproduced.
    /// </para>
    /// </summary>
    public static void DoAimAtBone(
        StudioHdr pBonesHdr,
        int iBone,
        CBoneAccessor bonetoworld,
        StudioHdr? pStudioHdr,
        bool aimConstraintEnabled = true)
    {
        int procBase = ProcedureAddress(pBonesHdr, iBone);
        if (procBase == 0)
        {
            // if ( !pProc ) return;
            return;
        }

        MstudioAimatBone pProc = new(pBonesHdr.RawImageForRows, procBase);

        if (!aimConstraintEnabled)
        {
            // The stock convar is dead code; this is its commented-out body
            // (oracle 4772-4789): copy the parent transform, then set the
            // translation to basepos pushed through that same transform.
            BoneMatrix3x4 boneToWorldSpace = bonetoworld.GetBone(pProc.Parent);
            Vector boneWorldPosition = BoneSetupMath.VectorTransform(pProc.BasePos, boneToWorldSpace);
            boneToWorldSpace.Position = boneWorldPosition;
            bonetoworld.GetBoneForWrite(iBone) = boneToWorldSpace;
            return;
        }

        // The world matrix of the bone to change
        BoneMatrix3x4 boneMatrix;

        // Guaranteed to be unit length
        Vector userAimVector = pProc.AimVector;

        // Guaranteed to be unit length
        Vector userUpVector = pProc.UpVector;

        // Get to get position of bone but also for up reference
        BoneMatrix3x4 parentSpace = bonetoworld.GetBone(pProc.Parent);

        // World space position of the bone to aim
        Vector aimWorldPosition = BoneSetupMath.VectorTransform(pProc.BasePos, parentSpace);

        // The worldspace matrix of the bone to aim at
        BoneMatrix3x4 aimAtSpace;
        if (pStudioHdr is not null)
        {
            // This means it's AIMATATTACH
            MstudioAttachment attachment = pStudioHdr.Attachment(pProc.Aim);
            aimAtSpace = BoneSetupMath.ConcatTransforms(
                bonetoworld.GetBone(attachment.LocalBone),
                attachment.Local);
        }
        else
        {
            aimAtSpace = bonetoworld.GetBone(pProc.Aim);
        }

        Vector aimAtWorldPosition = aimAtSpace.Position; // MatrixGetColumn(aimAtSpace, 3, ...)

        // make sure the redundant parent info is correct
        // Assert( pProc->parent == pBones[iBone].parent );      (release no-op)
        // make sure the redundant position info is correct
        // Assert( pProc->basepos.DistToSqr( pBones[iBone].pos ) < 0.1 ); (release no-op)

        // The aim and up data is relative to this bone, not the parent bone
        BoneMatrix3x4 boneLocalToWorld = BoneSetupMath.ConcatTransforms(
            bonetoworld.GetBone(pProc.Parent),
            // stock writes AngleMatrix(pBones[iBone].quat, basepos, ...) on a
            // Quaternion: the implicit RadianEuler(Quaternion) overload — the
            // NB-06 route from docs/plans/needs-blend.md.
            BoneSetupMath.AngleMatrix(
                BoneSetupMath.QuaternionAngles(pBonesHdr.Bone(iBone).Quaternion), pProc.BasePos));

        Vector aimVector = VectorNormalizeFast(VectorSubtract(aimAtWorldPosition, aimWorldPosition));

        Vector axis = VectorNormalizeFast(BoneSetupMath.CrossProduct(userAimVector, aimVector));
        // Assert( 1.0f - fabs( DotProduct( userAimVector, aimVector ) ) > FLT_EPSILON ); (release no-op)
        float angle = MathF.Acos(BoneSetupMath.Dot(userAimVector, aimVector));
        Quaternion aimRotation = AxisAngleQuaternion(axis, BoneSetupMath.RadToDeg * angle);

        if (1.0f - MathF.Abs(BoneSetupMath.Dot(userUpVector, userAimVector)) > BoneSetupMath.FltEpsilon)
        {
            BoneMatrix3x4 aimRotationMatrix = BoneSetupMath.QuaternionMatrix(aimRotation);

            Vector tmp_pUp = BoneSetupMath.VectorRotate(userUpVector, aimRotationMatrix);
            Vector tmpV = BoneSetupMath.VectorScale(aimVector, BoneSetupMath.Dot(aimVector, tmp_pUp));
            Vector pUp = VectorNormalizeFast(VectorSubtract(tmp_pUp, tmpV));

            Vector tmp_pParentUp = BoneSetupMath.VectorRotate(userUpVector, boneLocalToWorld);
            tmpV = BoneSetupMath.VectorScale(aimVector, BoneSetupMath.Dot(aimVector, tmp_pParentUp));
            Vector pParentUp = VectorNormalizeFast(VectorSubtract(tmp_pParentUp, tmpV));

            Quaternion upRotation;
            // Assert( 1.0f - fabs( DotProduct( pUp, pParentUp ) ) > FLT_EPSILON ); (stock's own assert is commented out)
            float upAngle;
            Vector upAxis;
            if (1.0f - MathF.Abs(BoneSetupMath.Dot(pUp, pParentUp)) > BoneSetupMath.FltEpsilon)
            {
                upAngle = (float)Math.Acos(BoneSetupMath.Dot(pUp, pParentUp)); // stock: `acos` (double)
                upAxis = BoneSetupMath.CrossProduct(pUp, pParentUp);
            }
            else
            {
                upAngle = 0.0f;
                upAxis = pUp;
            }

            upAxis = VectorNormalizeFast(upAxis);
            upRotation = AxisAngleQuaternion(upAxis, BoneSetupMath.RadToDeg * upAngle);

            Quaternion boneRotation = BoneSetupMath.QuaternionMult(upRotation, aimRotation);
            boneMatrix = BoneSetupMath.QuaternionMatrix(boneRotation, aimWorldPosition);
        }
        else
        {
            boneMatrix = BoneSetupMath.QuaternionMatrix(aimRotation, aimWorldPosition);
        }

        // MatrixCopy( boneMatrix, bonetoworld.GetBoneForWrite( iBone ) );
        bonetoworld.GetBoneForWrite(iBone) = boneMatrix;
    }

    /// <summary>
    /// Stock's <c>CalcProceduralBone</c> (oracle 4932-4965): if the bone's
    /// flags carry <c>BONE_ALWAYS_PROCEDURAL</c>, dispatch on
    /// <c>proctype</c> and report whether a procedural transform was applied;
    /// every other proctype (including JIGGLE and 0) answers false and
    /// touches nothing. No recursion, no depth guard — see the class doc
    /// (NP-02). AIMATBONE passes a null selector; AIMATATTACH passes the hdr.
    /// </summary>
    public static bool CalcProceduralBone(StudioHdr pStudioHdr, int iBone, CBoneAccessor bonetoworld)
    {
        if ((pStudioHdr.BoneFlags(iBone) & BONE_ALWAYS_PROCEDURAL) != 0)
        {
            switch (ProcedureType(pStudioHdr, iBone))
            {
                case STUDIO_PROC_AXISINTERP:
                    DoAxisInterpBone(pStudioHdr, iBone, bonetoworld);
                    return true;

                case STUDIO_PROC_QUATINTERP:
                    DoQuatInterpBone(pStudioHdr, iBone, bonetoworld);
                    return true;

                case STUDIO_PROC_AIMATBONE:
                    DoAimAtBone(pStudioHdr, iBone, bonetoworld, null);
                    return true;

                case STUDIO_PROC_AIMATATTACH:
                    DoAimAtBone(pStudioHdr, iBone, bonetoworld, pStudioHdr);
                    return true;

                default:
                    return false;
            }
        }

        return false;
    }

    // -----------------------------------------------------------------
    // file-local helpers
    // -----------------------------------------------------------------

    /// <summary>Stock's <c>mstudiobone_t::proctype</c> (I32 at 164).</summary>
    private static int ProcedureType(StudioHdr hdr, int ibone)
    {
        MstudioBone bone = hdr.Bone(ibone);
        return BinaryPrimitivesReadI32(bone.RawImage, bone.RawAddress + 164);
    }

    /// <summary>Stock's <c>mstudiobone_t::pProcedure()</c>: the record base +
    /// <c>procindex</c> (I32 at 168), or 0 when <c>procindex == 0</c>.</summary>
    private static int ProcedureAddress(StudioHdr hdr, int ibone)
    {
        MstudioBone bone = hdr.Bone(ibone);
        int procindex = BinaryPrimitivesReadI32(bone.RawImage, bone.RawAddress + 168);
        return procindex == 0 ? 0 : bone.RawAddress + procindex;
    }

    private static int BinaryPrimitivesReadI32(byte[] image, int addr) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(addr, 4));

    /// <summary>Stock's column extraction <c>matrix[row][axis]</c>: the axis
    /// column of the rotation part as a vector (one per row).</summary>
    private static Vector Column(in BoneMatrix3x4 m, int axis) =>
        new(m.Row0[axis], m.Row1[axis], m.Row2[axis]);

    /// <summary>mathlib.h's <c>clamp&lt;float&gt;</c>: two compares, early
    /// returns.</summary>
    private static float Clamp(float val, float minVal, float maxVal)
    {
        if (val < minVal)
        {
            return minVal;
        }

        if (val > maxVal)
        {
            return maxVal;
        }

        return val;
    }

    /// <summary><c>VectorSubtract(a, b, out)</c> — the mirror is missing from
    /// <see cref="BoneSetupMath"/> (NB-07's private-helper precedent): a − b
    /// componentwise, no convention in play.</summary>
    private static Vector VectorSubtract(Vector a, Vector b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary><c>VectorNormalizeFast</c>: in the shipped (non-X360) build
    /// vector.h's body is exactly <c>VectorNormalize(vec)</c>, so the port
    /// returns the mirror's normalized value (stock normalizes in place; the
    /// value semantics make that the same observable).</summary>
    private static Vector VectorNormalizeFast(Vector v) => BoneSetupMath.VectorNormalize(v);

    /// <summary>
    /// mathlib_base.cpp's <c>AxisAngleQuaternion</c> (2191-2200): the half
    /// angle's sin times the axis, cosine in w. The axis is NOT normalized
    /// here — stock's callers normalize first. Missing from
    /// <see cref="BoneSetupMath"/>; file-local per NB-07 (ledger NP-03
    /// proposes the promotion).
    /// </summary>
    private static Quaternion AxisAngleQuaternion(Vector axis, float angle)
    {
        float r = BoneSetupMath.DegToRad * angle * 0.5f;
        float sa = MathF.Sin(r);
        float ca = MathF.Cos(r);
        return new Quaternion(axis.X * sa, axis.Y * sa, axis.Z * sa, ca);
    }
}

/// <summary>One quat-interp trigger, decoded (<see cref="MstudioQuatInterpInfo"/>'s values).</summary>
internal readonly struct QuatInterpTrigger
{
    public QuatInterpTrigger(in MstudioQuatInterpInfo info)
    {
        InvTolerance = info.InvTolerance;
        Trigger = info.Trigger;
        Pos = info.Pos;
        Quat = info.Quat;
    }

    public float InvTolerance { get; }
    public Quaternion Trigger { get; }
    public Vector Pos { get; }
    public Quaternion Quat { get; }
}

/// <summary>
/// A quat-interp bone's record decoded once per header (plan_physics_bones_perf
/// A3): the control bone and the triggers DoQuatInterpBone reads every build,
/// at most 32 of them (its clamp). <see cref="Trigger0"/> is read even with no
/// triggers, as stock's <c>pTrigger(0)</c> is, so it is decoded eagerly too.
/// </summary>
internal sealed class QuatInterpTable
{
    public QuatInterpTable(MstudioQuatInterpBone proc)
    {
        Control = proc.Control;
        Triggers = new QuatInterpTrigger[Math.Clamp(proc.NumTriggers, 0, 32)];

        for (int i = 0; i < Triggers.Length; i++)
        {
            Triggers[i] = new QuatInterpTrigger(proc.Trigger(i));
        }

        Trigger0 = Triggers.Length > 0 ? Triggers[0] : new QuatInterpTrigger(proc.Trigger(0));
    }

    public int Control { get; }
    public QuatInterpTrigger[] Triggers { get; }
    public QuatInterpTrigger Trigger0 { get; }
}
