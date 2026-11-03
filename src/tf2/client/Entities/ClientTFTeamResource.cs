//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Shared.Resources;

namespace TF2.Client.Entities;

/// <summary>
/// The per-team table as the scoreboard and the HUD read it: stock's
/// <c>C_TFTeam</c> (c_tf_team.h:36-37, c_tf_team.cpp:40-54) over
/// <c>C_Team</c>'s score and rounds (c_team.cpp:98-111, :198-228), which stock
/// networked as one <c>tf_team</c> entity per team and this port networks as
/// one row per team.
/// </summary>
public interface ITFTeamSource
{
    /// <summary><c>m_iScore</c> (stock <c>C_Team::Get_Score</c>).</summary>
    int Score(int team);

    /// <summary><c>m_iRoundsWon</c> (stock <c>C_Team::GetRoundsWon</c>).</summary>
    int RoundsWon(int team);

    /// <summary><c>m_nFlagCaptures</c> (stock <c>C_TFTeam::GetFlagCaptures</c>).</summary>
    int FlagCaptures(int team);

    /// <summary><c>m_iRole</c> (stock <c>C_TFTeam::GetRole</c>: TEAM_ROLE_*).</summary>
    int Role(int team);

    /// <summary>The team's players (stock <c>C_Team::Get_Number_Players</c>, which read its player list's length).</summary>
    int PlayerCount(int team);
}

/// <summary>
/// The client half of <c>tf_team_resource</c>: the decoded
/// <see cref="TeamResourceNetState"/>, kept in <see cref="TFClientTeamResources"/>
/// from its first update to its removal.
/// </summary>
[ClientStandaloneEntity(ObjectiveClassNames.TeamResource)]
public sealed class ClientTFTeamResource : ClientStandaloneEntity
{
    private readonly TFClientTeamResources _teams;

    public ClientTFTeamResource(IEngineEntityRender render, TFClientTeamResources teams)
        : base(render)
    {
        _teams = teams;
    }

    public override string ClassName => ObjectiveClassNames.TeamResource;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TeamResourceNetState State = new();

    /// <summary>How many updates have decoded into <see cref="State"/>.</summary>
    public int Decodes { get; private set; }

    public override void OnSpawn() => _teams.Live = this;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;
        _teams.Live = this;
    }

    public override void OnRemoved()
    {
        if (ReferenceEquals(_teams.Live, this))
            _teams.Live = null;
    }
}

/// <summary>The client's live <c>tf_team_resource</c>, when one has arrived.</summary>
[SideService(Side.Client, As = [typeof(ITFTeamSource)])]
public sealed class TFClientTeamResources : ITFTeamSource
{
    public ClientTFTeamResource? Live { get; set; }

    /// <summary>Whether a team index names one of the table's rows.</summary>
    private static bool Known(int team) => team >= 0 && team < ObjectiveLimits.Teams;

    public int Score(int team) => Known(team) && Live is { } row ? row.State.Score[team] : 0;

    public int RoundsWon(int team) => Known(team) && Live is { } row ? row.State.RoundsWon[team] : 0;

    public int FlagCaptures(int team) => Known(team) && Live is { } row ? row.State.FlagCaptures[team] : 0;

    public int Role(int team) => Known(team) && Live is { } row ? row.State.Role[team] : 0;

    public int PlayerCount(int team) => Known(team) && Live is { } row ? row.State.PlayerCount[team] : 0;
}
