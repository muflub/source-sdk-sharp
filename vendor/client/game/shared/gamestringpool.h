//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Pool of all per-level strings. Allocates memory for strings, 
//			consolodating duplicates. The memory is freed on behalf of clients
//			at level transition. Strings are of type string_t.

#pragma once

// String allocation
string_t AllocPooledString( const char *pszValue );
string_t FindPooledString( const char *pszValue );

#define AssertIsValidString( s )	AssertMsg( s == NULL_STRING || s == FindPooledString( STRING(s) ), "Invalid string " #s );
		 
// String system accessor
class IGameSystem;
