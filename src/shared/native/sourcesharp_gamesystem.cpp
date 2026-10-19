//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — Source <-> .NET bridge game system ==================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// Compiled into BOTH client.so and server.so. The two halves are identical
// apart from which side they load and which per-frame hook they use.
//
// This file is the ONLY place Source and SourceSharp meet: libsourcesharp
// itself has no Source dependency, which keeps it testable under the fake
// engine harness (src/sourcesharp/test).
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#include "igamesystem.h"
#else
// The server half is on public/ headers and server.so's own: the engine's
// globals and server interface (connected in shell_engineboundary.cpp), and
// the gravity answer movevars_shared.cpp defines.
#include "edict.h"
#include "eiface.h"
extern CGlobalVars *gpGlobals;
extern IVEngineServer *engine;
float GetCurrentGravity( void );
#endif
#include "tier1/convar.h"
#include "tier1/utlvector.h"

#include "ishim_root.h"
#include "imanaged_root.h"
#include "imanaged_areas.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE2/CE7: IManagedCommandsClient, IManagedSaveRestoreClient
#include "shell_shim_console.h"
#include "sourcesharp.h"
// The interfaces this file's client arm implements against: IClientRuntimeAdapter
// and SourceSharp_SetClientRuntime. It declares interfaces and nothing else, and
// the server arm has always reached them through managed_runtime_server.h
// instead -- which is why this file did not include it before.
#include "sourcesharp_gamesystem.h"
// THE ENTRY-POINT PROTOTYPES, GENERATED. 483 hand-written lines stood at the
// top of this file, one per slot whose body is in some other shim file, and
// every one was a second spelling of a signature SourceSharpEngineApi already
// declares -- checked against nothing. tools/entrypoint-decls derives them
// from the header and from the fill block below, which is what decides which
// side each is declared on. plan_shim.md §5 3e; the prose that introduced them
// is in docs/engine-boundary.md, keyed by slot.
#include "sourcesharp_entrypoints.h"
#ifdef CLIENT_DLL
#include "shell_convars_client.h"
#endif
#include "shell_vtable_interfaces.h"
#include "shell_diagnostics.h"
#include "managed_init_failure.h"
#ifdef CLIENT_DLL
#include "movevars_shared.h"
#endif
#include "native_command_body.h"
#include "convar_ownership.h"
#include "ss_string.h"
#include "console_registry.h"
#include "console_display_forward.h"
#include <string>

#include <vector>

#ifndef CLIENT_DLL
	#include "shell_server.h"
#endif

//-----------------------------------------------------------------------------
// THE FOUR ENTRY POINTS THAT ARE NOT SLOTS.
//
// Every other prototype this file needs is in sourcesharp_entrypoints.h, which
// tools/entrypoint-decls derives from SourceSharpEngineApi's field signatures
// and from the fill block below. These four have no field to derive a signature
// from, because nothing calls them through the engine API -- the fill calls
// them directly, at level shutdown and at the end of a frame.
//
// SERVER ONLY, and the guard is the same one the generated header applies to
// everything the fill wires inside `#ifndef CLIENT_DLL`: there is no entity
// slot table, no constraint list and no motion controller list on the client.
//
// plan_shim.md §5 3e. See docs/engine-boundary.md for the paragraph each of
// these used to carry; what is left here is the fact a reader of this file
// needs, which is which file the body is in.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
	// server/shell_entityslots.cpp. The entity control block's queue
	// (plan_nettable_generic.md Part 9): Drain reports how many slots changed
	// since the last frame and Reset empties the table at level shutdown.
	int  SourceSharp_DrainEntitySlots( void );
	void SourceSharp_ResetEntitySlots( void );

#endif

#ifdef CLIENT_DLL
// TWO FORWARD DECLARATIONS, AND THEY ARE IN THIS BLOCK ON PURPOSE.
//
// Not prototypes: the two parameter structs the client's fog and skybox slots
// take by pointer. The generated header includes sourcesharp.h, which declares
// both -- these are here because the fill block below names the types before
// that include would matter, and a forward declaration costs nothing.
//
// IN THE CLIENT'S BLOCK AND NOT THE SERVER'S, which is not a style choice and
// cost one build to learn: everything inside the long `#ifndef CLIENT_DLL`
// arms is compiled away on this side, so a client declaration written in one is
// a client declaration that does not exist. The server's SetSkyboxParams is a
// different function with a different signature.
struct SourceSharpFogParams;
struct SourceSharpSkyboxParams;
#endif

// AND ONE THAT IS NOT AN ENTRY POINT AT ALL. Defined in
// shared/shell_movement.cpp and called from the convar change callback below
// rather than from the ABI: it is the observer plan_shim.md 4a item 16 asks
// for, not a slot. docs/defect-convar-order.md.

//-----------------------------------------------------------------------------
// AND TWO SLOT BODIES DEFINED HERE RATHER THAN ELSEWHERE, which is why the
// generator emits no prototype for either: they are `static`, they are two
// lines each, and both are about gpGlobals -- the one thing this file has that
// no shell file does.
//
// Shared: both sides have gpGlobals, each pointing at its own side's globals.
//-----------------------------------------------------------------------------
static float SourceSharp_GetCurrentTime( void )
{
	return gpGlobals ? gpGlobals->curtime : 0.0f;
}

// plan_crossings.md Phase X1: the ADDRESS of the engine's CGlobalVars, so the
// managed side can READ the struct instead of calling across for one float of
// it. Shared for the getter's reason, and the managed side checks m_bClient
// against the side it asked from before it trusts one.
//
// NULL BEFORE THE ENGINE HAS SET gpGlobals is a real answer, not an error: the
// managed side treats it as "not yet" and asks again, so an early call cannot
// bind zero permanently.
static void *SourceSharp_GetGlobalVarsPtr( void )
{
	return (void *)gpGlobals;
}
#ifndef CLIENT_DLL
#ifndef CLIENT_DLL
#endif
#ifndef CLIENT_DLL
#endif
#endif

#ifndef CLIENT_DLL
// Temp entities (plan_tmp.md Part 3), implemented in server/shell_tempents.cpp.
// It builds a SendTable and a ServerClass from a description at DLL init and
// sends a blob through engine->PlaybackTempEntity. Init wires the base-table
// resolver BEFORE anything can be refused for the lack of one; Close shuts the
// window that the engine's single walk of g_pServerClassHead defines.
//
// Server only: the client half of the same design is C-Phase 1 and lands in
// shell_tempents_client.cpp with its own slot.
int32_t SourceSharp_TempEntRegister( const SourceSharpTeClass_t *pDesc );
void    SourceSharp_TempEntInit( void );
void    SourceSharp_TempEntCloseRegistration( void );

#endif

#ifdef CLIENT_DLL
// Temp entities, client half (plan_tmp.md Part 3), implemented in
// client/shell_tempents_client.cpp. The SAME ABI slot the server fills, with
// the other implementation behind it: this one builds a RecvTable and a
// ClientClass from the same description, and hands the decoded blob to managed
// code through OnTempEntity.
//
// THE WINDOW IS CShellClientDLL::Init()'s, which calls
// LoadManagedRuntime( this ) deliberately ahead of gHUD.Init(), and
// deliberately not from a second CAutoGameSystem, because ordering between game
// systems is link order (plan 2D). It was SourceSharp_ClientEarlyInit()'s
// window until 5b-runtime gave the load a named owner; the window is the same
// one and is still the reason this registration lands where it does.
void    SourceSharp_TempEntClientInit( void );
void    SourceSharp_TempEntClientCloseRegistration( void );
#endif

//-----------------------------------------------------------------------------
// GetCurrentGravity(), which Frame multiplies by frametime every tick.
//
// WIRED ON BOTH SIDES, though only the client's tempent physics asks for it
// today. GetCurrentGravity lives in movevars_shared.h and answers on both, and
// a slot wired on one side only would answer the managed fallback on the other
// -- a plausible number rather than a refusal, which is the exact hazard
// SOURCESHARP_TRACE_ALL's note describes for the trace masks.
//-----------------------------------------------------------------------------
static float SourceSharp_CurrentGravity( void )
{
	return GetCurrentGravity();
}


// Implemented in shared/shell_gameevents.cpp. Shared: both sides see events,
// the server the authoritative ones and the client the networked copies.
void        SourceSharp_StopListeningForGameEvents( void );

// Each side's own host exports (plan_side_entries SE1): no side travels as an
// argument, so this file names the side's functions once, here.
#ifdef CLIENT_DLL
	#include "cdll_client_int.h"
	#define SOURCESHARP_SIDE_TAG "client"
	#define SourceSharp_LoadMySide_Host   SourceSharp_LoadClient
	#define SourceSharp_UnloadMySide      SourceSharp_UnloadClient
	#define SourceSharp_ReloadCheckMySide SourceSharp_ReloadCheckClient
#else
	#include "eiface.h"
	#define SOURCESHARP_SIDE_TAG "server"
	#define SourceSharp_LoadMySide_Host   SourceSharp_LoadServer
	#define SourceSharp_UnloadMySide      SourceSharp_UnloadServer
	#define SourceSharp_ReloadCheckMySide SourceSharp_ReloadCheckServer
#endif


static bool g_bSourceSharpLoaded = false;

//-----------------------------------------------------------------------------
// The engine api table, kept rather than built on the stack.
//
// It was a local of Init, which was right while a side was loaded exactly once.
// A hot reload loads a second time (plan_nettable_generic.md Step 10) and needs
// the same table to hand over — and rebuilding it at the reload site would be a
// second list of a hundred and fifty assignments that has to be kept in step
// with this one by hand, which is the shape of drift this tree keeps paying
// for. Init fills THIS, and the reload passes the same pointer again.
//
// Its contents are all native function addresses, so they are as valid after a
// managed unload as before one: nothing in here points into an ALC.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
#endif

//-----------------------------------------------------------------------------
// THE STRUCT'S WAY INTO IShimConsole (plan_engineapi_vtables A4). The ten
// console slots' bodies are CShimConsole's methods now; the struct, which the
// client still reads, reaches them through these until the client's wave
// deletes the struct and these with it. Client-only: the server has no struct.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
static void SourceSharp_ConsoleMsg( const char *p ) { ShimConsole().Msg( p ); }
static void SourceSharp_ConsoleWarning( const char *p ) { ShimConsole().Warning( p ); }
static int32_t SourceSharp_ConsoleRegisterConCommand( const char *n, const char *h, int32_t f )
	{ return ShimConsole().RegisterConCommand( n, h, f ); }
static int32_t SourceSharp_ConsoleRegisterConVar( const char *n, const char *d, const char *h, int32_t f,
                                                  int32_t bMin, float flMin, int32_t bMax, float flMax )
	{ return ShimConsole().RegisterConVar( n, d, h, f, bMin, flMin, bMax, flMax ); }
static void SourceSharp_ConsoleSetConVarValue( int32_t id, const char *v ) { ShimConsole().SetConVarValue( id, v ); }
static void SourceSharp_ConsoleGetConVarValue( int32_t id, char *o, int32_t n ) { ShimConsole().GetConVarValue( id, o, n ); }
static int32_t SourceSharp_ConsoleFindConVarValue( const char *n, char *o, int32_t s )
	{ return ShimConsole().FindConVarValue( n, o, s ); }
static void SourceSharp_ConsoleSetNamedConVarValue( const char *n, const char *v ) { ShimConsole().SetNamedConVarValue( n, v ); }
static void SourceSharp_ConsoleRevertNamedConVar( const char *n ) { ShimConsole().RevertNamedConVar( n ); }
static int32_t SourceSharp_ConsoleConsoleSnapshot() { return ShimConsole().ConsoleSnapshot(); }
static int32_t SourceSharp_ConsoleConsoleSnapshotEntry( int32_t i, SourceSharpConsoleEntry *o )
	{ return ShimConsole().ConsoleSnapshotEntry( i, o ); }
#endif

//-----------------------------------------------------------------------------
// ONE LOAD, FOR THE FIRST LOAD AND THE RELOAD ALIKE -- plan_engineapi_vtables
// step A3. Each side hands managed its IShimRoot through LoadSideV2, beside the
// root's forwarder table (plan_forwarders_everywhere.md), which managed calls it
// through. Both call sites go through here so they cannot drift.
//-----------------------------------------------------------------------------
static int SourceSharp_LoadMySide()
{
	const void *pRootTable = NULL;
	IShimRoot *pRoot = ShimRoot_Get( &pRootTable );

	// D4, and plan_client_vtables CD for the client: the managed root comes back
	// beside the shim root, and is kept only if its Validate agrees with the
	// compiler's slot hashes -- refused loudly otherwise. Neither side takes a
	// ManagedApi table (the server since E-last, the client since CE-last).
	void *pManagedRoot = NULL;
	const int rc = SourceSharp_LoadMySide_Host( pRoot, pRootTable, &pManagedRoot );
	if ( rc != SOURCESHARP_OK )
		return rc;

	char szWhy[ 160 ];

	// plan_managed_vtable_reload.md L4: a reload. Native acquired once and kept
	// every object; the new build patched the same blocks in place.
	if ( ManagedRoot_Get() )
	{
		if ( !ManagedRoot_Reattached( pManagedRoot, szWhy, sizeof( szWhy ) ) )
		{
			Warning( "[SourceSharp] %s: the reloaded managed side was refused: %s\n", SOURCESHARP_SIDE_TAG, szWhy );
			return SOURCESHARP_ERR_ABI_MISMATCH;
		}

		Msg( "[SourceSharp] %s: managed root re-attached in place (%d slots, validated); every area validated\n",
		     SOURCESHARP_SIDE_TAG, (int)kIManagedRootSlotCount );
		return rc;
	}

	if ( !ManagedRoot_Accept( pManagedRoot, szWhy, sizeof( szWhy ) ) )
	{
		Warning( "[SourceSharp] %s: managed root refused: %s\n", SOURCESHARP_SIDE_TAG, szWhy );
		return SOURCESHARP_ERR_ABI_MISMATCH;
	}

	if ( !ManagedRoot_AcquireAreas( szWhy, sizeof( szWhy ) ) )
	{
		Warning( "[SourceSharp] %s: a managed-built area was refused: %s\n", SOURCESHARP_SIDE_TAG, szWhy );
		return SOURCESHARP_ERR_ABI_MISMATCH;
	}

	// The engine's console prints, forwarded to the area acquired above
	// (docs/todo.md's console-capture item). Installed here — the one point
	// both sides have ICvar connected AND a load's areas — and removed by
	// SourceSharp_UnloadManagedSide below BEFORE the areas go, so the engine
	// never holds a pointer to a forwarder whose managed object is gone. A
	// reload keeps the installation: the forwarder reads its area per line.
	if ( !ConsoleDisplay_Install( g_pCVar ) )
	{
		Warning( "[SourceSharp] %s: the console display func was not installed "
		         "(no ICvar); engine prints will not reach managed code\n",
		         SOURCESHARP_SIDE_TAG );
	}

	Msg( "[SourceSharp] %s: managed root accepted (%d slots, validated), runtime acquired\n",
	     SOURCESHARP_SIDE_TAG, (int)kIManagedRootSlotCount );
	return rc;
}

//-----------------------------------------------------------------------------
// A RELOAD IS QUEUED, NOT DONE WHERE IT IS ASKED FOR.
//
// This is not tidiness and it is not about re-entrancy in the ordinary sense.
// A collectible load context unloads only when nothing anywhere references it,
// and "anywhere" includes the STACK OF EVERY THREAD — so a reload triggered
// from inside a call that is itself running the side's own code cannot possibly
// unload that side. It leaks it, silently, and keeps running the old build.
//
// The dev API is the case that made this concrete rather than theoretical. Its
// listener runs on a thread of its own, in an assembly loaded into the side's
// context, and a request is served by handing work to the game thread and
// waiting for it. So `./debug-api cmd cs_sv_reload` reaches the command with the
// dev API's own frames on another thread's stack, blocked on the very frame
// that is trying to unload them — the join in HttpSocketServer.Dispose times
// out, the context is pinned, and the reload appears to work while leaking the
// build it replaced. Measured, before this: "the accept loop did not stop"
// followed by "ServerALC did NOT unload".
//
// A frame boundary is the place with no managed frame on any stack: the console
// command returns, the dev API request completes, its thread goes back to
// blocking in accept, and the reload happens at the top of the next frame where
// Dispose can close the socket and join.
//-----------------------------------------------------------------------------
static bool g_bReloadRequested = false;

static void SourceSharp_ReloadManagedSide( void );

// NOT static: the shell game DLL's GameFrame is the server's service point
// from Phase 4 (plan_gamedll.md Part 3b point 1), and it is declared in
// sourcesharp_gamesystem.h -- the one header the shell includes.
void SourceSharp_ServiceReloadRequest( void )
{
	if ( !g_bReloadRequested )
		return;

	g_bReloadRequested = false;
	SourceSharp_ReloadManagedSide();
}

// Whether Init has already run TO COMPLETION. Separate from
// g_bSourceSharpLoaded, which says whether the managed side is currently up: a
// hot reload drops that one and back without reopening this gate.
//
// A gate rather than a bare bool because WHEN it closes is the whole point. It
// used to be a bool set on entry to Init(), which meant a failed attempt still
// marked the system initialised — see managed_init_failure.h.
static CSourceSharpInitGate g_SourceSharpInitGate;


//-----------------------------------------------------------------------------
// Engine functions exposed to managed code.
//-----------------------------------------------------------------------------
// The host has no Source dependency, so it logs through this.
static void SourceSharp_HostLog( const char *pMsg )
{
	Msg( "%s", pMsg ? pMsg : "" );
}

void CShimConsole::Msg( const char *pMsg )
{
	// "%s" rather than passing pMsg as the format — managed strings may contain '%'.
	//
	// Reaching the terminal as well as the console is the spew hook's job now,
	// in shell_diagnostics: it catches every caller rather than each one
	// remembering to echo itself, and echoing here as well printed twice.
	::Msg( "%s", pMsg ? pMsg : "" );
}

void CShimConsole::Warning( const char *pMsg )
{
	::Warning( "%s", pMsg ? pMsg : "" );
}

//-----------------------------------------------------------------------------
// Strings that arrived from C# are COPIED into the object that registers them.
//
// tier1 keeps the pointer it is given and never copies it -- "Name should be
// static data" (convar.cpp:161) -- while every string crossing the ABI points
// into a managed array pinned only for the duration of the call. So the engine
// held a name that was garbage collected out from under it the moment
// registration returned.
//
// What that looked like: cs_hello registered, listed by the API, and dead. No
// error, no "unknown command" -- the engine simply had nothing under that name
// any more, so it dispatched nothing. Convar writes went the same way, which is
// why a C# convar could be read but never set.
//
// Each carrier below owns its copies as std::string members declared BEFORE
// the ConCommand/ConVar that is handed their pointers, and the carriers live
// until SourceSharp_ReleaseConsoleObjects -- after the engine has let go of
// them all (UnregisterConCommands, in DLLShutdown). Until P4.9 they were
// "never freed, deliberately" and every one read as live at unload.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ConCommand bridge.
//
// Native owns these objects for the process lifetime and only the managed
// handler is rebound across a reload — UnregisterConCommands is all-or-nothing
// per DLL (tier1/convar.cpp:93). See plan.md Part 5, rule 2.
//-----------------------------------------------------------------------------
// The server's SS_NATIVE_COMMAND bodies (native_command_body.h). The client
// defines none, so it does not ask.
static const CNativeCommandBody *NativeBodyFor( const char *pName )
{
#ifdef CLIENT_DLL
	(void)pName;
	return NULL;
#else
	return CNativeCommandBody::Find( pName );
#endif
}

#if defined( _MSC_VER )
// 'this' in the initialiser list below is deliberate. The build disables C4355
// (/wd4355, as Valve's VPC does), but tier1/convar.h ends with
// `#pragma warning( default : 4355 )`, which turns it back on for the rest of
// any file that includes it.
#pragma warning( disable : 4355 )
#endif
class CSourceSharpCommand final : public ICommandCallback
{
public:
	CSourceSharpCommand( int nId, const char *pName, const char *pHelp, int nFlags )
		: m_nId( nId ),
		  m_Name( pName ? pName : "" ),
		  m_Help( pHelp ? pHelp : "" ),
		  m_Command( m_Name.c_str(), this, m_Help.c_str(), nFlags ),
		  m_pNativeBody( NativeBodyFor( m_Name.c_str() ) )
	{
	}

	virtual void CommandCallback( const CCommand &command ) OVERRIDE
	{
		// A COMMAND C# DECLARES AND NATIVE CODE ANSWERS -- plan_server_novalve.md
		// P2.3c, native_command_body.h. Run here, on the engine's own CCommand
		// and call stack, with no crossing: the body reads native state, and a
		// managed frame between the engine and it would only add a round trip
		// (and, for cs_sv_reload, a frame in the assembly being unloaded).
		if ( m_pNativeBody )
		{
			m_pNativeBody->Run( command );
			return;
		}

		// The side's managed-built area: E15 on the server, plan_client_vtables
		// CE2 on the client.
#ifndef CLIENT_DLL
		IManagedCommands *pCommands = g_bSourceSharpLoaded ? ManagedCommands() : NULL;
#else
		IManagedCommandsClient *pCommands = g_bSourceSharpLoaded ? ManagedCommandsClient() : NULL;
#endif
		if ( !pCommands )
		{
			Warning( "[SourceSharp] '%s' ignored — managed runtime not loaded\n", command.Arg( 0 ) );
			return;
		}

		if ( SourceSharp_GetVerbosity() > 1 )
			Msg( "[SourceSharp] dispatch cmd id=%d '%s'\n", m_nId, command.ArgS() );

		pCommands->ConCommandDispatch( m_nId, command.ArgS() );
	}

	int GetId() const { return m_nId; }

	// The name this was registered under, for the reload path below: a second
	// registration of the same name must find THIS object rather than make
	// another, because tier1 refuses the duplicate ConCommand and the managed
	// side would then hold an id nothing dispatches.
	const char *Name() const { return m_Name.c_str(); }

private:
	// Declared before the command, because that is the order they are built in
	// and the command is handed these pointers.
	int			m_nId;
	std::string m_Name;
	std::string m_Help;
	ConCommand	m_Command;
	const CNativeCommandBody *m_pNativeBody;
};

// By id and by name (case-blind): console_registry.h.
static CConsoleRegistry< CSourceSharpCommand > g_SourceSharpCommands;

//-----------------------------------------------------------------------------
// ConVar bridge.
//
// Same ownership rule as ConCommands: native owns the ConVar objects for the
// process lifetime, because tier1 only offers per-DLL bulk unregistration
// (convar.cpp:93). C# holds a cached value refreshed by the change callback.
//-----------------------------------------------------------------------------
class CSourceSharpConVar;
static CConsoleRegistry< CSourceSharpConVar > g_SourceSharpConVars;

static void SourceSharp_OnConVarChanged( IConVar *pVar, const char *pOldValue, float flOldValue );

class CSourceSharpConVar final
{
public:
	CSourceSharpConVar( int nId, const char *pName, const char *pDefault, const char *pHelp,
	                    int nFlags, bool bHasMin, float flMin, bool bHasMax, float flMax )
		: m_nId( nId ),
		  m_Name( pName ? pName : "" ),
		  m_Default( pDefault ? pDefault : "" ),
		  m_Help( pHelp ? pHelp : "" ),
		  // The overload that takes bounds and NO callback. Declaring one without
		  // bounds passes false for both, which is what every other convar in
		  // the game does and means exactly "unclamped".
		  //
		  // NO PER-CONVAR CALLBACK, DELIBERATELY -- see the long comment on
		  // SourceSharp_OnConVarChanged below. A per-ConVar callback cannot
		  // survive this name being registered by anyone else first, which for
		  // six of our knobs the ENGINE already does.
		  m_ConVar( m_Name.c_str(), m_Default.c_str(), nFlags, m_Help.c_str(),
		            bHasMin, flMin, bHasMax, flMax )
	{
	}

	int      GetId() const   { return m_nId; }
	ConVar  *GetVar()        { return &m_ConVar; }

	// BY NAME, never by pointer. `m_ConVar` is not the object the engine hands
	// a change callback when someone else registered this name first: tier1
	// makes ours a CHILD and fires on the PARENT. The name is the only identity
	// that survives that, and it is what the engine matched on to link them.
	bool     IsNamed( const char *pName ) const
	{
		return pName != NULL && ss::StrICmp( m_Name.c_str(), pName ) == 0;
	}

private:
	// Before the convar, for the same reason as the command above: it is handed
	// these and keeps them.
	int         m_nId;
	std::string m_Name;
	std::string m_Default;
	std::string m_Help;
	ConVar      m_ConVar;
};

//-----------------------------------------------------------------------------
// The C#-declared commands and convars, deleted with their names -- ONLY after
// the engine has let go of them all: server.so calls this from DLLShutdown
// right after ConVar_Unregister (UnregisterConCommands for this DLL). Before
// that, deleting one would leave the engine's ICvar pointing at freed memory.
//-----------------------------------------------------------------------------
void SourceSharp_ReleaseConsoleObjects()
{
	g_SourceSharpCommands.DeleteAll();
	g_SourceSharpConVars.DeleteAll();
}

//-----------------------------------------------------------------------------
// The change callback, installed ONCE per DLL as a GLOBAL callback and matching
// BY NAME.
//
// Both of those are corrections to what this was, and both are forced by what
// tier1 does when a convar name is registered twice. `CCvar::RegisterConCommand`
// (vstdlib) makes the second registration a CHILD of the first:
//
//     pChildVar->m_pParent = pParentVar->m_pParent;
//     if ( pChildVar->m_fnChangeCallback ) {
//         if ( !pParentVar->m_fnChangeCallback )
//             pParentVar->m_fnChangeCallback = pChildVar->m_fnChangeCallback;
//         else
//             Warning( "Convar %s has multiple different change callbacks\n" );
//     }
//
// So a per-ConVar callback on a name someone else owns has TWO ways to fail and
// only one of them looks like a bug:
//
//   * the parent has no callback -- ours is slammed onto the PARENT and fires
//     with `pVar` pointing at the parent's object, never at our `m_ConVar`. A
//     pointer comparison misses it.
//   * the parent already has one -- ours is DROPPED with a warning and is never
//     called at all. A pointer comparison is not even reached, so no amount of
//     fixing the match would have helped.
//
// `ConVar::ChangeStringValue` (tier1/convar.cpp) then calls
// `g_pCVar->CallGlobalChangeCallbacks( this, ... )` UNCONDITIONALLY, and the
// engine keeps those in a vector rather than one slot -- so a global callback
// is called for every change, once per installer, in every DLL that installed
// one. That covers both failures with one mechanism.
//
// `this` is the PARENT in both paths, so the object we are handed is not ours
// whenever anyone else registered the name first. Six of the knobs this mod
// declares are in that position because the ENGINE declares them too:
// violence_hblood, violence_hgibs, violence_ablood, violence_agibs,
// sv_alternateticks and sv_unlockedchapters, all present in bin/linux64/
// engine.so. Their values were correct in the engine and STALE in managed code
// for exactly this reason.
//
// Reading the value off OUR object is still right after a name match: every
// ConVar accessor goes through `m_pParent`, so a child reports the parent's
// value.
//-----------------------------------------------------------------------------
static void SourceSharp_OnConVarChanged( IConVar *pVar, const char *pOldValue, float flOldValue )
{
	if ( !pVar )
		return;

	// ABOVE THE GUARD BELOW, DELIBERATELY, and that placement is the whole
	// point of it. plan_shim.md 4a item 16 -- sv_gravity reading its declared
	// 800 instead of this mod's 600 -- is a question about the window between
	// managed startup's write and the first frame, and the early-out on the
	// next line is exactly what made that window unobservable: the managed side
	// is not "loaded" for part of it, so every change in it was dropped here
	// without a line. See shell_movement.cpp and docs/defect-convar-order.md.
	// The side's managed-built area: E15 on the server, plan_client_vtables
	// CE2 on the client.
#ifndef CLIENT_DLL
	IManagedCommands *pCommands = g_bSourceSharpLoaded ? ManagedCommands() : NULL;
#else
	IManagedCommandsClient *pCommands = g_bSourceSharpLoaded ? ManagedCommandsClient() : NULL;
#endif
	if ( !pCommands )
		return;

	// The global callback sees EVERY convar in the process, so most calls are
	// about a name we never declared. Matching on the name is the whole filter.
	const char *pName = pVar->GetName();

	// By name, case-blind -- IsNamed's own compare (StrICmp). A name registers
	// once in any case (a second registration answers the first id), so this is
	// the one carrier the walk's first IsNamed match was; IsNamed confirms it.
	CSourceSharpConVar *pConVar = g_SourceSharpConVars.ByName( pName );
	int32_t nId = -1;
	const char *pNewValue = "";

	if ( pConVar && pConVar->IsNamed( pName ) )
	{
		nId = pConVar->GetId();
		pNewValue = pConVar->GetVar()->GetString();
	}
#ifdef CLIENT_DLL
	else
	{
		// A WATCHED name (IShimConVarsClient, plan_client_novalve C8): the
		// engine's convars, and native ones managed decides for, forwarded under
		// the id the watch answered. The value is the convar's own string.
		nId = SourceSharp_ClientWatchedConVarId( pName );
		ConVar *pWatched = ( nId >= 0 && g_pCVar ) ? g_pCVar->FindVar( pName ) : NULL;
		pNewValue = pWatched ? pWatched->GetString() : "";
	}
#endif

	if ( nId < 0 )
		return;

	if ( SourceSharp_GetVerbosity() > 1 )
		Msg( "[SourceSharp] convar '%s' changed -> '%s'\n", pName, pNewValue );

	pCommands->ConVarChanged( nId, pNewValue, pOldValue ? pOldValue : "" );
}

//-----------------------------------------------------------------------------
// Install the change callback, once per DLL, on the FIRST convar registration.
//
// Once, because `CCvar::InstallGlobalChangeCallback` asserts the callback is not
// already in its vector and would otherwise call us twice per change. Per DLL
// rather than per convar, because the callback is a property of this module's
// bridge, not of any one convar -- and because a global callback is the only
// kind that survives a name the engine already owns (see the callback's comment).
//
// Never removed, matching the ownership rule the objects above already follow:
// this DLL is not unloaded, and `g_pCVar` outlives it either way. It is
// installed on first registration rather than at connect because that is the
// first moment we know `g_pCVar` is live and we have something to match against.
//-----------------------------------------------------------------------------
static bool g_bSourceSharpConVarCallbackInstalled = false;

static void SourceSharp_InstallConVarChangeCallback()
{
	if ( g_bSourceSharpConVarCallbackInstalled || !g_pCVar )
		return;

	g_pCVar->InstallGlobalChangeCallback( SourceSharp_OnConVarChanged );
	g_bSourceSharpConVarCallbackInstalled = true;

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] convar change callback installed (global, matched by name)\n" );
}

#ifdef CLIENT_DLL
// A watch can come before any registration (shell_convars_client.cpp).
void SourceSharp_EnsureConVarChangeCallback()
{
	SourceSharp_InstallConVarChangeCallback();
}
#endif

//-----------------------------------------------------------------------------
// The same rule as the command above, and the convar has a second reason for it.
//
// A duplicate ConVar object would be refused by tier1 exactly as a duplicate
// command is, leaving the new managed side holding an id nothing answers. And
// even if it were not refused, re-creating one would reset it to its DEFAULT —
// so a reload would silently undo every convar the operator had set, which is
// the opposite of what a reload is for. The value lives in the surviving object
// and the reload rebinds to it.
//-----------------------------------------------------------------------------
int32_t CShimConsole::RegisterConVar( const char *pName, const char *pDefaultValue,
                                                 const char *pHelp, int32_t nFlags,
                                                 int32_t bHasMin, float flMin,
                                                 int32_t bHasMax, float flMax )
{
	if ( !pName || !pName[0] )
		return -1;

	SourceSharp_InstallConVarChangeCallback();

	if ( CSourceSharpConVar *pConVar = g_SourceSharpConVars.ByName( pName ) )
	{
		if ( SourceSharp_GetVerbosity() > 0 )
			::Msg( "[SourceSharp] C# re-registered convar '%s' -> existing id %d, value '%s'\n",
				pName, pConVar->GetId(),
				pConVar->GetVar()->GetString() );

		return pConVar->GetId();
	}

	// A NAME THE ENGINE OWNS IS REFUSED, NOT ALIASED (convar_ownership.h). The
	// engine's own variables are registered before either game DLL loads, so
	// the lookup sees every one of them. No carrier is built; the managed
	// registry turns this answer into a startup failure naming the class.
	if ( g_pCVar )
	{
		const ConVar *pExisting = g_pCVar->FindVar( pName );
		const int nOwnerFlags = !pExisting ? 0 :
			( pExisting->IsFlagSet( FCVAR_GAMEDLL ) ? FCVAR_GAMEDLL : 0 ) |
			( pExisting->IsFlagSet( FCVAR_CLIENTDLL ) ? FCVAR_CLIENTDLL : 0 );
		if ( pExisting && SourceSharp_ConVarOwnedByEngine( nOwnerFlags ) )
		{
			::Warning( "[SourceSharp] C# declared convar '%s', which the engine defines: refused. "
			         "Read the engine's by name instead of declaring it.\n", pName );
			return SOURCESHARP_CONVAR_ENGINE_OWNED;
		}
	}

	// THE DLL IDENTIFIER FLAG, WHICH tier1 CANNOT GIVE US.
	//
	// ConVar_Register ORs its nCVarFlag into every ConCommandBase on the static
	// construction list, calls Init on each, and then sets that list to NULL
	// (tier1/convar.cpp:70-80). A managed convar is built AFTER that has run, so
	// it is on no list and receives no flag -- it carries neither FCVAR_GAMEDLL
	// nor FCVAR_CLIENTDLL where every natively declared convar in this DLL
	// carries one.
	//
	// That is not cosmetic. AreConVarsLinkable refuses the replicated link when
	// the child is FCVAR_CLIENTDLL and the parent is not FCVAR_GAMEDLL, printing
	// "For FCVAR_REPLICATED, ConVar must be defined in client and game .dlls".
	// A managed server-side replicated declaration therefore becomes a parent
	// the client's native twin is refused, the twin's m_pParent is never
	// assigned, and it sits at its own declared default forever. Five convars
	// are in exactly that state on every run before this line existed --
	// GameVars.cs's five -- harmless only because nothing on the client link
	// line happens to read them.
	//
	// So we apply the flag ConVar_Register would have applied, and the SIDE
	// picks it: the server registers FCVAR_GAMEDLL (shell_gamedll.cpp:232) and
	// the client FCVAR_CLIENTDLL (shell_clientdll.cpp:991). Choosing it here
	// rather than in managed code is deliberate -- this file is compiled into
	// both DLLs, and a flag chosen on the managed side would be one value for
	// both sides, which is the shape of an earlier attempt that took the engine
	// warnings from 5 to 29.
#ifdef CLIENT_DLL
	nFlags |= FCVAR_CLIENTDLL;
#else
	nFlags |= FCVAR_GAMEDLL;
#endif

	const int nId = g_SourceSharpConVars.NextId();
	g_SourceSharpConVars.Add( pName,
		new CSourceSharpConVar( nId, pName, pDefaultValue ? pDefaultValue : "",
		                        pHelp ? pHelp : "", nFlags,
		                        bHasMin != 0, flMin, bHasMax != 0, flMax ) );

	if ( SourceSharp_GetVerbosity() > 0 )
		::Msg( "[SourceSharp] C# registered convar '%s' = '%s' (id %d)\n",
			pName, pDefaultValue ? pDefaultValue : "", nId );

	return nId;
}

void CShimConsole::SetConVarValue( int32_t nId, const char *pValue )
{
	CSourceSharpConVar *pConVar = g_SourceSharpConVars.ById( nId );

	if ( !pConVar || !pValue )
		return;

	pConVar->GetVar()->SetValue( pValue );
}

void CShimConsole::GetConVarValue( int32_t nId, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';
	CSourceSharpConVar *pConVar = g_SourceSharpConVars.ById( nId );

	if ( !pConVar )
		return;

	ss::StrCopy( pOut, pConVar->GetVar()->GetString(), nOutSize );
}

//-----------------------------------------------------------------------------
// A NAME REGISTERED TWICE IS THE SAME COMMAND, NOT A SECOND ONE.
//
// plan.md Part 5 rule 2 says native owns the ConCommand objects for the process
// lifetime and a reload rebinds only the managed handler. That was true of the
// OBJECTS and not of this function, which appended unconditionally — so the
// first managed reload made a second ConCommand of every name, tier1 refused
// each one (convar.cpp registers by name and keeps the first), and the new
// managed side was handed ids belonging to objects the engine had thrown away.
// Every C#-declared console command would have stopped working, silently,
// while the console still listed them.
//
// So the lookup comes first, and a re-registration hands back the id the
// surviving object already answers to. The help text is not updated: the
// ConCommand kept the pointer it was handed and there is no supported way to
// change it, and a stale help string is a cosmetic cost against a dead command.
//-----------------------------------------------------------------------------
int32_t CShimConsole::RegisterConCommand( const char *pName, const char *pHelp,
                                          int32_t nFlags )
{
	if ( !pName || !pName[0] )
		return -1;

	if ( CSourceSharpCommand *pCommand = g_SourceSharpCommands.ByName( pName ) )
	{
		if ( SourceSharp_GetVerbosity() > 0 )
			::Msg( "[SourceSharp] C# re-registered console command '%s' -> existing id %d\n",
				pName, pCommand->GetId() );

		return pCommand->GetId();
	}

	// THE DLL IDENTIFIER FLAG, WHICH tier1 CANNOT GIVE US.
	//
	// The same hole the convar registrar above fills, for the same mechanical
	// reason: ConVar_Register ORs its nCVarFlag into every ConCommandBase on
	// the static construction list and then sets that list to NULL
	// (tier1/convar.cpp:70-80), and a managed ConCommand is built long after
	// that has run. It is on no list, so it receives no flag and carries
	// neither FCVAR_GAMEDLL nor FCVAR_CLIENTDLL -- where every natively
	// declared command in this DLL carries one.
	//
	// What it costs is DIFFERENT for a command than for a convar, which is why
	// this comment is not a pointer at that one. A convar without the flag is
	// refused the replicated link by AreConVarsLinkable. A COMMAND without it
	// is still found and still run -- but the engine reads FCVAR_GAMEDLL off the
	// ConCommandBase it found to decide whether a line typed at the console is
	// the SERVER's command, and therefore whether to set the command client to
	// the listen server's local player before dispatching
	// (tier1/commandbuffer.cpp:559-585). A managed server command missing it is
	// dispatched with no command client set, so anything that asks who typed it
	// is answered -1 on a listen server.
	//
	// The SIDE picks the flag, natively, exactly as it does for a convar: this
	// file is compiled into both DLLs and a flag chosen on the managed side
	// would be one value for both.
#ifdef CLIENT_DLL
	nFlags |= FCVAR_CLIENTDLL;
#else
	nFlags |= FCVAR_GAMEDLL;
#endif

	const int nId = g_SourceSharpCommands.NextId();
	g_SourceSharpCommands.Add( pName,
		new CSourceSharpCommand( nId, pName, pHelp ? pHelp : "", nFlags ) );

	if ( SourceSharp_GetVerbosity() > 0 )
		::Msg( "[SourceSharp] C# registered console command '%s' (id %d, flags 0x%x)\n",
			pName, nId, nFlags );

	return nId;
}

//-----------------------------------------------------------------------------
// The engine's OWN console registry, walked so a debugging tool can show it.
//
// Nothing here registers anything: these read the list the engine already has,
// which holds every convar and command the engine and both game DLLs declared.
// That list is thousands of entries and is not ours, which is why the walk is
// taken as a snapshot and drained in one go rather than held onto — the entries
// live exactly as long as the DLL that registered them.
//
// The snapshot keeps POINTERS, not copies. Copying every string at snapshot
// time would mean holding a couple of megabytes for a listing that is read once
// and thrown away; the strings are copied out one entry at a time instead, in
// the same frame, while the list they came from is still the list that was
// walked.
//-----------------------------------------------------------------------------
static std::vector< ConCommandBase * > g_SourceSharpConsoleSnapshot;

static void SourceSharp_CopyOut( char *pOut, int nOutSize, const char *pIn )
{
	if ( pIn && pIn[0] )
		ss::StrCopy( pOut, pIn, nOutSize );
	else
		pOut[0] = '\0';
}

//-----------------------------------------------------------------------------
// Assembled a bit at a time because ConCommandBase keeps its flags private and
// offers only IsFlagSet. Asking 32 questions per entry is more calls than
// reading a field would be, and it is also the ANSWER THE ENGINE GIVES: ConVar
// overrides IsFlagSet to report its parent's flags, so a replicated convar
// declared by both DLLs reads the same here as it does to the console.
//-----------------------------------------------------------------------------
static int32_t SourceSharp_ConsoleFlags( const ConCommandBase *pBase )
{
	int32_t nFlags = 0;

	for ( int i = 0; i < 32; i++ )
	{
		const int nBit = (int)( 1u << i );

		if ( pBase->IsFlagSet( nBit ) )
			nFlags |= nBit;
	}

	return nFlags;
}

int32_t CShimConsole::ConsoleSnapshot( void )
{
	g_SourceSharpConsoleSnapshot.clear();

	if ( !g_pCVar )
		return 0;

	ICvar::Iterator iter( g_pCVar );
	for ( iter.SetFirst(); iter.IsValid(); iter.Next() )
	{
		ConCommandBase *pBase = iter.Get();

		if ( pBase )
			g_SourceSharpConsoleSnapshot.push_back( pBase );
	}

	return (int)g_SourceSharpConsoleSnapshot.size();
}

int32_t CShimConsole::ConsoleSnapshotEntry( int32_t nIndex, SourceSharpConsoleEntry *pOut )
{
	if ( !pOut || nIndex < 0 || nIndex >= (int)g_SourceSharpConsoleSnapshot.size() )
		return 0;

	memset( pOut, 0, sizeof( *pOut ) );

	ConCommandBase *pBase = g_SourceSharpConsoleSnapshot[nIndex];

	SourceSharp_CopyOut( pOut->szName, sizeof( pOut->szName ), pBase->GetName() );
	SourceSharp_CopyOut( pOut->szHelp, sizeof( pOut->szHelp ), pBase->GetHelpText() );

	pOut->nFlags     = SourceSharp_ConsoleFlags( pBase );
	pOut->bIsCommand = pBase->IsCommand() ? 1 : 0;

	// A command has no value and no default, and the buffers stay empty rather
	// than being filled with something that looks like one.
	if ( !pBase->IsCommand() )
	{
		ConVar *pVar = static_cast< ConVar * >( pBase );

		SourceSharp_CopyOut( pOut->szValue, sizeof( pOut->szValue ), pVar->GetString() );
		SourceSharp_CopyOut( pOut->szDefault, sizeof( pOut->szDefault ), pVar->GetDefault() );

		// GetMin writes only when there is one, and the memset above left both
		// values at zero -- which is why the flag is what says whether to read
		// them, rather than the value being compared against anything.
		float flBound = 0.0f;

		if ( pVar->GetMin( flBound ) )
		{
			pOut->bHasMin = 1;
			pOut->flMin   = flBound;
		}

		if ( pVar->GetMax( flBound ) )
		{
			pOut->bHasMax = 1;
			pOut->flMax   = flBound;
		}
	}

	return 1;
}

int32_t CShimConsole::FindConVarValue( const char *pName, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return 0;

	pOut[0] = '\0';

	if ( !pName || !pName[0] || !g_pCVar )
		return 0;

	// FindVar rather than FindCommandBase: the caller asked for a value, and a
	// command has none. Answering 0 for one is the difference between "there is
	// no such setting" and "that name is a command", which the caller can then
	// go and ask about separately.
	ConVar *pVar = g_pCVar->FindVar( pName );

	if ( !pVar )
		return 0;

	ss::StrCopy( pOut, pVar->GetString(), nOutSize );
	return 1;
}

//-----------------------------------------------------------------------------
// A convar the ENGINE owns, set by name.
//
// The write that FindConVarValue is the read of, and it exists because the
// world-state entities write convars nobody here registered:
// env_tonemap_controller sets mat_hdr_tonemapscale, env_detail_controller sets
// cl_detaildist. SetConVarValue cannot reach either — it addresses this mod's
// own registrations by id, and these have none.
//
// Set through the ConVar rather than the console, which is the point. Every
// convar named here is FCVAR_CHEAT or close to it, a console change to one is
// refused without sv_cheats, and every stock entity that writes one writes it
// from code where no such check applies. Going the other way would have made
// env_tonemap_controller work only on a cheating server.
//
// A name that resolves to nothing is ignored rather than reported: this is
// compiled into both sides and half the names it will be asked for exist on
// only one of them.
//-----------------------------------------------------------------------------
void CShimConsole::SetNamedConVarValue( const char *pName, const char *pValue )
{
	if ( !pName || !pName[0] || !pValue || !g_pCVar )
		return;

	ConVar *pVar = g_pCVar->FindVar( pName );

	if ( !pVar )
		return;

	pVar->SetValue( pValue );
}

//-----------------------------------------------------------------------------
// The same convar back to its default, by name: ConVar::Revert. The client
// soundscape system reverts dsp_volume and snd_soundmixer when a soundscape
// does not set them (stock C_SoundscapeSystem::StartNewSoundscape). A name
// that resolves to nothing is ignored, as SetNamedConVarValue's is.
//-----------------------------------------------------------------------------
void CShimConsole::RevertNamedConVar( const char *pName )
{
	if ( !pName || !pName[0] || !g_pCVar )
		return;

	ConVar *pVar = g_pCVar->FindVar( pName );

	if ( !pVar )
		return;

	// ConVar::Revert's own body (tier1/convar.cpp): the parent's default, set
	// again. Spelled out because server.so's convar_port.cpp carries no Revert.
	pVar->SetValue( pVar->GetDefault() );
}

//-----------------------------------------------------------------------------
// Locates <mod>/bin, which holds libsourcesharp, the bundled runtime and the
// managed assemblies.
//-----------------------------------------------------------------------------
static bool SourceSharp_GetModBinDir( char *pOut, int nOutSize )
{
	SOURCESHARP_TRACE();

#ifdef CLIENT_DLL
	const char *pGameDir = engine->GetGameDirectory();
	if ( !pGameDir || !pGameDir[0] )
		return false;
	ss::Format( pOut, nOutSize, "%s/bin", pGameDir );
#else
	char szGameDir[512];
	engine->GetGameDir( szGameDir, sizeof( szGameDir ) );
	if ( !szGameDir[0] )
		return false;
	ss::Format( pOut, nOutSize, "%s/bin", szGameDir );
#endif
	ss::FixSlashes( pOut );
	return true;
}

//-----------------------------------------------------------------------------
// A managed init that failed, reported once and then fatal.
//
// Error() rather than Warning()-and-continue, and rather than returning false
// out of Init():
//
//   * Error() is what this codebase already uses for exactly this class of
//     failure — the managed side not delivering something the game cannot run
//     without. shell_server.cpp does it when C# registered no player class, and
//     shell_usermessages.cpp does it when the message list overflows. Stock
//     Source does it in usermessages.cpp itself, three times.
//
//   * It is NORETURN (tier0/dbg.h) and goes through the engine's spew handler,
//     so the reader gets this text — in the console, in console.log under
//     -condebug, and in a dialog where there is one — and then the process
//     stops. Not a silent exit and not a fault twenty frames later.
//
//   * Returning false out of Init() would NOT do the job on the client, and
//     5b-runtime MEASURED the second half of that. It used to be an argument
//     about ordering -- the caller was SourceSharp_ClientEarlyInit(), a void
//     function that threw the answer away, with gHUD.Init() between it and the
//     InitAllSystems() that would have seen the false. That caller is gone and
//     CShellClientDLL::Init does check the answer, so only the stronger half
//     is left: `return false` out of IBaseClientDLL::Init is a STACK OVERFLOW.
//     The engine has no clean answer for it and recurses until the stack is
//     gone, so the crash is what the reader sees instead of this message. See
//     SourceSharp_FatalClientInitFailure in
//     sourcesharp/game/client/managed_runtime_client.cpp, which reaches the
//     same conclusion from the other end.
//
// The message is assembled in managed_init_failure.h, which is engine-free and
// tested; this function is the part that cannot be, because it does not return.
//-----------------------------------------------------------------------------
static void SourceSharp_FatalInitFailure( int nStage, int rc, const char *pDetail )
{
	char szMessage[2048];
	SourceSharpInit_Format( szMessage, sizeof( szMessage ),
		SOURCESHARP_SIDE_TAG, nStage, rc, pDetail );

	// "%s", not the message as the format: the detail is a path and a host
	// string, and one stray %s in it would read the stack.
	Error( "%s", szMessage );
}

//-----------------------------------------------------------------------------
// BRINGING THE MANAGED SIDE UP, factored out of CSourceSharpSystem::Init.
//
// FACTORED, NOT MOVED, and that distinction is the whole point of this
// function existing. This translation unit is compiled into BOTH server.so and
// client.so, and each side has ONE named caller in its own file:
//
//   server  LoadManagedRuntime( IManagedRuntimeEnvironment * )
//           sourcesharp/game/server/managed_runtime_server.cpp
//   client  LoadManagedRuntime( IManagedRuntimeEnvironment * )
//           sourcesharp/game/client/managed_runtime_client.cpp
//
// The client's used to be CSourceSharpSystem::Init reached from
// SourceSharp_ClientEarlyInit -- a load with no interface and no partner for
// the release. 5b-runtime gave it the server's shape; the CALL SITE inside
// CShellClientDLL::Init is at the same point in the same order it always was.
//
// Moving the sequence onto either side's file would compile green on that side
// and leave the other with no runtime at all -- this tree's documented
// shared-file failure, and the reason plan_gamedll.md Part 3 spells the order
// out.
//
// Idempotent by the gate: whichever side calls it, a second call is a no-op.
//-----------------------------------------------------------------------------
bool SourceSharp_LoadManagedSide( void )
{
	// Idempotent, and on the client that is now belt AND braces rather than the
	// mechanism.
	//
	// It used to be the mechanism: this ran twice on the client -- once from
	// SourceSharp_ClientEarlyInit() ahead of gHUD.Init(), because the HUD hooks
	// its user messages against a list C# registers, and again from
	// IGameSystem::InitAllSystems() afterwards -- and the gate is what made the
	// second call a no-op. There is ONE client caller now,
	// LoadManagedRuntime() from CShellClientDLL::Init, at the same point in the
	// same order; the kClientSystems[] row does no load of its own. The gate is
	// kept because the SERVER still has its own reasons for it and because a
	// second caller reappearing must not Acquire twice.
	//
	// The gate closes at the BOTTOM of this function, not here: an attempt
	// that did not finish must not read as one that did. See
	// managed_init_failure.h for what that used to cost.
	if ( !g_SourceSharpInitGate.ShouldRun() )
		return true;

	// Route host diagnostics into the Source console before anything else,
	// so a failure during Acquire is visible rather than silent.
	SourceSharp_SetLogger( SourceSharp_HostLog );
	SourceSharp_SetVerbosity( SourceSharp_GetVerbosity() );

	Msg( "[SourceSharp] %s DLL init — bringing up managed runtime\n", SOURCESHARP_SIDE_TAG );

	// Show exactly what this DLL exports, so a missing interface is visible
	// in the console instead of surfacing as an engine crash later.
	// Before anything that can assert, which on a bad day is the entity
	// factory installing a name twice during static init.
	SourceSharp_InstallAssertLogging();

	SourceSharp_MarkDiagnosticsReady();
	SourceSharp_DumpExportedInterfaces();
	SourceSharp_DumpNetworkClasses();

	char szModBin[512];
	if ( !SourceSharp_GetModBinDir( szModBin, sizeof( szModBin ) ) )
		SourceSharp_FatalInitFailure( kSourceSharpInitStage_ModBinDir, 0, NULL );

	Msg( "[SourceSharp] mod bin dir resolved: %s\n", szModBin );

	int rc = SourceSharp_Acquire( szModBin );
	if ( rc != SOURCESHARP_OK )
		SourceSharp_FatalInitFailure( kSourceSharpInitStage_Acquire, rc, SourceSharp_LastError() );

// plan_client_vtables CC-last: the client passes no struct either. Every slot
// its fill served is a method of one of its shim areas (shared bases in
// ishim_areas.h, client-only areas in ishim_areas_client.h), acquired through
// its shim root; the fill went with them.

#ifndef CLIENT_DLL
	// BEFORE LoadSide, because managed registers its temp entity classes
	// from inside it and a base table still owned by stock has to be
	// resolvable by then. Wiring the resolver afterwards would refuse every
	// derived table on the first run and none on the second.
	SourceSharp_TempEntInit();
#else
	// The same, on the same side of LoadSide and for the same reason. Every
	// managed base table this client declares is resolved against
	// g_pClientClassHead when stock still owns it, which is the ordinary
	// state of this port for its whole length.
	SourceSharp_TempEntClientInit();
#endif

	rc = SourceSharp_LoadMySide();
	if ( rc != SOURCESHARP_OK )
	{
		// No SourceSharp_Release() here, where the old degrade path had
		// one. It existed to leave the host tidy for a game that was going
		// to carry on; this one is not. Error() does not return, so the
		// only thing a release could do is run hostfxr's teardown inside a
		// process that has already been declared dead — after the message
		// has been printed, where a fault in it would be blamed on this.
		SourceSharp_FatalInitFailure( kSourceSharpInitStage_LoadSide, rc,
			SourceSharp_LastError() );
	}

	g_bSourceSharpLoaded = true;

#ifndef CLIENT_DLL
	// AND THE TEMP ENTITY WINDOW CLOSES HERE. Everything managed had to say
	// has been said; a ServerClass built after this point is not in the list
	// the engine already walked, and nothing anywhere would report it.
	SourceSharp_TempEntCloseRegistration();
#else
	// The client's window shuts here too. A ClientClass linked into
	// g_pClientClassHead after CHLClient::GetAllClasses has been walked is
	// not in the list the handshake negotiates, and the first event of that
	// class then resolves to nothing with no line anywhere.
	SourceSharp_TempEntClientCloseRegistration();
#endif

	// The gate closes HERE, past every failure point, and nowhere earlier.
	g_SourceSharpInitGate.MarkSucceeded();

	Msg( "[SourceSharp] managed %s runtime online (%d C# console command(s) registered)\n",
		SOURCESHARP_SIDE_TAG, g_SourceSharpCommands.Count() );
	return true;
}

//-----------------------------------------------------------------------------
// TAKING THE MANAGED SIDE DOWN, the exact counterpart of
// SourceSharp_LoadManagedSide above, and factored for the same reason: the
// server reaches it from CManagedRuntimeServer::OnSystemStop (plan_gamedll.md
// Part 3's table row "Shutdown() -- UnloadSide, Release -> the adapter's
// OnSystemStop"), the client from CSourceSharpSystem::Shutdown.
//
// The CLR is refcounted across client.so and server.so (sourcesharp.cpp), so a
// side that loads and never releases is how the runtime outlives the side that
// owned it -- which is the same imbalance plan_gamedll.md Part 10b names in
// DLLShutdown, reached from the other end.
//-----------------------------------------------------------------------------
void SourceSharp_UnloadManagedSide( void )
{
	SOURCESHARP_TRACE();
	g_SourceSharpInitGate.Reset();

	// AN object the engine owns that points into this DLL, and it is not
	// behind CLIENT_DLL because both sides subscribe: the server to the
	// authoritative events, the client to the networked copies.
	//
	// ABOVE the g_bSourceSharpLoaded early-out, deliberately and for the same
	// reason the filesystem hook is. Every path that reaches this function is a
	// path where this DLL is going away, and the listener was installed by C#
	// asking for it -- so a side that got far enough to subscribe and then
	// failed somewhere else is exactly the case that must still unhook. The
	// call is a no-op when nothing subscribed.
	SourceSharp_StopListeningForGameEvents();

	if ( !g_bSourceSharpLoaded )
		return;

	Msg( "[SourceSharp] %s shutting down managed runtime\n", SOURCESHARP_SIDE_TAG );

	// The display func leaves the engine's list FIRST: nothing the engine
	// prints after this point may re-enter this DLL through a forwarder whose
	// managed object is about to go.
	ConsoleDisplay_Remove();

	// D4 (and plan_client_vtables CD for the client): the managed root and its
	// areas, released BEFORE UnloadSide unloads the module: a handle still held
	// then keeps the load context alive and is released a second time by the
	// module's own net. Neither side has a Shutdown slot any more (the server
	// since plan_engineapi_vtables E18, the client since plan_client_vtables
	// CE-last): it only ran the module's unload, which UnloadSide runs.
	ManagedRoot_Release();

	SourceSharp_UnloadMySide();
	SourceSharp_Release();

	g_bSourceSharpLoaded = false;
}

//-----------------------------------------------------------------------------
// The bridge itself.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// THE MANAGED SIDE'S FOUR SERVER EVENT POINTS, EACH WITH EXACTLY ONE BODY.
//
// plan_gamedll.md Phase 7. Two things drive these points in the server binary:
// CSourceSharpSystem in delegate mode, and the shell's kServerSystems[] row
// with -shellgamedll. There must never be two IMPLEMENTATIONS. The phase's
// invariant -- NOTHING RUNS TWICE, NOTHING STOPS RUNNING -- is held
// structurally by there being one body per point, here, called from both.
//
// Factored out of CSourceSharpSystem below rather than copied. A copy is two
// bodies that agree until one of them is edited, and what would be copied is
// the order in which managed code learns that a level started and a frame ran.
//
// The map name is a PARAMETER because the two callers get it from different
// places: CSourceSharpSystem has IGameSystem::MapName(), which is set by
// LevelInitPreEntityAllSystems, and the shell does not call that -- its row
// passes gpGlobals->mapname. Both name the map that is loading.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE PRE-ENTITY HALF, added by plan_gamedll_after.md Phase A3 -- a FIFTH body
// beside the four above, and the reason it is a body here rather than a call in
// the shell is the same reason those four are: there must be one of it.
//
// IT HAD NO CALLER AT ALL BEFORE THIS PHASE, on either side of the boundary.
// Part 3a added IGameLifecycle.OnLevelInitPreEntity and
// LifecycleDriver.LevelInitPreEntity and ServerModule only ever called the
// PostEntity half, so a participant that declared the pre-entity stage was
// never reached and nothing said so.
//
// Phase A3's two systems are why it is wired now: CDecalEmitterSystem and
// CPropData both do their work in that half, and both of them PRECACHE -- a
// decal index and a chunk model -- so the work has to happen inside the window
// that the world entity's own precache opens and the first Spawn() closes.
//-----------------------------------------------------------------------------
void SourceSharp_ManagedLevelInitPreEntity( const char *pszMapName )
{
	// E1: the managed-built runtime's slot, not a ManagedApi one.
	IManagedRuntimeServer *pRuntime = ManagedRuntimeServer();
	if ( !g_bSourceSharpLoaded || !pRuntime )
		return;

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] %s level init pre-entity -> C#: %s\n",
		     SOURCESHARP_SIDE_TAG, pszMapName );

	pRuntime->OnMapLoadPreEntity( pszMapName );
}

void SourceSharp_ManagedLevelInit( const char *pszMapName )
{
	// E18: the managed-built IManagedFrame.
	IManagedFrame *pFrame = g_bSourceSharpLoaded ? ManagedFrame() : NULL;
	if ( !pFrame )
		return;

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] %s level init -> C#: %s\n", SOURCESHARP_SIDE_TAG, pszMapName );

	pFrame->LevelInit( pszMapName );
}

//-----------------------------------------------------------------------------
// THE PRE-ENTITY HALF OF LEVEL SHUTDOWN -- plan_gamedll_after.md Phase A3, the
// fifth "C" system. A SIXTH body beside the five above, and it is here for the
// same reason they are: there must be exactly one of it.
//
// IT HAD NO CALLER AT ALL BEFORE THIS PHASE, on either side of the boundary,
// and that is the third time this plan has found a stage in that state.
// LifecycleDriver.LevelShutdownPreEntity existed from Part 3a's split; the only
// things that had ever driven it were two facts in LifecycleTests.cs.
//
// The cost was real rather than theoretical: SoundController declares
// OnLevelShutdownPreEntity for SystemReset (Phase A3, second pair), so every
// live sound patch survived a level change from that phase until this one.
//-----------------------------------------------------------------------------
void SourceSharp_ManagedLevelShutdownPreEntity( void )
{
	// E1: the managed-built runtime's slot, not a ManagedApi one.
	IManagedRuntimeServer *pRuntime = ManagedRuntimeServer();
	if ( !g_bSourceSharpLoaded || !pRuntime )
		return;

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] %s level shutdown pre-entity -> C#\n", SOURCESHARP_SIDE_TAG );

	pRuntime->OnMapUnloadPreEntity();
}

//-----------------------------------------------------------------------------
// CHECKTRANSMIT'S RECIPIENT -- plan_server.md Phase 8b, plan-audit C4.
//
// Asks C# which hierarchy answers for this client and what its 3D-skybox area
// is. 1 with *pOutArea written; 0 with it untouched.
//
// THE FALLBACK IS NOT A SILENT ZERO. If the managed runtime is absent -- which
// is the shell's "cannot happen, and if it does the server is already dead"
// case, since ServerSkyboxAreaForClient is on the required list -- this answers
// 0, and 0 makes CheckTransmit RETURN EARLY: a client that receives nothing.
// That is loud in the only way this method can be loud, and it is strictly
// better than the alternative of walking 473 edicts against an area of 0 and
// transmitting a plausible wrong set. A recipient nobody can describe is a
// recipient this function has always declined.
//
// NOT VERBOSE-LOGGED. Every other wrapper in this file prints under
// SourceSharp_GetVerbosity() > 0; this one runs once per client per frame, so
// at 66 ticks a verbose server would print 66 lines a second per player and the
// verbosity switch would stop being usable for anything else.
//-----------------------------------------------------------------------------
int SourceSharp_SkyboxAreaForClient( int iClientEdict, int *pOutArea )
{
	if ( !g_bSourceSharpLoaded || !ManagedClients() )
		return 0;

	int32_t nArea = 0;
	const int32_t nFound = ManagedClients()->ServerSkyboxAreaForClient( iClientEdict, &nArea );

	// WRITTEN ONLY ON A HIT, which is the contract the two engine-side steps
	// keep as well: area 0 is a real visleaf area, so a caller that pre-set
	// *pOutArea and then read it back after a miss must find what it put there.
	if ( nFound )
		*pOutArea = nArea;

	return nFound ? 1 : 0;
}

//-----------------------------------------------------------------------------
// CLIENTEARPOSITION'S HIERARCHY -- plan_server.md Phase 7, plan-audit C5.
//
// Asks C# which hierarchy answers for this client's ears and where they are.
// 1 with pOutXYZ written; 0 with it untouched.
//
// THE FALLBACK IS A "NO", WHICH IS THE ANSWER THE CALLER ALREADY HANDLES. If
// the managed runtime is absent this returns 0, and ClientEarPosition's miss
// path is stock's own: Assert(0) and vec3_origin. That is the loudest thing
// this wrapper can do and it is a path that already existed -- unlike
// SkyboxAreaForClient above, whose 0 makes a client receive nothing, this one
// puts the listener at the world origin and asserts about it in a debug build.
//
// NOT VERBOSE-LOGGED, for SkyboxAreaForClient's reason: at most once per client
// per frame is still 66 lines a second per player.
//-----------------------------------------------------------------------------
int SourceSharp_EarPositionForClient( int iClientEdict, float *pOutXYZ )
{
	if ( pOutXYZ == NULL )
		return 0;

	if ( !g_bSourceSharpLoaded || !ManagedClients() )
		return 0;

	float flEars[ 3 ] = { 0.0f, 0.0f, 0.0f };
	const int32_t nFound = ManagedClients()->ServerEarPositionForClient( iClientEdict, flEars );

	// WRITTEN ONLY ON A HIT, which is the contract the two engine-side steps
	// keep as well: the world origin is a real position, so a caller that
	// pre-set pOutXYZ and then read it back after a miss must find what it put
	// there.
	if ( nFound )
	{
		pOutXYZ[ 0 ] = flEars[ 0 ];
		pOutXYZ[ 1 ] = flEars[ 1 ];
		pOutXYZ[ 2 ] = flEars[ 2 ];
	}

	return nFound ? 1 : 0;
}

void SourceSharp_ManagedLevelShutdown( void )
{
	// E18: the managed-built IManagedFrame.
	IManagedFrame *pFrame = g_bSourceSharpLoaded ? ManagedFrame() : NULL;
	if ( !pFrame )
		return;

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] %s level shutdown -> C#\n", SOURCESHARP_SIDE_TAG );

	pFrame->LevelShutdown();

	// Every control-block slot closed, after managed shutdown rather than
	// before it: a state object is entitled to write during its entity's
	// teardown, and a slot closed early would turn that write into an exception
	// rather than into the value it is trying to leave behind.
	SourceSharp_ResetEntitySlots();
}

void SourceSharp_ManagedFramePreThink( void )
{
	IManagedFrame *pFrame = g_bSourceSharpLoaded ? ManagedFrame() : NULL;   // E18
	if ( pFrame )
		pFrame->FramePreThink( gpGlobals->frametime );
}

void SourceSharp_ManagedFrameThink( void )
{
	IManagedFrame *pFrame = g_bSourceSharpLoaded ? ManagedFrame() : NULL;   // E18
	if ( pFrame )
		pFrame->Frame( gpGlobals->frametime );

	// AFTER the managed frame and before the snapshot, which is what makes this
	// the right hook rather than a convenient one. Every managed write in the
	// frame has landed by now, and each stored a one into its entity's dirty
	// word instead of making a call; this turns the whole frame's stores into
	// one NetworkStateChanged per entity that actually changed.
	//
	// This is not the LAST place a managed write can happen -- CGameRules::
	// EndGameFrame drains again. This one is kept because g_pGameRules is NULL
	// between levels, where EndGameFrame is never called at all.
	SourceSharp_DrainEntitySlots();
}
#endif // !CLIENT_DLL

//=============================================================================
// THE CLIENT'S GAME SYSTEM -- and on the server there is NO SUCH TYPE.
//
// plan_gamedll.md Part 3, landed last in Phase 7a because Part 3 says it may
// only land last: it is safe exactly once every server hook has moved onto the
// shell, and they have. The server's managed frame, level init and level
// shutdown are the kServerSystems[] row `SourceSharpManaged`
// (sourcesharp/game/server/shell_serversystems.cpp); the server's load and
// unload are LoadManagedRuntime and CManagedRuntimeServer::OnSystemStop; the
// server's reload service point is IServerGameDLL::Think.
//
// THIS IS THE STRONGEST FORM OF THE PHASE'S GOAL. Everywhere else a system
// swapped CAutoGameSystem for a base that does not register. Here the type is
// not instantiated and does not exist, so there is nothing to register and
// nothing to forget to name in the table.
//
// THE CLIENT KEEPS THE CLASS, and two things in this paragraph were wrong by
// the time 5b-runtime read them, so both are corrected rather than left:
//
//   client_csharp.vpc does NOT $Include client_base.vpc -- it lists its own
//   274 $File entries, cdll_client_int.cpp is not among them, and the class the
//   engine finds behind CLIENT_DLL_INTERFACE_VERSION is CShellClientDLL. There
//   is no IGameSystem on the client's path either; kClientSystems[] in
//   sourcesharp/game/client/shell_clientsystems.cpp is what walks this object,
//   and CClientSystemPerFrame is its base.
//
//   And this object is no longer the client's route to the CLR. That is
//   LoadManagedRuntime(), from CShellClientDLL::Init, exactly as the server
//   reaches it from DLLInit. This row is the FRAME -- it holds the adapter and
//   forwards OnLevelInit / OnLevelShutdown / OnFrame through it.
//
// THE GUARD IS STILL #ifdef CLIENT_DLL AND NOT A DELETION: the server has no
// such row (its per-frame work is the kServerSystems[] SourceSharpManaged row),
// so deleting the class outright compiles green on the server and leaves the
// client with no managed frame at all -- this project's recorded failure mode
// for a shared file.
//
// The four SourceSharp_Managed* bodies above are NOT inside this guard. They
// are the server's, they had two callers, and this removes one of them.
//=============================================================================
#ifdef CLIENT_DLL

#include "shell_clientsystem.h"	// plan_cworld_trace.md T4

class CSourceSharpSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpSystem() : CClientSystemPerFrame( "SourceSharp" ), m_pRuntime( NULL ) {}

	// THE ROW HOLDS THE ADAPTER; IT DOES NOT LOAD IT.
	//
	// Set by SourceSharp_SetClientRuntime, from CShellClientDLL::Init, right
	// after LoadManagedRuntime( this ) has handed it over. That is the client's
	// half of the arrangement kServerSystems[] already has: the shell brings the
	// CLR up by name from a named point, and the table row is the frame.
	void SetRuntime( IClientRuntimeAdapter *pRuntime ) { m_pRuntime = pRuntime; }

	virtual bool Init() OVERRIDE
	{
		// NOTHING, AND THAT IS THE POINT OF THIS CHANGE.
		//
		// This body used to be `return SourceSharp_LoadManagedSide();`, reached
		// from SourceSharp_ClientEarlyInit(), and it was the client's ONLY route
		// to the CLR -- a load with no interface, no named owner and no partner
		// for the release. CShellClientDLL::Init calls LoadManagedRuntime( this )
		// at exactly the point this used to be called from, so by the time the
		// kClientSystems[] walk reaches this row the runtime is already up.
		//
		// The server's SourceSharpManaged row says the same thing for the same
		// reason (shell_serversystems.cpp: "The CLR is already up:
		// LoadManagedRuntime ran earlier in the shell's DLLInit").
		return true;
	}

	virtual void Shutdown() OVERRIDE
	{
		// The row drops its reference at the position the table shuts it down.
		// The managed teardown itself is CShellClientDLL::Shutdown's, through
		// IClientRuntimeAdapter::OnSystemStop, immediately before this walk --
		// the mirror of CShellGameDll::DLLShutdown, which stops the managed side
		// and then walks kServerSystems[] backwards.
		//
		// Clearing it here rather than leaving it dangling is what makes
		// UnloadManagedRuntime, which runs after this walk, unable to leave the
		// row pointing at freed memory.
		m_pRuntime = NULL;
	}

	virtual void LevelInitPostEntity() OVERRIDE
	{
		SOURCESHARP_TRACE();

		// THE SLOT TEST IS GONE AND THE LOAD TEST IS NOT, and they were never
		// the same question. `g_ManagedApi.LevelInit != NULL` asked whether C#
		// implemented the slot, and skipping on NO was a client that never told
		// managed code a level had loaded, forever, with a clean log; that is
		// answered once now, at load, by SourceSharp_ValidateClientApi.
		// `g_bSourceSharpLoaded` asks whether the table is live AT ALL, which is
		// a real interval -- before the first load, after the last unload, and
		// inside a cs_cl_reload -- and it stays.
		if ( !g_bSourceSharpLoaded || m_pRuntime == NULL )
			return;

		// SOURCESHARP plan_cworld_trace.md T4: was IGameSystem::MapName().
		char szMapName[ MAX_PATH ];
		SourceSharp_ClientMapName( szMapName, sizeof( szMapName ) );

		if ( SourceSharp_GetVerbosity() > 0 )
			Msg( "[SourceSharp] %s level init -> C#: %s\n", SOURCESHARP_SIDE_TAG, szMapName );

		m_pRuntime->OnLevelInit( szMapName );
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		SOURCESHARP_TRACE();

		if ( !g_bSourceSharpLoaded || m_pRuntime == NULL )
			return;

		if ( SourceSharp_GetVerbosity() > 0 )
			Msg( "[SourceSharp] %s level shutdown -> C#\n", SOURCESHARP_SIDE_TAG );

		m_pRuntime->OnLevelShutdown();
	}

	virtual void Update( float frametime ) OVERRIDE
	{
		// DELIBERATELY NOT SOURCESHARP_TRACE_HOT: this is the client's per-frame
		// system update, and letting it out at verbosity 2 would put a line on
		// every frame of a session someone turned verbosity 2 on to read. One
		// notch quieter than the hot rule, on purpose.
		if ( SourceSharp_GetVerbosity() > 2 )
			SOURCESHARP_TRACE();

		// Before the managed frame, so the frame this runs in is the new
		// build's. See SourceSharp_ServiceReloadRequest for why it is here and
		// not in the console command that asked for it.
		//
		// THE SERVER'S TWO STAGES USED TO BE HERE, under #else:
		// FrameUpdatePreEntityThink and FrameUpdatePostEntityThink, forwarding
		// to SourceSharp_ManagedFramePreThink and SourceSharp_ManagedFrameThink.
		// Phase 7a removed them with the class, and the shell's kServerSystems[]
		// row is what calls those two bodies now. The server's reload service
		// point went with them and is IServerGameDLL::Think alone, which is the
		// only per-frame entry that runs with no level loaded.
		SourceSharp_ServiceReloadRequest();

		if ( g_bSourceSharpLoaded && m_pRuntime != NULL )
			m_pRuntime->OnFrame( frametime );
	}

private:
	IClientRuntimeAdapter *m_pRuntime;
};

static CSourceSharpSystem g_SourceSharpSystem;

// plan_cworld_trace.md T4: kClientSystems[] is the only thing that reaches this.
CClientSystem *ClientSystem_SourceSharpManaged() { return &g_SourceSharpSystem; }

// Handing the row the adapter the shell just loaded. Declared in
// sourcesharp_gamesystem.h beside LoadManagedRuntime, because it is part of the
// same arrangement: the shell owns the load, the row owns the frame, and this
// is the one line between them.
void SourceSharp_SetClientRuntime( IClientRuntimeAdapter *pRuntime )
{
	g_SourceSharpSystem.SetRuntime( pRuntime );
}

#endif // CLIENT_DLL


//=============================================================================//
// HOT RELOAD. plan.md Part 5 tier 1, plan_nettable_generic.md Step 10.
//
// Unload this module's side and load it again, in place, with the game running
// and clients connected. What makes it possible is that nothing the two sides
// negotiate is generated any more: the ServerClasses are the seven SIZE TIERS,
// registered at DLL init and never touched again, so a managed type may be
// added, removed or reshaped inside its tier without moving one prop.
//
// WHAT SURVIVES, because it is native memory the managed assembly never owned:
// every entity, its edict, its blob, its control-block slot and serial, the
// published type table, and every ConVar and ConCommand object with the values
// an operator set on them. WHAT DOES NOT: every managed object, which is the
// point -- the new assembly builds its own, lazily, as the game asks for them.
//
// WHAT THE RELOAD HAS TO PUT RIGHT is the one fact that is copied rather than
// referenced: each live entity carries the schema hash of the layout it was
// stamped with, and GetServerClass stamps it once and never again. See
// SourceSharp_StandaloneResyncSchema.
//
// TWO COMMANDS, ONE PER MODULE, because a listen server runs two of these in
// one process with a load context each, and reloading ONE is the interesting
// case as well as reloading both -- a server whose C# has moved and a client
// whose C# has not is the REMOTE client's situation reproduced locally, and it
// is the only way this build can exercise it at all.
//
// Not FCVAR_CHEAT. A console command is not forwarded to the server, so
// cs_sv_reload is reachable only from the machine running the server; and a
// cheat-gated command that silently does nothing is the failure shape this tree
// spends most of its time removing.
//=============================================================================//
#ifndef CLIENT_DLL
void SourceSharp_StandaloneResyncSchema( int *pOutChecked, int *pOutRestamped );
int  SourceSharp_RetireEntitySlots( void );
int  SourceSharp_StandaloneLiveCount( void );
#endif

static void SourceSharp_ReloadManagedSide( void )
{
	if ( !g_bSourceSharpLoaded )
	{
		Warning( "[SourceSharp] %s: nothing to reload -- the managed runtime is not up\n",
			SOURCESHARP_SIDE_TAG );
		return;
	}

	// plan_managed_vtable_reload.md L5: BEFORE anything is saved or unloaded.
	// Native holds every managed-built object for the process, so the new
	// build must keep each one's slot hashes; if not, the old build keeps
	// running and nothing has been touched.
	char szWhy[ 256 ];
	if ( SourceSharp_ReloadCheckMySide( szWhy, sizeof( szWhy ) ) != 0 )
	{
		Warning( "[SourceSharp] %s: reload REFUSED, the running build stays: %s\n", SOURCESHARP_SIDE_TAG, szWhy );
		return;
	}

	Msg( "[SourceSharp] %s: reloading managed assemblies\n", SOURCESHARP_SIDE_TAG );

	// EVERY LIVE ENTITY'S STATE, FIRST, WHILE THE ASSEMBLY THAT UNDERSTANDS IT
	// IS STILL LOADED.
	//
	// A reload keeps every entity and destroys every managed object, and the
	// managed object is where a map's keyvalues ended up: they are handed over
	// during the parse and kept nowhere else, so an entity rebuilt afterwards
	// would come back with its class's declared defaults -- a func_door with
	// stock's speed instead of the mapper's, looking entirely normal. What this
	// call produces is text in the ABI, which lives in the default load context
	// and is the only thing that outlives both builds.
	// E4 server, CE7 client: read here, at the call, from the old build (reload L4).
#ifndef CLIENT_DLL
	IManagedSaveRestore *pSaveArea = ManagedSaveRestore();
#else
	IManagedSaveRestoreClient *pSaveArea = ManagedSaveRestoreClient();
#endif
	const int nSaved = pSaveArea ? pSaveArea->ReloadSave() : 0;

	// Entry.UnloadSide runs the module's Shutdown, drops the ABI's caches for
	// this side, unloads the context and then CHECKS that it actually died -- a
	// context that will not unload is the number-one hot-reload bug and it is
	// silent, so the warning it prints is the thing to read after this line.
	// plan_managed_vtable_reload.md L4: native keeps the managed root and every
	// area across the reload. UnloadSide detaches their blocks (no handle into
	// the outgoing context, every slot on the trap table) and the new build
	// patches the same blocks, so nothing here is released or re-acquired.
	// The console forwarder sits out the gap: until the load below patches the
	// blocks, its area's slots are the trap (the hot-reload SIGABRT, 10-10/11).
	CConsoleDisplayReloadGap consoleGap;

	SourceSharp_UnloadMySide();

	// THE PREDICTION RING NEEDS NO LINE HERE ANY MORE (plan_entity.md step 6f).
	//
	// It used to: the rings were native statics holding copies of blobs the
	// outgoing assembly's layout wrote, so a ring that survived a reload would
	// compare a previous world's bytes and report the difference as
	// misprediction -- which is why this call was unconditional. The ring is
	// now a field of the managed ClientPlayerPrediction, which the client's
	// load context owns, so UnloadSide above takes it with everything else.
	// The invalidation is by CONSTRUCTION rather than by a call somebody has to
	// remember, which is the direction the port keeps moving this kind of rule.

	// The player sync's resolved offsets belong to the outgoing assembly's
	// layout for the same reason, on BOTH sides (Phase 9c5). The next sync
	// re-asks rather than writing the loop's state through a moved layout.
	void SourceSharp_PlayerSyncInvalidate( const char *pReason );
	SourceSharp_PlayerSyncInvalidate( "managed reload" );

	g_bSourceSharpLoaded = false;

#ifndef CLIENT_DLL
	// THE ONE WINDOW WITH NO MANAGED CODE IN IT, which is the only place this
	// can go. Every blob pointer the outgoing assembly cached is still a valid
	// pointer to still-valid memory -- the entity did not die -- so nothing
	// about the pointer itself says it must not be written through any more.
	// The serial is what says it, and it only ever moved when a slot was opened
	// for a DIFFERENT entity, so until this line it answered entity death alone.
	const int nRetired = SourceSharp_RetireEntitySlots();
#endif

	const int rc = SourceSharp_LoadMySide();

	if ( rc != SOURCESHARP_OK )
	{
		// FATAL, AND IT USED TO WARN AND CARRY ON. That was correct for exactly
		// as long as native could fall back: every other caller of the
		// managed side in this tree tests its area for NULL and degrades to
		// stock, so a dead table meant a game that ran stock's
		// behaviour badly rather than one that faulted.
		//
		// The shell game DLL is the one caller that cannot degrade. With
		// CServerGameDLL guarded out there is nothing behind IServerGameDLL,
		// and CManagedRuntimeServer forwards without a null test BECAUSE this
		// line is now an Error: the next GameFrame after a failed reload would
		// otherwise call through a memset table. plan_gamedll.md Part 3b
		// point 2, and it is the same rule this project already applies to a
		// failed INIT (managed_init_failure.h) and to a missing table slot
		// (SourceSharp_ValidateServerApi) -- the same argument at the same
		// boundary, reached a third way.
		//
		// Error() is NORETURN and goes through the engine's spew handler, so
		// this text reaches the console, console.log under -condebug, and a
		// dialog where there is one, and then the process stops.
		Error( "[SourceSharp] %s: managed reload FAILED (%d): %s -- this side is now dead "
		       "and there is no stock behaviour behind it, so the process stops here rather "
		       "than calling through an unloaded assembly on the next frame. %d entity(ies) "
		       "worth of saved state is dropped with it, because the assembly that was going "
		       "to adopt them is not there and state kept for a restore that never happens "
		       "would be applied to whatever the next reload finds at those handles.\n",
			SOURCESHARP_SIDE_TAG, rc, SourceSharp_LastError(), nSaved );
		return;
	}

	g_bSourceSharpLoaded = true;
	consoleGap.End();   // the blocks are patched: the new build takes the prints

	// By here LoadSide has run the new module's Initialize, so every type has
	// been published again and the type table holds the NEW hashes. The live
	// entities still carry the old ones.
#ifndef CLIENT_DLL
	Msg( "[SourceSharp] %s: %d bound blob pointer(s) retired\n", SOURCESHARP_SIDE_TAG, nRetired );

	// One shell, one walk. The second went with the runtime-networked shell;
	// the walk still ADDS to these counters rather than assigning, because that
	// is a property of the counters and not of how many walks share them.
	int nChecked = 0, nRestamped = 0;
	SourceSharp_StandaloneResyncSchema( &nChecked, &nRestamped );

	Msg( "[SourceSharp] %s: %d stamped entity(ies) checked, %d reshaped and re-stamped\n",
		SOURCESHARP_SIDE_TAG, nChecked, nRestamped );
#endif

	// AND ONLY NOW THE RESTORE, which is the ordering that is not obvious and is
	// the reason this is a separate call rather than something the new module
	// does in its own Initialize. The resync above ZEROES the blob of any class
	// whose layout moved -- those bytes were written through a layout that no
	// longer exists -- so a restore that ran before it would have every one of
	// its writes wiped by it, and would look exactly like a restore that worked.
	//
	// BOTH SIDES, outside the guard above. The client keeps managed halves of
	// its own -- the surface renderer's, the sprites', everything that draws --
	// and they die in a client reload exactly as the server's do. There is no
	// resync to wait for over there because a client's schema hash arrives on
	// the wire rather than being stamped, so the ordering constraint is the
	// server's alone and the call is not.
	// E4 server, CE7 client: the area the load above just acquired from the new build.
#ifndef CLIENT_DLL
	IManagedSaveRestore *pRestoreArea = ManagedSaveRestore();
#else
	IManagedSaveRestoreClient *pRestoreArea = ManagedSaveRestoreClient();
#endif
	const int nRestored = pRestoreArea ? pRestoreArea->ReloadRestore() : 0;

	Msg( "[SourceSharp] %s: managed reload complete -- %d entity(ies) saved, %d restored\n",
		SOURCESHARP_SIDE_TAG, nSaved, nRestored );

	// THE ONE NUMBER THAT SAYS WHETHER THIS WORKED, and it is a comparison
	// rather than a total.
	//
	// A reload used to keep the state and lose the BEHAVIOUR: the registry that
	// maps a handle to an instance went with the old context, so every entity
	// that already existed resolved to nothing -- inputs dropped, no thinks, no
	// outputs -- while replicating perfectly and looking entirely normal from
	// the client and from the inspector. That is what the save and the restore
	// are for, and a restore that quietly carried nothing would look exactly
	// like the reload that worked.
	//
	// So the two counts are printed together and a shortfall is a warning. They
	// are allowed to differ: an entity whose class the new build no longer
	// declares has nothing to be rebuilt as, and that is a real answer rather
	// than a fault -- but it is one somebody should be told about rather than
	// left to notice when the map stops working.
	if ( nRestored < nSaved )
	{
		Warning( "[SourceSharp] %d of %d saved entity(ies) did NOT come back. Each one still "
		         "exists, still replicates and still looks normal, and none of them will "
		         "think, accept an input or fire an output. The lines above name them.\n",
			nSaved - nRestored, nSaved );
	}

#ifndef CLIENT_DLL
	if ( nRestamped > 0 )
	{
		Warning( "[SourceSharp] a layout moved. Any client whose OWN managed side has not "
		         "been reloaded will now REFUSE those entities' payloads rather than decode "
		         "them through the wrong offsets -- which is the intended outcome and not a "
		         "fault. Reload the client side too (cs_cl_reload on a listen server), or "
		         "reconnect a remote client with a rebuilt game.\n" );
	}
#endif
}

static void SourceSharp_RequestReload( void )
{
	if ( g_bReloadRequested )
	{
		Msg( "[SourceSharp] %s: a managed reload is already queued\n", SOURCESHARP_SIDE_TAG );
		return;
	}

	g_bReloadRequested = true;
	Msg( "[SourceSharp] %s: managed reload queued for the next frame\n", SOURCESHARP_SIDE_TAG );
}

#ifdef CLIENT_DLL
CON_COMMAND( cs_cl_reload,
	"Reload the CLIENT's managed assemblies in place, without restarting the game" )
{
	SourceSharp_RequestReload();
}
#else
SS_NATIVE_COMMAND( cs_sv_reload )
{
	SourceSharp_RequestReload();
}
#endif


#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// Server shell bridge.
//
// These are the hooks server_base leaves for the mod. Each routes the decision
// into C# and degrades to a safe default when the managed runtime is not up —
// the game must still boot if C# fails to load. See plan.md Part 8.
//-----------------------------------------------------------------------------

const char *SourceSharp_GetGameDescription()
{
	SOURCESHARP_TRACE_HOT();

	static char s_szDescription[128];

	// E11: the managed-built runtime's browser answer.
	IManagedRuntimeServer *pRuntime = ManagedRuntimeServer();
	if ( g_bSourceSharpLoaded && pRuntime )
	{
		s_szDescription[0] = '\0';
		pRuntime->OnGetGameDescription( s_szDescription, sizeof( s_szDescription ) );
		if ( s_szDescription[0] )
			return s_szDescription;
	}

	return "SourceSharp";
}

void SourceSharp_OnClientPutInServer( edict_t *pEntity, const char *pPlayerName )
{
	SOURCESHARP_TRACE();
	if ( g_bSourceSharpLoaded && ManagedClients() )
		ManagedClients()->OnClientPutInServer( pEntity ? pEntity->m_EdictIndex : -1, pPlayerName );
}

void SourceSharp_OnClientActive( edict_t *pEntity, bool bLoadGame )
{
	SOURCESHARP_TRACE();
	if ( g_bSourceSharpLoaded && ManagedClients() )
		ManagedClients()->OnClientActive( pEntity ? pEntity->m_EdictIndex : -1, bLoadGame ? 1 : 0 );
}

void SourceSharp_OnClientGamePrecache()
{
	SOURCESHARP_TRACE();
	if ( g_bSourceSharpLoaded && ManagedClients() )
		ManagedClients()->OnClientGamePrecache();
}

#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// SourceSharp_ClientEarlyInit() STOOD HERE, AND IT IS GONE WITH THE ASYMMETRY
// IT EXISTED TO PAPER OVER.
//
// It was the client's whole route to the CLR: a void free function that called
// a file-static game system's Init(), called from CShellClientDLL::Init ahead
// of gHUD.Init() because the HUD hooks user messages C# declares. Void, so its
// answer was thrown away -- which is the reason managed_init_failure.h had to
// make a failed init FATAL rather than a returned false.
//
// The ordering it enforced is unchanged and is now enforced by a call that has
// a return value and an owner: CShellClientDLL::Init does
// `m_pManagedRuntime = LoadManagedRuntime( this )` at exactly this point, and
// returns false if it answers NULL.
//
// Its second caller was `src/game/client/cdll_client_int.cpp` under
// `#ifdef SOURCESHARP` -- a file client_csharp.vpc does not compile and a macro
// the vpcs that DO compile it do not define, so that block had been dead in
// every configuration since CShellClientDLL took over
// CLIENT_DLL_INTERFACE_VERSION. It is deleted with this, which is one
// `#ifdef SOURCESHARP` off a stock file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE SERVER'S SHIM AREAS (plan_engineapi_vtables Phase C, tools/shim-areas).
// Here, at the end, because their methods forward to the bodies the struct is
// filled with, and several of those are static to this file.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
#include "ss_string.h"
#include "shell_shim_areas.h"
#include "shell_shim_areas.inc"
#else
// plan_client_vtables CB, CC: the client's shim areas, forwarding to the bodies
// docs/engineapi-areas.tsv names (tools/shim-areas).
#include "ss_string.h"
#include "shell_shim_areas.h"
#include "shell_shim_areas_client.inc"
#endif
