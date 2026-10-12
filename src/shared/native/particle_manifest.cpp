//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the particle manifest, without the entity ===========//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_entity.md Phase 4, step 4c. The manifest half of
// game/shared/particle_parse.cpp, moved out VERBATIM so that that file can leave
// both vpcs.
//
// WHY THE SPLIT IS WHERE IT IS. particle_parse.cpp was two files sharing a name.
// One half reads a KeyValues manifest, hands filenames to g_pParticleSystemMgr
// and puts names into a network string table; it knows nothing about entities,
// and it is the ONLY half the SourceSharp shim has ever called — three symbols
// on the server (shell_level, world_adapter) and two on the client. The
// other half is the DispatchParticleEffect family: every one of those takes an
// entity pointer, follows an attachment point and reads a model, and every one
// of them is already answered by managed code through the effects ABI.
//
// So this is not a rewrite and there is nothing clever in it. The five bodies
// below are byte-for-byte what they were; what changed is which object they land
// in, and therefore what the linker still needs from the file they left.
//
// IN BOTH VPCS, because both sides call these. The server's ParseParticleEffects
// runs at level load (shell_level) and the client's ReloadParticleEffects
// runs on a pure-server file change; splitting them per side would be two copies
// of a manifest parser, which is exactly the duplication step 4d's library
// argument rules out.
//
// NOT ENTITY-FREE IN THE bone_library SENSE, and it does not claim to be: it
// includes cbase.h like every other shim TU, because ReloadParticleEffects wants
// the current level name and that comes off gpGlobals on one side and the engine
// interface on the other. Nothing here NAMES an entity type — that is a
// different and weaker property than the one overlay_library.h and
// bone_library.h hold, and it is stated rather than implied so that nobody
// later reads this file as one of those.
//
//=============================================================================//

#include "cbase.h"
#include "particle_parse.h"
#include "ss_string.h"
#ifdef CLIENT_DLL
#include "particles/particles.h"
#include "imanaged_areas_client.h"
#include "ss_bitblock.h"

#include <string>
#include <vector>
#endif

#ifdef GAME_DLL
#include "networkstringtable_gamedll.h"
#include "te_effect_dispatch.h"
#else
#include "networkstringtable_clientdll.h"
#include "c_te_effect_dispatch.h"
#endif


// CLIENT ONLY FROM HERE TO THE #endif (plan_server_novalve.md P2.1). The
// manifests are read in C# on both sides (ParticleManifestFiles; the server's
// ParticleManifest reads the files' names too, plan_client_novalve C9), and server.so
// links neither particles.a nor dmxloader.a; client.so still loads every
// definition through CParticleSystemMgr, because it simulates and draws them.
#ifdef CLIENT_DLL

//-----------------------------------------------------------------------------
// The manifests are read in C# (C9 KeyValues K1: SourceSharp.Shared's
// ParticleManifestFiles, the server's reader too): one crossing a manifest,
// the file entries back NUL-separated in one BitBlock. Answers the entries'
// count, -1 for a map with no manifest; a block too small, or no managed
// side, is said and answers none.
//-----------------------------------------------------------------------------
enum { kManifestBlockBytes = 16 * 1024 };

// CParticleMgr::Init asks for the first read during the client DLL's Init,
// before the managed runtime that reads the manifest is up. That read is held
// and run the moment it is (SourceSharp_ParticleManifestRuntimeUp, from
// CShellClientDLL::Init), still inside Init and so before any precache.
static bool s_bReadPending;
static bool s_bPendingLoadSheets;
static bool s_bPendingPrecache;

void ParseParticleEffects( bool bLoadSheets, bool bPrecache );

static int ManifestFiles( bool bMap, const char *pszMapName, std::vector<std::string> &list )
{
	IManagedParticlesClient *p = ManagedParticlesClient();
	if ( !p )
		return 0;

	static uint8_t s_Block[ ss::kBitBlockHeaderBytes + kManifestBlockBytes ];
	const int nFiles = p->ManifestFiles( bMap ? 1 : 0, pszMapName ? pszMapName : "", s_Block, sizeof( s_Block ) );
	if ( nFiles == -2 )
	{
		Warning( "[SourceSharp] a particle manifest's file list is over %d bytes; not loaded\n", (int)kManifestBlockBytes );
		return 0;
	}
	if ( nFiles <= 0 )
		return nFiles;

	const char *pszAt = (const char *)s_Block + ss::kBitBlockHeaderBytes;
	const char *pszEnd = pszAt + ss::BitBlockBits( s_Block ) / 8;
	for ( int i = 0; i < nFiles && pszAt < pszEnd; i++ )
	{
		list.emplace_back( pszAt );
		pszAt += strlen( pszAt ) + 1;
	}
	return nFiles;
}

// The game manifest's files. particle_parse.h's GetParticleManifest
// (CUtlVector<CUtlString>) had no caller outside this file, and its CUtlString
// was the last thing taking tier1's utlstring.o onto client.so's link (C9),
// so the list is a std::vector here and that declaration is left undefined.
static void GameManifestFiles( std::vector<std::string> &list )
{
	ManifestFiles( false, NULL, list );
}

void SourceSharp_ParticleManifestRuntimeUp()
{
	if ( !s_bReadPending || !ManagedParticlesClient() )
		return;
	s_bReadPending = false;
	ParseParticleEffects( s_bPendingLoadSheets, s_bPendingPrecache );
}


//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
// Full reads of the manifest's definitions, for /client/{slot}/particles'
// "definitionReads" (lane c5-particles): every full read is this function.
static int s_nParticleDefinitionReads;

int32_t SourceSharp_ParticleDefinitionReads()
{
	return s_nParticleDefinitionReads;
}

void ParseParticleEffects( bool bLoadSheets, bool bPrecache )
{
	MEM_ALLOC_CREDIT();

	if ( !ManagedParticlesClient() )
	{
		s_bReadPending = true;
		s_bPendingLoadSheets = bLoadSheets;
		s_bPendingPrecache = bPrecache;
		return;
	}

	s_nParticleDefinitionReads++;

	g_pParticleSystemMgr->ShouldLoadSheets( bLoadSheets );

	std::vector<std::string> files;
	GameManifestFiles( files );

	int nCount = (int)files.size();
	for ( int i = 0; i < nCount; ++i )
	{
		g_pParticleSystemMgr->ReadParticleConfigFile( files[i].c_str(), bPrecache, false );
	}

	g_pParticleSystemMgr->DecommitTempMemory();
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void ReloadParticleEffects()
{
	MEM_ALLOC_CREDIT();

	std::vector<std::string> files;
	GameManifestFiles( files );

	// CAB 2/17/11 Reload all the particles regardless (Fixes filename change exploits).
	bool bReloadAll = true;

	//int nCount = files.Count();
	//for ( int i = 0; i < nCount; ++i )
	//{
	//	// Skip the precache marker
	//	const char *pFile = files[i];
 //		if ( pFile[0] == '!' )
 //		{
 //			pFile++;
 //		}

	//	char szDX80Filename[MAX_PATH];
	//	V_strncpy( szDX80Filename, pFile, sizeof( szDX80Filename ) );
	//	V_StripExtension( pFile, szDX80Filename, sizeof( szDX80Filename ) );
	//	V_strncat( szDX80Filename, "_dx80.", sizeof( szDX80Filename ) );
	//	V_strncat( szDX80Filename, V_GetFileExtension( pFile ), sizeof( szDX80Filename ) );

	//	if ( pFilesToReload->IsFileInList( pFile ) || pFilesToReload->IsFileInList( szDX80Filename ) )
	//	{
	//		Msg( "Reloading all particle files due to pure settings.\n" );
	//		bReloadAll = true;
	//		break;
	//	}
	//}

	// plan_map_load P8: stock parsed the map's manifest here AND again in
	// LevelInitPreEntity right after; the global reload below replaced this
	// first parse's definitions by name, so only the second one ever counted.
	// The caller's parse is the one kept.

	if ( bReloadAll )
	{
		ParseParticleEffects( true, true );
	}
	
	g_pParticleSystemMgr->DecommitTempMemory();
}

//-----------------------------------------------------------------------------
// Purpose: loads per-map manifest!
//-----------------------------------------------------------------------------
int ParseParticleEffectsMapFiles( const char *pMapName, bool bLoadSheets )
{
	MEM_ALLOC_CREDIT();

	std::vector<std::string> files;
	if ( ManifestFiles( true, pMapName, files ) < 0 )
		return 0;	// no manifest, and no warning: most maps have none

	int nCount = (int)files.size();
	if ( !nCount )
	{
		return 0;
	}

	g_pParticleSystemMgr->ShouldLoadSheets( bLoadSheets );

	for ( int i = 0; i < nCount; ++i )
	{
		g_pParticleSystemMgr->ReadParticleConfigFile( files[i].c_str(), true, true );
	}

	g_pParticleSystemMgr->DecommitTempMemory();
	return nCount;
}

// The stock entry point, unchanged for its callers: the fork's particle_parse.h
// declares it void.
void ParseParticleEffectsMap( const char *pMapName, bool bLoadSheets )
{
	ParseParticleEffectsMapFiles( pMapName, bLoadSheets );
}

//-----------------------------------------------------------------------------
// plan_map_load P8: what a level needs from ReloadParticleEffects when nothing
// has replaced a stock definition since the last full read. LevelShutdown's
// UncacheAllParticleSystems drops every definition's precache but keeps the
// definitions, and the always-precache mark ReadParticleConfigFile( ..., true )
// set on them; re-precaching those by name is the re-read's effect on them
// without the DMX parse (1.0-1.5 s a hop on a remote client at 178bfc1).
// Returns how many systems it precached.
//-----------------------------------------------------------------------------
int PrecacheDefinedParticleSystems()
{
	int nPrecached = 0;
	const int nCount = g_pParticleSystemMgr->GetParticleSystemCount();
	for ( int i = 0; i < nCount; ++i )
	{
		const char *pszName = g_pParticleSystemMgr->GetParticleSystemNameFromIndex( i );
		CParticleSystemDefinition *pDef = pszName ? g_pParticleSystemMgr->FindParticleSystem( pszName ) : NULL;
		if ( pDef && pDef->ShouldAlwaysPrecache() )
		{
			g_pParticleSystemMgr->PrecacheParticleSystem( pszName );
			++nPrecached;
		}
	}
	return nPrecached;
}

#endif // CLIENT_DLL

// PrecacheStandardParticleSystems stood here, GAME_DLL only: it precached the
// manager's always-precache systems from CWorld::Precache. ServerGameDll does
// that in C# now, right after it parses the manifests (plan_server_novalve.md
// P2.1).


//=============================================================================//
// THE SECOND MOVE OUT OF particle_parse.cpp, AND §6 GOT THE DESTINATION WRONG.
//
// plan_entity.md §6's "17 that LEAVE" table rules `particle_parse.cpp` out with
// «`SourceSharp_ParticleSystemStart` / `Stop` / `FollowPoints`, already in the
// shim» as what does the thing afterwards, and lane `c8-flip` carried that
// forward as its family 6: "RE-POINT, cheapest family". IT IS NOT A RE-POINT.
// Checked against the shim rather than against the sentence:
//
//   shell_sprite_render.cpp:1004  int32_t SourceSharp_ParticleSystemStart(
//                                     uint32_t hOwner, const char *pszName )
//
// takes a MANAGED ENTITY HANDLE, resolves it with SourceSharp_StandaloneFromHandle,
// refuses when there is no such entity, and attaches ONE long-lived
// CNewParticleEffect that ManagedParticleSystems_Follow() then drags after that
// entity every frame. It is the flame-on-a-burning-NPC call.
//
// The three §6-STAYS emitters that owe these two overloads want the opposite
// thing — a ONE-SHOT effect at a world point, with no owner at all:
//
//   fx_blood.cpp:518   DispatchParticleEffect( name, data.m_vOrigin, vecAngles )
//   fx_water.cpp:395   DispatchParticleEffect( "slime_splash_01", origin, vecAngles )
//   fx_tracer.cpp:178  DispatchParticleEffect( data.m_nHitBox, vecStart, vecEnd,
//                                              vecAngles, pEntity )
//
// Two of the three pass no entity whatsoever (the parameter defaults to NULL),
// and there is no handle to resolve, nothing to follow and nothing to stop. A
// re-point would have had to invent an owner entity per blood spray, and each
// one would have been REPLACED by the next spray, because Start() is documented
// "one at a time per entity". So the ruling here is RE-HOME, not RE-POINT, and
// this is the second of §6's five checked rulings to come back wrong.
//
// WHY THESE TWO AND NOT THE OTHER FIVE. The seven DispatchParticleEffect
// overloads split exactly the way this file's own header describes the manifest
// split: five of them take a ParticleAttachment_t, an attachment NAME or an
// attachment POINT, and end at `pEntity->ParticleProp()->Create(...)` — those
// ARE the entity component's front door and they leave with it, answered by the
// SourceSharp_ParticleSystem* trio above. These two never touch ParticleProp.
// They fill a CEffectData with an origin, a start and an angle and hand it to
// DispatchEffect( "ParticleEffect", ... ) — and BOTH ends of that are §6 STAYS:
// `c_te_effect_dispatch.cpp` ("the client-effect registry and its front door")
// and `effect_dispatch_data.cpp` ("the effect wire struct and its network
// proxies"). Nothing in the two bodies below reaches an entity except the arm
// that is already inside `#ifndef SS_CBASEENTITY_ALIAS`.
//
// MOVED VERBATIM, guards and all. The `#ifndef SS_CBASEENTITY_ALIAS` regions
// are byte-for-byte what they were in particle_parse.cpp — including the
// closing-on-the-`else` shape, whose reason is written inside them and is not
// this lane's to re-decide.
//
// IN BOTH VPCS, like everything else in this file: the server calls the
// name-and-origin overload too (`physics.cpp:4688`, `item_world.cpp:491`), and
// `particle_parse.cpp` is in the SERVER removal set as well as the client's.
//=============================================================================//

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void DispatchParticleEffect( int iEffectIndex, Vector vecOrigin, Vector vecStart, QAngle vecAngles, IGameEntity *pEntity )
{
	CEffectData	data;

	data.m_nHitBox = iEffectIndex;
	data.m_vOrigin = vecOrigin;
	data.m_vStart = vecStart;
	data.m_vAngles = vecAngles;

	// ARMED, NO CALLER PASSES AN ENTITY -- see the first overload for the
	// evidence and for the two walls that stop this being a retype.
	//
	// THERE IS NO GUARD ANY MORE -- this said "the guard closes on the `else`,
	// so armed the no-entity arm below runs unconditionally", and 1/N deleted
	// that guard. The clear below is unconditional for a simpler reason now. That is deliberate: this overload's else is not empty,
	// it CLEARS the entity field, and losing it would leave m_nEntIndex /
	// m_hEntity at whatever CEffectData was constructed with rather than at the
	// explicit "no entity" this function has always written.
	{
#ifdef CLIENT_DLL
		data.m_hEntity = NULL;
#else
		data.m_nEntIndex = 0;
#endif
	}

	DispatchEffect( "ParticleEffect", data );
}
//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
void DispatchParticleEffect( const char *pszParticleName, Vector vecOrigin, QAngle vecAngles, IGameEntity *pEntity )
{
	int iIndex = GetParticleSystemIndex( pszParticleName );
	DispatchParticleEffect( iIndex, vecOrigin, vecOrigin, vecAngles, pEntity );
}
