//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

using TF2.Client.Entities;
using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Client.Ui;

/// <summary>
/// The map-goal hint panel's data, stock's <c>CHudTeamGoal::ShouldDraw</c>
/// (tf_hud_teamgoal.cpp:102-158) with <c>SetupGoalPanel</c>'s reads
/// (:177-246): the round could show it (<c>CTFGameRules::ShouldShowTeamGoal</c>,
/// tf_gamerules.cpp:18553 — preround, running, or the setup period), the
/// local player is alive on a game team, and the team's goal string stands;
/// the panel then shows for 15 seconds (:144), re-arming only once the
/// round leaves the could-see states (:114-119). Stock's tournament arm
/// (:109-112), the played-15-times count (:135), the team-switch alert
/// (:148-150) and the training icon have no port to gate on; the switch
/// label is stock's hidden one, so the goal label sits at its y (:241-243).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFTeamGoal)])]
public sealed class TFHudTeamGoal(TFClientPlayerResources resources, IClientStandalones standalones,
                                  IEngineHud screen, IGameClock clock, TFClientTeamResources teams,
                                  IUiLocalisation localise)
    : ITFTeamGoal
{
    /// <summary>FIRST_GAME_TEAM (tf_team.h): the team floor ShouldDraw names.</summary>
    private const int FirstGameTeam = (int)TFTeam.Red;

    /// <summary>The show, 15 seconds (tf_hud_teamgoal.cpp:143-144).</summary>
    private const float ShowSeconds = 15f;

    /// <summary>The role icon for <paramref name="role"/>, stock's
    /// <c>pszTeamRoleIcons</c> (tf_hud_teamgoal.cpp:160-165), the ../
    /// dropped; out of range keeps the res default.</summary>
    private static string RoleIcon(int role) => role switch
    {
        0 => "hud/hud_icon_capture",        // TEAM_ROLE_NONE
        1 => "hud/hud_icon_defend",         // TEAM_ROLE_DEFENDERS
        2 => "hud/hud_icon_attack",         // TEAM_ROLE_ATTACKERS
        _ => "",
    };

    private readonly IUiLocalisation _localise = localise;

    private float _hideAt;
    private bool _shown;
    private string _text = string.Empty;
    private string _image = string.Empty;

    public bool Visible
    {
        get
        {
            bool couldSee = CouldSee;

            // The hide arm (L114-122): past the show, dark until the round
            // leaves the could-see states, which clears the timer.
            if (_hideAt != 0f && _hideAt < clock.Now)
            {
                if (!couldSee)
                    _hideAt = 0f;
                _shown = false;
                return false;
            }

            if (!couldSee || Row is not { } row || !Alive(row.Index))
            {
                _shown = false;
                return false;
            }

            int team = row.Live.State.Team[row.Index];
            string goal = teams.GoalString(team);
            if (goal.Length == 0)
            {
                _shown = false;
                return false;
            }

            if (!_shown)
            {
                // SetupGoalPanel: the localised line and the role icon, taken
                // once per show (L141, :181-188, :214-217).
                _text = Localise(goal);
                _image = RoleIcon(teams.Role(team));
                _hideAt = clock.Now + ShowSeconds;
                _shown = true;
            }

            return true;
        }
    }

    public string Text => _shown ? _text : string.Empty;

    public string Image => _shown ? _image : string.Empty;

    public int Team => Row is { } row ? row.Live.State.Team[row.Index] : 0;

    /// <summary>Stock's <c>ShouldShowTeamGoal</c> (tf_gamerules.cpp:18553):
    /// the states the panel may show in, on the resource's round words.</summary>
    private bool CouldSee
        => Row is { } row
           && (row.Live.State.RoundState == (byte)RoundState.Preround
               || row.Live.State.RoundState == (byte)RoundState.RoundRunning
               || row.Live.State.InSetup != 0);

    /// <summary>The local row of the TF resource, a player's slot (0 is the world).</summary>
    private (ClientTFPlayerResource Live, int Index)? Row
        => resources.Live is { } live && screen.LocalPlayerIndex is > 0 and < TFPlayerResourceNetState.Slots
            ? (live, screen.LocalPlayerIndex)
            : null;

    /// <summary><c>pPlayer->IsAlive()</c> and the team floor of L127: the
    /// core resource's row.</summary>
    private bool Alive(int index)
        => standalones.OfClass(EntityNames.PlayerManager).OfType<ClientPlayerResource>().FirstOrDefault()
               is { } core
           && core.State.IsAlive(index)
           && core.State.Team[index] >= FirstGameTeam;

    /// <summary>Stock's localise-or-raw (<c>g_pVGuiLocalize->Find</c>, L181-189),
    /// through the game's token chain — the TF2 strings ride in a VPK, where no
    /// table the engine loads reaches.</summary>
    private string Localise(string goal)
    {
        if (goal.Length == 0 || goal[0] != '#')
            return goal;

        return _localise.Find(goal) ?? goal;
    }
}

