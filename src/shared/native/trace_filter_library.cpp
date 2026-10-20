//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's trace filter family ===================//
// SS_REASON e  a synchronous mid-step answer
//
// plan_entity.md Phase 4, step 4e (Utilities) moved the shared trace-filter
// family here out of stock util_shared.cpp; every declaration stays in
// util_shared.h, so no caller changed.
//
// THE CLIENT'S ONLY SINCE plan_server_native_v2 T4f. The server's half --
// ss::CTraceFilterSimple, the shape clause for the parallel hierarchy
// (StandaloneFilterRules), ownership compared as handles, the entity's and the
// game rules' collision-group asks, and the engine's r_visualizetraces reader --
// is managed's: SourceSharp.Server.Interop's TraceFilterRules answers the
// ITraceFilter libss_fwd_server builds (fwd_trace_filter.cpp) for every server
// trace (Decision 2). server.so no longer compiles this file; what is left is
// exactly what the client compiled before, its preprocessed text unchanged.
//
// r_visualizetraces IS DECLARED HERE: the ConVar util_shared.h's UTIL_Trace*
// inlines read, beside the filter they construct.
//=============================================================================//

#include "cbase.h"
#include "util_shared.h"
#include "physics_shared.h"
#include "gamerules.h"
#include "model_types.h"
// The shape clause the server's hierarchy was read with; the client includes it
// as it always did. See util_shared.h.
#include "standard_filter_shape.h"

// The trace debug-draw switch the UTIL_Trace* inlines in util_shared.h read.
ConVar r_visualizetraces( "r_visualizetraces", "0", FCVAR_CHEAT );

//-----------------------------------------------------------------------------
// The pass entity. THE CLIENT HAS NO AnyEntity_t (it is server only), so past
// the pass entity itself this answers true, as the stock route would for a
// hierarchy whose entities resolve to no C_BaseEntity. The ownership rule is
// the server's, in managed TraceFilterRules.
//-----------------------------------------------------------------------------
bool PassServerEntityFilter( const IHandleEntity *pTouch, const IHandleEntity *pPass ) 
{
	if ( !pPass )
		return true;
	if ( pTouch == pPass )
		return false;
	return true;
}

//-----------------------------------------------------------------------------
// The shape clause. Its SDK arm was unreachable (its resolver answered a
// constant NULL) and its parallel-hierarchy arm was the server's.
//
// THE CLIENT GAP IS UNCHANGED AND STILL OPEN: on the client every entity reads
// as the static-prop case, as it always has. Closing it needs a client shape
// clause, which is a behaviour change and not a move's.
//-----------------------------------------------------------------------------
bool StandardFilterRules( IHandleEntity *pHandleEntity, int fContentsMask )
{
	return true;
}

//-----------------------------------------------------------------------------
// Simple trace filter: util_shared.h's CTraceFilterSimple. The game-entity ask
// was the server's arm, so on the client nothing past the pass entity is hit
// (the answer it always had: EntityFromEntityHandle answered NULL).
//-----------------------------------------------------------------------------
#define SS_SIMPLE_FILTER CTraceFilterSimple
SS_SIMPLE_FILTER::CTraceFilterSimple( const IHandleEntity *passedict, int collisionGroup,
									   ShouldHitFunc_t pExtraShouldHitFunc )
{
	m_pPassEnt = passedict;
	m_collisionGroup = collisionGroup;
	m_pExtraShouldHitCheckFunction = pExtraShouldHitFunc;
}

bool SS_SIMPLE_FILTER::ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask )
{
	if ( !StandardFilterRules( pHandleEntity, contentsMask ) )
		return false;
	if ( m_pPassEnt )
	{
		if ( !PassServerEntityFilter( pHandleEntity, m_pPassEnt ) )
		{
			return false;
		}
	}
	return false;
}
