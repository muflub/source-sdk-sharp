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
#include "sourcesharp.h"
#include "dt_send.h"
#include "server_class.h"
#include "shell_tempents_store.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E13: the server's IManagedTempEnts
#include "server_state.h"     // CServerTempEnts, the DLL tier's (T6)

#include <string.h>
#include <set>
#include <string>

//-----------------------------------------------------------------------------
// THE TABLES LIVE IN THE GAME DLL'S DLL TIER (CServerTempEnts, server_state.h;
// plan_server_native_v2 T6): they were file-scope statics. NULL outside
// DLLInit..DLLShutdown, when there is nothing built and nothing to build into.
//-----------------------------------------------------------------------------
static CServerTempEnts *TempEnts()
{
	CServerDllState *pDll = ShellGameDll_Dll();
	return pDll ? &pDll->m_TempEnts : NULL;
}

static const char *InternName( CServerTempEnts &te, const char *pName )
{
	return te.m_Names.emplace( pName ).first->c_str();
}

//-----------------------------------------------------------------------------
// The accessors the dump reads (shell_tempents_internal.h).
//-----------------------------------------------------------------------------
SendTable *SourceSharpTe_BuiltTable( int i )
{
	CServerTempEnts *pTe = TempEnts();
	return ( pTe && i >= 0 && i < pTe->m_nBuilt ) ? pTe->m_Built[ i ].pTable : NULL;
}

ServerClass *SourceSharpTe_BuiltClass( int i )
{
	CServerTempEnts *pTe = TempEnts();
	return ( pTe && i >= 0 && i < pTe->m_nBuilt ) ? pTe->m_Built[ i ].pClass : NULL;
}

int SourceSharpTe_BuiltCount( void )
{
	CServerTempEnts *pTe = TempEnts();
	return pTe ? pTe->m_nBuilt : 0;
}

bool SourceSharpTe_RegistrationClosed( void )
{
	CServerTempEnts *pTe = TempEnts();
	return pTe ? pTe->m_bClosed : false;
}

//=============================================================================//
// REGISTRATION: a block in, a table out.
//=============================================================================//
int32_t SourceSharp_TempEntBuild( const void *pBlock, int32_t nBlockBytes,
                                  SourceSharpTeBuiltTable_t *pOut )
{
	SourceSharpTeTableBlock_t block;
	CServerTempEnts *pTe = TempEnts();
	if ( !pOut || !pTe || !SourceSharpTe_ReadTableBlock( pBlock, nBlockBytes, &block ) )
		return -1;
	CServerTempEnts &te = *pTe;

	// A CLASS BUILT BY AN EARLIER LOAD answers its table. The engine walked the
	// class list once, at DLL init, so a managed reload cannot build new tables
	// and must not try; what it needs back is the handle. Whether the class
	// changed shape is managed's to judge, from the hash it gave the first time.
	for ( int i = 0; i < te.m_nBuilt; i++ )
	{
		if ( strcmp( te.m_Built[ i ].pClassName, block.pClassName ) )
			continue;

		pOut->bExisted   = 1;
		pOut->nShapeHash = te.m_Built[ i ].nShapeHash;
		pOut->pSendTable = te.m_Built[ i ].pTable;
		pOut->pClassId   = te.m_Built[ i ].pClass ? &te.m_Built[ i ].pClass->m_ClassID : NULL;
		return i;
	}

	// The window. A ServerClass built after the engine's one walk of the class
	// list is not in it, and nothing would ever report it.
	if ( te.m_bClosed || te.m_nBuilt >= SOURCESHARP_TE_MAX_TABLES )
		return -1;

	const int nProps = block.header.nProps;
	SendProp *pProps = new SendProp[ nProps > 0 ? nProps : 1 ];

	for ( int i = 0; i < nProps; i++ )
	{
		const SourceSharpSendPropRow_t row = SourceSharpTe_BlockRow( block, i );
		const char *pName = SourceSharpTe_BlockName( block, row.oName );

		// A nested table is one built before this one, by handle.
		SendTable *pNested = ( row.hDataTable >= 0 && row.hDataTable < te.m_nBuilt )
			? te.m_Built[ row.hDataTable ].pTable : NULL;

		if ( !pName || !SourceSharpTe_StoreProp( row, InternName( te, pName ), pNested, &pProps[ i ] ) )
		{
			delete[] pProps;
			return -1;
		}
	}

	const int iHandle = te.m_nBuilt;
	SourceSharpTeBuilt_t &built = te.m_Built[ iHandle ];

	built.pProps     = pProps;
	built.nProps     = nProps;
	built.pTable     = new SendTable( pProps, nProps, InternName( te, block.pTableName ) );
	built.pClassName = InternName( te, block.pClassName );
	built.nShapeHash = block.header.nShapeHash;
	built.pClass     = block.header.bRegisterClass ? new ServerClass( built.pClassName, built.pTable ) : NULL;
	te.m_nBuilt = iHandle + 1;

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
	CServerTempEnts *pTe = TempEnts();
	if ( !pTe || pTe->m_bClosed )
		return;
	CServerTempEnts &te = *pTe;

	te.m_bClosed = true;

	int nClasses = 0;

	for ( int i = 0; i < te.m_nBuilt; i++ )
	{
		if ( te.m_Built[i].pClass )
			nClasses++;
	}

	Msg( "[SourceSharp] temp entities: %d table(s), %d ServerClass(es); "
	     "registration closed\n", te.m_nBuilt, nClasses );
}

//=============================================================================//
// THE PREDICTION HOST WENT (plan_server_native_v2 T6). It held the temp entity
// subsystem's suppress host for one reader, ApplyMultiDamage, which saved and
// restored it around TakeDamage; the server's multi-damage accumulator was
// never filled and went in T6 3e, and with it the only caller of
// SourceSharpTe_PredictionSystem. Nothing set a host, so the host was always
// NULL: suppress_host_dead_test.cpp holds that no writer comes back.
//=============================================================================//

//=============================================================================//
// THE ROUND TRIP FOR THE NATIVE TE_*() CALLERS (shell_te_forwards.cpp).
//
// They pass their own IRecipientFilter, a native object managed code cannot
// read, so managed packs the fields into the scratch blob and says which class;
// this sends them through the engine with the caller's filter. A refusal from
// managed sends NOTHING, rather than a blob it made up. One scratch blob: this
// runs on the server's thread inside one call and nothing yields in between.
//=============================================================================//

bool SourceSharpTe_Forward( int nId, IRecipientFilter &filter, float flDelay,
                            const float *pFloats, int nFloats,
                            const int *pInts, int nInts )
{
	// E13: the server's managed-built area.
	IManagedTempEnts *pTempEnts = ManagedTempEnts();
	CServerTempEnts *pTe = TempEnts();

	if ( !pTempEnts || !pTe )
		return false;

	int hClass = -1;

	const int nBytes = pTempEnts->OnNativeTempEntity( nId, pFloats, nFloats, pInts, nInts,
	                                                  pTe->m_Scratch, (int)sizeof( pTe->m_Scratch ),
	                                                  &hClass );

	if ( nBytes <= 0 || hClass < 0 || hClass >= pTe->m_nBuilt )
		return false;

	if ( !pTe->m_Built[ hClass ].pTable || !pTe->m_Built[ hClass ].pClass )
		return false;

	Assert( !filter.IsReliable() && !filter.IsInitMessage() );
	Assert( flDelay >= -1 && flDelay <= 1 );

	engine->PlaybackTempEntity( filter, flDelay, pTe->m_Scratch, pTe->m_Built[ hClass ].pTable,
	                            pTe->m_Built[ hClass ].pClass->m_ClassID );
	return true;
}

//-----------------------------------------------------------------------------
// The other half of SourceSharp_IsManagedServerClass (plan_gamedll.md Part 5's
// meter). Every ServerClass built here exists because a C# [TempEntity]
// declared it, so it belongs in the managed column. By identity, not by name.
//-----------------------------------------------------------------------------
bool SourceSharp_IsTempEntServerClass( const ServerClass *pClass )
{
	CServerTempEnts *pTe = TempEnts();
	if ( pClass == NULL || pTe == NULL )
		return false;

	for ( int i = 0; i < pTe->m_nBuilt; i++ )
	{
		if ( pTe->m_Built[ i ].pClass == pClass )
			return true;
	}

	return false;
}
