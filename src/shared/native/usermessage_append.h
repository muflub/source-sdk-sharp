//========= SourceSharp — appending a user message's payload =================//
// plan_server_novalve.md P2.2, and the user-message payload model (2026-10-04).
// Every user message server.so sends is C#'s: a payload its generated Write
// produced, handed to IShimUserMessages::UserMessageSend, whose body appends
// the bytes to the engine's buffer with SourceSharp_AppendBits below. Native
// code writes no payload of its own any more (CUserMessagePayload,
// UserMessageBegin and MessageEnd went with the last native sender).
//
// THE WIRE IS VALVE'S. bf_write stores bits LSB-first in little-endian words,
// so a byte string appended at any bit position reads back as the same bytes:
// a payload's byte is WRITE_BYTE's 8 bits, its short WRITE_SHORT's low 16, its
// float WRITE_FLOAT's 32, its string the characters and a NUL -- which is what
// lets a stock bf_read hook read a C# payload. usermessage_append_test holds
// the append to bf_write's own writers at every starting bit offset.
//=============================================================================//

#pragma once

#include <stdint.h>

#include "tier1/bitbuf.h"

// Appends nBits of pData (LSB-first) to an ENGINE bf_write through its public
// fields and inline bits only (bitbuf.h): m_iCurBit / m_nDataBits for the
// bounds, WriteOneBitNoCheck per bit, SetOverflowFlag when it does not fit --
// in which case none of it is written, which is how bf_write's own writers
// leave a buffer too. Nothing of tier1's is called (CheckForOverflow would be,
// through its error handler). usermessage_append_test holds it to bf_write's
// own writers, bit for bit, at every starting bit offset.
inline void SourceSharp_AppendBits( bf_write *pBuf, const void *pData, int nBits )
{
	if ( nBits <= 0 || pData == NULL )
		return;

	if ( pBuf->m_iCurBit + nBits > pBuf->m_nDataBits )
	{
		pBuf->SetOverflowFlag();
		return;
	}

	const uint8_t *pBytes = (const uint8_t *)pData;
	for ( int i = 0; i < nBits; i++ )
		pBuf->WriteOneBitNoCheck( ( pBytes[ i >> 3 ] >> ( i & 7 ) ) & 1 );
}

// The three Valve-code senders' message (shell_usermessage_send.cpp): Begin
// resolves the name through C#'s registry, and a name C# refuses opens a
// DROPPED message that takes no bytes and is never handed to the engine.
