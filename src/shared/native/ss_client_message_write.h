//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Writes a CSourceSharpClientMessage: the message type, then ss_client_message.h's wire.
// Uses tier1's bf_write, so only client.so and the tests include it.

#pragma once

#include "ss_client_message.h"

inline bool SourceSharp_WriteClientMessage(const CSourceSharpClientMessage& msg, bf_write& buffer)
{
	buffer.WriteUBitLong(SOURCESHARP_NETMSG_CLIENT_MESSAGE, SOURCESHARP_NETMSG_TYPE_BITS);
	buffer.WriteUBitLong((unsigned int)msg.KeyLength(), SOURCESHARP_CLIENT_MESSAGE_KEY_BITS);
	buffer.WriteBits(msg.Key(), msg.KeyLength() * 8);
	buffer.WriteUBitLong((unsigned int)msg.DataLength(), SOURCESHARP_CLIENT_MESSAGE_LENGTH_BITS);
	if (msg.DataLength() > 0)
		buffer.WriteBits(msg.Data(), msg.DataLength() * 8);
	return !buffer.IsOverflowed();
}
