//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Reads the block one planned temp entity table crosses in and stores each row
// into a SendProp. Every value is decided in C#. Separate so a test can link it
// with dt_send.cpp alone.
#pragma once

#include "tempent_table_block.h"
#include "dt_send.h"

// The block, checked (tempent_table_block.h), with the server's rows.
inline bool SourceSharpTe_ReadTableBlock(const void* pBlock, int32_t nBlockBytes, SourceSharpTeTableBlock_t* pOut)
{
	return SourceSharpTe_ReadTableBlock(pBlock, nBlockBytes, (int32_t)sizeof(SourceSharpSendPropRow_t), pOut);
}

// Row i (0 <= i < header.nProps) of a block read above.
SourceSharpSendPropRow_t SourceSharpTe_BlockRow(const SourceSharpTeTableBlock_t& block, int i);

// A proxy id as the function the engine compares with g_StandardSendProxies;
// NULL for an unknown id or the datatable and array ids.
SendVarProxyFn SourceSharpTe_VarProxy(int32_t nProxy);

// One row into one SendProp. pNested is the table a DPT_DataTable row nests
// (the caller resolves hDataTable). False, leaving pOut untouched, for a proxy
// id that does not fit the row's type or a datatable row with no table.
bool SourceSharpTe_StoreProp(const SourceSharpSendPropRow_t& row, const char* pName, SendTable* pNested,
							 SendProp* pOut);
