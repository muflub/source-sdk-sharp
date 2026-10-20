//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.ClientSystems;

/// <summary>
/// vgui_game_viewport.cpp's CViewportClientSystem, kClientSystems[]'s
/// "CViewportClientSystem" row: its Init lays the client mode out and
/// succeeds; every other stage is empty. Held to the pristine file by
/// ViewportClientSystemOracleTests over Fixtures/viewport-stock.tsv.
/// </summary>
public sealed class ViewportClientSystem(IClientModeLayout clientMode) : ManagedClientSystem
{
    public override string Name => "CViewportClientSystem";

    /// <summary>g_pClientMode->Layout(), then true.</summary>
    public override bool Init()
    {
        clientMode.Layout();
        return true;
    }
}
