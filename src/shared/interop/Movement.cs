//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================
// plan_entity.md step 3e — CGameMovement::ProcessMovement, IN MANAGED CODE.
//
// Everything above this line is Phase 6's parameter blocks: C# said how high a
// jump reaches and the native loop read it out of a struct. Everything below is
// the loop itself.
//
// WHY THE WHOLE LOOP AND NOT TEN MORE LEAVES. MoveMath already holds the ten
// CGameMovement methods whose bodies read nothing but their arguments, and that
// selection rule is exactly what stops at the walk path: WalkMove, Duck,
// CategorizePosition and TryPlayerMove all TRACE, so none of them could come
// over until the trace itself could cross. §3.10 answers that with a trace ABI
// and an IGameMovement shim that copies CMoveData across once per command, and
// this file is the other half of it.
//
// The argument for doing it at all is §7's "Prediction parity": stock compiles
// this arithmetic TWICE, into client.so and server.so, the two must agree
// bit-for-bit or the player rubber-bands, and the jump-misprediction class the
// tree already records is what a disagreement between two copies looks like
// from outside. One copy called by both sides removes the class rather than
// testing for it — and, like MoveMath, it lives in SourceSharp.Abi because that
// is the assembly GameLoadContext resolves to the DEFAULT context, so there is
// ONE of it for both sides and across every hot reload.
//
// EVERY QUIRK IS REPRODUCED, NOT FIXED, and named at its site. A quirk "fixed"
// here is a misprediction invented here, because the fixture it is measured
// against is the C++ this replaces.
//=============================================================================

/// <summary>
/// The engine's own float arithmetic, where it is NOT what a C# programmer
/// would write.
///
/// <para>
/// This exists because the acceptance for this step is a BIT-EXACT replay
/// against a recording of the native loop, and three pieces of Source's
/// mathlib do not agree with the obvious managed spelling:
/// </para>
///
/// <list type="bullet">
/// <item><description>
/// <c>VectorNormalize</c> is not <c>v / v.Length</c>. On every Intel build it
/// is <c>_mm_rsqrt_ss</c> — a HARDWARE APPROXIMATION — plus one Newton-Raphson
/// step, over a length squared with <c>1e-10f</c> added
/// (<c>public/mathlib/vector.h:2239</c>). It is accurate to about 22 bits, not
/// 24, and the difference reaches the player's position through every
/// <c>wishdir</c> the loop computes.
/// </description></item>
/// <item><description>
/// <c>DEG2RAD</c> is <c>x * (float)(M_PI_F / 180.f)</c> — the conversion factor
/// is rounded to a float ONCE, at compile time. <c>x * MathF.PI / 180f</c> is a
/// different number.
/// </description></item>
/// <item><description>
/// Several movement expressions carry an unsuffixed C literal, which is a
/// DOUBLE, so the whole expression promotes and rounds once at the store.
/// <see cref="MoveMath.Gravity"/> already records two of them.
/// </description></item>
/// </list>
/// </summary>
public static class MoveVec
{
    /// <summary>
    /// <c>DEG2RAD</c>: mathlib.h:302 rounds <c>M_PI/180</c> to a float first,
    /// and multiplies by that. Written as one constant so it cannot be
    /// "simplified" back into a multiply-then-divide, which is a different
    /// number in the last bit.
    /// </summary>
    public const float DegToRad = (float)(System.Math.PI / 180.0);

    /// <summary><c>Vector::Length</c> — <c>sqrtf</c> of the squared sum.</summary>
    public static float Length(in Vector v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    /// <summary><c>Vector::Length2D</c>.</summary>
    public static float Length2D(in Vector v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>
    /// <c>_SSE_RSqrtInline</c>: <c>rsqrtss</c> then one Newton-Raphson step,
    /// exactly as <c>public/mathlib/vector.h:2225</c> writes it.
    ///
    /// <para>
    /// The hardware instruction where it is available, because that is what the
    /// engine executes and an accurate reciprocal square root here is a
    /// DIFFERENT ANSWER — one that would show up as a misprediction on the very
    /// first frame a player turns. The managed fallback is only for a machine
    /// with no SSE, where nothing this compares against runs either.
    /// </para>
    /// </summary>
    public static float RSqrt(float a)
    {
        float xr = System.Runtime.Intrinsics.X86.Sse.IsSupported
            ? System.Runtime.Intrinsics.Vector128.ToScalar(
                  System.Runtime.Intrinsics.X86.Sse.ReciprocalSqrtScalar(
                      System.Runtime.Intrinsics.Vector128.CreateScalarUnsafe(a)))
            : 1f / MathF.Sqrt(a);

        // xt = 3 - xr*xr*a ; xt *= 0.5 ; xr *= xt
        float xt = xr * xr;
        xt = xt * a;
        xt = 3f - xt;
        xt = xt * 0.5f;
        return xr * xt;
    }

    /// <summary>
    /// <c>VectorNormalize</c> — vector.h:2239, the Intel arm.
    ///
    /// <para>
    /// Returns stock's return value, which is <c>sqrlen * invlen</c> and NOT
    /// the length: it is the length as reconstructed from the approximation,
    /// and the loop compares it against <c>mv-&gt;m_flMaxSpeed</c>. A zero
    /// vector normalises to zero here rather than to (0,0,1), because the
    /// <c>1e-10f</c> keeps the reciprocal finite and stock's (0,0,1) arm is in
    /// the commented-out version this build does not compile.
    /// </para>
    /// </summary>
    public static float Normalize(ref Vector v)
    {
        float sqrlen = v.X * v.X + v.Y * v.Y + v.Z * v.Z + 1.0e-10f;
        float invlen = RSqrt(sqrlen);
        v.X *= invlen;
        v.Y *= invlen;
        v.Z *= invlen;
        return sqrlen * invlen;
    }

    /// <summary>
    /// <c>AngleVectors</c> — mathlib_base.cpp:919, spelled term for term.
    ///
    /// <para>
    /// NOT <see cref="Angles.Vectors"/>, and the difference is not style:
    /// that one computes <c>x * MathF.PI / 180f</c>, which rounds twice where
    /// <c>DEG2RAD</c> rounds once. Two spellings of one conversion is exactly
    /// the drift this port exists to remove, so this one names the engine's.
    /// </para>
    /// </summary>
    public static void AngleVectors(in Vector angles, out Vector forward, out Vector right, out Vector up)
    {
        float sp = MathF.Sin(angles.X * DegToRad), cp = MathF.Cos(angles.X * DegToRad);
        float sy = MathF.Sin(angles.Y * DegToRad), cy = MathF.Cos(angles.Y * DegToRad);
        float sr = MathF.Sin(angles.Z * DegToRad), cr = MathF.Cos(angles.Z * DegToRad);

        forward = new Vector(cp * cy, cp * sy, -sp);
        right = new Vector(
            -1 * sr * sp * cy + -1 * cr * -sy,
            -1 * sr * sp * sy + -1 * cr * cy,
            -1 * sr * cp);
        up = new Vector(
            cr * sp * cy + -sr * -sy,
            cr * sp * sy + -sr * cy,
            cr * cp);
    }

    /// <summary>Just the facing, which is <c>AngleVectors</c>'s one-output overload.</summary>
    public static Vector AngleForward(in Vector angles)
    {
        float sp = MathF.Sin(angles.X * DegToRad), cp = MathF.Cos(angles.X * DegToRad);
        float sy = MathF.Sin(angles.Y * DegToRad), cy = MathF.Cos(angles.Y * DegToRad);
        return new Vector(cp * cy, cp * sy, -sp);
    }

    /// <summary><c>SimpleSpline</c> — mathlib.h's ease in/out, used by the duck transition.</summary>
    public static float SimpleSpline(float value)
    {
        float valueSquared = value * value;
        return 3 * valueSquared - 2 * valueSquared * value;
    }

    /// <summary>Read component <paramref name="i"/> of a vector, as <c>v[i]</c> does.</summary>
    public static float At(in Vector v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    /// <summary>Write component <paramref name="i"/> of a vector.</summary>
    public static void SetAt(ref Vector v, int i, float value)
    {
        if (i == 0) v.X = value;
        else if (i == 1) v.Y = value;
        else v.Z = value;
    }
}

/// <summary>
/// Everything one <c>ProcessMovement</c> reads and writes — <c>CMoveData</c>,
/// the player fields <c>IMovementPlayer</c> exposes, and the two
/// <c>CGameMovement</c> members that survive between commands.
///
/// <para>
/// ONE STRUCT rather than three, because the native shim copies it across in
/// one crossing per command and the parity fixture records it in one row. The
/// field ORDER here is load-bearing: it is the column order of
/// <c>Fixtures/movement-processmovement-stock.tsv</c>, which was written by a
/// probe inside the native loop, and <c>MovementFixture</c> reads the two
/// against each other by position.
/// </para>
///
/// <para>
/// <see cref="OnLadder"/> and <see cref="OldWaterLevel"/> are
/// <c>CGameMovement</c>'s own members and not the player's. They are here
/// because they are read at the START of a command and written during it, so a
/// loop that did not carry them would answer differently on its second call
/// than the C++ does.
/// </para>
///
/// <para>
/// A class rather than a struct: it is passed by reference through about forty
/// methods, and a struct would need <c>ref</c> on every one of them for no
/// gain — the shim copies it into and out of <c>CMoveData</c> once, and that
/// copy is where the boundary is.
/// </para>
/// </summary>
public sealed class MoveState
{
    public int CommandNumber;
    public float FrameTime;
    public int MoveType;
    public int Flags;
    public int WaterLevel;
    public int WaterType;
    public bool DeadFlag;

    public Vector Origin;
    public Vector Velocity;
    public Vector BaseVelocity;

    /// <summary>The command's view angles. What the loop takes its directions from.</summary>
    public Vector ViewAngles;

    public float ForwardMove, SideMove, UpMove;
    public int Buttons, OldButtons;

    /// <summary>
    /// CHL2_Player's m_bPlayUseDenySound: a +USE found nothing, so the deny
    /// sound is due after the move (PlayerUse). The ladder code clears it when
    /// that press mounted or left a ladder.
    /// </summary>
    public bool PlayUseDenySound;
    public float MaxSpeed, ClientMaxSpeed;

    public bool Ducked, Ducking, InDuckJump;
    public float DuckTime, DuckJumpTime, JumpTime;
    public float FallVelocity, StepSize;

    public Vector ViewOffset;
    public Vector PunchAngle, PunchAngleVel;

    /// <summary>The player's own gravity multiplier; zero means "one" (a stock quirk).</summary>
    public float EntityGravity;

    /// <summary><c>GetCurrentGravity()</c>, which is sv_gravity and can move at runtime.</summary>
    public float CurrentGravity;

    public float SurfaceFriction;

    /// <summary>Whether there is a ground entity at all.</summary>
    public bool HasGround;

    /// <summary>How fast the ground is moving. Zero when there is none.</summary>
    public Vector GroundVelocity;

    public float WaterJumpTime;
    public Vector WaterJumpVel;
    public float SwimSoundTime, StepSoundTime;
    public int Health;
    public bool GameCodeMovedPlayer;

    public float ConstraintRadius, ConstraintWidth, ConstraintSpeedFactor;
    public Vector ConstraintCenter;

    public float OldForwardMove;
    public int ImpulseCommand;
    public float LaggedMovementValue;
    public int SurfaceProps;
    public Vector LadderNormal;

    public float OutStepHeight;
    public Vector OutWishVel, OutJumpVel;

    public int MoveCollide;

    /// <summary><c>CGameMovement::m_nOnLadder</c>, carried between commands.</summary>
    public int OnLadder;

    /// <summary><c>CGameMovement::m_nOldWaterLevel</c>, carried between commands.</summary>
    public int OldWaterLevel;

    /// <summary>The ladder words' "no ladder": entity index 0 is the world, so -1.</summary>
    public const int NoLadder = -1;

    /// <summary>
    /// HL2's <c>m_HL2Local.m_hLadder</c>: the useable ladder the player is on,
    /// by entity index, or <see cref="NoLadder"/>. Networked and predicted.
    /// </summary>
    public int Ladder = NoLadder;

    /// <summary><c>LadderMove_t::m_bForceLadderMove</c>: mid mount or dismount.</summary>
    public bool LadderForceMove;

    /// <summary><c>LadderMove_t::m_bForceMount</c>: that forced move gets on rather than off.</summary>
    public bool LadderForceMount;

    // ---- HL2MP's player (m_HL2Local), for GameMovement.Hl2Player ----------

    /// <summary><c>m_flSuitPower</c>: 0 to 100.</summary>
    public float SuitPower;

    /// <summary><c>m_flSuitPowerLoad</c>: the drain of the devices on.</summary>
    public float SuitPowerLoad;

    /// <summary><c>m_bitsActiveDevices</c>.</summary>
    public int SuitDevices;

    /// <summary><c>m_flTimeAllSuitDevicesOff</c>: when the last device went off.</summary>
    public float SuitDevicesOffTime;

    /// <summary><c>m_bNewSprinting</c>.</summary>
    public bool NewSprinting;

    /// <summary><c>IsSuitEquipped()</c>: every HL2MP player spawns with it.</summary>
    public bool SuitEquipped;

    /// <summary>
    /// <c>!m_bSprintEnabled</c>: a +USE carry stops the sprint (the server's
    /// CanSprint reads it; stock does not network it).
    /// </summary>
    public bool SprintDisabled;

    /// <summary><c>LadderMove_t::m_flStartTime</c> / <c>m_flArrivalTime</c>.</summary>
    public float LadderStartTime, LadderArrivalTime;

    /// <summary><c>LadderMove_t::m_vecGoalPosition</c> / <c>m_vecStartPosition</c>.</summary>
    public Vector LadderGoalPosition, LadderStartPosition;

    /// <summary><c>LadderMove_t::m_hForceLadder</c>: the ladder a forced mount ends on.</summary>
    public int LadderForceLadder = NoLadder;

    public bool IsAlive;
    public int ObserverMode;

    /// <summary>
    /// The observed target's pose, for in-eye, chase and POI (stock's
    /// GetObserverTarget() in FullObserverMove). Not on the native blob: the
    /// runner fills it from the side's own knowledge
    /// (<see cref="MovementRunner.ObserverTargetPose"/>).
    /// </summary>
    public bool HasObserverTarget;
    public Vector ObserverTargetOrigin;
    public Vector ObserverTargetAngles;
    public Vector ObserverTargetVelocity;
    public bool InAVehicle;
    public float ModelScale;
    public int EntIndex;
    public int StuckLast;

    /// <summary>The angles the loop WRITES, which CheckParameters computes from the command's.</summary>
    public Vector Angles;

    public Vector OldAngles;
    public bool FirstRunOfFunctions;
    public float CurTime;

    /// <summary>Whether <c>MvSurfaceData()</c> is a pointer and not NULL.</summary>
    public bool SurfaceDataValid;

    public bool AllowAutoMovement;
    public bool SlowMovement;

    /// <summary>
    /// Which side is running this command.
    ///
    /// <para>
    /// NOT a fixture column: the recording is server-side, so the reader sets
    /// it explicitly and says so. It gates two things in stock — CheckStuck's
    /// world-nudge loop, which is client-only, and Duck's
    /// <c>IsFirstTimePredicted</c>, which has no meaning on a server that never
    /// reruns a command.
    /// </para>
    /// </summary>
    public bool IsServer = true;

    public MoveState Clone() => (MoveState)MemberwiseClone();
}

/// <summary>
/// The three managed DECISIONS over how movement feels, as one immutable value
/// a run hands its loop (statics to zero): how much of a jump the player gets
/// (1 is the ordinary jump), the friction the loop computed, scaled, and the
/// speed it arrived at, scaled. <see cref="Stock"/> is the identity, so a loop
/// nobody hands rules to moves exactly like Half-Life.
/// </summary>
public readonly record struct MovementRules(
    Func<JumpRequest, float> Jump,
    Func<int, float, float, bool, int, float> Friction,
    Func<int, float, bool, int, float> MaxSpeed)
{
    /// <summary>Stock: no scale on anything.</summary>
    public static readonly MovementRules Stock = new(
        static _ => 1f,
        static (_, friction, _, _, _) => friction,
        static (_, maxSpeed, _, _) => maxSpeed);
}

/// <summary>
/// <c>CGameMovement::ProcessMovement</c>, in managed code.
///
/// <para>
/// One instance per side, reused across commands: the two fields that survive a
/// command (<c>m_nOldWaterLevel</c>, <c>m_nOnLadder</c>) live in
/// <see cref="MoveState"/> so the shim carries them, and everything else here
/// is scratch for one call. It is NOT static, unlike <see cref="MoveMath"/>,
/// because it holds an <see cref="IMoveWorld"/> — and the server's world and
/// the client's are different objects even though the arithmetic between them
/// is the same one copy.
/// </para>
///
/// <para>
/// <b>EVERY MOVE TYPE IS ANSWERED HERE (step 8f-movetypes).</b> Step 3e
/// declared <c>MOVETYPE_OBSERVER</c>, <c>MOVETYPE_FLY</c> and
/// <c>MOVETYPE_FLYGRAVITY</c> refusals, so <see cref="ProcessMovement"/>
/// answered <c>false</c> and the native shim ran stock's body for those
/// commands. An OWNER RULING removed that refusal, because
/// <c>gamemovement.cpp</c> is leaving the build and the stock body those three
/// fell through to is leaving with it: a refusal whose fallback has been
/// deleted is a silent loss, not a refusal.
/// <see cref="FullObserverMove"/>, <see cref="FullTossMove"/> and
/// <see cref="PerformFlyCollisionResolution"/> are the ports, each written
/// against the C++ it replaces and cited by line.
/// </para>
///
/// <para>
/// <b>What that port is and is not certified by.</b> The three arms have NO
/// rows in the parity fixture, and cannot: they are unreachable from a live
/// <c>dm_lockdown</c> player through the shim's own death path
/// (<c>CStandalonePlayer::SetObserver</c> maps observer onto
/// <c>MOVETYPE_NOCLIP</c>, and nothing in this build ever sets either flying
/// type on a player). So they are certified three ways instead, and the limit
/// is stated rather than papered over: hermetic facts per arm against the C++,
/// a LIVE run driving a real player through each of the three at
/// <c>tools/8f-movetype-gate</c>, and the fact that the SAME C# runs on both
/// sides — which is what makes the prediction question structural here rather
/// than arithmetic.
/// </para>
///
/// <para>
/// <b>The target arm of <c>FullObserverMove</c></b> (<c>OBS_MODE_IN_EYE</c> /
/// <c>CHASE</c> / <c>POI</c>) copies the observed target's origin, angles and
/// velocity. It was unreachable while <c>MvGetObserverTarget()</c> answered
/// <c>NULL</c>; the managed observer (lane player-msgs) supplies the target
/// through <see cref="MovementRunner.ObserverTargetPose"/>, and
/// <see cref="ObserverTargetAsks"/> still counts the commands that reach it.
/// </para>
/// </summary>
public sealed partial class GameMovement
{
    // gamemovement.cpp's own constants, at the values this build compiles.
    public const float GameMovementDuckTime = 1000.0f;
    public const float GameMovementJumpTime = 510.0f;
    public const float TimeToDuck = 0.4f;
    public const float TimeToUnduck = 0.2f;
    public const float GameMovementTimeToUnduck = TimeToUnduck * 1000.0f;
    public const float GameMovementTimeToUnduckInv = GameMovementDuckTime - GameMovementTimeToUnduck;

    // shareddefs.h:405-413, the HL2 arm, which stock HL2MP compiles (its
    // projects define HL2_DLL and HL2_CLIENT_DLL; the fork's own build defines
    // neither, and this loop followed the fork until 2026-10-26). It sets the
    // fall thresholds, and CheckJumpButton's jump speed and forward bonus.
    // PlayerMaxSafeFallSpeed IS MoveMath's, not a second copy. The lane that
    // moved the fall-damage amount out of C++ found three copies of this number
    // reading two different fall velocities; this alias is what keeps the
    // arming threshold and the damage slope the same number for ever.
    public const float PlayerMaxSafeFallSpeed = MoveMath.PlayerMaxSafeFallSpeed;
    public const float PlayerLandOnFloatingObject = 173f;
    public const float PlayerMinBounceSpeed = 173f;
    public const float PlayerFallPunchThreshold = 303f;

    // The non-HL2 arm's (shareddefs.h:415-419), for MoveConstants.NoHl2Arms.
    public const float NoHl2MaxSafeFallSpeed = 580f;
    public const float NoHl2LandOnFloatingObject = 200f;
    public const float NoHl2MinBounceSpeed = 200f;
    public const float NoHl2FallPunchThreshold = 350f;

    // The arm this loop runs (MoveConstants.NoHl2Arms).
    private float MaxSafeFallSpeed => _c.NoHl2Arms ? NoHl2MaxSafeFallSpeed : PlayerMaxSafeFallSpeed;
    private float MinBounceSpeed => _c.NoHl2Arms ? NoHl2MinBounceSpeed : PlayerMinBounceSpeed;
    private float FallPunchThreshold => _c.NoHl2Arms ? NoHl2FallPunchThreshold : PlayerFallPunchThreshold;

    /// <summary>
    /// CheckJumpButton's optimised jump speed, the HL2 arm's literal: "approx.
    /// 21 units" at gravity 600 (the other arm's 268.33 is gravity 800's).
    /// </summary>
    public const float Hl2JumpSpeed = 160.0f;

    /// <summary><c>GAMEMOVEMENT_JUMP_HEIGHT</c>: the height <see cref="Hl2JumpSpeed"/> stands for.</summary>
    public const float StockJumpHeight = 21.0f;

    public const float StopEpsilon = 0.1f;
    public const int MaxClipPlanes = 5;
    public const float DistEpsilon = 0.03125f;
    public const float CoordResolution = 1.0f / 32.0f;
    public const float NonJumpVelocity = 140.0f;
    public const int WaterJumpHeight = 8;
    public const float MaxClimbSpeed = 200f;
    public const float CheckStuckMinTime = 0.05f;

    // The four CheckInterval periods, in SECONDS. gamemovement.cpp:144-154
    // divides each by TICK_INTERVAL and truncates, so the tick counts depend on
    // the server's tick rate and are computed rather than written down.
    public const float CategorizeGroundSurfaceInterval = 0.3f;
    public const float CheckStuckInterval = 1.0f;
    public const float CheckStuckIntervalSp = 0.2f;
    public const float CheckLadderInterval = 0.2f;

    // const.h / bspflags.h / in_buttons.h.
    public const int FlOnGround = 1 << 0;
    public const int FlDucking = 1 << 1;
    public const int FlWaterJump = 1 << 3;
    public const int FlOnTrain = 1 << 4;
    public const int FlFrozen = 1 << 6;

    public const int InJump = 1 << 1;
    public const int InDuck = 1 << 2;
    public const int InSpeed = 1 << 17;
    public const int InUse = 1 << 5;

    public const int MoveTypeNone = 0;
    public const int MoveTypeIsometric = 1;
    public const int MoveTypeWalk = 2;
    public const int MoveTypeFly = 4;
    public const int MoveTypeFlyGravity = 5;
    public const int MoveTypeNoClip = 8;
    public const int MoveTypeLadder = 9;
    public const int MoveTypeObserver = 10;

    // const.h:216-221. Only the three PerformFlyCollisionResolution names a
    // case for are here; MOVECOLLIDE_FLY_SLIDE falls into its `default:`, which
    // is stock's Assert(0) and does nothing, and is written out below as a case
    // so the "does nothing" is a decision on the page rather than a hole.
    public const int MoveCollideDefault = 0;
    public const int MoveCollideFlyBounce = 1;
    public const int MoveCollideFlyCustom = 2;
    public const int MoveCollideFlySlide = 3;

    // shareddefs.h:493-500. FullObserverMove switches on these.
    public const int ObsModeNone = 0;
    public const int ObsModeDeathCam = 1;
    public const int ObsModeFreezeCam = 2;
    public const int ObsModeFixed = 3;
    public const int ObsModeInEye = 4;
    public const int ObsModeChase = 5;
    public const int ObsModePoi = 6;
    public const int ObsModeRoaming = 7;

    public const int WlNotInWater = 0;
    public const int WlFeet = 1;
    public const int WlWaist = 2;
    public const int WlEyes = 3;

    public const int ContentsEmpty = 0;
    public const int ContentsSolid = 0x1;
    public const int ContentsWindow = 0x2;
    public const int ContentsGrate = 0x8;
    public const int ContentsSlime = 0x10;
    public const int ContentsWater = 0x20;
    public const int ContentsMoveable = 0x4000;
    public const int ContentsPlayerClip = 0x10000;
    public const int ContentsCurrent0 = 0x40000;
    public const int ContentsCurrent90 = 0x80000;
    public const int ContentsCurrent180 = 0x100000;
    public const int ContentsCurrent270 = 0x200000;
    public const int ContentsCurrentUp = 0x400000;
    public const int ContentsCurrentDown = 0x800000;
    public const int ContentsMonster = 0x2000000;
    public const int ContentsLadder = 0x20000000;

    public const uint MaskPlayerSolid =
        ContentsSolid | ContentsMoveable | ContentsPlayerClip |
        ContentsWindow | ContentsMonster | ContentsGrate;

    public const uint MaskPlayerSolidBrushOnly =
        ContentsSolid | ContentsMoveable | ContentsWindow | ContentsPlayerClip | ContentsGrate;

    public const uint MaskWater = ContentsWater | ContentsMoveable | ContentsSlime;

    public const uint MaskCurrent =
        ContentsCurrent0 | ContentsCurrent90 | ContentsCurrent180 |
        ContentsCurrent270 | ContentsCurrentUp | ContentsCurrentDown;

    public const int CollisionGroupPlayerMovement = 8;

    /// <summary><c>PLAYER_WALK</c> / <c>PLAYER_JUMP</c>, the two the loop sets.</summary>
    public const int PlayerWalkAnimation = 0;
    public const int PlayerJumpAnimation = 2;

    /// <summary><c>RUMBLE_FALL_LONG</c> / <c>RUMBLE_FALL_SHORT</c>.</summary>
    public const byte RumbleFallLong = 30;
    public const byte RumbleFallShort = 31;

    /// <summary><c>SPEED_CROPPED_DUCK</c>.</summary>
    private const int SpeedCroppedReset = 0;
    private const int SpeedCroppedDuck = 1;

    private readonly IMoveWorld _w;
    private MoveConstants _c;
    private MoveState _s = null!;

    private Vector _forward, _right, _up;
    private int _speedCropped;

    /// <summary>
    /// <c>MvSurfaceData()</c>, which is a POINTER in stock and therefore has a
    /// meaningful null. <see cref="MoveState.SurfaceDataValid"/> is that null,
    /// and this is what it points at.
    /// </summary>
    private MoveSurface _surfaceData;

    /// <summary>
    /// <c>MvPreviousTextureType()</c> — server-only, and the only thing the
    /// surface-trigger notification reads.
    /// </summary>
    private int _previousTextureType;

    /// <summary><c>m_flStuckCheckTime[player][server?0:1]</c>.</summary>
    private readonly float[] _stuckCheckTime = new float[2];

    /// <summary>Whether the last <see cref="ProcessMovement"/> ran at all.</summary>
    public bool LastCommandRan { get; private set; }

    /// <summary>
    /// How many commands hit <see cref="PlayerMove"/>'s <c>default:</c> arm —
    /// stock's <c>"Bogus pmove player movetype %i"</c> DevMsg. Zero for every
    /// value <c>MoveType_t</c> actually has, so a non-zero reading means the
    /// crossing carried a move type the enum does not name. Step 8f-movetypes:
    /// before it, this arm was where the three refused types would have landed
    /// had they got past the refusal, so it read as "unreachable" and could
    /// hide a fourth.
    /// </summary>
    public long BogusMoveTypes { get; private set; }

    /// <summary>
    /// How many commands reached <see cref="FullObserverMove"/>'s
    /// target-following arm (<c>OBS_MODE_IN_EYE</c> / <c>CHASE</c> /
    /// <c>POI</c>). The arm is UNREACHABLE-ARMED: the only
    /// <c>MvGetObserverTarget</c> this build links returns <c>NULL</c>
    /// unconditionally, so stock's body for it was already dead and there is
    /// nothing to copy from the target. Counted rather than asserted so that
    /// "nothing spectates a target in this build" is a reading and not a
    /// belief — the moment something does, this is non-zero and the arm needs
    /// a target on the crossing.
    /// </summary>
    public long ObserverTargetAsks { get; private set; }

    public GameMovement(IMoveWorld world, MoveConstants constants)
    {
        _w = world ?? throw new ArgumentNullException(nameof(world));
        _c = constants;
    }

    /// <summary>The constants, which the shim pushes when a convar moves.</summary>
    public MoveConstants Constants
    {
        get => _c;
        set => _c = value;
    }

    /// <summary>
    /// <c>rgv3tStuckTable</c> — 54 nudges, built once.
    ///
    /// <para>
    /// Despite <c>GetRandomStuckOffsets</c>'s name there is NO randomness in it:
    /// the table is a fixed sweep and the index is a counter on the player. So
    /// this is a static readonly array rather than something needing a seed, and
    /// a replay of a stuck player is reproducible.
    /// </para>
    /// </summary>
    private static readonly ImmutableArray<Vector> StuckTable = [.. BuildStuckTable()];

    private static Vector[] BuildStuckTable()
    {
        var t = new Vector[54];
        int idx = 0;

        for (float z = -0.125f; z <= 0.125f; z += 0.125f)
            t[idx++] = new Vector(0f, 0f, z);
        for (float y = -0.125f; y <= 0.125f; y += 0.125f)
            t[idx++] = new Vector(0f, y, 0f);
        for (float x = -0.125f; x <= 0.125f; x += 0.125f)
            t[idx++] = new Vector(x, 0f, 0f);

        for (float x = -0.125f; x <= 0.125f; x += 0.250f)
            for (float y = -0.125f; y <= 0.125f; y += 0.250f)
                for (float z = -0.125f; z <= 0.125f; z += 0.250f)
                    t[idx++] = new Vector(x, y, z);

        // Big moves. Three Z nudges, then Y, then X, then the 3x3x3 of them --
        // and the last multi-axis loop OVERRUNS 54 in stock, which asserts in a
        // debug build and writes past the table in a release one. Stock's own
        // Assert says `idx < 54` and idx reaches 63. The port stops at 54,
        // which is what GetRandomStuckOffsets's `idx % 54` can address anyway.
        float[] zi = { 0.0f, 1.0f, 6.0f };
        for (int i = 0; i < 3 && idx < t.Length; i++)
            t[idx++] = new Vector(0f, 0f, zi[i]);

        for (float y = -2.0f; y <= 2.0f && idx < t.Length; y += 2.0f)
            t[idx++] = new Vector(0f, y, 0f);
        for (float x = -2.0f; x <= 2.0f && idx < t.Length; x += 2.0f)
            t[idx++] = new Vector(x, 0f, 0f);

        for (int i = 0; i < 3; i++)
        {
            for (float x = -2.0f; x <= 2.0f; x += 2.0f)
            {
                for (float y = -2.0f; y <= 2.0f; y += 2.0f)
                {
                    if (idx >= t.Length)
                        return t;
                    t[idx++] = new Vector(x, y, zi[i]);
                }
            }
        }

        return t;
    }

    //=========================================================================
    // ProcessMovement and PlayerMove.
    //=========================================================================

    /// <summary>
    /// <c>CGameMovement::ProcessMovement</c> (gamemovement.cpp:1238).
    ///
    /// <para>
    /// Returns whether the managed loop ran. <b>Since step 8f-movetypes it
    /// answers <c>true</c> for EVERY move type</b> — the three that used to be
    /// declared refusals (<c>MOVETYPE_OBSERVER</c>, <c>MOVETYPE_FLY</c>,
    /// <c>MOVETYPE_FLYGRAVITY</c>) are ported and run here like the rest. The
    /// return value is kept, and kept as the shim's <c>false</c> path, because
    /// a null state is still an answer this has to be able to give and because
    /// the counter pair (<see cref="CommandsRun"/> /
    /// <see cref="CommandsDeclined"/>) is what <c>tools/movemath-gate</c> reads
    /// to tell the managed body running from the managed body being asked for.
    /// <see cref="CommandsDeclined"/> is now expected to be ZERO on both sides
    /// for the life of a session, and <c>tools/8f-movetype-gate</c> asserts it.
    /// </para>
    /// </summary>
    public bool ProcessMovement(MoveState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _s = state;
        LastCommandRan = true;

        // "!!HACK HACK: Adrian - slow down all player movement by this factor."
        // The scaling is stock's and it is applied to gpGlobals->frametime,
        // which every method below then reads; here the state carries the
        // frame time, so the scale lands on it once and is restored at the end
        // exactly as stock restores the global.
        float storeFrameTime = _s.FrameTime;
        _s.FrameTime *= _s.LaggedMovementValue;

        // ResetGetPointContentsCache() is the world's, not this loop's: the
        // cache lives beside the query it saves (MovePointContentsCache).
        _w.ResetPointContentsCache(_c.Optimizations);
        _speedCropped = SpeedCroppedReset;

        _surfaceData = _s.SurfaceDataValid ? _w.Surface(_s.SurfaceProps) : default;

        PlayerMove();
        FinishMove();

        _s.FrameTime = storeFrameTime;
        return true;
    }

    /// <summary><c>FinishMove</c> (gamemovement.cpp:1305).</summary>
    private void FinishMove()
    {
        _s.OldButtons = _s.Buttons;
        _s.OldForwardMove = _s.ForwardMove;
    }

    /// <summary><c>PlayerMove</c> (gamemovement.cpp:5017).</summary>
    private void PlayerMove()
    {
        CheckParameters();

        _s.OutWishVel = Vector.Zero;
        _s.OutJumpVel = Vector.Zero;

        _w.ResetTouchList();

        ReduceTimers();

        MoveVec.AngleVectors(_s.ViewAngles, out _forward, out _right, out _up);

        if (_s.MoveType != MoveTypeNoClip &&
            _s.MoveType != MoveTypeNone &&
            _s.MoveType != MoveTypeIsometric &&
            _s.MoveType != MoveTypeObserver &&
            !_s.DeadFlag)
        {
            if (CheckInterval(IntervalType.Stuck))
            {
                if (CheckStuck() != 0)
                    return;   // Can't move, we're stuck.
            }
        }

        if (_s.MoveType != MoveTypeWalk || _s.GameCodeMovedPlayer || !_c.Optimizations)
        {
            CategorizePosition();
        }
        else
        {
            if (_s.Velocity.Z > 250.0f)
                SetGroundEntity(null);
        }

        _s.OldWaterLevel = _s.WaterLevel;

        if (!_s.HasGround)
            _s.FallVelocity = -_s.Velocity.Z;

        _s.OnLadder = 0;

        _w.UpdateStepSound(_s.Origin, _s.Velocity, _s.SurfaceDataValid ? _s.SurfaceProps : -1);

        UpdateDuckJumpEyeOffset();
        Duck();

        if (!_s.DeadFlag && (_s.Flags & FlOnTrain) == 0)
        {
            // HL2MP's useable ladders when the loop has them, base
            // CGameMovement's brush ladders when it does not.
            bool onLadder = Ladders is null ? LadderMove() : UseableLadderMove();
            if (!onLadder && _s.MoveType == MoveTypeLadder)
            {
                _s.MoveType = MoveTypeWalk;
                _s.MoveCollide = MoveCollideDefault;
            }
        }

        switch (_s.MoveType)
        {
            case MoveTypeNone:
                break;

            case MoveTypeNoClip:
                FullNoClipMove(_c.NoClipSpeed, _c.NoClipAccelerate);
                break;

            case MoveTypeFly:
            case MoveTypeFlyGravity:
                // Both flying types share one body; FullTossMove asks the move
                // type again for the one line that differs (AddGravity).
                FullTossMove();
                break;

            case MoveTypeLadder:
                if (Ladders is null)
                    FullLadderMove();
                else
                    FullUseableLadderMove();
                break;

            case MoveTypeWalk:
            case MoveTypeIsometric:
                // ISOMETRIC falls through to the walk move in stock too, with
                // IsometricMove() commented out beside it.
                FullWalkMove();
                break;

            case MoveTypeObserver:
                FullObserverMove();   // clips against world & players
                break;

            default:
                // Stock's arm is a DevMsg naming the bogus move type. There is
                // no DevMsg on this side of the boundary and inventing a
                // crossing for a diagnostic would be a per-command cost for a
                // case the enum does not have a value for; the count is kept
                // instead, so "the port never saw one" is a number.
                BogusMoveTypes++;
                break;
        }
    }

    //=========================================================================
    // Parameters, timers and the punch spring.
    //=========================================================================

    /// <summary><c>ComputeConstraintSpeedFactor</c> (gamemovement.cpp:1020).</summary>
    private float ComputeConstraintSpeedFactor()
    {
        if (_s.ConstraintRadius == 0.0f)
            return 1.0f;

        Vector d = _s.Origin - _s.ConstraintCenter;
        float distSq = d.X * d.X + d.Y * d.Y + d.Z * d.Z;

        float outerRadiusSq = _s.ConstraintRadius * _s.ConstraintRadius;
        float innerRadiusSq = _s.ConstraintRadius - _s.ConstraintWidth;
        innerRadiusSq *= innerRadiusSq;

        if (distSq <= innerRadiusSq || distSq >= outerRadiusSq)
            return 1.0f;

        var desired = new Vector(
            _forward.X * _s.ForwardMove, _forward.Y * _s.ForwardMove, _forward.Z * _s.ForwardMove);
        desired = new Vector(
            desired.X + _s.SideMove * _right.X,
            desired.Y + _s.SideMove * _right.Y,
            desired.Z + _s.SideMove * _right.Z);
        desired = new Vector(
            desired.X + _s.UpMove * _up.X,
            desired.Y + _s.UpMove * _up.Y,
            desired.Z + _s.UpMove * _up.Z);

        Vector delta = _s.Origin - _s.ConstraintCenter;
        MoveVec.Normalize(ref delta);
        MoveVec.Normalize(ref desired);
        if (delta.Dot(desired) < 0.0f)
            return 1.0f;

        // sqrt() on a float argument in C++ is the DOUBLE sqrt: the argument
        // widens, and the whole expression is evaluated in double before the
        // store. Written in float it differs in the last bit on a slope.
        float frac = (float)((System.Math.Sqrt(distSq) - (_s.ConstraintRadius - _s.ConstraintWidth))
                             / _s.ConstraintWidth);

        // Lerp( frac, 1.0f, m_flConstraintSpeedFactor )
        return 1.0f + frac * (_s.ConstraintSpeedFactor - 1.0f);
    }

    /// <summary><c>CheckParameters</c> (gamemovement.cpp:1058).</summary>
    private void CheckParameters()
    {
        // CHL2GameMovement's: the speed changes first, "so they are predicted
        // correctly" (hl_gamemovement.cpp:532).
        if (Hl2Player is { } hl2)
            HandleSpeedChanges(hl2);

        if (_s.MoveType != MoveTypeIsometric &&
            _s.MoveType != MoveTypeNoClip &&
            _s.MoveType != MoveTypeObserver)
        {
            float spd = _s.ForwardMove * _s.ForwardMove +
                        _s.SideMove * _s.SideMove +
                        _s.UpMove * _s.UpMove;

            float maxspeed = _s.ClientMaxSpeed;
            if (maxspeed != 0.0)
                _s.MaxSpeed = MathF.Min(maxspeed, _s.MaxSpeed);

            float speedFactor = 1.0f;
            if (_s.SurfaceDataValid)
                speedFactor = _surfaceData.MaxSpeedFactor;

            float constraintSpeedFactor = ComputeConstraintSpeedFactor();
            if (constraintSpeedFactor < speedFactor)
                speedFactor = constraintSpeedFactor;

            _s.MaxSpeed *= speedFactor;

            // OnPlayerMaxSpeed, asked LAST so a rule overrides the answer
            // rather than competing with its inputs. The pack's MaxSpeed is the
            // identity unless a mod registers IMovementRules, which is what
            // makes this a plumbing point and not a gameplay change.
            _s.MaxSpeed = MaxSpeedScale(_s.EntIndex, _s.MaxSpeed, _s.Ducked,
                                        _s.SurfaceDataValid ? _surfaceData.Material : 0);

            if (_c.Optimizations)
            {
                if (spd != 0.0 && spd > _s.MaxSpeed * _s.MaxSpeed)
                {
                    float ratio = _s.MaxSpeed / MathF.Sqrt(spd);
                    _s.ForwardMove *= ratio;
                    _s.SideMove *= ratio;
                    _s.UpMove *= ratio;
                }
            }
            else
            {
                spd = MathF.Sqrt(spd);
                if (spd != 0.0 && spd > _s.MaxSpeed)
                {
                    float ratio = _s.MaxSpeed / spd;
                    _s.ForwardMove *= ratio;
                    _s.SideMove *= ratio;
                    _s.UpMove *= ratio;
                }
            }
        }

        if ((_s.Flags & FlFrozen) != 0 || (_s.Flags & FlOnTrain) != 0 || IsDead())
        {
            _s.ForwardMove = 0;
            _s.SideMove = 0;
            _s.UpMove = 0;
        }

        DecayPunchAngle();

        if (!IsDead())
        {
            Vector vAngle = _s.Angles + _s.PunchAngle;

            if (_s.MoveType != MoveTypeIsometric && _s.MoveType != MoveTypeNoClip)
                _s.Angles.Z = CalcRoll(vAngle, _s.Velocity, _c.RollAngle, _c.RollSpeed);
            else
                _s.Angles.Z = 0.0f;

            _s.Angles.X = vAngle.X;
            _s.Angles.Y = vAngle.Y;
        }
        else
        {
            _s.Angles = _s.OldAngles;
        }

        if (IsDead())
            _s.ViewOffset = _c.DeadViewHeight * _s.ModelScale;

        if (_s.Angles.Y > 180.0f)
            _s.Angles.Y -= 360.0f;
    }

    /// <summary><c>CalcRoll</c> (gamemovement.cpp:3582), through MoveMath.</summary>
    private float CalcRoll(in Vector angles, in Vector velocity, float rollAngle, float rollSpeed)
    {
        MoveVec.AngleVectors(angles, out _, out Vector right, out _);
        return MoveMath.CalcRoll(right, velocity, rollAngle, rollSpeed);
    }

    /// <summary><c>DecayPunchAngle</c> (gamemovement.cpp:1319), through MoveMath.</summary>
    private void DecayPunchAngle()
    {
        Vector punch = _s.PunchAngle;
        Vector punchVel = _s.PunchAngleVel;
        MoveMath.DecayPunchAngle(ref punch, ref punchVel, _s.FrameTime);
        _s.PunchAngle = punch;
        _s.PunchAngleVel = punchVel;
    }

    /// <summary><c>ReduceTimers</c> (gamemovement.cpp:1172), through MoveMath.</summary>
    private void ReduceTimers()
    {
        // CHL2GameMovement's: the player's half first (hl_gamemovement.cpp:547).
        if (Hl2Player is { } hl2)
            Hl2ReduceTimers(hl2);

        float duck = _s.DuckTime, duckJump = _s.DuckJumpTime, jump = _s.JumpTime, swim = _s.SwimSoundTime;
        MoveMath.ReduceTimers(ref duck, ref duckJump, ref jump, ref swim, _s.FrameTime);
        _s.DuckTime = duck;
        _s.DuckJumpTime = duckJump;
        _s.JumpTime = jump;
        _s.SwimSoundTime = swim;
    }

    /// <summary><c>IsDead</c> (gamemovement.cpp:1012).</summary>
    private bool IsDead() => _s.Health <= 0 && !_s.IsAlive;

    //=========================================================================
    // The hull, the mask, and the tick intervals.
    //=========================================================================

    /// <summary><c>GetPlayerMins(bool)</c> (gamemovement.cpp:786).</summary>
    private Vector GetPlayerMins(bool ducked)
        => (ducked ? _c.DuckHullMin : _c.HullMin) * _s.ModelScale;

    /// <summary><c>GetPlayerMaxs(bool)</c>.</summary>
    private Vector GetPlayerMaxs(bool ducked)
        => (ducked ? _c.DuckHullMax : _c.HullMax) * _s.ModelScale;

    /// <summary><c>GetPlayerMins()</c> — the observer hull is a different hull.</summary>
    private Vector GetPlayerMins()
        => IsObserver() ? _c.ObsHullMin * _s.ModelScale
                        : (_s.Ducked ? _c.DuckHullMin : _c.HullMin) * _s.ModelScale;

    /// <summary><c>GetPlayerMaxs()</c>.</summary>
    private Vector GetPlayerMaxs()
        => IsObserver() ? _c.ObsHullMax * _s.ModelScale
                        : (_s.Ducked ? _c.DuckHullMax : _c.HullMax) * _s.ModelScale;

    /// <summary><c>GetPlayerViewOffset(bool)</c>.</summary>
    private Vector GetPlayerViewOffset(bool ducked)
        => (ducked ? _c.DuckView : _c.View) * _s.ModelScale;

    private bool IsObserver() => _s.ObserverMode != 0;

    /// <summary><c>PlayerSolidMask</c> (gamemovement.cpp:715).</summary>
    private static uint PlayerSolidMask(bool brushOnly = false)
        => brushOnly ? MaskPlayerSolidBrushOnly : MaskPlayerSolid;

    private enum IntervalType { Ground, Stuck, Ladder }

    /// <summary><c>GetCheckInterval</c> (gamemovement.cpp:725).</summary>
    private int GetCheckInterval(IntervalType type) => type switch
    {
        IntervalType.Ground => Ticks(CategorizeGroundSurfaceInterval),
        IntervalType.Stuck => _s.StuckLast != 0
            ? 1
            : Ticks(_c.MaxClients == 1 ? CheckStuckIntervalSp : CheckStuckInterval),
        IntervalType.Ladder => Ticks(CheckLadderInterval),
        _ => 1,
    };

    /// <summary>
    /// <c>(int)( &lt;seconds&gt; / TICK_INTERVAL )</c>, in FLOAT and truncated,
    /// which is what the macros expand to. At 0.015 that is 20, 66, 13 and 13 —
    /// and the 66 is why the stuck check does not run on most commands.
    /// </summary>
    private int Ticks(float seconds)
    {
        if (_c.TickInterval <= 0f)
            return 1;
        return (int)(seconds / _c.TickInterval);
    }

    /// <summary>
    /// <c>CheckInterval</c> (gamemovement.cpp:766).
    ///
    /// <para>
    /// The stuck interval is SIXTY-SIX commands at a 0.015 tick, so this
    /// answers false on most of them — which is the whole reason it exists, and
    /// the reason a port that ran CheckStuck every command traced where the C++
    /// did not.
    /// </para>
    /// </summary>
    private bool CheckInterval(IntervalType type)
    {
        int tickInterval = GetCheckInterval(type);
        if (!_c.Optimizations)
            return true;
        return (_s.CommandNumber + _s.EntIndex) % tickInterval == 0;
    }

    /// <summary><c>TracePlayerBBox</c> (gamemovement.cpp:5375).</summary>
    private MoveTrace TracePlayerBBox(in Vector start, in Vector end, uint mask, int collisionGroup)
        => _w.Trace(start, end, GetPlayerMins(), GetPlayerMaxs(), mask, collisionGroup);

    /// <summary>
    /// <c>TestPlayerPosition</c> (gamemovement.cpp:859).
    ///
    /// <para>
    /// Answers whether the position is INSIDE something, which stock spells as
    /// "the handle it returns is not INVALID_EHANDLE". The test is
    /// <c>(contents &amp; PlayerSolidMask()) &amp;&amp; m_pEnt</c> — the RAW
    /// pointer, so a static prop counts here where it does not count as ground.
    /// </para>
    /// </summary>
    private bool TestPlayerPosition(in Vector pos, int collisionGroup, out MoveTrace pm)
    {
        pm = _w.Trace(pos, pos, GetPlayerMins(), GetPlayerMaxs(),
                      PlayerSolidMask(), collisionGroup);
        return (pm.Contents & (int)PlayerSolidMask()) != 0 && pm.HasEntity;
    }

    //=========================================================================
    // Gravity, friction, acceleration — all four through MoveMath.
    //=========================================================================

    /// <summary><c>StartGravity</c> (gamemovement.cpp:1376).</summary>
    private void StartGravity()
    {
        MoveMath.Gravity(MoveMath.GravityPhase.Start, ref _s.Velocity, ref _s.BaseVelocity,
                         ref _s.Origin, _s.EntityGravity, _s.CurrentGravity,
                         _s.FrameTime, _c.MaxVelocity, _s.WaterJumpTime != 0.0f);
    }

    /// <summary><c>FinishGravity</c> (gamemovement.cpp:1878).</summary>
    private void FinishGravity()
    {
        MoveMath.Gravity(MoveMath.GravityPhase.Finish, ref _s.Velocity, ref _s.BaseVelocity,
                         ref _s.Origin, _s.EntityGravity, _s.CurrentGravity,
                         _s.FrameTime, _c.MaxVelocity, _s.WaterJumpTime != 0.0f);
    }

    /// <summary><c>AddGravity</c> (gamemovement.cpp:3435).</summary>
    private void AddGravity()
    {
        MoveMath.Gravity(MoveMath.GravityPhase.Add, ref _s.Velocity, ref _s.BaseVelocity,
                         ref _s.Origin, _s.EntityGravity, _s.CurrentGravity,
                         _s.FrameTime, _c.MaxVelocity, _s.WaterJumpTime != 0.0f);
    }

    /// <summary><c>CheckVelocity</c> (gamemovement.cpp:3371).</summary>
    private void CheckVelocity()
        => MoveMath.CheckVelocity(ref _s.Velocity, ref _s.Origin, _c.MaxVelocity);

    /// <summary>
    /// <c>CanAccelerate</c> (gamemovement.cpp:2048): not dead, not water
    /// jumping. HL2MP's loop (one handed <see cref="Ladders"/>, which is
    /// CHL2GameMovement) answers true always: its override
    /// (hl_gamemovement.cpp:1182) calls the base and discards the answer.
    /// </summary>
    private bool CanAccelerate() => Ladders is not null || (!_s.DeadFlag && _s.WaterJumpTime == 0f);

    /// <summary><c>Accelerate</c> (gamemovement.cpp:2068).</summary>
    private void Accelerate(in Vector wishDir, float wishSpeed, float accel)
        => MoveMath.Accelerate(ref _s.Velocity, wishDir, wishSpeed, accel,
                               _s.FrameTime, _s.SurfaceFriction, CanAccelerate());

    /// <summary><c>GetAirSpeedCap</c> — a parameter since Phase 6, not a literal 30.</summary>
    private float GetAirSpeedCap() => _c.AirSpeedCap;

    /// <summary><c>AirAccelerate</c> (gamemovement.cpp:1935).</summary>
    private void AirAccelerate(in Vector wishDir, float wishSpeed, float accel)
    {
        Vector outWish = _s.OutWishVel;
        MoveMath.AirAccelerate(ref _s.Velocity, ref outWish, wishDir, wishSpeed, accel,
                               _s.FrameTime, _s.SurfaceFriction, GetAirSpeedCap(),
                               CanAccelerate());
        _s.OutWishVel = outWish;
    }

    /// <summary>
    /// <c>Friction</c> (gamemovement.cpp:1773).
    ///
    /// <para>
    /// The friction RULE is applied here rather than inside
    /// <see cref="MoveMath.Friction"/>, because the rule may depend on the
    /// player and the arithmetic may not. Stock's two early returns are
    /// repeated exactly rather than collapsed into "on ground": stock computes
    /// the friction INSIDE the on-ground branch, after the water-jump and
    /// too-slow returns, so a rule asked outside them would be asked on frames
    /// stock never asks on.
    /// </para>
    /// </summary>
    private void Friction()
    {
        float friction = _c.Friction * _s.SurfaceFriction;
        float speed = MoveVec.Length(_s.Velocity);

        // `!(speed < 0.1f)` and not `speed >= 0.1f`: a NaN falls through
        // stock's test and would be caught by the tidier form.
        if (_s.HasGround && _s.WaterJumpTime == 0f && !(speed < 0.1f))
        {
            friction = FrictionScale(_s.EntIndex, friction, speed, _s.Ducked,
                                     _s.SurfaceDataValid ? _surfaceData.Material : 0);
        }

        Vector outWish = _s.OutWishVel;
        MoveMath.Friction(ref _s.Velocity, ref outWish, _s.HasGround, friction,
                          _c.StopSpeed, _s.FrameTime, _s.WaterJumpTime != 0f);
        _s.OutWishVel = outWish;
    }

    /// <summary><c>ClipVelocity</c> (gamemovement.cpp:3516).</summary>
    private static int ClipVelocity(in Vector input, in Vector normal, out Vector output,
                                    float overbounce, float redirectCoeff = 0f)
        => MoveMath.ClipVelocity(input, normal, out output, overbounce, redirectCoeff);

    //=========================================================================
    // The three managed DECISIONS, which are C#'s say over how movement feels.
    //
    // Hooks rather than direct calls to SourceSharp.Shared.Movement, and that
    // is a layering fact rather than a preference: this file is in
    // SourceSharp.Abi, which Shared references and which therefore cannot
    // reference Shared back. The defaults are stock — the identity — so a build
    // that registers nothing moves exactly like Half-Life.
    //=========================================================================

    /// <summary>
    /// This loop's three decisions (statics to zero: they were three static
    /// hooks every side and every game instance shared). Stock unless the run
    /// that owns this loop hands in the module's own; see
    /// <see cref="MovementRunner.ProcessMovement(MoveStateBlob*, in MovementRules)"/>.
    /// </summary>
    public MovementRules Rules { get; set; } = MovementRules.Stock;

    /// <summary>
    /// Clamped at zero, exactly as the native forwarder clamps it: a negative
    /// friction accelerates you the way you were already going, which is not a
    /// slippery floor but a game that has stopped obeying its own physics.
    /// </summary>
    private float FrictionScale(int playerIndex, float friction, float speed,
                                bool ducking, int groundMaterial)
    {
        float scaled = Rules.Friction(playerIndex, friction, speed, ducking, groundMaterial);
        return scaled < 0.0f ? 0.0f : scaled;
    }

    /// <summary>Clamped at zero: a negative maximum speed inverts the comparison.</summary>
    private float MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
    {
        float scaled = Rules.MaxSpeed(playerIndex, maxSpeed, ducking, groundMaterial);
        return scaled < 0.0f ? 0.0f : scaled;
    }

    /// <summary>Clamped at zero: a negative multiplier is a jump into the floor.</summary>
    private float JumpScale(in JumpRequest request)
    {
        float scale = Rules.Jump(request);
        return scale < 0.0f ? 0.0f : scale;
    }

    /// <summary>
    /// CheckJumpButton's jump speed, both of stock's branches: under
    /// movement optimisations (the server's and the client's, always) the HL2
    /// arm's literal <see cref="Hl2JumpSpeed"/>, whatever the gravity; without
    /// them, or in the non-HL2 arms the movement recording holds
    /// (<see cref="MoveConstants.NoHl2Arms"/>), sqrt( 2 g h ).
    ///
    /// <para>
    /// The literal stands for <see cref="StockJumpHeight"/>, so a
    /// <see cref="MovementParams.JumpHeight"/> a mod sets scales it as that
    /// height's speed would scale (by the root of the ratio), which keeps the
    /// parameter reachable in the branch stock hard-coded. At the stock height
    /// it is the literal exactly: sqrt( 2 * 600 * 21 ) is 158.75, 0.8% short of
    /// what stock jumps.
    /// </para>
    /// </summary>
    private float JumpVelocity(float gravity, float jumpHeight)
    {
        if (jumpHeight <= 0.0f)
            return 0.0f;
        if (_c.Optimizations && !_c.NoHl2Arms)
            return jumpHeight == StockJumpHeight ? Hl2JumpSpeed : Hl2JumpSpeed * MathF.Sqrt(jumpHeight / StockJumpHeight);
        return gravity > 0.0f ? MathF.Sqrt(2.0f * gravity * jumpHeight) : 0.0f;
    }

    /// <summary>The jump height, which the shim pushes as part of the parameters.</summary>
    public float JumpHeight { get; set; } = 21.0f;

    //=========================================================================
    // The move types.
    //=========================================================================

    /// <summary><c>FullWalkMove</c> (gamemovement.cpp:2286).</summary>
    private void FullWalkMove()
    {
        if (!CheckWater())
            StartGravity();

        // Leaping out of the water: just update the counters.
        if (_s.WaterJumpTime != 0f)
        {
            WaterJump();
            TryPlayerMove();
            CheckWater();
            return;
        }

        if (_s.WaterLevel >= WlWaist)
        {
            if (_s.WaterLevel == WlWaist)
                CheckWaterJump();

            if (_s.Velocity.Z < 0 && _s.WaterJumpTime != 0f)
                _s.WaterJumpTime = 0;

            if ((_s.Buttons & InJump) != 0)
                CheckJumpButton();
            else
                _s.OldButtons &= ~InJump;

            WaterMove();
            CategorizePosition();

            if (_s.HasGround)
                _s.Velocity.Z = 0;
        }
        else
        {
            if ((_s.Buttons & InJump) != 0)
                CheckJumpButton();
            else
                _s.OldButtons &= ~InJump;

            // Friction is handled BEFORE any base velocity is added, so a
            // player standing still on a conveyor is not slowed relative to it.
            if (_s.HasGround)
            {
                _s.Velocity.Z = 0.0f;
                Friction();
            }

            CheckVelocity();

            if (_s.HasGround)
                WalkMove();
            else
                AirMove();

            CategorizePosition();
            CheckVelocity();

            if (!CheckWater())
                FinishGravity();

            if (_s.HasGround)
                _s.Velocity.Z = 0;

            CheckFalling();
        }

        if ((_s.OldWaterLevel == WlNotInWater && _s.WaterLevel != WlNotInWater) ||
            (_s.OldWaterLevel != WlNotInWater && _s.WaterLevel == WlNotInWater))
        {
            PlaySwimSound();
            _w.Splash();
        }
    }

    /// <summary><c>WalkMove</c> (gamemovement.cpp:2152).</summary>
    private void WalkMove()
    {
        MoveVec.AngleVectors(_s.ViewAngles, out Vector forward, out Vector right, out _);

        bool oldGround = _s.HasGround;

        float fmove = _s.ForwardMove;
        float smove = _s.SideMove;

        if (_c.Optimizations)
        {
            // Stock skips the normalize when the z was already zero, which is
            // NOT the same as always normalizing: VectorNormalize's rsqrt is an
            // approximation, so normalizing an already-unit vector moves it.
            if (forward.Z != 0)
            {
                forward.Z = 0;
                MoveVec.Normalize(ref forward);
            }

            if (right.Z != 0)
            {
                right.Z = 0;
                MoveVec.Normalize(ref right);
            }
        }
        else
        {
            forward.Z = 0;
            right.Z = 0;
            MoveVec.Normalize(ref forward);
            MoveVec.Normalize(ref right);
        }

        var wishvel = new Vector(forward.X * fmove + right.X * smove,
                                 forward.Y * fmove + right.Y * smove,
                                 0f);

        Vector wishdir = wishvel;
        float wishspeed = MoveVec.Normalize(ref wishdir);

        if (wishspeed != 0.0f && wishspeed > _s.MaxSpeed)
        {
            wishvel *= _s.MaxSpeed / wishspeed;
            wishspeed = _s.MaxSpeed;
        }

        _s.Velocity.Z = 0;
        Accelerate(wishdir, wishspeed, _c.Accelerate);
        _s.Velocity.Z = 0;

        _s.Velocity += _s.BaseVelocity;

        float spd = MoveVec.Length(_s.Velocity);

        if (spd < 1.0f)
        {
            _s.Velocity = Vector.Zero;
            _s.Velocity -= _s.BaseVelocity;
            return;
        }

        var dest = new Vector(
            _s.Origin.X + _s.Velocity.X * _s.FrameTime,
            _s.Origin.Y + _s.Velocity.Y * _s.FrameTime,
            _s.Origin.Z);

        MoveTrace pm = TracePlayerBBox(_s.Origin, dest, PlayerSolidMask(), CollisionGroupPlayerMovement);

        _s.OutWishVel += wishdir * wishspeed;

        if (pm.Fraction == 1)
        {
            _s.Origin = pm.EndPos;
            _s.Velocity -= _s.BaseVelocity;
            StayOnGround();
            return;
        }

        // Don't walk up stairs if not on ground.
        if (!oldGround && _s.WaterLevel == 0)
        {
            _s.Velocity -= _s.BaseVelocity;
            return;
        }

        if (_s.WaterJumpTime != 0f)
        {
            _s.Velocity -= _s.BaseVelocity;
            return;
        }

        StepMove(dest, ref pm);

        _s.Velocity -= _s.BaseVelocity;

        StayOnGround();
    }

    /// <summary><c>AirMove</c> (gamemovement.cpp:1999).</summary>
    private void AirMove()
    {
        MoveVec.AngleVectors(_s.ViewAngles, out Vector forward, out Vector right, out _);

        float fmove = _s.ForwardMove;
        float smove = _s.SideMove;

        // AirMove normalizes UNCONDITIONALLY where WalkMove tests the z first.
        // The asymmetry is stock's and it is one rsqrt of difference in the
        // wish direction, which is enough to move a player a hundredth of a
        // unit over a jump.
        forward.Z = 0;
        right.Z = 0;
        MoveVec.Normalize(ref forward);
        MoveVec.Normalize(ref right);

        var wishvel = new Vector(forward.X * fmove + right.X * smove,
                                 forward.Y * fmove + right.Y * smove,
                                 0f);

        Vector wishdir = wishvel;
        float wishspeed = MoveVec.Normalize(ref wishdir);

        if (wishspeed != 0 && wishspeed > _s.MaxSpeed)
        {
            wishvel *= _s.MaxSpeed / wishspeed;
            wishspeed = _s.MaxSpeed;
        }

        AirAccelerate(wishdir, wishspeed, _c.AirAccelerate);

        _s.Velocity += _s.BaseVelocity;
        TryPlayerMove();
        _s.Velocity -= _s.BaseVelocity;
    }

    /// <summary><c>StepMove</c> (gamemovement.cpp:1678).</summary>
    private void StepMove(in Vector destination, ref MoveTrace trace)
    {
        Vector endPos = destination;

        Vector pos = _s.Origin;
        Vector vel = _s.Velocity;

        // Slide move down.
        TryPlayerMove(endPos, trace);

        Vector downPos = _s.Origin;
        Vector downVel = _s.Velocity;

        _s.Origin = pos;
        _s.Velocity = vel;

        // Move up a stair height.
        endPos = _s.Origin;
        if (_s.AllowAutoMovement)
            endPos.Z += _s.StepSize + DistEpsilon;

        trace = TracePlayerBBox(_s.Origin, endPos, PlayerSolidMask(), CollisionGroupPlayerMovement);
        if (!trace.StartSolid && !trace.AllSolid)
            _s.Origin = trace.EndPos;

        // Slide move up.
        TryPlayerMove();

        // Move down a stair (attempt to).
        endPos = _s.Origin;
        if (_s.AllowAutoMovement)
            endPos.Z -= _s.StepSize + DistEpsilon;

        trace = TracePlayerBBox(_s.Origin, endPos, PlayerSolidMask(), CollisionGroupPlayerMovement);

        // If we are not on the ground any more then use the original attempt.
        if (trace.PlaneNormal.Z < 0.7)
        {
            _s.Origin = downPos;
            _s.Velocity = downVel;
            float stepDistDown = _s.Origin.Z - pos.Z;
            if (stepDistDown > 0.0f)
                _s.OutStepHeight += stepDistDown;
            return;
        }

        if (!trace.StartSolid && !trace.AllSolid)
            _s.Origin = trace.EndPos;

        Vector upPos = _s.Origin;

        float downDist = (downPos.X - pos.X) * (downPos.X - pos.X) +
                         (downPos.Y - pos.Y) * (downPos.Y - pos.Y);
        float upDist = (upPos.X - pos.X) * (upPos.X - pos.X) +
                       (upPos.Y - pos.Y) * (upPos.Y - pos.Y);
        if (downDist > upDist)
        {
            _s.Origin = downPos;
            _s.Velocity = downVel;
        }
        else
        {
            // Copy the z from the slide move.
            _s.Velocity.Z = downVel.Z;
        }

        float stepDist = _s.Origin.Z - pos.Z;
        if (stepDist > 0)
            _s.OutStepHeight += stepDist;
    }

    /// <summary><c>StayOnGround</c> (gamemovement.cpp:2116).</summary>
    private void StayOnGround()
    {
        Vector start = _s.Origin;
        Vector end = _s.Origin;
        start.Z += 2;
        end.Z -= _s.StepSize;

        // See how far up we can go without getting stuck.
        MoveTrace trace = TracePlayerBBox(_s.Origin, start, PlayerSolidMask(), CollisionGroupPlayerMovement);
        start = trace.EndPos;

        // startsolid is unreliable here — it does not get set when tracing a
        // bounding box against terrain — so this uses the fraction instead.
        trace = TracePlayerBBox(start, end, PlayerSolidMask(), CollisionGroupPlayerMovement);
        if (trace.Fraction > 0.0f &&
            trace.Fraction < 1.0f &&
            !trace.StartSolid &&
            trace.PlaneNormal.Z >= 0.7)
        {
            float delta = MathF.Abs(_s.Origin.Z - trace.EndPos.Z);

            // Stock's own comment: "This is incredibly hacky."
            if (delta > 0.5f * CoordResolution)
                _s.Origin = trace.EndPos;
        }
    }

    /// <summary><c>FullNoClipMove</c> (gamemovement.cpp:2535).</summary>
    private void FullNoClipMove(float factor, float maxAcceleration)
    {
        float maxspeed = _c.MaxSpeed * factor;

        MoveVec.AngleVectors(_s.ViewAngles, out Vector forward, out Vector right, out _);

        // STOCK QUIRK, KEPT: maxspeed above was computed with the UNHALVED
        // factor, and the speed key halves the factor only afterwards — so
        // walking in noclip moves at half speed but is still capped at the full
        // one. Written the tidy way the cap would change too.
        if ((_s.Buttons & InSpeed) != 0)
            factor /= 2.0f;

        float fmove = _s.ForwardMove * factor;
        float smove = _s.SideMove * factor;

        MoveVec.Normalize(ref forward);
        MoveVec.Normalize(ref right);

        var wishvel = new Vector(forward.X * fmove + right.X * smove,
                                 forward.Y * fmove + right.Y * smove,
                                 forward.Z * fmove + right.Z * smove);
        wishvel.Z += _s.UpMove * factor;

        Vector wishdir = wishvel;
        float wishspeed = MoveVec.Normalize(ref wishdir);

        if (wishspeed > maxspeed)
        {
            wishvel *= maxspeed / wishspeed;
            wishspeed = maxspeed;
        }

        if (maxAcceleration > 0.0)
        {
            Accelerate(wishdir, wishspeed, maxAcceleration);

            float spd = MoveVec.Length(_s.Velocity);
            if (spd < 1.0f)
            {
                _s.Velocity = Vector.Zero;
                return;
            }

            // The unsuffixed 4.0 makes this a DOUBLE comparison and a double
            // quotient in stock; written in float the threshold differs.
            float control = (spd < maxspeed / 4.0) ? (float)(maxspeed / 4.0) : spd;
            float friction = _c.Friction * _s.SurfaceFriction;
            float drop = control * friction * _s.FrameTime;

            float newspeed = spd - drop;
            if (newspeed < 0)
                newspeed = 0;

            newspeed /= spd;
            _s.Velocity *= newspeed;
        }
        else
        {
            _s.Velocity = wishvel;
        }

        _s.Origin = new Vector(
            _s.Origin.X + _s.FrameTime * _s.Velocity.X,
            _s.Origin.Y + _s.FrameTime * _s.Velocity.Y,
            _s.Origin.Z + _s.FrameTime * _s.Velocity.Z);

        if (maxAcceleration < 0.0f)
            _s.Velocity = Vector.Zero;
    }

    /// <summary>
    /// <c>FullObserverMove</c> (gamemovement.cpp:3032) — <c>MOVETYPE_OBSERVER</c>.
    ///
    /// <para>
    /// Ported by step 8f-movetypes, which removed 3e's refusal. Three arms:
    /// following a target (unreachable-armed, see
    /// <see cref="ObserverTargetAsks"/>), anything that is not roaming (do not
    /// move at all — fixed and death cams), and roaming, which is either
    /// <see cref="FullNoClipMove"/> at the spectator convars or a clipped free
    /// roam that ends in <see cref="TryPlayerMove"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Two stock oddities, and only one of them is observable.</b> The speed
    /// clamp scales <c>wishvel</c> by <c>mv-&gt;m_flMaxSpeed / wishspeed</c>
    /// and then assigns <c>wishspeed = maxspeed</c>, where <c>maxspeed</c> is
    /// <c>sv_maxvelocity</c> and not <c>m_flMaxSpeed</c> — two different
    /// numbers. The SCALE IS DEAD in stock: nothing reads <c>wishvel</c> after
    /// it, only <c>wishdir</c> and <c>wishspeed</c> reach
    /// <see cref="Accelerate"/>. It is written out anyway, because deleting a
    /// dead line makes the next reader think stock clamped the way every other
    /// arm clamps; <c>wishspeed</c> taking <c>sv_maxvelocity</c> is the half
    /// that moves a spectator and is the half a fact pins.
    /// The second oddity is the friction: it is bare <c>sv_friction</c>, NOT
    /// multiplied by the surface friction the way <see cref="Friction"/> does
    /// it. That one is unobservable from outside for a different reason —
    /// <see cref="CategorizePosition"/> forces <c>m_surfaceFriction</c> to 1
    /// before every observer command — and it is kept because the day an
    /// observer keeps a surface friction, stock's answer is this one.
    /// </para>
    /// </summary>
    private void FullObserverMove()
    {
        int mode = _s.ObserverMode;

        if (mode == ObsModeInEye || mode == ObsModeChase || mode == ObsModePoi)
        {
            // Stock: the target's origin, angles and velocity, and no move of
            // our own. The target comes from the runner (the managed
            // observer's, lane player-msgs), not the native blob; with none,
            // stock's bare return, counted.
            ObserverTargetAsks++;
            if (_s.HasObserverTarget)
            {
                _s.Origin = _s.ObserverTargetOrigin;
                _s.ViewAngles = _s.ObserverTargetAngles;
                _s.Velocity = _s.ObserverTargetVelocity;
            }

            return;
        }

        if (mode != ObsModeRoaming)
        {
            // Don't move in fixed or death cam mode.
            return;
        }

        if (_c.SpecNoClip)
        {
            // Roam in noclip mode.
            FullNoClipMove(_c.SpecSpeed, _c.SpecAccelerate);
            return;
        }

        // A full clipped free roam move.
        MoveVec.AngleVectors(_s.ViewAngles, out Vector forward, out Vector right, out _);

        float factor = _c.SpecSpeed;

        if ((_s.Buttons & InSpeed) != 0)
            factor /= 2.0f;

        float fmove = _s.ForwardMove * factor;
        float smove = _s.SideMove * factor;

        MoveVec.Normalize(ref forward);
        MoveVec.Normalize(ref right);

        var wishvel = new Vector(forward.X * fmove + right.X * smove,
                                 forward.Y * fmove + right.Y * smove,
                                 forward.Z * fmove + right.Z * smove);
        wishvel.Z += _s.UpMove;

        Vector wishdir = wishvel;
        float wishspeed = MoveVec.Normalize(ref wishdir);

        // sv_maxvelocity, and the clamp below divides by m_flMaxSpeed instead.
        // Stock's, kept: see the summary.
        float maxspeed = _c.MaxVelocity;

        if (wishspeed > maxspeed)
        {
            wishvel *= _s.MaxSpeed / wishspeed;
            wishspeed = maxspeed;
        }

        // "give observer 50% acceration bonus" — stock's comment; the bonus is
        // sv_specaccelerate's default relative to sv_accelerate, not a factor
        // written here.
        Accelerate(wishdir, wishspeed, _c.SpecAccelerate);

        float spd = MoveVec.Length(_s.Velocity);
        if (spd < 1.0f)
        {
            _s.Velocity = Vector.Zero;
            return;
        }

        // sv_friction alone. NOT * m_surfaceFriction.
        float friction = _c.Friction;

        float drop = spd * friction * _s.FrameTime;

        float newspeed = spd - drop;

        if (newspeed < 0)
            newspeed = 0;

        newspeed /= spd;

        _s.Velocity *= newspeed;

        CheckVelocity();

        TryPlayerMove();
    }

    /// <summary>
    /// <c>FullTossMove</c> (gamemovement.cpp:5869) — <c>MOVETYPE_FLY</c> and
    /// <c>MOVETYPE_FLYGRAVITY</c>, which differ by exactly one call.
    ///
    /// <para>
    /// Ported by step 8f-movetypes. Unlike every other arm in this class it
    /// does not go through <see cref="TryPlayerMove"/>'s bump loop at all: it
    /// pushes once with <see cref="PushEntity"/> and hands whatever it hit to
    /// <see cref="PerformFlyCollisionResolution"/>. That is why a flying player
    /// bounces rather than slides.
    /// </para>
    /// </summary>
    private void FullTossMove()
    {
        CheckWater();

        // Add velocity if the player is moving.
        if (_s.ForwardMove != 0.0f || _s.SideMove != 0.0f || _s.UpMove != 0.0f)
        {
            MoveVec.AngleVectors(_s.ViewAngles, out Vector forward, out Vector right, out _);

            float fmove = _s.ForwardMove;
            float smove = _s.SideMove;

            MoveVec.Normalize(ref forward);
            MoveVec.Normalize(ref right);

            var wishvel = new Vector(forward.X * fmove + right.X * smove,
                                     forward.Y * fmove + right.Y * smove,
                                     forward.Z * fmove + right.Z * smove);
            wishvel.Z += _s.UpMove;

            Vector wishdir = wishvel;
            float wishspeed = MoveVec.Normalize(ref wishdir);

            if (wishspeed > _s.MaxSpeed)
            {
                wishvel *= _s.MaxSpeed / wishspeed;
                wishspeed = _s.MaxSpeed;
            }

            Accelerate(wishdir, wishspeed, _c.Accelerate);
        }

        if (_s.Velocity.Z > 0)
            SetGroundEntity(null);

        // If on ground and not moving, return.
        if (_s.HasGround)
        {
            if (_s.BaseVelocity.X == 0f && _s.BaseVelocity.Y == 0f && _s.BaseVelocity.Z == 0f &&
                _s.Velocity.X == 0f && _s.Velocity.Y == 0f && _s.Velocity.Z == 0f)
                return;
        }

        CheckVelocity();

        // THE ONE LINE THAT SEPARATES THE TWO MOVE TYPES.
        if (_s.MoveType == MoveTypeFlyGravity)
            AddGravity();

        // Move origin. Stock's comment: base velocity is not properly accounted
        // for since this entity will move again after the bounce without taking
        // it into account.
        _s.Velocity += _s.BaseVelocity;

        CheckVelocity();

        var move = new Vector(_s.Velocity.X * _s.FrameTime,
                              _s.Velocity.Y * _s.FrameTime,
                              _s.Velocity.Z * _s.FrameTime);

        _s.Velocity -= _s.BaseVelocity;

        MoveTrace pm = PushEntity(move);   // Should this clear basevelocity?

        CheckVelocity();

        if (pm.AllSolid)
        {
            // Entity is trapped in another solid.
            SetGroundEntity(pm);
            _s.Velocity = Vector.Zero;
            return;
        }

        if (pm.Fraction != 1)
            PerformFlyCollisionResolution(pm);

        // Check for in water.
        CheckWater();
    }

    /// <summary>
    /// <c>PerformFlyCollisionResolution</c> (gamemovement.cpp:5799) — what a
    /// flying player does when it hits something.
    ///
    /// <para>
    /// Ported by step 8f-movetypes. Both of stock's <c>Assert(0)</c> arms are
    /// written out as cases that do nothing, with the assert's meaning in a
    /// comment, rather than folded into a <c>default</c>: an assert is stock
    /// saying "this cannot happen", and a port that silently absorbs it loses
    /// the claim.
    /// </para>
    /// </summary>
    private void PerformFlyCollisionResolution(in MoveTrace pm)
    {
        switch (_s.MoveCollide)
        {
            case MoveCollideFlyCustom:
                // Do nothing; the velocity should have been modified by touch.
                // Stock Assert(0)s here — "Should this ever occur for
                // players!?" — and then does nothing anyway.
                //
                // MEASURED REDUNDANT, and kept anyway: deleting this label
                // sends FLY_CUSTOM to `default:`, which also does nothing, and
                // a mutation that deleted it survived the whole suite. The
                // mutation that does NOT survive is grouping it with the two
                // cases below, which is precisely the mistake a reader who
                // saw an empty case would make. It is a label carrying stock's
                // "not this one", not a branch.
                break;

            case MoveCollideFlyBounce:
            case MoveCollideDefault:
            {
                // `2.0 - m_surfaceFriction` is a DOUBLE subtraction in stock
                // (the literal is 2.0, not 2.0f), and the two differ in the
                // last bit for a surface friction below one. Written the tidy
                // way it is a different bounce.
                float backoff = _s.MoveCollide == MoveCollideFlyBounce
                    ? (float)(2.0 - (double)_s.SurfaceFriction)
                    : 1f;

                ClipVelocity(_s.Velocity, pm.PlaneNormal, out Vector clipped, backoff);
                _s.Velocity = clipped;
                break;
            }

            default:
                // MOVECOLLIDE_FLY_SLIDE and anything else: stock Assert(0)s on
                // "Invalid collide type!" and leaves the velocity alone.
                break;
        }

        // Stop if on ground.
        if ((double)pm.PlaneNormal.Z > 0.7)
        {
            // `Vector base` in stock, zeroed and subtracted at the end. It is
            // written out because the subtraction is not quite the identity in
            // IEEE-754 — it is the identity for every value a velocity can
            // hold here, and that is a fact about the values, not the code.
            Vector zeroBase = Vector.Zero;

            if (_s.Velocity.Z < _s.CurrentGravity * _s.FrameTime)
            {
                // We're rolling on the ground, add static friction.
                SetGroundEntity(pm);
                _s.Velocity.Z = 0;
            }

            float vel = _s.Velocity.Dot(_s.Velocity);

            if (vel < (30 * 30) || _s.MoveCollide != MoveCollideFlyBounce)
            {
                SetGroundEntity(pm);
                _s.Velocity = Vector.Zero;
            }
            else
            {
                // Every literal here is a double in stock, so the whole scale
                // is computed in double and narrowed once at the VectorScale
                // call. In float it is a different push.
                float scale = (float)((1.0 - (double)pm.Fraction) * (double)_s.FrameTime * 0.9);

                // Stock reuses FullTossMove's `move` (it is a Vector& here)
                // and nothing reads it afterwards, so it is a local: the
                // reference is stock's storage decision, not a behaviour.
                var move = new Vector(_s.Velocity.X * scale,
                                  _s.Velocity.Y * scale,
                                  _s.Velocity.Z * scale);
                PushEntity(move);
            }

            _s.Velocity -= zeroBase;
        }
    }

    /// <summary><c>FullLadderMove</c> (gamemovement.cpp:2857).</summary>
    private void FullLadderMove()
    {
        CheckWater();

        if ((_s.Buttons & InJump) != 0)
            CheckJumpButton();
        else
            _s.OldButtons &= ~InJump;

        _s.Velocity += _s.BaseVelocity;
        TryPlayerMove();
        _s.Velocity -= _s.BaseVelocity;
    }

    private void PlaySwimSound() => _w.StartSound(_s.Origin, "Player.Swim");

    //=========================================================================
    // TryPlayerMove, the bump loop everything above ends in.
    //=========================================================================

    /// <summary><c>TryPlayerMove</c> (gamemovement.cpp:2881).</summary>
    private int TryPlayerMove() => TryPlayerMove(null, default, 0f);

    private int TryPlayerMove(in Vector firstDest, in MoveTrace firstTrace)
        => TryPlayerMove(firstDest, firstTrace, 0f);

    private int TryPlayerMove(Vector? firstDest, MoveTrace firstTrace, float slideMultiplier)
    {
        const int numbumps = 4;

        int blocked = 0;
        int numplanes = 0;
        var planes = new Vector[MaxClipPlanes];

        Vector originalVelocity = _s.Velocity;
        Vector primalVelocity = _s.Velocity;
        Vector newVelocity = Vector.Zero;

        float allFraction = 0;
        float timeLeft = _s.FrameTime;

        MoveTrace pm = default;

        for (int bumpcount = 0; bumpcount < numbumps; bumpcount++)
        {
            if (MoveVec.Length(_s.Velocity) == 0.0)
                break;

            var end = new Vector(
                _s.Origin.X + timeLeft * _s.Velocity.X,
                _s.Origin.Y + timeLeft * _s.Velocity.Y,
                _s.Origin.Z + timeLeft * _s.Velocity.Z);

            if (_c.Optimizations && firstDest.HasValue &&
                end.X == firstDest.Value.X && end.Y == firstDest.Value.Y && end.Z == firstDest.Value.Z)
            {
                // WalkMove already traced exactly this, so stock reuses it and
                // does NOT trace again. Reproducing the SKIP matters as much as
                // reproducing the arithmetic: a replay that traced here would
                // consume a recorded answer that belongs to the next question.
                pm = firstTrace;
            }
            else
            {
                pm = TracePlayerBBox(_s.Origin, end, PlayerSolidMask(), CollisionGroupPlayerMovement);
            }

            allFraction += pm.Fraction;

            if (pm.AllSolid)
            {
                _s.Velocity = Vector.Zero;
                return 4;
            }

            if (pm.Fraction > 0)
            {
                if (pm.Fraction == 1)
                {
                    // A precision issue in terrain tracing lets a swept box
                    // succeed with its end position inside a triangle. Stock
                    // re-tests with an unswept box; the condition is
                    // `numbumps > 0`, which is a constant 4, so this always runs.
                    MoveTrace stuck = TracePlayerBBox(pm.EndPos, pm.EndPos,
                                                      PlayerSolidMask(), CollisionGroupPlayerMovement);
                    if (stuck.StartSolid || stuck.Fraction != 1.0f)
                    {
                        _s.Velocity = Vector.Zero;
                        break;
                    }
                }

                _s.Origin = pm.EndPos;
                originalVelocity = _s.Velocity;
                numplanes = 0;
            }

            if (pm.Fraction == 1)
                break;

            _w.AddToTouched(pm, _s.Velocity);

            if (pm.PlaneNormal.Z > 0.7)
                blocked |= 1;      // floor
            if (pm.PlaneNormal.Z == 0)
                blocked |= 2;      // step / wall

            timeLeft -= timeLeft * pm.Fraction;

            if (numplanes >= MaxClipPlanes)
            {
                _s.Velocity = Vector.Zero;
                break;
            }

            planes[numplanes] = pm.PlaneNormal;
            numplanes++;

            if (numplanes == 1 && _s.MoveType == MoveTypeWalk && !_s.HasGround)
            {
                for (int i = 0; i < numplanes; i++)
                {
                    if (planes[i].Z > 0.7)
                    {
                        ClipVelocity(originalVelocity, planes[i], out newVelocity, 1, slideMultiplier);
                        originalVelocity = newVelocity;
                    }
                    else
                    {
                        ClipVelocity(originalVelocity, planes[i], out newVelocity,
                                     1.0f + _c.Bounce * (1 - _s.SurfaceFriction), slideMultiplier);
                    }
                }

                _s.Velocity = newVelocity;
                originalVelocity = newVelocity;
            }
            else
            {
                int i, j = 0;
                for (i = 0; i < numplanes; i++)
                {
                    ClipVelocity(originalVelocity, planes[i], out Vector clipped, 1, slideMultiplier);
                    _s.Velocity = clipped;

                    for (j = 0; j < numplanes; j++)
                    {
                        if (j != i)
                        {
                            if (_s.Velocity.Dot(planes[j]) < 0)
                                break;   // not ok
                        }
                    }
                    if (j == numplanes)
                        break;
                }

                if (i != numplanes)
                {
                    // Went along this plane; velocity is already set.
                }
                else
                {
                    if (numplanes != 2)
                    {
                        _s.Velocity = Vector.Zero;
                        break;
                    }

                    Vector dir = planes[0].Cross(planes[1]);
                    MoveVec.Normalize(ref dir);
                    float d = dir.Dot(_s.Velocity);
                    _s.Velocity = dir * d;
                }

                // If the velocity now opposes the original, stop dead — this is
                // what stops the tiny oscillation in an acute corner.
                float dot = _s.Velocity.Dot(primalVelocity);
                if (dot <= 0)
                {
                    _s.Velocity = Vector.Zero;
                    break;
                }
            }
        }

        if (allFraction == 0)
            _s.Velocity = Vector.Zero;

        float slamVol = 0.0f;
        float lateralStoppingAmount = MoveVec.Length2D(primalVelocity) - MoveVec.Length2D(_s.Velocity);
        if (lateralStoppingAmount > MaxSafeFallSpeed * 2.0f)
            slamVol = 1.0f;
        else if (lateralStoppingAmount > MaxSafeFallSpeed)
            slamVol = 0.85f;

        PlayerRoughLandingEffects(slamVol);

        return blocked;
    }

    /// <summary><c>PushEntity</c> (gamemovement.cpp:3491).</summary>
    private MoveTrace PushEntity(in Vector push)
    {
        Vector end = _s.Origin + push;
        MoveTrace trace = TracePlayerBBox(_s.Origin, end, PlayerSolidMask(), CollisionGroupPlayerMovement);
        _s.Origin = trace.EndPos;

        if (trace.Fraction < 1.0 && !trace.AllSolid)
            _w.AddToTouched(trace, _s.Velocity);

        return trace;
    }

    //=========================================================================
    // Where we are: water, ground, falling.
    //=========================================================================

    /// <summary><c>CheckWater</c> (gamemovement.cpp:3931).</summary>
    private bool CheckWater()
    {
        Vector mins = GetPlayerMins();
        Vector maxs = GetPlayerMaxs();

        var point = new Vector(
            _s.Origin.X + (mins.X + maxs.X) * 0.5f,
            _s.Origin.Y + (mins.Y + maxs.Y) * 0.5f,
            _s.Origin.Z + mins.Z + 1);

        _s.WaterLevel = WlNotInWater;
        _s.WaterType = ContentsEmpty;

        int cont = _w.PointContents(point, 0);

        if ((cont & (int)MaskWater) != 0)
        {
            _s.WaterType = cont;
            _s.WaterLevel = WlFeet;

            point.Z = _s.Origin.Z + (mins.Z + maxs.Z) * 0.5f;
            cont = _w.PointContents(point, 1);
            if ((cont & (int)MaskWater) != 0)
            {
                _s.WaterLevel = WlWaist;

                point.Z = _s.Origin.Z + _s.ViewOffset.Z;
                cont = _w.PointContents(point, 2);
                if ((cont & (int)MaskWater) != 0)
                    _s.WaterLevel = WlEyes;
            }

            if ((cont & (int)MaskCurrent) != 0)
            {
                var v = Vector.Zero;
                if ((cont & ContentsCurrent0) != 0) v.X += 1;
                if ((cont & ContentsCurrent90) != 0) v.Y += 1;
                if ((cont & ContentsCurrent180) != 0) v.X -= 1;
                if ((cont & ContentsCurrent270) != 0) v.Y -= 1;
                if ((cont & ContentsCurrentUp) != 0) v.Z += 1;
                if ((cont & ContentsCurrentDown) != 0) v.Z -= 1;

                // The deeper we are, the stronger the current. The 50.0 is a
                // double literal, so the scale is computed in double.
                float scale = (float)(50.0 * _s.WaterLevel);
                _s.BaseVelocity = new Vector(
                    _s.BaseVelocity.X + scale * v.X,
                    _s.BaseVelocity.Y + scale * v.Y,
                    _s.BaseVelocity.Z + scale * v.Z);
            }
        }

        return _s.WaterLevel > WlFeet;
    }

    /// <summary><c>SetGroundEntity</c> (gamemovement.cpp:4011).</summary>
    private void SetGroundEntity(MoveTrace? pm)
    {
        bool newGround = pm.HasValue && pm.Value.HasGround;
        Vector newGroundVelocity = pm.HasValue ? pm.Value.GroundVelocity : Vector.Zero;

        bool oldGround = _s.HasGround;
        Vector baseVelocity = _s.BaseVelocity;

        if (!oldGround && newGround)
        {
            // Subtract the ground's velocity at the instant we land.
            baseVelocity -= newGroundVelocity;
            baseVelocity.Z = newGroundVelocity.Z;
        }
        else if (oldGround && !newGround)
        {
            // Add it back at the instant we leave.
            Vector vecGround = _s.GroundVelocity;
            baseVelocity += vecGround;
            baseVelocity.Z = vecGround.Z;
        }

        _s.BaseVelocity = baseVelocity;
        _s.HasGround = newGround;
        _s.GroundVelocity = newGround ? newGroundVelocity : Vector.Zero;

        // FL_ONGROUND, which stock does NOT set here: the loop calls
        // player->MvSetGroundEntity, and CBaseEntity::SetGroundEntity is what
        // adds and removes the flag. There is no player object on this side of
        // the boundary, so the flag moves with the ground here — and it has to,
        // because the flag is networked and predicted and the parity fixture
        // compares it. Found by the fixture: the first replayed command kept
        // FL_ONGROUND over a take-off the C++ recorded as clearing it.
        if (newGround)
            _s.Flags |= FlOnGround;
        else
            _s.Flags &= ~FlOnGround;

        if (newGround)
        {
            MoveTrace hit = pm!.Value;
            CategorizeGroundSurface(hit);

            _s.WaterJumpTime = 0;

            if (!hit.HitWorld)
                _w.AddToTouched(hit, _s.Velocity);

            _s.Velocity.Z = 0.0f;
        }
    }

    /// <summary><c>CategorizeGroundSurface</c> (gamemovement.cpp:996).</summary>
    private void CategorizeGroundSurface(in MoveTrace pm)
    {
        _s.SurfaceProps = pm.SurfaceProps;
        _surfaceData = _w.Surface(_s.SurfaceProps);
        _s.SurfaceDataValid = _surfaceData.Valid;
        _s.SurfaceFriction = _surfaceData.Friction;

        // "HACKHACK: Scale this to fudge the relationship between vphysics
        // friction values and player friction values." 0.8 vphysics is 1.0 for
        // a player, and this multiply is what makes them equivalent.
        _s.SurfaceFriction *= 1.25f;
        if (_s.SurfaceFriction > 1.0f)
            _s.SurfaceFriction = 1.0f;
    }

    /// <summary><c>TryTouchGroundInQuadrants</c> (gamemovement.cpp:4140).</summary>
    private void TryTouchGroundInQuadrants(in Vector start, in Vector end, uint mask,
                                           int collisionGroup, ref MoveTrace pm)
    {
        Vector minsSrc = GetPlayerMins();
        Vector maxsSrc = GetPlayerMaxs();

        float fraction = pm.Fraction;
        Vector endpos = pm.EndPos;

        // Four sub-boxes. Whatever the answer, the ORIGINAL fraction and end
        // position are restored, so the player is never moved down onto the new
        // floor — a leaning wall the first trace hit would otherwise stick them.
        Span<(Vector mins, Vector maxs)> quadrants = stackalloc (Vector, Vector)[4]
        {
            (minsSrc, new Vector(MathF.Min(0, maxsSrc.X), MathF.Min(0, maxsSrc.Y), maxsSrc.Z)),
            (new Vector(MathF.Max(0, minsSrc.X), MathF.Max(0, minsSrc.Y), minsSrc.Z), maxsSrc),
            (new Vector(minsSrc.X, MathF.Max(0, minsSrc.Y), minsSrc.Z),
             new Vector(MathF.Min(0, maxsSrc.X), maxsSrc.Y, maxsSrc.Z)),
            (new Vector(MathF.Max(0, minsSrc.X), minsSrc.Y, minsSrc.Z),
             new Vector(maxsSrc.X, MathF.Min(0, maxsSrc.Y), maxsSrc.Z)),
        };

        foreach (var (mins, maxs) in quadrants)
        {
            pm = _w.Trace(start, end, mins, maxs, mask, collisionGroup);
            if (pm.HasEntity && pm.PlaneNormal.Z >= 0.7)
            {
                pm.Fraction = fraction;
                pm.EndPos = endpos;
                return;
            }
        }

        pm.Fraction = fraction;
        pm.EndPos = endpos;
    }

    /// <summary><c>CategorizePosition</c> (gamemovement.cpp:4203).</summary>
    private void CategorizePosition()
    {
        // Reset each time, otherwise the friction is bogus after plunging into
        // water at speed.
        _s.SurfaceFriction = 1.0f;

        CheckWater();

        if (IsObserver())
            return;

        const float offset = 2.0f;
        var point = new Vector(_s.Origin.X, _s.Origin.Y, _s.Origin.Z - offset);
        Vector bumpOrigin = _s.Origin;

        float zvel = _s.Velocity.Z;
        bool movingUp = zvel > 0.0f;
        bool movingUpRapidly = zvel > NonJumpVelocity;
        if (movingUpRapidly)
        {
            // A player restored on a lift has a high abs velocity that is the
            // lift's, not theirs. Subtracting the ground's z is what tells the
            // two apart.
            if (_s.HasGround)
                movingUpRapidly = (zvel - _s.GroundVelocity.Z) > NonJumpVelocity;
        }

        if (movingUpRapidly || (movingUp && _s.MoveType == MoveTypeLadder))
        {
            SetGroundEntity(null);
        }
        else
        {
            MoveTrace pm = _w.Trace(bumpOrigin, point, GetPlayerMins(), GetPlayerMaxs(),
                                    MaskPlayerSolid, CollisionGroupPlayerMovement);

            if (!pm.HasEntity || pm.PlaneNormal.Z < 0.7)
            {
                TryTouchGroundInQuadrants(bumpOrigin, point, MaskPlayerSolid,
                                          CollisionGroupPlayerMovement, ref pm);

                if (!pm.HasEntity || pm.PlaneNormal.Z < 0.7)
                {
                    SetGroundEntity(null);
                    if (_s.Velocity.Z > 0.0f && _s.MoveType != MoveTypeNoClip)
                        _s.SurfaceFriction = 0.25f;
                }
                else
                {
                    SetGroundEntity(pm);
                }
            }
            else
            {
                SetGroundEntity(pm);
            }

            // Tell any player surface triggers watching that the material
            // underfoot changed. Server-only in stock, and it is a
            // notification rather than a decision — nothing in the loop reads
            // the answer back.
            if (!_s.InAVehicle)
            {
                MoveSurface surface = _w.Surface(pm.SurfaceProps);
                int currentMaterial = surface.Material;
                if (!_s.HasGround)
                    currentMaterial = 0;

                if (_previousTextureType != currentMaterial)
                    _w.SetPlayerSurface(currentMaterial);

                _previousTextureType = currentMaterial;
            }
        }
    }

    /// <summary><c>CheckFalling</c> (gamemovement.cpp:4339).</summary>
    private void CheckFalling()
    {
        // Despite the name this is about LANDING.
        if (!_s.HasGround || _s.FallVelocity <= 0)
            return;

        if (!IsDead() && _s.FallVelocity >= FallPunchThreshold)
        {
            bool alive = true;
            float fvol = 0.5f;

            if (_s.WaterLevel > 0)
            {
                // They landed in water.
            }
            else
            {
                // MovementGroundIsFloating is FALSE for every ground this build
                // can have — it is EFL_TOUCHING_FLUID plus a density lookup on
                // the SDK physics bridge, which the standalone hierarchy does
                // not take. So the floating arm is not ported: it would be dead
                // arithmetic with no way to reach it. Named here rather than
                // silently dropped.

                float groundVelZ = _s.GroundVelocity.Z;
                if (groundVelZ < 0.0f)
                {
                    // Landed on a descending object; subtract its velocity.
                    _s.FallVelocity += groundVelZ;
                    _s.FallVelocity = MathF.Max(0.1f, _s.FallVelocity);
                }

                if (_s.FallVelocity > MaxSafeFallSpeed)
                {
                    alive = _w.PlayerFallingDamage(MoveMath.FallDamage(_s.FallVelocity));
                    fvol = 1.0f;
                }
                else if (_s.FallVelocity > MaxSafeFallSpeed / 2)
                {
                    fvol = 0.85f;
                }
                else if (_s.FallVelocity < MinBounceSpeed)
                {
                    fvol = 0;
                }
            }

            PlayerRoughLandingEffects(fvol);

            if (alive)
                _w.PlayerSetAnimation(PlayerWalkAnimation);
        }

        _s.FallVelocity = 0;
    }

    /// <summary><c>PlayerRoughLandingEffects</c> (gamemovement.cpp:4409).</summary>
    private void PlayerRoughLandingEffects(float fvol)
    {
        if (fvol > 0.0)
        {
            _s.StepSoundTime = 400;
            _w.PlayStepSound(_s.Origin, _s.SurfaceDataValid ? _s.SurfaceProps : -1, fvol, true);

            // Knock the screen around. The 0.013 is a double literal.
            _s.PunchAngle.Z = (float)(_s.FallVelocity * 0.013);

            // STOCK QUIRK, KEPT: it sets ROLL and then tests PITCH. The clamp
            // therefore never fires from this write; it clamps whatever pitch
            // some earlier punch left. Fixing it would change what the player
            // sees on every hard landing.
            if (_s.PunchAngle.X > 8)
                _s.PunchAngle.X = 8;

            _w.RumbleEffect(fvol > 0.85f ? RumbleFallLong : RumbleFallShort, 0, 0);
        }
    }

    /// <summary><c>CheckStuck</c> (gamemovement.cpp:3784).</summary>
    private int CheckStuck()
    {
        if (!TestPlayerPosition(_s.Origin, CollisionGroupPlayerMovement, out MoveTrace traceresult))
        {
            _s.StuckLast = 0;
            return 0;
        }

        Vector basePos = _s.Origin;

        // The world-or-BSP nudge loop is CLIENT ONLY in stock (`!IsServer()`),
        // and this port runs a server state. It is here so the shape is
        // complete, and it is unreachable while IsServer is true.
        if (!_s.IsServer)
        {
            if (traceresult.HitWorld)
            {
                _s.StuckLast = 0;
                for (int reps = 0; reps < 54; reps++)
                {
                    Vector offset = NextStuckOffset();
                    Vector test = basePos + offset;
                    if (!TestPlayerPosition(test, CollisionGroupPlayerMovement, out traceresult))
                    {
                        _s.StuckLast = 0;
                        _s.Origin = test;
                        return 0;
                    }
                }
            }
        }

        int idx = _s.IsServer ? 0 : 1;
        float time = _w.EngineTime();

        if (_stuckCheckTime[idx] >= time - CheckStuckMinTime)
            return 1;
        _stuckCheckTime[idx] = time;

        _w.AddToTouched(traceresult, _s.Velocity);

        Vector nudge = NextStuckOffset();
        Vector testPos = basePos + nudge;

        if (!TestPlayerPosition(testPos, CollisionGroupPlayerMovement, out traceresult))
        {
            _s.StuckLast = 0;
            _s.Origin = testPos;
            return 0;
        }

        return 1;
    }

    /// <summary><c>GetRandomStuckOffsets</c> — a counter into a fixed table.</summary>
    private Vector NextStuckOffset()
    {
        int idx = _s.StuckLast++;
        return StuckTable[idx % 54];
    }

    //=========================================================================
    // Jumping.
    //=========================================================================

    private const int InBack = 1 << 4;
    private const int InForward = 1 << 3;
    private const int InMoveLeft = 1 << 9;
    private const int InMoveRight = 1 << 10;

    /// <summary><c>CheckJumpButton</c> (gamemovement.cpp:2633).</summary>
    private bool CheckJumpButton()
    {
        if (_s.DeadFlag)
        {
            _s.OldButtons |= InJump;   // don't jump again until released
            return false;
        }

        // Water jumping: decrement and return.
        if (_s.WaterJumpTime != 0f)
        {
            _s.WaterJumpTime -= _s.FrameTime;
            if (_s.WaterJumpTime < 0)
                _s.WaterJumpTime = 0;
            return false;
        }

        // In the water most of the way: swimming, not jumping.
        if (_s.WaterLevel >= 2)
        {
            SetGroundEntity(null);

            if (_s.WaterType == ContentsWater)
                _s.Velocity.Z = 100;
            else if (_s.WaterType == ContentsSlime)
                _s.Velocity.Z = 80;

            if (_s.SwimSoundTime <= 0)
            {
                _s.SwimSoundTime = 1000;
                PlaySwimSound();
            }

            return false;
        }

        if (!_s.HasGround)
        {
            _s.OldButtons |= InJump;
            return false;      // in air, so no effect
        }

        // A stasis field. `#ifndef HL2_EPISODIC`, which this build is — checked
        // against the ninja file, not assumed.
        if (_s.SlowMovement)
            return false;

        if ((_s.OldButtons & InJump) != 0)
            return false;      // don't pogo stick

        // Cannot jump while in the unduck transition.
        if (_s.Ducking && (_s.Flags & FlDucking) != 0)
            return false;

        // Still updating the eye position.
        if (_s.DuckJumpTime > 0.0f)
            return false;

        SetGroundEntity(null);

        _w.PlayStepSound(_s.Origin, _s.SurfaceDataValid ? _s.SurfaceProps : -1, 1.0f, true);
        _w.PlayerSetAnimation(PlayerJumpAnimation);

        float groundFactor = 1.0f;
        if (_s.SurfaceDataValid)
            groundFactor = _surfaceData.JumpFactor;

        // C# gets a say. Asked HERE rather than earlier because everything
        // above is the ENGINE refusing — underwater, already airborne,
        // mid-unduck — and a managed rule has nothing to add to any of those.
        var groundVelocity = new Vector(_s.Velocity.X, _s.Velocity.Y, 0.0f);
        float jumpScale = JumpScale(new JumpRequest
        {
            PlayerIndex = _s.EntIndex,
            HorizontalSpeed = MoveVec.Length(groundVelocity),
            VerticalSpeed = _s.Velocity.Z,
            Ducking = (_s.Ducking || (_s.Flags & FlDucking) != 0) ? 1 : 0,
            GroundMaterial = (SurfaceMaterial)(_s.SurfaceDataValid ? _surfaceData.Material : 0),
            CurrentTime = _s.CurTime,
        });

        if (jumpScale <= 0.0f)
        {
            // Refused. The button is marked handled so holding it does not ask
            // again every tick — which is what "don't pogo stick" does above.
            _s.OldButtons |= InJump;
            return false;
        }

        float mul = JumpVelocity(_s.CurrentGravity, JumpHeight) * jumpScale;

        float startz = _s.Velocity.Z;
        if (_s.Ducking || (_s.Flags & FlDucking) != 0)
            _s.Velocity.Z = groundFactor * mul;
        else
            _s.Velocity.Z += groundFactor * mul;

        // The HL2 arm's forward bonus, single player only: a share of the
        // forward move, clipped so it never carries the speed past that share
        // over the max speed. CHLMoveData's m_bIsSprinting is IsSprinting(),
        // whose m_fIsSprinting nothing sets in this SDK, so the share is the
        // not-sprinting one: half, or a tenth when ducked.
        if (_c.MaxClients == 1 && !_c.NoHl2Arms)
            AddJumpForwardBonus();

        FinishGravity();

        _s.OutJumpVel.Z += _s.Velocity.Z - startz;
        _s.OutStepHeight += 0.15f;

        // Set jump time. SINGLE PLAYER ONLY, and that is what makes the whole
        // duck-jump family unreachable in a multiplayer build.
        if (_c.MaxClients == 1)
        {
            _s.JumpTime = GameMovementJumpTime;
            _s.InDuckJump = true;
        }

        _s.OldButtons |= InJump;   // don't jump again until released
        return true;
    }

    /// <summary>CheckJumpButton's single-player forward bonus (gamemovement.cpp:2466).</summary>
    private void AddJumpForwardBonus()
    {
        Vector forward = MoveVec.AngleForward(_s.ViewAngles);
        forward.Z = 0;
        MoveVec.Normalize(ref forward);

        float boostPerc = !_s.Ducked ? 0.5f : 0.1f;
        float addition = MathF.Abs(_s.ForwardMove * boostPerc);
        float maxSpeed = _s.MaxSpeed + _s.MaxSpeed * boostPerc;
        float newSpeed = addition + MoveVec.Length2D(_s.Velocity);

        if (newSpeed > maxSpeed)
            addition -= newSpeed - maxSpeed;

        if (_s.ForwardMove < 0.0f)
            addition *= -1.0f;

        _s.Velocity += forward * addition;
    }

    //=========================================================================
    // Water.
    //=========================================================================

    /// <summary><c>CheckWaterJump</c> (gamemovement.cpp:1422).</summary>
    private void CheckWaterJump()
    {
        Vector forward = MoveVec.AngleForward(_s.ViewAngles);

        if (_s.WaterJumpTime != 0f)
            return;

        // Don't hop out if we just jumped in.
        if (_s.Velocity.Z < -180)
            return;

        var flatvelocity = new Vector(_s.Velocity.X, _s.Velocity.Y, 0);
        float curspeed = MoveVec.Normalize(ref flatvelocity);

        var flatforward = new Vector(forward.X, forward.Y, 0);
        MoveVec.Normalize(ref flatforward);

        // Backing into water from steps? Then don't pop forward.
        if (curspeed != 0.0 && flatvelocity.Dot(flatforward) < 0.0)
            return;

        Vector mins = GetPlayerMins();
        Vector maxs = GetPlayerMaxs();
        Vector vecStart = _s.Origin + (mins + maxs) * 0.5f;
        Vector vecEnd = vecStart + flatforward * 24.0f;

        MoveTrace tr = TracePlayerBBox(vecStart, vecEnd, PlayerSolidMask(), CollisionGroupPlayerMovement);
        if (tr.Fraction < 1.0)     // solid at waist
        {
            // Stock also refuses when the thing at waist height is a physics
            // object the player is HOLDING. That check is already dead in this
            // build — TraceEntity answers NULL for everything on the parallel
            // hierarchy, so the held-object arm never fires — and it is named
            // rather than reproduced, because reproducing it would need a
            // physics question this interface deliberately does not carry.

            vecStart.Z = _s.Origin.Z + _s.ViewOffset.Z + WaterJumpHeight;
            vecEnd = vecStart + flatforward * 24.0f;
            _s.WaterJumpVel = tr.PlaneNormal * -50.0f;

            tr = TracePlayerBBox(vecStart, vecEnd, PlayerSolidMask(), CollisionGroupPlayerMovement);
            if (tr.Fraction == 1.0)    // open at eye level
            {
                vecStart = vecEnd;
                vecEnd.Z -= 1024.0f;
                tr = TracePlayerBBox(vecStart, vecEnd, PlayerSolidMask(), CollisionGroupPlayerMovement);
                if (tr.Fraction < 1.0f && tr.PlaneNormal.Z >= 0.7)
                {
                    _s.Velocity.Z = 256.0f;
                    _s.OldButtons |= InJump;
                    _s.Flags |= FlWaterJump;
                    _s.WaterJumpTime = 2000.0f;
                }
            }
        }
    }

    /// <summary><c>WaterJump</c> (gamemovement.cpp:1511).</summary>
    private void WaterJump()
    {
        if (_s.WaterJumpTime > 10000)
            _s.WaterJumpTime = 10000;

        if (_s.WaterJumpTime == 0f)
            return;

        _s.WaterJumpTime -= 1000.0f * _s.FrameTime;

        if (_s.WaterJumpTime <= 0 || _s.WaterLevel == 0)
        {
            _s.WaterJumpTime = 0;
            _s.Flags &= ~FlWaterJump;
        }

        _s.Velocity.X = _s.WaterJumpVel.X;
        _s.Velocity.Y = _s.WaterJumpVel.Y;
    }

    /// <summary><c>WaterMove</c> (gamemovement.cpp:1534).</summary>
    private void WaterMove()
    {
        MoveVec.AngleVectors(_s.ViewAngles, out Vector forward, out Vector right, out _);

        var wishvel = new Vector(
            forward.X * _s.ForwardMove + right.X * _s.SideMove,
            forward.Y * _s.ForwardMove + right.Y * _s.SideMove,
            forward.Z * _s.ForwardMove + right.Z * _s.SideMove);

        if ((_s.Buttons & InJump) != 0)
        {
            wishvel.Z += _s.ClientMaxSpeed;
        }
        else if (_s.ForwardMove == 0 && _s.SideMove == 0 && _s.UpMove == 0)
        {
            wishvel.Z -= 60;    // drift towards the bottom
        }
        else
        {
            float upwardMovement = _s.ForwardMove * forward.Z * 2;
            upwardMovement = MathF.Min(MathF.Max(upwardMovement, 0f), _s.ClientMaxSpeed);
            wishvel.Z += _s.UpMove + upwardMovement;
        }

        Vector wishdir = wishvel;
        float wishspeed = MoveVec.Normalize(ref wishdir);

        if (wishspeed > _s.MaxSpeed)
        {
            wishvel *= _s.MaxSpeed / wishspeed;
            wishspeed = _s.MaxSpeed;
        }

        // Slow us down a bit. The 0.8 is a double literal in stock.
        wishspeed = (float)(wishspeed * 0.8);

        Vector temp = _s.Velocity;
        float speed = MoveVec.Normalize(ref temp);
        float newspeed;
        if (speed != 0)
        {
            newspeed = speed - _s.FrameTime * speed * _c.Friction * _s.SurfaceFriction;
            if (newspeed < 0.1f)
                newspeed = 0;

            _s.Velocity *= newspeed / speed;
        }
        else
        {
            newspeed = 0;
        }

        if (wishspeed >= 0.1f)
        {
            float addspeed = wishspeed - newspeed;
            if (addspeed > 0)
            {
                MoveVec.Normalize(ref wishvel);
                float accelspeed = _c.Accelerate * wishspeed * _s.FrameTime * _s.SurfaceFriction;
                if (accelspeed > addspeed)
                    accelspeed = addspeed;

                for (int i = 0; i < 3; i++)
                {
                    float deltaSpeed = accelspeed * MoveVec.At(wishvel, i);
                    MoveVec.SetAt(ref _s.Velocity, i, MoveVec.At(_s.Velocity, i) + deltaSpeed);
                    MoveVec.SetAt(ref _s.OutWishVel, i, MoveVec.At(_s.OutWishVel, i) + deltaSpeed);
                }
            }
        }

        _s.Velocity += _s.BaseVelocity;

        // Assume a stair or a slope, so press down from step height above.
        var dest = new Vector(
            _s.Origin.X + _s.FrameTime * _s.Velocity.X,
            _s.Origin.Y + _s.FrameTime * _s.Velocity.Y,
            _s.Origin.Z + _s.FrameTime * _s.Velocity.Z);

        MoveTrace pm = TracePlayerBBox(_s.Origin, dest, PlayerSolidMask(), CollisionGroupPlayerMovement);
        if (pm.Fraction == 1.0f)
        {
            Vector start = dest;
            if (_s.AllowAutoMovement)
                start.Z += _s.StepSize + 1;

            pm = TracePlayerBBox(start, dest, PlayerSolidMask(), CollisionGroupPlayerMovement);

            if (!pm.StartSolid && !pm.AllSolid)
            {
                float stepDist = pm.EndPos.Z - _s.Origin.Z;
                _s.OutStepHeight += stepDist;
                _s.Origin = pm.EndPos;
                _s.Velocity -= _s.BaseVelocity;
                return;
            }

            TryPlayerMove();
        }
        else
        {
            if (!_s.HasGround)
            {
                TryPlayerMove();
                _s.Velocity -= _s.BaseVelocity;
                return;
            }

            StepMove(dest, ref pm);
        }

        _s.Velocity -= _s.BaseVelocity;
    }

    //=========================================================================
    // Ladders.
    //=========================================================================

    /// <summary><c>GameHasLadders</c> — true, as stock.</summary>
    private static bool GameHasLadders() => true;

    private static float LadderDistance() => 2.0f;
    private static uint LadderMask() => MaskPlayerSolid;
    private static float ClimbSpeed() => MaxClimbSpeed;

    /// <summary><c>OnLadder</c> (gamemovement.cpp:3266).</summary>
    private bool OnLadder(in MoveTrace trace)
    {
        if ((trace.Contents & ContentsLadder) != 0)
            return true;

        MoveSurface surface = _w.Surface(trace.SurfaceProps);
        return surface.Valid && surface.Climbable != 0;
    }

    /// <summary><c>LadderMove</c> (gamemovement.cpp:3176).</summary>
    private bool LadderMove()
    {
        if (_s.MoveType == MoveTypeNoClip)
            return false;

        if (!GameHasLadders())
            return false;

        Vector wishdir;
        if (_s.MoveType == MoveTypeLadder)
        {
            wishdir = Vector.Zero - _s.LadderNormal;
        }
        else
        {
            if (_s.ForwardMove != 0 || _s.SideMove != 0)
            {
                wishdir = new Vector(
                    _forward.X * _s.ForwardMove + _right.X * _s.SideMove,
                    _forward.Y * _s.ForwardMove + _right.Y * _s.SideMove,
                    _forward.Z * _s.ForwardMove + _right.Z * _s.SideMove);
                MoveVec.Normalize(ref wishdir);
            }
            else
            {
                return false;    // not attempting to move, no ladder behaviour
            }
        }

        Vector end = _s.Origin + wishdir * LadderDistance();
        MoveTrace pm = TracePlayerBBox(_s.Origin, end, LadderMask(), CollisionGroupPlayerMovement);

        if (pm.Fraction == 1.0f || !OnLadder(pm))
            return false;

        _s.MoveType = MoveTypeLadder;
        _s.MoveCollide = MoveCollideDefault;
        _s.LadderNormal = pm.PlaneNormal;

        Vector floor = _s.Origin;
        floor.Z += GetPlayerMins().Z - 1;

        // enginetrace->GetPointContents, UNCACHED — stock does not route this
        // one through GetPointContentsCached, and the slot -1 says so.
        bool onFloor = _w.PointContents(floor, -1) == ContentsSolid || _s.HasGround;

        _s.EntityGravity = 0;

        float climbSpeed = ClimbSpeed();
        float forwardSpeed = 0, rightSpeed = 0;

        if ((_s.Buttons & InBack) != 0) forwardSpeed -= climbSpeed;
        if ((_s.Buttons & InForward) != 0) forwardSpeed += climbSpeed;
        if ((_s.Buttons & InMoveLeft) != 0) rightSpeed -= climbSpeed;
        if ((_s.Buttons & InMoveRight) != 0) rightSpeed += climbSpeed;

        if ((_s.Buttons & InJump) != 0)
        {
            _s.MoveType = MoveTypeWalk;
            _s.MoveCollide = MoveCollideDefault;
            _s.Velocity = pm.PlaneNormal * 270f;
        }
        else
        {
            if (forwardSpeed != 0 || rightSpeed != 0)
            {
                var velocity = new Vector(
                    _forward.X * forwardSpeed + rightSpeed * _right.X,
                    _forward.Y * forwardSpeed + rightSpeed * _right.Y,
                    _forward.Z * forwardSpeed + rightSpeed * _right.Z);

                // Perpendicular in the ladder plane.
                var tmp = new Vector(0, 0, 1);
                Vector perp = tmp.Cross(pm.PlaneNormal);
                MoveVec.Normalize(ref perp);

                float normal = velocity.Dot(pm.PlaneNormal);

                // The velocity INTO the face of the ladder.
                Vector cross = pm.PlaneNormal * normal;

                // The player's additional velocity.
                Vector lateral = velocity - cross;

                // Turn the velocity into the face of the ladder into velocity
                // roughly vertically perpendicular to that face. It IS possible
                // to face up and move down — by design, says stock.
                tmp = pm.PlaneNormal.Cross(perp);

                _s.Velocity = lateral + tmp * -normal;

                if (onFloor && normal > 0)   // on ground moving away from the ladder
                    _s.Velocity += pm.PlaneNormal * MaxClimbSpeed;
            }
            else
            {
                _s.Velocity = Vector.Zero;
            }
        }

        return true;
    }

    //=========================================================================
    // Ducking.
    //=========================================================================

    /// <summary><c>SetDuckedEyeOffset</c> (gamemovement.cpp:4717).</summary>
    private void SetDuckedEyeOffset(float duckFraction)
    {
        Vector duckHullMin = GetPlayerMins(true);
        Vector standHullMin = GetPlayerMins(false);

        float more = duckHullMin.Z - standHullMin.Z;

        Vector duckViewOffset = GetPlayerViewOffset(true);
        Vector standViewOffset = GetPlayerViewOffset(false);

        Vector temp = _s.ViewOffset;
        temp.Z = (duckViewOffset.Z - more) * duckFraction + standViewOffset.Z * (1 - duckFraction);
        _s.ViewOffset = temp;
    }

    /// <summary><c>HandleDuckingSpeedCrop</c> (gamemovement.cpp:4738).</summary>
    private void HandleDuckingSpeedCrop()
    {
        if ((_speedCropped & SpeedCroppedDuck) == 0 &&
            (_s.Flags & FlDucking) != 0 && _s.HasGround)
        {
            // C# says what a crouch costs; stock's 0.33333333 is a literal here
            // with no convar behind it.
            float frac = _c.DuckSpeedScale;
            _s.ForwardMove *= frac;
            _s.SideMove *= frac;
            _s.UpMove *= frac;
            _speedCropped |= SpeedCroppedDuck;
        }
    }

    /// <summary><c>FixPlayerCrouchStuck</c> (gamemovement.cpp:4479).</summary>
    private void FixPlayerCrouchStuck(bool upward)
    {
        int direction = upward ? 1 : 0;

        if (!TestPlayerPosition(_s.Origin, CollisionGroupPlayerMovement, out _))
            return;

        Vector test = _s.Origin;
        for (int i = 0; i < 36; i++)
        {
            Vector org = _s.Origin;
            org.Z += direction;
            _s.Origin = org;
            if (!TestPlayerPosition(_s.Origin, CollisionGroupPlayerMovement, out _))
                return;
        }

        _s.Origin = test;   // failed
    }

    /// <summary>
    /// The origin an unduck would move to — the body <c>CanUnduck</c> and
    /// <c>FinishUnDuck</c> share verbatim in stock.
    /// </summary>
    private Vector UnduckOrigin()
    {
        Vector newOrigin = _s.Origin;

        if (_s.HasGround)
        {
            Vector duckMin = GetPlayerMins(true);
            Vector standMin = GetPlayerMins(false);
            newOrigin = new Vector(
                newOrigin.X + (duckMin.X - standMin.X),
                newOrigin.Y + (duckMin.Y - standMin.Y),
                newOrigin.Z + (duckMin.Z - standMin.Z));
        }
        else
        {
            // In the air and letting go of crouch: offset the origin to make up
            // for uncrouching.
            Vector hullSizeNormal = GetPlayerMaxs(false) - GetPlayerMins(false);
            Vector hullSizeCrouch = GetPlayerMaxs(true) - GetPlayerMins(true);
            Vector viewDelta = hullSizeNormal - hullSizeCrouch;
            viewDelta = Vector.Zero - viewDelta;
            newOrigin += viewDelta;
        }

        return newOrigin;
    }

    /// <summary><c>CanUnduck</c> (gamemovement.cpp:4506).</summary>
    private bool CanUnduck()
    {
        Vector newOrigin = UnduckOrigin();

        // The hull for THIS trace is the standing one, which stock arranges by
        // clearing m_bDucked around the call and putting it back.
        bool saveDucked = _s.Ducked;
        _s.Ducked = false;
        MoveTrace trace = TracePlayerBBox(_s.Origin, newOrigin, PlayerSolidMask(), CollisionGroupPlayerMovement);
        _s.Ducked = saveDucked;

        return !trace.StartSolid && trace.Fraction == 1.0f;
    }

    /// <summary><c>FinishUnDuck</c> (gamemovement.cpp:4545).</summary>
    private void FinishUnDuck()
    {
        Vector newOrigin = UnduckOrigin();

        _s.Ducked = false;
        _s.Flags &= ~FlDucking;
        _s.Ducking = false;
        _s.InDuckJump = false;
        _s.ViewOffset = GetPlayerViewOffset(false);
        _s.DuckTime = 0;

        _s.Origin = newOrigin;

        _w.ResetDuckLatched();
        CategorizePosition();
    }

    /// <summary><c>UpdateDuckJumpEyeOffset</c> (gamemovement.cpp:4589).</summary>
    private void UpdateDuckJumpEyeOffset()
    {
        if (_s.DuckJumpTime != 0.0f)
        {
            float duckMilliseconds = MathF.Max(0.0f, GameMovementDuckTime - _s.DuckJumpTime);
            float duckSeconds = duckMilliseconds / GameMovementDuckTime;
            if (duckSeconds > TimeToUnduck)
            {
                _s.DuckJumpTime = 0.0f;
                SetDuckedEyeOffset(0.0f);
            }
            else
            {
                float duckFraction = MoveVec.SimpleSpline(1.0f - duckSeconds / TimeToUnduck);
                SetDuckedEyeOffset(duckFraction);
            }
        }
    }

    /// <summary><c>FinishUnDuckJump</c> (gamemovement.cpp:4611).</summary>
    private void FinishUnDuckJump(in MoveTrace trace)
    {
        Vector newOrigin = _s.Origin;

        Vector hullSizeNormal = GetPlayerMaxs(false) - GetPlayerMins(false);
        Vector hullSizeCrouch = GetPlayerMaxs(true) - GetPlayerMins(true);
        Vector viewDelta = hullSizeNormal - hullSizeCrouch;

        float deltaZ = viewDelta.Z;
        viewDelta.Z *= trace.Fraction;
        deltaZ -= viewDelta.Z;

        _s.Flags &= ~FlDucking;
        _s.Ducked = false;
        _s.Ducking = false;
        _s.InDuckJump = false;
        _s.DuckTime = 0.0f;
        _s.DuckJumpTime = 0.0f;
        _s.JumpTime = 0.0f;

        Vector viewOffset = GetPlayerViewOffset(false);
        viewOffset.Z -= deltaZ;
        _s.ViewOffset = viewOffset;

        _s.Origin = newOrigin - viewDelta;

        CategorizePosition();
    }

    /// <summary><c>FinishDuck</c> (gamemovement.cpp:4647).</summary>
    private void FinishDuck()
    {
        if ((_s.Flags & FlDucking) != 0)
            return;

        _s.Flags |= FlDucking;
        _s.Ducked = true;
        _s.Ducking = false;

        _s.ViewOffset = GetPlayerViewOffset(true);

        // "HACKHACK - Fudge for collision bug - no time to fix this properly".
        if (_s.HasGround)
        {
            Vector duckMin = GetPlayerMins(true);
            Vector standMin = GetPlayerMins(false);
            for (int i = 0; i < 3; i++)
            {
                Vector org = _s.Origin;
                MoveVec.SetAt(ref org, i,
                    MoveVec.At(org, i) - (MoveVec.At(duckMin, i) - MoveVec.At(standMin, i)));
                _s.Origin = org;
            }
        }
        else
        {
            Vector hullSizeNormal = GetPlayerMaxs(false) - GetPlayerMins(false);
            Vector hullSizeCrouch = GetPlayerMaxs(true) - GetPlayerMins(true);
            Vector viewDelta = hullSizeNormal - hullSizeCrouch;
            _s.Origin += viewDelta;
            _w.ResetDuckLatched();
        }

        FixPlayerCrouchStuck(true);
        CategorizePosition();
    }

    /// <summary><c>StartUnDuckJump</c> (gamemovement.cpp:4690).</summary>
    private void StartUnDuckJump()
    {
        _s.Flags |= FlDucking;
        _s.Ducked = true;
        _s.Ducking = false;

        _s.ViewOffset = GetPlayerViewOffset(true);

        Vector hullSizeNormal = GetPlayerMaxs(false) - GetPlayerMins(false);
        Vector hullSizeCrouch = GetPlayerMaxs(true) - GetPlayerMins(true);
        Vector viewDelta = hullSizeNormal - hullSizeCrouch;
        _s.Origin += viewDelta;

        FixPlayerCrouchStuck(true);
        CategorizePosition();
    }

    /// <summary><c>CanUnDuckJump</c> (gamemovement.cpp:4759).</summary>
    private bool CanUnDuckJump(out MoveTrace trace)
    {
        Vector vecEnd = _s.Origin;
        vecEnd.Z -= 36.0f;      // "This will have to change if bounding hull change!"

        trace = TracePlayerBBox(_s.Origin, vecEnd, PlayerSolidMask(), CollisionGroupPlayerMovement);
        if (trace.Fraction < 1.0f)
        {
            vecEnd.Z = _s.Origin.Z + (-36.0f * trace.Fraction);

            bool wasDucked = _s.Ducked;
            _s.Ducked = false;
            MoveTrace traceUp = TracePlayerBBox(vecEnd, vecEnd, PlayerSolidMask(), CollisionGroupPlayerMovement);
            _s.Ducked = wasDucked;
            if (!traceUp.StartSolid)
                return true;
        }

        return false;
    }

    /// <summary><c>Duck</c> (gamemovement.cpp:4786).</summary>
    private void Duck()
    {
        int buttonsChanged = _s.OldButtons ^ _s.Buttons;
        int buttonsPressed = buttonsChanged & _s.Buttons;
        int buttonsReleased = buttonsChanged & _s.OldButtons;

        bool inAir = !_s.HasGround;
        bool inDuck = (_s.Flags & FlDucking) != 0;
        bool duckJump = _s.JumpTime > 0.0f;
        bool duckJumpTime = _s.DuckJumpTime > 0.0f;

        if ((_s.Buttons & InDuck) != 0)
            _s.OldButtons |= InDuck;
        else
            _s.OldButtons &= ~InDuck;

        if (IsDead())
            return;

        HandleDuckingSpeedCrop();

        // "Assumes we never rerun commands on the server." On the client this
        // is prediction->IsFirstTimePredicted(), which only gates the eye-height
        // repair at the very bottom.
        bool firstTimePredicted = _s.IsServer || _s.FirstRunOfFunctions;

        if ((_s.Buttons & InDuck) != 0 || _s.Ducking || inDuck || duckJump)
        {
            // DUCK
            if ((_s.Buttons & InDuck) != 0 || duckJump)
            {
                // The X360 crouch-hint block is gated on IsX360(), which is
                // constant-false on every platform this mod ships.

                if ((buttonsPressed & InDuck) != 0 && !inDuck && !duckJump && !duckJumpTime)
                {
                    _s.DuckTime = GameMovementDuckTime;
                    _s.Ducking = true;
                }

                if (_s.Ducking && !duckJump && !duckJumpTime)
                {
                    float duckMilliseconds = MathF.Max(0.0f, GameMovementDuckTime - _s.DuckTime);
                    float duckSeconds = duckMilliseconds * 0.001f;

                    if (duckSeconds > TimeToDuck || inDuck || inAir)
                    {
                        FinishDuck();
                    }
                    else
                    {
                        float duckFraction = MoveVec.SimpleSpline(duckSeconds / TimeToDuck);
                        SetDuckedEyeOffset(duckFraction);
                    }
                }

                if (duckJump)
                {
                    // Make the bounding box small immediately.
                    if (!inDuck)
                    {
                        StartUnDuckJump();
                    }
                    else
                    {
                        // Crouch override.
                        if ((_s.Buttons & InDuck) == 0)
                        {
                            if (CanUnDuckJump(out MoveTrace trace))
                            {
                                FinishUnDuckJump(trace);
                                _s.DuckJumpTime = GameMovementTimeToUnduck * (1.0f - trace.Fraction)
                                                + GameMovementTimeToUnduckInv;
                            }
                        }
                    }
                }
            }
            // UNDUCK (or attempt to)
            else
            {
                if (_s.InDuckJump)
                {
                    if ((_s.Buttons & InDuck) == 0)
                    {
                        if (CanUnDuckJump(out MoveTrace trace))
                        {
                            FinishUnDuckJump(trace);

                            // STOCK ASYMMETRY, KEPT: this arm guards the timer
                            // write with `trace.fraction < 1.0f` and the arm
                            // above does not.
                            if (trace.Fraction < 1.0f)
                            {
                                _s.DuckJumpTime = GameMovementTimeToUnduck * (1.0f - trace.Fraction)
                                                + GameMovementTimeToUnduckInv;
                            }
                        }
                    }
                    else
                    {
                        _s.InDuckJump = false;
                    }
                }

                if (duckJumpTime)
                    return;

                // NOTE: when not on the ground, you can always unduck.
                if (_s.AllowAutoMovement || inAir || _s.Ducking)
                {
                    if ((buttonsReleased & InDuck) != 0)
                    {
                        if (inDuck && !duckJump)
                        {
                            _s.DuckTime = GameMovementDuckTime;
                        }
                        else if (_s.Ducking && !_s.Ducked)
                        {
                            // Invert the time if released before fully ducked.
                            float unduckMilliseconds = 1000.0f * TimeToUnduck;
                            float duckMilliseconds = 1000.0f * TimeToDuck;
                            float elapsedMilliseconds = GameMovementDuckTime - _s.DuckTime;

                            float fracDucked = elapsedMilliseconds / duckMilliseconds;
                            float remainingUnduckMilliseconds = fracDucked * unduckMilliseconds;

                            _s.DuckTime = GameMovementDuckTime - unduckMilliseconds
                                        + remainingUnduckMilliseconds;
                        }
                    }

                    if (CanUnduck())
                    {
                        if (_s.Ducking || _s.Ducked)
                        {
                            float duckMilliseconds = MathF.Max(0.0f, GameMovementDuckTime - _s.DuckTime);
                            float duckSeconds = duckMilliseconds * 0.001f;

                            if (duckSeconds > TimeToUnduck || (inAir && !duckJump))
                            {
                                FinishUnDuck();
                            }
                            else
                            {
                                float duckFraction = MoveVec.SimpleSpline(1.0f - duckSeconds / TimeToUnduck);
                                SetDuckedEyeOffset(duckFraction);
                                _s.Ducking = true;
                            }
                        }
                    }
                    else
                    {
                        // Still under something. Reset the timer so we unduck
                        // once we leave the tunnel.
                        if (_s.DuckTime != GameMovementDuckTime)
                        {
                            SetDuckedEyeOffset(1.0f);
                            _s.DuckTime = GameMovementDuckTime;
                            _s.Ducked = true;
                            _s.Ducking = false;
                            _s.Flags |= FlDucking;
                        }
                    }
                }
            }
        }
        // "HACK: (jimd 5/25/2006) we have a reoccuring bug where the player's
        // view height gets left at the ducked height while standing." This
        // senses the problem and restores the eye.
        else if (firstTimePredicted && !IsDead() && !IsObserver() && !_s.InAVehicle)
        {
            float offsetDelta = _s.ViewOffset.Z - GetPlayerViewOffset(false).Z;
            if (_s.DuckJumpTime == 0.0f && MathF.Abs(offsetDelta) > 0.1)
                SetDuckedEyeOffset(0.0f);
        }
    }
}

/// <summary>
/// <see cref="MoveConstants"/>' stock values.
///
/// <para>
/// A class of its own rather than a static member of the constants since
/// plan_shim_areas_split phase 4: the constants are in a seam's signature, so
/// they live with the seam in an interface assembly, which holds only data, and
/// a static field's initializer is a static constructor.
/// </para>
/// </summary>
public static class MoveConstantDefaults
{
    /// <summary>Stock's numbers, so a state with no constants pushed still moves like Half-Life.</summary>
    public static readonly MoveConstants Stock = new()
    {
        Friction = 4f, Accelerate = 10f, AirAccelerate = 10f, StopSpeed = 75f,
        MaxVelocity = 3500f, MaxSpeed = 320f, Bounce = 0f, RollAngle = 0f,
        RollSpeed = 200f, NoClipSpeed = 5f, NoClipAccelerate = 5f,
        SpecSpeed = 3f, SpecAccelerate = 5f,
        AirSpeedCap = 30f, DuckSpeedScale = 0.33333333f,
        Optimizations = true, SpecNoClip = true, MaxClients = 2,
        TickInterval = 0.015f,
        View = new Vector(0f, 0f, 64f),
        HullMin = new Vector(-16f, -16f, 0f), HullMax = new Vector(16f, 16f, 72f),
        DuckHullMin = new Vector(-16f, -16f, 0f), DuckHullMax = new Vector(16f, 16f, 36f),
        DuckView = new Vector(0f, 0f, 28f),
        ObsHullMin = new Vector(-10f, -10f, -10f), ObsHullMax = new Vector(10f, 10f, 10f),
        DeadViewHeight = new Vector(0f, 0f, 14f),
    };
}
