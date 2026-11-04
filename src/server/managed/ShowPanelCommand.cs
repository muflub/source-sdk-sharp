using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Sends one player stock's VGUIMenu: a viewport panel by name, shown, with
/// its data (stock's ShowViewPortPanel). What the UI's team menu and MOTD
/// answer on the client (plan_client_ui U3); the live steps ui-teammenu and
/// the MOTD read drive it. A cheat: it puts a menu on somebody's screen.
/// </summary>
[ConCommand("cs_sv_showpanel", Flags = ConVarFlags.Cheat,
            Help = "cs_sv_showpanel <player index> <panel> [show 1|0] [title] [msg|@entry]: send that player VGUIMenu, " +
                   "e.g. team, info, scores; @motd is the InfoPanel entry (type 1).")]
public sealed class ShowPanelCommand(ILog log, IUserMessageSender messages)
{
    public void Execute([ArgRange(1, int.MaxValue)] int player, string panel, bool show = true, string? title = null, [Rest] string text = "")
    {
        var keys = new List<string>();
        var values = new List<string>();
        if (title is not null)
        {
            keys.Add("title");
            values.Add(title);
            // "@entry": stock's index content (type 1), the InfoPanel table's entry,
            // as HL2MP's join sends "motd".
            string msg = text;
            bool index = msg.StartsWith('@');
            keys.Add("type");
            values.Add(index ? "1" : "0");
            keys.Add("msg");
            values.Add(index ? msg[1..] : msg.Replace("\\n", "\n", StringComparison.Ordinal));
        }

        bool sent = messages.Send(new VguiMenu { Name = panel, Show = show, Keys = [.. keys], Values = [.. values] },
                                  player, reliable: true);
        log.Msg($"[SourceSharp/Server] showpanel {panel} {(show ? "shown" : "hidden")} to {player}: " +
                $"{(sent ? "sent" : "too large to send")}\n");
    }
}

/// <summary>
/// Sends one player stock's TextMsg (UTIL_ClientPrint's message): a
/// destination (1 notify, 2 console, 3 talk, 4 centre), the text and up to
/// four parameters, '#' tokens left for the client to localise. What proves the
/// client's TextMessages; a cheat, it puts text on somebody's screen.
/// </summary>
[ConCommand("cs_sv_textmsg", Flags = ConVarFlags.Cheat,
            Help = "cs_sv_textmsg <player index> <dest 1-4> <text> [param...]: send that player TextMsg")]
public sealed class TextMsgCommand(ILog log, IUserMessageSender messages)
{
    public void Execute([ArgRange(1, int.MaxValue)] int player, [ArgRange(1, 4)] int dest, string text, params string[] parameters)
    {
        var p = parameters.Take(4).ToArray();
        bool sent = messages.Send(new TextMsg { Destination = (byte)dest, Text = text, Params = p }, player, reliable: true);
        log.Msg($"[SourceSharp/Server] textmsg {dest} '{text}' to {player}: {(sent ? "sent" : "too large to send")}\n");
    }
}
