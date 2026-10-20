//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — who hears it ========================================//
// SS_REASON a  a vtable the engine calls through
//
// game/server/recipientfilter.cpp, moved here whole by plan_entity.md step 4b.
//
// WHY IT IS A SHIM FILE AND NOT STOCK. A recipient filter answers one question
// -- "which connected clients does this message reach" -- and its whole state
// is a list of PLAYER SLOTS. It decides nothing about the game; it is the
// mechanism a decision made in C# is delivered through, which is §6's own test
// for what may stay native. Nothing in it constructs, owns or names an entity
// any more: the four remaining bodies that took a CBaseEntity * are the
// soundscript constructors stock callers still spell, and the HANDLE-shaped
// constructors beside them are what this port's own code uses.
//
// WHAT LEFT WITH THE MOVE, and none of it was reachable:
//
//   * the `#ifndef SS_CBASEENTITY_ALIAS` prediction-suppression block in
//     AddRecipientByPlayerIndex, and the ToBasePlayer cull in
//     UsePredictionRules. Both read IPredictionSystem's suppress host, which is
//     NULL on every path in this build -- physics_main.cpp's writer is inside
//     `#ifndef SOURCESHARP`, player.cpp is not in the vpc at all, and
//     takedamageinfo.cpp's ApplyMultiDamage only saves and restores whatever it
//     read. suppress_host_dead_test.cpp holds that as a fact over the compiled
//     TU list, so the day a writer appears the test is red and names these two
//     sites rather than leaving them silently missing.
//   * the SDK tail of CPASAttenuationFilter::Filter. Every recipient index
//     names a connected client and every connected client is a standalone
//     player, so the arm above it had already `continue`d for all of them.
//     (The class itself went at plan_server_native_v2 T5: nothing built one.)
//
// SIX §3.16 CAST SITES WENT WITH THOSE THREE BLOCKS: two C-style casts to a
// base entity pointer, two ToBasePlayer calls and two uses of the base
// entity's static Instance. They are spelled in prose here on purpose --
// tools/entity-census counts SPELLINGS over the file's text, so a comment
// quoting a cast is a cast as far as the gate is concerned, and a shim file
// that quotes what it deleted has not removed it from the count.
//
//=============================================================================//
#include "ss_util.h"            // engine, ss::INDEXENT

// The engine's globals (connected in shell_gamedll.cpp); cbase.h supplied them.
extern CGlobalVars *gpGlobals;
#include "ss_recipient_filter.h"

#include <algorithm>
// team.h: removed with team.cpp, which is out of the build entirely
// (server_csharp.vpc). Nothing here had named a CTeam since
// CTeamRecipientFilter's constructor went -- see the header's note and the
// deleted-constructor marker further down -- so this was an include kept alive
// by a class that had already left.
#include "shell_standalone_player.h"

// g_RecipientFilterPredictionSystem WAS HERE: a bare IPredictionSystem whose
// one effect was to join g_pPredictionSystems at static init. Nothing on the
// server walks that list (nm over every server object: no SuppressEvents,
// SuppressHostEvents or CDisablePredictionFiltering), and nothing here read it
// after the prediction rules above went, so it went too (P5 lane SP).

void ss::CRecipientFilter::AddAllPlayers( void )
{
	m_Recipients.clear();

	// By slot, not by UTIL_PlayerByIndex: that helper answers only for players
	// on the SDK hierarchy, and under SourceSharp the players are not. A slot
	// in 1..maxClients holds a player edict or nothing — the engine reserves
	// those indices — so a non-free edict there IS a connected player,
	// whichever hierarchy its entity lives on.
	int i;
	for ( i = 1; i <= gpGlobals->maxClients; i++ )
	{
		edict_t *pEdict = ss::INDEXENT( i );
		if ( !pEdict || pEdict->IsFree() || !pEdict->GetUnknown() )
			continue;

		AddRecipientByPlayerIndex( i );
	}
}

void ss::CRecipientFilter::AddRecipientsByPVS( const Vector& origin )
{
	if ( gpGlobals->maxClients == 1 )
	{
		AddAllPlayers();
	}
	else
	{
		CBitVec< ABSOLUTE_PLAYER_LIMIT > playerbits;
		engine->Message_DetermineMulticastRecipients( false, origin, playerbits );
		AddPlayersFromBitMask( playerbits );
	}
}

void ss::CRecipientFilter::AddRecipientsByPAS( const Vector& origin )
{
	if ( gpGlobals->maxClients == 1 )
	{
		AddAllPlayers();
	}
	else
	{
		CBitVec< ABSOLUTE_PLAYER_LIMIT > playerbits;
		engine->Message_DetermineMulticastRecipients( true, origin, playerbits );
		AddPlayersFromBitMask( playerbits );
	}
}

// CTeamRecipientFilter's constructor was here; deleted, see the header's note.

// THE HANDLE-SHAPED CONSTRUCTOR WENT (plan_server_native_v2 T4a) with its one
// caller, shell_sound.cpp's entity emit: ServerSounds.EntityEmitSound builds
// the same filter in C#, around the entity's centre at the script's level.

// RecipientFilter_RemoveIfNotCloseCaptioning WENT (plan_server_native_v2 T4a)
// with sound_emitter.cpp's server caption, its one importer: the rule is
// SoundRecipients.RemoveIfNotCloseCaptioning (C#), closecaption_recipient.h's
// "" versus "0" distinction included.
