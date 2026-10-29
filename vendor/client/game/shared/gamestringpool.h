//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Pool of all per-level strings. Allocates memory for strings,
//			consolodating duplicates. The memory is freed on behalf of clients
//			at level transition. Strings are of type string_t.

#pragma once

// String allocation
string_t AllocPooledString(const char* pszValue);

// String system accessor
class IGameSystem;
