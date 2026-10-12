namespace SourceSharp.Abi;

/// <summary>
/// The flat ray and trace IManagedBones::TestHitboxes and IManagedBonesClient::TestHitboxes carry: the indices of
/// src/shared/native/standalone_bone_trace.h's enums, which BoneTraceFlatTests
/// reads to hold the two equal. Both sides' bone interfaces carry it
/// (IManagedBones, IManagedBonesClient).
/// </summary>
public static class BoneTraceFlat
{
    public const int RayStartX = 0;
    public const int RayDeltaX = 3;
    public const int RayStartOffsetX = 6;
    public const int RayExtentsX = 9;
    public const int RayIsRay = 12;
    public const int RayIsSwept = 13;
    public const int RayFloats = 14;

    public const int TraceStartX = 0;
    public const int TraceEndX = 3;
    public const int TracePlaneNormalX = 6;
    public const int TracePlaneDist = 9;
    public const int TraceFraction = 10;
    public const int TraceFloats = 11;

    public const int TracePlaneType = 0;
    public const int TraceContents = 1;
    public const int TraceAllSolid = 2;
    public const int TraceStartSolid = 3;
    public const int TraceHitGroup = 4;
    public const int TraceHitBox = 5;
    public const int TracePhysicsBone = 6;
    public const int TraceSurfaceFlags = 7;
    public const int TraceSurfaceProps = 8;
    public const int TraceSurfaceIsStudio = 9;
    public const int TraceInts = 10;
}
