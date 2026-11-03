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
/// The game's own look at the line, stock's per-player
/// <c>CheckChatText</c> override (client.cpp:313), runs here as the
/// <see cref="IPlayerChatHook"/> chain, before anything is relayed: HL2MP's
/// player spent that override on the ready signal
/// (hl2mp_player.cpp:1547-1571). The hooks hear the checked text, and a
/// console line -- no speaker -- reaches none of them, stock's
/// <c>if ( pEdict )</c> gate.
/// </para>
///
/// <para>
/// Not ported: the voice manager's ignore list (no player can ignore another
/// in this build), and team membership itself (team changes are a TODO).
/// </para>
/// </summary>
public sealed class HostSay(ILog log, IEnginePlayers players, IEngineEntities entities, IEntityDirectory directory,
                            IUserMessageSender messages, IConnectedClients clients, IEngineGameEvents events, IEngineHost host,
                            IHooks<IPlayerChatHook>? chatHooks = null, TeamNames? teamNames = null,
                            IHooks<IChatAudience>? audience = null)
{
    private readonly TeamNames _teamNames = teamNames ?? TeamNames.Stock();
    private readonly IHooks<IPlayerChatHook>? _chatHooks = chatHooks;
    private readonly IHooks<IChatAudience>? _audience = audience;

    /// <summary>
    /// The mode proxy, for the active mode's <see cref="IGameMode.Teamplay"/>
    /// answer, wired by <see cref="AttachModes"/>. An ATTACH and not a
    /// constructor parameter for the Players reason: this class is created
    /// before the proxy exists, and `Create` resolves constructor arguments
    /// eagerly, so asking by parameter would make the live game pass null.
    /// Null stays the no-owner arm: HL2MP without teamplay has no teammates.
    /// </summary>
    private GameModes? _modes;

    /// <summary>The wiring the module does once the proxy exists (see <see cref="_modes"/>).</summary>
    internal void AttachModes(GameModes modes) => _modes = modes;

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

        // The game's hook, before the relay (client.cpp:313): the checked text,
        // and never for the console's own line.
        if (hasSpeaker)
            _chatHooks?.Each(nameof(IPlayerChatHook.OnPlayerChat),
                             h => h.OnPlayerChat(new PlayerChat(speaker, text, teamOnly)));
        int speakerIndex = hasSpeaker ? entities.GetIndex(speaker) : 0;
        string name = hasSpeaker ? clients.ClientConVar(speakerIndex, "name") ?? "unconnected" : "Console";

        // HL2MP: no format for the console (SayText), whom nobody can hear anyway.
        string? format = hasSpeaker ? Format(teamOnly, players.GetTeam(speaker)) : null;
        string line = $"{name}: {text}\n";

        int sent = 0;
        if (hasSpeaker)
        {
            foreach (var entity in directory.OfClass(EntityNames.Player))
            {
                if (entity is not SsPlayer listener || listener.Handle == speaker)
                    continue;

                if (!CanHear(listener.Handle, speaker, teamOnly))
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
        string team = hasSpeaker ? TeamNameOf(players.GetTeam(speaker)) : "Console";
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

    /// <summary>
    /// Whether <paramref name="listener"/> would be sent a line
    /// <paramref name="speaker"/> says: the relay's own rule, for whoever acts
    /// on a line in a player's place and must hear only what that player would.
    /// </summary>
    public bool CanHear(EntityHandle listener, EntityHandle speaker, bool teamOnly)
    {
        // say_team: GR_TEAMMATE only, and HL2MP without teamplay has no teammates.
        if (teamOnly && !Teammates(listener, speaker))
            return false;

        // Stock's per-listener stage (client.cpp:387): the listener's
        // CanHearAndReadChatFrom. Nobody registered vetoes nobody, the
        // base player's plain true (player.h:699).
        return _audience is not { } audience || audience.All.All(a => a.CanHear(listener, speaker));
    }

    private bool Send(int to, int from, string format, string name, string text)
        => messages.Send(new SayText2 { Client = (byte)from, Chat = true, Name = format, Params = [name, text, "", ""] },
                         to, reliable: true);

    /// <summary>
    /// CHL2MPRules::PlayerRelationship under the active mode: HL2MP without
    /// teamplay has no teammates, and a mode that plays with teams says one
    /// game team (numbered 2 and up) is the same side. Spectator (1) and
    /// unassigned (0) never make teammates either way.
    /// </summary>
    public bool Teammates(EntityHandle a, EntityHandle b)
    {
        if (_modes?.Active?.Teamplay is not true)
            return false;

        int team = players.GetTeam(a);
        return team >= 2 && team == players.GetTeam(b);
    }

    /// <summary>CHL2MPRules::GetChatFormat for a player.</summary>
    public static string Format(bool teamOnly, int team)
        => teamOnly ? (team == TeamSpectator ? "HL2MP_Chat_Spec" : "HL2MP_Chat_Team")
                    : (team == TeamSpectator ? "HL2MP_Chat_AllSpec" : "HL2MP_Chat_All");

    private string TeamNameOf(int team) => _teamNames.Of(team, TeamName(team));

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

/// <summary>One said line, as <see cref="IPlayerChatHook"/> hears it.</summary>
/// <param name="Speaker">The player who said it; never <see cref="EntityHandle.None"/> (the console reaches no hook).</param>
/// <param name="Text">The line after <see cref="HostSay.CheckChatText"/>: quotes gone, cut at a CR, LF or ESC, 127 at most.</param>
/// <param name="TeamOnly">A <c>say_team</c> line.</param>
public readonly record struct PlayerChat(EntityHandle Speaker, string Text, bool TeamOnly);

/// <summary>
/// The say path's per-player hook (stock's <c>CBasePlayer::CheckChatText</c>
/// override, client.cpp:313): <see cref="HostSay"/> calls every one with each
/// line a player says, after the text is checked and before anything is
/// relayed. Implement it on a server <c>[SideService]</c> and register
/// nothing else; order among several is <see cref="IOrderedHook"/>'s. The
/// hooks observe; the line they hear is the line that is sent.
/// </summary>
public interface IPlayerChatHook
{
    /// <summary>Called once per said line, in the say path, before the relay.</summary>
    void OnPlayerChat(PlayerChat chat);
}

/// <summary>
/// The say relay's per-listener stage, stock's
/// <c>CBasePlayer::CanHearAndReadChatFrom</c> (player.h:699, plain true) as a
/// registered veto: the game's override — TF2's dead-talk gate,
/// <c>CTFPlayer::CanHearAndReadChatFrom</c> (tf_player.cpp:19775) — refuses
/// listeners from a line. Every registered audience must hear the pair for
/// the line to reach <paramref name="listener"/>; none registered, everyone
/// does. Resolved lazily with the other hooks, after the container is whole.
/// </summary>
public interface IChatAudience
{
    /// <summary>Whether <paramref name="listener"/> hears <paramref name="speaker"/>'s line.</summary>
    bool CanHear(EntityHandle listener, EntityHandle speaker);
}
