//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the payload props, built but not used ===============//
// SS_REASON f  a bit-level wire codec
//
// See generic_payload.h for why the element is a dword and why the proxy is the
// default one. This file is the small part of that which needs the engine's
// datatable headers; the arithmetic lives in the header so a gtest can reach it.
//
// Nothing here is registered with the engine. No SendTable is constructed, no
// ServerClass is linked into g_pServerClassHead, and no client sees a prop that
// did not exist before — cs_sv_dump_payload builds a prop set on the stack, asks
// it what it resolved to, prints that, and throws it away. Step 3 of Part 14 is
// what puts a table around this. (That command is a diagnostic and lives in
// generic_payload_debug.cpp, which only a debug build of server.so compiles.)
//
//=============================================================================//

#include "tier1/convar.h"
#include "generic_payload.h"
#include "generic_nettable.h"
#include "ss_string.h"

#ifndef CLIENT_DLL
	#include "dt_send.h"
#endif


//-----------------------------------------------------------------------------
// The interned names.
//
// Filled on first ask rather than by a static constructor: this file is linked
// into a game DLL whose static initialisation order relative to tier0 is not
// something to depend on, and the cost is one branch on a path that runs once
// per prop per process.
//-----------------------------------------------------------------------------
static char g_PayloadPropNames[ SOURCESHARP_PAYLOAD_MAX_WORDS ][ SOURCESHARP_PAYLOAD_NAME_BYTES ];
static bool g_bPayloadPropNamesBuilt = false;

static void SourceSharpPayload_BuildPropNames( void )
{
	if ( g_bPayloadPropNamesBuilt )
		return;

	for ( int i = 0; i < SOURCESHARP_PAYLOAD_MAX_WORDS; i++ )
	{
		SourceSharpPayload_FormatPropName( i, g_PayloadPropNames[i], SOURCESHARP_PAYLOAD_NAME_BYTES );
	}

	g_bPayloadPropNamesBuilt = true;
}

//-----------------------------------------------------------------------------
// The published type table, for the same reason and with the same lifetime.
//
// Declared in generic_nettable.h and defined here because that header has no
// translation unit of its own — and because both shells and both modules want
// the SAME table: an entity's type id is looked up by whichever shell received
// it, and a second table per shell would answer differently depending on who
// asked.
//-----------------------------------------------------------------------------
CSourceSharpTypeTable &SourceSharp_NetTypes( void )
{
	static CSourceSharpTypeTable s_Types;
	return s_Types;
}

const char *SourceSharpPayload_PropName( int iWord )
{
	if ( iWord < 0 || iWord >= SOURCESHARP_PAYLOAD_MAX_WORDS )
		return NULL;

	SourceSharpPayload_BuildPropNames();
	return g_PayloadPropNames[ iWord ];
}

#ifndef CLIENT_DLL

//-----------------------------------------------------------------------------
// One SendPropInt per payload word, at blobBase + i*4, four bytes wide, 32 bits
// on the wire, unsigned.
//
// Every one of those five arguments is load-bearing:
//
//   the offset  — reaches into the blob rather than the entity's own members;
//   sizeofVar 4 — selects SendProxy_Int32ToInt32 as the default proxy, and is
//                 also what makes the read four bytes rather than one;
//   nBits 32    — the whole word, since the payload is a memory image and the
//                 generator has already done any narrowing it is going to do;
//   SPROP_UNSIGNED — upgrades the proxy to SendProxy_UInt32ToInt32 AND stops
//                 the encoder treating the top bit as a sign;
//   no proxy    — a custom one would land every word on the slow local-transfer
//                 list. This is the reason the parameter is not exposed.
//-----------------------------------------------------------------------------
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
		pProps[i] = SendPropInt( SourceSharpPayload_PropName( i ),
		                         SourceSharpPayload_WordOffset( nBlobBase, i ),
		                         SOURCESHARP_PAYLOAD_WORD_BYTES,
		                         32,
		                         SPROP_UNSIGNED );
	}

	return nWords;
}

bool SourceSharpPayload_IsFastSendProxy( const SendProp *pProp )
{
	if ( !pProp )
		return false;

	// Both halves of what the engine's fast path asks of the send side.
	// The type check is not redundant: a prop of the right proxy and the wrong
	// type never reaches the comparison at all.
	return pProp->GetType() == DPT_Int &&
	       pProp->GetProxyFn() == g_StandardSendProxies.m_UInt32ToInt32;
}

const char *SourceSharpPayload_SendProxyName( const SendProp *pProp )
{
	if ( !pProp )
		return "(null)";

	const SendVarProxyFn fn = pProp->GetProxyFn();

	if ( fn == g_StandardSendProxies.m_UInt32ToInt32 )	return "m_UInt32ToInt32";
	if ( fn == g_StandardSendProxies.m_Int32ToInt32 )	return "m_Int32ToInt32";
	if ( fn == g_StandardSendProxies.m_UInt16ToInt32 )	return "m_UInt16ToInt32";
	if ( fn == g_StandardSendProxies.m_Int16ToInt32 )	return "m_Int16ToInt32";
	if ( fn == g_StandardSendProxies.m_UInt8ToInt32 )	return "m_UInt8ToInt32";
	if ( fn == g_StandardSendProxies.m_Int8ToInt32 )	return "m_Int8ToInt32";
	if ( fn == g_StandardSendProxies.m_FloatToFloat )	return "m_FloatToFloat";
	if ( fn == g_StandardSendProxies.m_VectorToVector )	return "m_VectorToVector";

	// The answer that matters. Anything not on the list above is a proxy the
	// engine has no fast path for, whatever it is.
	return "custom (SLOW)";
}

// cs_sv_dump_payload, which builds a prop set like the one above and reports
// what each prop's proxy resolved to, is a diagnostic: it lives in
// generic_payload_debug.cpp, which only a debug build of server.so compiles.

#endif // !CLIENT_DLL
