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
    public void Run(int type, string title, string message, string? command = null)
    {
        if (ui() is not { } runtime || !runtime.Answers("info"))
        {
            log.Msg("Couldn't find info panel.\n");
            return;
        }
        var data = new List<KeyValuePair<string, string>>
        {
            new("type", type.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("title", title),
            new("message", message),
        };
        if (command is not null)
            data.Add(new("command", command));
        runtime.TakePanel("info", true, data);
    }

}

[ConCommand("showinfo", Help = "Shows a info panel: <type> <title> <message> [<command number>]")]
public sealed class ShowInfoCommand(ShowInfo info)
{
    public void Execute(int type, string title, string message, string? command = null) => info.Run(type, title, message, command);
}
