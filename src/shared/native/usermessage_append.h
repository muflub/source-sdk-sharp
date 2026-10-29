// Appending a C# user message payload to the engine's buffer. bf_write stores bits
// LSB-first in little-endian words, so bytes appended at any bit position read
// back as the same bytes, and a stock bf_read hook can read a C# payload.

#pragma once

#include <stdint.h>

#include "tier1/bitbuf.h"

// Appends nBits of pData (LSB-first) to an engine bf_write using only its public
// fields and inline bits. When it does not fit, nothing is written and the
// overflow flag is set, as bf_write's own writers do.
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
		pBuf->WriteOneBitNoCheck( ( pBytes[i >> 3] >> ( i & 7 ) ) & 1 );
}
