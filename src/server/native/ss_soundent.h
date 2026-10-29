//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's entry points to the managed sound-hint pool (SoundEnt), defined in
// shell_soundent.cpp.

#pragma once

class CGlobalVars;
class IManagedSoundEnt;
class IHandleEntity;
struct edict_t;

namespace ss
{

// The managed pool's level start and end (CSoundEnt::InitSoundEnt /
// ShutdownSoundEnt), over the load's sound-ent area (NULL outside a load: nothing to
// do). Init answers true, as stock's did.
bool SoundEnt_Init(IManagedSoundEnt* pApi, const CGlobalVars* pGlobals);
void SoundEnt_Shutdown(IManagedSoundEnt* pApi);

// Clears the reserved slot of the leaving client at this entity index.
void SoundEnt_ResetClientSound(IManagedSoundEnt* pApi, int iClientIndex);

} // namespace ss
