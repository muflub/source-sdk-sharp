//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// <c>game_round_win</c> (<c>CTeamplayRoundWin</c>, server/tf/entity_roundwin.cpp):
/// the map's way to end a round — most often a round timer's <c>OnFinished</c>
/// (Payload's defenders hold out). Its team wins with its reason; no team is a
/// timer stalemate (<see cref="ITeamRoundControl.SetStalemate"/>), which is
/// sudden death under <c>mp_stalemate_enable</c> and a win for nobody without it.
/// </summary>
[ScriptedEntity(Classname)]
public sealed class GameRoundWin(ITeamRoundControl rounds) : ScriptedEntity
{
    /// <summary>The classname.</summary>
    public const string Classname = "game_round_win";

    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public GameRoundWinVars Vars = new();

    public readonly Output OnRoundWin = new();

    /// <summary>The team this entity declares the winner (the entity's own <c>TeamNum</c>).</summary>
    public TFTeam Team => (TFTeam)Vars.TeamNum;

    [ScriptedEvent("SetTeam")] private void OnSetTeam(float team) => Vars.TeamNum = (int)team;

    [ScriptedEvent("RoundWin")] private void OnRoundWinInput() => RoundWin();

    /// <summary>Stock <c>RoundWin</c>.</summary>
    public void RoundWin()
    {
        var reason = (WinReason)Vars.WinReason;

        if (Team > TFTeam.Spectator)
        {
            if (!Vars.ForceMapReset)
                rounds.SetWinningTeam(Team, reason, Vars.ForceMapReset);
            else
                rounds.SetWinningTeam(Team, reason, Vars.ForceMapReset, Vars.SwitchTeams);
        }
        else
        {
            rounds.SetStalemate(StalemateReason.Timer, Vars.ForceMapReset, Vars.SwitchTeams);
        }

        OnRoundWin.Fire(Handle);
    }
}

/// <summary>The map keys of <see cref="GameRoundWin"/>.</summary>
[ScriptVars]
public partial class GameRoundWinVars
{
    /// <summary>The entity's team (CBaseEntity's <c>TeamNum</c> key).</summary>
    public int TeamNum { get; set; }

    [VarName("force_map_reset")] public bool ForceMapReset { get; set; }

    [VarName("switch_teams")] public bool SwitchTeams { get; set; }

    /// <summary>The constructor's default: a map-fired win defends until the time limit.</summary>
    [VarName("win_reason")] public int WinReason { get; set; } = (int)Shared.Rules.WinReason.DefendUntilTimeLimit;
}
