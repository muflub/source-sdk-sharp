using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// What a swept player hull found — <c>SourceSharpMoveTraceResult</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MoveTraceBlob
{
    public float Fraction;
    public float EndPosX, EndPosY, EndPosZ;
    public float PlaneNormalX, PlaneNormalY, PlaneNormalZ;
    public float PlaneDist;
    public int StartSolid;
    public int AllSolid;
    public int SurfaceProps;
    public int SurfaceFlags;
    public int Contents;
    public int HasGround;
    public float GroundVelocityX, GroundVelocityY, GroundVelocityZ;
    public int HasEntity;
    public int HitWorld;
}
