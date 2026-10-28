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

#include "tempent_registry.h"

#include "server_class.h"
#include "shell_tempents_store.h"
#include "tier0/dbg.h"

const char *CServerTempEnts::InternName( const char *pName )
{
	return m_Names.emplace( pName ).first->c_str();
}

SendTable *CServerTempEnts::BuiltTable( int i ) const
{
	return ( i >= 0 && i < m_nBuilt ) ? m_Built[i].pTable : NULL;
}

ServerClass *CServerTempEnts::BuiltClass( int i ) const
{
	return ( i >= 0 && i < m_nBuilt ) ? m_Built[i].pClass : NULL;
}

int32_t CServerTempEnts::TempEntBuild( const void *pBlock, int32_t nBlockBytes, SourceSharpTeBuiltTable_t *pOut )
{
	SourceSharpTeTableBlock_t block;
	if ( !pOut || !SourceSharpTe_ReadTableBlock( pBlock, nBlockBytes, &block ) )
		return -1;

	// A class built by an earlier load answers its table: the engine walked the class
	// list once, so a managed reload cannot add tables.
	for ( int i = 0; i < m_nBuilt; i++ )
	{
		if ( strcmp( m_Built[i].pClassName, block.pClassName ) )
			continue;

		pOut->bExisted = 1;
		pOut->nShapeHash = m_Built[i].nShapeHash;
		pOut->pSendTable = m_Built[i].pTable;
		pOut->pClassId = m_Built[i].pClass ? &m_Built[i].pClass->m_ClassID : NULL;
		return i;
	}

	// A ServerClass built after the engine's one walk of the class list is never seen.
	if ( m_bClosed || m_nBuilt >= SOURCESHARP_TE_MAX_TABLES )
		return -1;

	const int nProps = block.header.nProps;
	SendProp *pProps = new SendProp[nProps > 0 ? nProps : 1];

	for ( int i = 0; i < nProps; i++ )
	{
		const SourceSharpSendPropRow_t row = SourceSharpTe_BlockRow( block, i );
		const char *pName = SourceSharpTe_BlockName( block, row.oName );

		SendTable *pNested =
			( row.hDataTable >= 0 && row.hDataTable < m_nBuilt ) ? m_Built[row.hDataTable].pTable : NULL;

		if ( !pName || !SourceSharpTe_StoreProp( row, InternName( pName ), pNested, &pProps[i] ) )
		{
			delete[] pProps;
			return -1;
		}
	}

	const int iHandle = m_nBuilt;
	SourceSharpTeBuilt_t &built = m_Built[iHandle];

	built.pProps = pProps;
	built.nProps = nProps;
	built.pTable = new SendTable( pProps, nProps, InternName( block.pTableName ) );
	built.pClassName = InternName( block.pClassName );
	built.nShapeHash = block.header.nShapeHash;
	built.pClass = block.header.bRegisterClass ? new ServerClass( built.pClassName, built.pTable ) : NULL;
	m_nBuilt = iHandle + 1;

	pOut->bExisted = 0;
	pOut->nShapeHash = built.nShapeHash;
	pOut->pSendTable = built.pTable;
	pOut->pClassId = built.pClass ? &built.pClass->m_ClassID : NULL;
	return iHandle;
}

void CServerTempEnts::CloseRegistration()
{
	if ( m_bClosed )
		return;

	m_bClosed = true;

	int nClasses = 0;

	for ( int i = 0; i < m_nBuilt; i++ )
	{
		if ( m_Built[i].pClass )
			nClasses++;
	}

	Msg( "[SourceSharp] temp entities: %d table(s), %d ServerClass(es); "
		 "registration closed\n",
		 m_nBuilt, nClasses );
}

bool CServerTempEnts::IsTempEntServerClass( const ServerClass *pClass ) const
{
	if ( pClass == NULL )
		return false;

	for ( int i = 0; i < m_nBuilt; i++ )
	{
		if ( m_Built[i].pClass == pClass )
			return true;
	}

	return false;
}
