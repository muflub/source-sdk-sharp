//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// SOURCESHARP: RELOCATED AND EMPTIED OF EVERYTHING WITH NO CALLER.
// plan_shim.md Phase 2c.
// REASON (d), AND THE PLAN ITSELF CALLS IT WEAK. What keeps this native is not
// a vtable, an interface name or a wire format: it is that a `string_t` IS AN
// ADDRESS. Every string_t field the engine block carries, and every one a
// native caller passes across, is a pointer into this pool, and the pool is
// what makes two spellings of one name compare equal by pointer. A managed
// intern table could hold the strings and could not hand out the addresses the
// native residue already has in its hands.
// SO IT IS A LEAF ALLOCATOR FOR THE NATIVE RESIDUE AND NOTHING ELSE. No
// managed code needs it -- C# has its own strings -- and when the last native
// AllocPooledString caller goes, so does this file. The five that are left,
// measured over the debug link line with tools/shim-census (118 server objects
// on the graph, 118 on disk, 0 ghosts):
//   server  mapentities.o  shell_gamerules.o  shell_soundenvelope.o
//           shell_standalone_player.o  world_adapter.o      AllocPooledString
//           shell_serversystems.o                           GameStringSystem
//   client  shell_gamerules.o  soundenvelope.o (shell_propdata_system.o left, C9)
//           shell_clientsystems.o                           GameStringSystem
// WHAT WENT, AND THE PROOF IT COULD. Four exports had ZERO importers on either
// side -- joined per symbol, `nm -C --defined-only` over gamestringpool.o
// against `nm -C -u` over every other object on each link line, symbol type
// filtered [TWDBRV]:
//   FindPooledString                                0 server  0 client
//   RemovePooledString                              0        0
//   PurgeDeferredPooledStrings                      0        0
//   AllocPooledString_StaticConstantStringPointer   0        0
// and with them the two data structures that existed only to serve them:
//   m_DeferredDeleteList  written ONLY by Remove(), which was RemovePooledString's
//                         body. With no writer the list is always empty, so
//                         PurgeDeferredDeleteList freed nothing and
//                         PurgeDeferredPooledStrings was a public entry point
//                         onto an empty vector.
//   m_KeyLookupCache      read and written ONLY by AllocateWithKey(), which was
//                         AllocPooledString_StaticConstantStringPointer's body.
//                         A CUtlHashtable per level, for a fast path nothing
//                         took.
// THE HEADER IS NOT EDITED, and that is a deliberate difference from what this
// same lane did to game/server/game.h an hour earlier. game.h's seventeen
// extern ConVars had no user anywhere in the tree once game.cpp stopped being
// compiled, so a declaration with no definition was pure trap. These four are
// stock API with about ten callers still ON DISK -- ai_basenpc_squad.cpp,
// filters.cpp, ai_looktarget.cpp, baseentity.cpp, the econ cluster,
// teamplayroundbased_gamerules.cpp -- none of them compiled, all of them a
// future lane's to re-enable or evict. Deleting the declarations would turn
// "this build does not link that" into "that API does not exist", which is a
// bigger claim than the measurement supports. The header's AssertIsValidString
// macro expands to FindPooledString and is in the same position.
// THE ConCommand STAYS NATIVE FOR NOW, and it is the one thing plan_shim.md 2c
// asked for that this commit does not do. `dumpgamestringtable` prints the
// pool's contents, and the pool is here; a managed [ConCommand] would need a
// new engine-API slot to ask for the dump. Appending one is allowed and is
// three lines of native -- but it is six files, four of them (Abi.cs, Engine.cs,
// the engine-area interface, RecordingEngine) shared with every other lane in
// flight. It is recorded as a Phase 5 ledger row instead: the ledger is the
// place that decides which ConCommands this mod ships, and it is one serialised
// lane rather than five concurrent merges.

// step 7h: this file is compiled into BOTH DLLs, and the two sides have
// DIFFERENT cbase.h files. The client half of the step is done here -- the
// client TU names what it uses and is opted out of the client precompiled
// header in client_csharp.vpc. The server half belongs to step 4h ("the
// entity-free fifteen"), which has not run; until it does the server keeps its
// own prelude, and the #else arm below is what it still sees. Two of these
// headers (cdll_util.h, icliententityinternal.h) are under game/client, which
// is not on the server include path at all, so the guard is not tidiness.
#ifdef CLIENT_DLL
#include "string_t.h"
#include "client_class.h"
#include "icliententityinternal.h"
#include "shell_clientsystem.h"	// plan_cworld_trace.md T4
#else
// The server half is on public/ headers and server.so's own: the two
// accessors it defines come from ss_gamestringpool.h, and the two the debug
// dump reads from gamestringpool_internal.h.
#include "shell_serversystem.h"	// plan_gamedll.md Phase 7a / T1
#include "ss_gamestringpool.h"
#include "gamestringpool_internal.h"
#include "tier0/dbg.h"
#include <cstddef>
#endif

#ifdef CLIENT_DLL
#include "igamesystem.h"
#include "gamestringpool.h"
#endif

// Both sides' storage (C9: the client's was tier1's CStringPool, which took
// stringpool.o and generichash.o onto client.so's link for this one class).
#include "server_string_pool.h"

//-----------------------------------------------------------------------------
// Purpose: The actual storage for pooled per-level strings
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
class CGameStringPool : public CServerStringPool,	public CClientSystem
#else
class CGameStringPool : public CServerStringPool,	public CServerSystem
#endif
{
	virtual char const *Name() { return "CGameStringPool"; }

	virtual void LevelShutdownPostEntity()
	{
		Cleanup();
	}

public:
	~CGameStringPool()
	{
		Cleanup();
	}

	// FreeAll and nothing else now. The deferred-delete list and the key cache
	// this also purged had no writer left -- see the banner -- so their purges
	// were two calls that freed nothing.
	void Cleanup()
	{
		FreeAll();
	}

#ifndef CLIENT_DLL
	// Every pooled string, in order. What was Dump() walks the pool through
	// this from gamestringpool_debug.cpp; see GameStringPool_Visit below.
	void Visit( GameStringPoolVisitFn pfnVisit, void *pContext ) const
	{
		for ( Set::const_iterator it = m_Strings.begin(); it != m_Strings.end(); ++it )
		{
			pfnVisit( it->c_str(), pContext );
		}
	}
#endif
};

static CGameStringPool g_GameStringPool;

//-----------------------------------------------------------------------------
// String system accessor
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
CClientSystem *GameStringSystem()
#else
CServerSystem *GameStringSystem()
#endif
{
	return &g_GameStringPool;
}


//-----------------------------------------------------------------------------
// Purpose: The public accessor for the level-global pooled strings
//-----------------------------------------------------------------------------
string_t AllocPooledString( const char * pszValue )
{
	if (pszValue && *pszValue)
		return MAKE_STRING( g_GameStringPool.Allocate( pszValue ) );
	return NULL_STRING;
}

#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// The pool, read-only, for dumpgamestringtable -- a diagnostic that lives in
// gamestringpool_debug.cpp, which only a debug build compiles. Two functions
// rather than the pool itself: g_GameStringPool stays this file's.
//-----------------------------------------------------------------------------
void GameStringPool_Visit( GameStringPoolVisitFn pfnVisit, void *pContext )
{
	g_GameStringPool.Visit( pfnVisit, pContext );
}

int GameStringPool_Count( void )
{
	return g_GameStringPool.Count();
}
#endif
