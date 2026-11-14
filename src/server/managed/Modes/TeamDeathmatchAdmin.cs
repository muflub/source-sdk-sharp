using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A mode that can reshuffle its teams and even them out (team deathmatch):
/// what the scramble vote and the admin menu's Teams folder call.
/// </summary>
public interface ITeamScramble
{
    /// <summary>Every player of the teams in play dealt across them evenly in a seeded order, then the round restarts.</summary>
    void Scramble();

    /// <summary>The newest arrivals moved off the largest team until no team has more than one player over another.</summary>
    void Balance();
}

/// <summary>
/// <c>cs_sv_scramble_teams</c>, what a passed scramble vote runs (stock TF2's
/// <c>CScrambleTeams</c> runs <c>mp_scrambleteams 2</c>, tf_voteissues.cpp:1204;
/// the TF2 mod declares that name itself). Under a mode that cannot scramble it
/// refuses out loud.
/// </summary>
[ConCommand(Name, Help = "Team deathmatch: reshuffle the teams evenly and restart the round")]
public sealed class TeamScrambleCommand(ILog log, GameModes modes)
{
    public const string Name = "cs_sv_scramble_teams";

    public void Execute()
    {
        if (modes.Active is ITeamScramble scramble)
        {
            log.Msg("[SourceSharp/Teams] scrambling the teams\n");
            scramble.Scramble();
            return;
        }

        log.Msg($"[SourceSharp/Teams] {Name} refused: '{modes.ActiveId}' plays without teams\n");
    }
}

/// <summary>
/// The admin menu's Teams folder under team deathmatch (user 2026-11-08: "make
/// sure to add vote and admin menus for teams"): each player's moves to every
/// team in play and to the spectators (a move between teams kills, as a
/// switch does), Scramble, Balance now, and the team count. Absent while a
/// mode without teams plays.
/// </summary>
public sealed class TeamAdminMenu(GameModes modes, ITeams teams, ITeamAssignment assign, IEnginePlayers players,
                                  PlayerNames names, IEngineConsole console) : IAdminMenuProvider
{
    public IEnumerable<AdminNode> AdminItems(int slot)
    {
        if (modes.Active is not TeamDeathmatchMode tdm)
            yield break;

        yield return AdminNode.Folder("#SS_UI_Admin_Teams",
        [
            .. Players(tdm),
            new AdminNode("#SS_UI_Admin_Scramble", "teams:scramble", tdm.Scramble),
            new AdminNode("#SS_UI_Admin_Balance", "teams:balance", tdm.Balance),
            AdminNode.Folder("#SS_UI_Admin_TeamCount",
                [.. Enumerable.Range(2, 3).Where(n => n != tdm.TeamCount)
                              .Select(n => new AdminNode(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"teams:count:{n}",
                                                         () => console.ExecuteServerCommand($"mp_tdm_teams {n}\n")))]),
        ]);
    }

    /// <summary>
    /// One folder a player, by user id (no later player reuses one), of the
    /// teams they are not on.
    /// </summary>
    private IEnumerable<AdminNode> Players(TeamDeathmatchMode tdm)
    {
        for (int team = 0; team <= tdm.LastTeam; team++)
        {
            foreach (EntityHandle player in teams.Players(team))
            {
                int userId = players.GetUserId(player);
                int on = team;
                var moves = new List<AdminNode>();
                for (int to = TeamDeathmatchMode.TeamCombine; to <= tdm.LastTeam; to++)
                {
                    if (to == on)
                        continue;
                    int target = to;
                    moves.Add(new AdminNode($"#SS_UI_Team_{target}", $"teams:move:{userId}:{target}", () => assign.Assign(player, target)));
                }

                if (on != Teams.TeamSpectator)
                    moves.Add(new AdminNode("#SS_UI_Admin_Spectate", $"teams:move:{userId}:1", () => teams.Join(player, Teams.TeamSpectator)));

                yield return AdminNode.Folder(names.NameOf(player.EntryIndex), moves);
            }
        }
    }
}
