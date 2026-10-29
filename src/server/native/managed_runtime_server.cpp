//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// The server's managed runtime loader. The load sequence itself is shared with the
// client (SourceSharp_LoadManagedSide, sourcesharp_gamesystem.cpp).

#include "eiface.h"

#include "managed_runtime_server.h"

#include "managed_side.h"
#include "shell_managed_areas.h"

// The server's only path to the CLR, called from the shell's DLLInit.
IManagedRuntimeServer *LoadManagedRuntime( IManagedRuntimeEnvironment *env, CManagedSide &side,
										   const CManagedHolders &managed, const char *pszGameDir )
{
	if ( env == NULL )
		return NULL;

	// Load calls Error() on failure; a repeat call answers true.
	if ( !side.Load( pszGameDir ) )
		return NULL;

	// The managed-built runtime, which a reload replaces: callers read the holders again.
	return managed.m_pRuntimeServer;
}
