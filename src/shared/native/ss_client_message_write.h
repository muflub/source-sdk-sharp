//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's writer for CSourceSharpClientMessage ==//
//
// tier1's bf_write, so client.so and the tests only (server.so links no
// tier1 and never sends one). The type first, as every net message's
// WriteToBuffer writes it, then ss_client_message.h's wire.
//
//=============================================================================//

#ifndef SS_CLIENT_MESSAGE_WRITE_H
#define SS_CLIENT_MESSAGE_WRITE_H
#pragma once

#include "ss_client_message.h"

inline bool SourceSharp_WriteClientMessage( const CSourceSharpClientMessage &msg, bf_write &buffer )
{
	buffer.WriteUBitLong( SOURCESHARP_NETMSG_CLIENT_MESSAGE, SOURCESHARP_NETMSG_TYPE_BITS );
	buffer.WriteUBitLong( (unsigned int)msg.KeyLength(), SOURCESHARP_CLIENT_MESSAGE_KEY_BITS );
	buffer.WriteBits( msg.Key(), msg.KeyLength() * 8 );
	buffer.WriteUBitLong( (unsigned int)msg.DataLength(), SOURCESHARP_CLIENT_MESSAGE_LENGTH_BITS );
	if ( msg.DataLength() > 0 )
		buffer.WriteBits( msg.Data(), msg.DataLength() * 8 );
	return !buffer.IsOverflowed();
}

#endif // SS_CLIENT_MESSAGE_WRITE_H
