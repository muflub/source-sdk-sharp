//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities;

/// <summary>
/// Passes players on the filter's own team. TF2's own team filter
/// (tf_filters.cpp CFilterTFTeam): the map gives the filter a TeamNum, and
/// the activator passes when its team equals it — stock's own comparison
/// (GetTeamNumber() == GetTeamNumber()). Negated inverts the answer, as
/// every filter inherits from FilterBase.
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class FilterActivatorTFTeam : FilterBase
{
    public const string MapClass = "filter_activator_tfteam";

    public FilterActivatorTFTeam(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;

    /// <summary>What a map may set on this entity. See <see cref="FilterActivatorTFTeamVars"/>.</summary>
    [ScriptVars] public FilterActivatorTFTeamVars FilterActivatorTFTeamVars = new();

    // Stock compares the activator's team with the filter's own team number,
    // which the map sets through the entity's TeamNum key. Nobody's team
    // (an entity that is not a player) answers the unassigned team, which is
    // what stock compares against when the map left TeamNum unset.
    protected override bool Matches(EntityHandle entity)
        => Players.GetTeam(entity) == FilterActivatorTFTeamVars.TeamNum;
}

/// <summary>
/// The map keys <see cref="FilterActivatorTFTeam"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterActivatorTFTeamVars
{
    /// <summary>The team this filter answers to: the TeamNum key.</summary>
    public int TeamNum { get; set; }
}
