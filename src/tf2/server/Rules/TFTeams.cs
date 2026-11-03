//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// TF2's join rules (<see cref="IJoinTeamPolicy"/>, the single owner): the
/// refusals of <c>CTFPlayer::HandleCommand_JoinTeam</c> — no team change once
/// the game is over, and none that would unbalance the teams
/// (<c>WouldChangeUnbalanceTeams</c>) — and its <c>GetAutoTeam</c>.
///
/// <para>
/// Asked only while a TF2 mode is active: under another mode (<c>sv_mode
/// null</c>'s deathmatch) every real team is accepted, the core's own answer.
/// Stock answers an unbalancing join by showing the team menu again
/// (<c>ShowViewPortPanel( PANEL_TEAM )</c>) with no text; the core says a
/// refusal's reason to the player, so this one says why (a divergence row).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFTeams(TeamBalance balance, ITeamRoundRoster roster, IActiveTeamRoundMode mode,
                            ITeamRoundState round, IRandomSeeds seeds, ITeamRoundSettings settings) : IJoinTeamPolicy
{
    /// <summary>What the joiner is told when the team they asked for is full.</summary>
    public const string UnbalancedReason = "That team is full: joining it would unbalance the teams.";

    /// <summary>What the joiner is told after the game has ended.</summary>
    public const string GameOverReason = "The game is over.";

    private readonly Random _random = new(seeds.Next());

    public bool MayJoin(EntityHandle player, int requested, out string reason)
    {
        reason = string.Empty;

        if (mode.Current is null)
            return true;

        if (round.State == RoundState.GameOver)
        {
            reason = GameOverReason;
            return false;
        }

        if (balance.WouldChangeUnbalanceTeams((TFTeam)requested, roster.TeamOf(player)))
        {
            reason = UnbalancedReason;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The spectator arm's unbalance refusal
    /// (<c>HandleCommand_JoinTeam</c>, tf_player.cpp:6364-6381): with
    /// <c>mp_spectators_restricted</c>, a player on Red or Blue may not
    /// Spectate when their leaving widens the gap to
    /// <c>mp_teams_unbalance_limit</c>. Stock asks it after the core's
    /// mp_allowspectators gate, ours before it — both gates down, the
    /// unbalance line is the one printed — and beside
    /// <c>tf_mm_trusted</c>, which this port has not, under !MvM, a mode the
    /// port has not either.
    /// </summary>
    public bool MaySpectate(EntityHandle player)
    {
        if (!settings.SpectatorsRestricted)
            return true;

        TFTeam mine = roster.TeamOf(player);
        if (mine is not (TFTeam.Red or TFTeam.Blue))
            return true;

        int others = roster.Players(mine == TFTeam.Red ? TFTeam.Blue : TFTeam.Red).Count;
        int gap = others - roster.Players(mine).Count;
        return gap < settings.TeamsUnbalanceLimit;
    }

    /// <summary>
    /// Stock <c>CTFPlayer::GetAutoTeam</c> outside MvM, highlander and the bot
    /// quota: the team with fewer players; on even teams the attackers (Blue)
    /// on an attack/defend map, else <paramref name="preferred"/>, else a coin.
    /// </summary>
    public TFTeam AutoTeam(TFTeam preferred = TFTeam.Unassigned)
    {
        int blue = roster.Players(TFTeam.Blue).Count;
        int red = roster.Players(TFTeam.Red).Count;

        if (blue < red)
            return TFTeam.Blue;

        if (red < blue)
            return TFTeam.Red;

        // AutoTeam gives new players to the attackers on A/D maps if the teams are even.
        if (mode.Current is { EvenTeamsJoinAttackers: true })
            return TFTeam.Blue;

        if (preferred is TFTeam.Red or TFTeam.Blue)
            return preferred;

        return _random.Next(0, 2) == 1 ? TFTeam.Red : TFTeam.Blue;
    }
}
