//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — sending a user message ==============================//
// SS_REASON a  a vtable the engine calls through
//
// plan_side_isolation §2.2a. Every user-message DECISION is C#'s: the registry,
// the engine id a key travels under, who a message reaches and whether it is
// sent at all. What is left here carries those decisions out against the
// engine, and nothing else:
//
//   SourceSharp_UserMessageSend   IShimUserMessages' one slot: C# has looked
//       the key up and written the payload; this builds the filter, opens the
//       engine's message, appends the bytes and ends it. One slot rather than
//       Begin/Write/End crossings because the engine keeps the FILTER pointer
//       from UserMessageBegin until MessageEnd, so the filter has to outlive
//       all three, and only a single native frame can hold it without state.
//
// UserMessageBegin, MessageEnd and MessageWriteRawBits -- the Valve-signature
// entry points native senders called, with the dropped-message sentinel and
// the C# name lookup behind them -- went with the last native sender
// (user-message payload model, 2026-10-04): Shake and Fade are C#'s
// ScreenEffects, and the close caption is C#'s CloseCaptions. Every message is
// a C# payload through the slot above.
//
// The registry, the 64 dispatch trampolines and the managed send path that
// used to sit in shell_usermessages.cpp and shell_usermessage_registry.cpp are
// evicted (docs/server_evictions.md).
//
//=============================================================================//

#include "ss_recipient_filter.h"
#include "usermessage_append.h"
#include "imanaged_areas.h"
#include "edict.h"
#include "eiface.h"

// The engine's globals and its server interface (connected in
// shell_gamedll.cpp / shell_engineboundary.cpp).
extern CGlobalVars *gpGlobals;
extern IVEngineServer *engine;


//-----------------------------------------------------------------------------
// Whether a player slot holds a connected player, whichever hierarchy that
// player's entity lives on: the engine reserves 1..maxClients for players, so
// a non-free edict there is one. A FACT the send slot answers with; C# decides
// what a message to an empty slot means.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// IShimUserMessages::UserMessageSend. 1 when the engine was handed the
// message, 0 when nPlayerIndex names a slot with no player in it (nothing is
// sent: a message for one player never becomes a broadcast).
//-----------------------------------------------------------------------------
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

	// Two filter types rather than a flag: the SDK expresses reliability in
	// the TYPE of a broadcast filter.
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
