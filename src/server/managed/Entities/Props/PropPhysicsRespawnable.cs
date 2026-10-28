//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
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
                       PlayerPickupRules pickups)
        : base(damage, physics, world, effects, precache, host, blast, decals, render, propData, impacts, fluid, sounds,
               pickups)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PropPhysicsRespawnableVars"/>.</summary>
    [ScriptVars] public PropPhysicsRespawnableVars PropPhysicsRespawnableVars = new();

    private Vector _origin;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Recorded at spawn because a prop that has been shoved around the map
        // should come back where the mapper put it, not where it last stopped.
        _origin = Origin;
    }

    /// <summary>
    /// Hidden rather than removed: the entity has to survive to come back, and
    /// a map may have wired outputs to it.
    ///
    /// The output and the explosion have already happened by the time this
    /// runs, which is the point of it being the only part that differs.
    /// </summary>
    protected override void Broken()
    {
        SetPickupEnabled(false);
        SetVisible(false);
        SetNextThink(PropPhysicsRespawnableVars.RespawnTime > 0f ? PropPhysicsRespawnableVars.RespawnTime : 60f);
    }

    /// <summary>Whether the respawn timer has fired. For tests.</summary>
    internal bool Respawned { get; private set; }

    protected override void OnTimer()
    {
        Respawned = true;
        Unbreak();

        Origin = _origin;
        SetVisible(true);
        SetPickupEnabled(true);
        EnableMotion(true);
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
