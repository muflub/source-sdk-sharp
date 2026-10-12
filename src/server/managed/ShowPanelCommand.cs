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
public sealed class ShowPanelCommand(ILog log, IUserMessageSender messages) : IConCommand
{
    public void Execute(string args)
    {
        var parts = args.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int player) || player <= 0)
        {
            log.Msg("usage: cs_sv_showpanel <player index> <panel> [show 1|0] [title] [msg]\n");
            return;
        }

        bool show = parts.Length < 3 || parts[2] != "0";
        var keys = new List<string>();
        var values = new List<string>();
        if (parts.Length > 3)
        {
            keys.Add("title");
            values.Add(parts[3]);
            // "@entry": stock's index content (type 1), the InfoPanel table's entry,
            // as HL2MP's join sends "motd".
            string msg = parts.Length > 4 ? parts[4] : string.Empty;
            bool index = msg.StartsWith('@');
            keys.Add("type");
            values.Add(index ? "1" : "0");
            keys.Add("msg");
            values.Add(index ? msg[1..] : msg.Replace("\\n", "\n", StringComparison.Ordinal));
        }

        bool sent = messages.Send(new VguiMenu { Name = parts[1], Show = show, Keys = [.. keys], Values = [.. values] },
                                  player, reliable: true);
        log.Msg($"[SourceSharp/Server] showpanel {parts[1]} {(show ? "shown" : "hidden")} to {player}: " +
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
public sealed class TextMsgCommand(ILog log, IUserMessageSender messages) : IConCommand
{
    public void Execute(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !int.TryParse(parts[0], out int player) || player <= 0
            || !int.TryParse(parts[1], out int dest) || dest is < 1 or > 4)
        {
            log.Msg("usage: cs_sv_textmsg <player index> <dest 1-4> <text> [param...]\n");
            return;
        }
        var p = parts.Skip(3).Take(4).ToArray();
        bool sent = messages.Send(new TextMsg { Destination = (byte)dest, Text = parts[2], Params = p }, player, reliable: true);
        log.Msg($"[SourceSharp/Server] textmsg {dest} '{parts[2]}' to {player}: {(sent ? "sent" : "too large to send")}\n");
    }
}
