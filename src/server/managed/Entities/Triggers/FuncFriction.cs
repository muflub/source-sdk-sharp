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
/// Changes how well whatever is inside grips the floor: stock's
/// CFrictionModifier (triggers.cpp, compiled for HL1 in the SDK). Its
/// StartTouch and EndTouch replace CBaseTrigger's, so no trigger spawnflag,
/// filter, wait or output is involved: anything but a player that enters gets
/// the modifier, and gets 1 back when it leaves (FuncFrictionTests).
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

    // Never reached: OnTouch below does not go through the volume's filter.
    protected override void Fire(EntityHandle toucher) { }

    public override void OnTouch(EntityHandle toucher)
    {
        if (!Entities.IsPlayer(toucher))
            Physics.SetFriction(toucher, FuncFrictionVars.Modifier / 100f);
    }

    public override void OnEndTouch(EntityHandle toucher)
    {
        if (!Entities.IsPlayer(toucher))
            Physics.SetFriction(toucher, 1f);
    }
}

/// <summary>
/// The map keys <see cref="FuncFriction"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncFrictionVars
{
    /// <summary>The friction as a percentage of standard; stock's m_frictionFraction starts at 0.</summary>
    [VarName("modifier")]
    public float Modifier { get; set; }
}
