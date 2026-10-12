//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The vphysics calls an entity's one-object physics API makes, each through
/// the object's generated <see cref="PhysicsObjectVtbl"/>. The pointer is never
/// 0 here: <see cref="EntityPhysicsMotion"/> asks only when there is an object.
/// Angular velocities are in the OBJECT's own axes, as vphysics keeps them.
/// </summary>
public interface IVPhysicsMotionCalls
{
    float Mass(nint physicsObject);
    void SetMass(nint physicsObject, float mass);
    void EnableMotion(nint physicsObject, bool enable);
    void Wake(nint physicsObject);
    void Sleep(nint physicsObject);
    ushort GameFlags(nint physicsObject);
    void SetGameFlags(nint physicsObject, ushort flags);
    void RecheckCollisionFilter(nint physicsObject);
    void ApplyForceCenter(nint physicsObject, Vector force);
    void ApplyForceOffset(nint physicsObject, Vector force, Vector worldPoint);
    void ApplyTorqueCenter(nint physicsObject, Vector torque);
    void BecomeTrigger(nint physicsObject);
    void RemoveTrigger(nint physicsObject);
    (Vector Linear, Vector Angular) Velocity(nint physicsObject);
    void SetVelocity(nint physicsObject, Vector linear, Vector angular);

    /// <summary><c>AddVelocity( &amp;linear, NULL )</c>: the linear half only.</summary>
    void AddVelocity(nint physicsObject, Vector linear);

    Vector LocalToWorldVector(nint physicsObject, Vector local);
    Vector WorldToLocalVector(nint physicsObject, Vector world);
    void SetPosition(nint physicsObject, Vector origin, QAngle angles, bool isTeleport);
}

/// <summary>
/// The entity side of <see cref="EntityPhysicsMotion"/>: the entity's
/// <c>IPhysicsObject *</c> (a held-pointer read, 0 for none), its own velocity
/// members (the shell's mirrors, for an entity the solver is not moving), its
/// facing, and the console.
/// </summary>
public interface IEntityPhysicsMotionHost
{
    nint PhysicsObject(EntityHandle entity);
    Vector EntityVelocity(EntityHandle entity);
    void SetEntityVelocity(EntityHandle entity, Vector velocity);
    Vector EntityAngularVelocity(EntityHandle entity);
    void SetEntityAngularVelocity(EntityHandle entity, Vector degreesPerSecond);
    Vector Angles(EntityHandle entity);
    void Warning(string message);
}

/// <summary>
/// An entity's one-object physics calls, made by managed on the entity's
/// <c>IPhysicsObject</c> (plan_physics_bones_native_zero: the last thin
/// vphysics forwards leave shell_scripted.cpp, shell_entityapi.cpp and
/// shell_worldquery.cpp). No object is a no-op answering zero, as stock's
/// NULL checks are.
///
/// <para>
/// <b>WHICH HOME A VELOCITY LIVES IN</b> is decided here, once, for all four
/// velocity calls. Stock's <c>CBaseEntity::GetVelocity</c> (baseentity.cpp)
/// reads the physics object when the entity is simulated and the entity's own
/// members otherwise. On this hierarchy an object exists BECAUSE its entity
/// simulates (igameentity.h's GetMoveType note: there is nothing a move type
/// would add), so the object's presence is the whole question, and the setter
/// asks the same one as the getter: the defect native's shell_velocity_home.h (deleted 2026-10-12)
/// was written to fix was the two answering differently. A STATIC object is
/// still the solver's (it declines a velocity, which is the truthful answer).
/// </para>
///
/// <para>
/// <b>AN IMPULSE IS A VELOCITY</b>, as stock's
/// <c>CBaseEntity::ApplyAbsVelocityImpulse</c> (baseentity_shared.cpp) has it:
/// a zero impulse does nothing, <c>CheckEntityVelocity</c> clamps a suspicious
/// one to <see cref="MaxEntitySpeed"/> and discards an absurd one, and the rest
/// is <c>AddVelocity( &amp;vecImpulse, NULL )</c> (which wakes the object
/// itself). The native forward this replaces applied it as a FORCE scaled by
/// the object's mass, which comes to the same speed by a longer road. Stock's
/// other arm adds the impulse to the entity's own velocity; here an entity with
/// no object is moved by EntityMotion, which holds the velocity it integrates,
/// so that arm would write a mirror nothing reads and is left out as the
/// native forward left it out.
/// </para>
/// </summary>
public sealed class EntityPhysicsMotion(IEntityPhysicsMotionHost host, IVPhysicsMotionCalls v)
{
    /// <summary>Stock's <c>k_flMaxEntitySpeed</c>: <c>k_flMaxVelocity</c> (performance.h, 2000) twice.</summary>
    public const float MaxEntitySpeed = 2000f * 2.0f;

    private readonly IEntityPhysicsMotionHost _host = host;
    private readonly IVPhysicsMotionCalls _v = v;

    public float Mass(EntityHandle entity)
    {
        nint o = _host.PhysicsObject(entity);
        return o == 0 ? 0f : _v.Mass(o);
    }

    /// <summary>A mass that is not positive is refused: to the solver it is a division by it.</summary>
    public void SetMass(EntityHandle entity, float mass)
    {
        nint o = _host.PhysicsObject(entity);
        if (o != 0 && mass > 0f)
            _v.SetMass(o, mass);
    }

    /// <summary>Enabling also wakes: an object enabled while asleep would otherwise stay put until touched.</summary>
    public void EnableMotion(EntityHandle entity, bool enable)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        _v.EnableMotion(o, enable);
        if (enable)
            _v.Wake(o);
    }

    public void Wake(EntityHandle entity)
    {
        nint o = _host.PhysicsObject(entity);
        if (o != 0)
            _v.Wake(o);
    }

    public void Sleep(EntityHandle entity)
    {
        nint o = _host.PhysicsObject(entity);
        if (o != 0)
            _v.Sleep(o);
    }

    /// <summary>
    /// Stock's <c>PhysSetGameFlags</c> (physics_shared.h): the flags ORed in.
    /// vphysics caches a pair's collision answer, so the filter is rechecked.
    /// </summary>
    public void AddGameFlags(EntityHandle entity, uint flags)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        _v.SetGameFlags(o, (ushort)(_v.GameFlags(o) | (ushort)flags));
        _v.RecheckCollisionFilter(o);
    }

    public void ApplyForceCenter(EntityHandle entity, Vector force)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        _v.ApplyForceCenter(o, force);
        _v.Wake(o);
    }

    public void ApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        _v.ApplyForceOffset(o, force, worldPoint);
        _v.Wake(o);
    }

    public void ApplyTorque(EntityHandle entity, Vector torque)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        _v.ApplyTorqueCenter(o, torque);
        _v.Wake(o);
    }

    public void BecomeTrigger(EntityHandle entity, bool isTrigger)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        if (isTrigger)
            _v.BecomeTrigger(o);
        else
            _v.RemoveTrigger(o);
    }

    public Vector Velocity(EntityHandle entity)
    {
        nint o = _host.PhysicsObject(entity);
        return o != 0 ? _v.Velocity(o).Linear : _host.EntityVelocity(entity);
    }

    /// <summary>
    /// The solver's: the spin is read back and written unchanged, since
    /// SetVelocity takes both and anything else would stop a spinning crate as
    /// a side effect of pushing it; then woken, since the solver steps only
    /// what is awake.
    /// </summary>
    public void SetVelocity(EntityHandle entity, Vector velocity)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
        {
            _host.SetEntityVelocity(entity, velocity);
            return;
        }

        var (_, angular) = _v.Velocity(o);
        _v.SetVelocity(o, velocity, angular);
        _v.Wake(o);
    }

    /// <summary>Degrees per second about the WORLD's axes: the solver's is in the object's own, converted.</summary>
    public Vector AngularVelocity(EntityHandle entity)
    {
        nint o = _host.PhysicsObject(entity);
        return o != 0 ? _v.LocalToWorldVector(o, _v.Velocity(o).Angular) : _host.EntityAngularVelocity(entity);
    }

    /// <summary>The converse of <see cref="AngularVelocity"/>, keeping the travel and waking it.</summary>
    public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
        {
            _host.SetEntityAngularVelocity(entity, degreesPerSecond);
            return;
        }

        var (linear, _) = _v.Velocity(o);
        _v.SetVelocity(o, linear, _v.WorldToLocalVector(o, degreesPerSecond));
        _v.Wake(o);
    }

    /// <summary>Stock's <c>ApplyAbsVelocityImpulse</c>, the simulated arm. See the class remarks.</summary>
    public void ApplyVelocityImpulse(EntityHandle entity, Vector impulse)
    {
        if (impulse.X == 0f && impulse.Y == 0f && impulse.Z == 0f)
            return;

        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        if (CheckEntityVelocity(ref impulse) < 0)
        {
            _host.Warning(FormattableString.Invariant(
                $"Discarding ApplyAbsVelocityImpulse({impulse.X:F6},{impulse.Y:F6},{impulse.Z:F6}) on {entity}\n"));
            return;
        }

        _v.AddVelocity(o, impulse);
    }

    /// <summary>
    /// Stock's <c>CheckEntityVelocity</c> (baseentity_shared.cpp): 1 when every
    /// axis is inside <see cref="MaxEntitySpeed"/>; 0 when it is not but the
    /// length is under 100 times it, with <paramref name="v"/> scaled to that
    /// length; -1 for anything faster, which is rejected.
    /// </summary>
    public static int CheckEntityVelocity(ref Vector v)
    {
        const float r = MaxEntitySpeed;
        if (v.X > -r && v.X < r && v.Y > -r && v.Y < r && v.Z > -r && v.Z < r)
            return 1;

        float speed = v.Length;
        if (speed < MaxEntitySpeed * 100.0f)
        {
            float scale = MaxEntitySpeed / speed;
            v = new Vector(v.X * scale, v.Y * scale, v.Z * scale);
            return 0;
        }

        return -1;
    }

    /// <summary>
    /// A teleport's solver half, after the entity has been moved: the object
    /// is put at the same pose AS A TELEPORT (so vphysics does not resolve
    /// collisions along a path it never took), stopped when asked, and woken.
    /// Without it the next physics update reads the old pose back onto the
    /// entity and the teleport never happened.
    /// </summary>
    public void TeleportObject(EntityHandle entity, Vector origin, Vector? angles, bool zeroVelocity)
    {
        nint o = _host.PhysicsObject(entity);
        if (o == 0)
            return;

        _v.SetPosition(o, origin, QAngle.FromVector(angles ?? _host.Angles(entity)), isTeleport: true);

        if (zeroVelocity)
            _v.SetVelocity(o, Vector.Zero, Vector.Zero);

        _v.Wake(o);
    }
}

/// <summary>The real vphysics, by the generated vtable. Holds nothing.</summary>
public sealed unsafe class VPhysicsMotionCalls : IVPhysicsMotionCalls
{
    private static PhysicsObjectVtbl* Obj(nint o) => *(PhysicsObjectVtbl**)o;

    public float Mass(nint o) => Obj(o)->GetMass((void*)o);

    public void SetMass(nint o, float mass) => Obj(o)->SetMass((void*)o, mass);

    public void EnableMotion(nint o, bool enable) => Obj(o)->EnableMotion((void*)o, enable ? (byte)1 : (byte)0);

    public void Wake(nint o) => Obj(o)->Wake((void*)o);

    public void Sleep(nint o) => Obj(o)->Sleep((void*)o);

    public ushort GameFlags(nint o) => Obj(o)->GetGameFlags((void*)o);

    public void SetGameFlags(nint o, ushort flags) => Obj(o)->SetGameFlags((void*)o, flags);

    public void RecheckCollisionFilter(nint o) => Obj(o)->RecheckCollisionFilter((void*)o);

    public void ApplyForceCenter(nint o, Vector force) => Obj(o)->ApplyForceCenter((void*)o, &force);

    public void ApplyForceOffset(nint o, Vector force, Vector worldPoint)
        => Obj(o)->ApplyForceOffset((void*)o, &force, &worldPoint);

    public void ApplyTorqueCenter(nint o, Vector torque) => Obj(o)->ApplyTorqueCenter((void*)o, &torque);

    public void BecomeTrigger(nint o) => Obj(o)->BecomeTrigger((void*)o);

    public void RemoveTrigger(nint o) => Obj(o)->RemoveTrigger((void*)o);

    public (Vector Linear, Vector Angular) Velocity(nint o)
    {
        Vector linear, angular;
        Obj(o)->GetVelocity((void*)o, &linear, &angular);
        return (linear, angular);
    }

    public void SetVelocity(nint o, Vector linear, Vector angular)
        => Obj(o)->SetVelocity((void*)o, &linear, &angular);

    public void AddVelocity(nint o, Vector linear) => Obj(o)->AddVelocity((void*)o, &linear, null);

    public Vector LocalToWorldVector(nint o, Vector local)
    {
        Vector world;
        Obj(o)->LocalToWorldVector((void*)o, &world, &local);
        return world;
    }

    public Vector WorldToLocalVector(nint o, Vector world)
    {
        Vector local;
        Obj(o)->WorldToLocalVector((void*)o, &local, &world);
        return local;
    }

    public void SetPosition(nint o, Vector origin, QAngle angles, bool isTeleport)
        => Obj(o)->SetPosition((void*)o, &origin, &angles, isTeleport ? (byte)1 : (byte)0);
}
