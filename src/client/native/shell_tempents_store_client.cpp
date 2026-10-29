//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// A managed temp entity table plan, stored into RecvProps.

#include "shell_tempents_store_client.h"

// A row's type is DPT_*'s and its flags SPROP_*'s; these fail the build if
// dt_common.h moves under the values C# writes.
COMPILE_TIME_ASSERT( DPT_Int == 0 );
COMPILE_TIME_ASSERT( DPT_Float == 1 );
COMPILE_TIME_ASSERT( DPT_Vector == 2 );
COMPILE_TIME_ASSERT( DPT_Array == 5 );
COMPILE_TIME_ASSERT( DPT_DataTable == 6 );

SourceSharpRecvPropRow_t SourceSharpTe_RecvBlockRow( const SourceSharpTeTableBlock_t &block, int i )
{
	SourceSharpRecvPropRow_t row;
	memcpy( &row, block.pRows + (size_t)i * sizeof( row ), sizeof( row ) );
	return row;
}

RecvVarProxyFn SourceSharpTe_RecvVarProxy( int32_t nProxy )
{
	switch ( nProxy )
	{
		case SOURCESHARP_RECVPROXY_INT32_TO_INT8:
			return RecvProxy_Int32ToInt8;
		case SOURCESHARP_RECVPROXY_INT32_TO_INT16:
			return RecvProxy_Int32ToInt16;
		case SOURCESHARP_RECVPROXY_INT32_TO_INT32:
			return RecvProxy_Int32ToInt32;
		case SOURCESHARP_RECVPROXY_FLOAT_TO_FLOAT:
			return RecvProxy_FloatToFloat;
		case SOURCESHARP_RECVPROXY_VECTOR_TO_VECTOR:
			return RecvProxy_VectorToVector;
	}

	return NULL;
}

// The RecvProp constructor's defaults stand for whatever the row does not set;
// the array prop gets InternalRecvPropArray's shape (InitArray, no length proxy).
bool SourceSharpTe_StoreRecvProp( const SourceSharpRecvPropRow_t &row, const char *pName, RecvTable *pNested,
								  RecvProp *pOut )
{
	if ( !pName || !pOut )
		return false;

	RecvProp prop;

	if ( row.nType == DPT_Array )
	{
		if ( row.nProxy != SOURCESHARP_RECVPROXY_NONE )
			return false;

		prop.InitArray( row.nElements, row.nElementStride );
		prop.m_pVarName = pName;
		prop.SetArrayLengthProxy( NULL );
		*pOut = prop;
		return true;
	}

	prop.m_pVarName = pName;
	prop.m_RecvType = (SendPropType)row.nType;
	prop.m_Flags = row.nFlags;
	prop.SetOffset( row.nOffset );
	prop.SetNumElements( row.nElements );
	prop.SetElementStride( row.nElementStride );

	if ( row.nType == DPT_DataTable )
	{
		if ( row.nProxy != SOURCESHARP_RECVPROXY_STATIC_DATATABLE || !pNested )
			return false;

		prop.SetDataTable( pNested );
		prop.SetDataTableProxyFn( DataTableRecvProxy_StaticDataTable );
	}
	else
	{
		RecvVarProxyFn fn = SourceSharpTe_RecvVarProxy( row.nProxy );

		if ( !fn )
			return false;

		prop.SetProxyFn( fn );
	}

	*pOut = prop;
	return true;
}
