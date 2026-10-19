//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON c  static-init registration
// Purpose: Controls the loading, parsing and creation of the entities from the BSP.
// SOURCESHARP: game/server/mapentities.cpp, SPLIT AND RELOCATED.
// plan_shim.md Phase 2c. 1,355 lines -> 636.
// REASON (c) -- static-init registration. What is left is the front door onto
// EntityFactoryDictionary(), which is where LINK_ENTITY_TO_CLASS and
// CWorldFactory put themselves from their own static initialisers, plus the
// parser that turns one block of BSP text into a call on it. A managed spawner
// cannot be that door: the dictionary is native, the world it builds is the one
// C++ object in this port the engine names by index, and shell_entityapi.cpp's
// template-instance pass needs a block parsed and an entity back in the same
// statement.
// WHAT LEFT, AND THE PROOF. Twelve bands, ~710 lines. Every export in them had
// ZERO importers on the debug link line -- tools/shim-census over the graph
// (118 server objects, 118 on disk, 0 ghosts), joined per symbol with
// nm -C --defined-only against nm -C -u over the other 117, symbol type
// filtered [TWDBRV]:
//   MapEntity_ParseAllEntities   SpawnHierarchicalList   SpawnAllEntities
//   SetupParentsForSpawnList     RememberInitialEntityPositions
//   MapEntity_PrecacheEntity     MapEntity_PendingNonBaseCount
//   MapEntity_PendingNonBaseEntity  MapEntity_RecordPendingNonBaseMapData
//   ExtractParentName            CreateNetworkableByName
//   + the file-static sort band (CompareSpawnOrder,
//     ComputeSpawnHierarchyDepth(_r), SortSpawnListByHierarchy) and the three
//     g_pTemplateWindow* statics, none of which had a live intra-file caller
//     once MapEntity_ParseAllEntities went
// AND A LIVE COUNT, because a census answers "nothing links it" and not
// "nothing runs it". A counter on every entry point in this file, over TWO
// dm_lockdown loads in one process:
//   MapEntity_ParseAllEntities        0      MapEntity_PrecacheEntity      0
//   SpawnAllEntities                  0      SpawnHierarchicalList         0
//   MapEntity_SpawnMapTemplates       0      MapEntity_TemplateCapture     0
//   MapEntity_PendingNonBaseCount     0      MapEntity_PendingNonBaseEntity 0
//   MapEntity_RecordPendingNonBaseMapData 0  ExtractParentName             0
//   CreateNetworkableByName           0      CreateGameEntityByName        0
//   MapEntity_ParseWorldOnly          1 per load   <- the whole native map load
//   CreateEntityByNameEither        390 per load   <- managed's SourceSharp_CreateEntity
//   CreateNetworkableEntityByName   391 per load
// THE PREMISE THE PLAN INFERRED IS NOW MEASURED. 2c deletes this band on the
// ground that "CreateEntityByName always returns NULL since the entity port".
// It does, and the proof is not the absence of calls: CreateEntityByName's NULL
// comes from SourceSharp_SDKEntityFromNetworkable< IServerEntity >, which is
// pNetworkable->GetBaseEntity(). A probe asked that same question of EVERY
// entity the map creates, inside CreateEntityByNameEither:
//     GetBaseEntity asked = 781   non-NULL = 0      (two dm_lockdown loads)
// 781 trials, zero. The only registered factories are managed classes, whose
// GetBaseEntity() is NULL by construction, and worldspawn, which
// CreateEntityByName refuses by name at its top.
// AND THE OTHER OPEN QUESTION, 2c's own: "KeyValueShellOnly at
// shell_standalone_base.cpp:2215 is still called from mapentities.o,
// shell_level.o and player.o; a one-map-load counter on
// CStandaloneEntity::KeyValue says whether a second parser is live."
//   CStandaloneEntity::KeyValue        0 over two map loads
//   KeyValueShellOnly              4,297 after load 1, 8,600 after load 2
//   SourceSharp_MapSpawnCreate keys 4,297 after load 1, 8,594 after load 2
// The counts agree to the key. Every KeyValueShellOnly arrival on a map load
// comes down through SourceSharp_MapSpawnCreate -- managed's own key delivery --
// and the two-armed CStandaloneEntity::KeyValue, which is what a NATIVE parser
// would use, is reached zero times. There is no second parser, and the split is
// therefore as large as N2 thought rather than smaller.
// WHAT DID NOT MOVE TO MapSpawner.cs, AND WHY THE PLAN'S "~350 move" IS ALREADY
// DONE. 2c asks for the pending-non-base list, MapEntity_SpawnMapTemplates,
// MapEntity_TemplateCapture, the Pending* accessors and MapEntity_ParseEntity to
// move into MapSpawner.cs. MapSpawner.cs ALREADY HAS ALL OF THEM: the window is
// its `_windowOpen` bool (MapSpawner.cs:120, "stock's three g_pTemplateWindow*
// statics, as one bool on an instance"), the capture is Capture() at :514, the
// spawn order is SpawnMap's five numbered steps, and the pending list is the
// `_spawned` list. That move landed at plan_entity.md step 2d. So the ~350 is a
// DELETE of the native duplicate, which is what happened above, and what is left
// here is the runtime single-block path that MapSpawner does not own:
// shell_entityapi.cpp's point_template instance (and, until plan_server_native_v2
// T3, the debug cs_standalone_mapspawn) -- from PARSED pairs since plan_server_novalve.md
// P5, which also deleted the text parser and took this file onto the R5
// include set (no cbase.h, no game/ headers).
// FreeContainingEntity WENT TO ITS ONE CALLER, shell_gameents.cpp's
// CServerGameEnts::FreeContainingEntity, which forwarded to it and nothing else.

// SERVER.SO ON THE R5 SET (plan_server_novalve.md P5): public/ headers and
// ours, in place of cbase.h, mapentities.h and mapentities_shared.h.
#include "igameentity.h"
#include "iservernetworkable.h"
#include "handle_cross.h"          // SourceSharp_SDKEntityFromNetworkable
#include "ss_util.h"               // engine, ss::FStrEq, UTIL_Remove
#include "ss_entityhandle.h"       // ss::GameEntityFromEntityHandle
#include "standalone_relays.h"
#include "ss_mapentities.h"
#include "ss_entityfactory.h"
#include "ss_world.h"

#include <algorithm>
#include <vector>


// The pending queue of non-CBaseEntity map entities went in
// plan_server_native_v2 T2. CreateFromKeys pushed onto it and its one live
// caller, the point_template path, claimed the entity straight back; the
// managed map load never queued here (0 entries over two dm_lockdown loads).
// Each caller now spawns what it created.
// g_pForceAttachEdict went with baseentity_statics.cpp
// (plan_server_native_min P1): stock's PostConstructor read it to attach the
// forced edict, and no CBaseEntity::PostConstructor exists to read it here.

//-----------------------------------------------------------------------------
// The same creation, without discarding an entity that is not a CBaseEntity.
//
// CreateEntityByName cannot answer for one, because its return type cannot
// name it. This is the honest version for callers that can cope.
//-----------------------------------------------------------------------------
IServerNetworkable *CreateNetworkableEntityByName( const char *className )
{
	return ss::EntityFactoryDictionary()->Create( className );
}

//-----------------------------------------------------------------------------
// Creation by name, once, for a classname on either hierarchy.
//
// The factory is asked ONE time and the answer is then classified. That matters
// because both hierarchies are reachable only through their own accessor, and a
// caller that guesses wrong used to create the entity, throw it away, and create
// it again through the other one.
//
// The parallel hierarchy is asked FIRST, deliberately. It is the larger half of
// this mod's entity list -- most classnames the game registers are C# now -- so
// asking the SDK first means the common case is the one that pays for a wrong
// guess. Nothing about the order is load-bearing beyond that; the entity is
// reached through the entity HANDLE rather than by casting the networkable,
// because the two are different subobjects of the same object.
//
// Whatever comes back is the CALLER'S. Nothing is released here.
//-----------------------------------------------------------------------------
IGameEntity *CreateEntityByNameEither( const char *className, IGameEntity **ppOutBaseEntity,
                                       IServerEntity **ppOutServerEntity )
{
	if ( ppOutBaseEntity )
		*ppOutBaseEntity = NULL;
	if ( ppOutServerEntity )
		*ppOutServerEntity = NULL;

	IServerNetworkable *pNetworkable = CreateNetworkableEntityByName( className );
	if ( !pNetworkable )
		return NULL;

	if ( IGameEntity *pGameEntity = ss::GameEntityFromEntityHandle( pNetworkable->GetEntityHandle() ) )
	{
		// The same address the game-entity cast crossed from: on the server
		// the handle IS the server entity. Handed out so a caller can ask
		// the faces IGameEntity no longer duplicates.
		if ( ppOutServerEntity )
			*ppOutServerEntity = (IServerEntity *)pNetworkable->GetEntityHandle();
		return pGameEntity;
	}

	if ( ppOutBaseEntity )
	{
		*ppOutBaseEntity = pNetworkable->GetBaseEntity();
		if ( *ppOutBaseEntity )
			return NULL;
	}

	// Registered, created, and unreachable: it implements neither the SDK's base
	// class nor the interface that stands in for it. Releasing it is the only
	// correct thing, since nothing can configure or spawn it -- and keeping it
	// would leak one entity per call.
	Warning( "'%s' is neither a CBaseEntity nor an IGameEntity; removed\n", className );
	pNetworkable->Release();
	return NULL;
}

//-----------------------------------------------------------------------------
// The parallel hierarchy alone, for callers that want nothing else.
//
// A CBaseEntity asked for here is created and released, which is the same answer
// CreateEntityByName gives for the opposite mistake. A caller that can cope with
// either should use CreateEntityByNameEither and pay for neither.
//-----------------------------------------------------------------------------
IGameEntity *CreateGameEntityByName( const char *className, IServerEntity **ppOutServerEntity )
{
	// No base-entity out-parameter: CreateEntityByNameEither then releases
	// anything that is not on the parallel hierarchy itself. Its CBaseEntity
	// arm, and the UTIL_Remove it called, went in plan_server_native_v2 T2: a
	// standalone's handle answers first, so no entity reached it.
	return CreateEntityByNameEither( className, NULL, ppOutServerEntity );
}


//=============================================================================
// THE WORLD, FROM ITS PARSED KEYS (plan_server_novalve.md P5).
//
// WHY THE WORLD IS BUILT HERE AT ALL, when the managed map spawner owns
// everything else: the world is the one entity in this port that stays a C++
// object, because the engine names it by index, and the only construction of
// CWorld in the whole build is CWorldFactory::Create (world_adapter.cpp).
// Without it there is no CWorld::Spawn, no CWorld::Precache, and
// EntityPrecache_IsAllowed() is false for the whole level init -- 199 "no model
// yet", 17 "PrecacheDecal: too late", and a SIGSEGV in the engine's ray cast
// from the first decal (status/worldspawn.md).
//
// WHAT CHANGED: THE PARSE LEFT. This used to be MapEntity_ParseWorldOnly, a
// brace walk over the residue text with a filter admitting one classname.
// Walking text to find a block is a decision about the text, so it is C#'s
// (plan §3.2): MapSpawner has already split the lump with MapEntityBlock, picks
// the worldspawn block, and hands its pairs over through IShimLevel::SpawnWorld
// in block order -- classname included, as CEntityMapData's walk gave them.
//
// Spawn() and not DispatchSpawn(): DispatchSpawn is CBaseEntity's whole spawn
// and every part of it is meaningless or disabled for worldspawn.
//=============================================================================
static CWorld *MapEntity_CreateWorld();   // below, with the template path's world arm

bool MapEntity_SpawnWorld( const char *const *ppKeys, const char *const *ppValues, int nKeys )
{
	CWorld *pWorld = MapEntity_CreateWorld();
	if ( !pWorld )
		return false;

	for ( int i = 0; i < nKeys; i++ )
		pWorld->KeyValue( ppKeys[ i ], ppValues[ i ] );

	pWorld->Spawn();
	return true;
}




//-----------------------------------------------------------------------------
// A standalone entity from pairs that arrive parsed (plan_server_novalve.md
// P5): create and key each pair in order. The caller places and spawns it.
// What the text parse's non-base arm did, without the text (that parse is
// gone). Native carries out what the caller decided; it reads no text.
//-----------------------------------------------------------------------------
IGameEntity *MapEntity_CreateFromKeys( const char *pClassName, const char *const *ppKeys,
                                       const char *const *ppValues, int nKeys )
{
	if ( !pClassName )
		return NULL;

	IGameEntity *pGameEntity = CreateGameEntityByName( pClassName, NULL );
	if ( !pGameEntity )
		return NULL;

	for ( int i = 0; i < nKeys; i++ )
		StandaloneEntity_ApplyMapKey( pGameEntity, ppKeys[ i ], ppValues[ i ] );

	return pGameEntity;
}

//-----------------------------------------------------------------------------
// worldspawn, which is on NEITHER of the two hierarchies this function knows.
//
// It cannot come back through CreateEntityByName: that function's return type
// is CBaseEntity *, so it resolves the factory's answer with
// SourceSharp_SDKEntityFromNetworkable, gets NULL for this class, and then --
// correctly, for its own contract -- DESTROYS the object rather than leak it.
// The world would be built and freed on the same line.
//
// So this asks the factory directly, through the accessor that already exists
// for callers who "can cope" (CreateNetworkableEntityByName). The keys and the
// Spawn are MapEntity_SpawnWorld's, which calls this.
//-----------------------------------------------------------------------------
static CWorld *MapEntity_CreateWorld()
{
	IServerNetworkable *pNetworkable = CreateNetworkableEntityByName( "worldspawn" );
	if ( !pNetworkable )
		return NULL;

	// The singleton, claimed by CWorld's own constructor. Read back rather than
	// cast from the networkable: a cast here would be this file asserting a
	// type, and the constructor has already recorded the fact.
	CWorld *pWorld = GetWorldEntity();

	if ( !pWorld )
	{
		Warning( "[SourceSharp] worldspawn was created and did not claim the "
		         "world singleton. The level has no world.\n" );
		pNetworkable->Release();
		return NULL;
	}

	return pWorld;
}


