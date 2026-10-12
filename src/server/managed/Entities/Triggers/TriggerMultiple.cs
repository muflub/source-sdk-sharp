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
/// Fires every time something walks in. The workhorse: doors, lifts, ambushes,
/// anything a map does in response to somebody arriving somewhere.
/// </summary>
[ScriptedEntity(EntityNames.TriggerMultiple, ScriptedEntityKind.Brush)]
public sealed class TriggerMultiple : TriggerVolume
{
    public TriggerMultiple(IGameClock clock)
        : base(clock)
    {
    }

    protected override void Fire(EntityHandle toucher) => OnTrigger.Fire();
}
