//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
// The payload props that need the engine's datatable headers; the arithmetic is in
// generic_payload.h so a gtest can reach it.

#include "tier1/convar.h"

#include "generic_nettable.h"
#include "ss_string.h"

#ifndef CLIENT_DLL
#include "dt_send.h"
#endif

// The client's published type table. Both shells must share one, so the answer does not
// depend on which shell looks a type up. The server's is its CServerNetTables'.
#ifdef CLIENT_DLL
CSourceSharpTypeTable& SourceSharp_NetTypes()
{
	static CSourceSharpTypeTable s_Types;
	return s_Types;
}

const char* SourceSharpPayload_PropName(int iWord)
{
	static CSourceSharpPayloadPropNames s_Names;
	return s_Names.At(iWord);
}
#endif

#ifndef CLIENT_DLL

// One SendPropInt per payload word at blobBase + i*4: four bytes and 32 unsigned bits,
// which select SendProxy_UInt32ToInt32. No custom proxy: one would put every word on the
// engine's slow local-transfer list.
int SourceSharpPayload_BuildSendProps(SendProp* pProps, int nWords, int nBlobBase, CSourceSharpPayloadPropNames& names)
{
	if (!pProps)
		return -1;

	if (nWords < 0 || nWords > SOURCESHARP_PAYLOAD_MAX_WORDS)
		return -1;

	if (!SourceSharpPayload_OffsetsFit(nBlobBase, nWords))
		return -1;

	for (int i = 0; i < nWords; i++)
	{
		pProps[i] = SendPropInt(names.At(i), SourceSharpPayload_WordOffset(nBlobBase, i),
								SOURCESHARP_PAYLOAD_WORD_BYTES, 32, SPROP_UNSIGNED);
	}

	return nWords;
}

bool SourceSharpPayload_IsFastSendProxy(const SendProp* pProp)
{
	if (!pProp)
		return false;

	// The engine's fast path checks the type as well as the proxy.
	return pProp->GetType() == DPT_Int && pProp->GetProxyFn() == g_StandardSendProxies.m_UInt32ToInt32;
}

const char* SourceSharpPayload_SendProxyName(const SendProp* pProp)
{
	if (!pProp)
		return "(null)";

	const SendVarProxyFn fn = pProp->GetProxyFn();

	if (fn == g_StandardSendProxies.m_UInt32ToInt32)
		return "m_UInt32ToInt32";
	if (fn == g_StandardSendProxies.m_Int32ToInt32)
		return "m_Int32ToInt32";
	if (fn == g_StandardSendProxies.m_UInt16ToInt32)
		return "m_UInt16ToInt32";
	if (fn == g_StandardSendProxies.m_Int16ToInt32)
		return "m_Int16ToInt32";
	if (fn == g_StandardSendProxies.m_UInt8ToInt32)
		return "m_UInt8ToInt32";
	if (fn == g_StandardSendProxies.m_Int8ToInt32)
		return "m_Int8ToInt32";
	if (fn == g_StandardSendProxies.m_FloatToFloat)
		return "m_FloatToFloat";
	if (fn == g_StandardSendProxies.m_VectorToVector)
		return "m_VectorToVector";

	// No engine fast path for anything else.
	return "custom (SLOW)";
}

#endif // !CLIENT_DLL
