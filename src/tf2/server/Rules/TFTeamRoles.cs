//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>Stock's anonymous <c>TEAM_ROLE_*</c> enum (tf_shareddefs.h:74-76).</summary>
public enum TFTeamRole
{
    /// <summary><c>TEAM_ROLE_NONE</c>: the unset answer.</summary>
    None = 0,

    /// <summary><c>TEAM_ROLE_DEFENDERS</c>.</summary>
    Defenders,

    /// <summary><c>TEAM_ROLE_ATTACKERS</c>.</summary>
    Attackers,
}

/// <summary>One role per game team, as the maps' role inputs leave it.</summary>
public interface ITFTeamRoles
{
    /// <summary>The role a team stands as; <see cref="TFTeamRole.None"/> while unset.</summary>
    TFTeamRole RoleOf(TFTeam team);

    /// <summary>Names the team's role, stock's <c>CTFTeam::SetRole</c> (tf_team.h:57).</summary>
    void SetRole(TFTeam team, TFTeamRole role);
}

/// <summary>
/// The team roles (stock's <c>CTFTeam::SetRole</c>/<c>GetRole</c>, tf_team.h:57-58:
/// one int per team on the team object). The maps speak them through the rules
/// proxy's <c>SetRedTeamRole</c>/<c>SetBlueTeamRole</c> inputs
/// (tf_gamerules.cpp:1605-1606, the handlers :1785-1806), and the even-teams
/// arm of <c>CTFPlayer::GetAutoTeam</c> reads RED's
/// (<c>tf_player.cpp:6129-6135</c>: on an attack/defend map a new player goes
/// to the attackers when RED defends). The store clears with each level, as
/// stock's team objects are remade with the level.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFTeamRoles)])]
public sealed class TFTeamRoles : ITFTeamRoles, IGameLifecycle
{
    private readonly Dictionary<TFTeam, TFTeamRole> _roles = new();

    /// <summary>Stock's <c>GetRole</c>: a team no one named stands at None.</summary>
    public TFTeamRole RoleOf(TFTeam team) => _roles.TryGetValue(team, out TFTeamRole role) ? role : TFTeamRole.None;

    /// <inheritdoc cref="ITFTeamRoles.SetRole"/>
    public void SetRole(TFTeam team, TFTeamRole role) => _roles[team] = role;

    /// <summary>The rules' band, the +4 slot: the store clears as each level loads.</summary>
    int IGameLifecycle.LifecycleOrder => TeamRoundOrders.Band + 4;

    void IGameLifecycle.OnLevelInit(string mapName) => _roles.Clear();
}
