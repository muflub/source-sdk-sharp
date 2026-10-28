//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// A string_t is an address into this pool, which makes two spellings of one name
// compare equal by pointer.

// Compiled into both DLLs; the game/client headers are not on the server include path.
#ifdef CLIENT_DLL
#include "string_t.h"
#include "client_class.h"
#include "icliententityinternal.h"
#include "shell_clientsystem.h"
#else
#include "shell_serversystem.h"
#include "ss_gamestringpool.h"
#include "gamestringpool_internal.h"
#include "tier0/dbg.h"

#endif

#include "server_string_pool.h"

// The storage for pooled per-level strings.
#ifdef CLIENT_DLL
class CGameStringPool : public CServerStringPool, public CClientSystem
#else
class CGameStringPool : public CServerStringPool, public CServerSystem
#endif
{
	virtual char const *Name() { return "CGameStringPool"; }

	virtual void LevelShutdownPostEntity() { Cleanup(); }

public:
	~CGameStringPool() { Cleanup(); }

	void Cleanup() { FreeAll(); }

#ifndef CLIENT_DLL
	// Every pooled string, in order.
	void Visit( GameStringPoolVisitFn pfnVisit, void *pContext ) const
	{
		for ( Set::const_iterator it = m_Strings.begin(); it != m_Strings.end(); ++it )
		{
			pfnVisit( it->c_str(), pContext );
		}
	}
#endif
};

static CGameStringPool g_GameStringPool;

#ifdef CLIENT_DLL
CClientSystem *GameStringSystem()
#else
CServerSystem *GameStringSystem()
#endif
{
	return &g_GameStringPool;
}

// Allocates a level-global pooled string.
string_t AllocPooledString( const char *pszValue )
{
	if ( pszValue && *pszValue )
		return MAKE_STRING( g_GameStringPool.Allocate( pszValue ) );
	return NULL_STRING;
}

#ifndef CLIENT_DLL
// Read-only access to the pool for the debug dump.
void GameStringPool_Visit( GameStringPoolVisitFn pfnVisit, void *pContext )
{
	g_GameStringPool.Visit( pfnVisit, pContext );
}

int GameStringPool_Count()
{
	return g_GameStringPool.Count();
}
#endif
