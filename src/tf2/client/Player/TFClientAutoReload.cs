//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Client.Player;

// The client's half of cl_autoreload: the prediction reads the player's own
// convar (CTFPlayer::ShouldAutoReload, c_tf_player.cpp:299), so the replay
// wishes what the player's panel set without a round trip.

/// <summary>The local player's <c>cl_autoreload</c>, as they set it.</summary>
[SideService(Side.Client, As = [typeof(ITFAutoReload)])]
public sealed class TFClientAutoReload(ClAutoReloadConVar? convar = null) : ITFAutoReload
{
    public bool Wants(int ownerIndex) => convar?.Value != false;
}
