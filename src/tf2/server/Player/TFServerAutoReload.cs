//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

// The server's half of cl_autoreload: stock caches the client's convar into
// CTFPlayer::m_bAutoReload when the userinfo changes
// (CTeamplayRules::ClientSettingsChanged, tf_gamerules.cpp:10269) and the
// weapon's post frame reads the player's. Hers reads it as the wish is asked,
// as the medigun's autoheal does its own (tf_medigun_autoheal); the engine's
// per-client copy is the cache.

/// <summary>The player's <c>cl_autoreload</c>, as they told the server.</summary>
[SideService(Side.Server, As = [typeof(ITFAutoReload)])]
public sealed class TFServerAutoReload(IConnectedClients? clients = null) : ITFAutoReload
{
    public bool Wants(int ownerIndex) =>
        ownerIndex > 0
        && int.TryParse(clients?.ClientConVar(ownerIndex, "cl_autoreload"), out int wish)
        && wish > 0;
}
