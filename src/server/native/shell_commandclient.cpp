//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — who typed the command ===============================//
// SS_REASON b  an interface resolved by name at load
//
// plan_gamedll_after.md Phase A4, subject 3. UTIL_GetCommandClientIndex,
// UTIL_GetCommandClient, UTIL_PlayerActivatorBySlot and
// UTIL_GetCommandClientActivator were free functions in gameinterface.cpp
// (:302-365), reading a file-scope int that CServerGameClients::SetCommandClient
// wrote 3,200 lines further down the same file.
//
// WHY THIS IS A SOURCESHARP FILE RATHER THAN util.cpp, where the other two
// hundred UTIL_ functions live. Two of the four are this port's own:
// UTIL_PlayerActivatorBySlot exists because the standalone player is not a
// CBasePlayer and UTIL_PlayerByIndex answers NULL for it -- indistinguishable
// from an empty slot -- and UTIL_GetCommandClientActivator is the console
// client through that same fix. Putting them in a stock file would put an
// #ifdef SOURCESHARP arm in one. UTIL_GetModDir, which came out of the same
// run of gameinterface.cpp and has none of that, went to util.cpp instead.
//
// The declarations stay in util.h. No caller changed.
//
//=============================================================================//

#include "edict.h"
#include "eiface.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;
#include "shell_standalone_player.h"


//-----------------------------------------------------------------------------
// WHO TYPED THE COMMAND -- the slot, as a field.
//
// gameinterface.cpp held it as `static int g_nCommandClientIndex = 0;` at file
// scope (:259) and CServerGameClients::SetCommandClient assigned it directly.
// tools/statics-audit gates a file-scope static out of src/sourcesharp, so the
// slot is a field and the assignment became a call. UTIL_SetCommandClientIndex
// is declared in util.h beside the getter it pairs with; it is the ONE new
// symbol this move adds and it exists because the writer and the reader are now
// in different files.
//
// THE VALUE IS STOCK'S, INCLUDING ITS OFF-BY-ONE. The engine hands
// SetCommandClient a 0-based slot and -1 for the dedicated console;
// UTIL_GetCommandClientIndex adds one. Nothing here changed that, and the
// initial 0 -- which reads back as player 1 before the engine has said anything
// -- is stock's too.
//-----------------------------------------------------------------------------
class CSourceSharpCommandClient
{
public:
	CSourceSharpCommandClient() : m_nSlot( 0 ) {}

	int  Slot() const        { return m_nSlot; }
	void SetSlot( int nSlot ) { m_nSlot = nSlot; }

private:
	int m_nSlot;
};

static CSourceSharpCommandClient &CommandClient()
{
	static CSourceSharpCommandClient client;
	return client;
}

//-----------------------------------------------------------------------------
// Purpose: the engine naming the console client, straight from
//			IServerGameClients::SetCommandClient. Stock assigned the file-scope
//			int in place; this is the same assignment through the field.
//-----------------------------------------------------------------------------
void UTIL_SetCommandClientIndex( int index )
{
	CommandClient().SetSlot( index );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Output : int
//-----------------------------------------------------------------------------
int UTIL_GetCommandClientIndex( void )
{
	// -1 == unknown,dedicated server console
	// 0  == player 1

	// Convert to 1 based offset
	return (CommandClient().Slot()+1);
}

//--------------------------------------------------------------------------------------------------------------
// UTIL_IsCommandIssuedByServerAdmin -- MOVED HERE, WHOLE AND UNEDITED, from
// game/server/util.cpp by the Phase 4 exit lane.
//
// Stock's comment, kept: "Returns true if the command was issued by the
// listenserver host, or by the dedicated server, via rcon or the server
// console. This is valid during ConCommand execution."
//
// WHY IT IS HERE. Its body reads exactly one symbol,
// UTIL_GetCommandClientIndex, and that has been this file's since
// plan_gamedll_after.md Phase A4 -- so the function was the last thing keeping
// util.cpp on the other side of a link edge from its own helper. It was also
// util.cpp's LAST tie to game/server/globalstate.cpp and
// game/shared/gamestringpool.cpp: each of those two owed util.cpp exactly one
// symbol and it was this one. With the body here neither owes util.cpp
// anything, and both leave the stock link graph's single cycle -- which is the
// Phase 4 exit's atomic batch. Measured by tools/p4-exit-batches, pinned by
// src/sourcesharp/test/admin_command_move_test.cpp.
//
// ONE RESPECT IN WHICH THE MOVED TEXT IS NOT STOCK'S TEXT, and it is the same
// one 4e and 7f each hit: a guard that was fine inside a stock file is DEAD
// CODE once the code is the shim's. util.cpp's body opened its replay test with
//
//     #if defined( REPLAY_ENABLED )
//         player_info_t pi;
//         bool bPlayerIsReplay = engine->GetPlayerInfo( 1, &pi ) && pi.isreplay;
//     #else
//         bool bPlayerIsReplay = false;
//     #endif
//
// and REPLAY_ENABLED is on neither link line
// (`grep -oP '(?<=-D)[A-Za-z0-9_]+' src/_vpc_/ninja/sdk_csharp_debug.ninja`
// lists it nowhere). `tools/dead-conditionals` reports a shim file carrying
// such a guard as a PROBLEM, and its ledger's own rule is that the pending list
// can only SHRINK -- so ledgering a site this move CREATED would be the wrong
// half of that mechanism. The arm this build takes was kept and the other
// deleted, on the tool's own instruction and following 4e's two deletions and
// 7f's three at the same gate.
//
// SAID PLAINLY RATHER THAN BURIED: if REPLAY_ENABLED were ever defined, stock
// would let a replay client at slot 2 count as an admin and this will not. The
// arm was unreachable before the move as well as after it, `make live-gate` has
// no replay coverage (plan_entity.md §5 says so), and 0e's two engine
// deref sites for GetPlayerState are hltv and replay -- so this is a real, if
// hypothetical, loss of stock fidelity and not a tidy-up. It belongs in the
// same list as 4e's EntityTracer_ComputeStartPosition parameter.
//
// AND THE HYPOTHETICAL IS NOW CLOSED -- plan_shim.md step 5c. HLTV and Replay
// are DROPPED from this mod as a decision, so the `false` local went too and
// the function tests the player index once. "Stock would have done otherwise"
// is no longer a loss to record against a feature we might restore; it is what
// dropping the feature means. The launch line enforces it (-nohltv and
// tv_enable 0, see the Makefile and tools/live-gate), so slot 2 cannot be an
// HLTV or Replay master on any path this build takes.
//--------------------------------------------------------------------------------------------------------------
bool UTIL_IsCommandIssuedByServerAdmin( void )
{
	int issuingPlayerIndex = UTIL_GetCommandClientIndex();

	if ( engine->IsDedicatedServer() && issuingPlayerIndex > 0 )
		return false;

	// BOTH HALVES OF THE REPLAY TEST ARE GONE, not just stock's guarded arm.
	// See the note above the function: this mod does not ship Replay, so slot 2
	// is never a replay client and "not slot 1" is the whole question.
	if ( issuingPlayerIndex > 1 )
	{
		return false;
	}

	return true;
}
