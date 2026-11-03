//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Commands;

/// <summary>
/// <c>mp_forcewin</c> (teamplayroundbased_gamerules.cpp:362-399): the named
/// team wins the round — ALL_POINTS_CAPTURED, or STALEMATE for no team —
/// through the round machine's <see cref="ITeamRoundControl"/> with stock's
/// defaults. Without a team, player 1's (stock's UTIL_PlayerByIndex(1) arm,
/// the lowest-index player the roster holds), else stock's message.
/// </summary>
[ConCommand("mp_forcewin", Help = "Forces team to win", Flags = ConVarFlags.Cheat)]
public sealed class ForceWinCommand(ILog log, IServiceLocator services) : IConCommand
{
    public void Execute(string args)
    {
        string[] argv = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        TFTeam team;
        if (argv.Length == 0)
        {
            if (services.Resolve<ITeamRoundRoster>() is not { } roster)
                return;

            var everyone = new List<EntityHandle>();
            foreach (TFTeam held in new[] { TFTeam.Unassigned, TFTeam.Spectator, TFTeam.Red, TFTeam.Blue })
                everyone.AddRange(roster.Players(held));

            EntityHandle player = everyone.OrderBy(roster.EntIndex).FirstOrDefault();
            if (player == EntityHandle.None)
            {
                log.Msg("Unable to determine default team. Usage: mp_forcewin <opt: team#>\n");
                return;
            }
            team = roster.TeamOf(player);
        }
        else
        {
            if (argv.Length > 1)
            {
                log.Msg("Usage: mp_forcewin <opt: team#>\n");
                return;
            }
            team = (TFTeam)int.Parse(argv[0]);
        }

        if (services.Resolve<ITeamRoundControl>() is not { } rounds)
            return;

        WinReason reason = team == TFTeam.Unassigned ? WinReason.Stalemate : WinReason.AllPointsCaptured;
        rounds.SetWinningTeam(team, reason);
    }
}
