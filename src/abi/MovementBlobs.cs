//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// How movement feels, as the native loop reads it.
///
/// Must match SourceSharpMovementParams in sourcesharp.h, in order.
///
/// <para>
/// A parameter block, and it stays one even though the loop that reads it is
/// managed now. The argument that put it here was cost — the movement loop runs
/// on the client for every unacknowledged command and again on the server, so a
/// managed call for a constant would be paid every one of those times. Step 3e
/// changed which side the loop runs on, not that: these are numbers a mod picks
/// once, and they are still read out of memory rather than asked for.
/// </para>
///
/// <para>
/// The <see cref="GameMovement"/> port further down this file is the loop
/// itself; <see cref="MoveConstants"/> is what it actually reads, and the two
/// are separate because this one crosses to C as
/// <c>SourceSharpMovementParams</c> and that one does not cross at all.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MovementParams
{
    /// <summary>How high a jump reaches, in units.</summary>
    public float JumpHeight;

    public float Gravity;
    public float Friction;
    public float Accelerate;
    public float AirAccelerate;
    public float StopSpeed;
    public float MaxVelocity;
    public float StepSize;

    /// <summary>
    /// How much a player may steer while airborne, in units per second.
    ///
    /// The deathmatch movement knob. Stock is a hardcoded 30 behind
    /// CGameMovement::GetAirSpeedCap — a virtual with no convar and nobody
    /// overriding it — and it is what decides whether air strafing and
    /// bunny-hopping work at all.
    /// </summary>
    public float AirSpeedCap;

    /// <summary>What fraction of their speed a crouching player keeps.</summary>
    public float DuckSpeedScale;

    /// <summary>
    /// How fast a player runs. ONE speed, not three: walking and sprinting are
    /// CHL2_Player states, and this build's player derives straight from
    /// CBasePlayer.
    /// </summary>
    public float MaxSpeed;

    /// <summary>
    /// The hulls — how big a player is, where their eye is, and what all three
    /// become when they crouch. Nine vectors, flattened to twenty-seven floats.
    ///
    /// <para>
    /// These were nine <c>Vector</c> literals in a native
    /// <c>static CViewVectors g_DefaultViewVectors</c>, compiled once into
    /// <c>client.so</c> and once into <c>server.so</c>. They reach native code
    /// through <c>shareddefs.h</c>'s <c>VEC_HULL_MIN</c> / <c>VEC_DUCK_VIEW</c>
    /// macros, which expand to <c>g_pGameRules->GetViewVectors()-&gt;m_vHullMin</c>
    /// and are read inside the movement loop, per command, on both sides — so
    /// they belong in the parameter block rather than behind a slot, for the
    /// reason the whole block exists.
    /// </para>
    ///
    /// <para>
    /// Flattened rather than typed as <c>Vector</c>: this struct crosses to C as
    /// <c>SourceSharpMovementParams</c> and a <c>Vector</c>-typed field would be
    /// blittable by luck rather than by declaration, which is the same rule
    /// <c>MoveStateBlob</c>'s <c>CvViewX/Y/Z</c> already follow.
    /// </para>
    /// </summary>
    public float ViewX, ViewY, ViewZ;

    /// <inheritdoc cref="ViewX"/>
    public float HullMinX, HullMinY, HullMinZ;

    /// <inheritdoc cref="ViewX"/>
    public float HullMaxX, HullMaxY, HullMaxZ;

    /// <inheritdoc cref="ViewX"/>
    public float DuckHullMinX, DuckHullMinY, DuckHullMinZ;

    /// <inheritdoc cref="ViewX"/>
    public float DuckHullMaxX, DuckHullMaxY, DuckHullMaxZ;

    /// <inheritdoc cref="ViewX"/>
    public float DuckViewX, DuckViewY, DuckViewZ;

    /// <inheritdoc cref="ViewX"/>
    public float ObsHullMinX, ObsHullMinY, ObsHullMinZ;

    /// <inheritdoc cref="ViewX"/>
    public float ObsHullMaxX, ObsHullMaxY, ObsHullMaxZ;

    /// <inheritdoc cref="ViewX"/>
    public float DeadViewHeightX, DeadViewHeightY, DeadViewHeightZ;

    /// <summary>
    /// The hulls as vectors, for callers that want to read or state them that
    /// way. Setting one writes the three floats it is made of.
    /// </summary>
    public Vector View
    {
        get => new(ViewX, ViewY, ViewZ);
        set { ViewX = value.X; ViewY = value.Y; ViewZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector HullMin
    {
        get => new(HullMinX, HullMinY, HullMinZ);
        set { HullMinX = value.X; HullMinY = value.Y; HullMinZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector HullMax
    {
        get => new(HullMaxX, HullMaxY, HullMaxZ);
        set { HullMaxX = value.X; HullMaxY = value.Y; HullMaxZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector DuckHullMin
    {
        get => new(DuckHullMinX, DuckHullMinY, DuckHullMinZ);
        set { DuckHullMinX = value.X; DuckHullMinY = value.Y; DuckHullMinZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector DuckHullMax
    {
        get => new(DuckHullMaxX, DuckHullMaxY, DuckHullMaxZ);
        set { DuckHullMaxX = value.X; DuckHullMaxY = value.Y; DuckHullMaxZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector DuckView
    {
        get => new(DuckViewX, DuckViewY, DuckViewZ);
        set { DuckViewX = value.X; DuckViewY = value.Y; DuckViewZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector ObsHullMin
    {
        get => new(ObsHullMinX, ObsHullMinY, ObsHullMinZ);
        set { ObsHullMinX = value.X; ObsHullMinY = value.Y; ObsHullMinZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector ObsHullMax
    {
        get => new(ObsHullMaxX, ObsHullMaxY, ObsHullMaxZ);
        set { ObsHullMaxX = value.X; ObsHullMaxY = value.Y; ObsHullMaxZ = value.Z; }
    }

    /// <inheritdoc cref="View"/>
    public Vector DeadViewHeight
    {
        get => new(DeadViewHeightX, DeadViewHeightY, DeadViewHeightZ);
        set { DeadViewHeightX = value.X; DeadViewHeightY = value.Y; DeadViewHeightZ = value.Z; }
    }
}

/// <summary>
/// Which kinds of damage behave which way — the mirror of
/// <c>SourceSharpDamageRules</c> in <c>sourcesharp.h</c>.
///
/// <para>
/// Five bitmasks of <see cref="DamageType"/>, stated once at startup for the
/// same reason the movement parameters are: they are constants, the native side
/// reads them from damage paths that run per hit, and a crossing per hit to
/// fetch a value that never changes is what <c>plan_server_all.md</c> §3 says to
/// design away. <see cref="SourceSharp.Server.DamageRules"/> holds the values
/// and the reasoning.
/// </para>
///
/// <para>
/// <c>int</c> rather than <see cref="DamageType"/> on the fields: this struct
/// crosses to C as five <c>int32_t</c>, and an enum-typed field would be
/// blittable by luck rather than by declaration.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DamageRuleMasks
{
    /// <summary>Applied a tick at a time rather than all at once.</summary>
    public int TimeBased;

    /// <summary>
    /// Need not supply a physics force and position. Includes
    /// <see cref="TimeBased"/>, which is stock's composition rather than a
    /// repeated list.
    /// </summary>
    public int NoPhysicsForce;

    /// <summary>Violent enough to gib a corpse.</summary>
    public int ShouldGibCorpse;

    /// <summary>Draws no blood.</summary>
    public int ShouldNotBleed;

    /// <summary>The HUD has directional art for it.</summary>
    public int ShowOnHud;
}

/// <summary>
/// Everything a jump decision is given, in one crossing.
///
/// Must match SourceSharpJumpRequest in sourcesharp.h, in order.
///
/// <para>
/// One struct rather than a handle to ask questions of, and that shape is what
/// measuring the boundary actually taught: one crossing costs about 7ns, which
/// is nothing — but a decision that reached back for six more facts would pay
/// it seven times. The cost was never the crossing; it was the number of them.
/// </para>
///
/// <para>
/// <b>This runs on both sides.</b> Movement is predicted, so a rule that
/// depends on anything not in this struct — a counter only the server keeps, a
/// random number, the time of day — will disagree with the client and show up
/// as rubber-banding rather than as a wrong answer.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct JumpRequest
{
    /// <summary>
    /// The player, by entity index rather than by handle: a server handle
    /// carries a serial the client's entity list knows nothing about, and this
    /// question is asked on both sides.
    /// </summary>
    public int PlayerIndex;

    /// <summary>Speed along the ground, which is what a rule about bunny-hopping needs.</summary>
    public float HorizontalSpeed;

    /// <summary>Straight up, before the jump is added. Negative while falling.</summary>
    public float VerticalSpeed;

    public int Ducking;

    /// <summary>What the ground is made of.</summary>
    public SurfaceMaterial GroundMaterial;

    public float CurrentTime;
}

/// <summary>
/// The rules' constants, mirroring <c>SourceSharpGameRulesPolicy</c>.
///
/// <para>
/// Pushed once at startup rather than asked for per call, which is the same
/// trade <see cref="DamageRuleMasks"/> makes and for the same reason: every
/// field is a number a mod picks once, and several of the methods that read
/// them run per item, per weapon or per kill. A managed call for a value
/// decided at startup is a boundary crossing to be told the same thing again.
/// </para>
///
/// <para>
/// Only CONSTANTS belong here. Anything whose answer depends on who is asking —
/// fall damage, whether a player may connect, what two players are to each
/// other — is a managed call, because a table cannot hold an answer that is
/// different every time.
/// </para>
///
/// <para>
/// <c>int</c> rather than <c>bool</c> on the flags: this crosses to C as
/// <c>int32_t</c>, and C#'s <c>bool</c> is one byte.
/// </para>
///
/// <para>
/// <b>Two fields, down from seventeen.</b> The other fifteen fed native methods
/// that nothing in this build called, and those methods are gone. A constant
/// whose only reader was a deleted method is dead data.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GameRulesPolicyBlob
{
    /// <summary>Whether this is a multiplayer game.</summary>
    public int Multiplayer;

    // AllowDamage went at plan_shim.md §5 3e: the native method that read it,
    // CSourceSharpGameRules::AllowDamage, had no caller in any compiled
    // translation unit -- baseentity.cpp, which held its one call site, is in
    // neither vpc. A field nothing reads is dead data, which is why this struct
    // is two fields and was seventeen.
}

/// <summary>
/// What a swept-hull player trace answered — the whole of <c>trace_t</c> that
/// the movement loop actually reads, and nothing else.
///
/// <para>
/// The membership rule is counted rather than guessed: a grep of
/// <c>gamemovement.cpp</c> for field accesses on a <c>trace_t</c> finds
/// <c>fraction</c> (36), <c>plane.normal</c> (26), <c>endpos</c> (25),
/// <c>m_pEnt</c> (12), <c>startsolid</c> (8), <c>allsolid</c> (6),
/// <c>surface.surfaceProps</c> (3), <c>contents</c> (2) and
/// <c>surface.flags</c> (1). Everything else in a <c>trace_t</c> is the
/// engine's business.
/// </para>
///
/// <para>
/// <see cref="HasGround"/> is <c>MovementGroundFromTrace</c>'s answer and NOT
/// <c>m_pEnt != NULL</c>: a static prop is in the same partition and reported
/// in the same field, has no entity behind it, and stock has therefore always
/// treated a player standing on one as standing on nothing. Both are here
/// because <c>CategorizePosition</c> tests the raw pointer and
/// <c>SetGroundEntity</c> tests the resolved one.
/// </para>
/// </summary>
public struct MoveTrace
{
    public float Fraction;
    public Vector EndPos;
    public Vector PlaneNormal;
    public float PlaneDist;
    public bool StartSolid;
    public bool AllSolid;
    public int SurfaceProps;
    public int SurfaceFlags;
    public int Contents;

    /// <summary>Whether what was hit resolves to an entity of either hierarchy.</summary>
    public bool HasGround;

    /// <summary>How fast that entity is moving, or zero.</summary>
    public Vector GroundVelocity;

    /// <summary><c>m_pEnt != NULL</c> — true for a static prop, unlike <see cref="HasGround"/>.</summary>
    public bool HasEntity;

    /// <summary><c>trace_t::DidHitWorld</c>.</summary>
    public bool HitWorld;

    /// <summary>A trace that went the whole way and hit nothing.</summary>
    public static MoveTrace Clear(in Vector end) => new()
    {
        Fraction = 1f,
        EndPos = end,
        PlaneNormal = Vector.Zero,
    };
}

/// <summary>
/// One surfaceprop, as the movement loop reads it: the four <c>game</c> fields
/// it uses and the physics friction behind them.
/// </summary>
public struct MoveSurface
{
    public bool Valid;
    public float Friction;
    public int Material;
    public int Climbable;
    public float MaxSpeedFactor;
    public float JumpFactor;
    /// <summary>The material's density (physics_surfaceprops), kg per cubic metre.</summary>
    public float Density;
    /// <summary>audio.roughnessFactor: how rough this surface sounds to a scrape against it.</summary>
    public float RoughnessFactor;
    /// <summary>audio.roughThreshold: a hit surface rougher than this scrapes rough.</summary>
    public float RoughThreshold;
}
