//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — libss_fwd_engine: the engine's interfaces, forwarded ==//
// SS_REASON b  an interface resolved by name at load
//
// plan_engine_forwarders.md. The forwarders and their tables are generated
// (fwd_engine.g.inc, by tools/engine-forwarders); this file includes the
// interface headers they need and answers the library's one export.
//
// The library holds no state and connects to nothing: every forwarder is
// handed the engine object it calls through, and the tables are constant data.
// So it needs the SDK headers to compile and no SDK library to link.
//=============================================================================//
#include "ss_fwd.h"

#include "engine/ivmodelinfo.h"

#include <string.h>

#include "fwd_engine.g.inc"

SS_FWD_ENGINE_API const SSForwarderTable *SSFwdEngine_Table( const char *pszName )
{
	if ( !pszName )
		return NULL;

	for ( const SSForwarderNamed &named : s_ForwarderTables )
	{
		if ( !strcmp( named.pszName, pszName ) )
			return named.pTable;
	}

	return NULL;
}
