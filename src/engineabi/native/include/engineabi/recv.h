//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client's recv-table builders
//
// plan_client_novalve.md 3.1 / 3.5 / C8: what public/dt_recv.cpp was to
// client.so. The engine walks ClientClass / RecvTable / RecvProp by layout and
// calls the proxy pointers in them, so the TYPES stay dt_recv.h's and
// client_class.h's (allowlisted roots; K2 in docs/abi-parity.tsv holds their
// layout to upstream). What this module writes is the code that fills them:
// the prop builders, the standard proxies and the table and prop
// initialisation.
//
// The builders live in this namespace so the oracle
// (src/test/oracle/native/recv_builders_oracle_test.cpp) can link them beside
// upstream's dt_recv.cpp and compare every field and every proxy's output.
// client.so's callers keep the Valve names (RecvPropInt, RecvProxy_FloatToFloat,
// g_StandardRecvProxies, ...): engineabi_recv_names.cpp defines each proxy as a
// forward to the function here (so its pointer is the Valve-named one), the
// standard table over those, and the out-of-line members of RecvProp,
// RecvTable and CStandardRecvProxies that dt_recv.h declares.
//
// Only what client.so imports is here (nm over the link line at C8): no
// RecvPropArray3, VectorXY or quaternion props, and no int64 (dt_common.h
// leaves SUPPORTS_INT64 undefined).
//=============================================================================//
#pragma once

#include "dt_recv.h"

namespace engineabi
{
namespace recv
{
// The props. Each answers a prop with RecvProp()'s defaults and the named
// fields set; `sizeofVar` picks Int's proxy from `standard` when none is given.
//
// THE STANDARD PROXIES ARE PASSED, NOT NAMED HERE, because their POINTERS are
// part of the contract: the engine's local backdoor (the local
// network transfer) compares a prop's proxy against the table
// GetStandardRecvProxies hands it and copies directly when they match. So a
// default proxy must be the very function the table holds, which is the
// Valve-named one client.so's callers also pass by name.
RecvProp Float(const char* pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn proxy,
			   const CStandardRecvProxies& standard);
RecvProp Vector(const char* pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn proxy,
				const CStandardRecvProxies& standard);
RecvProp Int(const char* pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn proxy,
			 const CStandardRecvProxies& standard);
RecvProp String(const char* pVarName, int offset, int bufferSize, int flags, RecvVarProxyFn proxy);
RecvProp DataTable(const char* pVarName, int offset, int flags, RecvTable* pTable, DataTableRecvVarProxyFn proxy);
RecvProp ArrayLength(int elementCount, int elementStride, const char* pName, ArrayLengthRecvProxyFn proxy);

// The standard proxies: the value the engine decoded, written at pOut.
void FloatToFloat(const CRecvProxyData* pData, void* pStruct, void* pOut);
void VectorToVector(const CRecvProxyData* pData, void* pStruct, void* pOut);
void Int32ToInt8(const CRecvProxyData* pData, void* pStruct, void* pOut);
void Int32ToInt16(const CRecvProxyData* pData, void* pStruct, void* pOut);
void Int32ToInt32(const CRecvProxyData* pData, void* pStruct, void* pOut);
void StringToString(const CRecvProxyData* pData, void* pStruct, void* pOut);
void StaticDataTable(const RecvProp* pProp, void** pOut, void* pData, int objectID);
} // namespace recv
} // namespace engineabi
