//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>A screen shake a collision asked for.</summary>
/// <param name="Origin">The contact point the shake radiates from.</param>
/// <param name="Amplitude">How hard.</param>
/// <param name="Frequency">How fast.</param>
/// <param name="Duration">For how long.</param>
/// <param name="Radius">How far it carries.</param>
public readonly record struct CollisionShake(Vector Origin, float Amplitude, float Frequency,
                                             float Duration, float Radius);

/// <summary>An impact sound a collision asked for.</summary>
/// <param name="Volume">0..1, from the impact speed.</param>
/// <param name="Speed">The impact speed itself, which picks the sound.</param>
/// <param name="Channel">Body for a self-collision, Static otherwise.</param>
public readonly record struct CollisionSound(float Volume, float Speed, CollisionSoundChannel Channel);

/// <summary>Which sound channel a physics impact plays on.</summary>
public enum CollisionSoundChannel
{
    /// <summary>CHAN_BODY. Stock's choice when both sides are the same entity.</summary>
    Body = 0,

    /// <summary>CHAN_STATIC. Stock's choice for two different entities.</summary>
    Static = 1,
}

/// <summary>
/// WHAT A PHYSICS IMPACT MAKES THE GAME DO — sound, shake and dust — as
/// arithmetic over one <see cref="PhysicsEvent"/>.
///
/// <para>
/// This is <c>CBaseEntity::VPhysicsCollision</c> (game/server/baseentity.cpp:3254)
/// together with the three functions it calls in <c>game/server/physics.cpp</c>:
/// <c>PhysCollisionSound</c> (:4158), <c>PhysCollisionScreenShake</c> (:4182)
/// and <c>PhysCollisionDust</c> (:4220). Every number below is one of theirs and
/// is cited where it sits.
/// </para>
///
/// <para>
/// <b>Why it is here at all.</b> Not one of those ran for a managed entity.
/// <c>CCollisionEvent::PostCollision</c> resolves each object's owner through
/// <c>.pBase</c> and takes stock's own <c>if ( !pEntity ) return;</c> — which for
/// an entity that is not a <c>CBaseEntity</c> is every time — so a managed prop
/// hitting a wall made no sound, kicked up no dust and shook no screen. The
/// events were recorded into the physics shell's ring instead, and the ring was
/// drained into a null sink. Measured live before the fix, on dm_lockdown:
/// high water 263 events, drain crossings 0, events handed on 0.
/// </para>
///
/// <para>
/// <b>PURE, AND THAT IS THE POINT.</b> Nothing here calls the engine, holds
/// state, or looks anything up. A rule that decides whether a crate makes a
/// noise is exactly the code that is silently wrong rather than obviously
/// broken, so every clause of it is reachable from an xUnit fact with no game
/// running — the same reasoning physics_shell.h's own header gives for putting
/// the ring under gtest. <see cref="PhysicsCollisionResponse"/> is the half that
/// touches the engine, and it is deliberately thin enough to have nothing to
/// decide.
/// </para>
/// </summary>
public static class PhysicsCollisionRules
{
    //-------------------------------------------------------------------------
    // THE NUMBERS. Each is stock's, and the citation is the point of the name:
    // a constant with no source is a constant the next reader has to guess at.
    //-------------------------------------------------------------------------

    /// <summary><c>VPHYSICS_LARGE_OBJECT_MASS</c>, game/server/physics.h:49.</summary>
    public const float LargeObjectMass = 500.0f;

    /// <summary>
    /// The shake's mass ceiling — <c>clamp(mass, VPHYSICS_LARGE_OBJECT_MASS, 2000.f)</c>,
    /// physics.cpp:4192. A heavier object does not shake harder.
    /// </summary>
    public const float LargeObjectMassCeiling = 2000.0f;

    /// <summary><c>collision_shake_amp</c>'s default, physics.cpp:4176.</summary>
    public const float ShakeAmplitudeScale = 0.2f;

    /// <summary><c>collision_shake_freq</c>'s default, physics.cpp:4177.</summary>
    public const float ShakeFrequency = 0.5f;

    /// <summary><c>collision_shake_time</c>'s default, physics.cpp:4178.</summary>
    public const float ShakeDuration = 0.5f;

    /// <summary>Below this impact speed there is no shake. physics.cpp:4189.</summary>
    public const float ShakeMinSpeed = 30.0f;

    /// <summary>
    /// Two collisions closer together than this do not shake twice.
    /// physics.cpp:4189.
    /// </summary>
    public const float ShakeMinDelta = 0.25f;

    /// <summary>Two impacts closer together than this make one sound. physics.cpp:4160.</summary>
    public const float SoundMinDelta = 0.05f;

    /// <summary>Below this impact speed there is no sound. physics.cpp:4160.</summary>
    public const float SoundMinSpeed = 70.0f;

    /// <summary>Full volume at this impact speed. physics.cpp:4163.</summary>
    public const float SoundFullVolumeSpeed = 320.0f;

    /// <summary>
    /// A ragdoll's own limbs hitting each other more often than this are one
    /// event. baseentity.cpp:3268 — a longer interval than
    /// <see cref="SoundMinDelta"/>, and deliberately so.
    /// </summary>
    public const float SelfCollisionMinDelta = 0.5f;

    /// <summary>Sand and dirt need this much speed to raise dust. physics.cpp:4232.</summary>
    public const float DustSoftMinSpeed = 200.0f;

    /// <summary>Concrete needs more. physics.cpp:4239.</summary>
    public const float DustConcreteMinSpeed = 340.0f;

    /// <summary>The dust puff's size — <c>g_pEffects-&gt;Dust(..., 8.0f, ...)</c>, physics.cpp:4258.</summary>
    public const float DustSize = 8.0f;

    /// <summary>
    /// <c>amplitude * 60</c> is the shake's radius, physics.cpp:4196. Named
    /// because it is a factor and not a coincidence.
    /// </summary>
    public const float ShakeRadiusFromAmplitude = 60.0f;

    /// <summary>
    /// The <c>30.0f</c> in <c>collision_shake_amp / (30.0f * VPHYSICS_LARGE_OBJECT_MASS)</c>,
    /// physics.cpp:4195.
    ///
    /// <para>
    /// A CONSTANT OF ITS OWN even though it happens to equal
    /// <see cref="ShakeMinSpeed"/>. The two are unrelated: one is the speed
    /// below which nothing shakes, the other normalises the amplitude so that a
    /// 500 kg object at 30 in/s produces exactly <c>collision_shake_amp</c>.
    /// Writing the threshold here would mean raising the threshold quietly
    /// changed how hard every shake in the game hits.
    /// </para>
    /// </summary>
    public const float ShakeAmplitudeReferenceSpeed = 30.0f;

    //-------------------------------------------------------------------------
    // The surface materials this rule set names, as the letters surfaceprops
    // stores. decals.h has the first three; 'X' it does not.
    //-------------------------------------------------------------------------

    /// <summary><c>CHAR_TEX_CONCRETE</c>, decals.h:17.</summary>
    public const int MaterialConcrete = 'C';

    /// <summary><c>CHAR_TEX_DIRT</c>, decals.h:18.</summary>
    public const int MaterialDirt = 'D';

    /// <summary><c>CHAR_TEX_SAND</c>, decals.h:28.</summary>
    public const int MaterialSand = 'N';

    /// <summary>
    /// The silent material.
    ///
    /// <para>
    /// NOT IN decals.h, which lists 'X' as unused — it is a surfaceprops
    /// convention rather than a decal kind, and baseentity.cpp:3272 tests the
    /// letter directly with the comment "don't make noise for
    /// hidden/invisible/sky materials". Named here because a bare 'X' three
    /// lines into a condition is exactly the constant a later reader deletes.
    /// </para>
    /// </summary>
    public const int MaterialHidden = 'X';

    //-------------------------------------------------------------------------
    // THE RULES.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether this impact produces any response at all — the three gates
    /// <c>CBaseEntity::VPhysicsCollision</c> opens with, in its order.
    ///
    /// <para>
    /// 1. <b>Neither side is simulated by VPhysics.</b> Stock's comment: "Don't
    ///    make sounds / effects if neither entity is MOVETYPE_VPHYSICS. The game
    ///    physics should have done so." A shadow pushing a shadow is the game's
    ///    own movement and has already made whatever noise it makes.
    /// </para>
    /// <para>
    /// 2. <b>A self-collision inside half a second.</b> A ragdoll's limbs strike
    ///    each other many times per second and stock filters them on a longer
    ///    interval than it uses for two different entities.
    /// </para>
    /// <para>
    /// 3. <b>Either material is hidden.</b> Sky, clip and invisible brushes are
    ///    not things to make a noise against.
    /// </para>
    /// </summary>
    /// <param name="ev">The impact.</param>
    /// <param name="material0">Surface material letter on side 0.</param>
    /// <param name="material1">Surface material letter on side 1.</param>
    public static bool Responds(in PhysicsEvent ev, int material0, int material1)
    {
        if (ev.Kind != PhysicsEventKind.PostCollision)
            return false;

        if (ev.MoveType0 != (int)MoveType.VPhysics && ev.MoveType1 != (int)MoveType.VPhysics)
            return false;

        if (ev.IsSelfCollision && ev.DeltaCollisionTime < SelfCollisionMinDelta)
            return false;

        if (material0 == MaterialHidden || material1 == MaterialHidden)
            return false;

        return true;
    }

    /// <summary>
    /// The impact sound, or null when the impact is too quiet or too soon after
    /// the last one — <c>PhysCollisionSound</c>, physics.cpp:4158.
    ///
    /// <para>
    /// The volume curve is stock's: speed squared over 320 squared, clamped at
    /// 1. Quadratic and not linear, so a crate nudged at 80 in/s is a twentieth
    /// as loud as one thrown at 320 rather than a quarter.
    /// </para>
    /// </summary>
    public static CollisionSound? Sound(in PhysicsEvent ev)
    {
        if (ev.DeltaCollisionTime < SoundMinDelta || ev.Speed < SoundMinSpeed)
            return null;

        float volume = ev.Speed * ev.Speed / (SoundFullVolumeSpeed * SoundFullVolumeSpeed);
        if (volume > 1.0f)
            volume = 1.0f;

        return new CollisionSound(volume, ev.Speed,
            ev.IsSelfCollision ? CollisionSoundChannel.Body : CollisionSoundChannel.Static);
    }

    /// <summary>
    /// The screen shake this impact asks for from the point of view of one
    /// side, or null — <c>PhysCollisionScreenShake</c>, physics.cpp:4182.
    ///
    /// <para>
    /// Four conditions, all of them stock's: the side must be at least
    /// <see cref="LargeObjectMass"/>, the OTHER side must be static, the moving
    /// side must not be stuck inside something
    /// (<c>FVPHYSICS_PENETRATING</c> — a penetrating object is being ejected by
    /// the solver rather than hitting anything, and shaking the screen for that
    /// is shaking it for a bug), and the impact must be fast enough and far
    /// enough from the last one.
    /// </para>
    /// </summary>
    /// <param name="ev">The impact.</param>
    /// <param name="index">Which side to ask about: 0 or 1.</param>
    public static CollisionShake? Shake(in PhysicsEvent ev, int index)
    {
        float mass = index == 0 ? ev.Mass0 : ev.Mass1;
        bool otherIsStatic = index == 0 ? ev.Static1 != 0 : ev.Static0 != 0;
        uint gameFlags = index == 0 ? ev.GameFlags0 : ev.GameFlags1;

        if (mass < LargeObjectMass)
            return null;
        if (!otherIsStatic)
            return null;
        if ((gameFlags & PhysicsGameFlags.Penetrating) != 0)
            return null;

        if (ev.Speed <= ShakeMinSpeed || ev.DeltaCollisionTime <= ShakeMinDelta)
            return null;

        if (mass > LargeObjectMassCeiling)
            mass = LargeObjectMassCeiling;

        float impulse = ev.Speed * mass;
        float amplitude = impulse * (ShakeAmplitudeScale / (ShakeAmplitudeReferenceSpeed * LargeObjectMass));

        return new CollisionShake(ev.Point, amplitude, ShakeFrequency, ShakeDuration,
                                  amplitude * ShakeRadiusFromAmplitude);
    }

    /// <summary>
    /// Whether this impact kicks up dust against a surface of the given
    /// material — <c>PhysCollisionDust</c>, physics.cpp:4220.
    ///
    /// <para>
    /// Three materials and two thresholds: sand and dirt at
    /// <see cref="DustSoftMinSpeed"/>, concrete at
    /// <see cref="DustConcreteMinSpeed"/>, and everything else never. Stock's
    /// <c>default: return;</c> is a closed list and this reproduces it as one.
    /// </para>
    /// </summary>
    /// <param name="hitMaterial">The material of the surface being hit.</param>
    /// <param name="collisionSpeed">The impact speed.</param>
    public static bool Dust(int hitMaterial, float collisionSpeed) => hitMaterial switch
    {
        MaterialSand or MaterialDirt => collisionSpeed >= DustSoftMinSpeed,
        MaterialConcrete => collisionSpeed >= DustConcreteMinSpeed,
        _ => false,
    };
}

/// <summary>
/// <c>FVPHYSICS_*</c>, game/shared/physics_shared.h:48. Only the ones this mod
/// reads are named, for the reason <see cref="SurfaceMaterial"/> gives.
/// </summary>
public static class PhysicsGameFlags
{
    /// <summary>Object is currently stuck inside another object. :54.</summary>
    public const uint Penetrating = 0x0040;

    /// <summary>Object is part of a client or server ragdoll. :51.</summary>
    public const uint PartOfRagdoll = 0x0008;

    /// <summary>Object is held by the player. :50.</summary>
    public const uint PlayerHeld = 0x0004;

    /// <summary>Don't do impact damage to anything. :58.</summary>
    public const uint NoImpactDamage = 0x0400;

    /// <summary>Does slice damage, not just blunt damage. :49.</summary>
    public const uint SliceDamage = 0x0001;

    /// <summary>Object is constrained to the world, so it should behave like a static. :50.</summary>
    public const uint ConstraintStatic = 0x0002;

    /// <summary>Object is part of a multi-object entity. :52.</summary>
    public const uint MultiObjectEntity = 0x0010;

    /// <summary>Behaves like a much heavier object for impact damage. :53.</summary>
    public const uint HeavyObject = 0x0020;

    /// <summary>Does dissolve damage. :57.</summary>
    public const uint DissolveDamage = 0x0200;

    /// <summary>Don't do impact damage to NPCs or players. :59.</summary>
    public const uint NoNpcImpactDamage = 0x0800;
}
