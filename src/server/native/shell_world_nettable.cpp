//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================//
// SS_REASON a  a vtable the engine calls through
//
// worldspawn's ServerClass, built from the C# declaration. See
// shell_world_nettable.h for the ruling this file exists for and for the
// measurement that answers "how does managed net state attach to an entity
// managed code did not create".
//
//=============================================================================//

#include "dt_send.h"
#include "server_class.h"
#include "shell_world_nettable.h"
#include "generic_payload.h"
#include "ss_world.h"


//-----------------------------------------------------------------------------
// EVERYTHING THIS FILE REMEMBERS, AS FIELDS.
//
// The project rule is that state belongs in a class as fields and not at file
// scope. The one instance is a FUNCTION-LOCAL static, which is the idiom this
// tree already uses for exactly this shape -- SourceSharp_DroppedMessage()'s
// buffer, and the memoising statics in the native source-scanning tests -- and is
// the population tools/statics-audit deliberately does not gate (Phase A6 row
// 12 records that reading).
//
// It cannot be a member of CWorld, which is the obvious alternative: the table
// has to exist before any CWorld does. A ServerClass links itself into
// g_pServerClassHead from its own constructor and the engine walks that list
// once, during DLL init; the first CWorld is made when the first map is parsed.
//-----------------------------------------------------------------------------
class CWorldNetTable
{
public:
	CWorldNetTable()
		: m_nBlobBytes( 0 ),
		  m_nWords( 0 ),
		  m_pProps( NULL ),
		  m_pTable( NULL ),
		  m_pClass( NULL ),
		  m_bWarnedUndeclared( false )
	{
	}

	int          BlobBytes() const { return m_nBlobBytes; }
	ServerClass *Class()     const { return m_pClass; }

	int32_t Declare( int32_t nBlobBytes );
	void    WarnUndeclaredOnce();

private:
	int          m_nBlobBytes;
	int          m_nWords;
	SendProp    *m_pProps;
	SendTable   *m_pTable;
	ServerClass *m_pClass;
	bool         m_bWarnedUndeclared;
};

static CWorldNetTable &WorldNetTable()
{
	static CWorldNetTable s_Table;
	return s_Table;
}

//-----------------------------------------------------------------------------
// The table, built once.
//
// TWO KINDS OF PROP AND THE ORDER OF THEM IS THE WIRE FORMAT.
//
// Prop 0 is the base table, because that is what stock's macro emitted:
// BEGIN_SEND_TABLE (dt_send.h:586) puts SendPropDataTable("baseclass", ...)
// ahead of the props the author wrote, so DT_WORLD has always inherited
// DT_BaseEntity and the client has always received the world's origin, model
// index, effects and render colour through it. Dropping that would have been
// invisible in every offline gate and would have cost the client the world's
// model index -- so it is carried, and it is carried FIRST because that is
// where the macro put it.
//
// CBaseEntity::m_pClassSendTable is public (server_class.h:88 opens `public:`)
// and is defined by CBaseEntity's own IMPLEMENT_SERVERCLASS_ST, which is one of
// the four SDK registrations that remain. This is a READ of that table, not a
// second registration: nothing here makes CBaseEntity's ServerClass and nothing
// here stops it being deleted the day its own subclasses leave.
//
// Props 1..N are the generic payload -- one 32-bit unsigned int per word of the
// blob, at SourceSharp_WorldBlobOffset() + 4i, built by the same
// SourceSharpPayload_BuildSendProps every standalone tier is built with. That
// is the whole of the reuse: the world's props are the hierarchy's props at a
// different base.
//-----------------------------------------------------------------------------
int32_t CWorldNetTable::Declare( int32_t nBlobBytes )
{
	if ( m_pClass != NULL )
	{
		// A SECOND DECLARATION IS ONLY OK IF IT AGREES. The table is already in
		// g_pServerClassHead with its props resolved, so a different length now
		// would be props addressing bytes an entity was not allocated. This is
		// the hot-reload path: the managed side comes back and declares again
		// with the same number, which is fine and must stay silent.
		if ( nBlobBytes == m_nBlobBytes )
			return m_nWords;

		Warning( "[SourceSharp] worldspawn: %d declared blob byte(s) disagrees with the %d "
		         "a table has already been built against; REFUSED. The world's props are "
		         "resolved once, at DLL init, and cannot move afterwards\n",
			nBlobBytes, m_nBlobBytes );
		return -1;
	}

	const int nWords = SourceSharpPayload_WordCount( nBlobBytes );
	const int nBase  = SourceSharp_WorldBlobOffset();

	if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBase, nWords ) )
	{
		Warning( "[SourceSharp] worldspawn: %d declared blob byte(s) will not fit at blob "
		         "base %d; NO table is built and the world will network as its base\n",
			nBlobBytes, nBase );
		return -1;
	}

	// THE BASE TABLE IS GONE (plan_cworld_trace.md T3), AND IT HAD TO GO.
	//
	// This was:
	//
	//     m_pProps[0] = SendPropDataTable( "baseclass", 0,
	//                       CBaseEntity::m_pClassSendTable,
	//                       SendProxy_DataTableToDataTable );
	//
	// and the comment above it argued for keeping it: stock's BEGIN_SEND_TABLE
	// emitted one, DT_WORLD has always inherited DT_BaseEntity, and dropping it
	// "would have been invisible in every offline gate and would have cost the
	// client the world's model index".
	//
	// WHAT CHANGED IS NOT THE ARGUMENT, IT IS THE OBJECT. worldspawn is not a
	// CBaseEntity any more, and DT_BaseEntity's props carry CBaseEntity's
	// MEMBER OFFSETS. The engine encodes a table against edict->GetUnknown(),
	// which for this class is the CWorld object -- so every one of those props
	// would read at an offset belonging to a different and much larger class.
	// sizeof(CBaseEntity) is several times sizeof(CWorld), so most of them read
	// PAST THE END OF THE OBJECT, into the blob and past the allocation, and
	// send the result to every client as the world's origin and render colour.
	// That is not a degraded world; it is a heap over-read on the wire.
	//
	// AND THE CLIENT DOES NOT NEED IT, which is the part that makes this a
	// deletion rather than a replacement. C_BaseEntity::PostDataUpdate
	// (c_baseentity.cpp:2393) already forces the world's state locally:
	//
	//     if ( index == 0 )
	//     {
	//         m_nModelIndex = 1;
	//         SetSolid( SOLID_BSP );
	//         SetAbsOrigin( vec3_origin );
	//         SetAbsAngles( vec3_angle );
	//     }
	//
	// -- stock's own code, under stock's own comment "If it's the world, force
	// solid flags". Every field the earlier argument named is overwritten by
	// that block a moment after it arrives. The rest of DT_BaseEntity is
	// defaults, and C_World's allocation is calloc'd.
	//
	// BOTH HALVES MOVE TOGETHER, which is what shim_underive_verdict_test.cpp's
	// clause (c) has always required, and they can: both are built by this
	// port, in this file and in shell_world_nettable_client.cpp, from one
	// managed declaration. The client's RecvPropDataTable goes in the same
	// commit and for the same reason.
	m_pProps = new SendProp[ nWords > 0 ? nWords : 1 ];

	if ( nWords > 0
	  && SourceSharpPayload_BuildSendProps( m_pProps, nWords, nBase ) != nWords )
	{
		Warning( "[SourceSharp] worldspawn: payload build refused %d word(s) at blob base "
		         "%d; no table built\n", nWords, nBase );
		delete[] m_pProps;
		m_pProps = NULL;
		return -1;
	}

	m_nBlobBytes = nBlobBytes;
	m_nWords     = nWords;

	// THE NAMES ARE STOCK'S, and they have to be. The engine pairs the two
	// sides by the SERVER CLASS NAME -- CL_ParseClassInfo_EndClasses looks each
	// server class up in the client's list by name -- and c_world.cpp's client
	// class is registered under "CWorld" against a table called "DT_World".
	// Stock's server table was spelt "DT_WORLD"; both spellings are kept
	// exactly as they were so that nothing about the handshake changes here.
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

//-----------------------------------------------------------------------------
// Said ONCE, and said with what to do about it.
//
// A build whose managed side never declared has a world with no table of its
// own. The shim answers its base's class rather than NULL, because NULL is a
// dereference inside CServerNetworkProperty::GetServerClass on the first entity
// the engine packs -- but the world then networks as DT_BaseEntity and the
// client's C_World never receives a thing, which is exactly the "removes a
// capability rather than moving it" outcome Part B2 forbids. So it is loud.
//-----------------------------------------------------------------------------
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

//=============================================================================//
// The three functions the rest of the build sees.
//=============================================================================//

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
