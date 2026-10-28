//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// Recipient filters: the set of player slots a message reaches.

#include "ss_util.h"

extern CGlobalVars *gpGlobals;
#include "ss_recipient_filter.h"

#include <algorithm>
#include "shell_standalone_player.h"

void ss::CRecipientFilter::AddAllPlayers()
{
	m_Recipients.clear();

	// By slot: a non-free edict in 1..maxClients is a connected player, whichever
	// hierarchy its entity is on.
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
