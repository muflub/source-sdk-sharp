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
/// own shows do. Stock skips the raise for a custom server page; here a
/// registered <see cref="ITFMapInfoGate"/> may skip it too.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFTextWindowActions)])]
public sealed class TFTextWindowUi(ITFMenuNavigation nav, IHooks<ITFMapInfoGate> gates) : ITFTextWindowActions
{
    public void Okay()
    {
        nav.Close("tf2textwindow");   // stock hides the window...

        // Every gate is asked; a throw is logged and refuses nothing.
        bool raise = true;
        gates.Each(nameof(ITFMapInfoGate.RaisesMapInfo), g => raise &= g.RaisesMapInfo());
        if (raise)
            nav.OpenPanel("mapinfo"); // ...and raises PANEL_MAPINFO (tf_shareddefs.h:164)
    }
}

/// <summary>
/// Whether the text window's OK raises the map info panel, and so the team
/// setup its CONTINUE runs for a player with no team (stock's
/// <c>CTFTextWindow::OnCommand</c> skips it for a custom server page,
/// tf_textwindow.cpp:224-228). A game whose players are placed on a team
/// and a class for them, with no map briefing to read, refuses it: the
/// window closes and nothing follows.
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, registered by
/// <see cref="TFUiRegistrar"/>): every gate is asked, and one refusal
/// refuses. TF2 registers none, so TF2's own OK always raises it.
/// </para>
/// </summary>
public interface ITFMapInfoGate
{
    /// <summary>False keeps the map info panel down after the text window's OK.</summary>
    bool RaisesMapInfo();
}
