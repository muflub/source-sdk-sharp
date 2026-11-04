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
    public void Run(string name, bool show) => ui.ViewportPanel(name, show);

    /// <summary>The panels the UI answers, for completion.</summary>
    public IEnumerable<string> Names() => ui.Current?.PanelNames ?? [];
}

[ConCommand("showpanel", Help = "Shows a viewport panel <name>")]
public sealed class ShowPanelCommand(ViewportPanelCommand panels)
{
    private IEnumerable<string> Names() => panels.Names();

    public void Execute([Complete(From = nameof(Names))] string name) => panels.Run(name, show: true);
}

[ConCommand("hidepanel", Help = "Hides a viewport panel <name>")]
public sealed class HidePanelCommand(ViewportPanelCommand panels)
{
    private IEnumerable<string> Names() => panels.Names();

    public void Execute([Complete(From = nameof(Names))] string name) => panels.Run(name, show: false);
}
