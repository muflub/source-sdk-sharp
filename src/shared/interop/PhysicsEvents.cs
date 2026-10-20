//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// What kind of thing the physics solver reported.
///
/// <para>
/// The names and the numbers are native physics_shell.h's SS_PHYSEVENT_*,
/// kept as they were when plan_physics_bones_native_zero 2.3 deleted the
/// native event ring: every producer is managed now (the vphysics callback
/// tables and PhysicsFrame), so this enum is the one definition.
/// </para>
/// </summary>
public enum PhysicsEventKind
{
    /// <summary>Nothing. A zeroed record reads as this and never as a real kind.</summary>
    None = 0,

    /// <summary>
    /// Two physics objects began touching. Not recorded today: managed's own
    /// IPhysicsCollisionEvent table (PhysicsCollisionEventBinding, B2.2) holds
    /// the pair in SolverHeldOverlaps, and the overlap diff reports the touch.
    /// </summary>
    StartTouch = 1,

    /// <summary>Two physics objects stopped touching. Not recorded today: see <see cref="StartTouch"/>.</summary>
    EndTouch = 2,

    /// <summary>Before the solver resolved an impact. Not recorded today.</summary>
    PreCollision = 3,

    /// <summary>
    /// An impact the solver has already resolved. The only kind that carries
    /// the collision tail — see <see cref="PhysicsEvent.DeltaCollisionTime"/>.
    /// Not recorded today by the native ring: PhysicsCollisionImpacts answers
    /// it in the callback and posts it to the router (B2.2).
    /// </summary>
    PostCollision = 4,

    /// <summary>
    /// Sustained sliding contact. Not recorded today by the native ring:
    /// PhysicsCollisionEventBinding posts it to the router (B2.2) with the
    /// contact point, the velocity there, the energy and both surface-property
    /// indices, for FrictionSounds' throttled sound and effect.
    /// </summary>
    Friction = 5,

    /// <summary>
    /// An object entered a fluid volume. Not recorded today by the native
    /// ring: PhysicsFluidEvents posts it from the callback (B2.2).
    /// </summary>
    FluidStart = 6,

    /// <summary>An object left a fluid volume. Not recorded today: see <see cref="FluidStart"/>.</summary>
    FluidEnd = 7,

    /// <summary>
    /// An object entered a physics trigger. Not recorded today: the binding
    /// holds the pair in SolverHeldOverlaps, as for <see cref="StartTouch"/>.
    /// </summary>
    EnterTrigger = 8,

    /// <summary>An object left a physics trigger. Not recorded today: see <see cref="EnterTrigger"/>.</summary>
    LeaveTrigger = 9,

    /// <summary>
    /// An object started simulating again. Not recorded today by the native
    /// ring: managed's own IPhysicsObjectEvent table (PhysicsObjectEventBinding)
    /// posts it to the router (plan_physics_managed B2.1).
    /// </summary>
    Wake = 10,

    /// <summary>
    /// An object went to sleep. Not recorded today by the native ring: posted
    /// by managed's IPhysicsObjectEvent table, as <see cref="Wake"/>.
    /// </summary>
    Sleep = 11,

    /// <summary>
    /// A constraint tore. Not recorded today by the native ring: managed's own
    /// IPhysicsConstraintEvent table (PhysicsConstraintEventBinding) hands the
    /// break to PhysicsConstraints, which posts it to the router with the
    /// constraint's id in Object0 (plan_physics_bones_native_zero 2.2).
    /// </summary>
    ConstraintBroken = 12,

    /// <summary>
    /// A kind this build does not know. NOT a member of the native enum: it is
    /// what <see cref="PhysicsEvent.Kind"/> answers for anything at or past
    /// <c>SS_PHYSEVENT_KIND_COUNT</c>, so a native side that grew a kind
    /// reaches a named arm here instead of falling through a switch.
    /// </summary>
    /// <summary>
    /// The solver moved this entity's object this frame: stock's per-tick
    /// VPhysicsUpdate, one per object on the active list. Recorded after the
    /// step, so it is delivered at the next drain.
    /// </summary>
    Update = 13,

    /// <summary>
    /// A player's active hull is awake after the step, and this is how hard the
    /// world squeezes it: stock's CBasePlayer::VPhysicsUpdate stress (physics
    /// stock parity B5). <see cref="PhysicsEvent.Speed"/> is the received
    /// stress, <see cref="PhysicsEvent.Energy"/> the hull's mass, and
    /// <see cref="PhysicsEvent.Flags"/> the <c>Stress*</c> bits.
    /// </summary>
    Stress = 14,

    /// <summary>
    /// PhysFrame's shadow walk: a player's active hull is awake after the step,
    /// stock's per-tick VPhysicsShadowUpdate (physics stock parity B7).
    /// </summary>
    ShadowUpdate = 15,

    /// <summary>
    /// The penetration ledger's frame handed a pair to the entity solver:
    /// debris stuck in a non-static object for over half a second, stock's
    /// COLLSTATE_TRYENTITYSOLVER (plan_physics_managed A9).
    /// <see cref="PhysicsEvent.Flags"/> carries <see cref="PhysicsEvent.SolverDebris0"/>
    /// and <see cref="PhysicsEvent.SolverDebris1"/>.
    /// </summary>
    EntitySolver = 16,

    Unknown = 17,
}

/// <summary>
/// ONE PHYSICS EVENT, in the layout native's ring recorded it.
///
/// <para>
/// Native's <c>SourceSharpPhysicsEvent_s</c> and its ring are gone
/// (plan_physics_bones_native_zero 2.3); managed's callbacks and PhysicsFrame
/// build these and the router delivers them. <c>PhysicsEventLayoutTests</c>
/// still pins the size and every offset, which is the layout the record had.
/// </para>
///
/// <para>
/// EVERYTHING IN IT IS A VALUE. The drain happens after the simulation step and
/// the step can have destroyed either entity or either object, so entities are
/// named by packed handle, objects by the shell's id table, and the collision
/// tail is copied out at record time. Nothing here is a pointer and nothing
/// here needs a call back into native to be understood.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsEvent
{
    /// <summary>Raw kind word. Read <see cref="Kind"/> instead.</summary>
    public uint KindRaw;

    /// <summary>Kind-specific flags; 0 for every kind that defines none.</summary>
    public uint Flags;

    /// <summary>Packed handle of the entity on side 0, or <see cref="NoEntity"/>.</summary>
    public uint Entity0;

    /// <summary>Packed handle of the entity on side 1, or <see cref="NoEntity"/>.</summary>
    public uint Entity1;

    /// <summary>Shell id of the physics object on side 0; 0 for none.</summary>
    public uint Object0;

    /// <summary>Shell id of the physics object on side 1; 0 for none.</summary>
    public uint Object1;

    /// <summary>Surface property index on side 0.</summary>
    public uint SurfaceProps0;

    /// <summary>Surface property index on side 1.</summary>
    public uint SurfaceProps1;

    /// <summary>Contact point, world space. Zero for a kind that has no point.</summary>
    public float PointX;

    /// <summary>Contact point, world space.</summary>
    public float PointY;

    /// <summary>Contact point, world space.</summary>
    public float PointZ;

    /// <summary>Contact normal.</summary>
    public float NormalX;

    /// <summary>Contact normal.</summary>
    public float NormalY;

    /// <summary>Contact normal.</summary>
    public float NormalZ;

    /// <summary>Impact speed, or friction energy for <see cref="PhysicsEventKind.Friction"/>.</summary>
    public float Speed;

    /// <summary>Energy, where the kind carries one.</summary>
    public float Energy;

    /// <summary>
    /// Seconds since these two objects last collided.
    /// <see cref="PhysicsEventKind.PostCollision"/> only; zero otherwise.
    /// </summary>
    public float DeltaCollisionTime;

    /// <summary>MOVETYPE_* of the owner on side 0. Collision only.</summary>
    public int MoveType0;

    /// <summary>MOVETYPE_* of the owner on side 1. Collision only.</summary>
    public int MoveType1;

    /// <summary>Mass in kg of the object on side 0. Collision only.</summary>
    public float Mass0;

    /// <summary>Mass in kg of the object on side 1. Collision only.</summary>
    public float Mass1;

    /// <summary>FVPHYSICS_* of the object on side 0. Collision only.</summary>
    public uint GameFlags0;

    /// <summary>FVPHYSICS_* of the object on side 1. Collision only.</summary>
    public uint GameFlags1;

    /// <summary>Whether the object on side 0 is static. Collision only.</summary>
    public int Static0;

    /// <summary>Whether the object on side 1 is static. Collision only.</summary>
    public int Static1;

    // THE MOTION TAIL (physics stock parity C6): what stock's
    // CalculatePhysicsImpactDamage reads, per side. Zero for other kinds.
    /// <summary>Side 0: Linear velocity before the impact (PreCollision). PostCollision only.</summary>
    public float PreVelocity0X, PreVelocity0Y, PreVelocity0Z;

    /// <summary>Side 0: Linear velocity after it. PostCollision only.</summary>
    public float PostVelocity0X, PostVelocity0Y, PostVelocity0Z;

    /// <summary>Side 0: Angular velocity before the impact. PostCollision only.</summary>
    public float PreAngularVelocity0X, PreAngularVelocity0Y, PreAngularVelocity0Z;

    /// <summary>Side 0: The object's inertia. PostCollision only.</summary>
    public float Inertia0X, Inertia0Y, Inertia0Z;

    /// <summary>Side 1: Linear velocity before the impact (PreCollision). PostCollision only.</summary>
    public float PreVelocity1X, PreVelocity1Y, PreVelocity1Z;

    /// <summary>Side 1: Linear velocity after it. PostCollision only.</summary>
    public float PostVelocity1X, PostVelocity1Y, PostVelocity1Z;

    /// <summary>Side 1: Angular velocity before the impact. PostCollision only.</summary>
    public float PreAngularVelocity1X, PreAngularVelocity1Y, PreAngularVelocity1Z;

    /// <summary>Side 1: The object's inertia. PostCollision only.</summary>
    public float Inertia1X, Inertia1Y, Inertia1Z;

    /// <summary>PostCollision's <see cref="Flags"/>: the solver will move side 0.</summary>
    public const uint FlagMoveable0 = 1u << 0;
    /// <summary>The solver will move side 1.</summary>
    public const uint FlagMoveable1 = 1u << 1;
    /// <summary>Side 0 is a shadow-controlled object (CALLBACK_SHADOW_COLLISION).</summary>
    public const uint FlagShadow0 = 1u << 2;
    /// <summary>Side 1 is a shadow-controlled object.</summary>
    public const uint FlagShadow1 = 1u << 3;
    /// <summary>The solver called it a collision (vcollisionevent_t::isCollision).</summary>
    public const uint FlagCollision = 1u << 4;
    /// <summary>The solver called it a shadow collision (isShadowCollision).</summary>
    public const uint FlagShadowCollision = 1u << 5;

    /// <summary>A Stress event's own bits: a moveable, non-friendly pusher (hasNonStaticStress).</summary>
    public const uint StressNonStatic = 1u << 0;
    /// <summary>An EntitySolver event: side 0 is debris moved by physics (IsDebris and MOVETYPE_VPHYSICS).</summary>
    public const uint SolverDebris0 = 1u << 0;
    /// <summary>An EntitySolver event: side 1 is debris moved by physics.</summary>
    public const uint SolverDebris1 = 1u << 1;
    /// <summary>A Stress event: a pusher of 500 kg or more that is not the ground (hasLargeObjectContact).</summary>
    public const uint StressLargeObject = 1u << 1;
    /// <summary>A Stress event: the hull is FVPHYSICS_PENETRATING, stuck rather than squeezed.</summary>
    public const uint StressPenetrating = 1u << 2;

    /// <summary>One side's motion, for the damage arithmetic.</summary>
    public Vector PreVelocity(int side) => side == 0 ? new(PreVelocity0X, PreVelocity0Y, PreVelocity0Z) : new(PreVelocity1X, PreVelocity1Y, PreVelocity1Z);
    /// <inheritdoc cref="PreVelocity"/>
    public Vector PostVelocity(int side) => side == 0 ? new(PostVelocity0X, PostVelocity0Y, PostVelocity0Z) : new(PostVelocity1X, PostVelocity1Y, PostVelocity1Z);
    /// <inheritdoc cref="PreVelocity"/>
    public Vector PreAngularVelocity(int side) => side == 0 ? new(PreAngularVelocity0X, PreAngularVelocity0Y, PreAngularVelocity0Z) : new(PreAngularVelocity1X, PreAngularVelocity1Y, PreAngularVelocity1Z);
    /// <inheritdoc cref="PreVelocity"/>
    public Vector Inertia(int side) => side == 0 ? new(Inertia0X, Inertia0Y, Inertia0Z) : new(Inertia1X, Inertia1Y, Inertia1Z);
    /// <summary>Whether the solver will move that side.</summary>
    public bool IsMoveable(int side) => (Flags & (side == 0 ? FlagMoveable0 : FlagMoveable1)) != 0;
    /// <summary>Whether that side is a shadow-controlled object.</summary>
    public bool IsShadow(int side) => (Flags & (side == 0 ? FlagShadow0 : FlagShadow1)) != 0;
    /// <summary>Whether the solver called it a collision: stock's VPhysicsCollision gate.</summary>
    public bool IsCollision => (Flags & FlagCollision) != 0;
    /// <summary>Whether the solver called it a shadow collision: stock's VPhysicsShadowCollision gate.</summary>
    public bool IsShadowCollision => (Flags & FlagShadowCollision) != 0;

    /// <summary>
    /// No entity on that side.
    ///
    /// <para>
    /// NOT zero. Zero is a valid packed handle — entity index 0, serial 0 — and
    /// treating it as "none" is the "raw zero is entry zero" trap. This
    /// is <c>CBaseHandle</c>'s own invalid word, and native's
    /// <c>SS_PHYSEVENT_NO_ENTITY</c>.
    /// </para>
    /// </summary>
    public const uint NoEntity = 0xFFFFFFFFu;

    /// <summary>How many bytes one record is. Native static_asserts the same number.</summary>
    public const int SizeInBytes = 196;

    /// <summary>The last kind this build knows, as a raw word.</summary>
    public const uint KindCount = 17;

    /// <summary>
    /// The kind, named — <see cref="PhysicsEventKind.Unknown"/> for anything
    /// this build has no name for, rather than a cast that would produce an
    /// enum value no switch arm matches.
    /// </summary>
    public PhysicsEventKind Kind
        => KindRaw < KindCount ? (PhysicsEventKind)KindRaw : PhysicsEventKind.Unknown;

    /// <summary>Whether side 0 names an entity at all.</summary>
    public bool HasEntity0 => Entity0 != NoEntity;

    /// <summary>Whether side 1 names an entity at all.</summary>
    public bool HasEntity1 => Entity1 != NoEntity;

    /// <summary>
    /// Whether both sides are the same entity — a ragdoll's limbs hitting each
    /// other, which stock filters on a longer repeat interval than it uses for
    /// two different entities.
    /// </summary>
    public bool IsSelfCollision => HasEntity0 && Entity0 == Entity1;

    /// <summary>The contact point.</summary>
    public Vector Point => new Vector(PointX, PointY, PointZ);

    /// <summary>The contact normal.</summary>
    public Vector Normal => new Vector(NormalX, NormalY, NormalZ);
}

/// <summary>
/// The surfaceprop table, for the collision response.
///
/// <para>
/// The impact rules decide from the MATERIAL LETTER of the two surfaces a
/// collision happened between — sky and clip make no noise, sand and dirt and
/// concrete kick up dust — and the letter lives in the engine's
/// <c>IPhysicsSurfaceProps</c>. This is the one call that reads it.
/// </para>
///
/// <para>
/// IT IS THE MOVEMENT LOOP'S SLOT, NOT A SECOND ONE. <c>MoveSurface</c> already
/// answers exactly the four <c>game</c> fields a surfaceprop carries, and a
/// slot of this lane's own would be a second declaration of one engine
/// question — the "could it be GENERATED rather than written" test, failed. The
/// name says movement only because movement asked first.
/// </para>
///
/// <para>
/// Answers <c>default</c> (<c>Valid</c> false, <c>Material</c> 0) for a
/// surfaceprop the engine does not know and when the slot is not wired, which
/// is what a build with no managed side yet looks like. A caller must read
/// <see cref="MoveSurface.Valid"/> before believing
/// <see cref="MoveSurface.Material"/>: material 0 is not a letter, so an
/// unknown surface can never be mistaken for sand.
/// </para>
/// <para>
/// One per side, over that side's own routing (plan_abi_hotreload Phase 1):
/// its <see cref="EngineSide"/> builds it and the side's composition registers
/// it, so a caller is handed its side's table rather than asking whichever side
/// the thread had active.
/// </para>
/// </summary>
public sealed class PhysicsSurfaces
{
    private readonly IMovementArea _movement;

    internal PhysicsSurfaces(IMovementArea movement)
    {
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
    }

    /// <summary>The surfaceprop at that index, or <c>default</c>.</summary>
    public unsafe MoveSurface Of(int surfaceProps)
    {
        MoveSurfaceBlob blob = default;
        if (_movement.MoveSurface(surfaceProps, &blob) == 0)
            return default;

        return new MoveSurface
        {
            Valid = blob.Valid != 0,
            Friction = blob.Friction,
            Material = blob.Material,
            Climbable = blob.Climbable,
            MaxSpeedFactor = blob.MaxSpeedFactor,
            JumpFactor = blob.JumpFactor,
            Density = blob.Density,
            RoughnessFactor = blob.RoughnessFactor,
            RoughThreshold = blob.RoughThreshold,
        };
    }
}
