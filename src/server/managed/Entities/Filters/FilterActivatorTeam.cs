//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Passes players on a given team.</summary>
[ScriptedEntity(EntityNames.FilterActivatorTeam)]
public sealed class FilterActivatorTeam : FilterBase
{
    public FilterActivatorTeam(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>What a map may set on this entity. See <see cref="FilterActivatorTeamVars"/>.</summary>
    [ScriptVars] public FilterActivatorTeamVars FilterActivatorTeamVars = new();

    // GetTeam answers -1 for anything that is not a player, which is also
    // the right answer here: a crate is on no team and passes no team filter.
    protected override bool Matches(EntityHandle entity)
        => FilterActivatorTeamVars.Team >= 0 && Players.GetTeam(entity) == FilterActivatorTeamVars.Team;
}

/// <summary>
/// The map keys <see cref="FilterActivatorTeam"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterActivatorTeamVars
{
    [VarName("filterteam")]
    public int Team { get; set; } = -1;
}
