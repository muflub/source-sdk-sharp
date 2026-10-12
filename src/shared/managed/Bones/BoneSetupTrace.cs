//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The trace cluster of bone_setup.cpp, ported to C#: ClipRayToHitbox
// (file-static in stock), SweepBoxToStudio and TraceToStudio — the server's
// entry points for tracing rays and swept boxes against studio hitboxes
// (tracelist-to-studio, the physics consumer).
//
// The collision primitives the three functions lean on live in
// collisionutils.cpp in stock (IntersectRayWithBox, IntersectRayWithOBB,
// IsRayIntersectingSphere) and in mathlib (MatrixITransformPlane); they are
// ported here as private mirrors with their expression order kept, because
// no managed assembly owns them yet. They are listed in docs/plans/needs-trace.md for
// promotion to a shared trace/math home once a second consumer appears.
//
// Omissions, all IO-only or unreachable, per the port contract (native
// never decides; no convars, no clock, no static state):
//   * The three ClipRayToHitbox "trace precision error case" DevWarnings
//     are COMMENTED OUT in stock itself (oracle 5208-5209, 5218-5219,
//     5228-5229), so there is nothing to omit beyond their dead arms.
//   * cl_showshadowmesh / mod_studiocache: neither ConVar appears anywhere
//     in the three functions' bodies (grep of the pristine oracle finds no
//     hit in 5153-5433); the only ConVar reads in bone_setup.cpp are the
//     pose/telemetry ones other clusters document. Nothing to port.
//   * m_pBoxAbortFramework: not referenced by this cluster (it belongs to
//     the engine's ICollideable studio box framework, not these functions),
//     so no early-out parameter is needed.
//   * IPhysicsSurfaceProps *pProps: stock answers the surfaceProps via the
//     model/physics loader (pProps->GetSurfaceIndex(pBone->pszSurfaceProp())).
//     The port takes it as the injected SurfacePropReader delegate — the
//     hdr-read rule: the caller resolves what the loader would.
//   * Assert(tr.physicsbone >= 0) arms are release no-ops in stock; omitted.
//   * Entity/engine references (the engine's own trace filters) stay out:
//     contents mask, origin, scale and the bone matrices all arrive as
//     parameters.
//
// The content/solid gate is ported exactly: (boneContents & fContentsMask)
// == 0 skips the hitbox (stock 5277/5342), and the sweep loop breaks on
// tr.startsolid (5294-5295); the allsolid/startsolid arms arrive with the
// copied obbTrace (5287-5288), the same fields the ray arm's IntersectRay-
// WithBox startsolid arm produces.
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// Answers stock's <c>pProps->GetSurfaceIndex( pBone->pszSurfaceProp() )</c>
/// (bone_setup.cpp:5308/5409). Stock asks the physics surface-props service,
/// which the model loader feeds; managed code injects the lookup instead
/// (per the porting rule, and inventory row 109's "injected surface props").
/// The delegate receives the hdr and the bone index and returns the
/// surfaceProps index — the game-side answer over the bone record's
/// <c>surfacepropidx</c>.
/// </summary>
public delegate int SurfacePropReader(StudioHdr hdr, int boneIndex);

/// <summary>
/// The fields of stock's <c>trace_t</c> / <c>CBaseTrace</c> (trace.h:50-60,
/// IEngineTrace.h trace_t) this cluster writes. A plain mutable carrier —
/// stock passes <c>trace_t&amp;</c> in and out, and the sweep loop copies
/// whole-trace results out of a local, so this is a class, not a ref struct.
/// The <c>cplane_t</c> triple is flattened to <see cref="PlaneNormal"/> /
/// <see cref="PlaneDist"/> / <see cref="PlaneType"/>; keeping cplane_t's
/// <c>type</c> semantics (0..2 = axis fast planes, 3 = general) is what the
/// ported callers read.
/// </summary>
public sealed class BoneTrace
{
    public Vector StartPos;
    public Vector EndPos;
    public Vector PlaneNormal;
    public float PlaneDist;
    public int PlaneType;
    public float Fraction = 1.0f;
    public int Contents;
    public bool AllSolid;
    public bool StartSolid;
    public int HitGroup;
    public int HitBox = -1;
    public int PhysicsBone;
    public int SurfaceFlags;
    public int SurfaceProps;
    public string? SurfaceName;
}

/// <summary>
/// Stock's <c>Ray_t</c> (cmodel.h:61-99): a segment, optionally an AABB
/// extruded along it. <see cref="FromPoints"/> is <c>Init(start,end)</c>;
/// <see cref="FromBox"/> is <c>Init(start,end,mins,maxs)</c> including the
/// <c>1e-6</c> extents test and the negated <c>m_StartOffset</c> — the two
/// arms the TraceToStudio scaled-ray rebuild and the sweep dispatch depend on.
/// </summary>
public struct BoneRay
{
    public Vector Start;
    public Vector Delta;
    public Vector StartOffset;
    public Vector Extents;
    public bool IsRay;
    public bool IsSwept;

    /// <summary><c>Ray_t::Init(start, end)</c>: zero extents, ray form.</summary>
    public static BoneRay FromPoints(Vector start, Vector end)
    {
        Vector delta = end - start;
        return new BoneRay
        {
            Start = start,
            Delta = delta,
            StartOffset = Vector.Zero,
            Extents = Vector.Zero,
            IsRay = true,
            IsSwept = BoneSetupMath.VectorLengthSqr(delta) != 0,
        };
    }

    /// <summary><c>Ray_t::Init(start, end, mins, maxs)</c>, field for field.</summary>
    public static BoneRay FromBox(Vector start, Vector end, Vector mins, Vector maxs)
    {
        Vector delta = end - start;
        Vector extents = (maxs - mins) * 0.5f;
        Vector startOffset = (mins + maxs) * 0.5f;
        return new BoneRay
        {
            Start = start + startOffset,
            Delta = delta,
            StartOffset = -startOffset,
            Extents = extents,
            IsRay = BoneSetupMath.VectorLengthSqr(extents) < 1e-6f,
            IsSwept = BoneSetupMath.VectorLengthSqr(delta) != 0,
        };
    }
}

/// <summary>
/// The server-side studio-model trace entry points, line-for-line from the
/// pristine oracle (citations are to <c>src/public/bone_setup.cpp</c> at
/// valve-sdk.lock 22288b91; the private collision mirrors cite
/// <c>src/public/collisionutils.cpp</c> and <c>src/public/mathlib/mathlib.h</c>).
/// </summary>
public static class BoneSetupTrace
{
    /// <summary>bspflags.h:26.</summary>
    public const int ContentsSolid = 0x1;

    /// <summary>bspflags.h:76.</summary>
    public const int ContentsHitbox = unchecked((int)0x40000000);

    /// <summary>bspflags.h:97.</summary>
    public const int SurfHitbox = 0x8000;

    /// <summary>The surface name stock stamps on a hitbox trace
    /// (bone_setup.cpp:5306/5407).</summary>
    public const string StudioSurfaceName = "**studio**";

    // -----------------------------------------------------------------
    // ClipRayToHitbox (oracle 5153-5253; file-static in stock)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>static int ClipRayToHitbox( const Ray_t &amp;ray, mstudiobbox_t
    /// *pbox, matrix3x4_t&amp; matrix, trace_t &amp;tr )</c> — oracle
    /// 5153-5253. Separating-axis test of the half-delta segment against the
    /// box (three box axes, then three cross axes with the
    /// <c>flProjEpsilon = 0.01f</c> precision floor stock added over
    /// collisionutils' plain compare), then the exact slab clip in bone space.
    /// Returns stock's hitside (0..2 = the negative-axis face, 3..5 = the
    /// positive one, chosen by the sign of the box-space plane's own axis
    /// component, oracle 5248-5253) or -1 for no hit. The caller
    /// (<see cref="TraceToStudio"/>) turns the hitside into the world plane.
    /// <para>
    /// Side effects match stock: on a hit <c>tr.Fraction *= boxTrace.fraction</c>
    /// and <c>tr.startsolid = boxTrace.startsolid</c> only (5246-5247) — the
    /// ray was pre-scaled by the current fraction, so no closer-hit test is
    /// needed here. The three commented-out <c>DevWarning</c> precision-case
    /// arms (5208-5209, 5218-5219, 5228-5229) are dead in stock and absent.
    /// </para>
    /// </summary>
    internal static int ClipRayToHitbox(
        in BoneRay ray, MstudioBBox pbox, in BoneMatrix3x4 matrix, BoneTrace tr)
    {
        const float flProjEpsilon = 0.01f;
        // scale by current t so hits shorten the ray and increase the
        // likelihood of early outs
        Vector delta2 = BoneSetupMath.VectorScale(ray.Delta, (0.5f * tr.Fraction));

        // compute center in local space, transform to world space
        Vector boxextents = new(
            (pbox.BbMin.X + pbox.BbMax.X) * 0.5f,
            (pbox.BbMin.Y + pbox.BbMax.Y) * 0.5f,
            (pbox.BbMin.Z + pbox.BbMax.Z) * 0.5f);
        Vector boxCenter = BoneSetupMath.VectorTransform(boxextents, matrix);
        // calc extents from local center
        boxextents = new(
            pbox.BbMax.X - boxextents.X,
            pbox.BbMax.Y - boxextents.Y,
            pbox.BbMax.Z - boxextents.Z);

        Vector segmentCenter = new(
            ray.Start.X + delta2.X - boxCenter.X,
            ray.Start.Y + delta2.Y - boxCenter.Y,
            ray.Start.Z + delta2.Z - boxCenter.Z);

        // check box axes for separation (stock's loop over extent[j];
        // extent[j] = dot(delta2, column j) = VectorIRotate's component sum)
        Vector extent = BoneSetupMath.VectorIRotate(delta2, matrix);
        Vector uextent = Vector.Abs(extent);
        Vector scProj = BoneSetupMath.VectorIRotate(segmentCenter, matrix);
        Vector scAbs = Vector.Abs(scProj);
        if (scAbs.X > (boxextents.X + uextent.X))
        {
            return -1;
        }

        if (scAbs.Y > (boxextents.Y + uextent.Y))
        {
            return -1;
        }

        if (scAbs.Z > (boxextents.Z + uextent.Z))
        {
            return -1;
        }

        // now check cross axes for separation
        Vector cross = BoneSetupMath.CrossProduct(delta2, segmentCenter);
        Vector crossProj = BoneSetupMath.VectorIRotate(cross, matrix);
        float cextent = MathF.Abs(crossProj.X);
        float tmp = boxextents.Y * uextent.Z + boxextents.Z * uextent.Y;
        float tmpfix = MathF.Max(tmp, flProjEpsilon);
        if (cextent > tmpfix)
        {
            return -1;
        }

        cextent = MathF.Abs(crossProj.Y);
        tmp = boxextents.X * uextent.Z + boxextents.Z * uextent.X;
        tmpfix = MathF.Max(tmp, flProjEpsilon);
        if (cextent > tmpfix)
        {
            return -1;
        }

        cextent = MathF.Abs(crossProj.Z);
        tmp = boxextents.X * uextent.Y + boxextents.Y * uextent.X;
        tmpfix = MathF.Max(tmp, flProjEpsilon);
        if (cextent > tmpfix)
        {
            return -1;
        }

        // !!! We hit this box !!! compute intersection point and return
        // Compute ray start in bone space
        Vector start = BoneSetupMath.VectorITransform(ray.Start, matrix);
        // extent is delta2 in bone space, recompute delta in bone space
        extent = BoneSetupMath.VectorScale(extent, 2);

        // delta was prescaled by the current t, so no need to see if this
        // intersection is closer
        BoneTrace boxTrace = new();
        if (!IntersectRayWithBox(start, extent, pbox.BbMin, pbox.BbMax, 0.0f, boxTrace))
        {
            return -1;
        }

        tr.Fraction *= boxTrace.Fraction;
        tr.StartSolid = boxTrace.StartSolid;
        int hitside = boxTrace.PlaneType;
        if (boxTrace.PlaneNormal[hitside] >= 0)
        {
            hitside += 3;
        }

        return hitside;
    }

    // -----------------------------------------------------------------
    // SweepBoxToStudio (oracle 5262-5314)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>bool SweepBoxToStudio( IPhysicsSurfaceProps *pProps, const Ray_t&amp;
    /// ray, CStudioHdr *pStudioHdr, mstudiohitboxset_t *set, matrix3x4_t
    /// **hitboxbones, int fContentsMask, trace_t &amp;tr )</c> — oracle
    /// 5262-5314. Box-sweep arm: per hitbox, the contents gate
    /// <c>(pBone->contents &amp; fContentsMask) == 0</c> skips it, then
    /// <c>IntersectRayWithOBB</c> against the bone's world matrix. On a hit
    /// the whole obbTrace is copied (startpos/endpos/plane/allsolid/
    /// startsolid), the fraction MULTIPLIES down and the working ray's delta
    /// shortens with it (the early-out shrink), and <c>tr.startsolid</c>
    /// breaks the loop. The nearest-hit bookkeeping is that shrink: the last
    /// hitbox to survive it owns the trace.
    /// <para>
    /// Stock's "FIXME: Won't work with scaling!" comment (5280) stands as in
    /// stock: this entry point takes no scale (the scale arm lives in
    /// <see cref="TraceToStudio"/>). The <c>pProps</c> answer becomes the
    /// injected <paramref name="surfaceProps"/> delegate.
    /// </para>
    /// </summary>
    public static bool SweepBoxToStudio(
        StudioHdr hdr,
        MstudioHitboxSet set,
        BoneMatrix3x4[] hitboxBones,
        in BoneRay ray,
        int fContentsMask,
        BoneTrace tr,
        SurfacePropReader surfaceProps)
    {
        tr.Fraction = 1.0f;
        tr.StartSolid = false;

        BoneRay clippedRay = ray;
        int hitbox = -1;
        for (int i = 0; i < set.NumHitboxes; i++)
        {
            MstudioBBox pbox = set.Hitbox(i);

            // Filter based on contents mask
            int fBoneContents = ReadBoneContents(hdr, pbox.Bone);
            if ((fBoneContents & fContentsMask) == 0)
            {
                continue;
            }

            // FIXME in stock: won't work with scaling!
            BoneTrace obbTrace = new();
            if (IntersectRayWithOBB(
                    clippedRay, hitboxBones[pbox.Bone], pbox.BbMin, pbox.BbMax, 0.0f, obbTrace))
            {
                tr.StartPos = obbTrace.StartPos;
                tr.EndPos = obbTrace.EndPos;
                tr.PlaneNormal = obbTrace.PlaneNormal;
                tr.PlaneDist = obbTrace.PlaneDist;
                tr.PlaneType = obbTrace.PlaneType;
                tr.StartSolid = obbTrace.StartSolid;
                tr.AllSolid = obbTrace.AllSolid;

                // This logic here is to shorten the ray each time to get
                // more early outs
                tr.Fraction *= obbTrace.Fraction;
                clippedRay.Delta *= obbTrace.Fraction;
                hitbox = i;
                if (tr.StartSolid)
                {
                    break;
                }
            }
        }

        if (hitbox >= 0)
        {
            tr.HitGroup = set.Hitbox(hitbox).Group;
            tr.HitBox = hitbox;
            int bone = set.Hitbox(hitbox).Bone;
            tr.Contents = ReadBoneContents(hdr, bone) | ContentsHitbox;
            tr.PhysicsBone = ReadPhysicsBone(hdr, bone);
            tr.SurfaceName = StudioSurfaceName;
            tr.SurfaceFlags = SurfHitbox;
            tr.SurfaceProps = surfaceProps(hdr, bone);
            return true;
        }

        return false;
    }

    // -----------------------------------------------------------------
    // TraceToStudio (oracle 5320-5433)
    // -----------------------------------------------------------------

    /// <summary>
    /// <c>bool TraceToStudio( IPhysicsSurfaceProps *pProps, const Ray_t&amp;
    /// ray, CStudioHdr *pStudioHdr, mstudiohitboxset_t *set, matrix3x4_t
    /// **hitboxbones, int fContentsMask, const Vector &amp;vecOrigin, float
    /// flScale, trace_t &amp;tr )</c> — oracle 5320-5433.
    /// <para>
    /// Delegation arm first, exactly as stock: a non-ray <c>Ray_t</c> (the
    /// swept-AABB form) hands the whole job to
    /// <see cref="SweepBoxToStudio"/>; scale and origin do not exist on that
    /// path. The ray arm then clips every contents-passing hitbox with
    /// <see cref="ClipRayToHitbox"/>, in the pre-scaled space when
    /// <c>flScale</c> differs from 1 by more than <c>FLT_EPSILON</c>: the
    /// bone origins and the ray start are pulled toward <c>vecOrigin</c> by
    /// <c>1/flScale</c> and the matrices and ray delta scale uniformly
    /// (oracle 5353-5384), because the bone-space inversion inside the clip
    /// cannot carry scale.
    /// </para>
    /// <para>
    /// On a hit, stock rebuilds the trace end position from the accumulated
    /// fraction, stamps group/hitbox/contents|CONTENTS_HITBOX/physicsbone and
    /// the <c>**studio**</c> surface, then turns the box-space hitside into
    /// the world plane: the matrix's <c>hitside</c> column, negated for the
    /// negative-axis faces, with <c>dist = dot(endpos, normal)</c> and
    /// <c>type = 3</c> (oracle 5412-5430).
    /// </para>
    /// <para>
    /// <c>pProps</c> becomes <paramref name="surfaceProps"/>; the release
    /// <c>Assert(tr.physicsbone &gt;= 0)</c> is a no-op in stock and omitted;
    /// the shadow-mesh/studiocache telemetry paths the cluster overview
    /// mentions are not in this function's body (grep of the oracle finds no
    /// <c>cl_showshadowmesh</c> / <c>mod_studiocache</c> reference in
    /// 5320-5433), so the decision logic is complete as ported.
    /// </para>
    /// </summary>
    public static bool TraceToStudio(
        StudioHdr hdr,
        MstudioHitboxSet set,
        BoneMatrix3x4[] hitboxBones,
        in BoneRay ray,
        int fContentsMask,
        Vector vecOrigin,
        float flScale,
        BoneTrace tr,
        SurfacePropReader surfaceProps)
    {
        if (!ray.IsRay)
        {
            return SweepBoxToStudio(hdr, set, hitboxBones, ray, fContentsMask, tr, surfaceProps);
        }

        tr.Fraction = 1.0f;
        tr.StartSolid = false;

        // no hit yet
        int hitbox = -1;
        int hitside = -1;

        for (int i = 0; i < set.NumHitboxes; i++)
        {
            MstudioBBox pbox = set.Hitbox(i);

            // Filter based on contents mask
            int fBoneContents = ReadBoneContents(hdr, pbox.Bone);
            if ((fBoneContents & fContentsMask) == 0)
            {
                continue;
            }

            // columns are axes of the bones in world space, translation is
            // in world space
            BoneMatrix3x4 matrix = hitboxBones[pbox.Bone];

            // Because we're sending in a matrix with scale data, and because
            // the matrix inversion in the hitbox code does not handle that
            // case, we pre-scale the bones and ray down here and do our
            // collision checks in unscaled space.
            int side = -1;
            if (flScale < 1.0f - BoneSetupMath.FltEpsilon
                || flScale > 1.0f + BoneSetupMath.FltEpsilon)
            {
                BoneMatrix3x4 matScaled = matrix;
                float invScale = 1.0f / flScale;

                Vector vecBoneOrigin = matScaled.Position;

                // Pre-scale the origin down
                Vector vecNewOrigin = vecBoneOrigin - vecOrigin;
                vecNewOrigin *= invScale;
                vecNewOrigin += vecOrigin;
                matScaled.Position = vecNewOrigin;

                // Scale it uniformly (basis only — VectorScale over a
                // matrix3x4 row touches m[i][0..2], the translation stays)
                matScaled.Row0.X *= invScale;
                matScaled.Row0.Y *= invScale;
                matScaled.Row0.Z *= invScale;
                matScaled.Row1.X *= invScale;
                matScaled.Row1.Y *= invScale;
                matScaled.Row1.Z *= invScale;
                matScaled.Row2.X *= invScale;
                matScaled.Row2.Y *= invScale;
                matScaled.Row2.Z *= invScale;

                // Pre-scale our ray as well
                Vector vecRayStart = ray.Start - vecOrigin;
                vecRayStart *= invScale;
                vecRayStart += vecOrigin;

                Vector vecRayDelta = ray.Delta * invScale;

                BoneRay newRay = BoneRay.FromPoints(vecRayStart, vecRayStart + vecRayDelta);

                side = ClipRayToHitbox(newRay, pbox, matScaled, tr);
            }
            else
            {
                side = ClipRayToHitbox(ray, pbox, matrix, tr);
            }

            if (side >= 0)
            {
                hitbox = i;
                hitside = side;
            }
        }

        if (hitbox >= 0)
        {
            MstudioBBox pbox = set.Hitbox(hitbox);
            tr.EndPos = BoneSetupMath.VectorMA(ray.Start, tr.Fraction, ray.Delta);
            tr.HitGroup = pbox.Group;
            tr.HitBox = hitbox;
            tr.Contents = ReadBoneContents(hdr, pbox.Bone) | ContentsHitbox;
            tr.PhysicsBone = ReadPhysicsBone(hdr, pbox.Bone);
            tr.SurfaceName = StudioSurfaceName;
            tr.SurfaceFlags = SurfHitbox;
            tr.SurfaceProps = surfaceProps(hdr, pbox.Bone);

            ref BoneMatrix3x4 matrix = ref hitboxBones[pbox.Bone];
            if (hitside >= 3)
            {
                hitside -= 3;
                // stock reads column `hitside` of the rows: m[0][h], m[1][h],
                // m[2][h]
                tr.PlaneNormal = new Vector(
                    Elem(matrix.Row0, hitside),
                    Elem(matrix.Row1, hitside),
                    Elem(matrix.Row2, hitside));
            }
            else
            {
                tr.PlaneNormal = new Vector(
                    -Elem(matrix.Row0, hitside),
                    -Elem(matrix.Row1, hitside),
                    -Elem(matrix.Row2, hitside));
            }

            // simpler plane constant equation
            tr.PlaneDist = BoneSetupMath.Dot(tr.EndPos, tr.PlaneNormal);
            tr.PlaneType = 3;
            return true;
        }

        return false;
    }

    // -----------------------------------------------------------------
    // private collision mirrors — collisionutils.cpp / mathlib.h.
    // Listed in docs/plans/needs-trace.md for promotion once a second consumer exists.
    // -----------------------------------------------------------------

    /// <summary>BoxTraceInfo_t (collisionutils.h:133-139), the internal slab
    /// result of IntersectRayWithBox.</summary>
    private struct BoxTraceInfo
    {
        public float T1;
        public float T2;
        public int HitSide;
        public bool StartSolid;
    }

    /// <summary>Stock's matrix3x4 column read <c>matrix[row][col]</c> over
    /// the row-major rows; column 3 is the row's translation element
    /// (collisionutils.cpp:1478-1482 adds <c>matOBBToWorld[i][3]</c> to the
    /// bounding-sphere center). The hitside/plane-type callers only ever
    /// pass 0..2 (bone_setup.cpp:5416-5425, collisionutils.cpp:1384-1386 —
    /// IntersectRayWithBox types its plane 0..2 or 0), so 3 belongs to the
    /// translation arm alone.</summary>
    private static float Elem(System.Numerics.Vector4 row, int col) => col switch
    {
        0 => row.X,
        1 => row.Y,
        2 => row.Z,
        _ => row.W,
    };

    /// <summary>Stock's <c>v[i]</c> float-array-style component read on a
    /// Vector (collisionutils indexes Vector3s this way throughout).</summary>
    private static float Component(Vector v, int i) => i switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    /// <summary>The same read over a matrix row (a Vector4 view of
    /// <c>matOBBToWorld[row]</c>, first three elements).</summary>
    private static float Component(System.Numerics.Vector4 v, int i) => i switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    /// <summary>Stock's row read <c>const float *p = matOBBToWorld[i]</c>:
    /// the first three elements of row i as the basis vector (the rows are
    /// the OBB's world-space axes; <c>VectorCopy( matOBBToWorld[i], ...
    /// )</c> at collisionutils.cpp:1534 copies exactly these).</summary>
    private static Vector RowVector(in BoneMatrix3x4 m, int i) => i switch
    {
        0 => new Vector(m.Row0.X, m.Row0.Y, m.Row0.Z),
        1 => new Vector(m.Row1.X, m.Row1.Y, m.Row1.Z),
        _ => new Vector(m.Row2.X, m.Row2.Y, m.Row2.Z),
    };

    /// <summary>Whole row i as stock's <c>const float *</c> view.</summary>
    private static System.Numerics.Vector4 Row(in BoneMatrix3x4 m, int i) => i switch
    {
        0 => m.Row0,
        1 => m.Row1,
        _ => m.Row2,
    };

    /// <summary>
    /// collisionutils.cpp:1130-1202 — <c>IntersectRayWithBox( start, delta,
    /// min, max, tolerance, BoxTraceInfo_t* )</c>, the six-face slab walk.
    /// The faces come in (min, min, min, max, max, max) order: the negative
    /// faces measure <c>-start[i] + min[i]</c> with the delta SUBTRACTED, the
    /// positive ones <c>start[i] - max[i]</c> with it added — the sign flip
    /// is what makes one loop cover both families. Kept expression for
    /// expression, including the tolerance clamps.
    /// </summary>
    private static bool IntersectRayWithBoxCore(
        Vector rayStart, Vector rayDelta, Vector boxMins, Vector boxMaxs,
        float flTolerance, ref BoxTraceInfo trace)
    {
        trace.T1 = -1.0f;
        trace.T2 = 1.0f;
        trace.HitSide = -1;
        trace.StartSolid = true;

        for (int i = 0; i < 6; ++i)
        {
            float d1, d2;
            if (i >= 3)
            {
                d1 = rayStart[i - 3] - boxMaxs[i - 3];
                d2 = d1 + rayDelta[i - 3];
            }
            else
            {
                d1 = -rayStart[i] + boxMins[i];
                d2 = d1 - rayDelta[i];
            }

            // if completely in front of face, no intersection
            if (d1 > 0 && d2 > 0)
            {
                trace.StartSolid = false;
                return false;
            }

            // completely inside, check next face
            if (d1 <= 0 && d2 <= 0)
            {
                continue;
            }

            if (d1 > 0)
            {
                trace.StartSolid = false;
            }

            float f;
            // crosses face
            if (d1 > d2)
            {
                f = d1 - flTolerance;
                if (f < 0)
                {
                    f = 0;
                }

                f = f / (d1 - d2);
                if (f > trace.T1)
                {
                    trace.T1 = f;
                    trace.HitSide = i;
                }
            }
            else
            {
                // leave
                f = (d1 + flTolerance) / (d1 - d2);
                if (f < trace.T2)
                {
                    trace.T2 = f;
                }
            }
        }

        return trace.StartSolid || (trace.T1 < trace.T2 && trace.T1 >= 0.0f);
    }

    /// <summary>collisionutils.cpp:28-37 — <c>Collision_ClearTrace</c>. Note
    /// what it does NOT touch: the plane and hitgroup fields keep whatever
    /// they held; every caller sets them in its own hit arm.</summary>
    private static void ClearTrace(Vector vecRayStart, Vector vecRayDelta, BoneTrace tr)
    {
        tr.StartPos = vecRayStart;
        tr.EndPos = vecRayStart;
        tr.EndPos += vecRayDelta;
        tr.StartSolid = false;
        tr.AllSolid = false;
        tr.Fraction = 1.0f;
        tr.Contents = 0;
    }

    /// <summary>
    /// collisionutils.cpp:1208-1259 — the <c>CBaseTrace</c> arm of
    /// <c>IntersectRayWithBox(start, delta, min, max, tolerance, pTrace)</c>.
    /// Clear, run the slab core, then the two hit arms: the clean-entry arm
    /// publishes t1 with its axis plane, the startsolid arm zeroes the
    /// fraction, sets <c>allsolid</c> from t2 and drags startpos to the exit
    /// point (5253). The <c>pFractionLeftSolid</c> out-param is unused on
    /// this path (stock passes NULL at bone_setup.cpp:5242) so it is not
    /// ported.
    /// </summary>
    private static bool IntersectRayWithBox(
        Vector vecRayStart, Vector vecRayDelta, Vector boxMins, Vector boxMaxs,
        float flTolerance, BoneTrace tr)
    {
        ClearTrace(vecRayStart, vecRayDelta, tr);

        BoxTraceInfo trace = new();
        if (IntersectRayWithBoxCore(vecRayStart, vecRayDelta, boxMins, boxMaxs, flTolerance, ref trace))
        {
            tr.StartSolid = trace.StartSolid;
            if (trace.T1 < trace.T2 && trace.T1 >= 0.0f)
            {
                tr.Fraction = trace.T1;
                tr.EndPos = BoneSetupMath.VectorMA(tr.StartPos, trace.T1, vecRayDelta);
                tr.Contents = ContentsSolid;
                Vector planeNormal = Vector.Zero;
                int hitside = trace.HitSide;
                if (hitside >= 3)
                {
                    hitside -= 3;
                    tr.PlaneDist = boxMaxs[hitside];
                    planeNormal[hitside] = 1.0f;
                    tr.PlaneType = hitside;
                }
                else
                {
                    tr.PlaneDist = -boxMins[hitside];
                    planeNormal[hitside] = -1.0f;
                    tr.PlaneType = hitside;
                }

                tr.PlaneNormal = planeNormal;
                return true;
            }

            if (tr.StartSolid)
            {
                tr.AllSolid = (trace.T2 <= 0.0f) || (trace.T2 >= 1.0f);
                tr.Fraction = 0;
                tr.EndPos = tr.StartPos;
                tr.Contents = ContentsSolid;
                tr.PlaneDist = tr.StartPos.X;
                tr.PlaneNormal = new Vector(1.0f, 0.0f, 0.0f);
                tr.PlaneType = 0;
                tr.StartPos = vecRayStart + (trace.T2 * vecRayDelta);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// collisionutils.cpp:1301-1393 — the <c>CBaseTrace</c> arm of
    /// <c>IntersectRayWithOBB(start, delta, matrix, min, max, tolerance,
    /// pTrace)</c>: SAT against the box axes and the three cross axes (no
    /// epsilon floor — that is ClipRayToHitbox's addition only), then the
    /// same bone-space slab clip; on a hit the end position goes back through
    /// the matrix, fraction doubles against the half-delta it clipped, and
    /// the axis plane is rebuilt as <c>flSign *</c> the matrix's <c>type</c>
    /// column with <c>dist = dot(endpos, normal)</c>, <c>type = 3</c>.
    /// Stock's <c>Assert(flTolerance == 0)</c> is a no-op; every caller
    /// passes 0.
    /// </summary>
    private static bool IntersectRayWithOBB(
        Vector vecRayStart, Vector vecRayDelta, in BoneMatrix3x4 matOBBToWorld,
        Vector vecOBBMins, Vector vecOBBMaxs, float flTolerance, BoneTrace tr)
    {
        ClearTrace(vecRayStart, vecRayDelta, tr);

        // compute center in local space, transform to world space
        Vector vecBoxExtents = new(
            (vecOBBMins.X + vecOBBMaxs.X) * 0.5f,
            (vecOBBMins.Y + vecOBBMaxs.Y) * 0.5f,
            (vecOBBMins.Z + vecOBBMaxs.Z) * 0.5f);
        Vector vecBoxCenter = BoneSetupMath.VectorTransform(vecBoxExtents, matOBBToWorld);
        // calc extents from local center
        vecBoxExtents = vecOBBMaxs - vecBoxExtents;

        Vector segmentCenter = vecRayStart + vecRayDelta - vecBoxCenter;

        // check box axes for separation
        Vector extent = BoneSetupMath.VectorIRotate(vecRayDelta, matOBBToWorld);
        Vector uextent = Vector.Abs(extent);
        Vector scAbs = Vector.Abs(BoneSetupMath.VectorIRotate(segmentCenter, matOBBToWorld));
        if (scAbs.X > (vecBoxExtents.X + uextent.X))
        {
            return false;
        }

        if (scAbs.Y > (vecBoxExtents.Y + uextent.Y))
        {
            return false;
        }

        if (scAbs.Z > (vecBoxExtents.Z + uextent.Z))
        {
            return false;
        }

        // now check cross axes for separation
        Vector cross = BoneSetupMath.CrossProduct(vecRayDelta, segmentCenter);
        Vector crossProj = BoneSetupMath.VectorIRotate(cross, matOBBToWorld);
        float cextent = MathF.Abs(crossProj.X);
        float tmp = vecBoxExtents.Y * uextent.Z + vecBoxExtents.Z * uextent.Y;
        if (cextent > tmp)
        {
            return false;
        }

        cextent = MathF.Abs(crossProj.Y);
        tmp = vecBoxExtents.X * uextent.Z + vecBoxExtents.Z * uextent.X;
        if (cextent > tmp)
        {
            return false;
        }

        cextent = MathF.Abs(crossProj.Z);
        tmp = vecBoxExtents.X * uextent.Y + vecBoxExtents.Y * uextent.X;
        if (cextent > tmp)
        {
            return false;
        }

        // !!! We hit this box !!! compute intersection point and return
        // Compute ray start in bone space
        Vector start = BoneSetupMath.VectorITransform(vecRayStart, matOBBToWorld);
        // extent is the half-delta in bone space, recompute delta
        extent *= 2.0f;

        // delta was prescaled by the current t, so no need to see if this
        // intersection is closer
        if (!IntersectRayWithBox(start, extent, vecOBBMins, vecOBBMaxs, flTolerance, tr))
        {
            return false;
        }

        // Fix up the start/end pos and fraction
        tr.EndPos = BoneSetupMath.VectorTransform(tr.EndPos, matOBBToWorld);
        tr.StartPos = vecRayStart;
        tr.Fraction *= 2.0f;

        // Fix up the plane information
        int planeType = tr.PlaneType;
        float flSign = tr.PlaneNormal[planeType];
        tr.PlaneNormal = new Vector(
            flSign * Elem(matOBBToWorld.Row0, planeType),
            flSign * Elem(matOBBToWorld.Row1, planeType),
            flSign * Elem(matOBBToWorld.Row2, planeType));
        tr.PlaneDist = BoneSetupMath.Dot(tr.EndPos, tr.PlaneNormal);
        tr.PlaneType = 3;
        return true;
    }

    /// <summary>
    /// collisionutils.cpp:1465-1685 — the <c>Ray_t</c> dispatch of
    /// <c>IntersectRayWithOBB</c>. The ray form reuses the arm above; the
    /// swept-box form shrinks the box to a point and bloats the OBB by the
    /// ray extents (the explicit-GJK 15-separating-plane arm), exactly as
    /// stock: bounding-sphere trivial reject, the 9 OBB/edge plane sets built
    /// with the two support-map shapes, the enter/leave clip over both plane
    /// sides, and the two hit arms.
    /// </summary>
    private static bool IntersectRayWithOBB(
        in BoneRay ray, in BoneMatrix3x4 matOBBToWorld,
        Vector vecOBBMins, Vector vecOBBMaxs, float flTolerance, BoneTrace tr)
    {
        if (ray.IsRay)
        {
            return IntersectRayWithOBB(
                ray.Start, ray.Delta, matOBBToWorld, vecOBBMins, vecOBBMaxs, flTolerance, tr);
        }

        ClearTrace(ray.Start + ray.StartOffset, ray.Delta, tr);

        // Compute a bounding sphere around the bloated OBB
        Vector vecOBBCenter = (vecOBBMins + vecOBBMaxs) * 0.5f;
        vecOBBCenter.X += Elem(matOBBToWorld.Row0, 3);
        vecOBBCenter.Y += Elem(matOBBToWorld.Row1, 3);
        vecOBBCenter.Z += Elem(matOBBToWorld.Row2, 3);

        Vector vecOBBHalfDiagonal = (vecOBBMaxs - vecOBBMins) * 0.5f;
        float flRadius = BoneSetupMath.VectorLength(vecOBBHalfDiagonal)
                       + BoneSetupMath.VectorLength(ray.Extents);
        if (!IsRayIntersectingSphere(ray.Start, ray.Delta, vecOBBCenter, flRadius, flTolerance))
        {
            return false;
        }

        // transform the ray into the space of the OBB
        Vector vecLocalRayOrigin = BoneSetupMath.VectorITransform(ray.Start, matOBBToWorld);
        Vector vecLocalRayDirection = BoneSetupMath.VectorIRotate(ray.Delta, matOBBToWorld);

        // Next compute all separating planes
        Vector[] pPlaneNormal = new Vector[15];
        float[][] ppPlaneDist = new float[15][];
        for (int k = 0; k < 15; k++)
        {
            ppPlaneDist[k] = new float[2];
        }

        // s_ExtIndices / s_MatIndices (collisionutils.cpp:1451-1463)
        int[][] sExtIndices = [[2, 1], [0, 2], [0, 1]];
        int[][] sMatIndices = [[1, 2], [2, 0], [1, 0]];

        for (int i = 0; i < 3; ++i)
        {
            // the OBB planes, bloated by the extents dot in world space
            pPlaneNormal[i] = i switch
            {
                0 => new Vector(1f, 0f, 0f),
                1 => new Vector(0f, 1f, 0f),
                _ => new Vector(0f, 0f, 1f),
            };

            float flExtentDotNormal =
                MathF.Abs(Elem(matOBBToWorld.Row0, i) * ray.Extents.X) +
                MathF.Abs(Elem(matOBBToWorld.Row1, i) * ray.Extents.Y) +
                MathF.Abs(Elem(matOBBToWorld.Row2, i) * ray.Extents.Z);

            ppPlaneDist[i][0] = Component(vecOBBMins, i) - flExtentDotNormal;
            ppPlaneDist[i][1] = Component(vecOBBMaxs, i) + flExtentDotNormal;

            // the ray-extents planes, bloated by the extents, support-map
            // vertex chosen
            pPlaneNormal[i + 3] = RowVector(matOBBToWorld, i);
            ComputeSupportMap(pPlaneNormal[i + 3], vecOBBMins, vecOBBMaxs, ppPlaneDist[i + 3]);
            ppPlaneDist[i + 3][0] -= ray.Extents[i];
            ppPlaneDist[i + 3][1] += ray.Extents[i];

            // the edge cases: cross of the world ith axis with the OBB axes
            float flRayExtent0 = ray.Extents[sExtIndices[i][0]];
            float flRayExtent1 = ray.Extents[sExtIndices[i][1]];
            System.Numerics.Vector4 pMatRow0 = Row(matOBBToWorld, sMatIndices[i][0]);
            System.Numerics.Vector4 pMatRow1 = Row(matOBBToWorld, sMatIndices[i][1]);
            Vector matRowI = RowVector(matOBBToWorld, i);

            // x axis of the OBB + world ith axis
            pPlaneNormal[i + 6] = new Vector(0.0f, -matRowI.Z, matRowI.Y);
            ComputeSupportMap(pPlaneNormal[i + 6], 1, 2, vecOBBMins, vecOBBMaxs, ppPlaneDist[i + 6]);
            flExtentDotNormal =
                MathF.Abs(Component(pMatRow0, 0)) * flRayExtent0 +
                MathF.Abs(Component(pMatRow1, 0)) * flRayExtent1;
            ppPlaneDist[i + 6][0] -= flExtentDotNormal;
            ppPlaneDist[i + 6][1] += flExtentDotNormal;

            // y axis of the OBB + world ith axis
            pPlaneNormal[i + 9] = new Vector(matRowI.Z, 0.0f, -matRowI.X);
            ComputeSupportMap(pPlaneNormal[i + 9], 0, 2, vecOBBMins, vecOBBMaxs, ppPlaneDist[i + 9]);
            flExtentDotNormal =
                MathF.Abs(Component(pMatRow0, 1)) * flRayExtent0 +
                MathF.Abs(Component(pMatRow1, 1)) * flRayExtent1;
            ppPlaneDist[i + 9][0] -= flExtentDotNormal;
            ppPlaneDist[i + 9][1] += flExtentDotNormal;

            // z axis of the OBB + world ith axis
            pPlaneNormal[i + 12] = new Vector(-matRowI.Y, matRowI.X, 0.0f);
            ComputeSupportMap(pPlaneNormal[i + 12], 0, 1, vecOBBMins, vecOBBMaxs, ppPlaneDist[i + 12]);
            flExtentDotNormal =
                MathF.Abs(Component(pMatRow0, 2)) * flRayExtent0 +
                MathF.Abs(Component(pMatRow1, 2)) * flRayExtent1;
            ppPlaneDist[i + 12][0] -= flExtentDotNormal;
            ppPlaneDist[i + 12][1] += flExtentDotNormal;
        }

        float enterfrac, leavefrac;
        int hitplane = -1;
        int hitside = -1;
        enterfrac = -1.0f;
        leavefrac = 1.0f;

        tr.StartSolid = true;

        Vector vecLocalRayEnd = vecLocalRayOrigin + vecLocalRayDirection;

        for (int i = 0; i < 15; ++i)
        {
            float flStartDot = BoneSetupMath.Dot(pPlaneNormal[i], vecLocalRayOrigin);
            float flEndDot = BoneSetupMath.Dot(pPlaneNormal[i], vecLocalRayEnd);

            float[] d1 = new float[2];
            float[] d2 = new float[2];
            // Negative here is because the plane normal + dist are defined
            // in negative terms for the far plane (plane dist index 0)
            d1[0] = -(flStartDot - ppPlaneDist[i][0]);
            d2[0] = -(flEndDot - ppPlaneDist[i][0]);

            d1[1] = flStartDot - ppPlaneDist[i][1];
            d2[1] = flEndDot - ppPlaneDist[i][1];

            for (int j = 0; j < 2; ++j)
            {
                // if completely in front near plane or behind far plane no
                // intersection
                if (d1[j] > 0 && d2[j] > 0)
                {
                    return false;
                }

                // completely inside, check next plane set
                if (d1[j] <= 0 && d2[j] <= 0)
                {
                    continue;
                }

                if (d1[j] > 0)
                {
                    tr.StartSolid = false;
                }

                // crosses face
                float flDenom = 1.0f / (d1[j] - d2[j]);
                float f;
                if (d1[j] > d2[j])
                {
                    f = d1[j] - flTolerance;
                    if (f < 0)
                    {
                        f = 0;
                    }

                    f *= flDenom;
                    if (f > enterfrac)
                    {
                        enterfrac = f;
                        hitplane = i;
                        hitside = j;
                    }
                }
                else
                {
                    // leave
                    f = (d1[j] + flTolerance) * flDenom;
                    if (f < leavefrac)
                    {
                        leavefrac = f;
                    }
                }
            }
        }

        if (enterfrac < leavefrac && enterfrac >= 0.0f)
        {
            tr.Fraction = enterfrac;
            tr.EndPos = BoneSetupMath.VectorMA(tr.StartPos, enterfrac, ray.Delta);
            tr.Contents = ContentsSolid;

            // Need to transform the plane into world space...
            Vector tempNormal = pPlaneNormal[hitplane];
            float tempDist = ppPlaneDist[hitplane][hitside];
            if (hitside == 0)
            {
                tempNormal *= -1.0f;
                tempDist *= -1.0f;
            }

            // stock leaves the out-plane's `type` untouched here (the local
            // temp.type = 3 never reaches pTrace); the fresh local obbTrace
            // the caller passes starts at type 0, and this cluster never
            // reads it on the sweep path. Mirrored, not fixed.
            (tr.PlaneNormal, tr.PlaneDist) = MatrixITransformPlane(matOBBToWorld, tempNormal, tempDist);
            return true;
        }

        if (tr.StartSolid)
        {
            tr.AllSolid = (leavefrac <= 0.0f) || (leavefrac >= 1.0f);
            tr.Fraction = 0;
            tr.EndPos = tr.StartPos;
            tr.Contents = ContentsSolid;
            tr.PlaneDist = tr.StartPos.X;
            tr.PlaneNormal = new Vector(1.0f, 0.0f, 0.0f);
            tr.PlaneType = 0;
            return true;
        }

        return false;
    }

    /// <summary>collisionutils.cpp:375-411 — the closest-point-on-segment
    /// sphere test, tolerance folded into the radius.</summary>
    private static bool IsRayIntersectingSphere(
        Vector vecRayOrigin, Vector vecRayDelta, Vector vecCenter,
        float flRadius, float flTolerance)
    {
        flRadius += flTolerance;

        Vector vecRayToSphere = vecCenter - vecRayOrigin;
        float flNumerator = BoneSetupMath.Dot(vecRayToSphere, vecRayDelta);

        float t;
        if (flNumerator <= 0.0f)
        {
            t = 0.0f;
        }
        else
        {
            float flDenominator = BoneSetupMath.Dot(vecRayDelta, vecRayDelta);
            if (flNumerator > flDenominator)
            {
                t = 1.0f;
            }
            else
            {
                t = flNumerator / flDenominator;
            }
        }

        Vector vecClosestPoint = BoneSetupMath.VectorMA(vecRayOrigin, t, vecRayDelta);
        Vector delta = vecClosestPoint - vecCenter;
        return BoneSetupMath.VectorLengthSqr(delta) <= flRadius * flRadius;
    }

    /// <summary>collisionutils.cpp:1420-1434 — the full three-axis support
    /// map (the vertex whose corner maximises the direction's dot).</summary>
    private static void ComputeSupportMap(
        Vector vecDirection, Vector vecBoxMins, Vector vecBoxMaxs, float[] pDist)
    {
        int nIndex = vecDirection.X > 0.0f ? 1 : 0;
        pDist[nIndex] = vecBoxMaxs.X * vecDirection.X;
        pDist[1 - nIndex] = vecBoxMins.X * vecDirection.X;

        nIndex = vecDirection.Y > 0.0f ? 1 : 0;
        pDist[nIndex] += vecBoxMaxs.Y * vecDirection.Y;
        pDist[1 - nIndex] += vecBoxMins.Y * vecDirection.Y;

        nIndex = vecDirection.Z > 0.0f ? 1 : 0;
        pDist[nIndex] += vecBoxMaxs.Z * vecDirection.Z;
        pDist[1 - nIndex] += vecBoxMins.Z * vecDirection.Z;
    }

    /// <summary>collisionutils.cpp:1436-1446 — the two-axis support map
    /// (the edge planes skip one axis, handled by the paired plane).</summary>
    private static void ComputeSupportMap(
        Vector vecDirection, int i1, int i2, Vector vecBoxMins, Vector vecBoxMaxs, float[] pDist)
    {
        int nIndex = vecDirection[i1] > 0.0f ? 1 : 0;
        pDist[nIndex] = vecBoxMaxs[i1] * vecDirection[i1];
        pDist[1 - nIndex] = vecBoxMins[i1] * vecDirection[i1];

        nIndex = vecDirection[i2] > 0.0f ? 1 : 0;
        pDist[nIndex] += vecBoxMaxs[i2] * vecDirection[i2];
        pDist[1 - nIndex] += vecBoxMins[i2] * vecDirection[i2];
    }

    /// <summary>mathlib.h:1832-1846 — <c>MatrixITransformPlane</c>: rotate
    /// the normal by the transpose, the distance rides the squared-norm and
    /// the inverse translation's dot, exactly as stock's two statements.</summary>
    private static (Vector Normal, float Dist) MatrixITransformPlane(
        in BoneMatrix3x4 src, Vector inNormal, float inDist)
    {
        Vector vecTranslation = src.Position;
        Vector vecInvTranslation = BoneSetupMath.VectorIRotate(vecTranslation, src);

        Vector outNormal = BoneSetupMath.VectorIRotate(inNormal, src);
        float outDist = inDist * BoneSetupMath.Dot(outNormal, outNormal);
        outDist -= outNormal.X * vecInvTranslation.X
                 + outNormal.Y * vecInvTranslation.Y
                 + outNormal.Z * vecInvTranslation.Z;
        return (outNormal, outDist);
    }

    // ---- hdr bone record fields stock gates on (mstudiobone_t contents /
    // physicsbone), via the reader's promoted accessors.

    /// <summary><c>mstudiobone_t::contents</c>.</summary>
    private static int ReadBoneContents(StudioHdr hdr, int bone) => hdr.Bone(bone).Contents;

    /// <summary><c>mstudiobone_t::physicsbone</c>.</summary>
    private static int ReadPhysicsBone(StudioHdr hdr, int bone) => hdr.Bone(bone).PhysicsBone;
}
