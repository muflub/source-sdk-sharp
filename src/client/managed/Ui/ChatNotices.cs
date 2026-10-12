//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// THE GAME'S NOTICES IN THE CHAT (stock ClientModeShared::FireGameEvent,
/// clientmode_shared.cpp): a player joined, left (with the reason, localised
/// when it is a token), joined a team (the game's choice said so; a silent
/// change or the disconnect's says nothing), changed name; a notify convar
/// changed on the server. A name not set yet (empty, "unconnected...",
/// "NULLNAME") says nothing. Each line goes to the chat and the console, as
/// CBaseHudChat::Printf put it. The formats are the engine's tokens, with
/// valve_english.txt's text when the tables lack them.
///
/// The native arms printed to a CHudChat this client no longer builds, so they
/// were silent; these are the lines again.
/// </summary>
public sealed class ChatNotices(IEngineHud hud, ILog log, ITextMessageChat chat)
{
    public static readonly IReadOnlyDictionary<string, string> StockFormats = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["#game_player_joined_game"] = "Player %s1 has joined the game",
        ["#game_player_left_game"] = "Player %s1 left the game (%s2)",
        ["#game_player_joined_team"] = "Player %s1 joined team %s2",
        ["#game_player_joined_autoteam"] = "Player %s1 was automatically assigned to team %s2",
        ["#game_player_changed_name"] = "Player %s1 changed name to %s2",
        ["#game_server_cvar_changed"] = "Server cvar '%s1' changed to %s2",
    };

    public void Subscribe(PlayerConnectClientEvent connect, PlayerDisconnectEvent disconnect, PlayerTeamEvent team,
                          PlayerChangeNameEvent name, ServerCvarEvent cvar)
    {
        connect.Fired += OnConnect;
        disconnect.Fired += OnDisconnect;
        team.Fired += OnTeam;
        name.Fired += OnChangeName;
        cvar.Fired += OnServerCvar;
    }

    public void OnConnect(PlayerConnectClientArgs e)
    {
        if (!NotSetYet(e.Name))
            Print("#game_player_joined_game", e.Name, "");
    }

    public void OnDisconnect(PlayerDisconnectArgs e)
    {
        if (NotSetYet(e.Name))
            return;
        string reason = e.Reason ?? "";
        if (reason.StartsWith('#') && Find(reason) is { } found)
            reason = found;
        Print("#game_player_left_game", e.Name, reason);
    }

    public void OnTeam(PlayerTeamArgs e)
    {
        if (e.Disconnect || NotSetYet(e.Name) || e.Silent)
            return;
        Print(e.AutoTeam ? "#game_player_joined_autoteam" : "#game_player_joined_team", e.Name, TeamName(e.Team));
    }

    public void OnChangeName(PlayerChangeNameArgs e)
    {
        if (!NotSetYet(e.OldName))
            Print("#game_player_changed_name", e.OldName, e.NewName);
    }

    public void OnServerCvar(ServerCvarArgs e) => Print("#game_server_cvar_changed", e.Name, e.Value);

    /// <summary>stock PlayerNameNotSetYet.</summary>
    public static bool NotSetYet(string? name)
        => string.IsNullOrEmpty(name)
           || name.StartsWith("unconnected", StringComparison.OrdinalIgnoreCase)
           || name.Equals("NULLNAME", StringComparison.OrdinalIgnoreCase);

    /// <summary>HL2MP's team names (CTeam's, as the server's teams are made); the number for any other.</summary>
    public static string TeamName(int team) => team switch
    {
        0 => "Unassigned",
        1 => "Spectator",
        2 => "Combine",
        3 => "Rebels",
        _ => team.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private void Print(string token, string p1, string p2)
    {
        string format = Find(token) ?? StockFormats[token];
        string line = TextMessages.Construct(format, p1, p2, "", "");
        chat.Print(line);
        log.Msg(line + "\n");
    }

    private string? Find(string token)
    {
        string found = hud.Localise(token);
        return found.Length > 0 && found != token ? found : null;
    }
}
