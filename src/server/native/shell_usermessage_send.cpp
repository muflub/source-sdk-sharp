//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// Sends a user message C# has encoded. One slot rather than Begin/Write/End
// crossings: the engine holds the filter pointer from UserMessageBegin until
// MessageEnd, so the filter must live in one native frame.

#include "ss_recipient_filter.h"
#include "usermessage_append.h"
#include "imanaged_areas.h"
#include "edict.h"
#include "eiface.h"

extern CGlobalVars *gpGlobals;
extern IVEngineServer *engine;

// The engine reserves 1..maxClients for players, so a non-free edict there is one.
static bool SourceSharp_IsPlayerSlotLive( int nPlayerIndex )
{
	if ( nPlayerIndex < 1 || nPlayerIndex > gpGlobals->maxClients )
		return false;

	edict_t *pEdict = engine->PEntityOfEntIndex( nPlayerIndex );
	return pEdict && !pEdict->IsFree() && pEdict->GetUnknown() != NULL;
}

static void SourceSharp_SendThrough( IRecipientFilter &filter, int32_t nMsgId,
                                     const unsigned char *pData, int32_t nBytes )
{
	bf_write *pBuf = engine->UserMessageBegin( &filter, nMsgId );
	if ( pBuf && nBytes > 0 )
		SourceSharp_AppendBits( pBuf, pData, nBytes * 8 );
	engine->MessageEnd();
}

// 1 when the engine was handed the message; 0 when nPlayerIndex names an empty
// slot (nothing is sent, so a message for one player never becomes a broadcast).
int32_t SourceSharp_UserMessageSend( int32_t nMsgId, const unsigned char *pData, int32_t nBytes,
                                     int32_t nPlayerIndex, int32_t bReliable )
{
	if ( nPlayerIndex > 0 )
	{
		if ( !SourceSharp_IsPlayerSlotLive( nPlayerIndex ) )
			return 0;

		ss::CSingleUserRecipientFilter filter( nPlayerIndex );
		if ( bReliable )
			filter.MakeReliable();

		SourceSharp_SendThrough( filter, nMsgId, pData, nBytes );
		return 1;
	}

	// The SDK expresses reliability in the broadcast filter's type.
	if ( bReliable )
	{
		ss::CReliableBroadcastRecipientFilter filter;
		SourceSharp_SendThrough( filter, nMsgId, pData, nBytes );
	}
	else
	{
		ss::CBroadcastRecipientFilter filter;
		SourceSharp_SendThrough( filter, nMsgId, pData, nBytes );
	}

	return 1;
}
