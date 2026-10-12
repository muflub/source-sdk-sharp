//=============================================================================//
// The client's runtime adapter -- the mirror of
// sourcesharp/game/server/managed_runtime_server.h.
//
// WHY A HEADER AT ALL, and the answer is the server's answer: the two
// properties this class has to have are properties of its BODIES, and a
// property of a body can only be tested by running it.
//
//   * every method is one forward and nothing else, and
//   * it holds no copy of the managed runtime.
//
// The second is the one that matters and the one that cannot be checked by
// reading. A runtime pointer cached in a constructor compiles, links, passes
// every existing test, and outlives the load that accepted it. So the class
// lives here, `managed_runtime_client.cpp` holds the loader, and
// sourcesharp/test/managed_runtime_client_test.cpp compiles this same header
// against runtimes it swaps underneath the adapter. Nothing there is a
// stand-in for the shipped class: it IS the shipped class.
//
// ENGINE-FREE, deliberately, for the same reason the server's is. Adding a
// `cbase.h` to this file would end the test.
//=============================================================================//

#pragma once

#include "sourcesharp_gamesystem.h"
#include "sourcesharp.h"
#include "imanaged_runtime_client.h"

#ifdef CLIENT_DLL

// Forward-declared rather than included: this load's managed-built runtime
// (plan_client_vtables CE1), held by the client DLL object and answered by
// shell_internalfactory.cpp.
IManagedRuntimeClient *ManagedRuntimeClient();

// The symmetric partner of LoadManagedRuntime. Defined in
// sourcesharp_gamesystem.cpp, which compiles into both .so files, so both
// sides reach the same teardown.
void SourceSharp_UnloadManagedSide( void );

//-----------------------------------------------------------------------------
// THE ADAPTER. One forward per method, and nothing else.
//
// NO CACHED RUNTIME, AND NO MEMBERS AT ALL. `ManagedRuntimeClient()` is asked
// on every call (plan_client_vtables CE1). The object is managed-built: a hot
// reload patches it in place, but the final unload releases it and the next
// load accepts a new one, so the holder on the client DLL object is the one
// place that knows which is live. `sizeof( CManagedRuntimeClient )` is one
// pointer for exactly that reason, as it was when this forwarded into the
// ManagedApi table.
//
// NO NULL TESTS. The three forwards used to be read at their call sites as
// `if ( g_bSourceSharpLoaded && g_ManagedApi.Frame )` -- a null slot was a
// frame silently not delivered to managed code, forever, with nothing in the
// log. The runtime is answered once now, at load: the managed root and the
// runtime are validated against their slot hashes (ManagedRoot_Accept,
// shell_internalfactory.cpp), and a load that fails that is refused.
//
// The one window in which there is no runtime -- inside a reload, between the
// unload and the next LoadSide -- is not observable from here: the reload is
// serviced from the top of CSourceSharpSystem::Update, inside which no other engine->shell entry
// can arrive, and the row's own `g_bSourceSharpLoaded` test is what covers the
// interval before the first load and after the last unload.
//-----------------------------------------------------------------------------
class CManagedRuntimeClient : public IClientRuntimeAdapter
{
public:
	// THE ONE METHOD THAT DOES NOT FORWARD TO THE TABLE, and it is the loader's
	// partner rather than a lifecycle slot -- the server's OnSystemStop word
	// for word. The managed half of the teardown is inside that call -- it
	// invokes the `Shutdown` slot before UnloadSide -- so a second "stop" slot
	// beside it would be two names for one moment. The CLR is refcounted across
	// client.so and server.so, and this is the CLIENT's release.
	void OnSystemStop() override
	{
		SourceSharp_UnloadManagedSide();
	}

	void OnLevelInit( const char *pMapName ) override
	{
		ManagedRuntimeClient()->LevelInit( pMapName );
	}

	void OnLevelShutdown() override
	{
		ManagedRuntimeClient()->LevelShutdown();
	}

	void OnFrame( float flFrameTime ) override
	{
		ManagedRuntimeClient()->Frame( flFrameTime );
	}
};

#endif // CLIENT_DLL
