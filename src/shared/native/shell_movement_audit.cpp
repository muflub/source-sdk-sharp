//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// Purpose: What happened to one of the eight movement ConVars after this mod
//          set it. The decision only -- see the header for why it is alone.

#include "shell_movement_audit.h"

#include <math.h>
#include <string.h>

namespace
{

//-----------------------------------------------------------------------------
// The eight, in the order shell_movement.cpp writes them.
//
// WRITTEN OUT RATHER THAN DERIVED. SourceSharpMovementParams carries twelve
// numbers and only eight of them reach a ConVar -- jump height, air speed cap,
// duck scale and the friction scale are read by managed code directly and have
// no engine name at all. A list generated from the struct would audit four
// names that do not exist and report them missing on every run.
//-----------------------------------------------------------------------------
const char *const kMovementConVars[] =
{
	"sv_gravity",
	"sv_friction",
	"sv_accelerate",
	"sv_airaccelerate",
	"sv_stopspeed",
	"sv_maxvelocity",
	"sv_stepsize",
	"sv_maxspeed",
};

const int kMovementConVarCount =
	(int)( sizeof( kMovementConVars ) / sizeof( kMovementConVars[0] ) );

//-----------------------------------------------------------------------------
// Whether two ConVar readings are the same number.
//
// A TOLERANCE RATHER THAN ==, and the tolerance is not superstition: every one
// of these values makes a round trip through a DECIMAL STRING on its way into
// the engine. `ConVar::SetValue(float)` formats with "%f" and the reader parses
// that back, so 0.33333334f writes "0.333333" and returns as 0.333333f. An
// exact comparison calls that a changed value and reports the defect on every
// run of a healthy build.
//
// 1e-3 rather than an epsilon: "%f" keeps six decimals, and the smallest of
// these eight numbers is 1 (sv_waterfriction is not one of them). Nothing this
// coarse can hide a real revert -- the ones that matter are 600 against 800 and
// 75 against 100.
//-----------------------------------------------------------------------------
bool SameReading( float a, float b )
{
	return fabsf( a - b ) <= 1e-3f;
}

} // namespace

SourceSharpMovementConVarVerdict SourceSharp_ClassifyMovementConVar(
	float flRegistered, float flDeclaredDefault, float flNow )
{
	// FIRST, and see the header: when the mod's tuning IS the declaration,
	// these two tests both match and "nothing has moved" is the true answer.
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
		// strcmp and not a case-insensitive compare: these are the names this
		// file's own sibling passes to SetEngineConVar, spelled once each, and
		// a match that tolerated "SV_GRAVITY" would be claiming to audit a name
		// nothing in the tree writes.
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

	return kMovementConVars[ nIndex ];
}
