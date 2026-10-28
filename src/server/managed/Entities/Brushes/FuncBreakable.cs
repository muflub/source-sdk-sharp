//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>func_breakable: a <see cref="BreakableBrush"/> that stands still (CBreakable itself).</summary>
[ScriptedEntity(EntityNames.FuncBreakable, ScriptedEntityKind.Brush)]
public sealed class FuncBreakable(IServerPropData propData, IEngineSound sound, IEnginePrecache precache, ITempEnts temps,
                                  IEngineHost host, IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                                  IBlastSweep blast, IServerDecals decals, IEngineEntityPhysics physics,
                                  ConVarRegistry? convars = null)
    : BreakableBrush(propData, sound, precache, temps, host, damage, world, effects, blast, decals, physics, convars)
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;
}
