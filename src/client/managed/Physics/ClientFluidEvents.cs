//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Physics;

/// <summary>What the client's fluid entries read and carry out.</summary>
public interface IClientFluidWorld : IClientSplashWorld
{
    nint GameData(nint physicsObject);

    ushort GameFlags(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetVelocity</c>, linear.</summary>
    Vector Velocity(nint physicsObject);

    /// <summary><c>IPhysicsFluidController::GetSurfacePlane</c>.</summary>
    (Vector Normal, float Dist) SurfacePlane(nint fluid);

    /// <summary>The owner entity's absolute origin and angles (pitch, yaw, roll).</summary>
    (Vector Origin, Vector Angles) OwnerTransform(EntityHandle owner);

    /// <summary><c>physcollision-&gt;CollideGetExtent( pObject-&gt;GetCollide(), origin, angles, direction )</c>.</summary>
    Vector CollideExtent(nint physicsObject, Vector origin, Vector angles, Vector direction);

    /// <summary>fx_water.h's effect, carried out.</summary>
    void Splash(in ClientSplashEffect effect);
}

/// <summary>
/// The client's fluid entries, as stock's client CCollisionEvent made them
/// (game/client/physics.cpp): FluidStartTouch's half-second throttle per
/// entity (DeltaTimeSinceLastFluid: the time since the entity's FIRST entry
/// in the list, the entry kept, not refreshed), PhysicsSplash at the owner's
/// transform, and UpdateFluidEvents' two-second expiry, once a frame
/// (plan_client_novalve C6, cluster 9; lane c6-input).
/// </summary>
public sealed class ClientFluidEvents(IClientFluidWorld world, IUniformRandom? random, Func<float> curTime) : IClientFluidSink
{
    /// <summary>FLUID_TIME_MAX: how long an entry is kept, and what a new one answers.</summary>
    private const float FluidTimeMax = 2.0f;

    /// <summary>The throttle, as stock's FluidStartTouch has it.</summary>
    private const float MinInterval = 0.5f;

    private readonly List<(EntityHandle Owner, float ImpactTime)> _entries = new();

    /// <summary>Splashes planned and carried out.</summary>
    public long Splashes { get; private set; }

    private readonly long[] _effects = new long[3];

    /// <summary>The fx_water.h effects carried out, of one kind.</summary>
    public long EffectsOf(ClientSplashKind kind) => _effects[(int)kind];

    void IClientFluidSink.FluidStart(nint physicsObject, nint fluid) => FluidStartTouch(physicsObject, fluid);

    /// <summary>CCollisionEvent::FluidStartTouch.</summary>
    public void FluidStartTouch(nint physicsObject, nint fluid)
    {
        if (physicsObject == 0 || fluid == 0)
            return;

        EntityHandle owner = PhysicsGameData.Owner(world.GameData(physicsObject));
        if (owner == EntityHandle.None)
            return;

        if (DeltaTimeSinceLastFluid(owner) < MinInterval)
            return;

        Splash(physicsObject, fluid, owner);
    }

    /// <summary>CCollisionEvent::DeltaTimeSinceLastFluid: found, the time since; not, listed now.</summary>
    private float DeltaTimeSinceLastFluid(EntityHandle owner)
    {
        float now = curTime();
        for (int i = _entries.Count - 1; i >= 0; --i)
        {
            if (_entries[i].Owner == owner)
                return now - _entries[i].ImpactTime;
        }

        _entries.Add((owner, now));
        return FluidTimeMax;
    }

    /// <summary>PhysicsSplash: the plan at the owner's transform, each effect carried out.</summary>
    private void Splash(nint physicsObject, nint fluid, EntityHandle owner)
    {
        // Stock's draws come from the engine's stream; a client without one throws no splash.
        if (random is null)
            return;

        (Vector normal, float dist) = world.SurfacePlane(fluid);
        (Vector origin, Vector angles) = world.OwnerTransform(owner);

        // EntityToWorldTransform's columns: AngleMatrix( angles ).
        BoneMatrix3x4 m = BoneSetupMath.AngleMatrix(new BoneAngle(angles.X, angles.Y, angles.Z));
        Span<Vector> axes =
        [
            new Vector(m.Row0.X, m.Row1.X, m.Row2.X),
            new Vector(m.Row0.Y, m.Row1.Y, m.Row2.Y),
            new Vector(m.Row0.Z, m.Row1.Z, m.Row2.Z),
        ];

        List<ClientSplashEffect> effects = ClientSplashPlan.Plan(
            world.GameFlags(physicsObject), world.Velocity(physicsObject), normal, dist, axes,
            direction => world.CollideExtent(physicsObject, origin, angles, direction), world, random);
        if (effects.Count == 0)
            return;

        foreach (ClientSplashEffect effect in effects)
        {
            world.Splash(in effect);
            _effects[(int)effect.Kind]++;
        }
        Splashes++;
    }

    /// <summary>CCollisionEvent::UpdateFluidEvents, from FrameUpdate: entries older than FLUID_TIME_MAX go.</summary>
    public void Frame()
    {
        float now = curTime();
        for (int i = _entries.Count - 1; i >= 0; --i)
        {
            if (now - _entries[i].ImpactTime > FluidTimeMax)
                _entries.RemoveAt(i);
        }
    }

    /// <summary>A level's end.</summary>
    public void Clear() => _entries.Clear();
}

