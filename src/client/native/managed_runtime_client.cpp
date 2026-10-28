//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//=============================================================================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
// The client's runtime adapter -- the mirror of
// sourcesharp/game/server/managed_runtime_client.cpp's server twin.
//
// Two things, and the class is not one of them: `CManagedRuntimeClient` lives
// in managed_runtime_client.h so a gtest can compile the SHIPPED bodies against
// a table it rewrites underneath them (see the header for why that is not
// optional). What is here is the loader.
//
//   LoadManagedRuntime( env )      -- brings the CLR up and hands back the
//                                     adapter.
//
// WHAT THIS FILE IS THE ANSWER TO. Before it, client.so had an
// `IClientRuntimeAdapter` with no methods and a `LoadManagedRuntime()` declared
// in sourcesharp_gamesystem.h and DEFINED NOWHERE IN THE TREE. The client still
// reached managed code -- CShellClientDLL::Init called
// SourceSharp_ClientEarlyInit(), which called a file-static game system's
// Init(), which called SourceSharp_LoadManagedSide() -- so a player connecting
// to a REMOTE server did get a managed client, and that is worth stating
// plainly because the shape of the gap invited the opposite conclusion. What
// was missing was not the load. It was the OWNER: no interface, no named
// caller, no validation of the slots, and a release that happened from a game
// system row's Shutdown() rather than from a partner of the load.
//
// THE SEQUENCE IS FACTORED, NOT MOVED. `SourceSharp_LoadManagedSide` still
// lives in sourcesharp/game/shared/sourcesharp_gamesystem.cpp, which compiles
// into BOTH server.so and client.so. Moving those seven hundred lines onto this
// client-only file would compile green here and leave server.so with no managed
// runtime at all -- this tree's documented shared-file failure, with the sides
// swapped.
//=============================================================================//

#include "tier0/dbg.h"
#include "cdll_client_int.h"
#include "cdll_int.h"

#include "managed_init_failure.h"
#include "managed_runtime_client.h"

// memdbgon must be the last include file in a .cpp file!!!

// The shared acquire-fill-LoadSide sequence. Defined in
// sourcesharp/game/shared/sourcesharp_gamesystem.cpp; declared here because
// the gamesystem publishes no header for it.
bool SourceSharp_LoadManagedSide( void );

//-----------------------------------------------------------------------------
// A CLIENT INIT THAT FAILED, REPORTED AND THEN FATAL -- and on this side it has
// to be fatal AT THE POINT OF FAILURE rather than by returning false.
//
// MEASURED (when the client still checked a ManagedApi table, before
// plan_client_vtables CE-last): with a required slot forced null under
// -nogamedll, the version of this that answered NULL and let CShellClientDLL::Init `return false` printed
// the slot name and then:
//
//     [SourceSharp] managed API slot 'Frame' is null -- ...
//     Stack overflow.
//     Segmentation fault (core dumped)
//
// The engine does not have a clean answer for IBaseClientDLL::Init saying no;
// it recurses until the stack is gone, and the crash is what the reader sees
// instead of the diagnostic. managed_init_failure.h already says this about
// the client in prose -- "Returning false out of Init() would NOT do the job on
// the client" -- and this is the same conclusion reached from the other end.
//
// SO IT USES THAT FILE'S MODEL AND NOT AN ERROR PATH OF ITS OWN. Same format,
// same reader, same Error() -- which is NORETURN (tier0/dbg.h), goes through
// the engine's spew handler, and reaches the console, console.log under
// -condebug, and a dialog where there is one.
//
// THE STAGE IS LoadSide: the one caller left (plan_client_vtables CE-last
// took the table check) is the shared load sequence answering no, and
// SourceSharp_LoadSide is the real function behind it. rc is SOURCESHARP_OK
// because there is no host result code here; Format prints no "code" line for
// OK, so the reader is not sent looking for one.
//-----------------------------------------------------------------------------
static void SourceSharp_FatalClientInitFailure( const char *pDetail )
{
	char szMessage[2048];

	// "client" spelled here rather than through SOURCESHARP_SIDE_TAG: that macro
	// is a per-file #define in the two shared translation units that need both
	// arms, and this file has only one side by construction.
	SourceSharpInit_Format( szMessage, sizeof( szMessage ),
		"client", kSourceSharpInitStage_LoadSide, SOURCESHARP_OK, pDetail );

	// "%s", not the message as the format: the detail carries a slot name and
	// one stray %s in it would read the stack.
	Error( "%s", szMessage );
}

//-----------------------------------------------------------------------------
// LOADING THE MANAGED RUNTIME.
//
// The server's LoadManagedRuntime seeds `engine` and `gpGlobals` from the
// environment before the sequence runs, because the sequence reads both and the
// shell owns the factory. This does the same two, from the client's own
// spellings of them:
//
//   `engine` is IVEngineClient here, and SourceSharp_GetModBinDir asks it for
//   the game directory -- on the client through GetGameDirectory(), on the
//   server through GetGameDir(). Without it the acquire has no path to the
//   managed assemblies.
//
//   `gpGlobals` is a CGlobalVarsBase * on this side, and it is stored as the
//   POINTER the engine handed the shell, not a copy of what it points at: the
//   engine writes through it every frame.
//
// BOTH ARE ALREADY ASSIGNED BY CShellClientDLL::Init before this runs, exactly
// as they are on the server, and this is a second assignment of one value
// rather than a second source for it. It is kept because it is what makes the
// loader's precondition a thing the loader states rather than a thing it
// inherits from its caller's line order.
//-----------------------------------------------------------------------------
IClientRuntimeAdapter *LoadManagedRuntime( IManagedRuntimeEnvironment *env )
{
	if ( env == NULL )
		return NULL;

	engine = (IVEngineClient *)env->GetEngineInterface( VENGINE_CLIENT_INTERFACE_VERSION, NULL );

	if ( engine == NULL )
	{
		// Fatal rather than a returned NULL, for the reason on
		// SourceSharp_FatalClientInitFailure: `return false` out of
		// IBaseClientDLL::Init is a stack overflow, not a diagnostic.
		SourceSharp_FatalClientInitFailure(
			"the engine did not hand out " VENGINE_CLIENT_INTERFACE_VERSION
			", so the managed assemblies cannot even be located -- "
			"SourceSharp_GetModBinDir asks it for the game directory" );
	}

	gpGlobals = &env->GetGlobalVars();

	// Fatal from the inside on every failure it can SEE: SourceSharp_LoadManagedSide
	// calls Error() rather than returning false, for the reason
	// managed_init_failure.h gives -- and that file's own comment names THIS
	// path as the one that forced it, because the client's first caller used to
	// be a void function that threw the answer away.
	//
	// So this arm is not reached today. It is fatal anyway rather than a
	// `return NULL`, because a returned NULL out of this function is a
	// `return false` out of IBaseClientDLL::Init, which is a stack overflow --
	// and "unreachable" is a property of the callee that a later edit can take
	// away silently.
	if ( !SourceSharp_LoadManagedSide() )
	{
		SourceSharp_FatalClientInitFailure(
			"the shared acquire-fill-LoadSide sequence answered no without "
			"stopping first, which it is not supposed to be able to do" );
	}

	// No table to check (plan_client_vtables CE-last), as on the server since
	// plan_engineapi_vtables E-last: the managed root and every client area were
	// accepted at their compilers' slot hashes inside SourceSharp_LoadManagedSide,
	// which refuses the load otherwise.

	// Allocated rather than a file-static. The adapter is stateless -- it has no
	// members and every method reads the table afresh -- so where it lives is
	// not a correctness question, and the project's rule is no file-scope
	// statics in SourceSharp's C++. Released by UnloadManagedRuntime, which
	// CShellClientDLL::Shutdown calls.
	return new CManagedRuntimeClient();
}

//-----------------------------------------------------------------------------
// Releasing it. The mirror of LoadManagedRuntime, and the reason the concrete
// type has to stay in this file: IClientRuntimeAdapter's destructor is
// protected, so `delete` through that pointer is ill-formed at any call site
// outside the hierarchy -- which is the interface saying the caller does not
// own the object.
//
// Null-safe on purpose. CShellClientDLL::Init returns false on many paths and
// the engine still calls Shutdown afterwards, so this is called with NULL in
// exactly the case the diagnostic matters most, and must not be the thing that
// crashes.
//-----------------------------------------------------------------------------
void UnloadManagedRuntime( IClientRuntimeAdapter *pRuntime )
{
	delete static_cast< CManagedRuntimeClient * >( pRuntime );
}
