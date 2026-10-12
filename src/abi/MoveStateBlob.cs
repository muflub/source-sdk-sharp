using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The movement loop's one crossing — <c>SourceSharpMoveState</c>.
///
/// <para>
/// The whole of <c>CMoveData</c> plus the <c>IMovementPlayer</c> fields the loop
/// reads, in and out, once per command. §3.10's "IGameMovement is a shim vtable
/// copying CMoveData across", made literal.
/// </para>
///
/// <para>
/// <b>GENERATED, NOT WRITTEN.</b> This and <c>SourceSharpMoveState</c> in
/// <c>sourcesharp.h</c> are both printed from one field list in
/// <c>tools/3e-gen-movestate</c>. Two hand-written declarations of one layout
/// is the drift the lane brief's second question is about, and
/// <see cref="WordCount"/> is checked against <c>sizeof</c> on both sides.
/// </para>
///
/// <para>
/// The constants travel with it — the tail is sv_friction and friends plus the
/// gamerules hull. 46 more words a command, deliberately: every one is a convar
/// an operator can move mid-round, and a cached copy is a staleness class the
/// movement code cannot detect.
/// </para>
///
/// <para>
/// <c>int</c> rather than <c>bool</c> on every flag: this crosses to C as
/// <c>int32_t</c>, and C#'s <c>bool</c> is one byte.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MoveStateBlob
{
    /// <summary>How many 32-bit words the layout is, checked on both sides.</summary>
    public const int WordCount = 147;

    public int CommandNumber;
    public float FrameTime;
    public int MoveType;
    public int Flags;
    public int WaterLevel;
    public int WaterType;
    public int DeadFlag;
    public float OriginX, OriginY, OriginZ;
    public float VelocityX, VelocityY, VelocityZ;
    public float BaseVelocityX, BaseVelocityY, BaseVelocityZ;
    public float ViewAnglesX, ViewAnglesY, ViewAnglesZ;
    public float ForwardMove;
    public float SideMove;
    public float UpMove;
    public int Buttons;
    public int OldButtons;
    public float MaxSpeed;
    public float ClientMaxSpeed;
    public int Ducked;
    public int Ducking;
    public int InDuckJump;
    public float DuckTime;
    public float DuckJumpTime;
    public float JumpTime;
    public float FallVelocity;
    public float StepSize;
    public float ViewOffsetX, ViewOffsetY, ViewOffsetZ;
    public float PunchAngleX, PunchAngleY, PunchAngleZ;
    public float PunchAngleVelX, PunchAngleVelY, PunchAngleVelZ;
    public float EntityGravity;
    public float CurrentGravity;
    public float SurfaceFriction;
    public int HasGround;
    public float GroundVelocityX, GroundVelocityY, GroundVelocityZ;
    public float WaterJumpTime;
    public float WaterJumpVelX, WaterJumpVelY, WaterJumpVelZ;
    public float SwimSoundTime;
    public float StepSoundTime;
    public int Health;
    public int GameCodeMovedPlayer;
    public float ConstraintRadius;
    public float ConstraintWidth;
    public float ConstraintSpeedFactor;
    public float ConstraintCenterX, ConstraintCenterY, ConstraintCenterZ;
    public float OldForwardMove;
    public int ImpulseCommand;
    public float LaggedMovementValue;
    public int SurfaceProps;
    public float LadderNormalX, LadderNormalY, LadderNormalZ;
    public float OutStepHeight;
    public float OutWishVelX, OutWishVelY, OutWishVelZ;
    public float OutJumpVelX, OutJumpVelY, OutJumpVelZ;
    public int MoveCollide;
    public int OnLadder;
    public int OldWaterLevel;
    public int IsAlive;
    public int ObserverMode;
    public int InAVehicle;
    public float ModelScale;
    public int EntIndex;
    public int StuckLast;
    public float AnglesX, AnglesY, AnglesZ;
    public float OldAnglesX, OldAnglesY, OldAnglesZ;
    public int FirstRunOfFunctions;
    public float CurTime;
    public int SurfaceDataValid;
    public int AllowAutoMovement;
    public int SlowMovement;
    public int IsServer;
    public float CvFriction;
    public float CvAccelerate;
    public float CvAirAccelerate;
    public float CvStopSpeed;
    public float CvMaxVelocity;
    public float CvMaxSpeed;
    public float CvBounce;
    public float CvRollAngle;
    public float CvRollSpeed;
    public float CvNoClipSpeed;
    public float CvNoClipAccelerate;
    public float CvSpecSpeed;
    public float CvSpecAccelerate;
    public float CvAirSpeedCap;
    public float CvDuckSpeedScale;
    public int CvOptimizations;
    public int CvSpecNoClip;
    public int CvMaxClients;
    public float CvTickInterval;
    public float CvViewX, CvViewY, CvViewZ;
    public float CvHullMinX, CvHullMinY, CvHullMinZ;
    public float CvHullMaxX, CvHullMaxY, CvHullMaxZ;
    public float CvDuckHullMinX, CvDuckHullMinY, CvDuckHullMinZ;
    public float CvDuckHullMaxX, CvDuckHullMaxY, CvDuckHullMaxZ;
    public float CvDuckViewX, CvDuckViewY, CvDuckViewZ;
    public float CvObsHullMinX, CvObsHullMinY, CvObsHullMinZ;
    public float CvObsHullMaxX, CvObsHullMaxY, CvObsHullMaxZ;
    public float CvDeadViewHeightX, CvDeadViewHeightY, CvDeadViewHeightZ;
    public float CvJumpHeight;

    /// <summary>engine->Time(), read only by CheckStuck's throttle.</summary>
    public float EngineTime;
}
