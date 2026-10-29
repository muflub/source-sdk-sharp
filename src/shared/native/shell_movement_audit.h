//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// What happened to one of the eight movement ConVars after this mod set it. The
// mod writes them as defaults during managed startup; this tells a config override
// from a declared default re-applied over the mod's value. Engine-free so it is testable.

#pragma once

enum SourceSharpMovementConVarVerdict
{
	// Still what SourceSharp_RegisterMovementParams asked for.
	kMovementConVarAsRegistered = 0,

	// Back at the declaration's own default after the mod's write: the only verdict
	// that is a bug.
	kMovementConVarRevertedToDeclared,

	// A third value from a config, map or console; the write is only a default.
	kMovementConVarOverridden,
};

// flRegistered is what the mod wrote, flDeclaredDefault the ConVar declaration's
// default, flNow the current value. "As registered" wins when the two match.
SourceSharpMovementConVarVerdict SourceSharp_ClassifyMovementConVar( float flRegistered, float flDeclaredDefault,
																	 float flNow );

// Which of the eight a name is, or -1. By name because a global change callback is
// handed the parent ConVar, which may not be this DLL's.
int SourceSharp_MovementConVarIndex( const char *pszName );

// The eight names, in the order SourceSharp_RegisterMovementParams writes them.
int SourceSharp_MovementConVarCount();
const char *SourceSharp_MovementConVarName( int nIndex );
