//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// What is left of the native save code: the server's load-game player gate.
//
// The save field codec this header carried is managed now, on both sides:
// SaveBlockCodec and SaveBlockSession (src/shared/interop), held byte for byte
// to stock's CSave::WriteFields / CRestore::ReadFields by SaveCodecOracleTests
// (savegame-stock.tsv, savegame_codec_oracle_test). The server's save cluster
// forwards to them since plan_server_native_v2 T4c and the client's since the
// client save codec lane; docs/managed-saverestore.md keeps the reasoning the
// codec's sections pointed at.

#ifndef SOURCESHARP_SHELL_SAVEGAME_H
#define SOURCESHARP_SHELL_SAVEGAME_H
#ifdef _WIN32
#pragma once
#endif

// See docs/managed-saverestore.md, "5. Who creates the player on a savegame load".
inline bool ShellSave_LoadGameNeedsPlayer( bool bLoadGame, bool bPlayerAlreadyBound )
{
	return bLoadGame && !bPlayerAlreadyBound;
}

#endif // SOURCESHARP_SHELL_SAVEGAME_H
