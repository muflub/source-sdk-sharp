//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// THE WORLD'S SOUND LIST, WHICH IS A SHIM -- plan_gamedll_after.md Phase A3.
//
// CSoundEnt was row 14 of kServerSystems[] and `game/server/soundent.cpp` was
// in server_csharp.vpc. Neither is true any more: the pool, the free and active
// lists, the expiry sweep, the channel reuse and the client reserve are
// `SoundEnt` in SourceSharp.Server, an IFrameLifecycle participant at
// FrameStage.SoundEnt. This file is the five entry points native code still
// calls, as ss_soundent.h spells them.
//
//=============================================================================
// WHY ss:: AND NOT CSoundEnt:: (P5 lane SP).
//
// These were CSoundEnt's statics, kept under stock's spelling for callers this
// port did not own. Every caller now spells ss_soundent.h's names
// (world_adapter, shell_level, shell_gameclients, shell_absent), nm found no
// server object importing a CSoundEnt static, and the class's last trace left:
// this file includes neither cbase.h nor soundent.h, and compiles on the R5
// include set.
//
//=============================================================================
// WHY soundent.cpp LEFT THE BUILD RATHER THAN GROWING #ifdefs.
//
// Different reason from decals.cpp's, and worth stating because the shape is
// the same and the argument is not. soundent.cpp is SERVER-ONLY, so there is no
// T1 hazard here at all -- nothing about this file compiles into client.so and
// client_shared_parity.sh has never had it in scope. What it left for is the
// simpler reason: with the pool managed, the file's every line is either the
// pool or a static that now forwards, so keeping it would mean a second copy of
// the data structure that nothing writes to.
//
// AND soundent.cpp IS UNTOUCHED, byte for byte; soundent.h took exactly ONE
// LINE, the ResetClientSound declaration below. This is decals.cpp's precedent
// applied: the removal is a line in server_csharp.vpc, so `server_base.vpc`'s
// stock hl2mp/tf projects -- which this build does not compile, but which still
// list both files -- keep a pair that compiles. A static declared there and
// never called there links fine.
//
//=============================================================================
// ONE THING STOCK DOES THAT THIS DOES NOT, and it is NOT hidden in a table.
//
// `displaysoundlist` draws three axis-aligned crosses per live sound through
// NDebugOverlay::Line, sized by the sound's volume, plus a second cross for the
// occluded volume. The managed sweep reproduces every LINE the convar prints --
// the add, the remove, the danger variants and the `Soundlist: n / m (d)`
// counts -- and draws nothing, because there is no managed debug-overlay engine
// area: IEngineEffects has beams, blood and temp entities and no line.
//
// BLOCKER: a managed NDebugOverlay area. One slot -- AddLineOverlay -- on
// IEngineEffects. It was not added here because an engine slot for a developer
// convar's 3D picture is ABI surface this phase did not need for anything else,
// and the next port that wants a debug line will want the same slot.
//
//=============================================================================
// NO FILE-SCOPE STATE. This file holds none at all: every function below is a
// forward, and the one piece of state the old file had -- g_pSoundEnt, the
// "is the pool up" gate -- is a field on the managed service, which is where
// the pool it gates now lives.
//
//=============================================================================

#include "ss_util.h"            // ss::ENTINDEX
#include "ss_soundent.h"

// The engine's globals (connected in shell_gamedll.cpp); cbase.h supplied them.
extern CGlobalVars *gpGlobals;
#include "sourcesharp.h"
#include "shell_server.h"
#include "shell_resolve.h"


#include "imanaged_areas.h"   // plan_engineapi_vtables E7: IManagedSoundEnt

//-----------------------------------------------------------------------------
// Turning the list on.
//
// KEEPS ITS SIGNATURE AND ITS RETURN, which is the whole point: its only caller
// in this build is world_adapter.cpp, which belongs to another lane, and a shim
// that changed the spelling would have required an edit there.
//
// maxClients is read HERE rather than in C#. It decides the pool size, and a
// service that reads the value it is sized by cannot be tested at two sizes --
// so it crosses as an argument and the managed side has no globals read at all
// on this path.
//-----------------------------------------------------------------------------
bool ss::SoundEnt_Init()
{
	// plan_engineapi_vtables E7: the managed-built sound-hint area.
	IManagedSoundEnt *pApi = ManagedSoundEnt();

	if ( pApi == NULL )
		return true;

	pApi->ServerSoundEntInit( gpGlobals->maxClients, gpGlobals->curtime );
	return true;
}

//-----------------------------------------------------------------------------
// Turning it off. Called twice on the way out of a level -- gameinterface.cpp's
// own teardown and shell_level.cpp's -- and stock's was idempotent for the
// same reason, so this one is too.
//-----------------------------------------------------------------------------
void ss::SoundEnt_Shutdown()
{
	// plan_engineapi_vtables E7: the managed-built sound-hint area.
	IManagedSoundEnt *pApi = ManagedSoundEnt();

	if ( pApi == NULL )
		return;

	pApi->ServerSoundEntShutdown();
}

//-----------------------------------------------------------------------------
// ss::SoundEnt_InsertSound WENT (plan_server_native_v2 T5): no server object
// called it (nm U over the link edge). Sound hints enter the managed pool from
// managed code (SoundEnt.InsertSound), which needs no native crossing.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// A client is leaving: clear its reserved slot.
//
// STOCK DID THIS THROUGH A CSound *, which is the one reason the pool had to be
// reachable as memory from C++ at all:
//
//     CSound *pSound = CSoundEnt::SoundPointerForIndex(
//                          CSoundEnt::ClientSoundIndex( pEdict ) );
//     if ( pSound ) pSound->Reset();
//
// Two statics and a member write, for one field triple. It is one call now, and
// gameinterface.cpp spells THIS instead -- the only caller edit this system
// needed, and it was needed because handing a pointer into a managed array is
// the thing a port must not do.
//
// The bounds test that used to be SoundPointerForIndex's -- index < 0, index
// past the pool -- is the managed side's, and answers the same nothing. Stock
// printed a message for both; this does not, because stock's message named a
// function that no longer exists.
//-----------------------------------------------------------------------------
void ss::SoundEnt_ResetClientSound( int iIndex )
{
	// plan_engineapi_vtables E7: the managed-built sound-hint area.
	IManagedSoundEnt *pApi = ManagedSoundEnt();

	if ( pApi == NULL )
		return;

	pApi->ServerSoundEntResetClientSound( iIndex );
}

//-----------------------------------------------------------------------------
// Clients are numbered 1..maxClients and their reserved slots 0..maxClients-1.
//
// STAYS NATIVE, and it is worth one line: this is arithmetic on an edict, it
// touches no list, and sending it across the boundary would be a crossing to
// compute a subtraction. Stock's _DEBUG range warning goes with it.
//-----------------------------------------------------------------------------
int ss::SoundEnt_ClientSoundIndex( edict_t *pClient )
{
	int iReturn = ss::ENTINDEX( pClient ) - 1;

#ifdef _DEBUG
	if ( iReturn < 0 || iReturn >= gpGlobals->maxClients )
	{
		Msg( "** ClientSoundIndex returning a bogus value! **\n" );
	}
#endif // _DEBUG

	return iReturn;
}
