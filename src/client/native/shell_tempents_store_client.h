//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A managed temp entity table plan, stored: the block one planned table crosses
// in (SourceSharpTeTableHeader_t and SourceSharpRecvPropRow_t rows), and one row
// stored into one RecvProp. Every value was decided in C#. Separate so a gtest
// can reach it with the recv builders alone.
#pragma once

#include "sourcesharp.h"
#include "tempent_table_block.h"
#include "dt_recv.h"

// The block, checked (tempent_table_block.h), with the client's rows.
inline bool SourceSharpTe_ReadRecvTableBlock( const void *pBlock, int32_t nBlockBytes,
                                              SourceSharpTeTableBlock_t *pOut )
{
	return SourceSharpTe_ReadTableBlock( pBlock, nBlockBytes, (int32_t)sizeof( SourceSharpRecvPropRow_t ), pOut );
}

// Row i (0 <= i < header.nProps) of a block read above.
SourceSharpRecvPropRow_t SourceSharpTe_RecvBlockRow( const SourceSharpTeTableBlock_t &block, int i );

// A proxy id as the standard recv proxy the engine compares with
// g_StandardRecvProxies; NULL for one this does not know (and for the datatable
// and array ids, which are not var proxies).
RecvVarProxyFn SourceSharpTe_RecvVarProxy( int32_t nProxy );

// One row into one RecvProp. pNested is the table a DPT_DataTable row nests
// (the caller resolves hDataTable). False, leaving pOut untouched, for a proxy
// id that does not fit the row's type or a datatable row with no table.
bool SourceSharpTe_StoreRecvProp( const SourceSharpRecvPropRow_t &row, const char *pName,
                                  RecvTable *pNested, RecvProp *pOut );
