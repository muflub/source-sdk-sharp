//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared.Rules;

namespace TF2.Server.Commands;

/// <summary>
/// <c>mp_switchteams</c> (teamplayroundbased_gamerules.cpp:276-292): switch
/// the teams and restart the game. Behind the server-admin rule
/// (UTIL_IsCommandIssuedByServerAdmin :278, ours
/// <see cref="ServerAdmin.MayRun"/>), it arms the switch, sets the five-second
/// game restart (:285) and disarms every arm of the reset that restart will
/// run — the switch trades the team scores, so they must survive
/// (<c>ShouldResetScores(false, false)</c> :286) and the rounds played with
/// them (:287).
/// </summary>
[ConCommand("mp_switchteams", Help = "Switch teams and restart the game")]
public sealed class SwitchTeamsCommand(IServiceLocator services, IEnginePlayers players, IEngineHost host)
{
    public void Execute()
    {
        if (!ServerAdmin.MayRun(players, host))
            return;

        if (services.Resolve<ITeamRoundControl>() is not { } rounds)
            return;

        rounds.SetSwitchTeams(true);
        if (services.Resolve<ITeamRoundSettings>() is { } settings)
            settings.RestartGame = 5;   // stock's mp_restartgame.SetValue(5)
        rounds.ShouldResetScores(false, false);
        rounds.ShouldResetRoundsPlayed(false);
    }
}

/// <summary>
/// <c>mp_scrambleteams</c> (teamplayroundbased_gamerules.cpp:297-321):
/// scramble the teams and restart the game, behind the same admin rule
/// (:299). The team scores reset with the restart (the redraw makes them
/// meaningless) but the players' accumulated stats do not (:307): the
/// scramble sorts and balances by them. A second argument of 2 keeps the
/// rounds played too (:309-316).
/// </summary>
[ConCommand("mp_scrambleteams", Help = "Scramble the teams and restart the game")]
public sealed class ScrambleTeamsCommand(IServiceLocator services, IEnginePlayers players, IEngineHost host)
{
    public void Execute(int keepRounds = 0)
    {
        if (!ServerAdmin.MayRun(players, host))
            return;

        if (services.Resolve<ITeamRoundControl>() is not { } rounds)
            return;

        rounds.SetScrambleTeams(true);
        if (services.Resolve<ITeamRoundSettings>() is { } settings)
            settings.RestartGame = 5;   // stock's mp_restartgame.SetValue(5)
        rounds.ShouldResetScores(true, false);

        // ("" and bare whitespace are ArgC() == 1 here).
        if (keepRounds == 2)
            rounds.ShouldResetRoundsPlayed(false);
    }
}
