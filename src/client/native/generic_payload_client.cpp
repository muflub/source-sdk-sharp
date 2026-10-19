//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's half of the payload props ============//
// SS_REASON f  a bit-level wire codec
//
// What generic_payload.cpp compiled only under CLIENT_DLL: the schema-hash
// refusal path both client shells ask (SourceSharpSchema_*, cs_cl_schema_status)
// and the receiving half of the payload props (SourceSharpPayload_BuildRecvProps
// and the proxy probes behind cs_cl_dump_payload). Moved here verbatim by
// plan_server_native_v2 T2 (2026-10-18): a shared TU keeps only what both DLLs
// import (user ruling 2026-10-12), and server.so never compiled any of it. The
// interned prop names and the type table stay in generic_payload.cpp, which
// both DLLs compile and which this file reads through generic_payload.h and
// generic_nettable.h.
//
//=============================================================================//

#include "tier1/convar.h"
#include "generic_payload.h"
#include "generic_nettable.h"
#include "ss_string.h"
#include "dt_recv.h"

// The live shell's blob base. Declared rather than included, as
// generic_payload.cpp does: shell_standalone_client.h is a long header.
int SourceSharp_StandaloneBlobOffset();


//=============================================================================//
// The schema-hash rejection path (Part 4 and Part 14 Step 6).
//
// One implementation for both client shells. The runtime-networked entity and
// the parallel hierarchy ask the same question about the same two numbers, and
// two copies of the answer would be two places for the refusal to stop being a
// refusal.
//=============================================================================//
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
