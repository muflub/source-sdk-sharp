//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — one name-matching rule, for both hierarchies =======//
//
// Does this targetname or classname match what a map, an output, or a console
// command asked for? Trailing '*' is a prefix wildcard, comparison is
// case-insensitive ASCII, and that is the whole language.
//
// WHY THIS FILE EXISTS. The rule was written twice. Stock's is NamesMatch in
// baseentity.cpp, FORCEINLINE and file-static, reachable only from
// CBaseEntity::NameMatchesComplex and ClassMatchesComplex. When the standalone
// hierarchy needed the same answer, entitylist.cpp could not call it and grew a
// second copy, GameEntityNameMatches, carrying this comment:
//
//     Deliberately the same walk as NamesMatch in baseentity.cpp, which is
//     FORCEINLINE there and so cannot be called from here. Kept identical on
//     purpose: two name-matching rules that differ in a corner is a map that
//     works for one kind of entity and not the other.
//
// That is a standing admission that the duplication is a hazard, with nothing
// in the build to notice the day the two drift. This is the one copy both call,
// so there is no longer a corner for them to differ in.
//
// AND IT IS ALSO THE RETYPE. plan_baseentity.md Phase 13 precondition (e):
// ClassMatchesComplex is an out-of-line CBaseEntity member, so every
// `pEnt->ClassMatches( sz )` in a walk is a reference into baseentity.o. The
// classname is answered by IServerNetworkable::GetClassName, which is
// STRING(m_iClassname) -- the same field the member read -- so a caller holding
// the interface can ask this instead and needs no entity at all.
//
// THE STRING_T / CONST CHAR* EQUIVALENCE, since the retype depends on it.
// Stock's NamesMatch takes a string_t and special-cases NULL_STRING:
//
//     if ( nameToMatch == NULL_STRING )
//         return ( !pszQuery || *pszQuery == 0 || *pszQuery == '*' );
//
// STRING(NULL_STRING) is "" (string_t.h), and this function's answer for "" is
// the same three cases -- the walk does not run, so it falls through to
// "both ended" (query empty) and then to the trailing-'*' test. So passing
// STRING(name) is not an approximation of passing the string_t; it is the same
// answer for every input. NULL is accepted as a spelling of "no name at all"
// for callers that have one.
//
// Engine-free: two char pointers in, a bool out, so it can be tested without a
// running Source. The tests are entity_name_match_test.cpp.
//
//=============================================================================//
#ifndef ENTITY_NAME_MATCH_H
#define ENTITY_NAME_MATCH_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// pszQuery is what was asked for -- a name, a classname, or a prefix ending in
// '*'. pszName is what the entity is called; NULL and "" both mean "unnamed".
//-----------------------------------------------------------------------------
inline bool EntityNames_Match( const char *pszQuery, const char *pszName )
{
	// An unnamed entity. Stock's NULL_STRING arm, spelled once: a query that
	// asks for nothing, or for anything, matches it; any other query does not.
	//
	// THE EMPTY STRING TAKES THIS ARM TOO, and that is the whole identity the
	// FClassnameIs retype rests on. STRING(NULL_STRING) is "", so a caller that
	// hands over GetClassName() gives "" exactly where a caller holding the
	// string_t gave NULL_STRING. Keying this arm on NULL alone -- which is how
	// this function was first written here -- sends "" down the walk instead,
	// and the walk answers a NULL query FALSE where stock answered TRUE. The
	// tests in entity_name_match_test.cpp caught it; nothing else would have.
	if ( !pszName || *pszName == 0 )
		return ( !pszQuery || *pszQuery == 0 || *pszQuery == '*' );

	if ( !pszQuery )
		return false;

	// Identical pointers are identical strings. Stock's fast path, and worth
	// keeping: names come from a pool, so this hits for most exact matches.
	if ( pszName == pszQuery )
		return true;

	while ( *pszName && *pszQuery )
	{
		const unsigned char cName  = (unsigned char)*pszName;
		const unsigned char cQuery = (unsigned char)*pszQuery;

		// Simple ASCII case conversion, both directions, exactly as stock.
		if ( cName == cQuery )
			;
		else if ( cName - 'A' <= (unsigned char)'Z' - 'A' && cName - 'A' + 'a' == cQuery )
			;
		else if ( cName - 'a' <= (unsigned char)'z' - 'a' && cName - 'a' + 'A' == cQuery )
			;
		else
			break;

		++pszName;
		++pszQuery;
	}

	if ( *pszQuery == 0 && *pszName == 0 )
		return true;

	// A '*' AT THE POINT THE WALK STOPPED. Stock's comment here says "only thing
	// supported is trailing *", and the comment understates what the code does:
	// the test is on wherever the query happens to have reached, so "ga*e_north"
	// matches "gate_north" and "*_north" matches everything. That is a wildcard
	// in the middle, working, contrary to the comment beside it.
	//
	// Reproduced rather than corrected, deliberately. This function's job is to
	// answer what CBaseEntity::ClassMatches answered, so that FClassnameIs can
	// stop asking the class; "fixing" it here would change what an existing map
	// does. The tests pin the real behaviour so the next reader is not misled by
	// stock's comment the way the first draft of those tests was.
	return ( *pszQuery == '*' );
}

#endif // ENTITY_NAME_MATCH_H
