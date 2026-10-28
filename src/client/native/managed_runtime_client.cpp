//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's managed runtime loader.
// SS_REASON h  the CoreCLR host and bootstrap dispatch

#include "tier0/dbg.h"
#include "cdll_client_int.h"
#include "cdll_int.h"

#include "managed_init_failure.h"
#include "managed_runtime_client.h"

// Defined in sourcesharp_gamesystem.cpp, which publishes no header for it.
bool SourceSharp_LoadManagedSide( void );

// Fatal at the point of failure: the engine answers a false IBaseClientDLL::Init with a
// stack overflow, which hides the diagnostic.
static void SourceSharp_FatalClientInitFailure( const char *pDetail )
{
	char szMessage[2048];

	// "client" spelled out: SOURCESHARP_SIDE_TAG is defined only in the shared TUs.
	SourceSharpInit_Format( szMessage, sizeof( szMessage ),
		"client", kSourceSharpInitStage_LoadSide, SOURCESHARP_OK, pDetail );

	// "%s" because the detail can carry a slot name.
	Error( "%s", szMessage );
}

// Seeds engine and gpGlobals before the shared sequence, which reads both. gpGlobals is
// the engine's pointer, not a copy: the engine writes through it every frame.
IClientRuntimeAdapter *LoadManagedRuntime( IManagedRuntimeEnvironment *env )
{
	if ( env == NULL )
		return NULL;

	engine = (IVEngineClient *)env->GetEngineInterface( VENGINE_CLIENT_INTERFACE_VERSION, NULL );

	if ( engine == NULL )
	{
		// Fatal rather than NULL, see SourceSharp_FatalClientInitFailure.
		SourceSharp_FatalClientInitFailure(
			"the engine did not hand out " VENGINE_CLIENT_INTERFACE_VERSION
			", so the managed assemblies cannot even be located -- "
			"SourceSharp_GetModBinDir asks it for the game directory" );
	}

	gpGlobals = &env->GetGlobalVars();

	// SourceSharp_LoadManagedSide calls Error() itself on failure; this stays fatal in case
	// it ever returns false, since NULL here becomes a false IBaseClientDLL::Init.
	if ( !SourceSharp_LoadManagedSide() )
	{
		SourceSharp_FatalClientInitFailure(
			"the shared acquire-fill-LoadSide sequence answered no without "
			"stopping first, which it is not supposed to be able to do" );
	}

	// Released by UnloadManagedRuntime from CShellClientDLL::Shutdown.
	return new CManagedRuntimeClient();
}

// IClientRuntimeAdapter's destructor is protected, so deletion happens here on the concrete
// type. Null-safe: the engine calls Shutdown after a failed Init.
void UnloadManagedRuntime( IClientRuntimeAdapter *pRuntime )
{
	delete static_cast< CManagedRuntimeClient * >( pRuntime );
}
