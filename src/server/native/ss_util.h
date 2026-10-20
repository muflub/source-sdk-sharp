//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The util.h helpers server.so TUs on the R5 include set use, on public/
// headers and our own (plan_server_novalve.md P5). For R5 TUs only: a TU that
// still includes the fork's util.h must not include this as well, because the
// two declare the same helpers (with the same default arguments) twice.
//
// TWO KINDS OF ENTRY, kept apart on purpose:
//
//   Functions DEFINED IN OUR CODE are declared here at global scope, with the
//   fork's signatures, so callers link to the one definition they always had:
//   UTIL_ClearTrace and UTIL_ImpactTrace (server_util_library.cpp; no
//   default argument here, so pass all three), UTIL_IsCommandIssuedByServerAdmin
//   (shell_commandclient.cpp), UTIL_ShouldShowBlood, UTIL_RemoveImmediate,
//   CreateEntityByName (its default the one baseentity.h gave it), the view
//   punch (shell_scripted.cpp; the shake and the fade are C#'s),
//   and DebugDrawLine (shell_debugoverlays.cpp). SourceSharp_EngineVisualizeTraces
//   left with the server's trace filter (plan_server_native_v2 T4f).
//
//   The fork's INLINE helpers would be a second body under a Valve name, so
//   they are ss::. Each body is the inline it replaces: FStrEq over C strings, INDEXENT, ENTINDEX for
//   an edict, and UTIL_TraceLine over a caller's filter. The overloads over an
//   ss::CTraceFilterSimple (UTIL_TraceLine's and UTIL_TraceHull's ignore-and-group
//   forms, UTIL_TraceRay) and UTIL_PointContents went with the server's native
//   trace filter (plan_server_native_v2 T4f): the game's rule is managed's.
//=============================================================================//

#ifndef SS_UTIL_H
#define SS_UTIL_H
#ifdef _WIN32
#pragma once
#endif

#include "basetypes.h"            // byte and color32, which shake.h assumes
#include "mathlib/vector.h"
#include "edict.h"
#include "eiface.h"
#include "gametrace.h"
#include "engine/IEngineTrace.h"
#include "shake.h"
#include "tier1/convar.h"
#include "tier1/strtools.h"

#include "igameentity_fwd.h"
class IServerNetworkable;

extern IVEngineServer *engine;
extern IEngineTrace   *enginetrace;

//-----------------------------------------------------------------------------
// Defined in our TUs.
//-----------------------------------------------------------------------------
void UTIL_ClearTrace( trace_t &trace );
bool UTIL_IsCommandIssuedByServerAdmin( void );
bool UTIL_ShouldShowBlood( int bloodColor );				// util_shared_library.cpp
void UTIL_ImpactTrace( trace_t *pTrace, int iDamageType, const char *pCustomImpactName );	// server_util_library.cpp

void UTIL_ViewPunch( const Vector &center, QAngle angPunch, float radius, bool bInAir );	// shell_scripted.cpp

void DebugDrawLine( const Vector &vecAbsStart, const Vector &vecAbsEnd,
                    int r, int g, int b, bool test, float duration );
extern float k_flMaxEntityEulerAngle;                      // entity_shared_statics.cpp

namespace ss
{

//-----------------------------------------------------------------------------
// Case-insensitive string equality, true for the same pointer (NULL included)
// without comparing.
//-----------------------------------------------------------------------------
inline bool FStrEq( const char *sz1, const char *sz2 )
{
	return ( sz1 == sz2 || V_stricmp( sz1, sz2 ) == 0 );
}

//-----------------------------------------------------------------------------
// Edict <-> index. The index is the edict's own; the engine is asked only to
// check it, in debug.
//-----------------------------------------------------------------------------
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

// baseentity_shared.h's sanity bound on an entity's Euler angles.
inline bool IsEntityQAngleReasonable( const QAngle &q )
{
	float r = k_flMaxEntityEulerAngle;
	return
		q.x > -r && q.x < r &&
		q.y > -r && q.y < r &&
		q.z > -r && q.z < r;
}

//-----------------------------------------------------------------------------
// A line through the world over the caller's own filter. The game's traces
// are managed's since plan_server_native_v2 T4f (ServerTracer, over the
// managed trace rule); this is what a native diagnostic traces with.
//-----------------------------------------------------------------------------
inline void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
                            ITraceFilter *pFilter, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd );

	enginetrace->TraceRay( ray, mask, pFilter, ptr );
}

} // namespace ss

#endif // SS_UTIL_H
