//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// THE RESTORE TRIO, AND THE ONE-WAY TRANSITION FLAG.
//
// plan_gamedll_after.md Phase A9 step 4, out of game/server/gameinterface.cpp,
// which this phase deletes. Four free functions that were interleaved with
// stock's CServerGameDLL and are not part of it:
//
//   AddRestoredEntity      six callers, all in game/shared/saverestore.cpp
//   BeginRestoreEntities   no caller left; its caller was stock's LevelInit
//   EndRestoreEntities     ONE caller, CServerGameClients::ClientActive
//   Game_SetOneWayTransition  two callers, world.cpp and world_adapter.cpp
//
// AND `nm` SAID EndRestoreEntities HAD ZERO CONSUMERS, WHICH WAS TRUE AND
// MISLEADING. Its one caller was in the same translation unit, so the reference
// never left gameinterface.o and no linker could see it. A9 step 2 split the
// pair into two objects and the call became a real undefined reference. A
// consumer count read off object tables is a FLOOR, not a total: it cannot see
// a call that never crossed a file boundary.
//
// WHAT THIS PATH DOES IN THIS BUILD, said once. Save and restore are refused
// wholesale: CShellGameDll answers fifteen save-cluster methods with
// ShellGameDll_Unreachable, and the engine's own reply to `save` is "Can't save
// multiplayer games". BeginRestoreEntities is what sets the flag the other two
// test, and nothing calls it, so AddRestoredEntity's list is always empty and
// EndRestoreEntities returns at its first line. The bodies are kept whole
// anyway, because the day a managed save/load lands this is the shape it has to
// come back in, and a body deleted is a body somebody has to re-derive.
//
// THE ENGINE'S REPLY IS A PROPERTY OF THE LAUNCH -- measured 2026-09-02,
// docs/plan-audit.md D16. "Can't save multiplayer games" is the answer at a
// DEFAULT launch; the gate is `sv.IsMultiplayer()` and this mod's own
// GetPlayerLimits answers `minplayers = 1`, so a server launched
// `-maxplayers 1` reaches the save cluster and counts its refusals. What still
// makes this path dead is narrower and is now a number: stock's entity save
// walks gEntList.NextEnt, which yields 0 of the 473 entities on a loaded
// dm_lockdown, because none of them is in the SDK's hierarchy any more.
//
// NO FILE-SCOPE STATE. Stock kept three file statics here -- g_RestoredEntities,
// g_InRestore and g_OneWayTransition. tools/statics-audit gates that shape out
// of src/sourcesharp, so they are fields of the two small classes below.
//
//===========================================================================//

#include "basehandle.h"
#include "igameentity.h"
#include "tier0/dbg.h"
#include "entity_shared_statics.h"
#include "shell_resolve.h"

#include <vector>


//-----------------------------------------------------------------------------
// The restore list and the in-restore flag, as fields.
//
// IsInRestore() is the Assert's subject and Begin/End's flag; stock had it as
// `static bool g_InRestore` with the comment "just for debugging, assert that
// this is the only time this function is called", which is exactly what it is
// still for.
//-----------------------------------------------------------------------------
class CSourceSharpRestoredEntities
{
public:
	bool IsInRestore() const           { return m_bInRestore; }
	void SetInRestore( bool bIn )      { m_bInRestore = bIn; }

	std::vector< CBaseHandle > &List()  { return m_Restored; }

private:
	std::vector< CBaseHandle >	m_Restored;
	bool					m_bInRestore = false;
};

//-----------------------------------------------------------------------------
// The one-way transition flag.
//
// ITS READER LEFT THE BUILD AND THE SETTER DID NOT. Stock read this in
// CServerGameDLL::LevelInit's loadGame arm, where a true meant
// engine->ClearSaveDirAfterClientLoad(); Phase A9 step 3 deleted that class,
// and the loadGame arm was refused long before that. The two CALLERS are live
// (world.cpp's CChangeLevel and world_adapter.cpp's), so the symbol has to
// exist -- which is the whole reason this class does.
//
// The flag is kept rather than the function emptied, so that a managed
// save/restore has something to read and so that the setter is not a lie about
// what it does. Nothing reads it today and IsSet() says so at its site.
//-----------------------------------------------------------------------------
class CSourceSharpOneWayTransition
{
public:
	void Set()                         { m_bOneWay = true; }

	// NO CALLER TODAY, deliberately, and named rather than left off: the
	// reader that used to exist was stock's LevelInit and it is gone. A
	// managed level load is where this comes back.
	bool IsSet() const                 { return m_bOneWay; }

private:
	bool					m_bOneWay = false;
};

namespace
{
	CSourceSharpRestoredEntities s_Restored;
	CSourceSharpOneWayTransition s_OneWay;
}

void Game_SetOneWayTransition( void )
{
	s_OneWay.Set();
}

void EndRestoreEntities()
{
	if ( !s_Restored.IsInRestore() )
		return;

	// The entire hierarchy is restored, so we can call GetAbsOrigin again.
	//CBaseEntity::SetAbsQueriesValid( true );

	// Call all entities' OnRestore handlers
	for ( int i = (int)s_Restored.List().size()-1; i >=0; --i )
	{
	// The server's half of the same pair as cdll_client_int.cpp's
	// DispatchOnRestore. IsDormant and OnRestore are both CBaseEntity members and
	// neither is one of IGameEntity's six; a managed entity's restore is C# work.
	}

	s_Restored.List().clear();


	s_Restored.SetInRestore( false );
	// THE SDK HALF of the two delete lists. Armed there is no such queue: the
	// list and every remove that fed it leave with the stock list's own
	// translation unit, and the parallel hierarchy's drain -- paired ahead of
	// this call in the managed binding -- is the whole drain. The body is kept
	// whole rather than deleted for the reason the file's banner gives: a
	// managed save/load comes back in this shape.

	// STOCK RE-RAN THE WHOLE OF ServerActivate HERE, on its own object:
	// `g_ServerGameDLL.ServerActivate( NULL, 0, 0 )`, under its own comment
	// "HACKHACK: UNDONE: We need to redesign the main loop with respect to
	// save/load/server activate".
	//
	// That object was deleted at plan_gamedll_after.md Phase A9 step 3, and the
	// call is deliberately NOT re-pointed at CShellGameDll. Reaching this line
	// requires BeginRestoreEntities to have run, which requires a save-game
	// load, which CShellGameDll refuses -- fifteen save-cluster methods answered
	// with ShellGameDll_Unreachable. Re-pointing it would be writing a call
	// nothing can make, and would give the restore path the appearance of
	// working.
	//
	// THE OLD SENTENCE HERE SAID THE ENGINE MAKES THAT IMPOSSIBLE, and D16
	// measured that it does not: "Can't save multiplayer games" is the reply at
	// a DEFAULT launch, and a server launched `-maxplayers 1` reaches the
	// cluster (docs/plan-audit.md §3.15). BeginRestoreEntities still has no
	// caller, so this line is still unreachable -- but by ONE fact now instead
	// of two, and the remaining one is in this binary rather than in the engine.
	//
	// Loud rather than silent, for the reason the refusals are loud: if this
	// ever prints, the paragraph above is wrong.
	Warning( "[SourceSharp] EndRestoreEntities reached its tail -- a save game "
	         "was restored, which this build refuses. The re-activation stock "
	         "ran here went with stock's game DLL class (plan_gamedll_after.md "
	         "A9 step 3).\n" );
	EntityPrecache_SetAllowed( false );
}
