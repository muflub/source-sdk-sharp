//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// How far apart team deathmatch lets the teams grow before it refuses a join
/// (plan_bots_teams_tf2 A.3 item 3, Q11: off by default). Stock HL2DM has no
/// such limit; the rule is stock TF2's <c>mp_teams_unbalance_limit</c>
/// (teamplayroundbased_gamerules.cpp:219), under the core's own name because
/// the TF2 mod declares that name itself and one name has one declaration.
/// </summary>
[ConVar("cs_sv_teams_unbalance_limit", "0", Min = 0, Max = 30,
        Help = "Team deathmatch: teams are unbalanced when one team has this many more players than the other team. (0 disables check)")]
public sealed class TeamsUnbalanceLimitConVar : IntConVar { }

/// <summary>
/// How many teams team deathmatch plays with (user 2026-11-08: "also support
/// 2/3/4 teams"): 2 is stock's Combine and Rebels; 3 adds Metro Police (team
/// 4); 4 adds Resistance (team 5). Replicated, so a client knows how many
/// teams exist, empty ones included. Out-of-range values are clamped
/// (<see cref="TeamDeathmatchMode.TeamCount"/>). A change mid-level re-teams
/// whoever stood on a team that no longer exists and restarts the round.
/// </summary>
[ConVar("mp_tdm_teams", "2", Min = 2, Max = 4, Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Team deathmatch: how many teams play (2-4)")]
public sealed class TdmTeamsConVar : IntConVar { }

/// <summary>
/// Team deathmatch's join rule, the active mode's
/// <see cref="IGameMode.JoinPolicy"/>:
///
/// <list type="bullet">
/// <item><c>jointeam 0</c> is the auto-assign (<see cref="ITeamAutoAssign"/>),
/// stock <c>CHL2MP_Player::PickDefaultSpawnTeam</c>'s smaller team and a
/// random one on a tie (hl2mp_player.cpp:280-328), across every team in play
/// (<see cref="TdmTeamsConVar"/>; <c>jointeam N</c> is valid up to the last
/// one, <see cref="ITeamCount"/>). Stock reaches it only at
/// a spawn on Unassigned and refuses <c>jointeam 0</c> outright; answering
/// the command too is a divergence row (the team menu's AUTO ASSIGN and the
/// bots send it). A player already on a playing team is counted out of the
/// teams and keeps their team on a tie, so asking twice never moves them.</item>
/// <item>A move between two playing teams kills a living player
/// (<see cref="ITeamChangeWatcher"/>): stock <c>CHL2MP_Player::ChangeTeam</c>'s
/// <c>bKill</c> under teamplay (hl2mp_player.cpp:977-983, 1005-1008),
/// <c>CommitSuicide</c> — the player is their own attacker, so the frag and
/// the new team's point go as any suicide's.</item>
/// <item>A join that would leave the chosen team more than
/// <see cref="TeamsUnbalanceLimitConVar"/> players ahead is refused; 0 never
/// refuses.</item>
/// </list>
/// </summary>
public sealed class TeamDeathmatchJoinPolicy(ITeams teams, IEnginePlayers players, IEngineDamage damage,
                                             TeamsUnbalanceLimitConVar limit, Random random, Func<int>? teamCount = null)
    : IJoinTeamPolicy, ITeamAutoAssign, ITeamChangeWatcher, ITeamCount
{
    /// <summary>What the refusal says, to the player who asked.</summary>
    public const string UnbalancedReason = "That team has too many players.";

    /// <summary>CommitSuicide's damage, the house suicide's number (PlayerSelfCommand).</summary>
    private const float SuicideDamage = PlayerSelfCommand.SuicideDamage;

    /// <summary>How many teams play now: 2 unless the mode says (mp_tdm_teams).</summary>
    public int TeamCount => teamCount?.Invoke() ?? 2;

    /// <summary>The highest playing team: Combine (2) and on, one a team.</summary>
    public int LastTeam => TeamDeathmatchMode.TeamCombine + TeamCount - 1;

    public bool MayJoin(EntityHandle player, int requested, out string reason)
    {
        reason = string.Empty;
        int over = limit.Value;
        if (over <= 0 || !IsPlayingTeam(requested))
            return true;

        int joined = CountWithout(requested, player) + 1;
        int smallest = PlayingTeams().Where(t => t != requested).Min(t => CountWithout(t, player));
        if (joined - smallest <= over)
            return true;

        reason = UnbalancedReason;
        return false;
    }

    /// <summary>
    /// The smallest of the playing teams, a random one among the smallest on
    /// a tie; a player already on one of the smallest keeps it.
    /// </summary>
    public int AutoTeam(EntityHandle player)
    {
        var counts = PlayingTeams().Select(t => (Team: t, Count: CountWithout(t, player))).ToArray();
        int fewest = counts.Min(c => c.Count);
        int[] smallest = [.. counts.Where(c => c.Count == fewest).Select(c => c.Team)];

        if (smallest.Length == 1)
            return smallest[0];

        int current = teams.Team(player);
        if (smallest.Contains(current))
            return current;

        // random->RandomInt( TEAM_COMBINE, TEAM_REBELS ) with two teams.
        return smallest[random.Next(smallest.Length)];
    }

    public void TeamChanged(EntityHandle player, int oldTeam, int newTeam)
    {
        if (!IsTeam(oldTeam) || !IsPlayingTeam(newTeam) || !players.IsAlive(player))
            return;

        damage.Apply(player, player, SuicideDamage, DamageType.Generic);
    }

    private IEnumerable<int> PlayingTeams()
        => Enumerable.Range(TeamDeathmatchMode.TeamCombine, TeamCount);

    private int CountWithout(int team, EntityHandle player)
        => teams.Players(team).Count(p => p != player);

    private bool IsPlayingTeam(int team)
        => team >= TeamDeathmatchMode.TeamCombine && team <= LastTeam;

    /// <summary>Any of team deathmatch's four teams, playing now or not (the one a player is leaving may be gone).</summary>
    private static bool IsTeam(int team)
        => team >= TeamDeathmatchMode.TeamCombine && team <= TeamDeathmatchMode.LastPossibleTeam;
}
