//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A SourceSharp net message on the game's own channel, client to server: a key and up
// to 4 KiB of bytes that managed code interprets. Type 60 is clear of the engine's
// net_/clc_/svc_ ids; a channel drops a client that sends a type it did not register,
// so the client sends this only to a server that asked it something first.
//
// The read decodes bits itself because server.so links no tier1; the client installs
// a bf_write-based writer. Wire, after the type: key length (6 bits), the key's bytes,
// data length (13 bits), the data's bytes.

#pragma once

#include <stdint.h>
#include <string.h>

#include "inetchannelinfo.h"
#include "inetmessage.h"
#include "tier1/bitbuf.h"

#define SOURCESHARP_NETMSG_TYPE_BITS 6
#define SOURCESHARP_NETMSG_CLIENT_MESSAGE 60
#define SOURCESHARP_CLIENT_MESSAGE_KEY_BITS 6
#define SOURCESHARP_CLIENT_MESSAGE_MAX_KEY 63
#define SOURCESHARP_CLIENT_MESSAGE_LENGTH_BITS 13
#define SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES 4096

class CSourceSharpClientMessage : public INetMessage
{
public:
	// The server's: called by Process with the message just read. Its answer
	// is Process's, and false makes the engine drop the client.
	typedef bool ( *Handler )( CSourceSharpClientMessage &msg );

	// The client's: writes the type, then the wire above, with tier1's bf_write.
	typedef bool ( *Writer )( const CSourceSharpClientMessage &msg, bf_write &buffer );

	explicit CSourceSharpClientMessage( Handler pHandler = NULL, Writer pWriter = NULL )
		: m_pHandler( pHandler ), m_pWriter( pWriter ), m_pChannel( NULL ), m_bReliable( true ), m_nOwner( 0 ), m_nKey( 0 ), m_nData( 0 )
	{
		m_szKey[ 0 ] = 0;
	}

	// The client's: what to send. False, and nothing set, for a key that is
	// empty or too long or data that is too long.
	bool Set( const char *pKey, const uint8_t *pData, int nData )
	{
		const size_t nKey = pKey ? strlen( pKey ) : 0;
		if ( nKey == 0 || nKey > SOURCESHARP_CLIENT_MESSAGE_MAX_KEY
			|| nData < 0 || nData > SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES || ( nData > 0 && !pData ) )
			return false;

		memcpy( m_szKey, pKey, nKey );
		m_szKey[ nKey ] = 0;
		m_nKey = (int)nKey;
		if ( nData > 0 )
			memcpy( m_Data, pData, (size_t)nData );
		m_nData = nData;
		return true;
	}

	// The server's: the entity index of the client whose channel this is registered on.
	void SetOwner( int nEntIndex ) { m_nOwner = nEntIndex; }
	int Owner() const { return m_nOwner; }

	const char *Key() const { return m_szKey; }
	int KeyLength() const { return m_nKey; }
	const uint8_t *Data() const { return m_Data; }
	int DataLength() const { return m_nData; }

	// INetMessage
	virtual void SetNetChannel( INetChannel *netchan ) OVERRIDE { m_pChannel = netchan; }
	virtual void SetReliable( bool state ) OVERRIDE { m_bReliable = state; }
	virtual bool Process() OVERRIDE { return m_pHandler ? m_pHandler( *this ) : true; }

	virtual bool ReadFromBuffer( bf_read &buffer ) OVERRIDE
	{
		const int nStart = buffer.GetNumBitsRead();
		const int nEnd = nStart + buffer.GetNumBitsLeft();
		int nUsed = 0;
		if ( !Decode( buffer.GetBasePointer(), nStart, nEnd, &nUsed ) )
		{
			buffer.Seek( nEnd + 1 );   // past the end: the engine sees an overflowed read
			return false;
		}
		return buffer.Seek( nStart + nUsed );
	}

	// The read without the engine's buffer: bits [nStart, nEnd) of pBase.
	// False, and nothing kept, for a truncated or malformed message.
	bool Decode( const unsigned char *pBase, int nStart, int nEnd, int *pnUsed )
	{
		m_nKey = 0;
		m_nData = 0;
		m_szKey[ 0 ] = 0;

		int bit = nStart;
		uint32_t nKey = 0, nData = 0;
		if ( !pBase || !Bits( pBase, &bit, nEnd, SOURCESHARP_CLIENT_MESSAGE_KEY_BITS, &nKey ) || nKey == 0 )
			return false;

		char key[ SOURCESHARP_CLIENT_MESSAGE_MAX_KEY + 1 ];
		for ( uint32_t i = 0; i < nKey; i++ )
		{
			uint32_t c = 0;
			if ( !Bits( pBase, &bit, nEnd, 8, &c ) || c == 0 )
				return false;
			key[ i ] = (char)c;
		}
		key[ nKey ] = 0;

		if ( !Bits( pBase, &bit, nEnd, SOURCESHARP_CLIENT_MESSAGE_LENGTH_BITS, &nData )
			|| nData > SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES )
			return false;
		for ( uint32_t i = 0; i < nData; i++ )
		{
			uint32_t b = 0;
			if ( !Bits( pBase, &bit, nEnd, 8, &b ) )
				return false;
			m_Data[ i ] = (uint8_t)b;
		}

		memcpy( m_szKey, key, nKey + 1 );
		m_nKey = (int)nKey;
		m_nData = (int)nData;
		*pnUsed = bit - nStart;
		return true;
	}

	virtual bool WriteToBuffer( bf_write &buffer ) OVERRIDE
	{
		return m_nKey > 0 && m_pWriter && m_pWriter( *this, buffer );
	}

	virtual bool IsReliable() const OVERRIDE { return m_bReliable; }
	virtual int GetType() const OVERRIDE { return SOURCESHARP_NETMSG_CLIENT_MESSAGE; }
	virtual int GetGroup() const OVERRIDE { return INetChannelInfo::GENERIC; }
	virtual const char *GetName() const OVERRIDE { return "ss_ClientMessage"; }
	virtual INetChannel *GetNetChannel() const OVERRIDE { return m_pChannel; }
	virtual const char *ToString() const OVERRIDE { return m_szKey; }
	virtual bool BIncomingMessageForProcessing( double dblNetTime, int numBytes ) OVERRIDE
	{
		(void)dblNetTime;
		(void)numBytes;
		return true;
	}
	virtual size_t GetSize() const OVERRIDE { return sizeof( *this ); }

private:
	// nBits at *pBit, least significant first, as bf_read::ReadUBitLong reads.
	static bool Bits( const unsigned char *pBase, int *pBit, int nEnd, int nBits, uint32_t *pOut )
	{
		if ( *pBit + nBits > nEnd )
			return false;
		uint32_t value = 0;
		for ( int i = 0; i < nBits; i++, ( *pBit )++ )
			value |= (uint32_t)( ( pBase[ *pBit >> 3 ] >> ( *pBit & 7 ) ) & 1 ) << i;
		*pOut = value;
		return true;
	}

	Handler m_pHandler;
	Writer m_pWriter;
	INetChannel *m_pChannel;
	bool m_bReliable;
	int m_nOwner;
	int m_nKey;
	int m_nData;
	char m_szKey[ SOURCESHARP_CLIENT_MESSAGE_MAX_KEY + 1 ];
	uint8_t m_Data[ SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES ];
};
