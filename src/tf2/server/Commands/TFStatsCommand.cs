using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf2_stats [player]</c>: the scored stats of each rostered player (or the
/// named index) as the server holds them, round and accumulated — captures,
/// defenses, flag returns, kills — beside the round and accumulated score the
/// scoreboard sends. A readout of what <see cref="ITFPlayerStats"/> already
/// answers, so a lane can read a stat from the server's console without a
/// client's scoreboard. Not behind <c>sv_cheats</c>: it shows only what the
/// scoreboard shows.
/// </summary>
[ConCommand("tf2_stats", Help = "Each player's scored stats, round/accumulated. tf2_stats [player]")]
public sealed class TFStatsCommand(ILog log, ITFPlayerStats stats, ITeamRoundRoster roster)
{
    public void Execute(int index = 0)
    {
        TFTeam[] teams = [TFTeam.Unassigned, TFTeam.Spectator, TFTeam.Red, TFTeam.Blue];
        foreach (TFTeam team in teams)
            foreach (EntityHandle player in roster.Players(team))
            {
                int at = roster.EntIndex(player);
                if (index == 0 || index == at)
                    Print(at, team, roster.Name(player));
            }
    }

    private void Print(int index, TFTeam team, string name)
    {
        string Pair(TFStat stat) => $"{stats.Round(index, stat)}/{stats.Accumulated(index, stat)}";

        log.Msg($"[TF2/Stats] {index} {name} team {(int)team} captures {Pair(TFStat.Captures)} " +
                $"defenses {Pair(TFStat.Defenses)} flagreturns {Pair(TFStat.Flagreturns)} kills {Pair(TFStat.Kills)} " +
                $"score {stats.RoundScore(index)}/{stats.Score(index)}\n");
    }
}
