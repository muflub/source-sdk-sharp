//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the engine interfaces C# calls, by name =============//
// SS_REASON b  an interface resolved by name at load
//
// TWO JOBS AND NOTHING ELSE.
//
//   1. Hand managed an interface pointer for a version string this side was
//      built to serve.
//   2. Four proxies, for the four slots on IVEngineClient013 that a managed
//      `delegate* unmanaged<...>` cannot spell at all.
//
//-----------------------------------------------------------------------------
// NO COUNT GOES WITH THE POINTER (plan_forwarders_everywhere.md).
//
// Managed once indexed these objects' vtables through structs generated from
// the headers, so each acquire carried the compiler's slot count (the last
// method's slot, read out of a pointer to it) and managed refused a struct
// longer than it. Managed now calls every one of them through a generated
// native forwarder (the forwarder libraries, src/fwd), compiled against the
// same headers, so a slot is the compiler's to number and never managed's,
// and a forwarder table carries its own count in its header. What is left
// here is which names this side serves.
//
//-----------------------------------------------------------------------------
// AND THE POINTERS ARE THE ONES THE ENGINE ALREADY HANDED THIS DLL.
//
// No dlopen, no CreateInterface, no factory lookup. `engine`, `modelinfo`,
// `filesystem` and `random` are the globals the game DLL was given at DLLInit,
// and the version string here is a KEY rather than something asked of a module.
//
// The filesystem is the one worth reading twice. IBaseFileSystem is a SECOND
// base of CFileSystem_Stdio, so its subobject does not sit at the top of the
// object -- and nothing here computes that. `static_cast<IBaseFileSystem *>`
// makes the compiler emit the same adjustment it would emit at any other call
// site, which is the only way to get it that cannot be off by eight.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#else
// The server half is on public/ headers alone: the interfaces whose version
// strings it serves, below, and server.so's own factory list.
#include "eiface.h"
#endif


#ifdef CLIENT_DLL
#include "con_nprint.h"
#endif
#include "filesystem.h"
#include "engine/ivmodelinfo.h"
#include "engine/IStaticPropMgr.h"
#include "engine/IEngineTrace.h"
#include "engine/ivdebugoverlay.h"
#include "engine/IEngineSound.h"
#include "igameevents.h"
#include "vphysics_interface.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "vstdlib/random.h"
#include "networkstringtabledefs.h"

// WHERE `random` COMES FROM, and it has to be said out loud: without this
// header the name resolves to <stdlib.h>'s `long random( void )` and the
// assignment below fails with "invalid conversion from 'long int (*)()'". The
// interface pointer this file wants is game/shared/sharedInterface.h's
// `extern IUniformRandomStream *random`, beside `filesystem` and the rest.
#ifdef CLIENT_DLL
#include "sharedInterface.h"
#include "inputsystem/iinputsystem.h"
#endif

// STOCK ALREADY CAPTURED THE FACTORIES, so nothing here captures them again --
// and that is what keeps this file free of file-scope state (CLAUDE.md's rule,
// tools/statics-audit's baseline). The engine hands the game DLL its three
// CreateInterfaceFn pointers and stock stores them: on the server at DLLInit
// (shell_engineboundary.cpp:151, into shell_globals.cpp's s_factories) and on
// the client at Init (cdll_client_int.cpp:983, into client_factorylist.cpp's).
// The two structs are NOT the same shape -- the client has no fileSystemFactory
// at all and spells the first member appSystemFactory -- which is why the two
// headers cannot be collapsed and why FactoryFor() below is per side.
#ifdef CLIENT_DLL
#include "client_factorylist.h"
#else
#include "ss_init_factory.h"
using ss::factorylist_t;
using ss::FactoryList_Retrieve;
#endif

#include "sourcesharp.h"
#include "shell_vtable_interfaces.h"
#include "shell_diagnostics.h"
#include "ss_string.h"



//-----------------------------------------------------------------------------
// THE SERVED SET: the engine interface versions this side answers for.
//
// No count goes with a name (plan_forwarders_everywhere.md): managed calls each
// through a forwarder table, whose header carries its own slot count, so what
// is left is which names this side serves. A name not listed is "this side
// does not serve that name", which is a real answer rather than a missing one:
// the server does not claim IVEngineClient, which only the client is built
// against.
//
// SERVING IS DECIDED HERE RATHER THAN BY ASKING A FACTORY, and the difference
// is visible on a listen server: engine.so registers VEngineClient014 and would
// happily answer server.so for it. The object behind that name is the CLIENT's
// engine interface with the client's own state, and handing it to server-side
// managed code because a factory would answer is not a binding anybody asked
// for. What this side was BUILT to serve is what it serves.
//-----------------------------------------------------------------------------
namespace
{

bool SourceSharpServes( const char *pszVersion )
{
	if ( !pszVersion )
		return false;

	if ( !ss::StrICmp( pszVersion, "VEngineRandom001" ) )
		return true;

	// BOTH MODEL-INFO NAMES ARE SERVED ON BOTH SIDES. ivmodelinfo.h declares one
	// IVModelInfo and the engine registers two objects of that class under two
	// names, and one forwarder table calls either -- WHICH object comes back is
	// the factory's answer rather than this list's.
	if ( !ss::StrICmp( pszVersion, "VModelInfoServer004" )
	     || !ss::StrICmp( pszVersion, "VModelInfoClient006" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, "VBaseFileSystem011" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, "VPhysicsSurfaceProps001" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, VPHYSICS_COLLISION_INTERFACE_VERSION ) )
		return true;

	if ( !ss::StrICmp( pszVersion, SOUNDEMITTERSYSTEM_INTERFACE_VERSION ) )
		return true;

	// One overlay object under one name for both sides: shared interop's
	// EngineDebugOverlay and the client's flashlight trace visualiser.
	if ( !ss::StrICmp( pszVersion, VDEBUG_OVERLAY_INTERFACE_VERSION ) )
		return true;

#ifdef CLIENT_DLL
	if ( !ss::StrICmp( pszVersion, "VEngineClient014" ) )
		return true;

	// The client's vphysics, from the physics factory its Init was handed.
	if ( !ss::StrICmp( pszVersion, VPHYSICS_INTERFACE_VERSION ) )
		return true;

	if ( !ss::StrICmp( pszVersion, INPUTSYSTEM_INTERFACE_VERSION ) )
		return true;

	// The client's copy of the world, for the movement loop's point contents
	// (GameMovement.CheckWater through EngineBound.Trace) in prediction.
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_ENGINETRACE_CLIENT ) )
		return true;

	// The client's static props, for the client's physics world.
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_STATICPROPMGR_CLIENT ) )
		return true;

	// The engine's sound interface the managed ClientSoundEmitter, the tracers
	// and the soundscapes emit through (EngineSoundClient: sound_emitter.cpp's
	// client emit path, moved).
	if ( !ss::StrICmp( pszVersion, IENGINESOUND_CLIENT_INTERFACE_VERSION ) )
		return true;
#else
	if ( !ss::StrICmp( pszVersion, "VEngineServer023" ) )
		return true;

	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_STATICPROPMGR_SERVER ) )
		return true;

	// The server's copy of the world, for the movement loop's point contents.
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_ENGINETRACE_SERVER ) )
		return true;

	// The game event manager ServerGameEvents fires through. Unserved, the
	// server fired no game event at all (bd894daf1 to 01862e849: no
	// player_death, no player_changename, no round end).
	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_GAMEEVENTSMANAGER2 ) )
		return true;

	// The engine's sound interface the managed SoundEmitter emits through
	// (EngineSoundServer, plan_server_native_v2 T4a).
	if ( !ss::StrICmp( pszVersion, IENGINESOUND_SERVER_INTERFACE_VERSION ) )
		return true;

	// The whole filesystem, for its own CreateDirHierarchy: the managed
	// SoundEmitterSystem's -makereslists precache log makes its reslists
	// directory (plan_server_native_v2 T4a). Its IBaseFileSystem half is
	// VBaseFileSystem011's, above.
	if ( !ss::StrICmp( pszVersion, FILESYSTEM_INTERFACE_VERSION ) )
		return true;
#endif

	// Each side's own container: engine.so registers both names on a listen
	// server, and the other side's tables are not this side's to write. (The
	// client's row sat inside the server's #else, so it never compiled:
	// ServedInterfaceTests reads each side's preprocessed body.)
#ifdef CLIENT_DLL
	if ( !ss::StrICmp( pszVersion, INTERFACENAME_NETWORKSTRINGTABLECLIENT ) )
		return true;
#else
	if ( !ss::StrICmp( pszVersion, INTERFACENAME_NETWORKSTRINGTABLESERVER ) )
		return true;
#endif

	return false;
}

//-----------------------------------------------------------------------------
// WHICH CreateInterfaceFn ANSWERS FOR A FACTORY KIND, ON THIS SIDE.
//
// The engine's factory pointers never cross the boundary; this is where the
// enum turns into one of them. Read out of stock's stored list on every call
// rather than cached, because a cache here would be file-scope state -- which
// this port does not have (tools/statics-audit) -- and the read is a copy of
// three pointers, once per acquire.
//
// NULL means "this side has no factory of that kind". *pbStored says whether
// the list has been stored AT ALL, which is a different failure and gets a
// different reason.
//-----------------------------------------------------------------------------
CreateInterfaceFn FactoryFor( SourceSharpFactoryType eType, bool *pbStored )
{
	factorylist_t factories;
	memset( &factories, 0, sizeof( factories ) );
	FactoryList_Retrieve( factories );

#ifdef CLIENT_DLL
	CreateInterfaceFn pAppSystem = factories.appSystemFactory;

	// THE CLIENT HAS NO THIRD FACTORY. cdll_client_int.cpp asks appSystemFactory
	// for FILESYSTEM_INTERFACE_VERSION, so that is where
	// SOURCESHARP_FACTORY_FILESYSTEM goes on this side. A caller names the ROLE
	// it wants; where the role lives on this side is not its business.
	CreateInterfaceFn pFileSystem = factories.appSystemFactory;
#else
	CreateInterfaceFn pAppSystem = factories.engineFactory;
	CreateInterfaceFn pFileSystem = factories.fileSystemFactory;
#endif

	// STORED-NESS IS THE APPSYSTEM POINTER, because both sides set that member in
	// the same call that sets the others. Before DLLInit (server) or Init
	// (client) the whole struct is zero, and the engine calls
	// IServerDLLSharedAppSystems methods before either -- before there is a CLR
	// at all -- so this is defensive rather than expected.
	*pbStored = ( pAppSystem != NULL );

	switch ( eType )
	{
	case SOURCESHARP_FACTORY_APPSYSTEM:  return pAppSystem;
	case SOURCESHARP_FACTORY_PHYSICS:    return factories.physicsFactory;
	case SOURCESHARP_FACTORY_FILESYSTEM: return pFileSystem;
	default:                             return NULL;
	}
}

} // namespace


//-----------------------------------------------------------------------------
// THE CLOSED SET OF FAILURE REASONS IS NOT IN THIS FILE, and that is on
// purpose: shell_acquire_reasons.cpp holds it, because this translation unit
// includes cbase.h and nothing that includes cbase.h can be compiled into a
// standalone gtest. The set's guarantee -- closed, ASCII, no format specifier
// -- is a claim about every member, so a test has to walk the real function
// rather than a copy of the table. See that file's header and
// sourcesharp/test/acquire_reason_test.cpp.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE ONE ACQUISITION ENTRY POINT.
//
// pError IS THE CALLER'S BUFFER and is written ONLY on failure, so a success
// leaves whatever was there and managed never reads it -- which is why a stale
// reason is unreportable rather than merely unlikely. It TRUNCATES rather than
// refusing: a reason is prose for a human, and refusing to report a failure
// because the report was long would be absurd. V_strncpy NUL-terminates within
// the size it is given, so there is no helper to write here.
//-----------------------------------------------------------------------------
void *SourceSharp_AcquireInterface( SourceSharpFactoryType eType, const char *pszVersion,
                                    char *pError, int32_t nErrorBytes )
{
	if ( eType != SOURCESHARP_FACTORY_APPSYSTEM
	     && eType != SOURCESHARP_FACTORY_PHYSICS
	     && eType != SOURCESHARP_FACTORY_FILESYSTEM
	     && eType != SOURCESHARP_FACTORY_INTERNAL )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes,
		                              SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE );
		return NULL;
	}

	if ( eType == SOURCESHARP_FACTORY_INTERNAL )
	{
#ifdef CLIENT_DLL
		// EXEMPT FROM THE NOT-YET-AVAILABLE CASE, and it is the only kind that
		// is: these singletons are ours and exist from DLL load, so there is no
		// captured factory to be waiting for.
		void *pShim = SourceSharp_InternalFactory( pszVersion );

		if ( !pShim )
		{
			SourceSharp_SayAcquireReason( pError, nErrorBytes,
			                              SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
			return NULL;
		}

		return pShim;
#else
		// THE SERVER'S ROOT ANSWERS THIS KIND ITSELF (plan_server_native_v2 T6):
		// CShimRootServer::AcquireInterface serves it from its own members and
		// never reaches here with it, so a caller that does is told no.
		SourceSharp_SayAcquireReason( pError, nErrorBytes,
		                              SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return NULL;
#endif
	}

	// THE NAME IS RESOLVED BEFORE ANY ENGINE FACTORY IS ASKED: a name this side
	// was not built to serve is refused without asking, so a factory that would
	// answer for the other side's object (VEngineClient014 on a listen
	// server's server.so) is never reached.
	if ( !SourceSharpServes( pszVersion ) )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes,
		                              SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return NULL;
	}

	bool bStored = false;
	CreateInterfaceFn pFactory = FactoryFor( eType, &bStored );

	if ( !bStored )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes,
		                              SOURCESHARP_ACQUIRE_NOT_YET_AVAILABLE );
		return NULL;
	}

	if ( !pFactory )
	{
		SourceSharp_SayAcquireReason( pError, nErrorBytes,
		                              SOURCESHARP_ACQUIRE_NO_SUCH_FACTORY );
		return NULL;
	}

	void *pInterface = pFactory( pszVersion, NULL );

	if ( !pInterface )
	{
		// A FACTORY ANSWERS ONLY FOR WHAT IT REGISTERED. This is the reply for the
		// genuinely optional, and for a name served by a factory of a different
		// kind than the caller named.
		SourceSharp_SayAcquireReason( pError, nErrorBytes,
		                              SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
		return NULL;
	}

	return pInterface;
}


#ifdef CLIENT_DLL

//-----------------------------------------------------------------------------
// THE FOUR PROXIES, and each exists because managed CANNOT make the call --
// never because anybody decided it should not.
//
// A refusal that is a judgement is expressed by the impl class exposing no
// method; a refusal that is a fact about the calling convention needs somewhere
// for the call to happen, and that somewhere has to be C++.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Con_NPrintf is varargs, and a varargs call cannot be issued through a fixed
// indirect signature at all.
//
// THE TEXT IS AN ARGUMENT AND NEVER THE FORMAT STRING. `Con_NPrintf( pos, text )`
// would hand caller-controlled text to a printf format parser -- a "%s" in a
// model name or a player's chat line reads the stack. `"%s"` as the format with
// the text beside it is the whole fix and it costs nothing.
//-----------------------------------------------------------------------------
void SourceSharp_ConNPrintf( void *pEngineClient, int32_t nPos, const char *pszText )
{
	IVEngineClient *pEngine = static_cast< IVEngineClient * >( pEngineClient );

	if ( !pEngine || !pszText )
		return;

	pEngine->Con_NPrintf( nPos, "%s", pszText );
}

//-----------------------------------------------------------------------------
// Con_NXPrintf, the same, with con_nprint_s FLATTENED INTO SCALARS.
//
// Managed does not spell the struct. Its layout is native's business, and a C#
// copy of it would be a second declaration of a "structure [that] cannot be
// changed by mods" (con_nprint.h's own words) with nothing holding the two in
// step.
//-----------------------------------------------------------------------------
void SourceSharp_ConNXPrintf( void *pEngineClient, int32_t nIndex, float flTimeToLive,
                              const float *pColorRGB, int32_t bFixedWidthFont,
                              const char *pszText )
{
	IVEngineClient *pEngine = static_cast< IVEngineClient * >( pEngineClient );

	if ( !pEngine || !pszText || !pColorRGB )
		return;

	con_nprint_t info;
	info.index            = nIndex;
	info.time_to_live     = flTimeToLive;
	info.color[ 0 ]       = pColorRGB[ 0 ];
	info.color[ 1 ]       = pColorRGB[ 1 ];
	info.color[ 2 ]       = pColorRGB[ 2 ];
	info.fixed_width_font = bFixedWidthFont != 0;

	pEngine->Con_NXPrintf( &info, "%s", pszText );
}

//-----------------------------------------------------------------------------
// GetLightForPoint and GetLightForPointFast return `Vector` BY VALUE.
//
// A twelve-byte class of three floats is returned in xmm0 and xmm1 under the
// SysV classification, and `delegate* unmanaged<...>` has no spelling for that
// -- the return type is a class, not a scalar or a pointer. So the three floats
// come back through a pointer instead, which is a shape managed can express and
// the caller already owns the storage for.
//
// pOutXYZ is written ONLY on success, so "the light here is black" and "there
// was no engine" stay distinguishable at the call site.
//-----------------------------------------------------------------------------
void SourceSharp_GetLightForPoint( void *pEngineClient, const float *pPosXYZ,
                                   int32_t bClamp, float *pOutXYZ )
{
	IVEngineClient *pEngine = static_cast< IVEngineClient * >( pEngineClient );

	if ( !pEngine || !pPosXYZ || !pOutXYZ )
		return;

	const Vector position( pPosXYZ[ 0 ], pPosXYZ[ 1 ], pPosXYZ[ 2 ] );
	const Vector light = pEngine->GetLightForPoint( position, bClamp != 0 );

	pOutXYZ[ 0 ] = light.x;
	pOutXYZ[ 1 ] = light.y;
	pOutXYZ[ 2 ] = light.z;
}

void SourceSharp_GetLightForPointFast( void *pEngineClient, const float *pPosXYZ,
                                       int32_t bClamp, float *pOutXYZ )
{
	IVEngineClient *pEngine = static_cast< IVEngineClient * >( pEngineClient );

	if ( !pEngine || !pPosXYZ || !pOutXYZ )
		return;

	const Vector position( pPosXYZ[ 0 ], pPosXYZ[ 1 ], pPosXYZ[ 2 ] );
	const Vector light = pEngine->GetLightForPointFast( position, bClamp != 0 );

	pOutXYZ[ 0 ] = light.x;
	pOutXYZ[ 1 ] = light.y;
	pOutXYZ[ 2 ] = light.z;
}

#endif // CLIENT_DLL
