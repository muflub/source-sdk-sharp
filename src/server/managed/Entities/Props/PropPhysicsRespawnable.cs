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
/// A physics prop that comes back. Deathmatch maps are full of them: the
/// scenery is meant to be destroyed and still be there next round.
/// </summary>
[ScriptedEntity(EntityNames.PropPhysicsRespawnable, ScriptedEntityKind.Prop)]
public sealed class PropPhysicsRespawnable : PropPhysics
{
    public PropPhysicsRespawnable(IEngineDamage damage, IEngineEntityPhysics physics, IEngineWorldQuery world,
                       IEngineEffects effects, IEnginePrecache precache, IEngineHost host,
                       IBlastSweep blast, IServerDecals decals, IEngineEntityRender render,
                       IServerPropData propData, PhysicsDamageQueue impacts, FluidTouches fluid, PhysicsSoundBatch sounds,
                       PlayerPickupRules pickups, DamageInflictors inflictors, IEngineSound sound,
                       IVPhysicsGrabCalls vphysics, ITempEnts temps, IEngineEnvironment environment,
                       ConVarRegistry? convars = null)
        : base(damage, physics, world, effects, precache, host, blast, decals, render, propData, impacts, fluid, sounds,
               pickups, inflictors, sound, vphysics, temps, environment, convars)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PropPhysicsRespawnableVars"/>.</summary>
    [ScriptVars] public PropPhysicsRespawnableVars PropPhysicsRespawnableVars = new();

    /// <summary>m_vOriginalSpawnOrigin and m_vOriginalSpawnAngles.</summary>
    [SaveField] private Vector _origin;

    [SaveField] private Vector _angles;

    /// <summary>RESPAWNABLE_PROP_DEFAULT_TIME.</summary>
    private const float DefaultRespawnTime = 60f;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Recorded at spawn because a prop that has been shoved around the map
        // should come back where the mapper put it, not where it last stopped.
        _origin = Origin;
        _angles = Entities.GetAngles(Handle);
    }

    /// <summary>
    /// CPhysicsPropRespawnable::Event_Killed: hidden rather than removed (the
    /// entity has to survive to come back, and a map may have wired outputs to
    /// it), its flame put out, sent back where it spawned and out of the
    /// simulation until it materializes.
    ///
    /// The output and the explosion have already happened by the time this
    /// runs, which is the point of it being the only part that differs.
    /// </summary>
    protected override void Broken()
    {
        HideForRespawn(_origin, _angles);
        SetNextThink(PropPhysicsRespawnableVars.RespawnTime > 0f ? PropPhysicsRespawnableVars.RespawnTime : DefaultRespawnTime);
    }

    /// <summary>Whether the respawn timer has fired. For tests.</summary>
    internal bool Respawned { get; private set; }

    /// <summary>
    /// CPhysicsPropRespawnable::Materialize: not while something stands where
    /// it spawned (another try in a second), and otherwise whole again.
    /// </summary>
    protected override void OnTimer()
    {
        var (mins, maxs) = Entities.GetBounds(Handle);
        if (World.TraceHull(_origin, _origin, mins, maxs, TraceMask.Solid, Handle).StartSolid)
        {
            SetNextThink(1f);
            return;
        }

        Respawned = true;
        MaterializeAfterRespawn();
    }
}

/// <summary>
/// The map keys <see cref="PropPhysicsRespawnable"/> declares.
/// </summary>
[ScriptVars]
public partial class PropPhysicsRespawnableVars
{
    /// <summary>Seconds before it returns.</summary>
    public float RespawnTime { get; set; } = 60f;
}
