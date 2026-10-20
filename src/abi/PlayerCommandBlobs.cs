using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// <c>SourceSharpPlayerCommandFacts</c>, mirrored byte for byte
/// (plan_server_native_v2 T4g): what <c>PlayerCommandBegin</c> is handed (the
/// ground the managed state stands on) and answers
/// (where the player is, the engine-read CPlayerState's angle words, the
/// death flag, health, model scale, index, and the ground's velocity). The
/// asserts beside SourceSharp_PlayerCommandBegin pin the offsets.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlayerCommandFacts
{
    public const int SizeInBytes = 80;

    public uint StructSize;

    /// <summary>In: the ground handle the managed state holds, or the invalid one.</summary>
    public uint Ground;

    /// <summary>Out: <c>GetAbsOrigin()</c>.</summary>
    public Vector Origin;

    /// <summary>Out: <c>pl.v_angle</c>, <c>pl.anglechange</c>, <c>pl.fixangle</c>, <c>pl.deadflag</c>.</summary>
    public Vector VAngle;
    public Vector AngleChange;
    public int FixAngle;
    public int DeadFlag;

    public int Health;
    public float ModelScale;
    public int EntIndex;

    /// <summary>Out: <see cref="Ground"/> names a live entity, and that entity's velocity.</summary>
    public int GroundResolves;
    public Vector GroundVelocity;
}

/// <summary>
/// <c>SourceSharpPlayerCommandResult</c>, mirrored byte for byte
/// (plan_server_native_v2 T4g): the move's result <c>PlayerCommandFinish</c>
/// carries out (the origin, the body's yaw, the triggers touched there) and
/// the words native code still reads off the player between commands.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlayerCommandResult
{
    public const int SizeInBytes = 76;

    public uint StructSize;
    public int Ran;
    public Vector Origin;
    public float Yaw;
    public int Flags;
    public int MoveType;
    public Vector ViewOffset;
    public Vector Velocity;
    public Vector BaseVelocity;
    public uint Ground;
    public float FallVelocity;
}
