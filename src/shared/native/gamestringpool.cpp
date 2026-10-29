//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// A string_t is an address into this pool, which makes two spellings of one name
// compare equal by pointer.

// The client's; the server's is CServerGameStrings (ss_gamestringpool.h), the DLL tier's.
#include "string_t.h"
#include "client_class.h"
#include "icliententityinternal.h"
#include "shell_clientsystem.h"

#include "server_string_pool.h"

// The storage for pooled per-level strings.
class CGameStringPool : public CServerStringPool, public CClientSystem
{
	virtual char const* Name() { return "CGameStringPool"; }

	virtual void LevelShutdownPostEntity() { Cleanup(); }

public:
	~CGameStringPool() { Cleanup(); }

	void Cleanup() { FreeAll(); }
};

static CGameStringPool g_GameStringPool;

CClientSystem* GameStringSystem()
{
	return &g_GameStringPool;
}

// Allocates a level-global pooled string.
string_t AllocPooledString(const char* pszValue)
{
	if (pszValue && *pszValue)
		return MAKE_STRING(g_GameStringPool.Allocate(pszValue));
	return NULL_STRING;
}
