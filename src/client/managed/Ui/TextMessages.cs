//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>Whether TextMsg prints at all (stock: cl_showtextmsg 1).</summary>
[ConVar("cl_showtextmsg", "1", Help = "Enable/disable text messages printing on the screen.")]
public sealed class ClShowTextMsgConVar : BoolConVar
{
}

/// <summary>Where a TextMsg line goes besides the centre and the console: the chat.</summary>
public interface ITextMessageChat
{
    void Print(string line);
}

/// <summary>
/// TEXTMSG, AS STOCK PRINTS IT (CBaseHudChat::MsgFunc_TextMsg, hud_basechat.cpp,
/// with CHudTextMessage::LookupString, text_message.cpp). Each of the five
/// strings (the text and four parameters) that starts with '#' is looked up in
/// titles.txt (the engine's TextMessageGet), whose negative effect overrides
/// the destination; then in the localisation tables; a parameter not found
/// there loses its trailing newline. With cl_showtextmsg on, the text is
/// built with the parameters in place of %s1..%s4 and goes to its
/// destination: HUD_PRINTCENTER the centre print (CR as NL), NOTIFY and
/// CONSOLE the console, TALK the chat and the console, each ending in a newline.
/// </summary>
public sealed class TextMessages(IEngineHud hud, ILog log, CentrePrint centre, ClShowTextMsgConVar? show = null,
                                 ITextMessageChat? chat = null)
{
    /// <summary>shareddefs.h's HUD_PRINT* destinations.</summary>
    public const int PrintNotify = 1, PrintConsole = 2, PrintTalk = 3, PrintCenter = 4;

    public void Subscribe(TextMsgMessage messages) => messages.Fired += Print;

    public void Print(TextMsg message)
    {
        int dest = message.Destination;
        Span<string> parts = new string[5];
        parts[0] = Resolve(message.Text ?? "", 0, ref dest);
        var p = message.Params ?? [];
        for (int i = 1; i < 5; i++)
            parts[i] = Resolve(i - 1 < p.Length ? p[i - 1] ?? "" : "", i, ref dest);

        if (show is { } s && !s.Value)
            return;

        string text = Construct(parts[0], parts[1], parts[2], parts[3], parts[4]);
        switch (dest)
        {
            case PrintCenter:
                centre.Print(text.Replace('\r', '\n'));
                break;
            case PrintNotify:
            case PrintConsole:
                log.Msg(WithNewline(text).Replace('\r', '\n'));
                break;
            case PrintTalk:
                string line = WithNewline(text).Replace('\r', '\n');
                chat?.Print(line);
                log.Msg(line);
                break;
        }
    }

    // LookupString, then vgui's Find; a parameter stock could not localise
    // loses its end newline (it is meant for substitution).
    private string Resolve(string raw, int index, ref int dest)
    {
        string s = raw;
        if (s.Length > 0 && s[0] == '#' && hud.TextMessageGet(s[1..]) is { } title)
        {
            if (title.Effect < 0)
                dest = -title.Effect;
            s = title.GetMessage();
        }
        string found = s.Length > 0 ? hud.Localise(s) : "";
        if (found.Length > 0 && found != s)
            return found;
        if (index > 0 && s.Length > 0 && (s[^1] == '\n' || s[^1] == '\r'))
            s = s[..^1];
        return s;
    }

    /// <summary>vgui's ConstructString as TextMsg uses it: %s1..%s4 replaced by the parameters.</summary>
    public static string Construct(string format, string p1, string p2, string p3, string p4)
    {
        var b = new StringBuilder(format.Length + 32);
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '%' && i + 2 < format.Length + 0 && format[i + 1] == 's' && format[i + 2] is >= '1' and <= '4')
            {
                b.Append((format[i + 2] - '0') switch { 1 => p1, 2 => p2, 3 => p3, _ => p4 });
                i += 2;
                continue;
            }
            b.Append(format[i]);
        }
        return b.ToString();
    }

    private static string WithNewline(string s)
        => s.Length > 0 && s[^1] != '\n' && s[^1] != '\r' ? s + "\n" : s;
}
