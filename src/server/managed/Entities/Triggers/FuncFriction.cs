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
/// Changes how well whoever is inside grips the floor. Ice.
/// </summary>
[ScriptedEntity(EntityNames.FuncFriction, ScriptedEntityKind.Brush)]
public sealed class FuncFriction : TriggerVolume
{
    public FuncFriction(IGameClock clock, IEngineEntityPhysics physics)
        : base(clock)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>What a map may set on this entity. See <see cref="FuncFrictionVars"/>.</summary>
    [ScriptVars] public FuncFrictionVars FuncFrictionVars = new();

    // The map writes a percentage.
    protected override void Fire(EntityHandle toucher) => Physics.SetFriction(toucher, FuncFrictionVars.Friction / 100f);

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);
        Physics.SetFriction(toucher, 1f);
    }
}

/// <summary>
/// The map keys <see cref="FuncFriction"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncFrictionVars
{
    [VarName("modifytofriction")]
    public float Friction { get; set; } = 100f;
}
