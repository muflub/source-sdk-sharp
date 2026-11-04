//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Server.Entities;
using TF2.Server.Rules;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf_listrounds</c> (team_control_point_master.cpp:1267-1303): the
/// current map's rounds, one name a line, or the no-rounds word when the
/// master plays none.
/// </summary>
[ConCommand("tf_listrounds", Help = "List the rounds for the current map", Flags = ConVarFlags.Cheat)]
public sealed class ListRoundsCommand(ILog log, IServiceLocator services)
{
    public void Execute()
    {
        // Resolved at execute: commands bind at registration, before the mods' services exist.
        if (services.Resolve<IObjectiveEntities>() is not { } entities || entities.Master is not { } master)
        {
            log.Warning("[TF2/Objectives] tf_listrounds: no control point master\n");
            return;
        }

        if (!master.PlayingMiniRounds)
        {
            log.Msg("* No rounds in this map *\n");
            return;
        }

        log.Msg("Rounds in this map:\n\n");
        foreach (TeamControlPointRound round in master.Rounds)
            log.Msg($"{round.Name}\n");
    }
}

/// <summary>
/// <c>tf_playround &lt;round&gt;</c> (team_control_point_master.cpp:1306-1341):
/// names the round to play next and restarts the game to reach it. Only a map
/// whose master plays mini-rounds answers; a name no round holds is said, as
/// stock's ConMsg does.
/// </summary>
[ConCommand("tf_playround", Help = "Play the selected round\n\tArgument: {round name given by \"tf_listrounds\" command}", Flags = ConVarFlags.Cheat)]
public sealed class PlayRoundCommand(ILog log, IServiceLocator services)
{
    private IEnumerable<string> Rounds()
        => services.Resolve<IObjectiveEntities>()?.Master is { } master ? master.Rounds.Select(r => r.Name) : [];

    public void Execute([Complete(From = nameof(Rounds))] string name)
    {
        if (services.Resolve<IObjectiveEntities>() is not { } entities
            || entities.Master is not { } master
            || !master.PlayingMiniRounds)
        {
            return;
        }

        if (entities.FindByName<TeamControlPointRound>(name) is not { } round)
        {
            log.Msg($"* Round \"{name}\" not found in this map *\n");
            return;
        }

        if (services.Resolve<IObjectiveRoundRules>() is not { } rules
            || services.Resolve<ITeamRoundSettings>() is not { } settings)
        {
            log.Warning("[TF2/Objectives] tf_playround: the round machine is not up yet\n");
            return;
        }

        rules.RoundToPlayNext = round.Name;
        settings.RestartGame = 5;
    }
}
