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

#include "ss_effects.h"
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

// g_sModelIndexSmoke is combatweapon.cpp's.
extern short g_sModelIndexSmoke;

void UTIL_Smoke( const Vector &origin, const float scale, const float framerate )
{
	ss::Effects()->Smoke( origin, g_sModelIndexSmoke, scale, framerate );
}

// g_Language is declared in C# and read by name.
static CManagedConVarRef s_Language( "g_Language" );

void UTIL_BloodStream( const Vector &origin, const Vector &direction, int color, int amount )
{
	if ( !UTIL_ShouldShowBlood( color ) )
		return;

	if ( s_Language.GetInt() == ss::kLanguageGerman && color == ss::BLOOD_COLOR_RED )
		color = 0;

	ss::CPVSFilter filter( origin );

	TE_BloodStream( filter, 0.0, &origin, &direction, 247, 63, 14, 255, MIN( amount, 255 ) );
}
