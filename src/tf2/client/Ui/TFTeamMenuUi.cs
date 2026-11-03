//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace TF2.Client.Ui;

/// <summary>
/// The TF2 team menu in game (stock's <c>CTFTeamMenu</c>,
/// game/client/tf/vgui/tf_teammenu.cpp): the head counts of the two teams
/// — stock's <c>redcount</c>/<c>bluecount</c> dialog variables, which the
/// panel refreshed from <c>Get_Number_Players</c> (:210-218) — read here
/// off the player resource's rows, and a choice sent as the named
/// <c>jointeam</c> (stock's button commands, through its
/// <c>OnCommand</c> at :615-665: the same-team arm of :650-654 sends
/// nothing, and the menu closes whichever way the click went).
///
/// <para>
/// The team names are TF2's, not HL2MP's: RED and BLU are the screen's,
/// and the command spells the teams as the server's <c>jointeam</c>
/// reads them (<c>TFJoinTeamCommand</c>).
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFTeamMenuSource), typeof(ITFTeamMenuActions)])]
public sealed class TFTeamMenuUi(IClientStandalones standalones, IEngineHud screen, IEngineConsole console,
                                 IFwdEngineClient engine, ITFMenuNavigation nav) : ITFTeamMenuSource, ITFTeamMenuActions
{
    private const int Slots = PlayerResourceNetState.Slots;

    private ClientPlayerResource? _resource;

    public int RedPlayers => Count(2);

    public int BluePlayers => Count(3);

    public int CurrentTeam
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            return Resource is { } r && local is > 0 and < Slots ? r.State.Team[local] : 0;
        }
    }

    /// <summary><c>mp_allowspectators</c> (stock's gate on the Spectate
    /// button, tf_teammenu.cpp:729, :836).</summary>
    public bool AllowsSpectators => int.TryParse(console.FindConVarValue("mp_allowspectators"), out int n) && n != 0;

    public void Choose(TFTeamChoice choice)
    {
        string command = choice switch
        {
            TFTeamChoice.Red => "jointeam red",
            TFTeamChoice.Blue => "jointeam blue",
            TFTeamChoice.Auto => "jointeam auto",
            TFTeamChoice.Spectate => "jointeam spectate",
            _ => throw new System.ArgumentOutOfRangeException(nameof(choice)),
        };

        // Stock's same-team arm (:650-654): the team you are already on
        // sends nothing; the menu still closes (:662-664).
        bool sameTeam = choice == TFTeamChoice.Red && CurrentTeam == 2
                     || choice == TFTeamChoice.Blue && CurrentTeam == 3;
        if (!sameTeam)
            engine.SubmitConsoleLine(command);
        nav.Close("tf2teammenu");
        // The next panel is the server's: stock's jointeam shows the
        // class panel from the server once the team is set
        // (tf_player.cpp:6450-6455) — and shows the team panel back for
        // a refused join (:6434-6440), which no client-side chain could
        // know. The client choice sends and closes, and opens nothing.
    }

    /// <summary>Cancel hides the menu without choosing (stock's Cancel is
    /// only visible with a team already chosen, :165).</summary>
    public void Cancel() => nav.Close("tf2teammenu");

    private int Count(int team)
    {
        if (Resource is not { } r)
            return 0;
        int n = 0;
        var state = r.State;
        for (int slot = 1; slot < Slots; slot++)
            if (state.HasFlag(slot, PlayerResourceNetState.FlagConnected) && state.Team[slot] == team)
                n++;
        return n;
    }

    /// <summary>The player resource's rows, re-found if it has gone (the
    /// scoreboard's way): the counts and the local team read live, no
    /// refresh hook needed.</summary>
    private ClientPlayerResource? Resource
    {
        get
        {
            if (_resource is null || !standalones.Live().Contains(_resource))
                _resource = standalones.OfClass(EntityNames.PlayerManager)
                                    .OfType<ClientPlayerResource>().FirstOrDefault();
            return _resource;
        }
    }
}
