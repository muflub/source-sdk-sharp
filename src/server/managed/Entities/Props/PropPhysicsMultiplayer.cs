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
/// A physics prop placed by a multiplayer map.
///
/// <para>
/// The name promises a different entity than it delivers. Stock's
/// CPhysicsPropMultiplayer is a CPhysicsProp with one extra idea on top —
/// <c>physicsmode</c>, which chooses whether the prop is simulated on the
/// server, on the client, or not solid at all — and that idea exists to hand
/// cheap debris to a client-side simulation so the server does not pay for a
/// hundred crates. Everything else about it is prop_physics: the same model,
/// the same mass, the same health, the same break.
/// </para>
///
/// <para>
/// This hierarchy has no client-side prop simulation, so CLIENTSIDE is served
/// on the server here. That is not an invention: stock does exactly the same
/// whenever <c>engine->IsInEditMode()</c>, for the same reason — a prop the
/// client will not simulate has to be simulated somewhere or it is simply not
/// in the map. The alternative, stock's release path, is <c>UTIL_Remove</c>,
/// and a barrel that vanishes on load is a worse answer than a barrel the
/// server moves.
/// </para>
///
/// <para>
/// What is deliberately NOT carried over is the collision-group dance around
/// <c>sv_turbophysics</c> (PUSHAWAY when awake, DEBRIS when asleep and
/// non-solid). Its only readers were the player-pushaway system and the
/// turbophysics path, neither of which is in this build —
/// <c>obstacle_pushaway.cpp</c> is not on the link line — so those writes went
/// to a value nothing asked. Measured, not assumed.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PropPhysicsMultiplayer, ScriptedEntityKind.Prop)]
public sealed class PropPhysicsMultiplayer : PropPhysics
{
    public PropPhysicsMultiplayer(IEngineDamage damage, IEngineEntityPhysics physics, IEngineWorldQuery world,
                       IEngineEffects effects, IEnginePrecache precache, IEngineHost host,
                       IBlastSweep blast, IServerDecals decals, IEngineEntityRender render,
                       IServerPropData propData, PhysicsDamageQueue impacts, FluidTouches fluid, PhysicsSoundBatch sounds,
                       PlayerPickupRules pickups)
        : base(damage, physics, world, effects, precache, host, blast, decals, render, propData, impacts, fluid, sounds,
               pickups)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PropPhysicsMultiplayerVars"/>.</summary>
    [ScriptVars] public PropPhysicsMultiplayerVars MultiplayerVars = new();

    /// <summary>
    /// The mapper's override: "simulate this one on the server whatever the
    /// physicsmode says". Stock spells it SF_PHYSPROP_FORCE_SERVER_SIDE.
    /// </summary>
    private const int ForceServerSide = 1 << 14;

    /// <summary>
    /// The resolved mode, after the map key, the spawnflag and this hierarchy's
    /// own limits have all had their say. Exposed for tests, which have no
    /// engine to ask.
    /// </summary>
    internal PhysicsMode ResolvedMode { get; private set; } = PhysicsMode.Autodetect;

    public override void OnSpawn()
    {
        base.OnSpawn();
        ResolvedMode = ResolveMode(MultiplayerVars.PhysicsMode, SpawnFlags);
    }

    /// <summary>
    /// The whole of the decision, as a function of two numbers, so it can be
    /// tested without a map and without a solver.
    ///
    /// <para>
    /// Autodetect stays autodetect rather than becoming a size-and-mass guess:
    /// stock's guess exists only to pick a mode, every mode this build can
    /// serve is served the same way, and a guess whose answers are
    /// indistinguishable is not a guess. Stock's autodetect branch is also the
    /// one that removes a prop whose model failed to load — see the guard in
    /// <c>CPhysicsPropMultiplayer::Spawn</c> — and a prop with no model is
    /// already handled by the shell, in one place, for every prop.
    /// </para>
    /// </summary>
    internal static PhysicsMode ResolveMode(int keyValue, int spawnFlags)
    {
        PhysicsMode mode = keyValue switch
        {
            1 => PhysicsMode.Solid,
            2 => PhysicsMode.NonSolid,
            3 => PhysicsMode.ClientSide,
            _ => PhysicsMode.Autodetect,
        };

        // The spawnflag beats the key, which is what "force" means.
        if ((spawnFlags & ForceServerSide) != 0)
            return PhysicsMode.NonSolid;

        // No client-side prop simulation here; see the class remark.
        return mode == PhysicsMode.ClientSide ? PhysicsMode.NonSolid : mode;
    }
}

/// <summary>
/// Where a physics prop is simulated. The numbers are stock's
/// PHYSICS_MULTIPLAYER_* and are written by maps, so they may not be renumbered.
/// </summary>
public enum PhysicsMode
{
    /// <summary>Work it out. What a map that sets no physicsmode asks for.</summary>
    Autodetect = 0,

    /// <summary>Simulated on the server and solid to everything.</summary>
    Solid = 1,

    /// <summary>Simulated on the server, but nothing collides with it.</summary>
    NonSolid = 2,

    /// <summary>Stock hands this one to the client. This build cannot; see the class remark.</summary>
    ClientSide = 3,
}

/// <summary>
/// The map keys <see cref="PropPhysicsMultiplayer"/> declares, on top of the
/// ones it inherits from <see cref="PropPhysicsVars"/>.
/// </summary>
[ScriptVars]
public partial class PropPhysicsMultiplayerVars
{
    /// <summary>One of the <see cref="Server.PhysicsMode"/> values.</summary>
    public int PhysicsMode { get; set; }
}
