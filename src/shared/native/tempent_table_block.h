//========= SourceSharp — a planned temp entity table's block, read ===========//
//
// Both sides' temp entity tables are planned in C# and cross as one
// length-prefixed block per table (sourcesharp.h: SourceSharpTeTableHeader_t,
// then the rows, then the names): the server's with SourceSharpSendPropRow_t
// rows (shell_tempents_store.cpp), the client's with SourceSharpRecvPropRow_t
// rows (shell_tempents_client.cpp). This reads and checks the block; each side
// copies its own rows out. Carrying out only: nothing here decides a prop.
//
// No engine headers, so the gtests of both stores reach it.
//
//=============================================================================//
#pragma once

#include <string.h>

#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// One block, read: the header, the rows (unaligned, so copied out one at a
// time), and the payload the names are found in.
//-----------------------------------------------------------------------------
struct SourceSharpTeTableBlock_t
{
	SourceSharpTeTableHeader_t  header;
	const unsigned char        *pPayload;
	int                         nPayload;
	const unsigned char        *pRows;
	const char                 *pClassName;
	const char                 *pTableName;
};

// A name at a payload offset: NULL when the offset is outside the payload, the
// text is empty, or it runs off the end.
inline const char *SourceSharpTe_BlockName( const SourceSharpTeTableBlock_t &block, int32_t oName )
{
	if ( oName < 0 )
		return NULL;

	for ( int i = oName; i < block.nPayload; i++ )
	{
		if ( block.pPayload[ i ] == 0 )
			return i > oName ? (const char *)block.pPayload + oName : NULL;
	}

	return NULL;
}

// The block, checked: a whole-byte bit count that fits the bytes given, a
// header, as many rows of nRowBytes as it claims, and both names. False for
// anything else.
inline bool SourceSharpTe_ReadTableBlock( const void *pBlock, int32_t nBlockBytes, int32_t nRowBytes,
                                          SourceSharpTeTableBlock_t *pOut )
{
	if ( !pBlock || !pOut || nRowBytes <= 0 || nBlockBytes < (int32_t)sizeof( int32_t ) )
		return false;

	// Length-prefixed: an int32 bit count, then the payload.
	int32_t nBits = 0;
	memcpy( &nBits, pBlock, sizeof( nBits ) );

	if ( nBits < 0 || ( nBits % 8 ) != 0 )
		return false;

	const int nPayload = nBits / 8;

	if ( nPayload > nBlockBytes - (int)sizeof( int32_t )
	  || nPayload < (int)sizeof( SourceSharpTeTableHeader_t ) )
		return false;

	SourceSharpTeTableBlock_t block;
	block.pPayload = (const unsigned char *)pBlock + sizeof( int32_t );
	block.nPayload = nPayload;
	memcpy( &block.header, block.pPayload, sizeof( block.header ) );
	block.pRows = block.pPayload + sizeof( block.header );

	const int nRoom = ( nPayload - (int)sizeof( block.header ) ) / nRowBytes;

	if ( block.header.nProps < 0 || block.header.nProps > nRoom )
		return false;

	block.pClassName = SourceSharpTe_BlockName( block, block.header.oClassName );
	block.pTableName = SourceSharpTe_BlockName( block, block.header.oTableName );

	if ( !block.pClassName || !block.pTableName )
		return false;

	*pOut = block;
	return true;
}
