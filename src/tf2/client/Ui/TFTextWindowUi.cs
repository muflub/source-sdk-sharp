//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Client.Ui;

/// <summary>
/// TF2's text window in game: what its OK does, stock's
/// <c>CTFTextWindow::OnCommand</c> (game/client/tf/vgui/tf_textwindow.cpp:220-229)
/// — hide the window and raise the map info panel
/// (<c>PANEL_MAPINFO</c>), by the panel's name as stock's viewport shows
/// it: the claim routes it to the map info screen just as the server's
/// own shows do.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFTextWindowActions)])]
public sealed class TFTextWindowUi(ITFMenuNavigation nav) : ITFTextWindowActions
{
    public void Okay()
    {
        nav.Close("tf2textwindow");   // stock hides the window...
        nav.OpenPanel("mapinfo");     // ...and raises PANEL_MAPINFO (tf_shareddefs.h:164)
    }
}
