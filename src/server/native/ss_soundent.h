//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the world's sound-hint list, as server.so reaches it
//
// plan_server_novalve.md P5. game/server/soundent.h's CSoundEnt is a class
// server.so never constructs: its pool is `SoundEnt` in SourceSharp.Server, and
// all that is left native are the statics the tree spells (shell_soundent.cpp).
// No engine reads any of it, so per the plan's naming rule the server's own
// entry points live in namespace ss, on public/ headers alone, and a TU on the
// R5 include set calls these instead of CSoundEnt::.
//
// shell_soundent.cpp defines them. CSoundEnt's statics forward here until their
// last legacy caller has switched; then they, and soundent.h, leave server.so.
//=============================================================================//

#ifndef SOURCESHARP_SS_SOUNDENT_H
#define SOURCESHARP_SS_SOUNDENT_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"

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

#endif // SOURCESHARP_SS_SOUNDENT_H
