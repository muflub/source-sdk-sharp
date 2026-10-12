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

#ifdef CLIENT_DLL
	#include "dt_recv.h"
#else
	#include "dt_send.h"
#endif

// The live shell's blob base. Declared rather than included: both sides export
// this name with this signature, and their headers are a thousand lines each.
int SourceSharp_StandaloneBlobOffset();


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

//=============================================================================//
// The schema-hash rejection path (Part 4 and Part 14 Step 6).
//
// One implementation for both client shells. The runtime-networked entity and
// the parallel hierarchy ask the same question about the same two numbers, and
// two copies of the answer would be two places for the refusal to stop being a
// refusal.
//=============================================================================//
#ifdef CLIENT_DLL

namespace
{
	struct SchemaRefusal_t
	{
		unsigned int nTypeId;
		unsigned int nExpected;
		unsigned int nSeen;
		int          nCount;
	};

	// Small and fixed. A build mismatch is a property of the BUILD, so the
	// number of distinct types it can affect is the number of types there are —
	// and if that overflows this, the first sixteen have already made the point.
	enum { SCHEMA_REFUSALS_MAX = 16 };

	SchemaRefusal_t g_Refusals[ SCHEMA_REFUSALS_MAX ];
	int g_nRefusalTypes   = 0;
	int g_nRefusalUpdates = 0;
}

bool SourceSharpSchema_Accept( unsigned int nTypeId, unsigned int nSchemaHash,
                               int nEntIndex, const char **ppOutClassName )
{
	const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ById( nTypeId );

	if ( ppOutClassName )
		*ppOutClassName = pType ? pType->szClass : NULL;

	// A type this side never published. Not a schema mismatch and not an error:
	// a class may exist on the server with no client half at all, and those
	// have always drawn nothing and decoded nothing. Refused for decoding, not
	// counted as a refusal.
	if ( !pType )
		return false;

	if ( pType->nSchemaHash == nSchemaHash )
		return true;

	g_nRefusalUpdates++;

	for ( int i = 0; i < g_nRefusalTypes; i++ )
	{
		if ( g_Refusals[i].nTypeId != nTypeId )
			continue;

		g_Refusals[i].nCount++;

		// Said once per type rather than once per update. The second line adds
		// nothing and the ten-thousandth buries everything else.
		return false;
	}

	if ( g_nRefusalTypes < SCHEMA_REFUSALS_MAX )
	{
		SchemaRefusal_t &r = g_Refusals[ g_nRefusalTypes++ ];
		r.nTypeId   = nTypeId;
		r.nExpected = pType->nSchemaHash;
		r.nSeen     = nSchemaHash;
		r.nCount    = 1;
	}

	Warning( "[SourceSharp/Client] SCHEMA MISMATCH on entity %d: '%s' (type 0x%08x) arrived "
	         "with layout fingerprint 0x%08x and this build's registry says 0x%08x. "
	         "THE PAYLOAD WAS NOT DECODED — the two builds' C# does not agree about where "
	         "this type's fields live, and decoding anyway would put plausible values in "
	         "the wrong fields with nothing reporting it. Rebuild both sides from the same "
	         "SourceSharp.Shared. (cs_cl_schema_status)\n",
		nEntIndex, pType->szClass, nTypeId, nSchemaHash, pType->nSchemaHash );

	return false;
}

int SourceSharpSchema_RefusedUpdates( void ) { return g_nRefusalUpdates; }
int SourceSharpSchema_RefusedTypes( void )   { return g_nRefusalTypes; }

int SourceSharpSchema_Refusal( int iIndex, unsigned int *pOutTypeId,
                               unsigned int *pOutExpected, unsigned int *pOutSeen,
                               const char **ppOutClassName, int *pOutCount )
{
	if ( iIndex < 0 || iIndex >= g_nRefusalTypes )
		return 0;

	const SchemaRefusal_t &r = g_Refusals[ iIndex ];
	const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ById( r.nTypeId );

	if ( pOutTypeId )
		*pOutTypeId = r.nTypeId;
	if ( pOutExpected )
		*pOutExpected = r.nExpected;
	if ( pOutSeen )
		*pOutSeen = r.nSeen;
	if ( ppOutClassName )
		*ppOutClassName = pType ? pType->szClass : "";
	if ( pOutCount )
		*pOutCount = r.nCount;

	return 1;
}

//-----------------------------------------------------------------------------
// The banner, on demand and in the console. Part 13 wants this in the dev UI as
// well, and it is there — but the dev API is served by the SERVER module and
// this fact belongs to the client, so the console is the place it can always be
// read from. See the note in docs/divergences.md.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_schema_status,
	"Which types this client has refused updates for, and what the two builds disagree about" )
{
	Msg( "[SourceSharp] schema: %d type(s) published here, %d refusal(s) over %d type(s)\n",
		SourceSharp_NetTypes().Count(), g_nRefusalUpdates, g_nRefusalTypes );

	for ( int i = 0; i < g_nRefusalTypes; i++ )
	{
		const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ById( g_Refusals[i].nTypeId );

		Warning( "[SourceSharp]   '%s' (0x%08x): expected 0x%08x, saw 0x%08x, %d update(s) refused\n",
			pType ? pType->szClass : "?", g_Refusals[i].nTypeId,
			g_Refusals[i].nExpected, g_Refusals[i].nSeen, g_Refusals[i].nCount );
	}

	if ( g_nRefusalTypes == 0 )
		Msg( "[SourceSharp]   nothing has been refused\n" );
}

#endif // CLIENT_DLL

#ifdef CLIENT_DLL

//-----------------------------------------------------------------------------
// The receiving half. The engine checks BOTH ends before it puts a
// prop on the fast int32 list, so a correct send prop paired with a recv prop
// that resolved to Int32ToInt8 is still the slow path — and the send-side dump
// would happily report everything fine.
//-----------------------------------------------------------------------------
int SourceSharpPayload_BuildRecvProps( RecvProp *pProps, int nWords, int nBlobBase )
{
	if ( !pProps )
		return -1;

	if ( nWords < 0 || nWords > SOURCESHARP_PAYLOAD_MAX_WORDS )
		return -1;

	if ( !SourceSharpPayload_OffsetsFit( nBlobBase, nWords ) )
		return -1;

	for ( int i = 0; i < nWords; i++ )
	{
		pProps[i] = RecvPropInt( SourceSharpPayload_PropName( i ),
		                         SourceSharpPayload_WordOffset( nBlobBase, i ),
		                         SOURCESHARP_PAYLOAD_WORD_BYTES );
	}

	return nWords;
}

bool SourceSharpPayload_IsFastRecvProxy( const RecvProp *pProp )
{
	if ( !pProp )
		return false;

	return pProp->GetProxyFn() == g_StandardRecvProxies.m_Int32ToInt32;
}

const char *SourceSharpPayload_RecvProxyName( const RecvProp *pProp )
{
	if ( !pProp )
		return "(null)";

	const RecvVarProxyFn fn = pProp->GetProxyFn();

	if ( fn == g_StandardRecvProxies.m_Int32ToInt32 )	return "m_Int32ToInt32";
	if ( fn == g_StandardRecvProxies.m_Int32ToInt16 )	return "m_Int32ToInt16";
	if ( fn == g_StandardRecvProxies.m_Int32ToInt8 )	return "m_Int32ToInt8";
	if ( fn == g_StandardRecvProxies.m_FloatToFloat )	return "m_FloatToFloat";
	if ( fn == g_StandardRecvProxies.m_VectorToVector )	return "m_VectorToVector";

	return "custom (SLOW)";
}

//-----------------------------------------------------------------------------
// The client's half of cs_sv_dump_payload. On a listen server both run in the
// same process against the same entity layout, which is the only place the two
// ends of Part 1A's claim can be checked against each other.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_dump_payload,
	"Build the client's generic-table payload prop set standalone and dump its offsets and proxies" )
{
	int nBytes = 32;

	if ( args.ArgC() > 1 )
		nBytes = ss::AtoI( args[1] );

	const int nWords = SourceSharpPayload_WordCount( nBytes );

	if ( nWords < 0 )
	{
		Warning( "[SourceSharp] payload dump: %d bytes is more than the largest tier (%d)\n",
			nBytes, SOURCESHARP_PAYLOAD_MAX_WORDS * SOURCESHARP_PAYLOAD_WORD_BYTES );
		return;
	}

	// The client's own. C_StandaloneEntity and CStandaloneEntity are separate
	// classes with the same blob arithmetic and different bases, which is
	// exactly the agreement Step 3 has to prove holds.
	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	RecvProp *pProps = new RecvProp[ nWords > 0 ? nWords : 1 ];
	const int nBuilt = SourceSharpPayload_BuildRecvProps( pProps, nWords, nBlobBase );

	if ( nBuilt != nWords )
	{
		Warning( "[SourceSharp] payload dump: build refused %d word(s) at blob base %d\n",
			nWords, nBlobBase );
		delete[] pProps;
		return;
	}

	Msg( "[SourceSharp] --- client payload prop dump: %d byte(s) -> %d word prop(s), "
	     "blob base %d ---\n", nBytes, nWords, nBlobBase );

	int nFast = 0;

	for ( int i = 0; i < nBuilt; i++ )
	{
		const RecvProp *pProp = &pProps[i];
		const bool bFast = SourceSharpPayload_IsFastRecvProxy( pProp );

		if ( bFast )
			nFast++;

		Msg( "[SourceSharp]     %-20s offset=%-4d proxy=%s%s\n",
			pProp->GetName(), pProp->GetOffset(),
			SourceSharpPayload_RecvProxyName( pProp ),
			bFast ? "" : "   <-- NOT ON THE FAST LOCAL-TRANSFER LIST" );
	}

	if ( nFast == nBuilt )
	{
		Msg( "[SourceSharp] client payload dump: all %d prop(s) resolved to m_Int32ToInt32\n",
			nBuilt );
	}
	else
	{
		Warning( "[SourceSharp] client payload dump: %d of %d prop(s) did NOT resolve to "
		         "m_Int32ToInt32\n", nBuilt - nFast, nBuilt );
	}

	delete[] pProps;
}

#endif // CLIENT_DLL
