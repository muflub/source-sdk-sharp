//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The pipes panel's reads (tf_hud_demomanpipes.cpp): the sticky bombs out,
/// off the local player's decoded carrier — stock's
/// <c>CTFPlayer::GetNumActivePipebombs</c> walks the carried launchers for
/// the pipebomb one and reads its <c>m_iPipebombCount</c>
/// (tf_player_shared.cpp:13065); here the count rides the carrier itself
/// (<see cref="TFPlayerNetState.PipebombCount"/>), written by the tray on
/// every stickying and unstickying. ShouldDraw (:102-122) is a live demoman;
/// the ghost, minigame and match-summary arms have no port to gate on.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFDemomanPipes)])]
public sealed class TFHudDemomanPipes(IEngineHud screen, ITFPlayerData data) : ITFDemomanPipes
{
    public bool Present
        => Carrier is { } state && state.Class() == TFClass.Demoman
           && screen.LocalPlayerHealth > 0;          // dead: stock's IsAlive arm

    public int Count => Carrier?.PipebombCount ?? 0;

    /// <summary>The local player's decoded carrier, 0 the world and out of
    /// range nobody (the same slot guard TFHudLocalPlayer keeps).</summary>
    private TFPlayerNetState? Carrier
        => screen.LocalPlayerIndex > 0 ? data.Networked(screen.LocalPlayerIndex) : null;
}
