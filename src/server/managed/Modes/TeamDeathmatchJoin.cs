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
/// Team deathmatch's join rule, the active mode's
/// <see cref="IGameMode.JoinPolicy"/>:
///
/// <list type="bullet">
/// <item><c>jointeam 0</c> is the auto-assign (<see cref="ITeamAutoAssign"/>),
/// stock <c>CHL2MP_Player::PickDefaultSpawnTeam</c>'s smaller team and a
/// random one on a tie (hl2mp_player.cpp:280-328). Stock reaches it only at
/// a spawn on Unassigned and refuses <c>jointeam 0</c> outright; answering
/// the command too is a divergence row (the team menu's AUTO ASSIGN and the
/// bots send it). A player already on a playing team is counted out of the
/// teams and keeps their team on a tie, so asking twice never moves them.</item>
/// <item>A move between the two playing teams kills a living player
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
                                             TeamsUnbalanceLimitConVar limit, Random random)
    : IJoinTeamPolicy, ITeamAutoAssign, ITeamChangeWatcher
{
    /// <summary>What the refusal says, to the player who asked.</summary>
    public const string UnbalancedReason = "That team has too many players.";

    /// <summary>CommitSuicide's damage, the house suicide's number (PlayerSelfCommand).</summary>
    private const float SuicideDamage = PlayerSelfCommand.SuicideDamage;

    public bool MayJoin(EntityHandle player, int requested, out string reason)
    {
        reason = string.Empty;
        int over = limit.Value;
        if (over <= 0 || !IsPlayingTeam(requested))
            return true;

        int other = requested == TeamDeathmatchMode.TeamCombine ? TeamDeathmatchMode.TeamRebels : TeamDeathmatchMode.TeamCombine;
        int joined = CountWithout(requested, player) + 1;
        int left = CountWithout(other, player);
        if (joined - left <= over)
            return true;

        reason = UnbalancedReason;
        return false;
    }

    public int AutoTeam(EntityHandle player)
    {
        int combine = CountWithout(TeamDeathmatchMode.TeamCombine, player);
        int rebels = CountWithout(TeamDeathmatchMode.TeamRebels, player);

        if (combine > rebels)
            return TeamDeathmatchMode.TeamRebels;
        if (combine < rebels)
            return TeamDeathmatchMode.TeamCombine;

        int current = teams.Team(player);
        if (IsPlayingTeam(current))
            return current;

        // random->RandomInt( TEAM_COMBINE, TEAM_REBELS ).
        return random.Next(TeamDeathmatchMode.TeamCombine, TeamDeathmatchMode.TeamRebels + 1);
    }

    public void TeamChanged(EntityHandle player, int oldTeam, int newTeam)
    {
        if (!IsPlayingTeam(oldTeam) || !IsPlayingTeam(newTeam) || !players.IsAlive(player))
            return;

        damage.Apply(player, player, SuicideDamage, DamageType.Generic);
    }

    private int CountWithout(int team, EntityHandle player)
        => teams.Players(team).Count(p => p != player);

    private static bool IsPlayingTeam(int team)
        => team is TeamDeathmatchMode.TeamCombine or TeamDeathmatchMode.TeamRebels;
}
