//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Shared;

namespace TF2.Server.Entities;

/// <summary>PlayerTeamSpawnMode_t (entity_tfstart.h).</summary>
public enum TeamSpawnMode
{
    Normal = 0,
    Triggered = 1,
}

/// <summary>PlayerTeamSpawn_MatchSummary_t (entity_tfstart.h).</summary>
public enum TeamSpawnMatchSummary
{
    None = 0,
    Loser = 1,
    Winner = 2,
}

/// <summary>
/// info_player_teamspawn: where a team's players spawn (stock's CTFTeamSpawn,
/// entity_tfstart.cpp). A position, a team, an on/off switch, and the point
/// or mini-round that decides when it is live; <see cref="TFSpawnSelector"/>
/// chooses among them and <see cref="IControlPointRules"/> switches them.
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class InfoPlayerTeamSpawn(IObjectiveEntities objectives) : ScriptedEntity
{
    public const string MapClass = "info_player_teamspawn";

    [ScriptVars] public InfoPlayerTeamSpawnVars Vars = new();

    private bool _disabled;
    private int _team;

    public override void OnSpawn()
    {
        _disabled = Vars.StartDisabled;
        _team = Vars.TeamNum;
    }

    /// <summary>
    /// Not stock's moment: CTFTeamSpawn finds its point and rounds only on
    /// RoundSpawn. Finding them once the map exists as well costs nothing and
    /// keeps a spawn whose round flow has not started a round still tied to
    /// its point.
    /// </summary>
    public override void OnServerActivate() => FindLinks();

    /// <summary>The team this spawn is for (stock's TeamNum, changed by SetupSpawnPointsForRound).</summary>
    public int Team
    {
        get => _team;
        set => _team = value;
    }

    public bool Disabled
    {
        get => _disabled;
        set => _disabled = value;
    }

    public TeamSpawnMode SpawnMode => (TeamSpawnMode)Vars.SpawnMode;

    public TeamSpawnMatchSummary MatchSummary => (TeamSpawnMatchSummary)Vars.MatchSummary;

    public TeamControlPoint? ControlPoint { get; private set; }

    public TeamControlPointRound? RoundBlueSpawn { get; private set; }

    public TeamControlPointRound? RoundRedSpawn { get; private set; }

    [ScriptedEvent] private void OnEnable() => _disabled = false;

    [ScriptedEvent] private void OnDisable() => _disabled = true;

    /// <summary>InputRoundSpawn: re-find the point and the rounds, which are recreated over round restarts in stock.</summary>
    [ScriptedEvent]
    private void OnRoundSpawn() => FindLinks();

    private void FindLinks()
    {
        if (Vars.ControlPoint.Length > 0)
        {
            ControlPoint = objectives.FindByName<TeamControlPoint>(Vars.ControlPoint);
            if (ControlPoint is null)
                Log.Warning($"{MapClass} failed to find control point named '{Vars.ControlPoint}'\n");
        }

        if (Vars.RoundBlueSpawn.Length > 0)
        {
            RoundBlueSpawn = objectives.FindByName<TeamControlPointRound>(Vars.RoundBlueSpawn);
            if (RoundBlueSpawn is null)
                Log.Warning($"{MapClass} failed to find control point round named '{Vars.RoundBlueSpawn}'\n");
        }

        if (Vars.RoundRedSpawn.Length > 0)
        {
            RoundRedSpawn = objectives.FindByName<TeamControlPointRound>(Vars.RoundRedSpawn);
            if (RoundRedSpawn is null)
                Log.Warning($"{MapClass} failed to find control point round named '{Vars.RoundRedSpawn}'\n");
        }
    }
}

/// <summary>The map keys <see cref="InfoPlayerTeamSpawn"/> declares (entity_tfstart.cpp:21-28, and CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class InfoPlayerTeamSpawnVars
{
    public bool StartDisabled { get; set; }

    /// <summary>CBaseEntity::KeyValue's TeamNum: a server-only point has no shell to keep it.</summary>
    public int TeamNum { get; set; }

    [VarName("controlpoint")] public string ControlPoint { get; set; } = string.Empty;
    [VarName("round_bluespawn")] public string RoundBlueSpawn { get; set; } = string.Empty;
    [VarName("round_redspawn")] public string RoundRedSpawn { get; set; } = string.Empty;
    public int SpawnMode { get; set; }
    public int MatchSummary { get; set; }
}
