//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>How long a chat line stays, seconds (stock: hud_saytext_time 12).</summary>
[ConVar("hud_saytext_time", "12", Help = "How long a chat line stays on the screen, seconds")]
public sealed class HudSayTextTimeConVar : FloatConVar
{
}

/// <summary>Who a client index is, for the chat: their name and team.</summary>
public interface IChatPlayers
{
    bool TryFind(int client, out string name, out int team);
}

/// <summary>
/// THE CHAT FEED (stock CBaseHudChat, hud_basechat.cpp): SayText and
/// SayText2 become lines. A SayText2 a player said (bWantsToChat) is stock's
/// "#HL2MP_Chat_*" format with the name and the text: the speaker, the text,
/// and the format's (TEAM) and *DEAD* tags; any other SayText2 is its format
/// localised (the engine's tables, then stock's English for the HL2MP formats
/// this mod's client does not load) with the parameters in place. A SayText is
/// its raw line (a '#' token looked up when it is not chat). Each line lasts
/// hud_saytext_time (12) and fades over its last second (CHATLINE_FADE_TIME
/// 1.0, PerformFadeout); a player's line is also printed to the console.
/// The core owns it, so a UI-only reload keeps it. Not ported: stock's
/// 5-second bright flash on a new line and its HudChat.Message sound.
/// </summary>
public sealed class ChatFeed(IGameClock clock, ILog log, IEngineHud hud, IChatPlayers players,
                             HudSayTextTimeConVar? time = null) : IChatSource, ITextMessageChat
{
    /// <summary>Lines kept; the chat screen shows the newest of them.</summary>
    public const int MaxLines = 16;

    /// <summary>Stock's CHATLINE_FADE_TIME.</summary>
    public const float FadeSeconds = 1f;

    /// <summary>Stock's HL2MP chat formats (hl2mp_english.txt), for the tables this client does not load.</summary>
    public static readonly IReadOnlyDictionary<string, string> StockFormats = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["#HL2MP_Chat_All"] = "%s1 :  %s2",
        ["#HL2MP_Chat_AllDead"] = "*DEAD* %s1 :  %s2",
        ["#HL2MP_Chat_Team"] = "(TEAM) %s1 :  %s2",
        ["#HL2MP_Chat_TeamDead"] = "*DEAD*(TEAM) %s1 :  %s2",
        ["#HL2MP_Name_Change"] = "* %s1 changed name to %s2",
    };

    private readonly ChatLine[] _lines = new ChatLine[MaxLines];
    private readonly float[] _expires = new float[MaxLines];
    private int _count;

    public int Count
    {
        get
        {
            Retire();
            return _count;
        }
    }

    public ChatLine Line(int i)
    {
        var l = _lines[i];
        float left = _expires[i] - clock.Now;
        return left >= FadeSeconds ? l with { Alpha = 1f } : l with { Alpha = Math.Clamp(left / FadeSeconds, 0f, 1f) };
    }

    public void Subscribe(SayTextMessage sayText, SayText2Message sayText2)
    {
        sayText.Fired += m => OnSayText(m);
        sayText2.Fired += m => OnSayText2(m);
    }

    public void OnSayText(SayText m)
    {
        string text = m.Text ?? "";
        if (!m.Chat && text.StartsWith('#'))
            text = Localise(text);
        Add(new ChatLine("", Team(m.Client), text, false, 1f));
        log.Msg(text);
    }

    public void OnSayText2(SayText2 m)
    {
        string format = m.Name ?? "";
        var p = m.Params ?? [];
        string P(int i) => i < p.Length ? p[i] ?? "" : "";

        if (m.Chat)
        {
            // A player's line: the format says team-only and dead; the line itself is the name and the text.
            bool team = format.Contains("Team", StringComparison.Ordinal), dead = format.Contains("Dead", StringComparison.Ordinal);
            var line = new ChatLine(P(0), Team(m.Client), P(1).Replace('\r', '\n'), team, 1f, dead);
            Add(line);
            log.Msg(TextMessages.Construct(Localise(format), P(0), P(1), P(2), P(3)).Replace('\r', '\n') + "\n");
            return;
        }
        Add(new ChatLine("", Team(m.Client), TextMessages.Construct(Localise(format), P(0), P(1), P(2), P(3)).Replace('\r', '\n'), false, 1f));
    }

    /// <summary>TextMsg's talk destination (TextMessages): a line with no speaker.</summary>
    public void Print(string line) => Add(new ChatLine("", 0, line.TrimEnd('\n'), false, 1f));

    private string Localise(string token)
    {
        string found = token.Length > 0 ? hud.Localise(token) : "";
        if (found.Length > 0 && found != token)
            return found;
        // The server may name the format with or without stock's '#'.
        string key = token.StartsWith('#') ? token : "#" + token;
        return StockFormats.TryGetValue(key, out var stock) ? stock : token;
    }

    private int Team(int client) => client > 0 && players.TryFind(client, out _, out int team) ? team : 0;

    private void Add(ChatLine line)
    {
        Retire();
        if (_count == MaxLines)
        {
            Array.Copy(_lines, 1, _lines, 0, MaxLines - 1);
            Array.Copy(_expires, 1, _expires, 0, MaxLines - 1);
            _count--;
        }
        _lines[_count] = line;
        _expires[_count] = clock.Now + (time is { } t ? t.Value : 12f);
        _count++;
    }

    private void Retire()
    {
        float now = clock.Now;
        int keep = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_expires[i] < now || _expires[i] - now > (time is { } t ? t.Value : 12f) + 1f)
                continue;
            _lines[keep] = _lines[i];
            _expires[keep] = _expires[i];
            keep++;
        }
        for (int i = keep; i < _count; i++)
            _lines[i] = default;
        _count = keep;
    }
}

/// <summary>Who a client index is, over the player resource and the engine's player info.</summary>
public sealed class ChatPlayers(IClientStandalones standalones, IPlayerNames names) : IChatPlayers
{
    public bool TryFind(int client, out string name, out int team)
    {
        (name, team) = ("", 0);
        var resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        if (resource is null || client <= 0 || client >= PlayerResourceNetState.Slots
            || !resource.State.HasFlag(client, PlayerResourceNetState.FlagConnected))
            return false;
        team = resource.State.Team[client];
        name = names.TryGet(client, out var info) ? info.Name : "";
        return true;
    }
}

/// <summary>
/// The line typed into the chat (stock messagemode / messagemode2, which
/// opened CBaseHudChat's input line): Enter sends it as <c>say "…"</c> or
/// <c>say_team "…"</c> through the console and closes; Esc closes; the keys
/// and characters come from the menu root while the chat input is open.
/// </summary>
public sealed class ChatInputUi(Func<IFwdEngineClient?> engine) : IChatInputSource
{
    /// <summary>The longest line stock's input takes (MAX_CHARS_PER_LINE 128).</summary>
    public const int MaxChars = 127;

    private string _input = string.Empty;

    public bool TeamOnly { get; private set; }

    public string Input => _input;

    public int Caret { get; private set; }

    /// <summary>Set by the UI: what closes the input.</summary>
    public Action? Closed { get; set; }

    /// <summary>A fresh line, to all or to the team.</summary>
    public void Begin(bool teamOnly)
    {
        TeamOnly = teamOnly;
        _input = string.Empty;
        Caret = 0;
    }

    /// <summary>A key; true when the input used it.</summary>
    public bool Key(UiKey key)
    {
        switch (key)
        {
            case UiKey.Enter: Send(); return true;
            case UiKey.Escape: Closed?.Invoke(); return true;
            case UiKey.Backspace:
                if (Caret > 0)
                {
                    _input = _input.Remove(Caret - 1, 1);
                    Caret--;
                }
                return true;
            case UiKey.Left: Caret = Math.Max(0, Caret - 1); return true;
            case UiKey.Right: Caret = Math.Min(_input.Length, Caret + 1); return true;
            case UiKey.Home: Caret = 0; return true;
            case UiKey.End: Caret = _input.Length; return true;
            default: return false;
        }
    }

    public void Char(char c)
    {
        if (char.IsControl(c) || _input.Length >= MaxChars)
            return;
        _input = _input.Insert(Caret, c.ToString());
        Caret++;
    }

    /// <summary>The line to the server, quoted as stock's (a double quote in it becomes a single one), then closed.</summary>
    public void Send()
    {
        string line = _input.Trim();
        if (line.Length > 0)
            engine()?.SubmitConsoleLine($"{(TeamOnly ? "say_team" : "say")} \"{line.Replace('"', '\'')}\"");
        _input = string.Empty;
        Caret = 0;
        Closed?.Invoke();
    }
}

/// <summary>Opens the chat's input line (stock StartMessageMode): a fresh line, to all or the team.</summary>
public interface IChatOpener
{
    void Open(bool teamOnly);
}

/// <summary><see cref="IChatOpener"/> over the UI's chatinput screen.</summary>
public sealed class ChatOpener(UiLoader ui, ChatInputUi chat) : IChatOpener
{
    public void Open(bool teamOnly)
    {
        chat.Begin(teamOnly);
        ui.Current?.Open("chatinput");
    }
}

/// <summary>Stock's messagemode: the chat's input line, to everyone.</summary>
[ConCommand("messagemode", Help = "Type a chat line to everyone")]
public sealed class MessageModeCommand(ChatOpener chat) : IConCommand
{
    public void Execute(string args) => chat.Open(teamOnly: false);
}

/// <summary>Stock's messagemode2: the chat's input line, to the team.</summary>
[ConCommand("messagemode2", Help = "Type a chat line to your team")]
public sealed class MessageMode2Command(ChatOpener chat) : IConCommand
{
    public void Execute(string args) => chat.Open(teamOnly: true);
}

/// <summary>
/// A SayText2 raised on this client alone, as the engine's would be dispatched:
/// what proves the chat before the server's Host_Say sends one. A cheat.
/// </summary>
[ConCommand("cs_cl_saytext2", Flags = ConVarFlags.Cheat,
            Help = "cs_cl_saytext2 <client index> <chat 1|0> <format> [param...]: raise SayText2 on this client")]
public sealed class ClientSayText2Command(SayText2Message messages, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        var p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3 || !byte.TryParse(p[0], out byte client))
        {
            log.Msg("usage: cs_cl_saytext2 <client index> <chat 1|0> <format> [param...]\n");
            return;
        }
        ((IUserMessage<SayText2>)messages).OnReceived(new SayText2 { Client = client, Chat = p[1] == "1", Name = p[2], Params = p.Skip(3).Take(4).ToArray() });
    }
}
