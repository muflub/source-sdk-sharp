//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — generic_payload.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "tier0/dbg.h"
#include "tier1/convar.h"     // CCommand
#include "dt_send.h"
#include "generic_payload.h"
#include "native_command_body.h"
#include "ss_string.h"

// The live shell's blob base (shell_standalone_class.cpp). Declared rather than
// included, as generic_payload.cpp does: shell_standalone.h is a thousand lines.
int SourceSharp_StandaloneBlobOffset();


//-----------------------------------------------------------------------------
// Builds a payload prop set and dumps what it resolved to.
//
// The point of the command is the proxy column, not the offsets. An offset that
// is wrong shows up the moment anything reads the blob; a prop that quietly fell
// onto the engine's slow local-transfer list behaves identically to one that did
// not, and there is no convar, no warning and no counter anywhere that would say
// so. This is the only thing that reports it.
//
// Output is laid out to be read next to GET /nettable/<typeid> from the dev API —
// same prefix, same columns in the same order — because from Step 3 onward the
// two describe the same entity's state two different ways and have to be
// comparable by eye.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_dump_payload )
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

	// The blob base the hierarchy actually uses, so the offsets printed are the
	// offsets the props would get if a table were built around them today.
	// Using 0 here would print arithmetic that is right and offsets that are
	// not.
	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	SendProp *pProps = new SendProp[ nWords > 0 ? nWords : 1 ];
	const int nBuilt = SourceSharpPayload_BuildSendProps( pProps, nWords, nBlobBase );

	if ( nBuilt != nWords )
	{
		Warning( "[SourceSharp] payload dump: build refused %d word(s) at blob base %d\n",
			nWords, nBlobBase );
		delete[] pProps;
		return;
	}

	Msg( "[SourceSharp] --- payload prop dump: %d byte(s) -> %d word prop(s), "
	     "blob base %d, last offset %d ---\n",
		nBytes, nWords, nBlobBase,
		nWords > 0 ? SourceSharpPayload_WordOffset( nBlobBase, nWords - 1 ) : nBlobBase );

	int nFast = 0;

	for ( int i = 0; i < nBuilt; i++ )
	{
		const SendProp *pProp = &pProps[i];
		const bool bFast = SourceSharpPayload_IsFastSendProxy( pProp );

		if ( bFast )
			nFast++;

		Msg( "[SourceSharp]     %-20s offset=%-4d bits=%-3d type=%d proxy=%s%s\n",
			pProp->GetName(), pProp->GetOffset(), pProp->m_nBits, (int)pProp->GetType(),
			SourceSharpPayload_SendProxyName( pProp ),
			bFast ? "" : "   <-- NOT ON THE FAST LOCAL-TRANSFER LIST" );
	}

	// Stated as a count rather than left to the reader to check: a single word
	// out of 512 on the slow list is exactly the thing a per-line dump hides.
	if ( nFast == nBuilt )
	{
		Msg( "[SourceSharp] payload dump: all %d prop(s) resolved to m_UInt32ToInt32 "
		     "(engine fast int32 local transfer)\n", nBuilt );
	}
	else
	{
		Warning( "[SourceSharp] payload dump: %d of %d prop(s) did NOT resolve to "
		         "m_UInt32ToInt32 — those words would be copied slowly, proxies and "
		         "all\n", nBuilt - nFast, nBuilt );
	}

	delete[] pProps;
}
