// Classname matching for the standalone shell, engine-free so it can be unit tested.
#pragma once

// Whether a classname matches a query, with CGlobalEntityList::FindEntityByClassname's
// rule (NamesMatch): only a trailing star is a wildcard, so "*" is everything and
// "prop_*" a prefix. Case-insensitive ASCII, without tier1.
inline bool StandaloneNameMatches( const char *pQuery, const char *pName )
{
	if ( !pQuery || !pName )
		return false;

	// An unnamed thing matches only the empty query or the bare wildcard, as NamesMatch
	// answers for NULL_STRING.
	if ( !pName[0] )
		return !pQuery[0] || *pQuery == '*';

	while ( *pName && *pQuery )
	{
		char a = *pName;
		char b = *pQuery;

		if ( a >= 'A' && a <= 'Z' )
			a = (char)( a - 'A' + 'a' );
		if ( b >= 'A' && b <= 'Z' )
			b = (char)( b - 'A' + 'a' );

		if ( a != b )
			break;

		pName++;
		pQuery++;
	}

	if ( !*pQuery && !*pName )
		return true;

	return *pQuery == '*';
}
