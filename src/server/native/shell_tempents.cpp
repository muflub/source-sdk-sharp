//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — temp entities, stored from a managed plan ===========//
// SS_REASON a  a vtable the engine calls through
//
// The server half of the temp entity system, and since plan_server_native_v2
// T4b it decides nothing. C# plans each table (TePropPlanner: which props, at
// what offsets, under what names) and decides every field of every SendProp
// (SendPropPlan: dt_send.cpp's NOSCALE rules, range multiplier, proxy choice
// and collapsible flag); the registry's refusals and the event-cap pricing are
// C#'s too (TeSchemaRegistry). One length-prefixed block per table crosses
// (sourcesharp.h, SourceSharpTeTableHeader_t), and this file stores it.
//
// WHY ANY OF IT IS NATIVE, measured rather than assumed. The engine walks a
// SendTable and its SendProps by C++ layout (a vptr, then the fields
// dt_send.h declares), calls each prop's m_ProxyFn and compares it with
// g_StandardSendProxies, and reaches the tables through ServerClass, which
// links itself into g_pServerClassHead when it is constructed; the engine walks
// that list exactly ONCE, at DLL init (it assigns the class ids then). So the objects are built here, during the window, and live for the
// process. The send itself is managed: TempEnts calls PlaybackTempEntity through
// the engine forwarder, with the managed recipient filter, the SendTable this
// answered and the class id the engine wrote into the ServerClass.
//
// WHAT IS LEFT besides the store: the round trip for the native TE_*() callers
// (SourceSharpTe_Forward), which hand a native IRecipientFilter that managed
// code cannot read, and goes when they do.
//
//=============================================================================//

#include "eiface.h"

// The engine (connected in shell_engineboundary.cpp); cbase.h supplied it.
extern IVEngineServer *engine;
#include "shell_tempents.h"
#include "shell_tempents_internal.h"
#include "ss_te_prediction.h"
#include "sourcesharp.h"
#include "dt_send.h"
#include "server_class.h"
#include "shell_tempents_store.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E13: the server's IManagedTempEnts

#include <string.h>
#include <set>
#include <string>

//-----------------------------------------------------------------------------
// One built table. The props, the table and the class are leaked on purpose: a
// SendTable is read by the engine for the life of the process, and there is no
// point at which they could be freed and nothing would notice.
//-----------------------------------------------------------------------------
enum { SOURCESHARP_TE_MAX_TABLES = 64 };

struct SourceSharpTeBuilt_t
{
	SendProp    *pProps;
	int          nProps;
	SendTable   *pTable;
	ServerClass *pClass;      // NULL for a table registered without one
	const char  *pClassName;  // interned: what a managed reload asks for again
	uint32_t     nShapeHash;
};

static SourceSharpTeBuilt_t g_TeBuilt[ SOURCESHARP_TE_MAX_TABLES ];
static int                  g_nTeBuilt = 0;
static bool                 g_bTeClosed = false;

//-----------------------------------------------------------------------------
// The names the engine keeps by pointer for the table's lifetime: prop names,
// table names and class names. A std::set node never moves, so c_str() is
// stable for the life of the set; the same text twice is the same pointer.
//-----------------------------------------------------------------------------
static std::set< std::string > g_TeNames;

static const char *InternName( const char *pName )
{
	return g_TeNames.emplace( pName ).first->c_str();
}

//-----------------------------------------------------------------------------
// The accessors the dump reads (shell_tempents_internal.h).
//-----------------------------------------------------------------------------
SendTable *SourceSharpTe_BuiltTable( int i )
{
	return ( i >= 0 && i < g_nTeBuilt ) ? g_TeBuilt[ i ].pTable : NULL;
}

ServerClass *SourceSharpTe_BuiltClass( int i )
{
	return ( i >= 0 && i < g_nTeBuilt ) ? g_TeBuilt[ i ].pClass : NULL;
}

int SourceSharpTe_BuiltCount( void )
{
	return g_nTeBuilt;
}

bool SourceSharpTe_RegistrationClosed( void )
{
	return g_bTeClosed;
}

//=============================================================================//
// REGISTRATION: a block in, a table out.
//=============================================================================//
int32_t SourceSharp_TempEntBuild( const void *pBlock, int32_t nBlockBytes,
                                  SourceSharpTeBuiltTable_t *pOut )
{
	SourceSharpTeTableBlock_t block;

	if ( !pOut || !SourceSharpTe_ReadTableBlock( pBlock, nBlockBytes, &block ) )
		return -1;

	// A CLASS BUILT BY AN EARLIER LOAD answers its table. The engine walked the
	// class list once, at DLL init, so a managed reload cannot build new tables
	// and must not try; what it needs back is the handle. Whether the class
	// changed shape is managed's to judge, from the hash it gave the first time.
	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( strcmp( g_TeBuilt[ i ].pClassName, block.pClassName ) )
			continue;

		pOut->bExisted   = 1;
		pOut->nShapeHash = g_TeBuilt[ i ].nShapeHash;
		pOut->pSendTable = g_TeBuilt[ i ].pTable;
		pOut->pClassId   = g_TeBuilt[ i ].pClass ? &g_TeBuilt[ i ].pClass->m_ClassID : NULL;
		return i;
	}

	// The window. A ServerClass built after the engine's one walk of the class
	// list is not in it, and nothing would ever report it.
	if ( g_bTeClosed || g_nTeBuilt >= SOURCESHARP_TE_MAX_TABLES )
		return -1;

	const int nProps = block.header.nProps;
	SendProp *pProps = new SendProp[ nProps > 0 ? nProps : 1 ];

	for ( int i = 0; i < nProps; i++ )
	{
		const SourceSharpSendPropRow_t row = SourceSharpTe_BlockRow( block, i );
		const char *pName = SourceSharpTe_BlockName( block, row.oName );

		// A nested table is one built before this one, by handle.
		SendTable *pNested = ( row.hDataTable >= 0 && row.hDataTable < g_nTeBuilt )
			? g_TeBuilt[ row.hDataTable ].pTable : NULL;

		if ( !pName || !SourceSharpTe_StoreProp( row, InternName( pName ), pNested, &pProps[ i ] ) )
		{
			delete[] pProps;
			return -1;
		}
	}

	const int iHandle = g_nTeBuilt;
	SourceSharpTeBuilt_t &built = g_TeBuilt[ iHandle ];

	built.pProps     = pProps;
	built.nProps     = nProps;
	built.pTable     = new SendTable( pProps, nProps, InternName( block.pTableName ) );
	built.pClassName = InternName( block.pClassName );
	built.nShapeHash = block.header.nShapeHash;
	built.pClass     = block.header.bRegisterClass ? new ServerClass( built.pClassName, built.pTable ) : NULL;
	g_nTeBuilt = iHandle + 1;

	pOut->bExisted   = 0;
	pOut->nShapeHash = built.nShapeHash;
	pOut->pSendTable = built.pTable;
	pOut->pClassId   = built.pClass ? &built.pClass->m_ClassID : NULL;
	return iHandle;
}

//-----------------------------------------------------------------------------
// Shuts the window. Called once, after managed has pushed its tables, from
// wherever LoadSide returns.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntCloseRegistration( void )
{
	if ( g_bTeClosed )
		return;

	g_bTeClosed = true;

	int nClasses = 0;

	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( g_TeBuilt[i].pClass )
			nClasses++;
	}

	Msg( "[SourceSharp] temp entities: %d table(s), %d ServerClass(es); "
	     "registration closed\n", g_nTeBuilt, nClasses );
}

//=============================================================================//
// THE PREDICTION HOST.
//
// It was a bare IPredictionSystem whose constructor linked it into
// g_pPredictionSystems, so that SuppressHostEvents would reach this channel.
// Nothing on the server walks that list (nm: no SuppressEvents,
// SuppressHostEvents or CDisablePredictionFiltering in server.so), and the one
// reader of the host -- takedamageinfo_library.cpp's ApplyMultiDamage -- asks
// this object directly, so it is the host alone now (P5 lane SP).
// suppress_host_dead_test.cpp fails if a writer of a live host appears.
//=============================================================================//
static ss::TePrediction g_TePrediction;

ss::TePrediction *SourceSharpTe_PredictionSystem( void )
{
	return &g_TePrediction;
}

//=============================================================================//
// THE ROUND TRIP FOR THE NATIVE TE_*() CALLERS (shell_te_forwards.cpp).
//
// They pass their own IRecipientFilter, a native object managed code cannot
// read, so managed packs the fields into the scratch blob and says which class;
// this sends them through the engine with the caller's filter. A refusal from
// managed sends NOTHING, rather than a blob it made up. One scratch blob: this
// runs on the server's thread inside one call and nothing yields in between.
//=============================================================================//
static unsigned char g_TeScratch[ 512 ];

bool SourceSharpTe_Forward( int nId, IRecipientFilter &filter, float flDelay,
                            const float *pFloats, int nFloats,
                            const int *pInts, int nInts )
{
	// E13: the server's managed-built area.
	IManagedTempEnts *pTempEnts = ManagedTempEnts();

	if ( !pTempEnts )
		return false;

	int hClass = -1;

	const int nBytes = pTempEnts->OnNativeTempEntity( nId, pFloats, nFloats, pInts, nInts,
	                                                  g_TeScratch, (int)sizeof( g_TeScratch ),
	                                                  &hClass );

	if ( nBytes <= 0 || hClass < 0 || hClass >= g_nTeBuilt )
		return false;

	if ( !g_TeBuilt[ hClass ].pTable || !g_TeBuilt[ hClass ].pClass )
		return false;

	Assert( !filter.IsReliable() && !filter.IsInitMessage() );
	Assert( flDelay >= -1 && flDelay <= 1 );

	engine->PlaybackTempEntity( filter, flDelay, g_TeScratch, g_TeBuilt[ hClass ].pTable,
	                            g_TeBuilt[ hClass ].pClass->m_ClassID );
	return true;
}

//-----------------------------------------------------------------------------
// The other half of SourceSharp_IsManagedServerClass (plan_gamedll.md Part 5's
// meter). Every ServerClass built here exists because a C# [TempEntity]
// declared it, so it belongs in the managed column. By identity, not by name.
//-----------------------------------------------------------------------------
bool SourceSharp_IsTempEntServerClass( const ServerClass *pClass )
{
	if ( pClass == NULL )
		return false;

	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( g_TeBuilt[ i ].pClass == pClass )
			return true;
	}

	return false;
}
