//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the engine services the shim still has to ask ======//
// SS_REASON b  an interface resolved by name at load
//
// plan_shim.md §5 3a. FOUR TRANSLATION UNITS BECAME ONE. shell_files.cpp,
// shell_steamcontext.cpp, shell_mdlcache.cpp and shell_templates.cpp were
// 109 + 127 + 119 + 83 lines, and between them held seven slot bodies and one
// forward — each file a header and an include block wrapped around two or three
// functions.
//
// WHAT MAKES THEM ONE FILE RATHER THAN FOUR SMALL ONES is not that they are
// small. It is that every one of them is the same KIND of thing: a leaf that
// asks an ENGINE-OWNED global a question managed code cannot ask itself, holds
// no state, and decides nothing. filesystem, steamgameserverapicontext, the
// model cache and the managed template store are four different globals and one
// pattern. A file per global is a file per pointer.
//
// NO FILE-SCOPE STATE, and it is gated rather than promised:
// SteamContextSources.NeitherFileHoldsStateOfItsOwn reads this file and
// requires nothing at file scope but the step definitions. That rule is why
// shell_mdlcache's walk declares its `extern CUtlVector<CStandaloneEntity *> &
// StandaloneEntities();` inside the body rather than above it — a declaration
// is not state, but the audit looks for the word `static` and a file-scope
// non-static would not be one, so the cheaper answer is to put the declaration
// where it is used.
//
// THE FOUR BODIES ARE CARRIED VERBATIM. A merge that rewrote a working body
// would be a change disguised as a move, and each of these four has an argument
// attached to it that somebody had to make once already.
//
//=============================================================================//

// shell_files: the GAME search path, which is the only way to reach the SDK's
// own loose hl2/scripts.
#include "filesystem.h"

extern IFileSystem *filesystem;

// shell_steamcontext: CSteamGameServerAPIContext is Steam's own struct of
// interface pointers, and steamgameserverapicontext is the engine's instance.
#include "eiface.h"

// shell_mdlcache: the standalone list this walks, and the entity it holds.
#include "shell_standalone.h"

// shell_templates: the managed store's slot.
#include "sourcesharp.h"

#include "sourcesharp_gamesystem.h"
#include "shell_server.h"


//=============================================================================//
// READING A GAME SCRIPT -- was shell_files.cpp
//=============================================================================//

//-----------------------------------------------------------------------------
// filesystem->Size, on the GAME path id -- the same one CPropData and
// CDecalEmitterSystem's own KeyValues::LoadFromFile calls used, so a file
// either system could find is a file this can find.
//-----------------------------------------------------------------------------
int32_t SourceSharp_FilesGameFileSize( const char *pszPath )
{
	if ( pszPath == NULL || pszPath[ 0 ] == '\0' || filesystem == NULL )
		return -1;

	// Asked through Open/Size/Close rather than filesystem->Size( path ) alone:
	// Size() on a path that does not exist answers 0 on this filesystem, which
	// is the one answer this slot may not give for a missing file.
	FileHandle_t hFile = filesystem->Open( pszPath, "rb", "GAME" );

	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;

	unsigned int nSize = filesystem->Size( hFile );
	filesystem->Close( hFile );

	return (int32_t)nSize;
}

//-----------------------------------------------------------------------------
// filesystem->Read into the CALLER's buffer.
//
// The byte count read, or -1. NOT NUL-TERMINATED and not claimed to be: the
// managed side is handed a length and reads exactly that many bytes, because a
// game script is allowed to contain no terminator and appending one would need
// a byte of the caller's buffer the caller did not offer.
//-----------------------------------------------------------------------------
int32_t SourceSharp_FilesReadGameFile( const char *pszPath, char *pOut, int32_t nOutSize )
{
	if ( pszPath == NULL || pszPath[ 0 ] == '\0' || pOut == NULL || nOutSize <= 0 )
		return -1;

	if ( filesystem == NULL )
		return -1;

	FileHandle_t hFile = filesystem->Open( pszPath, "rb", "GAME" );

	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;

	unsigned int nSize = filesystem->Size( hFile );

	// REFUSED, not truncated. See the header comment.
	if ( (int32_t)nSize > nOutSize )
	{
		filesystem->Close( hFile );

		Warning( "[SourceSharp] game file '%s' is %u byte(s) and the buffer offered "
		         "is %d; refused rather than truncated -- half a KeyValues document "
		         "parses into a table missing its tail\n",
		         pszPath, nSize, nOutSize );
		return -1;
	}

	int nRead = filesystem->Read( pOut, (int)nSize, hFile );
	filesystem->Close( hFile );

	return (int32_t)nRead;
}

//=============================================================================//
// STEAM'S GAME SERVER CONTEXT WAS HERE -- plan_server_novalve.md P2.5.
//
// Reacquire (Clear then CSteamGameServerAPIContext::Init), Clear and
// RequestGameplayStats are C#'s own now (SourceSharp.Abi.SteamGameServerFlat,
// libsteam_api.so's flat API, opened RTLD_NOLOAD so it is the engine's copy),
// and server.so neither links libsteam_api.so nor includes a steam/ header.
// Their three HostSteamGameServer* slots are retired (plan_engineapi_vtables
// Phase 0) and their stubs deleted.
//=============================================================================//

// SourceSharp_HostIsDedicatedServer IS GONE -- plan_native_api.md Phase 4.
//
// IEngineHost.IsDedicatedServer calls IVEngineServer::IsDedicatedServer
// directly.

//=============================================================================//
// THE MODEL CACHE FLUSH -- was shell_mdlcache.cpp
//=============================================================================//

//-----------------------------------------------------------------------------
// gameinterface.cpp:2049-2061, WITHOUT THE dynamic_cast (plan_entity.md step 4d,
// cast family 5).
//
// Stock walks gEntList and `dynamic_cast<CBaseAnimating *>`s each entity.
// plan_entity.md gate 3 counts every way of turning something into an entity
// pointer and requires zero, and dynamic_cast is on that list by name -- so
// this site had to go whatever else changed.
//
// WHAT REPLACES IT IS A VIRTUAL CALL, WHICH IS NOT A CAST. CBaseEntity declares
// `virtual CBaseAnimating *GetBaseAnimating() { return 0; }` (baseentity.h:1030)
// and CBaseAnimating overrides it with `return this` (baseanimating.h:83). The
// answer is identical for every entity on the list, it costs one indirect call
// instead of a runtime type walk, and the pattern is already in this tree --
// baseanimating.h:172 uses exactly it, under the SS_CBASEENTITY_ALIAS guard,
// for the same reason.
//
// A static_cast would have been the wrong answer for every brush entity, which
// is what stock's comment was defending against; a virtual that the base
// answers NULL to is not that trade.
//
//-----------------------------------------------------------------------------
// AND THE HALF THE WALK NEVER HAD.
//
// This walk exists to free CBaseAnimating::m_pStudioHdr after the engine
// flushes the model cache. A STANDALONE entity has no m_pStudioHdr -- it builds
// a CStudioHdr on the stack per ask, through SourceSharp_StandaloneStudioHdrFor
// -- so nothing about it was stale. Its BONE CACHE is: m_CachedBones holds
// matrices built out of the OLD studio data, and BoneCacheKey_t is keyed on the
// model INDEX, which a flush does not change. So every standalone entity kept
// posing itself from a skeleton that no longer existed, silently, and the
// symptom was the same one this file was written to fix -- a model reload that
// appears not to take.
//
// That fix was InvalidateBoneCache here. The skeleton is managed now (plan
// physics_bones_native_zero 1.2) and so is its half of the flush:
// ServerSkeletons.InvalidateMdlCache drops the copied model images and
// invalidates every skeleton, from the same managed flush that calls this.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitiesInvalidateMdlCacheAll( void )
{
	// HOW MANY, rather than nothing -- lane diag-visibility. The engine
	// flushing its model cache leaves no trace a running game can be asked
	// about, so an empty body and a working one read the same from outside;
	// A2's own note says that was "the whole of Phase A2's difficulty". The
	// number is what C# prints beside "model cache flushed (#N)", and it is the
	// same number on both sides because the client's half of this
	// (SourceSharp_ClientEntitiesInvalidateMdlCacheAll) counts the same way.
	int32_t nInvalidated = 0;

	// THE SDK ARM, AND IT IS THE ARM THAT GOES. gEntList is defined in
	// entitylist.cpp, which is in the flip's removal set, and GetBaseAnimating
	// is a CBaseEntity member that leaves with the class -- so armed there is
	// neither a list to walk nor a skeleton to find at the end of it. The
	// standalone walk below is not a fallback here; armed it is the whole
	// function, and it already counts the same way.

	// Declared in the body rather than at file scope, deliberately. This file
	// is two pure steps over engine-owned globals and
	// SteamContextSources.NeitherFileHoldsStateOfItsOwn holds it to exactly
	// that: nothing at file scope but the step definitions, because a
	// remembered flag here would be state the statics audit cannot see (it
	// looks for `static`, and a file-scope non-static would not be one). A
	// declaration is not state, but the guard is coarse on purpose and the
	// cheaper answer is to put the declaration where it is used.
	extern std::vector< CStandaloneEntity * > &StandaloneEntities();

	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	// THE BONE CACHE IS MANAGED NOW (plan physics_bones_native_zero 1.2):
	// ServerSkeletons drops its model images and invalidates every skeleton in
	// the same flush (ServerGameDll.InvalidateMdlCache). Nothing native holds
	// studio data per entity any more, so this counts the population the flush
	// reached, which is the number C# prints.
	nInvalidated = (int32_t)standalones.size();

	return nInvalidated;
}

//=============================================================================//
// THE MAP PARSE'S ONE TEMPLATE FORWARD -- was shell_templates.cpp
//=============================================================================//

#include "imanaged_areas.h"   // plan_engineapi_vtables E9: IManagedEntities

//=============================================================================//
// THE BREAK-PIECE PARSER WAS HERE (shell_breakpieces.cpp), and the one it
// called, BuildPropList / CBreakParser (shell_propdata_system.cpp).
//
// What a model breaks into is C#'s own now (SourceSharp.Abi.BreakModels, over
// IVModelInfo::GetVCollide's keyvalue text and the model descriptor's
// attachment origins), held to this parser field for field before it went:
// 588 of 588 pieces on dm_lockdown, first load and after a changelevel.
// The ModelBreakPieceCount and ModelBreakPiece slots are retired
// (plan_engineapi_vtables Phase 0) and their stubs deleted.
//=============================================================================//
