//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// util.h helpers for server TUs that do not include the fork's util.h; never include
// both, as they declare the same helpers. Functions defined in our code keep the fork's
// signatures at global scope; replacements for the fork's inline helpers are in ss::.

#pragma once

#include "server_engine.h"
#include "eiface.h"

#include "engine/IEngineTrace.h"

class IServerNetworkable;

extern IVEngineServer *engine;

// Defined in our TUs.
void UTIL_ClearTrace( trace_t &trace );
bool UTIL_IsCommandIssuedByServerAdmin();
bool UTIL_ShouldShowBlood( int bloodColor );

namespace ss
{

// Case-insensitive string equality, true for the same pointer (NULL included).
inline bool FStrEq( const char *sz1, const char *sz2 )
{
	return ( sz1 == sz2 || V_stricmp( sz1, sz2 ) == 0 );
}

// Edict <-> index; the engine checks it in debug.
inline int ENTINDEX( edict_t *pEdict )
{
	const int nResult = pEdict ? pEdict->m_EdictIndex : 0;
	Assert( nResult == engine->IndexOfEdict( pEdict ) );
	return nResult;
}

inline edict_t *INDEXENT( int iEdictNum )
{
	return engine->PEntityOfEntIndex( iEdictNum );
}

// A line through the world over the caller's filter, for native diagnostics.
inline void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
							ITraceFilter *pFilter, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd );

	ServerTrace()->TraceRay( ray, mask, pFilter, ptr );
}

} // namespace ss
