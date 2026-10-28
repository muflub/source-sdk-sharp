//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Hands managed the engine interface pointers this DLL was given, by version string, plus
// client proxies for the IVEngineClient calls a managed function pointer cannot express.

#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#else
#include "eiface.h"
#endif

#ifdef CLIENT_DLL
#include "con_nprint.h"
#endif
#include "filesystem.h"

#include "engine/IStaticPropMgr.h"
#include "engine/IEngineTrace.h"
#include "engine/ivdebugoverlay.h"
#include "engine/IEngineSound.h"
#include "igameevents.h"
#include "vphysics_interface.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

#include "networkstringtabledefs.h"

// Without this, `random` resolves to <stdlib.h>'s random() rather than
// sharedInterface.h's IUniformRandomStream *.
#ifdef CLIENT_DLL

#include "inputsystem/iinputsystem.h"
#endif

// Stock already stores the engine's factories; the two sides' factorylist_t differ in
// shape, hence the per-side FactoryFor().
#ifdef CLIENT_DLL
#include "client_factorylist.h"
#else
#include "ss_init_factory.h"
using ss::factorylist_t;
using ss::FactoryList_Retrieve;
#endif

#include "shell_vtable_interfaces.h"

#include "ss_string.h"

// The engine interface versions this side was built to serve. Decided here rather than
// by asking a factory: on a listen server engine.so would hand server.so the client's
// VEngineClient014.
namespace
{

bool SourceSharpServes( const char *pszVersion )
{
	if ( !pszVersion )
		return false;

	if ( !ss::StrICmp( pszVersion, "VEngineRandom001" ) )
		return true;

	// The engine registers two IVModelInfo objects; one forwarder table calls either.
	if ( !ss::StrICmp( pszVersion, "VModelInfoServer004" ) || !ss::StrICmp( pszVersion, "VModelInfoClient006" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, "VBaseFileSystem011" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, "VPhysicsSurfaceProps001" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, VPHYSICS_COLLISION_INTERFACE_VERSION ) )
		return true;

	if ( !ss::StrICmp( pszVersion, SOUNDEMITTERSYSTEM_INTERFACE_VERSION ) )
		return true;

	if ( !ss::StrICmp( pszVersion, VDEBUG_OVERLAY_INTERFACE_VERSION ) )
		return true;

#ifdef CLIENT_DLL
	if ( !ss::StrICmp( pszVersion, "VEngineClient014" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, VPHYSICS_INTERFACE_VERSION ) )
		return true;

	if ( !ss::StrICmp( pszVersion, INPUTSYSTEM_INTERFACE_VERSION ) )
		return true;

	// The client's world, for prediction's point contents.
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_ENGINETRACE_CLIENT ) )
		return true;

	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_STATICPROPMGR_CLIENT ) )
		return true;

	if ( !ss::StrICmp( pszVersion, IENGINESOUND_CLIENT_INTERFACE_VERSION ) )
		return true;
#else
	if ( !ss::StrICmp( pszVersion, "VEngineServer023" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_STATICPROPMGR_SERVER ) )
		return true;

	// The server's world, for the movement loop's point contents.
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_ENGINETRACE_SERVER ) )
		return true;

	// Unserved, the server fires no game events at all.
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_GAMEEVENTSMANAGER2 ) )
		return true;

	if ( !ss::StrICmp( pszVersion, IENGINESOUND_SERVER_INTERFACE_VERSION ) )
		return true;

	// The whole filesystem, for CreateDirHierarchy; its IBaseFileSystem half is above.
	if ( !ss::StrICmp( pszVersion, FILESYSTEM_INTERFACE_VERSION ) )
		return true;
#endif

	// Each side's own container; engine.so registers both names on a listen server.
#ifdef CLIENT_DLL
	if ( !ss::StrICmp( pszVersion, INTERFACENAME_NETWORKSTRINGTABLECLIENT ) )
		return true;
#else
	if ( !ss::StrICmp( pszVersion, INTERFACENAME_NETWORKSTRINGTABLESERVER ) )
		return true;
#endif

	return false;
}

// The CreateInterfaceFn for a factory kind on this side, read from stock's stored list
// on every call. NULL means no factory of that kind; *pbStored says whether the list
// has been stored at all.
CreateInterfaceFn FactoryFor( SourceSharpFactoryType eType, bool *pbStored )
{
	factorylist_t factories;
	memset( &factories, 0, sizeof( factories ) );
	FactoryList_Retrieve( factories );

#ifdef CLIENT_DLL
	CreateInterfaceFn pAppSystem = factories.appSystemFactory;

	// The client has no filesystem factory; its appSystemFactory serves the filesystem.
	CreateInterfaceFn pFileSystem = factories.appSystemFactory;
#else
	CreateInterfaceFn pAppSystem = factories.engineFactory;
	CreateInterfaceFn pFileSystem = factories.fileSystemFactory;
#endif

	// Both sides set appSystem in the same call as the others, so it marks the list
	// stored. The engine calls IServerDLLSharedAppSystems before either Init.
	*pbStored = ( pAppSystem != NULL );

	switch ( eType )
	{
		case SOURCESHARP_FACTORY_APPSYSTEM:
			return pAppSystem;
		case SOURCESHARP_FACTORY_PHYSICS:
			return factories.physicsFactory;
		case SOURCESHARP_FACTORY_FILESYSTEM:
			return pFileSystem;
		default:
			return NULL;
	}
}

} // namespace

// pError is written only on failure, truncating.
void *SourceSharp_AcquireInterface( SourceSharpFactoryType eType, const char *pszVersion, char *pError,
									int32_t nErrorBytes )
{
	if ( eType != SOURCESHARP_FACTORY_APPSYSTEM && eType != SOURCESHARP_FACTORY_PHYSICS &&
		 eType != SOURCESHARP_FACTORY_FILESYSTEM && eType != SOURCESHARP_FACTORY_INTERNAL )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE );
		return NULL;
	}

	if ( eType == SOURCESHARP_FACTORY_INTERNAL )
	{
#ifdef CLIENT_DLL
		// Our own singletons exist from DLL load, so there is no factory to wait for.
		void *pShim = SourceSharp_InternalFactory( pszVersion );

		if ( !pShim )
		{
			SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
			return NULL;
		}

		return pShim;
#else
		// The server's root serves this kind itself and never reaches here.
		SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return NULL;
#endif
	}

	// Refused before any factory is asked, so another side's object is never reached.
	if ( !SourceSharpServes( pszVersion ) )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return NULL;
	}

	bool bStored = false;
	CreateInterfaceFn pFactory = FactoryFor( eType, &bStored );

	if ( !bStored )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_NOT_YET_AVAILABLE );
		return NULL;
	}

	if ( !pFactory )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_NO_SUCH_FACTORY );
		return NULL;
	}

	void *pInterface = pFactory( pszVersion, NULL );

	if ( !pInterface )
	{
		// A factory answers only for what it registered.
		SourceSharp_SayAcquireReason( pError, nErrorBytes, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return NULL;
	}

	return pInterface;
}

#ifdef CLIENT_DLL

// Con_NPrintf is varargs, which managed cannot call. The text is passed as an argument,
// never as the format string.
void SourceSharp_ConNPrintf( void *pEngineClient, int32_t nPos, const char *pszText )
{
	IVEngineClient *pEngine = static_cast<IVEngineClient *>( pEngineClient );

	if ( !pEngine || !pszText )
		return;

	pEngine->Con_NPrintf( nPos, "%s", pszText );
}

// Con_NXPrintf, with con_nprint_s flattened into scalars so managed need not mirror it.
void SourceSharp_ConNXPrintf( void *pEngineClient, int32_t nIndex, float flTimeToLive, const float *pColorRGB,
							  int32_t bFixedWidthFont, const char *pszText )
{
	IVEngineClient *pEngine = static_cast<IVEngineClient *>( pEngineClient );

	if ( !pEngine || !pszText || !pColorRGB )
		return;

	con_nprint_t info;
	info.index = nIndex;
	info.time_to_live = flTimeToLive;
	info.color[0] = pColorRGB[0];
	info.color[1] = pColorRGB[1];
	info.color[2] = pColorRGB[2];
	info.fixed_width_font = bFixedWidthFont != 0;

	pEngine->Con_NXPrintf( &info, "%s", pszText );
}

// GetLightForPoint(Fast) return Vector by value in xmm registers, which a managed function
// pointer cannot express. pOutXYZ is written only on success.
void SourceSharp_GetLightForPoint( void *pEngineClient, const float *pPosXYZ, int32_t bClamp, float *pOutXYZ )
{
	IVEngineClient *pEngine = static_cast<IVEngineClient *>( pEngineClient );

	if ( !pEngine || !pPosXYZ || !pOutXYZ )
		return;

	const Vector position( pPosXYZ[0], pPosXYZ[1], pPosXYZ[2] );
	const Vector light = pEngine->GetLightForPoint( position, bClamp != 0 );

	pOutXYZ[0] = light.x;
	pOutXYZ[1] = light.y;
	pOutXYZ[2] = light.z;
}

void SourceSharp_GetLightForPointFast( void *pEngineClient, const float *pPosXYZ, int32_t bClamp, float *pOutXYZ )
{
	IVEngineClient *pEngine = static_cast<IVEngineClient *>( pEngineClient );

	if ( !pEngine || !pPosXYZ || !pOutXYZ )
		return;

	const Vector position( pPosXYZ[0], pPosXYZ[1], pPosXYZ[2] );
	const Vector light = pEngine->GetLightForPointFast( position, bClamp != 0 );

	pOutXYZ[0] = light.x;
	pOutXYZ[1] = light.y;
	pOutXYZ[2] = light.z;
}

#endif // CLIENT_DLL
