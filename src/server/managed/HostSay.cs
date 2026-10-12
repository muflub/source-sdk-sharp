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
/// Stock's Host_Say (client.cpp) under HL2MP's rules, behind <c>say</c> and
/// <c>say_team</c>.
///
/// <para>
/// The text is checked (CheckChatText: quotes stripped, cut at a CR, LF or
/// ESC, nothing if empty, 127 at most). The format is the gamerules'
/// (CHL2MPRules::GetChatFormat: HL2MP_Chat_All / _Team, the _AllSpec / _Spec
/// variants for a spectator). Every other player who may hear it is sent
/// SayText2 (the sender's index, chat, the format, then the name, the text,
/// the location -- HL2MP has none -- and an empty fourth), reliably, then the
/// sender. say_team reaches teammates only (PlayerCanHearChat), and HL2MP
/// without teamplay has none (PlayerRelationship: GR_NOTTEAMMATE). A line
/// from the server console has no speaker, and CHL2MP_Player::
/// CanHearAndReadChatFrom refuses it. Then the log line, the console echo on
/// a dedicated server, and the player_say game event.
/// </para>
///
/// <para>
/// Not ported: the voice manager's ignore list (no player can ignore another
/// in this build), and team membership itself (team changes are a TODO).
/// </para>
/// </summary>
public sealed class HostSay(ILog log, IEnginePlayers players, IEngineEntities entities, IEntityDirectory directory,
                            IUserMessageSender messages, IConnectedClients clients, IEngineGameEvents events, IEngineHost host)
{
    /// <summary>The longest line (CheckChatText's cut).</summary>
    public const int MaxText = 127;

    /// <summary>TEAM_SPECTATOR.</summary>
    public const int TeamSpectator = 1;

    /// <summary>Lines sent to at least one player, since the module started.</summary>
    public int Said { get; private set; }

    public void Say(EntityHandle speaker, string args, bool teamOnly)
    {
        string? text = CheckChatText(args);
        if (text is null)
            return;

        bool hasSpeaker = speaker != EntityHandle.None;
        int speakerIndex = hasSpeaker ? entities.GetIndex(speaker) : 0;
        string name = hasSpeaker ? clients.ClientConVar(speakerIndex, "name") ?? "unconnected" : "Console";

        // HL2MP: no format for the console (SayText), whom nobody can hear anyway.
        string? format = hasSpeaker ? Format(teamOnly, players.GetTeam(speaker)) : null;
        string line = $"{name}: {text}\n";

        int sent = 0;
        if (hasSpeaker)
        {
            foreach (var entity in directory.OfClass(EntityNames.SsPlayer))
            {
                if (entity is not SsPlayer listener || listener.Handle == speaker)
                    continue;

                // say_team: GR_TEAMMATE only, and HL2MP without teamplay has no teammates.
                if (teamOnly && !Teammates(listener.Handle, speaker))
                    continue;

                sent += Send(entities.GetIndex(listener.Handle), speakerIndex, format!, name, text) ? 1 : 0;
            }

            // The sender sees their own line.
            sent += Send(speakerIndex, speakerIndex, format!, name, text) ? 1 : 0;
        }

        if (sent > 0)
            Said++;

        if (host.IsDedicatedServer)
            log.Msg(line);

        int userId = hasSpeaker ? clients.UserId(speakerIndex) : 0;
        string networkId = hasSpeaker ? NetworkId(clients.SteamId64(speakerIndex)) : "Console";
        string team = hasSpeaker ? TeamName(players.GetTeam(speaker)) : "Console";
        log.Msg($"\"{name}<{userId}><{networkId}><{team}>\" {(teamOnly ? "say_team" : "say")} \"{text}\"\n");

        IntPtr e = events.EventCreate("player_say");
        if (e != IntPtr.Zero)
        {
            events.EventSetInt(e, "userid", userId);
            events.EventSetString(e, "text", text);
            events.EventSetInt(e, "priority", 1);
            events.EventFire(e);
        }
    }

    private bool Send(int to, int from, string format, string name, string text)
        => messages.Send(new SayText2 { Client = (byte)from, Chat = true, Name = format, Params = [name, text, "", ""] },
                         to, reliable: true);

    /// <summary>CHL2MPRules::PlayerRelationship: without teamplay, nobody is a teammate.</summary>
    public static bool Teammates(EntityHandle a, EntityHandle b) => false;

    /// <summary>CHL2MPRules::GetChatFormat for a player.</summary>
    public static string Format(bool teamOnly, int team)
        => teamOnly ? (team == TeamSpectator ? "HL2MP_Chat_Spec" : "HL2MP_Chat_Team")
                    : (team == TeamSpectator ? "HL2MP_Chat_AllSpec" : "HL2MP_Chat_All");

    /// <summary>HL2MP's team names (g_Teams): Unassigned, Spectator, Combine, Rebels.</summary>
    public static string TeamName(int team) => team switch
    {
        1 => "Spectator",
        2 => "Combine",
        3 => "Rebels",
        _ => "Unassigned",
    };

    /// <summary>GetNetworkIDString's form: [U:1:account], or STEAM_ID_LAN when unauthenticated.</summary>
    public static string NetworkId(ulong steamId64)
        => steamId64 == 0 ? "STEAM_ID_LAN" : $"[U:1:{steamId64 & 0xFFFFFFFF}]";

    /// <summary>Stock's CheckChatText: the line to say, or null for nothing.</summary>
    public static string? CheckChatText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        string p = text;
        if (p[0] == '"')
            p = p.Length >= 2 ? p[1..^1] : string.Empty;

        int cut = p.IndexOfAny(['\r', '\n', '\x1b']);
        if (cut >= 0)
            p = p[..cut];

        if (p.Length == 0)
            return null;

        return p.Length > MaxText ? p[..MaxText] : p;
    }
}
