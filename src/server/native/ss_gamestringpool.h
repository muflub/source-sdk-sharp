//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

#pragma once

#include "string_t.h"
#include "server_string_pool.h"

// Stock's AllocPooledString over the pool of the object that holds the string_t: a pooled
// string lives as long as that object, the world's for its model name, a player's for its
// name. There is no level-wide pool.
inline string_t PooledString(CServerStringPool& pool, const char* pszValue)
{
	if (pszValue && *pszValue)
		return MAKE_STRING(pool.Allocate(pszValue));
	return NULL_STRING;
}
