//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================//
// SS_REASON f  a bit-level wire codec
//
// worldspawn's ClientClass, built from the C# declaration. The twin of
// sourcesharp/game/server/shell_world_nettable.cpp; see that file and this
// one's header for the ruling and for the measurement behind the design.
//
//=============================================================================//

#include "cbase.h"
#include "shell_world_nettable_client.h"
#include "generic_payload.h"
#include "c_world.h"
#include "client_class.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// EVERYTHING THIS FILE REMEMBERS, AS FIELDS, in one function-local static — the
// server's file makes the whole argument and it is the same one here.
//-----------------------------------------------------------------------------
class CWorldNetTableClient
{
public:
	CWorldNetTableClient()
		: m_nBlobBytes( 0 ),
		  m_nWords( 0 ),
		  m_pProps( NULL ),
		  m_pTable( NULL ),
		  m_pClass( NULL ),
		  m_bWarnedUndeclared( false )
	{
	}

	int          BlobBytes() const { return m_nBlobBytes; }
	ClientClass *Class()     const { return m_pClass; }

	int32_t Declare( int32_t nBlobBytes );
	void    WarnUndeclaredOnce();

private:
	int          m_nBlobBytes;
	int          m_nWords;
	RecvProp    *m_pProps;
	RecvTable   *m_pTable;
	ClientClass *m_pClass;
	bool         m_bWarnedUndeclared;
};

static CWorldNetTableClient &WorldNetTable()
{
	static CWorldNetTableClient s_Table;
	return s_Table;
}

//-----------------------------------------------------------------------------
// The table, built once, PROP FOR PROP THE SERVER'S.
//
// Prop 0 is the base table, because prop 0 of the server's is: stock's
// BEGIN_RECV_TABLE (dt_recv.h:288) puts RecvPropDataTable("baseclass", ...)
// ahead of the props the author wrote, exactly as BEGIN_SEND_TABLE does. So the
// world has always received C_BaseEntity's own table — its origin, its model
// index, its effects and its render colour — and it still does.
//
// Props 1..N are the generic payload at THIS side's blob base, which is not the
// server's and never was: the word INDEX is what the two sides agree on.
//
// THE TWO NAMES ARE STOCK'S, INCLUDING THE ONE THAT DISAGREES WITH ITSELF. The
// engine pairs the sides by SERVER CLASS NAME, and stock registered this client
// class under "CWorld" against a table it spelt "DT_World" while the server's
// was spelt "DT_WORLD". Both spellings are carried across unchanged, because
// this port is not the place to find out which of the engine's comparisons is
// case-sensitive.
//-----------------------------------------------------------------------------
int32_t CWorldNetTableClient::Declare( int32_t nBlobBytes )
{
	if ( m_pClass != NULL )
	{
		// The hot-reload path: the same number again is fine and must stay
		// silent. A different one is refused, because the props are resolved.
		if ( nBlobBytes == m_nBlobBytes )
			return m_nWords;

		Warning( "[SourceSharp] worldspawn(client): %d declared blob byte(s) disagrees with "
		         "the %d a table has already been built against; REFUSED\n",
			nBlobBytes, m_nBlobBytes );
		return -1;
	}

	const int nWords = SourceSharpPayload_WordCount( nBlobBytes );
	const int nBase  = SourceSharp_WorldBlobOffset();

	if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBase, nWords ) )
	{
		Warning( "[SourceSharp] worldspawn(client): %d declared blob byte(s) will not fit at "
		         "blob base %d; NO table is built and the world's update will not pair\n",
			nBlobBytes, nBase );
		return -1;
	}

	// THE BASE TABLE IS GONE, AND ITS TWIN WENT IN THE SAME COMMIT.
	//
	// This was:
	//
	//     m_pProps[0] = RecvPropDataTable( "baseclass", 0, 0,
	//                       C_BaseEntity::m_pClassRecvTable,
	//                       DataTableRecvProxy_StaticDataTable );
	//
	// The reason is on the SERVER side and is written out in full in
	// shell_world_nettable.cpp: CWorld is not a CBaseEntity any more
	// (plan_cworld_trace.md T3), so DT_BaseEntity's props would encode a much
	// larger class's member offsets against a much smaller object -- most of
	// them past the end of it.
	//
	// THIS HALF LOSES NOTHING, and that is measured rather than hoped. The
	// world's C_BaseEntity state was never read off the wire on this side even
	// while it HAD one: C_BaseEntity::PostDataUpdate (c_baseentity.cpp:2394)
	// forced model index 1, SOLID_BSP, the origin and the angles for entity 0,
	// under stock's own comment "If it's the world, force solid flags" --
	// overwriting whatever arrived a moment earlier. Everything else
	// DT_BaseEntity carries is a default and C_World's allocation is calloc'd.
	//
	// THOSE FOUR FORCED VALUES ARE NOW C_World's OWN ANSWERS, which is the
	// paragraph step 5e had to add: the world came off C_BaseEntity on this side
	// too, so nothing runs that block for it any more. Each of the four is
	// answered directly instead, and the four are worth listing because their
	// disappearance would be silent --
	//
	//   model index 1   CWorldClientCollideable::GetCollisionModelIndex()
	//   SOLID_BSP       CWorldClientCollideable::GetSolid(), set in its ctor
	//   the origin      C_World::GetAbsOrigin / GetRenderOrigin, vec3_origin
	//   the angles      C_World::GetAbsAngles / GetRenderAngles, vec3_angle
	//
	// So the answers are the same four constants stock forced; what changed is
	// that they are the class's own rather than an `if ( index == 0 )` inside a
	// base it no longer has.
	//
	// THE TWO HALVES HAD TO MOVE TOGETHER, and this is what that looks like:
	// one managed declaration, two symmetric builders, one commit. That is the
	// requirement shim_underive_verdict_test.cpp's clause (c) states -- met,
	// rather than argued around.
	m_pProps = new RecvProp[ nWords > 0 ? nWords : 1 ];

	if ( nWords > 0
	  && SourceSharpPayload_BuildRecvProps( m_pProps, nWords, nBase ) != nWords )
	{
		Warning( "[SourceSharp] worldspawn(client): payload build refused %d word(s) at blob "
		         "base %d; no table built\n", nWords, nBase );
		delete[] m_pProps;
		m_pProps = NULL;
		return -1;
	}

	m_nBlobBytes = nBlobBytes;
	m_nWords     = nWords;

	m_pTable = new RecvTable( m_pProps, nWords, "DT_World" );
	m_pClass = new ClientClass( "CWorld", SourceSharp_ClientWorldFactory, NULL, m_pTable );

	// The recv half of the fast-local-transfer conjunction. The engine's local
	// (listen-server) transfer checks BOTH ends, so a correct send prop paired with a wrong recv prop is
	// still the slow list, and a server-only dump reports everything fine.
	int nSlow = 0;

	for ( int i = 0; i < nWords; i++ )
	{
		if ( !SourceSharpPayload_IsFastRecvProxy( m_pTable->GetProp( i ) ) )
			nSlow++;
	}

	Msg( "[SourceSharp] worldspawn(client) -> 'CWorld' / 'DT_World': no base table + %d "
	     "payload word(s) = %d prop(s), %d blob byte(s) at blob base %d\n",
		nWords, nWords, nBlobBytes, nBase );

	if ( nSlow > 0 )
	{
		Warning( "[SourceSharp] worldspawn(client): %d of %d payload word(s) did NOT resolve "
		         "to m_Int32ToInt32 and would be copied slowly\n", nSlow, nWords );
	}

	return nWords;
}

void CWorldNetTableClient::WarnUndeclaredOnce()
{
	if ( m_bWarnedUndeclared )
		return;

	m_bWarnedUndeclared = true;

	Warning( "[SourceSharp] worldspawn(client) has NO ClientClass of its own: the managed side "
	         "never called WorldNetDeclare, so SourceSharp.Shared.WorldNetState was not "
	         "declared here. The world's update will not pair and the renderer will read "
	         "zeroed world bounds, occlusion areas and detail sprite material\n" );
}

//=============================================================================//

int SourceSharp_WorldBlobBytes()
{
	return WorldNetTable().BlobBytes();
}

int32_t SourceSharp_WorldNetDeclare( int32_t nBlobBytes )
{
	return WorldNetTable().Declare( nBlobBytes );
}

ClientClass *SourceSharp_WorldClientClass()
{
	ClientClass *pClass = WorldNetTable().Class();

	if ( pClass == NULL )
		WorldNetTable().WarnUndeclaredOnce();

	return pClass;
}
