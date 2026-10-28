//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON f  a bit-level wire codec

#include "dt_common.h"
#include "dt_utlvector_common.h"
#include "ss_containers.h"

#include <set>

// Debug-only record of the names handed out, case-insensitive. A std::set so this
// file instantiates no CUtlMemory and imports no g_pMemAlloc.
#if defined( CLIENT_DLL ) && defined( _DEBUG )
typedef std::set< std::string, ss::CaselessLess > DataTableNames_t;
static DataTableNames_t *g_STDict = 0;
static DataTableNames_t *g_RTDict = 0;
#endif

#ifdef CLIENT_DLL
char* AllocateStringHelper2( const char *pFormat, va_list marker )
{
	char str[512];
	_vsnprintf( str, sizeof( str ), pFormat, marker );
	str[ ARRAYSIZE(str) - 1 ] = 0;

	int len = (int)strlen( str ) + 1;
	char *pRet = new char[len];
	memcpy( pRet, str, len );

	return pRet;
}
#endif

#ifdef CLIENT_DLL
char* AllocateStringHelper( const char *pFormat, ... )
{
	va_list marker;
	va_start( marker, pFormat );
	char *pRet = AllocateStringHelper2( pFormat, marker );
	va_end( marker );

	return pRet;
}
#endif

#ifdef CLIENT_DLL
char* AllocateUniqueDataTableName( bool bSendTable, const char *pFormat, ... )
{
	// Setup the string.
	va_list marker;
	va_start( marker, pFormat );
	char *pRet = AllocateStringHelper2( pFormat, marker );
	va_end( marker );

	// Make sure it's unique.
#ifdef _DEBUG
	// Have to allocate them here because if they're declared as straight global variables,
	// their constructors won't have been called yet by the time we get in here.
	if ( !g_STDict )
	{
		g_STDict = new DataTableNames_t;
		g_RTDict = new DataTableNames_t;
	}

	DataTableNames_t *pDict = bSendTable ? g_STDict : g_RTDict;
	if ( !pDict->insert( pRet ).second )
	{
		// Two utlvectors in different data tables have the same name and size limit.
		Assert( false );
	}
#endif

	return pRet;
}
#endif
