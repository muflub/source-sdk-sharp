//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// engineabi/recv.h's builders and standard proxies, following dt_recv.cpp's rules.

#include "engineabi/recv.h"

namespace engineabi
{
namespace recv
{

namespace
{
// The fields every scalar prop sets the same way.
RecvProp Scalar( const char *pVarName, int offset, int flags, SendPropType type, RecvVarProxyFn proxy )
{
	RecvProp prop;
	prop.m_pVarName = pVarName;
	prop.m_RecvType = type;
	prop.m_Flags = flags;
	prop.SetOffset( offset );
	prop.SetProxyFn( proxy );
	return prop;
}
} // namespace

RecvProp Float( const char *pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn proxy,
				const CStandardRecvProxies &standard )
{
	Assert( proxy != standard.m_FloatToFloat || sizeofVar == 0 || sizeofVar == sizeof( float ) );
	(void)sizeofVar;
	return Scalar( pVarName, offset, flags, DPT_Float, proxy );
}

RecvProp Vector( const char *pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn proxy,
				 const CStandardRecvProxies &standard )
{
	Assert( proxy != standard.m_VectorToVector || sizeofVar == 3 * (int)sizeof( float ) );
	(void)sizeofVar;
	return Scalar( pVarName, offset, flags, DPT_Vector, proxy );
}

RecvProp Int( const char *pVarName, int offset, int sizeofVar, int flags, RecvVarProxyFn proxy,
			  const CStandardRecvProxies &standard )
{
	// No proxy named: the variable's width picks the narrowing write.
	if ( !proxy )
	{
		switch ( sizeofVar )
		{
			case 2:
				proxy = standard.m_Int32ToInt16;
				break;
			case 4:
				proxy = standard.m_Int32ToInt32;
				break;
			case 1:
				proxy = standard.m_Int32ToInt8;
				break;
			default:
				Assert( !"RecvPropInt var has invalid size" );
				proxy = standard.m_Int32ToInt8;
				break;
		}
	}
	return Scalar( pVarName, offset, flags, DPT_Int, proxy );
}

RecvProp String( const char *pVarName, int offset, int bufferSize, int flags, RecvVarProxyFn proxy )
{
	RecvProp prop = Scalar( pVarName, offset, flags, DPT_String, proxy );
	prop.m_StringBufferSize = bufferSize;
	return prop;
}

RecvProp DataTable( const char *pVarName, int offset, int flags, RecvTable *pTable, DataTableRecvVarProxyFn proxy )
{
	RecvProp prop;
	prop.m_pVarName = pVarName;
	prop.m_RecvType = DPT_DataTable;
	prop.m_Flags = flags;
	prop.SetOffset( offset );
	prop.SetDataTableProxyFn( proxy );
	prop.SetDataTable( pTable );
	return prop;
}

RecvProp ArrayLength( int elementCount, int elementStride, const char *pName, ArrayLengthRecvProxyFn proxy )
{
	RecvProp prop;
	prop.InitArray( elementCount, elementStride );
	prop.m_pVarName = pName;
	prop.SetArrayLengthProxy( proxy );
	return prop;
}

void FloatToFloat( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	const float f = pData->m_Value.m_Float;
	Assert( IsFinite( f ) );
	*static_cast<float *>( pOut ) = f;
}

void VectorToVector( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	const float *pIn = pData->m_Value.m_Vector;
	Assert( IsFinite( pIn[0] ) && IsFinite( pIn[1] ) && IsFinite( pIn[2] ) );
	float *pDest = static_cast<float *>( pOut );
	for ( int i = 0; i < 3; i++ )
		pDest[i] = pIn[i];
}

// The engine decodes every integer as 32 bits; these keep the low bytes.
void Int32ToInt8( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	*static_cast<uint8 *>( pOut ) = static_cast<uint8>( pData->m_Value.m_Int );
}

void Int32ToInt16( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	*static_cast<uint16 *>( pOut ) = static_cast<uint16>( pData->m_Value.m_Int );
}

void Int32ToInt32( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	*static_cast<uint32 *>( pOut ) = static_cast<uint32>( pData->m_Value.m_Int );
}

// Copies up to the terminator, never past the prop's buffer, and always
// leaves the buffer's last byte a terminator. A prop with no buffer size
// writes nothing.
void StringToString( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	const int nSize = pData->m_pRecvProp->m_StringBufferSize;
	if ( nSize <= 0 )
		return;

	char *pDest = static_cast<char *>( pOut );
	const char *pSrc = pData->m_Value.m_pString;
	for ( int i = 0; i < nSize; i++ )
	{
		pDest[i] = pSrc[i];
		if ( !pSrc[i] )
			break;
	}
	pDest[nSize - 1] = 0;
}

// A data table embedded in its owner: the sub-object is at the prop's address.
void StaticDataTable( const RecvProp *pProp, void **pOut, void *pData, int objectID )
{
	*pOut = pData;
}

} // namespace recv
} // namespace engineabi
