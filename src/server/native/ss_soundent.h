//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's entry points to the managed sound-hint pool (SoundEnt), defined in
// shell_soundent.cpp.

#pragma once

class IHandleEntity;
struct edict_t;

namespace ss
{

// The managed pool's level start and end (CSoundEnt::InitSoundEnt /
// ShutdownSoundEnt). Init answers true, as stock's did.
bool SoundEnt_Init();
void SoundEnt_Shutdown();

// The client reserve: a player's slot in the list, and clearing it.
void SoundEnt_ResetClientSound( int iIndex );
int SoundEnt_ClientSoundIndex( edict_t *pClient );

} // namespace ss
