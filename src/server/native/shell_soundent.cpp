//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The world's sound list's native entry points; the pool itself is managed
// (SoundEnt). Missing: displaysoundlist draws no crosses, as there is no managed
// debug-overlay line.

#include "ss_util.h"
#include "ss_soundent.h"

extern CGlobalVars *gpGlobals;
#include "sourcesharp.h"
#include "shell_server.h"
#include "shell_resolve.h"

#include "imanaged_areas.h"

// maxClients sizes the pool, so it crosses as an argument.
bool ss::SoundEnt_Init()
{
	IManagedSoundEnt *pApi = ManagedSoundEnt();

	if ( pApi == NULL )
		return true;

	pApi->ServerSoundEntInit( gpGlobals->maxClients, gpGlobals->curtime );
	return true;
}

// Called twice on the way out of a level, so idempotent, as stock's was.
void ss::SoundEnt_Shutdown()
{
	IManagedSoundEnt *pApi = ManagedSoundEnt();

	if ( pApi == NULL )
		return;

	pApi->ServerSoundEntShutdown();
}

// A client is leaving: clears its reserved slot.
void ss::SoundEnt_ResetClientSound( int iIndex )
{
	IManagedSoundEnt *pApi = ManagedSoundEnt();

	if ( pApi == NULL )
		return;

	pApi->ServerSoundEntResetClientSound( iIndex );
}

// Clients are numbered 1..maxClients and their reserved slots 0..maxClients-1.
int ss::SoundEnt_ClientSoundIndex( edict_t *pClient )
{
	int iReturn = ss::ENTINDEX( pClient ) - 1;

#ifdef _DEBUG
	if ( iReturn < 0 || iReturn >= gpGlobals->maxClients )
	{
		Msg( "** ClientSoundIndex returning a bogus value! **\n" );
	}
#endif // _DEBUG

	return iReturn;
}
