//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Purpose: What happened to one of the eight movement ConVars after this mod
//          set it.

#ifndef SHELL_MOVEMENT_AUDIT_H
#define SHELL_MOVEMENT_AUDIT_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// SOURCESHARP: the instrument plan_shim.md 4a item 16 asks for.
//
// `SourceSharp_RegisterMovementParams` writes eight engine ConVars once, during
// managed startup, DELIBERATELY as defaults -- an operator's config is read
// afterwards and keeps what it sets. That makes every one of those eight a
// value with no declaration behind it: sv_gravity's declaration says 800 and
// this mod's tuning is 600, so a run in which the write is undone reads the
// engine's number and says nothing at all about it.
//
// docs/defect-convar-order.md is the diagnosis. The part that needs code is the
// last line of it: the window between the write and the first frame had no
// observer, because the only change callback in the process --
// SourceSharp_OnConVarChanged -- returns early until the managed side is
// loaded, which is the very window in question.
//
// THE DECISION IS HERE AND THE ENGINE IS NOT, deliberately, and for the same
// reason shell_acquire_reasons.cpp is its own translation unit: a rule stated
// in a file that includes cbase.h can only be checked by running the game, and
// this one is arithmetic. shell_movement.cpp does the engine half -- which name
// changed, what it reads now, what it was declared as -- and asks this.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// What a change to one of the eight means.
//
// THREE OUTCOMES AND NOT TWO. "It is not what we set" is not one answer: an
// operator typing `sv_gravity 700` and the defect putting 800 back look
// identical to a check that only compares against what managed registered, and
// the first of those is the feature working as intended. The value being
// EXACTLY the declaration's own default again is the signature that separates
// them, and it is the shape every reading of this defect has had.
//-----------------------------------------------------------------------------
enum SourceSharpMovementConVarVerdict
{
	// Still what SourceSharp_RegisterMovementParams asked for. Nothing to say.
	kMovementConVarAsRegistered = 0,

	// Back at the value the .cpp declares, which this mod deliberately does not
	// use. Somebody re-applied a declared default AFTER the mod's write --
	// plan_shim.md 4a item 16's defect, and the only verdict that is a bug.
	kMovementConVarRevertedToDeclared,

	// Some third number: a server config, a map, a console. The write is a
	// default and losing to those is what it is for.
	kMovementConVarOverridden,
};

//-----------------------------------------------------------------------------
// Which of the three, given the three numbers.
//
// flRegistered      what SourceSharp_RegisterMovementParams wrote.
// flDeclaredDefault what the ConVar declaration's default string parses to.
// flNow             what the ConVar reads after the change.
//
// THE ORDER OF THE TWO TESTS MATTERS and is not arbitrary. When a mod's tuning
// happens to equal the declaration -- six of the eight are in exactly that
// position today, which is why only sv_gravity and sv_stopspeed were ever
// visible -- "as registered" and "reverted" are the same number, and the honest
// answer is the first: nothing has gone wrong, because nothing has moved.
//-----------------------------------------------------------------------------
SourceSharpMovementConVarVerdict SourceSharp_ClassifyMovementConVar(
	float flRegistered, float flDeclaredDefault, float flNow );

//-----------------------------------------------------------------------------
// Which of the eight a name is, or -1.
//
// BY NAME rather than by ConVar pointer, for the reason the change callback
// beside it already gives: the object a global change callback is handed is the
// PARENT, which is not this DLL's object whenever anyone else registered the
// name first. The name is the only identity that survives that.
//-----------------------------------------------------------------------------
int SourceSharp_MovementConVarIndex( const char *pszName );

//-----------------------------------------------------------------------------
// The eight names, in the order SourceSharp_RegisterMovementParams writes them.
//
// Exposed so the test can assert the list rather than restate it: a ninth
// parameter added to the write with no row here would otherwise be unaudited
// and nothing would say so.
//-----------------------------------------------------------------------------
int SourceSharp_MovementConVarCount();
const char *SourceSharp_MovementConVarName( int nIndex );

#endif // SHELL_MOVEMENT_AUDIT_H
