//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
//
// shell_nettable.cpp's diagnostic console commands. Debug builds only; managed
// NativeCommands declares the matching rows under #if DEBUG.

#include "tier1/convar.h"
#include "generic_nettable.h"
#include "native_command_body.h"
#include "ss_string.h"
#include "server_services.h"

// Implemented beside the standalone hierarchy, which walks its own entities.
void SourceSharp_StandalonePayloadAgreeReport( const CServerEntityContext &context, int nWantedIndex, int *pOutChecked,
											   int *pOutAgreed );
bool SourceSharp_StandalonePokePayload( const CServerEntityContext &context, int nIndex, int iWord, uint32 nValue );
bool SourceSharp_StandaloneProbePayload( const CServerEntityContext &context, int nIndex, uint32 nSeed,
										 int *pOutWords );

// Whether each payload prop, read at the offset the engine uses, sees the bytes C#
// wrote into the blob.
SS_NATIVE_COMMAND( cs_sv_payload_agree )
{
	const int nWanted = args.ArgC() > 1 ? ss::AtoI( args[1] ) : -1;

	int nChecked = 0;
	int nAgreed = 0;

	SourceSharp_StandalonePayloadAgreeReport( services.m_EntityContext, nWanted, &nChecked, &nAgreed );

	if ( nChecked == 0 )
	{
		Warning( "[SourceSharp] payload agreement: no entity carrying a payload%s\n",
				 nWanted >= 0 ? " at that index" : " is alive" );
		return;
	}

	Msg( "[SourceSharp] payload agreement: %d of %d entity(ies) agree\n", nAgreed, nChecked );
}

// Writes one dword into a live entity's blob and marks it changed, so the delta path
// can be exercised.
SS_NATIVE_COMMAND( cs_sv_poke_payload )
{
	if ( args.ArgC() < 4 )
	{
		Msg( "usage: cs_sv_poke_payload <entindex> <word> <value>\n" );
		return;
	}

	const int nIndex = ss::AtoI( args[1] );
	const int iWord = ss::AtoI( args[2] );
	const uint32 nValue = (uint32)strtoul( args[3], NULL, 0 );

	if ( SourceSharp_StandalonePokePayload( services.m_EntityContext, nIndex, iWord, nValue ) )
		return;

	Warning( "[SourceSharp] nothing carrying a payload at index %d\n", nIndex );
}

// Writes the probe pattern (a function of seed and word index) into a live entity's
// payload; cs_cl_payload_probe recomputes it on the client and names the first word that
// differs.
SS_NATIVE_COMMAND( cs_sv_payload_probe )
{
	if ( args.ArgC() < 2 )
	{
		Msg( "usage: cs_sv_payload_probe <entindex> [seed]   (default seed 1)\n" );
		return;
	}

	const int nIndex = ss::AtoI( args[1] );
	const uint32 nSeed = ( args.ArgC() > 2 ) ? (uint32)strtoul( args[2], NULL, 0 ) : 1u;

	int nWords = 0;

	if ( SourceSharp_StandaloneProbePayload( services.m_EntityContext, nIndex, nSeed, &nWords ) )
		return;

	Warning( "[SourceSharp] nothing carrying a payload at index %d\n", nIndex );
}
