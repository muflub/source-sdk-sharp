//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Stock's <c>CCollisionEvent::FluidStartTouch</c> / <c>FluidEndTouch</c>
/// (game/server/physics.cpp), answered in managed (plan_physics_managed B2.2).
///
/// <para>
/// Entering: the owner touches fluid (FLUID_START to the router, whatever the
/// timer says: stock sets EFL_TOUCHING_FLUID before it checks), then, unless
/// it last met fluid under half a second ago, the splash and the entry drag
/// (linear along the entry, angular on the spin, each scaled by the fluid's
/// density and the step). Leaving: a splash past half a second, then
/// FLUID_END. The fluid timer is stock's: an entity's first contact is
/// stamped and later ones measure from it until it is two seconds old.
/// </para>
/// </summary>
public sealed class PhysicsFluidEvents(IVPhysicsObjects objects, IVPhysicsFluids fluids, IVPhysicsEnvironment environment,
                                       IEngineEntityPhysics physics, ITempEnts temps, IEngineWorldQuery world,
                                       IEngineHost host, IGameClock clock, Action<PhysicsEvent> post) : IPhysicsFluidSink
{
    /// <summary>ss::kFluidTimeMax / stock's FLUID_TIME_MAX.</summary>
    public const float FluidTimeMax = 2f;

    /// <summary>The gap under which a re-entry neither splashes nor drags.</summary>
    public const float SplashInterval = 0.5f;

    /// <summary>CONTENTS_SLIME (bspflags.h), which our Contents enum does not name.</summary>
    private const int ContentsSlime = 0x10;

    private readonly Dictionary<EntityHandle, float> _firstContact = new();

    public void FluidStart(nint physicsObject, nint fluid)
    {
        if (physicsObject == 0 || fluid == 0)
            return;

        EntityHandle owner = PhysicsGameData.Owner(objects.GameData(physicsObject));
        if (owner == EntityHandle.None)
            return;

        post(new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.FluidStart,
            Entity0 = owner.Value,
            Entity1 = PhysicsEvent.NoEntity,
            SurfaceProps0 = (uint)objects.MaterialIndex(physicsObject),
        });

        if (SinceFirstContact(owner) < SplashInterval)
            return;

        (Vector normal, _) = fluids.SurfacePlane(fluid);
        (Vector velocity, Vector spin) = objects.Velocity(physicsObject);
        float dragScale = fluids.Density(fluid) * environment.SimulationTimestep(physics.EnvironmentPointer());

        // Into the fluid: the plane's normal points out of it.
        Vector into = normal * -1f;
        float linearScale = Math.Clamp(0.5f * velocity.Normalized.Dot(into) * objects.LinearDrag(physicsObject, into) * dragScale,
                                       0f, 1f);
        float angularScale = Math.Clamp(0.25f * objects.AngularDrag(physicsObject, spin) * dragScale, 0f, 1f);

        Splash(physicsObject, fluid);
        objects.AddVelocity(physicsObject, velocity * -linearScale, spin * -angularScale);
    }

    public void FluidEnd(nint physicsObject, nint fluid)
    {
        if (physicsObject == 0 || fluid == 0)
            return;

        EntityHandle owner = PhysicsGameData.Owner(objects.GameData(physicsObject));
        if (owner == EntityHandle.None)
            return;

        if (SinceFirstContact(owner) >= SplashInterval)
            Splash(physicsObject, fluid);

        post(new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.FluidEnd,
            Entity0 = owner.Value,
            Entity1 = PhysicsEvent.NoEntity,
        });
    }

    /// <summary>Forgets the timers: a level change.</summary>
    public void Clear() => _firstContact.Clear();

    /// <summary>
    /// DeltaTimeSinceLastFluid with UpdateFluidEvents' expiry folded in: an
    /// entity with no live stamp is stamped now and answers the maximum.
    /// </summary>
    private float SinceFirstContact(EntityHandle owner)
    {
        float now = clock.Now;
        if (_firstContact.TryGetValue(owner, out float stamped) && now - stamped <= FluidTimeMax)
            return now - stamped;

        _firstContact[owner] = now;
        return FluidTimeMax;
    }

    private void Splash(nint physicsObject, nint fluid)
    {
        (Vector normal, float dist) = fluids.SurfacePlane(fluid);
        (Vector x, Vector y, Vector z) = objects.Axes(physicsObject);
        (Vector velocity, _) = objects.Velocity(physicsObject);
        bool ragdoll = (objects.GameFlags(physicsObject) & PhysicsGameFlags.PartOfRagdoll) != 0;

        // The owner's water type, as the contents just under the surface at
        // the body's footprint centre (stock read the entity's m_nWaterType).
        Vector probe = normal * (dist - 1f);
        bool slime = ((int)world.PointContents(probe) & ContentsSlime) != 0;

        var effects = PhysicsSplashPlan.Plan(normal, dist, [x, y, z],
                                             dir => physics.CollideExtent(physicsObject, dir), velocity,
                                             objects.Mass(physicsObject), ragdoll, slime,
                                             host.RandomFloat, RandomInt);

        foreach (SplashEffect effect in effects)
        {
            var data = new TeEffectData
            {
                Origin = effect.Origin,
                Normal = effect.Normal,
                Angles = new Vector(effect.Angles.Pitch, effect.Angles.Yaw, effect.Angles.Roll),
                Scale = effect.Scale,
                Flags = effect.InSlime ? FxWaterInSlime : 0,
            };
            temps.DispatchEffect(TeFilters.Pvs(effect.Origin), 0f, effect.Name, in data);
        }
    }

    /// <summary>FX_WATER_IN_SLIME (effect_dispatch_data.h).</summary>
    private const int FxWaterInSlime = 0x1;

    /// <summary>RandomInt( lo, hi ), inclusive, from the engine's float stream (PropData's form).</summary>
    private int RandomInt(int lo, int hi)
    {
        int i = (int)MathF.Floor(host.RandomFloat(lo, hi + 1));
        return i < lo ? lo : (i > hi ? hi : i);
    }
}
