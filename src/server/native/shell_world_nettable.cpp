//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// worldspawn's ServerClass, built from the C# declaration.

#include "server_class.h"

#include "ss_world.h"

// A function-local singleton rather than a CWorld member: the ServerClass must
// exist at DLL init, before the first CWorld is made.
class CWorldNetTable
{
public:
	CWorldNetTable()
		: m_nBlobBytes( 0 )
		, m_nWords( 0 )
		, m_pProps( NULL )
		, m_pTable( NULL )
		, m_pClass( NULL )
		, m_bWarnedUndeclared( false )
	{
	}

	int BlobBytes() const { return m_nBlobBytes; }

	ServerClass *Class() const { return m_pClass; }

	int32_t Declare( int32_t nBlobBytes );
	void WarnUndeclaredOnce();

private:
	int m_nBlobBytes;
	int m_nWords;
	SendProp *m_pProps;
	SendTable *m_pTable;
	ServerClass *m_pClass;
	bool m_bWarnedUndeclared;
};

static CWorldNetTable &WorldNetTable()
{
	static CWorldNetTable s_Table;
	return s_Table;
}

// The props are the generic payload, one 32-bit word per blob word at
// SourceSharp_WorldBlobOffset() + 4i.
int32_t CWorldNetTable::Declare( int32_t nBlobBytes )
{
	if ( m_pClass != NULL )
	{
		// A second declaration must agree: the props are already resolved. A hot reload
		// declares the same number again, silently.
		if ( nBlobBytes == m_nBlobBytes )
			return m_nWords;

		Warning( "[SourceSharp] worldspawn: %d declared blob byte(s) disagrees with the %d "
				 "a table has already been built against; REFUSED. The world's props are "
				 "resolved once, at DLL init, and cannot move afterwards\n",
				 nBlobBytes, m_nBlobBytes );
		return -1;
	}

	const int nWords = SourceSharpPayload_WordCount( nBlobBytes );
	const int nBase = SourceSharp_WorldBlobOffset();

	if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBase, nWords ) )
	{
		Warning( "[SourceSharp] worldspawn: %d declared blob byte(s) will not fit at blob "
				 "base %d; NO table is built and the world will network as its base\n",
				 nBlobBytes, nBase );
		return -1;
	}

	// No DT_BaseEntity base table: its props carry CBaseEntity member offsets, which
	// would read past the end of a CWorld. The client forces the world's model index,
	// solidity and transform itself (C_BaseEntity::PostDataUpdate, index 0).
	m_pProps = new SendProp[nWords > 0 ? nWords : 1];

	if ( nWords > 0 && SourceSharpPayload_BuildSendProps( m_pProps, nWords, nBase ) != nWords )
	{
		Warning( "[SourceSharp] worldspawn: payload build refused %d word(s) at blob base "
				 "%d; no table built\n",
				 nWords, nBase );
		delete[] m_pProps;
		m_pProps = NULL;
		return -1;
	}

	m_nBlobBytes = nBlobBytes;
	m_nWords = nWords;

	// Stock's names: the engine pairs server and client classes by name.
	m_pTable = new SendTable( m_pProps, nWords, "DT_WORLD" );
	m_pClass = new ServerClass( "CWorld", m_pTable );

	int nSlow = 0;

	for ( int i = 0; i < nWords; i++ )
	{
		if ( !SourceSharpPayload_IsFastSendProxy( m_pTable->GetProp( i ) ) )
			nSlow++;
	}

	Msg( "[SourceSharp] worldspawn -> 'CWorld' / 'DT_WORLD': no base table + %d payload "
		 "word(s) = %d prop(s), %d blob byte(s) at blob base %d\n",
		 nWords, nWords, nBlobBytes, nBase );

	if ( nSlow > 0 )
	{
		Warning( "[SourceSharp] worldspawn: %d of %d payload word(s) did NOT resolve to "
				 "m_UInt32ToInt32 and would be copied slowly\n",
				 nSlow, nWords );
	}

	return nWords;
}

// Without a declaration the world networks as its base class and the client's
// C_World receives nothing, so this is loud, once.
void CWorldNetTable::WarnUndeclaredOnce()
{
	if ( m_bWarnedUndeclared )
		return;

	m_bWarnedUndeclared = true;

	Warning( "[SourceSharp] worldspawn has NO ServerClass of its own: the managed side never "
			 "called WorldNetDeclare, so SourceSharp.Shared.WorldNetState was not published. "
			 "The world is networking as DT_BaseEntity and the client's renderer will read "
			 "zeroed world bounds, occlusion areas and detail sprite material\n" );
}

int SourceSharp_WorldBlobBytes()
{
	return WorldNetTable().BlobBytes();
}

int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes )
{
	return WorldNetTable().Declare( nBlobBytes );
}

ServerClass *SourceSharp_WorldServerClass()
{
	ServerClass *pClass = WorldNetTable().Class();

	if ( pClass == NULL )
		WorldNetTable().WarnUndeclaredOnce();

	return pClass;
}

bool SourceSharp_IsWorldServerClass( const ServerClass *pClass )
{
	return pClass != NULL && pClass == WorldNetTable().Class();
}
