//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// The welcome menu's close, stock's <c>closedwelcomemenu</c> and
/// <c>show_motd</c> client commands (<c>CTFPlayer::ClientCommand</c>'s arms,
/// tf_player.cpp:7370-7407 and :7767-7796) — where <c>mp_forceautoteam</c>
/// acts.
///
/// <para>
/// <b>closedwelcomemenu.</b> Teamless: under <see cref="ITeamRoundSettings.ForceAutoTeam"/>
/// the player is changed to <see cref="TFTeams.AutoTeam"/> at once and the
/// class panel of the team they landed on shows; otherwise the team panel
/// shows again (:7374-7384). On a game team with no class, the class panel
/// shows again (:7386-7403) — <c>tf_arena_force_class</c> stays false with
/// arena deferred, as <see cref="TFJoinTeamCommand"/>'s reading has it.
/// </para>
///
/// <para>
/// <b>show_motd.</b> Under the force, the player is auto-teamed whatever
/// team they are on, then the class panel (:7771-7782). Stock prefers the
/// player history's last team (<c>PlayerHistory_GetPlayerInfo</c>); no
/// history is kept in this port, so the ask is plain <c>AUTOASSIGN</c>.
/// Without the force, the team panel is hidden (:7794). Stock's standing
/// arms — <c>PANEL_ARENA_TEAM</c> hidden and the <c>PANEL_INFO</c> motd
/// itself (:7796-7812) — are the menus' (Stage 3).
/// </para>
///
/// <para>Stock's 0.3 s rate limit is not carried, as TFJoinClassCommand's.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFWelcomeMenuCommands(ITeams teams, TFTeams policy, ITeamRoundSettings settings,
                                          TFPlayerCarriers carriers, IUserMessageSender messages)
    : IClientCommandHook, IOrderedHook
{
    /// <summary>The stock word for the welcome menu's close.</summary>
    public const string ClosedWelcomeMenu = "closedwelcomemenu";

    /// <summary>The stock word for the rules' showing.</summary>
    public const string ShowMotd = "show_motd";

    public int HookOrder => TeamRoundOrders.WelcomeMenu;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (string.Equals(command, ClosedWelcomeMenu, StringComparison.OrdinalIgnoreCase))
        {
            WelcomeClosed(player);
            return true;
        }

        if (string.Equals(command, ShowMotd, StringComparison.OrdinalIgnoreCase))
        {
            MotdClosed(player);
            return true;
        }

        return false;
    }

    private void WelcomeClosed(EntityHandle player)
    {
        int team = teams.Team(player);

        if (team == (int)TFTeam.Unassigned)
        {
            if (settings.ForceAutoTeam)
            {
                // Stock's raw ChangeTeam: no unbalance gate, which the auto
                // pick cannot trip (it takes the emptier side).
                var joined = policy.AutoTeam();
                teams.Join(player, (int)joined);
                Show(joined == TFTeam.Blue ? TFJoinTeamCommand.ClassBluePanel : TFJoinTeamCommand.ClassRedPanel, player, show: true);
            }
            else
                Show(TFJoinTeamCommand.TeamPanel, player, show: true);

            return;
        }

        // On a team, no class: the class panel again.
        if (team is (int)TFTeam.Red or (int)TFTeam.Blue && carriers.ClassOf(player) == TFClass.Undefined)
            Show(team == (int)TFTeam.Red ? TFJoinTeamCommand.ClassRedPanel : TFJoinTeamCommand.ClassBluePanel, player, show: true);
    }

    private void MotdClosed(EntityHandle player)
    {
        if (settings.ForceAutoTeam)
        {
            var joined = policy.AutoTeam();
            teams.Join(player, (int)joined);
            Show(joined == TFTeam.Red ? TFJoinTeamCommand.ClassRedPanel : TFJoinTeamCommand.ClassBluePanel, player, show: true);
        }
        else
            Show(TFJoinTeamCommand.TeamPanel, player, show: false);
    }

    /// <summary>Stock's <c>ShowViewPortPanel(name, show)</c>: the panel, to that player alone.</summary>
    private void Show(string panel, EntityHandle player, bool show)
        => messages.Send(new VguiMenu { Name = panel, Show = show, Keys = [], Values = [] },
                         player: player.EntryIndex, reliable: true);
}
