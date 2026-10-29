//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's load-game player gate.

#pragma once

inline bool ShellSave_LoadGameNeedsPlayer(bool bLoadGame, bool bPlayerAlreadyBound)
{
	return bLoadGame && !bPlayerAlreadyBound;
}
