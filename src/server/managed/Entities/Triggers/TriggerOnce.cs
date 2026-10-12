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
/// Fires the first time and then never again. The same volume as
/// trigger_multiple; the difference is entirely in the remembering.
/// </summary>
[ScriptedEntity(EntityNames.TriggerOnce, ScriptedEntityKind.Brush)]
public sealed class TriggerOnce : TriggerVolume
{
    public TriggerOnce(IGameClock clock)
        : base(clock)
    {
    }

    private bool _fired;

    protected override void Fire(EntityHandle toucher)
    {
        if (_fired)
            return;

        _fired = true;
        OnTrigger.Fire();
        Remove();
    }
}
