//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — server shell ========================================//
// SS_REASON b  an interface resolved by name at load
//
// Server-side symbols server_base.vpc expects a mod to supply.
//
// The mechanical ones are plain instantiations. The ones that represent actual
// game decisions — game description, player connect/spawn, per-frame tick —
// route into C#, which is where mod logic is meant to live. See plan.md Part 8.
//
//=============================================================================//

#include "shell_gamerules.h"
#include "shell_standalone_player.h"
#include "shell_diagnostics.h"
#include "eiface.h"
// GetPlayerLimits, below, is a method of it (plan_entity.md step 4g moved the
// declaration out of gameinterface.h).
#include "shell_gameclients.h"
#include "shell_serversystem.h"
#include "shell_server.h"

#include "precache_model.h"
#include "ss_shareddefs.h"


//-----------------------------------------------------------------------------
// Game rules live in shared/shell_gamerules.cpp — they must be a shared,
// networked class registered with REGISTER_GAMERULES_CLASS so the engine can
// look them up by name and replicate them to clients.
//-----------------------------------------------------------------------------
void InstallGameRules()
{
	SOURCESHARP_TRACE();
	// Was CreateGameRulesObject( "CSourceSharpGameRules" ), a lookup in a
	// registry that had one entry. See shell_gamerules.cpp.
	SourceSharp_CreateGameRules();
}

//-----------------------------------------------------------------------------
// Client lifecycle. These are the hooks a mod actually cares about, so each
// notifies C#.
//-----------------------------------------------------------------------------
void ClientPutInServer( edict_t *pEntity, const char *playername )
{
	// The engine hands us an edict and expects the mod to attach a player
	// entity to it. Nothing spawns without this.
	SOURCESHARP_TRACE_MSG( playername );

	// The standalone player is THE player (Phase 13c — the 9a fork retired
	// with the -ss_player flag). Create only fails when the managed side never
	// registered the ss_player class, which is a load-time invariant of this
	// build — so fail loudly rather than strand the connect: a client admitted
	// with no player entity dies somewhere unrelated minutes later.
	if ( !StandalonePlayer_Create( pEntity, playername ) )
	{
		Error( "[SourceSharp] ClientPutInServer: managed side registered no player class — cannot put '%s' in server\n",
			playername );
		return;
	}

	SourceSharp_OnClientPutInServer( pEntity, playername );
}

void ClientActive( edict_t *pEdict, bool bLoadGame )
{
	SOURCESHARP_TRACE();

	// The standalone player spawns here, at the same moment stock's does: the
	// client is fully connected and the engine is ready to send it a world.
	// ClientPutInServer already failed fatally if the create didn't happen, so
	// a missing player here means the edict is not a client's at all.
	if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict ) )
		pStandalone->Spawn();

	SourceSharp_OnClientActive( pEdict, bLoadGame );
}

void ClientGamePrecache( void )
{
	SOURCESHARP_TRACE();
	// The engine requires the player model to be precached before anyone spawns.
	Precache_Model( "models/player/male_01.mdl" );

	SourceSharp_OnClientGamePrecache();
}

// InitBodyQue went (plan_server_native_min P1): a SOURCESHARP_STUB() body —
// the corpse pool is a managed decision (Corpses.cs), and the one caller
// called a no-op.

//-----------------------------------------------------------------------------
// Event log.
//
// No longer a CEventLog subclass. That base class existed to listen for
// player_connect, player_disconnect, player_death, player_team, player_activate
// and player_changename and write them to the server log — and an inert stub of
// it crashed the server, because registering a listener is what forces the
// engine to create an event's descriptor. With nothing listening,
// "player_connect" fired with a null descriptor and the engine segfaulted
// dereferencing it to read "userid".
//
// That requirement is now met from C#: shared/shell_gameevents.cpp subscribes to
// whatever the [GameEvent] handlers name, during LoadSide, which happens in
// DLLInit long before anyone can connect. So the descriptors exist for the same
// reason as before — something listens — while the behaviour lives in managed
// code instead of being inherited.
//
// The game system itself still has to exist and be non-NULL, hence this shell.
//-----------------------------------------------------------------------------
// CSourceSharpEventLog went (plan_server_native_min P1): its Init and
// Shutdown were SOURCESHARP_TRACE() and nothing else. The requirement it
// met -- a listener for every fired event, or the engine segfaults on a null
// descriptor -- is met from C# since the gameevents fold
// (shared/shell_gameevents.cpp subscribes during LoadSide in DLLInit).

//-----------------------------------------------------------------------------
// Ends the round, sending everyone to intermission.
//
// Routed through the gamerules rather than called directly so the base class
// runs its own end-of-round bookkeeping — scores, timers, the map change that
// follows — instead of the shell having to reimplement it.
//-----------------------------------------------------------------------------
void SourceSharp_EndRound( void )
{
	SOURCESHARP_TRACE();

	// IGameRules, not the class -- and this line is why the interface's method
	// list has EndMultiplayerGame on it. The sweep that built that list looked
	// for "g_pGameRules->" and "GameRules()->"; this call goes through a LOCAL,
	// so it was invisible to both and the compiler found it instead.
	IGameRules *pRules = g_pGameRules;	// GameRules()'s whole body (gamerules.h)
	if ( !pRules )
	{
		Warning( "[SourceSharp] EndRound with no game rules\n" );
		return;
	}

	pRules->EndMultiplayerGame();
}

//-----------------------------------------------------------------------------
// Engine-facing bits of CServerGameDLL / CServerGameClients that gameinterface
// leaves for the mod.
//-----------------------------------------------------------------------------
void CServerGameClients::GetPlayerLimits( int &minplayers, int &maxplayers, int &defaultMaxPlayers ) const
{
	SOURCESHARP_TRACE();
	minplayers = 1;
	maxplayers = ss::kMaxPlayers;	// shareddefs.h's MAX_PLAYERS
	defaultMaxPlayers = 16;
}

// CServerGameDLL::LevelInit_ParseAllEntities WAS HERE and went with the class
// at plan_gamedll_after.md Phase A9 step 3. It was a private hook of stock's
// game DLL, its only caller was stock's LevelInit, and its body was
// SOURCESHARP_STUB() -- so what is lost is a stub of a method of a class the
// engine could not reach. The mod entities it was a placeholder for are the
// managed entity layer's, which is not reached through this class at all.
