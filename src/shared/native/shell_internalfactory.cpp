//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the shim layer's own CreateInterfaceFn =============//
// SS_REASON b  an interface resolved by name at load
//
// plan_native_api.md Part 4a. The SOURCESHARP_FACTORY_INTERNAL arm of the one
// acquisition entry point, and it has exactly the shape the engine's own
// factories have: a version string in, a registered singleton out, NULL for a
// name this side does not serve.
//
// NOT AN EXPORTED SYMBOL, and that is the ruling rather than an oversight. An
// export would need dlsym, and nothing in this port opens a module to acquire
// anything. The switch in shell_vtable_interfaces.cpp calls this directly;
// managed reaches it through the same slot it reaches the engine's factories
// through, with the kind as the first argument.
//
// PER SIDE AUTOMATICALLY, because each DLL compiles its own arms. There is no
// registration list to keep in step and no side marker to get wrong: the server
// serves what the server built, the client serves what the client built, and
// asking the wrong side for a name gets the same "factory does not know that
// interface" the engine's factories give.
//
// ANSWERABLE FROM DLL LOAD. The three engine kinds exist only from DLLInit/Init
// onward, and the switch reports "factory not yet available" before that. This
// one is exempt: the singletons are ours and they exist as soon as the DLL is
// mapped, which is why the exemption is written into the switch rather than
// hoped for here.
//
// NO COUNT COMES BACK WITH THE POINTER (plan_forwarders_everywhere.md):
// managed calls an area through its forwarder table, whose header carries the
// table's own slot count, and never through the area's vtable.
//
//=============================================================================//


#include "sourcesharp.h"
#include "shell_vtable_interfaces.h"
#include "ss_string.h"
#include "ishim_root.h"
#include "shell_shim_console.h"
#include "shell_shim_areas.h"
#include "ss_fwd.h"                   // SSFwd_Serve (plan_engine_forwarders.md)
#include "imanaged_root.h"
#include "managed_accept.h"
#ifndef CLIENT_DLL
#include "sourcesharp_gamesystem.h"   // IManagedRuntimeServer
#include "shell_managed_areas.h"     // CManagedAreas (tools/managed-areas)
#else
#include "shell_managed_areas_client.h"   // CManagedHoldersClient (plan_client_vtables CD)
#include "particles_lib_client.h"         // ParticleLib_Acquire (plan_particles_so.md)
#include "ss_particles/iparticlelib.h"    // SS_PARTICLE_LIB_INTERFACE_VERSION
#endif

#ifndef CLIENT_DLL
#include "ishim_frame.h"
#include "ishim_level.h"
#endif


#ifndef CLIENT_DLL
// Defined in sourcesharp/game/server/shell_frame.cpp, beside the singleton
// it hands back. Declared here rather than given a header whose only reader
// would be this file -- the shape shell_frame.cpp uses for the stock
// functions it reaches, and a drifted signature fails to link.
void *ShimFrame_Acquire();
void *ShimLevel_Acquire();
#endif

#ifndef CLIENT_DLL
// The shim root's areas; defined beside the root, below.
CShimAreas &ShimAreasOfRoot();
#else
// The client's shared bases (plan_client_vtables CB); defined beside the root.
CShimAreasClient &ShimClientAreasOfRoot();
#endif

//-----------------------------------------------------------------------------
// The row set. One line per shim interface this side implements.
//-----------------------------------------------------------------------------
void *SourceSharp_InternalFactory( const char *pszName )
{
	if ( !pszName )
		return NULL;

	// plan_engineapi_vtables A4: a SHARED area. Both sides serve it since
	// plan_client_vtables CA, where the client's LoadSideV2 acquires it.
	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_CONSOLE_VERSION ) )
		return ShimConsole_Acquire();

	// plan_engine_forwarders.md: the forwarder libraries' tables, by the name
	// managed asks for (SSFwd_<table>). Both sides serve every table: a
	// forwarder is handed the object it calls, so it belongs to neither side.
	// The count is in the table's own header; managed holds it against its
	// generated struct (Forwarders.TryBind).
	if ( void *pTable = SSFwd_Serve( pszName ) )
		return pTable;

#ifndef CLIENT_DLL
	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_FRAME_VERSION ) )
		return ShimFrame_Acquire();

	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_LEVEL_VERSION ) )
		return ShimLevel_Acquire();
#endif

#ifdef CLIENT_DLL
	// plan_particles_so.md: libss_particles.so's ISSParticleLib, the object in
	// the library itself (client.so's forwards call the same one).
	if ( !ss::StrICmp( pszName, SS_PARTICLE_LIB_INTERFACE_VERSION ) )
		return ParticleLib_Acquire();
#endif

#ifndef CLIENT_DLL
	// plan_engineapi_vtables Phase C: the generated areas (tools/shim-areas).
	if ( void *pArea = ShimAreas_Acquire( ShimAreasOfRoot(), pszName ) )
		return pArea;
#else
	// plan_client_vtables CB: the client's shared bases.
	if ( void *pArea = ShimAreasClient_Acquire( ShimClientAreasOfRoot(), pszName ) )
		return pArea;
#endif

	// A name this side does not serve. Not an error, and reported as the same
	// "factory does not know that interface" an engine factory's NULL gets:
	// IShimFrame exists only on the server, because the client has no
	// GameFrame to have steps of.
	return NULL;
}

//-----------------------------------------------------------------------------
// THE ROOT -- plan_engineapi_vtables.md step A3.
//
// The one native object the managed side is handed at load (LoadSideV2), and
// the vtable spelling of the factory above: Acquire is
// SourceSharp_InternalFactory, so every area and forwarder table this side
// serves is reachable from the root.
// Stateless, like the areas it hands out -- a singleton for the DLL's life.
//
// THE ROOT OWNS THE AREAS (step A4): each area that leaves the struct is a
// by-value member here, so the DLL gains one static for all of them rather
// than one per area. Both sides build it: the client's struct reaches its
// console through it until the client's own wave.
//-----------------------------------------------------------------------------
namespace
{

class CShimRoot : public IShimRoot
{
public:
	void *Acquire( const char *pszName ) override
	{
		return SourceSharp_InternalFactory( pszName );
	}

	void *AcquireInterface( int32_t nType, const char *pszVersion,
	                        char *pError, int32_t nErrorBytes ) override
	{
		return SourceSharp_AcquireInterface( (SourceSharpFactoryType)nType, pszVersion,
		                                     pError, nErrorBytes );
	}

	void Release() override
	{
	}

	CShimConsole m_Console;
#ifndef CLIENT_DLL
	// The native-side shim areas only. What native holds from managed (the
	// root, the runtime, the generated areas) is the game DLL object's:
	// ShellGameDll_Managed() (plan_managed_vtable_reload.md L2).
	CShimAreas m_Areas;
#else
	// plan_client_vtables CB: the client's shared bases (tools/shim-areas).
	CShimAreasClient m_ClientAreas;
#endif
};

} // namespace

static CShimRoot g_ShimRoot;

IShimRoot *ShimRoot_Get( const void **ppTable )
{
	if ( ppTable )
		*ppTable = SSFwd_Serve( "SSFwd_ShimRoot" );

	return &g_ShimRoot;
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
CShimConsole &ShimConsole()
{
	return g_ShimRoot.m_Console;
}
#endif

#ifdef CLIENT_DLL
CShimAreasClient &ShimClientAreasOfRoot()
{
	return g_ShimRoot.m_ClientAreas;
}
#endif

#ifndef CLIENT_DLL
CShimAreas &ShimAreasOfRoot()
{
	return g_ShimRoot.m_Areas;
}

IManagedRoot *ManagedRoot_Get()
{
	return ShellGameDll_Managed().m_pRoot;
}

bool ManagedRoot_Accept( void *pObject, char *pWhy, size_t nWhy )
{
	CManagedHolders &managed = ShellGameDll_Managed();
	managed.m_pRoot = ManagedAccept< IManagedRoot >( pObject, kIManagedRootSlotHashes,
	                                                 kIManagedRootSlotCount, pWhy, nWhy );
	return managed.m_pRoot != NULL;
}

bool ManagedRoot_AcquireAreas( char *pWhy, size_t nWhy )
{
	CManagedHolders &managed = ShellGameDll_Managed();
	IManagedRoot *pRoot = managed.m_pRoot;
	if ( !pRoot )
	{
		snprintf( pWhy, nWhy, "no managed root was accepted" );
		return false;
	}

	void *pRuntime = pRoot->Acquire( "IManagedRuntimeServer" );
	managed.m_pRuntimeServer = ManagedAccept< IManagedRuntimeServer >( pRuntime, kIManagedRuntimeServerSlotHashes,
	                                                                   kIManagedRuntimeServerSlotCount, pWhy, nWhy );
	if ( !managed.m_pRuntimeServer )
		return false;

	return ManagedAreas_Acquire( managed.m_Areas, pRoot, pWhy, nWhy );
}

bool ManagedRoot_Reattached( void *pObject, char *pWhy, size_t nWhy )
{
	// plan_managed_vtable_reload.md L4: native acquired once and kept every
	// object across the reload; the new build patched the same blocks. The
	// root must be the one native holds, and the root, the runtime and every
	// area must validate through their patched thunks.
	CManagedHolders &managed = ShellGameDll_Managed();
	if ( pObject != managed.m_pRoot )
	{
		snprintf( pWhy, nWhy, "the reload handed back a different root (%p; native holds %p)",
		          pObject, (void *)managed.m_pRoot );
		return false;
	}

	return ManagedAcceptObject( managed.m_pRoot, kIManagedRootSlotHashes, kIManagedRootSlotCount, pWhy, nWhy )
		&& ManagedAcceptObject( managed.m_pRuntimeServer, kIManagedRuntimeServerSlotHashes,
		                        kIManagedRuntimeServerSlotCount, pWhy, nWhy )
		&& ManagedAreas_Validate( managed.m_Areas, pWhy, nWhy );
}

IManagedRuntimeServer *ManagedRuntimeServer()
{
	return ShellGameDll_Managed().m_pRuntimeServer;
}

void ManagedRoot_Release()
{
	// The areas first, each nulled before its release, then the root they came from.
	CManagedHolders &managed = ShellGameDll_Managed();
	ManagedAreas_Release( managed.m_Areas );
	ManagedRelease( managed.m_pRuntimeServer );
	ManagedRelease( managed.m_pRoot );
}
#else
//-----------------------------------------------------------------------------
// plan_client_vtables CD: the client's root, runtime and areas, held on the
// client DLL object (ShellClientDll_Managed()), accepted, re-attached across a
// reload and released exactly as the server's are above.
//-----------------------------------------------------------------------------
IManagedRoot *ManagedRoot_Get()
{
	return ShellClientDll_Managed().m_pRoot;
}

bool ManagedRoot_Accept( void *pObject, char *pWhy, size_t nWhy )
{
	CManagedHoldersClient &managed = ShellClientDll_Managed();
	managed.m_pRoot = ManagedAccept< IManagedRoot >( pObject, kIManagedRootSlotHashes,
	                                                 kIManagedRootSlotCount, pWhy, nWhy );
	return managed.m_pRoot != NULL;
}

bool ManagedRoot_AcquireAreas( char *pWhy, size_t nWhy )
{
	CManagedHoldersClient &managed = ShellClientDll_Managed();
	IManagedRoot *pRoot = managed.m_pRoot;
	if ( !pRoot )
	{
		snprintf( pWhy, nWhy, "no managed root was accepted" );
		return false;
	}

	void *pRuntime = pRoot->Acquire( "IManagedRuntimeClient" );
	managed.m_pRuntimeClient = ManagedAccept< IManagedRuntimeClient >( pRuntime, kIManagedRuntimeClientSlotHashes,
	                                                                   kIManagedRuntimeClientSlotCount, pWhy, nWhy );
	if ( !managed.m_pRuntimeClient )
		return false;

	return ManagedAreasClient_Acquire( managed.m_Areas, pRoot, pWhy, nWhy );
}

bool ManagedRoot_Reattached( void *pObject, char *pWhy, size_t nWhy )
{
	CManagedHoldersClient &managed = ShellClientDll_Managed();
	if ( pObject != managed.m_pRoot )
	{
		snprintf( pWhy, nWhy, "the reload handed back a different root (%p; native holds %p)",
		          pObject, (void *)managed.m_pRoot );
		return false;
	}

	return ManagedAcceptObject( managed.m_pRoot, kIManagedRootSlotHashes, kIManagedRootSlotCount, pWhy, nWhy )
		&& ManagedAcceptObject( managed.m_pRuntimeClient, kIManagedRuntimeClientSlotHashes,
		                        kIManagedRuntimeClientSlotCount, pWhy, nWhy )
		&& ManagedAreasClient_Validate( managed.m_Areas, pWhy, nWhy );
}

IManagedRuntimeClient *ManagedRuntimeClient()
{
	return ShellClientDll_Managed().m_pRuntimeClient;
}

void ManagedRoot_Release()
{
	CManagedHoldersClient &managed = ShellClientDll_Managed();
	ManagedAreasClient_Release( managed.m_Areas );
	ManagedRelease( managed.m_pRuntimeClient );
	ManagedRelease( managed.m_pRoot );
}
#endif

void *ShimConsole_Acquire()
{
	return static_cast< IShimConsole * >( &g_ShimRoot.m_Console );
}

// The generated areas' acquire, release and accessors (tools/managed-areas).
#include "shell_managed_areas.inc"
#ifdef CLIENT_DLL
#include "shell_managed_areas_client.inc"   // the client's (plan_client_vtables CD)
#endif
