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
/// Deletes whatever walks in. Used to clean up debris that falls out of a level.
/// </summary>
[ScriptedEntity(EntityNames.TriggerRemove, ScriptedEntityKind.Brush)]
public sealed class TriggerRemove : TriggerVolume
{
    public TriggerRemove(IGameClock clock, IEnginePlayers players)
        : base(clock)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    public readonly Output OnRemove = new();

    protected override void Fire(EntityHandle toucher)
    {
        // Never a player: a trigger_remove that deletes people would empty the
        // server, and the engine's version excludes them for the same reason.
        if (Players.GetTeam(toucher) >= 0)
            return;

        OnRemove.Fire(toucher);
        Entities.Remove(toucher);
    }
}
