//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the engine interfaces C# calls through their vtables =//
// SS_REASON b  an interface resolved by name at load
//
// TWO JOBS AND NOTHING ELSE.
//
//   1. Hand managed an interface pointer AND the number of entries in that
//      interface's vtable, taken from the COMPILER rather than counted.
//   2. Four proxies, for the four slots on IVEngineClient013 that a managed
//      `delegate* unmanaged<...>` cannot spell at all.
//
//-----------------------------------------------------------------------------
// WHY THERE IS A COUNT AT ALL, GIVEN THE VERSION STRING IS THE CONTRACT.
//
// It is not a check on the ENGINE. The engine's factory interfaces are stable:
// "VEngineClient014" names one layout, and Valve's own convention is that the
// string changes when the layout does. Asking the shipped module to prove
// itself every time it answers would be answering a question nobody asked.
//
// What the count checks is OURSELVES. SourceSharp.Abi/VtableLayouts.g.cs is
// GENERATED from these same headers by tools/vtable-layout, and a generated
// file can go stale -- somebody edits a header, or lands the generator's input
// without re-running it. Then the struct's field 71 is a different method from
// the engine's slot 71, no call faults, and the symptom arrives later looking
// like a bug in whatever ran next.
//
// So the number below is the compiler's own answer for how long the table is,
// managed compares it against sizeof(TVtbl) / sizeof(void *), and a stale
// generated file refuses to bind instead of calling the wrong function. One
// integer, once per acquire, nothing per call.
//
//-----------------------------------------------------------------------------
// THE COUNTS ARE NOT TYPED IN. `VTableHook::MemberOf( &I::LastMethod ).slot + 1`
// reads the slot out of a pointer-to-member-function, which under the Itanium
// ABI carries it directly -- so the number is whatever the compiler decided
// when it compiled these headers, and "count the virtual keywords" never
// happens. That matters most where it is least visible: IVModelInfo declares a
// virtual destructor, which occupies TWO slots, and MemberOf has already
// accounted for both.
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
// The server half is on public/ headers alone: the interfaces it counts
// slots of, below, and server.so's own factory list.
#include "eiface.h"
#endif

// THE DEFINES MUST MATCH THE ENGINE'S, and for cdll_int.h they are load-bearing:
// two of IVEngineClient013's virtuals are declared inside `#if` blocks, and each
// one shifts every slot below it. The shipped engine was built with USE_SDL in
// and NO_VOICE out (on Linux and macOS; Valve's Windows engine renders through
// DirectX and has no SDL, so there USE_SDL must be OUT: source_video_base.vpc
// sets SDL only with GL); this file's whole output is a slot count, so compiling it
// against a different answer would produce a wrong number that looks exactly
// like a right one.
//
// cbase.h has already pulled cdll_int.h in by here, so these cannot be DEFINED
// at this point -- only checked. Defining them lower down would be worse than
// useless: it would read as a guarantee and do nothing.
#ifdef CLIENT_DLL
#if defined( _WIN32 )
#if defined( USE_SDL )
#error "SourceSharp: the shipped Windows engine is built without USE_SDL and this translation unit has it. Every IVEngineClient013 slot from 127 down would be numbered one too high."
#endif
#elif !defined( USE_SDL )
#error "SourceSharp: the shipped engine is built with USE_SDL and this translation unit is not. Every IVEngineClient013 slot from 127 down would be numbered one too low."
#endif
#if defined( NO_VOICE )
#error "SourceSharp: the shipped engine is built with voice IN (NO_VOICE undefined) and this translation unit has NO_VOICE. Every IVEngineClient013 slot from 53 down would be numbered one too high."
#endif
#endif // CLIENT_DLL

#ifdef CLIENT_DLL
#include "con_nprint.h"
#endif
#include "filesystem.h"
#include "engine/ivmodelinfo.h"
#include "engine/IStaticPropMgr.h"
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
#include "vtable_hook.h"
#include "ss_string.h"



//-----------------------------------------------------------------------------
// THE PRECOMPILED LIST: one row per interface version string.
//
// `Entries()` is evaluated from the compiled headers, so the list is precompiled
// in the only sense that matters -- nothing in it was written down by a person.
//-----------------------------------------------------------------------------
namespace
{

int UniformRandomStreamEntries()
{
	// RandomFloatExp is the last declaration in vstdlib/random.h's
	// IUniformRandomStream. Its virtual destructor is COMMENTED OUT at line 28,
	// so there is no complete/deleting pair above these four.
	return VTableHook::MemberOf( &IUniformRandomStream::RandomFloatExp ).slot + 1;
}

int ModelInfoEntries()
{
	// UnregisterModelLoadCallback is the last declaration in IVModelInfo, and
	// the +2 for the virtual destructor is already inside the slot MemberOf
	// reports -- which is exactly why this is not a count of `virtual` lines.
	return VTableHook::MemberOf( &IVModelInfo::UnregisterModelLoadCallback ).slot + 1;
}

int PhysicsSurfacePropsEntries()
{
	// GetPhysicsParameters is IPhysicsSurfaceProps' last declaration; the
	// virtual destructor's two slots are inside the number MemberOf reports.
	// Served for plan_server_novalve.md P2.4: SourceSharp.Server's
	// SurfacePropertyManifest hands VPhysics the files the manifest names; and
	// on the client for the managed ragdolls' solids and the hitbox trace's
	// surface names (plan_physics_bones_native_zero Phase 3).
	return VTableHook::MemberOf( &IPhysicsSurfaceProps::GetPhysicsParameters ).slot + 1;
}

int PhysicsCollisionEntries()
{
	// ReadStat is IPhysicsCollision's last declaration; the virtual
	// destructor's two slots are inside the number MemberOf reports. Served
	// on the server for plan_physics_bones_native_zero 2.5 (managed's player
	// shadow builds its two box hulls with BBoxToCollide, PhysicsCollision.cs)
	// and on the client for Phase 3's managed ragdolls (the .phy key parser
	// and a collide's box).
	return VTableHook::MemberOf( &IPhysicsCollision::ReadStat ).slot + 1;
}

#ifdef CLIENT_DLL
int PhysicsEntries()
{
	// DestroyAllCollisionSets is IPhysics' last declaration, over IAppSystem's
	// slots. Served on the client for the managed ragdolls (plan
	// physics_bones_native_zero Phase 3): a model's collision set.
	return VTableHook::MemberOf( &IPhysics::DestroyAllCollisionSets ).slot + 1;
}
#endif

#ifndef CLIENT_DLL
int StaticPropMgrServerEntries()
{
	// GetAllStaticPropsInOBB is IStaticPropMgrServer's last declaration, over
	// IStaticPropMgr's. No virtual destructor. Served for
	// plan_physics_bones_native_zero 2.6: managed's world build makes the
	// static props' physics (CreateVPhysicsRepresentations), as stock's
	// PhysCreateWorld did.
	return VTableHook::MemberOf( &IStaticPropMgrServer::GetAllStaticPropsInOBB ).slot + 1;
}
#endif

int SoundEmitterEntries()
{
	// Flush is ISoundEmitterSystemBase's last declaration; IAppSystem's slots
	// come first and are inside the number. Served for plan_server_novalve.md
	// P3: C#'s model components ask it which script sounds exist; and on the
	// client for plan_physics_bones_native_zero 2.6, whose surface sound
	// precache writes each script's index back into vphysics' surface table.
	return VTableHook::MemberOf( &ISoundEmitterSystemBase::Flush ).slot + 1;
}

int BaseFileSystemEntries()
{
	// UnzipFile is IBaseFileSystem's last declaration. Numbered against
	// IBaseFileSystem's OWN table -- a pointer-to-member of a base is numbered
	// from the base, which is what the secondary vtable at this+8 holds.
	return VTableHook::MemberOf( &IBaseFileSystem::UnzipFile ).slot + 1;
}

#ifdef CLIENT_DLL
int EngineClientEntries()
{
	// SetRichPresenceConnect is IVEngineClient's last declaration, and
	// IVEngineClient derives from IVEngineClient013 by SINGLE inheritance -- so
	// its slots CONTINUE the base's numbering and this one number is the length
	// of the whole chain. With both conditionals in (checked above) it is 146.
	return VTableHook::MemberOf( &IVEngineClient::SetRichPresenceConnect ).slot + 1;
}

int InputSystemEntries()
{
	// SetSkipControllerInitialization is IInputSystem's last declaration, over
	// IAppSystem's slots. Served for the UI's key bindings (plan_client_ui §6):
	// ButtonCodeToString names a captured button.
	return VTableHook::MemberOf( &IInputSystem::SetSkipControllerInitialization ).slot + 1;
}
#else
int EngineServerEntries()
{
	// SetPausedForced is IVEngineServer's last declaration. eiface.h's class
	// body holds no preprocessor directive at all, so nothing here is
	// conditional and this number is the same under every compilation.
	return VTableHook::MemberOf( &IVEngineServer::SetPausedForced ).slot + 1;
}
#endif

// INetworkStringTableContainer. SetAllowClientSideAddString is its last
// declaration; the virtual destructor takes slots 0 and 1. C# acquires it to
// FindTable the engine's own tables (the message of the day's InfoPanel).
int NetworkStringTableContainerEntries()
{
	return VTableHook::MemberOf( &INetworkStringTableContainer::SetAllowClientSideAddString ).slot + 1;
}

//-----------------------------------------------------------------------------
// THE PRECOMPILED COUNT FOR ONE VERSION STRING, AND IT IS ALSO THE SERVED SET.
//
// Zero means "this side does not serve that name", which is a real answer
// rather than a missing one: EngineClientEntries only COMPILES on the client,
// so the server cannot claim a length for IVEngineClient and does not.
//
// SERVING IS DECIDED HERE RATHER THAN BY ASKING A FACTORY, and the difference
// is visible on a listen server: engine.so registers VEngineClient014 and would
// happily answer server.so for it. The object behind that name is the CLIENT's
// engine interface with the client's own state, and handing it to server-side
// managed code because a factory would answer is not a binding anybody asked
// for. What this side was BUILT to serve is what it serves.
//-----------------------------------------------------------------------------
int SourceSharpEntriesFor( const char *pszVersion )
{
	if ( !pszVersion )
		return 0;

	if ( !ss::StrICmp( pszVersion, "VEngineRandom001" ) )
		return UniformRandomStreamEntries();

	// BOTH MODEL-INFO NAMES ARE CLAIMED ON BOTH SIDES, and the claim is about the
	// TABLE LENGTH rather than about the object. ivmodelinfo.h declares one
	// IVModelInfo and the engine registers two objects of that class under two
	// names, so the length is the same either way -- and WHICH object comes back
	// is now the factory's answer rather than this table's.
	if ( !ss::StrICmp( pszVersion, "VModelInfoServer004" )
	     || !ss::StrICmp( pszVersion, "VModelInfoClient006" ) )
		return ModelInfoEntries();

	if ( !ss::StrICmp( pszVersion, "VBaseFileSystem011" ) )
		return BaseFileSystemEntries();

	if ( !ss::StrICmp( pszVersion, "VPhysicsSurfaceProps001" ) )
		return PhysicsSurfacePropsEntries();

	if ( !ss::StrICmp( pszVersion, VPHYSICS_COLLISION_INTERFACE_VERSION ) )
		return PhysicsCollisionEntries();

	if ( !ss::StrICmp( pszVersion, SOUNDEMITTERSYSTEM_INTERFACE_VERSION ) )
		return SoundEmitterEntries();

#ifdef CLIENT_DLL
	if ( !ss::StrICmp( pszVersion, "VEngineClient014" ) )
		return EngineClientEntries();

	// The client's vphysics, from the physics factory its Init was handed.
	if ( !ss::StrICmp( pszVersion, VPHYSICS_INTERFACE_VERSION ) )
		return PhysicsEntries();

	if ( !ss::StrICmp( pszVersion, INPUTSYSTEM_INTERFACE_VERSION ) )
		return InputSystemEntries();
#else
	if ( !ss::StrICmp( pszVersion, "VEngineServer023" ) )
		return EngineServerEntries();

	if ( !ss::StrICmp( pszVersion, INTERFACEVERSION_STATICPROPMGR_SERVER ) )
		return StaticPropMgrServerEntries();
#endif

	// Each side's own container: engine.so registers both names on a listen
	// server, and the other side's tables are not this side's to write. (The
	// client's row sat inside the server's #else, so it never compiled:
	// ServedInterfaceTests reads each side's preprocessed body.)
#ifdef CLIENT_DLL
	if ( !ss::StrICmp( pszVersion, INTERFACENAME_NETWORKSTRINGTABLECLIENT ) )
		return NetworkStringTableContainerEntries();
#else
	if ( !ss::StrICmp( pszVersion, INTERFACENAME_NETWORKSTRINGTABLESERVER ) )
		return NetworkStringTableContainerEntries();
#endif

	return 0;
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
                                    int32_t *pnEntries, char *pError, int32_t nErrorBytes )
{
	if ( pnEntries )
		*pnEntries = 0;

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
		// EXEMPT FROM THE NOT-YET-AVAILABLE CASE, and it is the only kind that
		// is: these singletons are ours and exist from DLL load, so there is no
		// captured factory to be waiting for.
		//
		// AND IT ANSWERS ITS OWN COUNT. A shim area's table length is the
		// compiler's answer about a class in THIS tree, taken beside the
		// singleton it describes; the engine table below cannot know it and
		// asking it to would be a second place to keep in step.
		int32_t nShimEntries = 0;
		void *pShim = SourceSharp_InternalFactory( pszVersion, &nShimEntries );

		if ( !pShim || nShimEntries <= 0 )
		{
			SourceSharp_SayAcquireReason( pError, nErrorBytes,
			                              SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE );
			return NULL;
		}

		if ( pnEntries )
			*pnEntries = nShimEntries;

		return pShim;
	}

	// THE NAME IS RESOLVED BEFORE ANY ENGINE FACTORY IS ASKED, because the count
	// is what makes a pointer bindable at all: managed's one check is this
	// number against sizeof(TVtbl)/sizeof(void *), and a pointer with no number
	// is nothing a caller could do anything with. A name this side has no count
	// for is a name this side does not serve.
	int nEntries = SourceSharpEntriesFor( pszVersion );

	if ( nEntries <= 0 )
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

	if ( pnEntries )
		*pnEntries = nEntries;

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
