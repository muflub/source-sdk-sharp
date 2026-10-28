//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Sends the user messages managed code encodes, one crossing per message: the engine
// holds the filter from UserMessageBegin to MessageEnd, so it lives in one frame.

#pragma once

#include "ishim_areas.h"

class CGlobalVars;
class IRecipientFilter;
class IVEngineServer;

class CServerUserMessages final : public IShimUserMessages
{
public:
	CServerUserMessages( IVEngineServer *pEngine, const CGlobalVars *pGlobals )
		: m_pEngine( pEngine )
		, m_pGlobals( pGlobals )
	{
	}

	CServerUserMessages( const CServerUserMessages & ) = delete;
	CServerUserMessages &operator=( const CServerUserMessages & ) = delete;

	// 1 when the engine was handed the message; 0 when nPlayerIndex names an empty
	// slot (nothing is sent, so a message for one player never becomes a broadcast).
	// nPlayerIndex 0 reaches every connected player.
	int32_t UserMessageSend( int32_t nMsgId, const unsigned char *pData, int32_t nBytes, int32_t nPlayerIndex,
							 int32_t bReliable ) override;

private:
	// The engine reserves 1..maxClients for players, so a non-free edict there is one.
	bool IsPlayerSlotLive( int nPlayerIndex ) const;
	void Send( IRecipientFilter &filter, int32_t nMsgId, const unsigned char *pData, int32_t nBytes );

	IVEngineServer *m_pEngine;
	const CGlobalVars *m_pGlobals;
};
