//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

#include "shell_usermessage_send.h"

#include "ss_recipient_filter.h"
#include "usermessage_append.h"

#include "eiface.h"

bool CServerUserMessages::IsPlayerSlotLive( int nPlayerIndex ) const
{
	if ( nPlayerIndex < 1 || nPlayerIndex > m_pGlobals->maxClients )
		return false;

	edict_t *pEdict = m_pEngine->PEntityOfEntIndex( nPlayerIndex );
	return pEdict && !pEdict->IsFree() && pEdict->GetUnknown() != NULL;
}

void CServerUserMessages::Send( IRecipientFilter &filter, int32_t nMsgId, const unsigned char *pData, int32_t nBytes )
{
	bf_write *pBuf = m_pEngine->UserMessageBegin( &filter, nMsgId );
	if ( pBuf && nBytes > 0 )
		SourceSharp_AppendBits( pBuf, pData, nBytes * 8 );
	m_pEngine->MessageEnd();
}

int32_t CServerUserMessages::UserMessageSend( int32_t nMsgId, const unsigned char *pData, int32_t nBytes,
											  int32_t nPlayerIndex, int32_t bReliable )
{
	ss::CRecipientFilter filter( m_pGlobals->maxClients );

	if ( nPlayerIndex > 0 )
	{
		if ( !IsPlayerSlotLive( nPlayerIndex ) )
			return 0;

		filter.AddRecipientByPlayerIndex( nPlayerIndex );
	}
	else
	{
		for ( int i = 1; i <= m_pGlobals->maxClients; i++ )
		{
			if ( IsPlayerSlotLive( i ) )
				filter.AddRecipientByPlayerIndex( i );
		}
	}

	if ( bReliable )
		filter.MakeReliable();

	Send( filter, nMsgId, pData, nBytes );
	return 1;
}
