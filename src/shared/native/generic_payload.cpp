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
#include "server_state.h"
#endif

// Interned prop names, filled on first ask rather than by a static constructor, so
// nothing depends on static-init order relative to tier0.
static char g_PayloadPropNames[SOURCESHARP_PAYLOAD_MAX_WORDS][SOURCESHARP_PAYLOAD_NAME_BYTES];
static bool g_bPayloadPropNamesBuilt = false;

static void SourceSharpPayload_BuildPropNames()
{
	if ( g_bPayloadPropNamesBuilt )
		return;

	for ( int i = 0; i < SOURCESHARP_PAYLOAD_MAX_WORDS; i++ )
	{
		SourceSharpPayload_FormatPropName( i, g_PayloadPropNames[i], SOURCESHARP_PAYLOAD_NAME_BYTES );
	}

	g_bPayloadPropNamesBuilt = true;
}

// The published type table. Both shells must share one, so the answer does not depend on
// which shell looks a type up.
#ifdef CLIENT_DLL
CSourceSharpTypeTable &SourceSharp_NetTypes()
{
	static CSourceSharpTypeTable s_Types;
	return s_Types;
}
#else
// The server's lives on CServerDllState, valid between DLLInit and DLLShutdown.
CSourceSharpTypeTable &SourceSharp_NetTypes()
{
	AssertMsg( ShellGameDll_Dll(), "the net type table was asked for outside DLLInit..DLLShutdown" );
	return ShellGameDll_Dll()->m_NetTypes;
}
#endif

const char *SourceSharpPayload_PropName( int iWord )
{
	if ( iWord < 0 || iWord >= SOURCESHARP_PAYLOAD_MAX_WORDS )
		return NULL;

	SourceSharpPayload_BuildPropNames();
	return g_PayloadPropNames[iWord];
}

#ifndef CLIENT_DLL

// One SendPropInt per payload word at blobBase + i*4: four bytes and 32 unsigned bits,
// which select SendProxy_UInt32ToInt32. No custom proxy: one would put every word on the
// engine's slow local-transfer list.
int SourceSharpPayload_BuildSendProps( SendProp *pProps, int nWords, int nBlobBase )
{
	if ( !pProps )
		return -1;

	if ( nWords < 0 || nWords > SOURCESHARP_PAYLOAD_MAX_WORDS )
		return -1;

	if ( !SourceSharpPayload_OffsetsFit( nBlobBase, nWords ) )
		return -1;

	for ( int i = 0; i < nWords; i++ )
	{
		pProps[i] = SendPropInt( SourceSharpPayload_PropName( i ), SourceSharpPayload_WordOffset( nBlobBase, i ),
								 SOURCESHARP_PAYLOAD_WORD_BYTES, 32, SPROP_UNSIGNED );
	}

	return nWords;
}

bool SourceSharpPayload_IsFastSendProxy( const SendProp *pProp )
{
	if ( !pProp )
		return false;

	// The engine's fast path checks the type as well as the proxy.
	return pProp->GetType() == DPT_Int && pProp->GetProxyFn() == g_StandardSendProxies.m_UInt32ToInt32;
}

const char *SourceSharpPayload_SendProxyName( const SendProp *pProp )
{
	if ( !pProp )
		return "(null)";

	const SendVarProxyFn fn = pProp->GetProxyFn();

	if ( fn == g_StandardSendProxies.m_UInt32ToInt32 )
		return "m_UInt32ToInt32";
	if ( fn == g_StandardSendProxies.m_Int32ToInt32 )
		return "m_Int32ToInt32";
	if ( fn == g_StandardSendProxies.m_UInt16ToInt32 )
		return "m_UInt16ToInt32";
	if ( fn == g_StandardSendProxies.m_Int16ToInt32 )
		return "m_Int16ToInt32";
	if ( fn == g_StandardSendProxies.m_UInt8ToInt32 )
		return "m_UInt8ToInt32";
	if ( fn == g_StandardSendProxies.m_Int8ToInt32 )
		return "m_Int8ToInt32";
	if ( fn == g_StandardSendProxies.m_FloatToFloat )
		return "m_FloatToFloat";
	if ( fn == g_StandardSendProxies.m_VectorToVector )
		return "m_VectorToVector";

	// No engine fast path for anything else.
	return "custom (SLOW)";
}

#endif // !CLIENT_DLL
