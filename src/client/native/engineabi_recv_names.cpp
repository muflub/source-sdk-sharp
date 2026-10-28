//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// The Valve recv-table names client.so links against, forwarding to engineabi/recv.h.
// The proxies are real functions rather than aliases because the engine compares their
// addresses: it copies a value directly when a prop's proxy is the standard one.

#include "engineabi/recv.h"

#include "client_class.h"
#include "renamed_recvtable_compat.h"

// Zero-initialised, so empty before any static constructor adds to them.
ClientClass *g_pClientClassHead = 0;
CRenamedRecvTableInfo *g_pRenamedRecvTableInfoHead = 0;

// Read by the network-var assertions in the shared headers (debug builds).
bool g_bUseNetworkVars = true;

void RecvProxy_FloatToFloat( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	engineabi::recv::FloatToFloat( pData, pStruct, pOut );
}

void RecvProxy_VectorToVector( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	engineabi::recv::VectorToVector( pData, pStruct, pOut );
}

void RecvProxy_Int32ToInt8( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	engineabi::recv::Int32ToInt8( pData, pStruct, pOut );
}

void RecvProxy_Int32ToInt16( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	engineabi::recv::Int32ToInt16( pData, pStruct, pOut );
}

void RecvProxy_Int32ToInt32( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	engineabi::recv::Int32ToInt32( pData, pStruct, pOut );
}

void RecvProxy_StringToString( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	engineabi::recv::StringToString( pData, pStruct, pOut );
}

void DataTableRecvProxy_StaticDataTable( const RecvProp *pProp, void **pOut, void *pData, int objectID )
{
	engineabi::recv::StaticDataTable( pProp, pOut, pData, objectID );
}

// The table GetStandardRecvProxies answers.
CStandardRecvProxies::CStandardRecvProxies()
{
	m_Int32ToInt8 = RecvProxy_Int32ToInt8;
	m_Int32ToInt16 = RecvProxy_Int32ToInt16;
	m_Int32ToInt32 = RecvProxy_Int32ToInt32;
	m_FloatToFloat = RecvProxy_FloatToFloat;
	m_VectorToVector = RecvProxy_VectorToVector;
}

CStandardRecvProxies g_StandardRecvProxies;

RecvProp RecvPropFloat( const char *pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn varProxy )
{
	return engineabi::recv::Float( pVarName, offset, sizeofVar, flags, varProxy, g_StandardRecvProxies );
}

RecvProp RecvPropVector( const char *pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn varProxy )
{
	return engineabi::recv::Vector( pVarName, offset, sizeofVar, flags, varProxy, g_StandardRecvProxies );
}

RecvProp RecvPropInt( const char *pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn varProxy )
{
	return engineabi::recv::Int( pVarName, offset, sizeofVar, flags, varProxy, g_StandardRecvProxies );
}

RecvProp RecvPropString( const char *pVarName, int offset, int bufferSize, int flags, RecvVarProxyFn varProxy )
{
	return engineabi::recv::String( pVarName, offset, bufferSize, flags, varProxy );
}

RecvProp RecvPropDataTable( const char *pVarName, int offset, int flags, RecvTable *pTable,
							DataTableRecvVarProxyFn varProxy )
{
	return engineabi::recv::DataTable( pVarName, offset, flags, pTable, varProxy );
}

RecvProp InternalRecvPropArray( const int elementCount, const int elementStride, const char *pName,
								ArrayLengthRecvProxyFn proxy )
{
	return engineabi::recv::ArrayLength( elementCount, elementStride, pName, proxy );
}

// m_StringBufferSize and m_pParentArrayPropName, indeterminate upstream, are zeroed so a
// fresh prop's bytes are deterministic; every builder that reads them sets them first.
RecvProp::RecvProp()
{
	m_pVarName = NULL;
	m_RecvType = DPT_Int;
	m_Flags = 0;
	m_StringBufferSize = 0;
	m_bInsideArray = false;
	m_pExtraData = NULL;
	m_pArrayProp = NULL;
	m_ArrayLengthProxy = NULL;
	m_ProxyFn = NULL;
	m_DataTableProxyFn = NULL;
	m_pDataTable = NULL;
	m_Offset = 0;
	m_ElementStride = -1;
	m_nElements = 1;
	m_pParentArrayPropName = NULL;
}

RecvTable::RecvTable()
{
	Construct( NULL, 0, NULL );
}

RecvTable::RecvTable( RecvProp *pProps, int nProps, const char *pNetTableName )
{
	Construct( pProps, nProps, pNetTableName );
}

RecvTable::~RecvTable() {}

void RecvTable::Construct( RecvProp *pProps, int nProps, const char *pNetTableName )
{
	m_pProps = pProps;
	m_nProps = nProps;
	m_pNetTableName = pNetTableName;
	m_pDecoder = NULL;
	m_bInitialized = false;
	m_bInMainList = false;
}
