//========= SourceSharp — a hitbox trace, read and written flat ==============//
//
// Both sides' bone interfaces (IManagedBones, IManagedBonesClient) carry the
// engine's Ray_t and trace_t as BoneTraceFlat's arrays; this reads them into
// the bone port's own types and writes the answer back. Moved here from the
// server's ServerSkeletons.cs when the client's skeleton came across.
//=============================================================================//

using System;
using SourceSharp.Abi;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// The engine's Ray_t and trace_t as IManagedBones and IManagedBonesClient carry them flat
/// (<see cref="BoneTraceFlat"/>), read into the bone port's own types and
/// written back.
/// </summary>
public static class BoneTraceCodec
{
    private static NVector Vec(ReadOnlySpan<float> f, int at) => new(f[at], f[at + 1], f[at + 2]);

    private static void Put(Span<float> f, int at, NVector v)
    {
        f[at] = v.X;
        f[at + 1] = v.Y;
        f[at + 2] = v.Z;
    }

    /// <summary>The ray, read.</summary>
    public static BoneRay ReadRay(ReadOnlySpan<float> ray) => new()
    {
        Start = Vec(ray, BoneTraceFlat.RayStartX),
        Delta = Vec(ray, BoneTraceFlat.RayDeltaX),
        StartOffset = Vec(ray, BoneTraceFlat.RayStartOffsetX),
        Extents = Vec(ray, BoneTraceFlat.RayExtentsX),
        IsRay = ray[BoneTraceFlat.RayIsRay] != 0f,
        IsSwept = ray[BoneTraceFlat.RayIsSwept] != 0f,
    };

    /// <summary>The trace as the engine handed it in. The surface name stays
    /// native's: only "the hit named it **studio**" comes back.</summary>
    public static BoneTrace ReadTrace(ReadOnlySpan<float> floats, ReadOnlySpan<int> ints) => new()
    {
        StartPos = Vec(floats, BoneTraceFlat.TraceStartX),
        EndPos = Vec(floats, BoneTraceFlat.TraceEndX),
        PlaneNormal = Vec(floats, BoneTraceFlat.TracePlaneNormalX),
        PlaneDist = floats[BoneTraceFlat.TracePlaneDist],
        Fraction = floats[BoneTraceFlat.TraceFraction],
        PlaneType = ints[BoneTraceFlat.TracePlaneType],
        Contents = ints[BoneTraceFlat.TraceContents],
        AllSolid = ints[BoneTraceFlat.TraceAllSolid] != 0,
        StartSolid = ints[BoneTraceFlat.TraceStartSolid] != 0,
        HitGroup = ints[BoneTraceFlat.TraceHitGroup],
        HitBox = ints[BoneTraceFlat.TraceHitBox],
        PhysicsBone = ints[BoneTraceFlat.TracePhysicsBone],
        SurfaceFlags = ints[BoneTraceFlat.TraceSurfaceFlags],
        SurfaceProps = ints[BoneTraceFlat.TraceSurfaceProps],
        SurfaceName = null,
    };

    /// <summary>The trace, written back for native to copy into the engine's.</summary>
    public static void WriteTrace(BoneTrace tr, Span<float> floats, Span<int> ints)
    {
        Put(floats, BoneTraceFlat.TraceStartX, tr.StartPos);
        Put(floats, BoneTraceFlat.TraceEndX, tr.EndPos);
        Put(floats, BoneTraceFlat.TracePlaneNormalX, tr.PlaneNormal);
        floats[BoneTraceFlat.TracePlaneDist] = tr.PlaneDist;
        floats[BoneTraceFlat.TraceFraction] = tr.Fraction;

        ints[BoneTraceFlat.TracePlaneType] = tr.PlaneType;
        ints[BoneTraceFlat.TraceContents] = tr.Contents;
        ints[BoneTraceFlat.TraceAllSolid] = tr.AllSolid ? 1 : 0;
        ints[BoneTraceFlat.TraceStartSolid] = tr.StartSolid ? 1 : 0;
        ints[BoneTraceFlat.TraceHitGroup] = tr.HitGroup;
        ints[BoneTraceFlat.TraceHitBox] = tr.HitBox;
        ints[BoneTraceFlat.TracePhysicsBone] = tr.PhysicsBone;
        ints[BoneTraceFlat.TraceSurfaceFlags] = tr.SurfaceFlags;
        ints[BoneTraceFlat.TraceSurfaceProps] = tr.SurfaceProps;
        ints[BoneTraceFlat.TraceSurfaceIsStudio] = tr.SurfaceName == BoneSetupTrace.StudioSurfaceName ? 1 : 0;
    }
}
