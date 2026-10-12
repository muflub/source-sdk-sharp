//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Everything the shadow's state machine reads —
/// <c>SourceSharpPlayerShadowState</c>, mirrored byte for byte.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlayerShadowState
{
    public uint StructSize;

    /// <summary>Whether the player has a shadow at all.</summary>
    public int HasShadow;

    /// <summary>
    /// Whether the shadow is switched on. WRITTEN BY <see cref="PlayerShadow.Step"/>
    /// and deliberately not by the probe: the declaration is managed, so native
    /// has no convar to read for it. See <c>PlayerShadowConVar</c>.
    /// </summary>
    public int Enabled;

    public Vector Origin;
    public Vector Velocity;

    /// <summary>Stock's <c>m_oldOrigin</c> — the previous command's.</summary>
    public Vector OldOrigin;

    /// <summary><c>CMoveData::m_outWishVel</c>, as the move left it.</summary>
    public Vector OutWishVel;

    /// <summary><c>CMoveData::m_outStepHeight</c>, as the move left it.</summary>
    public float OutStepHeight;

    public float MaxSpeed;

    /// <summary><c>FL_ONGROUND</c> and <c>FL_DUCKING</c>.</summary>
    public int Flags;

    public int MoveType;

    /// <summary>Which hull the solver currently holds.</summary>
    public int State;

    /// <summary>
    /// Whether <see cref="ShadowOrigin"/> means anything. A NOCLIP player has
    /// no active hull and so no position to read back, and a zero vector there
    /// would be a real answer meaning "the shadow is at the world origin" —
    /// which is a snap every tick.
    /// </summary>
    public int HasShadowPosition;

    public Vector ShadowOrigin;

    /// <summary>
    /// <see cref="PlayerShadow.ContactController"/> and
    /// <see cref="PlayerShadow.ContactTouched"/>.
    /// </summary>
    public int ContactBits;

    /// <summary>RAW. The clamp stock applies is a decision and is applied here.</summary>
    public float FrameTime;

    public float TickInterval;

    /// <summary>Which command of the queue this is — stock's loop counter.</summary>
    public int CommandIndex;

    /// <summary>The ground's physics mass: 0 when there is no ground, or it is not a moveable vphysics object.</summary>
    public float GroundPhysicsMass;

    /// <summary>
    /// The solver's side, what VPhysicsShadowUpdate reads (physics stock parity
    /// B7): the controller has a position for this tick (GetShadowPosition &gt; 0).
    /// </summary>
    public int PhysicsUpdated;

    /// <summary>The velocity the shadow achieved (GetShadowVelocity).</summary>
    public Vector ShadowVelocity;

    /// <summary>The controller was frozen this tick (WasFrozen).</summary>
    public int WasFrozen;

    /// <summary>A MOVETYPE_VPHYSICS mover is penetrating the player.</summary>
    public int PenetratingMover;

    /// <summary>The shadow's own mass.</summary>
    public float ShadowMass;
}

/// <summary>
/// What native is asked to do to the shadow — <c>SourceSharpPlayerShadowPlan</c>,
/// mirrored byte for byte.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlayerShadowPlan
{
    public uint StructSize;

    /// <summary>
    /// Whether there is anything to do. Distinct from a plan of all zeroes,
    /// which would be a real instruction to aim the shadow at the world origin.
    /// </summary>
    public int Valid;

    /// <summary>Take the body out of the solver, now.</summary>
    public int Destroy;

    public int State;
    public int StateChanged;
    public Vector StateOrigin;
    public Vector StateVelocity;

    public int StepUp;
    public float StepHeight;

    public int Snap;
    public Vector SnapOrigin;
    public Vector SnapVelocity;

    public Vector Position;
    public Vector WishVelocity;
    public float Arrival;
    public int OnGround;

    /// <summary>
    /// Stock's <c>IsRideablePhysics</c>: the ground is a moveable vphysics
    /// object heavier than twice the shadow, so native hands the controller
    /// that object as <c>Update</c>'s ground (player.cpp:4854-4867).
    /// </summary>
    public int RideableGround;
}

/// <summary>
/// What native knows of a player and the body it holds for them --
/// <c>SourceSharpPlayerShadowFacts</c>, mirrored byte for byte
/// (plan_physics_bones_native_zero 2.5). Native reads the player's own state
/// and hands over the three vphysics pointers it holds; managed reads vphysics
/// itself and decides everything. <see cref="PlayerShadowBodies"/> builds a
/// <see cref="PlayerShadowState"/> from this and its own reads.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlayerShadowFacts
{
    public const int SizeInBytes = 192;

    /// <summary><c>SOURCESHARP_PLAYER_TOUCH_MAX</c>: the touch list's listed entries.</summary>
    public const int TouchMax = 16;

    public uint StructSize;

    /// <summary>Native holds a body (stand, crouch and controller) for this player.</summary>
    public int HasShadow;

    /// <summary>The held body was built by the environment that exists now.</summary>
    public int BodyCurrent;

    /// <summary>Which hull the solver holds (<see cref="PlayerShadow.StateWalk"/>...), -1 for none yet.</summary>
    public int State;

    public nint Stand;
    public nint Crouch;
    public nint Controller;

    /// <summary>Where the move left the player, and how they are moving.</summary>
    public Vector Origin;
    public Vector Velocity;

    /// <summary><c>CMoveData::m_outWishVel</c> / <c>m_outStepHeight</c>.</summary>
    public Vector OutWishVel;
    public float OutStepHeight;

    public float MaxSpeed;
    public int Flags;
    public int MoveType;

    /// <summary>RAW <c>gpGlobals-&gt;frametime</c>, and the tick interval.</summary>
    public float FrameTime;
    public float TickInterval;

    /// <summary>The ground entity, or the invalid handle; its move type; its own physics object, or 0.</summary>
    public uint Ground;
    public int GroundMoveType;
    public nint GroundObject;

    /// <summary>
    /// What the last command's move ran into (the move helper's touch list,
    /// one per entity): <see cref="Touched"/> counts them all, the first
    /// <see cref="TouchMax"/> are listed, and bit i of <see cref="TouchedGround"/>
    /// says entry i was the ground when touched. Managed decides stock's
    /// <c>m_touchedPhysObject</c> from it (CBasePlayer::Touch).
    /// </summary>
    public int Touched;
    public uint TouchedGround;
    public TouchList TouchedEntities;

    /// <summary>The listed entities' raw handles.</summary>
    [System.Runtime.CompilerServices.InlineArray(TouchMax)]
    public struct TouchList
    {
        private uint _first;
    }
}
