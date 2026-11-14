//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>A stock viewport panel by name, shown or hidden: what the server's VGUIMenu does.</summary>
public interface IViewportPanels
{
    void Run(string name, bool show);
}

/// <summary>
/// Stock's showpanel and hidepanel (baseviewport.cpp): a viewport panel by
/// name, shown or hidden, with exactly one argument. Every panel by name is the
/// managed UI's now.
/// </summary>
public sealed class ViewportPanelCommand(UiLoader ui) : IViewportPanels
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

/// <summary>
/// Stock's <c>changeteam</c> (TF2's client command, tf_viewport.cpp:127; the
/// command every Source game's controller action set and text window name for
/// "choose a new team"): the team menu, the same panel the server's VGUIMenu
/// "team" opens, so a mod's own team menu answers it by its claim. Nothing goes
/// to the server: the menu's choices send <c>jointeam</c>. Stock opens it only
/// when the player may change team (CanShowTeamMenu); here that is team play,
/// the server's mode where the client knows it, else the roster
/// (<see cref="TeamPlay"/>): deathmatch has no teams to choose.
/// </summary>
[ConCommand("changeteam", Help = "Choose a new team")]
public sealed class ChangeTeamCommand(IClientStandalones standalones, IViewportPanels panels, ILog log, IActiveMode? catalog = null)
{
    /// <summary>Stock's PANEL_TEAM.</summary>
    public const string TeamPanel = "team";

    public void Execute()
    {
        var resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        if (resource is null || !TeamPlay.Of(catalog, resource.State))
        {
            log.Msg("[SourceSharp/Ui] changeteam: not a team game, no team to choose\n");
            return;
        }
        panels.Run(TeamPanel, show: true);
    }
}
