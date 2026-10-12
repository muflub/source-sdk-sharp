//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//=============================================================================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
// The server's runtime adapter -- plan_gamedll.md Part 2, Part 3 and Part 3b.
//
// Two things, and the class is not one of them: `CManagedRuntimeServer` lives
// in managed_runtime_server.h so a gtest can compile the SHIPPED bodies against
// a table it rewrites underneath them (see the header for why that is not
// optional). What is here is the loader and the instance.
//
//   LoadManagedRuntime( env )  -- brings the CLR up and hands back the adapter.
//
// THE REQUIRED-SLOT VALIDATOR IS GONE (plan_engineapi_vtables E17b). It named
// every ManagedApi slot the shell would call and refused the load on a null;
// since E17 the server calls none of them through the struct -- each is a
// method of a managed-built area, and ManagedAreas_Acquire refuses the load on
// an area that is missing or the wrong size, which is the same check, exact.
//
// THE SEQUENCE IS FACTORED, NOT MOVED. `SourceSharp_LoadManagedSide` still
// lives in sourcesharp/game/shared/sourcesharp_gamesystem.cpp, which compiles
// into BOTH server.so and client.so, and the client still reaches it from
// CSourceSharpSystem::Init via SourceSharp_ClientEarlyInit. Moving those seven
// hundred lines onto this server-only file would compile green here and leave
// client.so with no managed runtime at all -- this tree's documented shared-
// file failure, and the reason plan_gamedll.md Part 3 numbers the steps.
//=============================================================================//

#include "tier0/dbg.h"   // Warning
#include "eiface.h"

// The two globals seeded below (the R5 set, plan_server_novalve.md P5).
extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;

#include "managed_runtime_server.h"


// The shared acquire-fill-LoadSide sequence. Defined in
// sourcesharp/game/shared/sourcesharp_gamesystem.cpp; declared here for the
// same reason SourceSharp_GetManagedApi is -- the gamesystem publishes no
// header.
bool SourceSharp_LoadManagedSide( void );

//-----------------------------------------------------------------------------
// LOADING THE MANAGED RUNTIME, which on the server is what `LoadManagedRuntime`
// is named for.
//
// Stock reaches the acquire-fill-LoadSide sequence through
// `IGameSystem::InitAllSystems()`, called from CServerGameDLL::DLLInit -- an
// anonymous registration resolved by static-initialisation order. The shell
// calls none of that, so this is the server's only path to the CLR, and it is
// a NAMED call from a named point rather than a list the shell cannot read.
//
// The two globals seeded here are the two the sequence itself reads and the two
// stock's DLLInit assigns before it gets there (gameinterface.cpp:653, 713):
// `engine`, which SourceSharp_GetModBinDir asks for the game directory, and
// `gpGlobals`, which every managed frame hook reads the frame time out of.
// They are the whole reason `IManagedRuntimeEnvironment` exists (Part 0a #1):
// the shell owns DLLInit, so the shell owns the factory, and this asks IT
// rather than fetching its own.
//
// gpGlobals is stored as the POINTER the engine handed the shell, not a copy of
// what it points at: the engine writes through it every frame.
//
// `m_pServerClasses` and `m_pSendProxies` are NOT seeded here. They are
// Part 5's, and they are Phase 5's.
//-----------------------------------------------------------------------------
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

	// Fatal from the inside on every failure it can see: SourceSharp_LoadManagedSide
	// calls Error() rather than returning false, for the reason
	// managed_init_failure.h gives. The bool is kept because the gate makes a
	// second call a no-op that answers true, and a caller reading "did the
	// runtime come up" should not have to know that.
	if ( !SourceSharp_LoadManagedSide() )
		return NULL;

	// No table to check (plan_engineapi_vtables E-last): the server is handed
	// none. The managed root and every area were accepted at their compilers'
	// counts inside SourceSharp_LoadManagedSide, which refuses the load otherwise.

	// The managed-built runtime LoadSide just acquired through the managed root
	// (plan_engineapi_vtables D5). Returned for the shell's "did it come up";
	// the shell calls ManagedRuntimeServer() rather than keeping this, because
	// a reload replaces it.
	return ManagedRuntimeServer();
}
