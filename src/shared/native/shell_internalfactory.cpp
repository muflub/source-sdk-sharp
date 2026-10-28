//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The SOURCESHARP_FACTORY_INTERNAL factory: a version string in, this side's
// singleton out, NULL for a name it does not serve. Not exported; managed reaches
// it through the root. It answers from DLL load, before the engine factories exist.

#include "managed_side.h"
#include "shell_vtable_interfaces.h"
#include "ss_string.h"
#include "ishim_root.h"
#include "shell_shim_console.h"
#include "shell_shim_areas.h"
#include "ss_fwd.h"

#include "managed_accept.h"
#ifndef CLIENT_DLL

#include "shell_managed_areas.h"
#include "shell_shim_root_server.h"
#else
#include "shell_managed_areas_client.h"
#include "client_factorylist.h"
#include "particles_lib_client.h"
#include "fwd_shadows.h"
void *ShadowEnv_Acquire();
#include "ss_particles/iparticlelib.h"
#endif

#ifndef CLIENT_DLL
#include "ishim_frame.h"
#include "ishim_level.h"
#endif

#ifndef CLIENT_DLL
// Defined in shell_frame.cpp.
#endif

#ifdef CLIENT_DLL
// Defined beside the root.
CShimAreasClient &ShimClientAreasOfRoot();
#endif

#ifdef CLIENT_DLL
void *SourceSharp_InternalFactory( const char *pszName )
{
	if ( !pszName )
		return NULL;

	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_CONSOLE_VERSION ) )
		return ShimConsole_Acquire();

	// The forwarder libraries' tables, by the name managed asks for (SSFwd_<table>).
	if ( void *pTable = SSFwd_Serve( pszName ) )
		return pTable;

	// libss_particles.so's ISSParticleLib.
	if ( !ss::StrICmp( pszName, SS_PARTICLE_LIB_INTERFACE_VERSION ) )
		return ParticleLib_Acquire();

	// The environment the shadow forwarder table calls out over.
	if ( !ss::StrICmp( pszName, SS_SHADOW_ENV_VERSION ) )
		return ShadowEnv_Acquire();

	if ( void *pArea = ShimAreasClient_Acquire( ShimClientAreasOfRoot(), pszName ) )
		return pArea;

	// A name this side does not serve, reported as an engine factory's NULL is.
	return NULL;
}
#endif

#ifndef CLIENT_DLL
// The server's row set: its root's own members. The root is a member of the game
// DLL object.
void *CShimRootServer::Acquire( const char *pszName )
{
	if ( !pszName )
		return NULL;

	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_CONSOLE_VERSION ) )
		return static_cast<IShimConsole *>( &m_Console );

	// The forwarder libraries' tables, by the name managed asks for (SSFwd_<table>).
	if ( void *pTable = SSFwd_Serve( pszName ) )
		return pTable;

	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_FRAME_VERSION ) )
		return m_pFrame;

	if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_LEVEL_VERSION ) )
		return m_pLevel;

	// The generated areas.
	if ( void *pArea = ShimAreas_Acquire( m_Areas, pszName ) )
		return pArea;

	// A name this side does not serve, reported as an engine factory's NULL is.
	return NULL;
}

void *CShimRootServer::AcquireInterface( int32_t nType, const char *pszVersion, char *pError, int32_t nErrorBytes )
{
	// This side's own interfaces come from the root; engine factory kinds go to
	// the acquisition entry point.
	if ( nType == SOURCESHARP_FACTORY_INTERNAL )
	{
		void *pShim = Acquire( pszVersion );
		if ( !pShim )
			SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return pShim;
	}

	SourceSharpFactories_t factories = {};
	if ( m_pFactories )
	{
		factories.pAppSystem = m_pFactories->engineFactory;
		factories.pPhysics = m_pFactories->physicsFactory;
		factories.pFileSystem = m_pFactories->fileSystemFactory;
	}

	return SourceSharp_AcquireInterface( (SourceSharpFactoryType)nType, pszVersion, pError, nErrorBytes, &factories );
}
#endif

// The client's root, handed to managed at load. It owns the areas by value.
#ifdef CLIENT_DLL
namespace
{

class CShimRoot : public IShimRoot
{
public:
	void *Acquire( const char *pszName ) override { return SourceSharp_InternalFactory( pszName ); }

	void *AcquireInterface( int32_t nType, const char *pszVersion, char *pError, int32_t nErrorBytes ) override
	{
		factorylist_t stored;
		memset( &stored, 0, sizeof( stored ) );
		FactoryList_Retrieve( stored );

		// The client has no filesystem factory; its appSystemFactory serves the filesystem.
		const SourceSharpFactories_t factories = { stored.appSystemFactory, stored.physicsFactory,
												   stored.appSystemFactory };
		return SourceSharp_AcquireInterface( (SourceSharpFactoryType)nType, pszVersion, pError, nErrorBytes,
											 &factories );
	}

	void Release() override {}

	CShimConsole m_Console;
	CShimAreasClient m_ClientAreas;
};

} // namespace

static CShimRoot g_ShimRoot;
#endif

IShimRoot *ShimRoot_Get( const void **ppTable )
{
	if ( ppTable )
		*ppTable = SSFwd_Serve( "SSFwd_ShimRoot" );

#ifdef CLIENT_DLL
	return &g_ShimRoot;
#else
	return &ShellGameDll_ShimRoot();
#endif
}

#ifdef CLIENT_DLL
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
bool CManagedSide::AcceptRoot( void *pObject, char *pWhy, size_t nWhy )
{
	CManagedHolders &managed = m_Holders;
	managed.m_pRoot =
		ManagedAccept<IManagedRoot>( pObject, kIManagedRootSlotHashes, kIManagedRootSlotCount, pWhy, nWhy );
	return managed.m_pRoot != NULL;
}

bool CManagedSide::AcquireAreas( char *pWhy, size_t nWhy )
{
	CManagedHolders &managed = m_Holders;
	IManagedRoot *pRoot = managed.m_pRoot;
	if ( !pRoot )
	{
		snprintf( pWhy, nWhy, "no managed root was accepted" );
		return false;
	}

	void *pRuntime = pRoot->Acquire( "IManagedRuntimeServer" );
	managed.m_pRuntimeServer = ManagedAccept<IManagedRuntimeServer>( pRuntime, kIManagedRuntimeServerSlotHashes,
																	 kIManagedRuntimeServerSlotCount, pWhy, nWhy );
	if ( !managed.m_pRuntimeServer )
		return false;

	return ManagedAreas_Acquire( managed.m_Areas, pRoot, pWhy, nWhy );
}

bool CManagedSide::Reattached( void *pObject, char *pWhy, size_t nWhy )
{
	// Across a reload native keeps every object; the root must be the one native
	// holds, and everything must validate through the patched thunks.
	CManagedHolders &managed = m_Holders;
	if ( pObject != managed.m_pRoot )
	{
		snprintf( pWhy, nWhy, "the reload handed back a different root (%p; native holds %p)", pObject,
				  (void *)managed.m_pRoot );
		return false;
	}

	return ManagedAcceptObject( managed.m_pRoot, kIManagedRootSlotHashes, kIManagedRootSlotCount, pWhy, nWhy ) &&
		ManagedAcceptObject( managed.m_pRuntimeServer, kIManagedRuntimeServerSlotHashes,
							 kIManagedRuntimeServerSlotCount, pWhy, nWhy ) &&
		ManagedAreas_Validate( managed.m_Areas, pWhy, nWhy );
}

IManagedRuntimeServer *ManagedRuntimeServer()
{
	return ShellGameDll_Managed().m_pRuntimeServer;
}

void CManagedSide::ReleaseRoot()
{
	// The areas first, each nulled before its release, then the root they came from.
	CManagedHolders &managed = m_Holders;
	ManagedAreas_Release( managed.m_Areas );
	ManagedRelease( managed.m_pRuntimeServer );
	ManagedRelease( managed.m_pRoot );
}
#else
// The client's holders, handled as the server's are above.
bool CManagedSide::AcceptRoot( void *pObject, char *pWhy, size_t nWhy )
{
	CManagedHoldersClient &managed = m_Holders;
	managed.m_pRoot =
		ManagedAccept<IManagedRoot>( pObject, kIManagedRootSlotHashes, kIManagedRootSlotCount, pWhy, nWhy );
	return managed.m_pRoot != NULL;
}

bool CManagedSide::AcquireAreas( char *pWhy, size_t nWhy )
{
	CManagedHoldersClient &managed = m_Holders;
	IManagedRoot *pRoot = managed.m_pRoot;
	if ( !pRoot )
	{
		snprintf( pWhy, nWhy, "no managed root was accepted" );
		return false;
	}

	void *pRuntime = pRoot->Acquire( "IManagedRuntimeClient" );
	managed.m_pRuntimeClient = ManagedAccept<IManagedRuntimeClient>( pRuntime, kIManagedRuntimeClientSlotHashes,
																	 kIManagedRuntimeClientSlotCount, pWhy, nWhy );
	if ( !managed.m_pRuntimeClient )
		return false;

	return ManagedAreasClient_Acquire( managed.m_Areas, pRoot, pWhy, nWhy );
}

bool CManagedSide::Reattached( void *pObject, char *pWhy, size_t nWhy )
{
	CManagedHoldersClient &managed = m_Holders;
	if ( pObject != managed.m_pRoot )
	{
		snprintf( pWhy, nWhy, "the reload handed back a different root (%p; native holds %p)", pObject,
				  (void *)managed.m_pRoot );
		return false;
	}

	return ManagedAcceptObject( managed.m_pRoot, kIManagedRootSlotHashes, kIManagedRootSlotCount, pWhy, nWhy ) &&
		ManagedAcceptObject( managed.m_pRuntimeClient, kIManagedRuntimeClientSlotHashes,
							 kIManagedRuntimeClientSlotCount, pWhy, nWhy ) &&
		ManagedAreasClient_Validate( managed.m_Areas, pWhy, nWhy );
}

IManagedRuntimeClient *ManagedRuntimeClient()
{
	return ShellClientDll_Managed().m_pRuntimeClient;
}

void CManagedSide::ReleaseRoot()
{
	CManagedHoldersClient &managed = m_Holders;
	ManagedAreasClient_Release( managed.m_Areas );
	ManagedRelease( managed.m_pRuntimeClient );
	ManagedRelease( managed.m_pRoot );
}
#endif

#ifdef CLIENT_DLL
void *ShimConsole_Acquire()
{
	return static_cast<IShimConsole *>( &g_ShimRoot.m_Console );
}
#endif

// The generated areas' acquire, release and accessors.
#include "shell_managed_areas.inc"
#ifdef CLIENT_DLL
#include "shell_managed_areas_client.inc"
#endif
