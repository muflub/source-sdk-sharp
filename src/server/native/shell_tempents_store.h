//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a managed temp entity plan, stored ==================//
//
// plan_server_native_v2 T4b. The block one planned table crosses in
// (sourcesharp.h, SourceSharpTeTableHeader_t), read and checked, and one row
// stored into one SendProp. Carrying out only: every value in a row was decided
// in C# (TePropPlanner, SendPropPlan). Split from shell_tempents.cpp so a gtest
// (tempent_store_test.cpp) can reach it with dt_send.cpp and nothing else.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_TEMPENTS_STORE_H
#define SOURCESHARP_SHELL_TEMPENTS_STORE_H
#ifdef _WIN32
#pragma once
#endif

#include "sourcesharp.h"
#include "dt_send.h"

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

// The block, checked: a whole-byte bit count that fits the bytes given, a
// header, as many rows as it claims, and both names. False for anything else.
bool SourceSharpTe_ReadTableBlock( const void *pBlock, int32_t nBlockBytes,
                                   SourceSharpTeTableBlock_t *pOut );

// Row i (0 <= i < header.nProps) of a block read above.
SourceSharpSendPropRow_t SourceSharpTe_BlockRow( const SourceSharpTeTableBlock_t &block, int i );

// A name at a payload offset: NULL when the offset is outside the payload, the
// text is empty, or it runs off the end.
const char *SourceSharpTe_BlockName( const SourceSharpTeTableBlock_t &block, int32_t oName );

// A proxy id as the function the engine compares with g_StandardSendProxies;
// NULL for one this does not know (and for the datatable and array ids, which
// are not var proxies).
SendVarProxyFn SourceSharpTe_VarProxy( int32_t nProxy );

// One row into one SendProp. pNested is the table a DPT_DataTable row nests
// (the caller resolves hDataTable). False, leaving pOut untouched, for a proxy
// id that does not fit the row's type or a datatable row with no table.
bool SourceSharpTe_StoreProp( const SourceSharpSendPropRow_t &row, const char *pName,
                              SendTable *pNested, SendProp *pOut );

#endif // SOURCESHARP_SHELL_TEMPENTS_STORE_H
