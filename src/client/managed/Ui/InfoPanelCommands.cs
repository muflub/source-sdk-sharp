//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// Stock's cl_disablehtmlmotd (vguitextwindow.cpp), kept for the configs that
/// set it. The managed MOTD never shows a web page: an HTML or URL motd is shown
/// as its motd_text, which is what this asked for when set.
/// </summary>
[ConVar("cl_disablehtmlmotd", "0", Flags = ConVarFlags.Archive, Help = "Disable HTML motds.")]
public sealed class ClDisableHtmlMotdConVar : BoolConVar
{
}

/// <summary>
/// Stock's showinfo (vguitextwindow.cpp): the info panel with a type, a title,
/// a message and an optional command number, as keys type, title, message and
/// command. The panel reads its text from "msg", so stock's showinfo showed the
/// title over an empty body; this does the same.
/// </summary>
public sealed class ShowInfo(Func<IUiRuntime?> ui, ILog log)
{
    public void Run(string args)
    {
        var a = Split(args);
        if (a.Count < 3)
            return;
        if (ui() is not { } runtime || !runtime.Answers("info"))
        {
            log.Msg("Couldn't find info panel.\n");
            return;
        }
        var data = new List<KeyValuePair<string, string>>
        {
            new("type", int.TryParse(a[0], out int type) ? type.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"),
            new("title", a[1]),
            new("message", a[2]),
        };
        if (a.Count == 4)
            data.Add(new("command", a[3]));
        runtime.TakePanel("info", true, data);
    }

    /// <summary>A command's arguments as the engine's tokeniser splits them: by spaces, a quoted run one argument.</summary>
    public static List<string> Split(string args)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < args.Length)
        {
            while (i < args.Length && char.IsWhiteSpace(args[i]))
                i++;
            if (i >= args.Length)
                break;
            int start;
            if (args[i] == '"')
            {
                start = ++i;
                while (i < args.Length && args[i] != '"')
                    i++;
                parts.Add(args[start..i]);
                i++;
                continue;
            }
            start = i;
            while (i < args.Length && !char.IsWhiteSpace(args[i]))
                i++;
            parts.Add(args[start..i]);
        }
        return parts;
    }
}

[ConCommand("showinfo", Help = "Shows a info panel: <type> <title> <message> [<command number>]")]
public sealed class ShowInfoCommand(ShowInfo info) : IConCommand
{
    public void Execute(string args) => info.Run(args);
}
