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
/// Changes how fast whoever is inside falls. Low-gravity rooms.
/// </summary>
[ScriptedEntity(EntityNames.TriggerGravity, ScriptedEntityKind.Brush)]
public sealed class TriggerGravity : TriggerVolume
{
    public TriggerGravity(IGameClock clock, IEngineEntityPhysics physics)
        : base(clock)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>What a map may set on this entity. See <see cref="TriggerGravityVars"/>.</summary>
    [ScriptVars] public TriggerGravityVars TriggerGravityVars = new();

    protected override void Fire(EntityHandle toucher) => Physics.SetGravity(toucher, TriggerGravityVars.Gravity);

    // Restored on the way out, or the player keeps floating for the rest of the
    // round. The engine's version has this bug; there is no reason to keep it.
    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);
        Physics.SetGravity(toucher, 1f);
    }
}

/// <summary>
/// The map keys <see cref="TriggerGravity"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerGravityVars
{
    public float Gravity { get; set; } = 1f;
}
