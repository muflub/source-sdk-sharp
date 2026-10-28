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

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;

#include "managed_runtime_server.h"

// Defined in sourcesharp_gamesystem.cpp, which publishes no header.
bool SourceSharp_LoadManagedSide();

// The server's only path to the CLR, called from the shell's DLLInit. Seeds the two
// globals the sequence reads, as stock's DLLInit does: engine, and gpGlobals as the
// pointer the engine writes every frame.
IManagedRuntimeServer *LoadManagedRuntime( IManagedRuntimeEnvironment *env )
{
	if ( env == NULL )
		return NULL;

	engine = (IVEngineServer *)env->GetEngineInterface( INTERFACEVERSION_VENGINESERVER, NULL );

	if ( engine == NULL )
	{
		Warning( "[SourceSharp] no " INTERFACEVERSION_VENGINESERVER " -- the managed "
				 "runtime cannot be located without it\n" );
		return NULL;
	}

	gpGlobals = &env->GetGlobalVars();

	// SourceSharp_LoadManagedSide calls Error() on failure; a repeat call answers true.
	if ( !SourceSharp_LoadManagedSide() )
		return NULL;

	// The managed-built runtime. Callers use ManagedRuntimeServer() rather than keeping
	// this, because a reload replaces it.
	return ManagedRuntimeServer();
}
