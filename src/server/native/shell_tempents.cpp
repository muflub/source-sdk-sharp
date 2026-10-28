//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// Builds and stores the temp entity SendTables and ServerClasses C# plans, one
// length-prefixed block per table. Native because the engine reads SendTables by
// C++ layout and walks the server class list once, at DLL init, so they are
// built in that window and live for the process.

#include "eiface.h"

extern IVEngineServer *engine;
#include "shell_tempents.h"
#include "shell_tempents_internal.h"
#include "sourcesharp.h"
#include "dt_send.h"
#include "server_class.h"
#include "shell_tempents_store.h"
#include "imanaged_areas.h"
#include "server_state.h"

#include <string.h>
#include <set>
#include <string>

// The tables live in the DLL tier; NULL outside DLLInit..DLLShutdown.
static CServerTempEnts *TempEnts()
{
	CServerDllState *pDll = ShellGameDll_Dll();
	return pDll ? &pDll->m_TempEnts : NULL;
}

static const char *InternName( CServerTempEnts &te, const char *pName )
{
	return te.m_Names.emplace( pName ).first->c_str();
}

// The accessors the dump reads (shell_tempents_internal.h).
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

int32_t SourceSharp_TempEntBuild( const void *pBlock, int32_t nBlockBytes,
                                  SourceSharpTeBuiltTable_t *pOut )
{
	SourceSharpTeTableBlock_t block;
	CServerTempEnts *pTe = TempEnts();
	if ( !pOut || !pTe || !SourceSharpTe_ReadTableBlock( pBlock, nBlockBytes, &block ) )
		return -1;
	CServerTempEnts &te = *pTe;

	// A class built by an earlier load answers its table: the engine walked the class
	// list once, so a managed reload cannot add tables.
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

	// A ServerClass built after the engine's one walk of the class list is never seen.
	if ( te.m_bClosed || te.m_nBuilt >= SOURCESHARP_TE_MAX_TABLES )
		return -1;

	const int nProps = block.header.nProps;
	SendProp *pProps = new SendProp[ nProps > 0 ? nProps : 1 ];

	for ( int i = 0; i < nProps; i++ )
	{
		const SourceSharpSendPropRow_t row = SourceSharpTe_BlockRow( block, i );
		const char *pName = SourceSharpTe_BlockName( block, row.oName );

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

// Closes the window, once managed has pushed its tables.
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

// The native TE_*() callers pass an IRecipientFilter managed cannot read, so
// managed packs the fields into the scratch blob and this sends them with the
// caller's filter. A managed refusal sends nothing. One scratch blob suffices:
// this runs on the server thread and nothing yields in between.

bool SourceSharpTe_Forward( int nId, IRecipientFilter &filter, float flDelay,
                            const float *pFloats, int nFloats,
                            const int *pInts, int nInts )
{
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

// Whether a ServerClass was built here from a C# [TempEntity], by identity.
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
