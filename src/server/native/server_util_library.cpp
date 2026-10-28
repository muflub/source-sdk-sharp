//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
//
// Server utility functions from game/server/util.cpp that reach only the engine and the
// C runtime, never an entity. Declarations stay in util.h. Server only.

#include "ss_util.h"
#include "ss_shareddefs.h"

#include "ss_recipient_filter.h"
#include "shell_tempents.h"
#include "ss_world.h"

#include "managed_convar_ref.h"
#include "ss_string.h"
#include "trace_hit.h"

extern CGlobalVars *gpGlobals;

// So we always return a valid surface. External linkage: other TUs read it.
csurface_t g_NullSurface = { "**empty**", 0 };

void UTIL_ClearTrace( trace_t &trace )
{
	memset( &trace, 0, sizeof( trace ) );
	trace.fraction = 1.f;
	trace.fractionleftsolid = 0;
	trace.surface = g_NullSurface;
}

static CManagedConVarRef sv_strict_notarget( "sv_strict_notarget" );
