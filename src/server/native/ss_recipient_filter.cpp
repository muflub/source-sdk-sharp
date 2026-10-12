//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — ss_recipient_filter.h's list logic ==================//
// SS_REASON a  a vtable the engine calls through
//
// The part of the server's recipient filters that is a list of player slots
// and nothing else: copy, reset, the add and remove by slot, the bitmask adds
// the PVS/PAS queries feed. It needs only gpGlobals->maxClients, so it builds
// on the R5 include set and ss_recipient_filter_test holds it on its own. The
// engine-facing fills (all players, PVS, PAS, the sound-range cull, the
// closecaption cull) are shell_recipientfilter.cpp's.
//=============================================================================//

#include "ss_recipient_filter.h"

#include "edict.h"
#include "tier0/dbg.h"

#include <algorithm>

// The engine's globals (shell_gamedll.cpp connects it); only maxClients is read.
extern CGlobalVars *gpGlobals;


//-----------------------------------------------------------------------------
// Purpose: 
// Input  : src - 
//-----------------------------------------------------------------------------
void ss::CRecipientFilter::CopyFrom( const CRecipientFilter& src )
{
	m_bReliable = src.IsReliable();
	m_bInitMessage = src.IsInitMessage();

	m_bUsingPredictionRules = src.IsUsingPredictionRules();
	m_bIgnorePredictionCull = src.IgnorePredictionCull();

	int c = src.GetRecipientCount();
	for ( int i = 0; i < c; ++i )
	{
		m_Recipients.push_back( src.GetRecipientIndex( i ) );
	}
}



int ss::CRecipientFilter::GetRecipientIndex( int slot ) const
{
	if ( slot < 0 || slot >= GetRecipientCount() )
		return -1;

	return m_Recipients[ slot ];
}


//-----------------------------------------------------------------------------
// The add every other add funnels through. See the header: the filter stores
// indices and the engine addresses clients by index, so the CBasePlayer in the
// pointer-shaped adds was only ever a way to ask "which slot" — and a way that
// answers nothing for a player on the parallel hierarchy.
//-----------------------------------------------------------------------------
void ss::CRecipientFilter::AddRecipientByPlayerIndex( int playerindex )
{
	if ( playerindex < 1 || playerindex > gpGlobals->maxClients )
		return;

	// STOCK'S PREDICTION-SUPPRESSION BLOCK WAS HERE and it is gone, not stubbed.
	//
	// It read IPredictionSystem's suppress host and skipped the add when the
	// host was the slot being added -- "don't send the local player a sound he
	// already predicted". THE SUPPRESS HOST IS NULL ON EVERY PATH IN THIS
	// BUILD, so the block could never take its `return` and deleting it is
	// exact rather than a refusal. The three writers, checked one at a time:
	//
	//   game/server/physics_main.cpp:2205  inside `#ifndef SOURCESHARP`
	//   game/server/player.cpp:3376        the TU is not in server_csharp.vpc
	//   game/shared/takedamageinfo.cpp:320 writes back the value it just read,
	//                                      which is the NULL the constructor set
	//
	// suppress_host_dead_test.cpp holds that as a fact over the compiled TU
	// list rather than as prose here, so a fourth writer is a red test naming
	// this site and shell_tempents.cpp's, not a sound that quietly reaches one
	// client too many.
	//
	// It also took this function's last C-style cast to a base entity pointer:
	// the subject was the ROOT pointer, not a player, so there was no
	// IMovementPlayer face to ask it for a slot.

	// Already in list
	if ( std::find( m_Recipients.begin(), m_Recipients.end(), playerindex ) != m_Recipients.end() )
		return;

	m_Recipients.push_back( playerindex );
}



void ss::CRecipientFilter::RemoveRecipientByPlayerIndex( int playerindex )
{
	Assert( playerindex >= 1 && playerindex <= ABSOLUTE_PLAYER_LIMIT );

	{ auto it = std::find( m_Recipients.begin(), m_Recipients.end(), playerindex ); if ( it != m_Recipients.end() ) m_Recipients.erase( it ); }
}



// AddRecipientsByTeam, RemoveRecipientsByTeam and RemoveRecipientsNotOnTeam
// were here; all three deleted, see the header's note. The third took this
// file's last ask of CBaseEntity with it.

void ss::CRecipientFilter::AddPlayersFromBitMask( CBitVec< ABSOLUTE_PLAYER_LIMIT >& playerbits )
{
	// The engine set these bits, and it set them for CLIENTS — resolving each
	// one back to a CBasePlayer only re-asked a question the engine had already
	// answered, and answered "no such player" for every standalone one.
	int index = playerbits.FindNextSetBit( 0 );

	while ( index > -1 )
	{
		AddRecipientByPlayerIndex( index + 1 );

		index = playerbits.FindNextSetBit( index + 1 );
	}
}


