//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// Classifies what happened to one of the eight movement ConVars after this mod set it.

#include "shell_movement_audit.h"

#include <math.h>
#include <string.h>

namespace
{

// The eight that reach a ConVar, in the order shell_movement.cpp writes them. The
// other movement parameters have no engine name.
const char *const kMovementConVars[] = {
	"sv_gravity",   "sv_friction",    "sv_accelerate", "sv_airaccelerate",
	"sv_stopspeed", "sv_maxvelocity", "sv_stepsize",   "sv_maxspeed",
};

const int kMovementConVarCount = (int)( sizeof( kMovementConVars ) / sizeof( kMovementConVars[0] ) );

// A tolerance because each value round-trips through a "%f" string in the engine.
bool SameReading( float a, float b )
{
	return fabsf( a - b ) <= 1e-3f;
}

} // namespace

SourceSharpMovementConVarVerdict SourceSharp_ClassifyMovementConVar( float flRegistered, float flDeclaredDefault,
																	 float flNow )
{
	// First: when the mod's tuning equals the declared default, nothing has moved.
	if ( SameReading( flNow, flRegistered ) )
		return kMovementConVarAsRegistered;

	if ( SameReading( flNow, flDeclaredDefault ) )
		return kMovementConVarRevertedToDeclared;

	return kMovementConVarOverridden;
}

int SourceSharp_MovementConVarIndex( const char *pszName )
{
	if ( pszName == NULL || pszName[0] == '\0' )
		return -1;

	for ( int i = 0; i < kMovementConVarCount; i++ )
	{
		// Case-sensitive: these are the exact names the writer passes.
		if ( strcmp( kMovementConVars[i], pszName ) == 0 )
			return i;
	}

	return -1;
}

int SourceSharp_MovementConVarCount()
{
	return kMovementConVarCount;
}

const char *SourceSharp_MovementConVarName( int nIndex )
{
	if ( nIndex < 0 || nIndex >= kMovementConVarCount )
		return NULL;

	return kMovementConVars[nIndex];
}
