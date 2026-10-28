//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// The server's decal name queries (ss_decals.h), answered by the managed DecalEmitter.
// Native only for its native caller, CWorld::DecalTrace.

#include "ss_decals.h"
#include "sourcesharp.h"
#include "imanaged_areas.h"
#include "shell_server.h"

// Both string answers come back in buffers this object owns; every caller uses the
// answer before asking again. 260 is the engine's MAX_PATH.
class CShellDecalEmitter
{
public:
	CShellDecalEmitter()
	{
		m_szName[0] = '\0';
		m_szTranslated[0] = '\0';
	}

	int GetDecalIndexForName( char const *decalname )
	{
		// Stock's first line, answered without a crossing.
		if ( decalname == NULL || decalname[0] == '\0' )
			return -1;

		IManagedResources *pArea = ManagedResources();

		if ( pArea == NULL )
			return -1;

		return pArea->ServerDecalIndexForName( decalname );
	}

	const char *GetDecalNameForIndex( int nIndex )
	{
		// Stock answers "" for an unknown index; the ABI's -1 is translated here.
		m_szName[0] = '\0';

		IManagedResources *pArea = ManagedResources();

		if ( pArea == NULL )
			return m_szName;

		if ( pArea->ServerDecalNameForIndex( nIndex, m_szName, sizeof( m_szName ) ) < 0 )
			m_szName[0] = '\0';

		return m_szName;
	}

	char const *TranslateDecalForGameMaterial( char const *decalName, unsigned char gamematerial )
	{
		IManagedResources *pArea = ManagedResources();

		if ( pArea == NULL || decalName == NULL )
			return decalName;

		// Widened through unsigned char so the key byte is the same on both sides; char's
		// signedness is implementation-defined.
		int32_t nWritten = pArea->ServerDecalTranslateForGameMaterial( decalName, (int32_t)gamematerial, m_szTranslated,
																	   sizeof( m_szTranslated ) );

		// Stock's contract: an untranslated decal comes back unchanged.
		if ( nWritten < 0 )
			return decalName;

		return m_szTranslated;
	}

private:
	char m_szName[260];
	char m_szTranslated[260];
};

// Built on first ask. Every method answers stock's miss value without the managed side.
static CShellDecalEmitter *ShellDecalEmitter()
{
	static CShellDecalEmitter s_Emitter;
	return &s_Emitter;
}

// ss_decals.h's entry points.
int ss::DecalIndexForName( const char *pszDecalName )
{
	return ShellDecalEmitter()->GetDecalIndexForName( pszDecalName );
}
