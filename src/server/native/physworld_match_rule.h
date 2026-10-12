//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — which objects `ss_physworld_object` reports on ======//
//
// ONE LINE OF POLICY, EXTRACTED BECAUSE THE OBVIOUS SPELLING OF IT IS A TRAP.
//
// `ss_physworld_object <substring>` is how every physics probe in tools/ reads
// the environment, and a probe that wants ALL of it has to ask for that
// somehow. The spelling that looks right is an empty substring — "everything
// contains the empty string" — and it matches NOTHING.
//
// V_stristr (tier1/strtools.cpp:735) walks the haystack comparing each letter
// against the needle's FIRST character, and for an empty needle that character
// is the terminator. No letter equals it, so the loop runs off the end and the
// function returns NULL for every name in the world.
//
// That is not a hypothetical. tools/3a-penetration-granularity asked for ""
// on its first live run, got `matched=0 of 427` on both maps, fell through to
// its own "prop" fallback, and then reported — in a control whose entire job
// was to establish the scope of an absence — that it had read "every object in
// the environment". It had read 415 of 427. The finding survived the mistake,
// but the control did not, and a control that cannot fail is the thing this
// directory's tests exist to catch.
//
// So "*" is the spelling, it is handled before the substring search rather
// than by it, and both halves are pinned by physworld_match_rule_test.
//
//=============================================================================//
#ifndef PHYSWORLD_MATCH_RULE_H
#define PHYSWORLD_MATCH_RULE_H
#ifdef _WIN32
#pragma once
#endif

#include "ss_string.h"

//-----------------------------------------------------------------------------
// "*" AND ONLY "*". Not a prefix, not a glob, not a name that happens to
// contain one: a name like "prop*crate" is a substring request for a literal
// asterisk and stays one, because the alternative is a report that silently
// widens to the whole world when a caller's name has punctuation in it.
//-----------------------------------------------------------------------------
inline bool PhysWorldMatchAllRequested( const char *pszWanted )
{
	return pszWanted != NULL && pszWanted[ 0 ] == '*' && pszWanted[ 1 ] == '\0';
}

//-----------------------------------------------------------------------------
// Does this object's name answer the request?
//
// A NAMELESS OBJECT IS REPORTED BY "*" AND BY NOTHING ELSE. IPhysicsObject
// names are set by whoever made the object, and the world's is not; an
// environment census that skipped it would be a census of the objects the game
// happened to name, which is the same class of quiet subset the empty needle
// produced.
//-----------------------------------------------------------------------------
inline bool PhysWorldObjectMatches( const char *pszName, const char *pszWanted )
{
	if ( PhysWorldMatchAllRequested( pszWanted ) )
		return true;

	if ( pszName == NULL || pszWanted == NULL )
		return false;

	return ss::StrIStr( pszName, pszWanted ) != NULL;
}

#endif // PHYSWORLD_MATCH_RULE_H
