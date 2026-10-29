//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets

#include "shell_tempents_store.h"

// dt_send.cpp's unsigned proxies, which dt_send.h does not declare.
void SendProxy_UInt8ToInt32(const SendProp* pProp, const void* pStruct, const void* pData, DVariant* pOut, int iElement,
							int objectID);
void SendProxy_UInt16ToInt32(const SendProp* pProp, const void* pStruct, const void* pData, DVariant* pOut,
							 int iElement, int objectID);
void SendProxy_UInt32ToInt32(const SendProp* pProp, const void* pStruct, const void* pData, DVariant* pOut,
							 int iElement, int objectID);

// C# writes dt_common.h's DPT_* numbers.
COMPILE_TIME_ASSERT(DPT_Int == 0);
COMPILE_TIME_ASSERT(DPT_Float == 1);
COMPILE_TIME_ASSERT(DPT_Vector == 2);
COMPILE_TIME_ASSERT(DPT_String == 4);
COMPILE_TIME_ASSERT(DPT_Array == 5);
COMPILE_TIME_ASSERT(DPT_DataTable == 6);

SourceSharpSendPropRow_t SourceSharpTe_BlockRow(const SourceSharpTeTableBlock_t& block, int i)
{
	SourceSharpSendPropRow_t row;
	memcpy(&row, block.pRows + (size_t)i * sizeof(row), sizeof(row));
	return row;
}

SendVarProxyFn SourceSharpTe_VarProxy(int32_t nProxy)
{
	switch (nProxy)
	{
		case SOURCESHARP_SENDPROXY_INT8_TO_INT32:
			return SendProxy_Int8ToInt32;
		case SOURCESHARP_SENDPROXY_INT16_TO_INT32:
			return SendProxy_Int16ToInt32;
		case SOURCESHARP_SENDPROXY_INT32_TO_INT32:
			return SendProxy_Int32ToInt32;
		case SOURCESHARP_SENDPROXY_UINT8_TO_INT32:
			return SendProxy_UInt8ToInt32;
		case SOURCESHARP_SENDPROXY_UINT16_TO_INT32:
			return SendProxy_UInt16ToInt32;
		case SOURCESHARP_SENDPROXY_UINT32_TO_INT32:
			return SendProxy_UInt32ToInt32;
		case SOURCESHARP_SENDPROXY_FLOAT_TO_FLOAT:
			return SendProxy_FloatToFloat;
		case SOURCESHARP_SENDPROXY_VECTOR_TO_VECTOR:
			return SendProxy_VectorToVector;
		case SOURCESHARP_SENDPROXY_ANGLE_TO_FLOAT:
			return SendProxy_AngleToFloat;
		case SOURCESHARP_SENDPROXY_QANGLES:
			return SendProxy_QAngles;
		case SOURCESHARP_SENDPROXY_STRING_TO_STRING:
			return SendProxy_StringToString;
	}

	return NULL;
}

// The SendProp constructor's defaults stand for whatever the row does not set.
// The array prop goes through InternalSendPropArray, the one constructor that
// installs dt_send.cpp's file-local SendProxy_Empty.
bool SourceSharpTe_StoreProp(const SourceSharpSendPropRow_t& row, const char* pName, SendTable* pNested, SendProp* pOut)
{
	if (!pName || !pOut)
		return false;

	if (row.nType == DPT_Array)
	{
		if (row.nProxy != SOURCESHARP_SENDPROXY_EMPTY)
			return false;

		*pOut = InternalSendPropArray(row.nElements, row.nElementStride, pName, NULL);
		return true;
	}

	SendVarProxyFn fn = NULL;

	if (row.nType == DPT_DataTable)
	{
		if (row.nProxy != SOURCESHARP_SENDPROXY_DATATABLE_TO_DATATABLE || !pNested)
			return false;
	}
	else if ((fn = SourceSharpTe_VarProxy(row.nProxy)) == NULL)
	{
		return false;
	}

	SendProp prop;

	prop.m_Type = (SendPropType)row.nType;
	prop.m_pVarName = pName;
	prop.SetOffset(row.nOffset);
	prop.SetFlags(row.nFlags);
	prop.m_nBits = row.nBits;
	prop.m_fLowValue = row.flLowValue;
	prop.m_fHighValue = row.flHighValue;
	prop.m_fHighLowMul = row.flHighLowMul;
	prop.m_nElements = row.nElements;
	prop.m_ElementStride = row.nElementStride;

	if (row.nType == DPT_DataTable)
	{
		prop.SetDataTable(pNested);
		prop.SetDataTableProxyFn(SendProxy_DataTableToDataTable);
	}
	else
	{
		prop.SetProxyFn(fn);
	}

	*pOut = prop;
	return true;
}
