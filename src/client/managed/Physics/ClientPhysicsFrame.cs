//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Physics;

/// <summary>
/// game/client/physics.cpp's <c>cl_phys_timescale</c>, which moved with the
/// step that reads it (name, default, flags and help as stock spelled them).
/// </summary>
[ConVar("cl_phys_timescale", "1.0", Flags = ConVarFlags.Cheat,
        Help = "Sets the scale of time for client-side physics (ragdolls)")]
public sealed class ClientPhysTimescaleConVar : FloatConVar { }

/// <summary>
/// game/client/physics.cpp's <c>cl_ragdoll_collide</c>, which the client's
/// collision solver reads (ClientCollisionFacts, by name).
/// </summary>
[ConVar("cl_ragdoll_collide", "0")]
public sealed class ClientRagdollCollideConVar : BoolConVar { }

/// <summary><see cref="IClientPhysicsClock"/> over the engine's globals, its gravity and the timescale.</summary>
public sealed class ClientPhysicsClock(IEngineEnvironment engine, IEngineEffects effects, ClientPhysTimescaleConVar timescale)
    : IClientPhysicsClock
{
    public float FrameTime => engine.Globals.FrameTime;

    public float IntervalPerTick => engine.Globals.IntervalPerTick;

    public float Gravity => effects.CurrentGravity();

    public float TimeScale => timescale.Value;
}

/// <summary><see cref="IClientPhysicsStepTail"/> over the client's impact sounds, fluid entries and scrapes.</summary>
public sealed class ClientPhysicsStepTail(ClientImpactSounds impacts, ClientFluidEvents fluids, FrictionSounds scrapes,
                                          IGameClock clock, ClientContactTouches? touches = null) : IClientPhysicsStepTail
{
    public void UpdateFrictionSounds() => scrapes.Update(clock.Now);

    public void UpdateTouchEvents() => touches?.UpdateTouchEvents();

    public void UpdateFluidEvents() => fluids.Frame();

    public void PlayImpactSounds() => impacts.Play();

    public void RemoveAllImpactSounds() => impacts.Clear();
}
