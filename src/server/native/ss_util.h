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
//   DebugDrawLine (shell_debugoverlays.cpp) and SourceSharp_EngineVisualizeTraces
//   (trace_filter_library.cpp, what the r_visualizetraces macro expands to).
//
//   The fork's INLINE helpers would be a second body under a Valve name, so
//   they are ss::. Each body is the inline it replaces: FStrEq over C strings, INDEXENT, ENTINDEX for
//   an edict, UTIL_PointContents, and both UTIL_TraceLine overloads -- over a caller's filter, and
//   over an ss::CTraceFilterSimple (ss_tracefilter.h) built from an ignore
//   entity and a collision group -- UTIL_TraceHull's ignore-and-group
//   overload (lane SP) and UTIL_TraceRay.
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
#include "ss_tracefilter.h"

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
ConVar *SourceSharp_EngineVisualizeTraces();
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

//-----------------------------------------------------------------------------
// The contents at a point, as the engine's trace answers them.
//-----------------------------------------------------------------------------
// baseentity_shared.h's sanity bound on an entity's Euler angles.
inline bool IsEntityQAngleReasonable( const QAngle &q )
{
	float r = k_flMaxEntityEulerAngle;
	return
		q.x > -r && q.x < r &&
		q.y > -r && q.y < r &&
		q.z > -r && q.z < r;
}

inline int UTIL_PointContents( const Vector &vec )
{
	return enginetrace->GetPointContents( vec );
}

//-----------------------------------------------------------------------------
// Line traces, drawn when r_visualizetraces is on. On the server that name is a
// macro (the target's -D) for SourceSharp_EngineVisualizeTraces(), the
// engine's convar.
//-----------------------------------------------------------------------------
inline void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
                            const IHandleEntity *ignore, int collisionGroup, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd );
	CTraceFilterSimple traceFilter( ignore, collisionGroup );

	enginetrace->TraceRay( ray, mask, &traceFilter, ptr );

	if ( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 0, 0, true, -1.0f );
	}
}

inline void UTIL_TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
                            ITraceFilter *pFilter, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd );

	enginetrace->TraceRay( ray, mask, pFilter, ptr );

	if ( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 0, 0, true, -1.0f );
	}
}

//-----------------------------------------------------------------------------
// A hull trace over an ss::CTraceFilterSimple built from an ignore entity and a
// collision group (P5 lane SP, for shell_worldquery.cpp's SourceSharp_TraceHull):
// util_shared.h's inline overload, drawn yellow when r_visualizetraces is on.
//-----------------------------------------------------------------------------
inline void UTIL_TraceHull( const Vector &vecAbsStart, const Vector &vecAbsEnd, const Vector &hullMin,
                            const Vector &hullMax, unsigned int mask, const IHandleEntity *ignore,
                            int collisionGroup, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd, hullMin, hullMax );
	CTraceFilterSimple traceFilter( ignore, collisionGroup );

	enginetrace->TraceRay( ray, mask, &traceFilter, ptr );

	if ( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 255, 0, true, -1.0f );
	}
}

//-----------------------------------------------------------------------------
// A ray trace over an ss::CTraceFilterSimple (P5, for physics_shared.cpp's
// vehicle traces): util_shared.h's inline, drawn red when r_visualizetraces is on.
//-----------------------------------------------------------------------------
inline void UTIL_TraceRay( const Ray_t &ray, unsigned int mask, const IHandleEntity *ignore,
                           int collisionGroup, trace_t *ptr, ShouldHitFunc_t pExtraShouldHitCheckFn = NULL )
{
	CTraceFilterSimple traceFilter( ignore, collisionGroup, pExtraShouldHitCheckFn );

	enginetrace->TraceRay( ray, mask, &traceFilter, ptr );

	if ( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 0, 0, true, -1.0f );
	}
}

} // namespace ss

#endif // SS_UTIL_H
