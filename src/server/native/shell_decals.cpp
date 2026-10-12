//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// THE SERVER'S DECAL EMITTER, WHICH IS A SHIM -- plan_gamedll_after.md Phase A3.
//
// CDecalEmitterSystem was row 11 of kServerSystems[] and `game/shared/decals.cpp`
// was in server_csharp.vpc. Neither is true any more: the whole system --
// the KeyValues load, the name table, the weighted pick and the game-material
// translation -- is `DecalEmitter` in SourceSharp.Server, an IGameLifecycle
// participant. This file is the three-method shim the server's decal names
// are asked through (ss_decals.h).
//
//=============================================================================
// WHY THERE IS NO `decalsystem` ANY MORE (P5 lane SP).
//
// `decalsystem` -- an IDecalEmitterSystem pointer, decals.h's spelling -- was
// kept so the tree's callers would not need editing. Its compiled callers went:
// baseentity_shared.cpp left the build, and world_adapter.cpp's CWorld::
// DecalTrace asks ss::DecalIndexForName. nm then found no server object
// importing `decalsystem`, so the Valve interface left with it, and this file
// compiles on the R5 include set. No engine reads the interface.
//
// NATIVE ONLY FOR ITS NATIVE CALLER (x): CWorld::DecalTrace, a native caller of
// a table that is wholly managed. When that asks managed code itself, this goes.
//
//=============================================================================
// WHY decals.cpp LEFT THE SERVER BUILD RATHER THAN GROWING #ifdefs.
//
// T1: decals.cpp compiles into BOTH server.so and client.so, and this project's
// recorded failure mode for a shared file is that removing something from the
// server's arm compiles green and silently stops initialising the client's.
// Splitting the class body down the middle with CLIENT_DLL is the shape that
// invites that.
//
// Subtracting the file from the SERVER's project instead makes the client's
// copy textually untouched -- and it went further than untouched: the
// `#ifndef CLIENT_DLL` scaffolding Phase 7a had to add there is GONE, so
// decals.cpp is closer to stock after this phase than before it. Proved rather
// than argued, by src/sourcesharp/test/client_shared_parity.sh, which
// preprocesses both revisions with -DCLIENT_DLL and diffs.
//
//=============================================================================
// NO FILE-SCOPE STATE. The shim instance is a FUNCTION-LOCAL static, which is
// the population A1's gate deliberately does not cover and the project rule
// permits (see docs/gamedll-ledger.md, Phase A6 row 12).
//
//=============================================================================

#include "ss_decals.h"
#include "sourcesharp.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E3: IManagedResources
#include "shell_server.h"


//-----------------------------------------------------------------------------
// The two string answers come back into a buffer this object owns, one per
// method, because both interface methods return `const char *` and every caller
// uses the answer before asking again.
//
// A MEMBER RATHER THAN A FILE-SCOPE ARRAY, which is the project's standing rule
// and is also what stock does: CDecalEmitterSystem's own tables were members.
// 260 is the engine's own MAX_PATH for a material path; a decal name is one.
//-----------------------------------------------------------------------------
class CShellDecalEmitter
{
public:
	CShellDecalEmitter()
	{
		m_szName[ 0 ] = '\0';
		m_szTranslated[ 0 ] = '\0';
	}

	int GetDecalIndexForName( char const *decalname )
	{
		// Stock's own first line, kept here rather than crossed: an empty name
		// is -1 on both sides and asking C# would be a crossing for an answer
		// neither side can disagree about.
		if ( decalname == NULL || decalname[ 0 ] == '\0' )
			return -1;

		// E3: the managed-built resources area, acquired again on every load.
		IManagedResources *pArea = ManagedResources();

		if ( pArea == NULL )
			return -1;

		return pArea->ServerDecalIndexForName( decalname );
	}

	const char *GetDecalNameForIndex( int nIndex )
	{
		// Stock answers "" for an index no entry carries (decals.cpp:193), and
		// so does this: the ABI's own miss value is -1 and the translation back
		// to stock's contract happens HERE, so no caller learns a new one.
		m_szName[ 0 ] = '\0';

		IManagedResources *pArea = ManagedResources();

		if ( pArea == NULL )
			return m_szName;

		if ( pArea->ServerDecalNameForIndex( nIndex, m_szName, sizeof( m_szName ) ) < 0 )
			m_szName[ 0 ] = '\0';

		return m_szName;
	}

	char const *TranslateDecalForGameMaterial( char const *decalName,
	                                           unsigned char gamematerial )
	{
		IManagedResources *pArea = ManagedResources();

		if ( pArea == NULL || decalName == NULL )
			return decalName;

		// The game material crosses as an int32_t. It is a `char` used as a KEY
		// -- stock builds a two-byte string out of it and looks that up -- and
		// `char`'s signedness is implementation-defined, so widening it here
		// through `unsigned char` is what makes the key the same byte on both
		// sides of the boundary rather than the same number on one of them.
		int32_t nWritten = pArea->ServerDecalTranslateForGameMaterial(
			decalName, (int32_t)gamematerial, m_szTranslated, sizeof( m_szTranslated ) );

		// Stock's contract: a decal it has no translation for comes back
		// UNCHANGED, and the caller compares the pointer against nothing -- it
		// just uses it. -1 is "managed had no answer", which is that case.
		if ( nWritten < 0 )
			return decalName;

		return m_szTranslated;
	}

private:
	char m_szName[ 260 ];
	char m_szTranslated[ 260 ];
};

//-----------------------------------------------------------------------------
// The instance, function-local so that nothing at file scope holds state. It is
// built on the first ask; nothing can be asked before the CLR is up anyway:
// every method guards on a null managed table and answers stock's miss value.
//-----------------------------------------------------------------------------
static CShellDecalEmitter *ShellDecalEmitter()
{
	static CShellDecalEmitter s_Emitter;
	return &s_Emitter;
}

//-----------------------------------------------------------------------------
// ss_decals.h's entry points.
//-----------------------------------------------------------------------------
int ss::DecalIndexForName( const char *pszDecalName )
{
	return ShellDecalEmitter()->GetDecalIndexForName( pszDecalName );
}
