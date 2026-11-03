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

namespace TF2.Server.Rules;

/// <summary>
/// TF2's spelling of <c>jointeam</c> (<c>CTFPlayer::HandleCommand_JoinTeam</c>):
/// a team NAME — <c>auto</c>, <c>spectate</c>, <c>red</c>, <c>blue</c>,
/// matched without regard to case — where the core reads HL2DM's number. The
/// word is claimed and handed to the core's <see cref="ITeams.Join"/> as the
/// team number, so the refusals (<see cref="TFTeams"/>), the spectator gate
/// and the <c>player_team</c> event stay the core's.
///
/// <para>
/// <c>auto</c> is <see cref="TFTeams.AutoTeam"/>. An unknown name is stock's
/// default, Red; <c>unassigned</c> is stock's invalid selection (claimed, and
/// nothing moves); a number keeps the core's meaning, so an HL2DM-style
/// <c>jointeam 3</c> still works. <c>mp_forceautoteam</c> is not read here:
/// stock applies it when the welcome menu closes, which is
/// <see cref="TFWelcomeMenuCommands"/>'s commands. <c>mp_humans_must_join_team</c>
/// is: it overrides a human's non-spectator pick ahead of the same-team
/// return, stock's <c>GetAssignedHumanTeam</c> arm (tf_player.cpp:6280-6289).
/// </para>
///
/// <para>
/// Stock's arms raise the viewport panels around the join
/// (<c>tf_player.cpp:6354-6455</c>): after changing team, the class panel for
/// the team joined (:6450-6455 — <c>tf_arena_force_class</c> stays false with
/// arena deferred); the team panel again when the unbalance check refused the
/// join (:6436-6440); and nothing for a spectator (:6354-6411, whose class
/// panel is arena-only at :6407-6410), a same-team join (the early return at
/// :6414-6417), or a join after game over (:6226).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFJoinTeamCommand(ITeams teams, TFTeams policy, IUserMessageSender messages,
                                      MpHumansMustJoinTeamConVar? humansTeam = null)
    : IClientCommandHook, IOrderedHook
{
    /// <summary>The stock word.</summary>
    public const string Command = "jointeam";

    /// <summary>PANEL_CLASS_RED (tf_shareddefs.h:163).</summary>
    public const string ClassRedPanel = "class_red";

    /// <summary>PANEL_CLASS_BLUE (tf_shareddefs.h:162).</summary>
    public const string ClassBluePanel = "class_blue";

    /// <summary>PANEL_TEAM (viewport_panel_names.h).</summary>
    public const string TeamPanel = "team";

    public int HookOrder => TeamRoundOrders.JoinTeamCommand;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
            return false;

        string name = args.Length > 0 ? args[0] : string.Empty;

        // A number is the core's jointeam, unchanged.
        if (name.Length > 0 && (char.IsAsciiDigit(name[0]) || name[0] is '-' or '+'))
            return false;

        // Invalid team selection: claimed, and nothing moves.
        if (Resolve(name) is { } team)
        {
            int requested = (int)team;

            // The humans' team (tf_player.cpp:6280-6289): a non-spectator
            // pick yields to mp_humans_must_join_team unless it names any
            // team, and the join counts as auto-teamed. It stands ahead of
            // the same-team return, so a pick the override sends home is
            // one; stock's IsBot arm has no bots to spare here.
            if (requested != (int)TFTeam.Spectator && HumanTeam() is { } forced)
                requested = (int)forced;

            // Stock's early return: already on that team, so neither the
            // change nor the panels happen (:6414-6417).
            if (requested is (int)TFTeam.Red or (int)TFTeam.Blue && teams.Team(player) == requested)
                return true;

            // The spectator arm's unbalance refusal (tf_player.cpp:6364-6381):
            // ClientPrint(HUD_PRINTCENTER, #Cannot_Be_Spectator_Unbalance) and
            // nothing else - no panel, no join.
            if (requested == (int)TFTeam.Spectator && !policy.MaySpectate(player))
            {
                messages.Send(new TextMsg
                {
                    Destination = TFMessages.HudPrintCenter,
                    Text = TFMessages.CannotBeSpectatorUnbalance,
                    Params = [],
                }, player: player.EntryIndex, reliable: true);
                return true;
            }

            ShowPanel(player, teams.Join(player, requested), requested);
        }
        return true;
    }

    /// <summary>
    /// Stock's <c>GetAssignedHumanTeam</c> (tf_gamerules.cpp:16452-16470):
    /// the convar's team, matched without regard to case; anything else —
    /// and an unbound convar — is any team, null here.
    /// </summary>
    private TFTeam? HumanTeam()
    {
        string name = humansTeam?.Value ?? "any";
        if (string.Equals(name, "blue", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Blue;
        if (string.Equals(name, "red", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Red;
        if (string.Equals(name, "spectator", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Spectator;
        return null;
    }

    /// <summary>
    /// Stock's panels around the join: the class panel for the team joined
    /// (:6450-6455), the team panel for a join the balance refused
    /// (:6436-6440) — a game-over refusal shows nothing (:6226) — and none
    /// for the spectator arms (:6354-6411).
    /// </summary>
    private void ShowPanel(EntityHandle player, JoinTeamResult result, int requested)
    {
        if (result == JoinTeamResult.Playing)
        {
            Show(requested == (int)TFTeam.Red ? ClassRedPanel : ClassBluePanel, player);
            return;
        }

        if (result == JoinTeamResult.Refused
            && !policy.MayJoin(player, requested, out string why)
            && why == TFTeams.UnbalancedReason)
        {
            Show(TeamPanel, player);
        }
    }

    /// <summary>Stock's <c>ShowViewPortPanel(name, true)</c>: the panel, shown, to that player alone.</summary>
    private void Show(string panel, EntityHandle player)
        => messages.Send(new VguiMenu { Name = panel, Show = true, Keys = [], Values = [] },
                         player: player.EntryIndex, reliable: true);

    /// <summary>The team a <c>jointeam</c> name asks for; null for stock's invalid selection.</summary>
    public TFTeam? Resolve(string name)
    {
        if (string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase))
            return policy.AutoTeam();

        if (string.Equals(name, "spectate", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Spectator;

        // g_aTeamNames, whose indices match TEAM_UNASSIGNED, TEAM_SPECTATOR,
        // TF_TEAM_RED and TF_TEAM_BLUE ("Unassigned", "Spectator", "Red",
        // "Blue": tf_gamerules.cpp's sTeamNames comment spells the table).
        if (string.Equals(name, "unassigned", StringComparison.OrdinalIgnoreCase))
            return null;   // iTeam < TEAM_SPECTATOR: invalid team selection
        if (string.Equals(name, "spectator", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Spectator;
        if (string.Equals(name, "blue", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Blue;

        // "red" and anything else: stock's default.
        return TFTeam.Red;
    }
}
