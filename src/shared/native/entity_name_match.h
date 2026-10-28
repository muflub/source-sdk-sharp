//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The one name-matching rule for targetnames and classnames: case-insensitive ASCII,
// with '*' as a wildcard where the walk stops. NULL and "" both mean unnamed, so passing
// STRING(name) gives the same answer as stock's string_t NamesMatch.
#pragma once

// pszQuery is a name, a classname, or a prefix ending in '*'.
inline bool EntityNames_Match( const char *pszQuery, const char *pszName )
{
	// Stock's NULL_STRING arm. "" must take it too, as STRING(NULL_STRING) is "".
	if ( !pszName || *pszName == 0 )
		return ( !pszQuery || *pszQuery == 0 || *pszQuery == '*' );

	if ( !pszQuery )
		return false;

	// Names come from a pool, so this hits for most exact matches.
	if ( pszName == pszQuery )
		return true;

	while ( *pszName && *pszQuery )
	{
		const unsigned char cName  = (unsigned char)*pszName;
		const unsigned char cQuery = (unsigned char)*pszQuery;

	// Simple ASCII case conversion, both directions, as stock.
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

	// As stock, the '*' test is wherever the walk stopped, so a '*' mid-query matches too.
	// Reproduced deliberately: correcting it would change what existing maps do.
	return ( *pszQuery == '*' );
}
