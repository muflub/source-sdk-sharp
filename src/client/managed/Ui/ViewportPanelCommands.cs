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
/// Stock's showpanel and hidepanel (baseviewport.cpp): a viewport panel by
/// name, shown or hidden, with exactly one argument. Every panel by name is the
/// managed UI's now.
/// </summary>
public sealed class ViewportPanelCommand(UiLoader ui)
{
    /// <summary>The panel named by <paramref name="args"/>, or nothing for any other count of arguments.</summary>
    public void Run(string args, bool show)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            ui.ViewportPanel(parts[0], show);
    }
}

[ConCommand("showpanel", Help = "Shows a viewport panel <name>")]
public sealed class ShowPanelCommand(ViewportPanelCommand panels) : IConCommand
{
    public void Execute(string args) => panels.Run(args, show: true);
}

[ConCommand("hidepanel", Help = "Hides a viewport panel <name>")]
public sealed class HidePanelCommand(ViewportPanelCommand panels) : IConCommand
{
    public void Execute(string args) => panels.Run(args, show: false);
}
