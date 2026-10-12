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
// shell_entityapi.cpp's point_template instance and shell_standalone.cpp's
// cs_standalone_mapspawn -- both from PARSED pairs since plan_server_novalve.md
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


// Declared rather than included, because shell_standalone.h is the parallel
// hierarchy's own header and this file has no other business with it. One
// function, called from one place -- see MapEntity_SpawnPendingNonBaseEntities.
void StandaloneHierarchy_ResolveParentNames();

// Map entities that are not CBaseEntity, parsed and keyed but not yet spawned.
// Drained by MapEntity_SpawnPendingNonBaseEntities, which its one caller runs
// after the block it just parsed.
//
// A LIST OF ENTITIES AND NOTHING ELSE NOW. It used to be a struct of four --
// the entity, its engine-facing IServerEntity, and a pointer and length into
// the BSP entity lump -- because a native point_template pass read the classname
// and transform off the second and copied the map TEXT off the other two. That
// pass is MapSpawner's (see MapEntity_TemplateCapture below), and the three
// extra fields had no reader left: MapEntity_PendingNonBaseEntity and
// MapEntity_RecordPendingNonBaseMapData were their only accessors and both had
// zero importers on the link line.
static std::vector< IGameEntity * > g_PendingNonBaseSpawns;
extern edict_t *g_pForceAttachEdict;

// creates an entity by string name, but does not spawn it
IGameEntity *CreateEntityByName( const char *className, int iForceEdictIndex )
{
	if ( iForceEdictIndex != -1 )
	{
		g_pForceAttachEdict = engine->CreateEdict( iForceEdictIndex );
		if ( !g_pForceAttachEdict )
			Error( "CreateEntityByName( %s, %d ) - CreateEdict failed.", className, iForceEdictIndex );
	}

	// worldspawn CANNOT COME BACK THROUGH THIS FUNCTION, and saying so here is
	// cheaper than the alternative.
	//
	// The return type is CBaseEntity * and the world is not one
	// (plan_cworld_trace.md T3), so the resolution below would answer NULL and
	// the object would be destroyed on the next line -- after its constructor
	// had already claimed the world singleton and attached EDICT 0. The caller
	// would get NULL, which is what it gets now, and the level would quietly
	// have lost its world.
	//
	// MapEntity_CreateWorld is the one legitimate creation and it goes through
	// CreateNetworkableEntityByName, which neither resolves nor destroys.
	// CWorldFactory refuses a second construction as well; this is the earlier
	// and more specific of the two refusals, and it names the caller's mistake
	// rather than the symptom.
	if ( ss::FStrEq( className, "worldspawn" ) )
	{
		Warning( "[SourceSharp] CreateEntityByName( \"worldspawn\" ): the world is not a "
		         "CBaseEntity and cannot be returned by this function. Use "
		         "GetWorldEntity(), or CreateNetworkableEntityByName if you mean to "
		         "build one.\n" );
		return NULL;
	}

	IServerNetworkable *pNetwork = ss::EntityFactoryDictionary()->Create( className );
	g_pForceAttachEdict = NULL;

	if ( !pNetwork )
		return NULL;

	// NOT asserted any more.
	//
	// The factory returns IServerNetworkable, which is the engine's own type
	// and has never required a CBaseEntity — so a class registered by this mod
	// may legitimately answer NULL here. The assert claimed otherwise and would
	// fire on a correct answer.
	//
	// A caller that wants such an entity uses CreateNetworkableEntityByName
	// below, which hands back what the factory actually made. This one keeps
	// its signature and its meaning: the CBaseEntity, or nothing.
	IGameEntity *pEntity = SourceSharp_SDKEntityFromNetworkable< IServerEntity >( pNetwork );

	// And nothing means NOTHING, including nothing left behind.
	//
	// Returning NULL while keeping the object leaks one entity per call, and
	// the call used to happen more often than it looked: MapEntity_PrecacheEntity
	// created every entity in the map here, precached it, and removed it —
	// through the pointer this function returns. An entity it could not name
	// was created, never precached, and never removed, so every map entity on
	// the parallel hierarchy existed twice for the whole level: once as the
	// real one and once as a ghost that answered to the same name.
	//
	// It read as the map having double the entities it has, which is exactly
	// what it was. That caller is gone with the rest of the dead band (Phase
	// 2c) and the release stays, because the two live callers -- ParseEntity's
	// factory arm and EntityCreate_NoSpawn -- leak exactly the same way without
	// it.
	if ( !pEntity )
	{
		ss::EntityFactoryDictionary()->Destroy( className, pNetwork );
		return NULL;
	}

	// stock's `if ( g_pScriptVM ) g_VScriptGameEventListener.RunGameEventCallbacks(
	// "OnEntityCreated", ... )` stood here inside a `#if 0`. VScript is not in
	// this build at all -- vscript_server.h left this file's include list with
	// it -- and a `#if 0` around a call to something that does not exist is
	// dead text rather than a switched-off feature.

	return pEntity;
}

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
	IGameEntity *pBaseEntity = NULL;
	IGameEntity *pGameEntity = CreateEntityByNameEither( className, &pBaseEntity, ppOutServerEntity );
	if ( pGameEntity )
		return pGameEntity;

	if ( pBaseEntity )
	{
		Warning( "'%s' is a CBaseEntity, not an IGameEntity; removed\n", className );
		UTIL_Remove( pBaseEntity );
	}

	return NULL;
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
// P5): create, key each pair in order, queue. What the text parse's non-base
// arm did, without the text (that parse is gone). Native carries out what the
// caller decided; it reads no text.
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

	g_PendingNonBaseSpawns.push_back( pGameEntity );
	return pGameEntity;
}

//-----------------------------------------------------------------------------
// Spawns the map's non-CBaseEntity entities.
//
// Called last, and deliberately: everything a map entity might refer to by name
// now exists. These take no part in the hierarchical ordering, whose spawn list
// is typed to the SDK's own entity handle and therefore CBaseEntity-only — so
// they cannot be parented and cannot be told to spawn before or after a
// particular other entity. They spawn together, after everything else.
//
// (The handle's SPELLING is deliberately not written out, here or anywhere else
// in this file. typed_handle_sweep_test sweeps every compiled shim source for
// it, raw, comments included -- plan_entity.md 1e: the shim holds CBaseHandle
// and resolves through the slot table at the use site. This file was stock's
// until plan_shim.md Phase 2c relocated it, so the sweep had never read it
// before and caught the inherited mention on the first run.)
//
// Exposed rather than inlined into the loop so that anything parsing a single
// block — a test, a spawner — can finish the job the same way the map loader
// does.
//-----------------------------------------------------------------------------
void MapEntity_SpawnPendingNonBaseEntities()
{
	// THE BATCH IS TAKEN BEFORE ANY OF IT SPAWNS, and the queue is emptied
	// before any of it spawns either. Spawning can BOTH add to this queue and
	// take from it -- a point_template instance parses a fresh block, queues the
	// entity and claims it straight back (shell_entityapi.cpp:1979) -- so
	// walking a live list by index is a batch whose membership changes under it.
	// The template pass one screen up already collects first for exactly this
	// reason ("walking that list while it shrinks would skip entries").
	//
	// It was index-over-a-live-list before plan_entity.md's Phase 4 exit and is
	// a snapshot now. The observable behaviour is the same: the claim that
	// shrinks the queue is on the same line as the add that grew it.
	//
	// NO SPAWN-PRIORITY ORDERING HERE, AND THAT IS MEASURED RATHER THAN
	// FORGOTTEN. Stock breaks CompareSpawnOrder's ties with a thirteen-classname
	// list, and eleven of those thirteen are managed entities -- but the managed
	// map load does not come through this function at all. MapSpawner.SpawnMapText
	// splits the lump and keeps every block whose class the managed registry
	// owns, handing native only the residue: 586 of dm_lockdown's 605 blocks are
	// managed, and over two map loads in one process this queue produced ZERO
	// entries (the crossing counter for the slot that used to be asked here read
	// 0 against ServerLevelInit 2). The ordering lives in MapSpawner, which is
	// where the spawn is; a slot asked here would be dead ABI surface.
	std::vector< IGameEntity * > batch;
	batch.swap( g_PendingNonBaseSpawns );

	for ( IGameEntity *pEntity : batch )
		StandaloneEntity_PlaceAndSpawn( pEntity, NULL, NULL );

	// A THIRD pass, and it has to be a third one.
	//
	// A parentname is known from the moment the block is parsed, but the offset a
	// child keeps from its parent is measured from where the two of them ended up
	// — and an entity does not have that until its own Spawn has run. So the names
	// were recorded during the parse and are only turned into links here, once
	// every standalone entity in the map is standing where it belongs.
	//
	// Which also means the loop above no longer decides anything about ordering
	// that parenting depends on. The comment on this function says these entities
	// cannot be parented; for a standalone child of a standalone parent that is no
	// longer true, and this is the line that changed it.
	StandaloneHierarchy_ResolveParentNames();
}


//-----------------------------------------------------------------------------
// Turns every entity of a name into a template -- and there is no native window
// for it to do that IN, so it answers nothing.
//
// THAT IS THE RIGHT ANSWER AND NOT A STUB. A template claims its sources
// between the map's entities being created and being spawned. That window was
// three file-scope statics here, opened and shut by MapEntity_SpawnMapTemplates
// from inside MapEntity_ParseAllEntities -- and MapEntity_ParseAllEntities has
// no caller (0 importers on the link line, and a counter on it read 0 over two
// dm_lockdown loads in one process). So the window has never once been open in
// this build and every path through the old body returned 0 at its first line.
//
// THE WINDOW IS MapSpawner'S. SourceSharp.Server/MapSpawner.cs:514 is Capture,
// and TemplateStore.MapWindow is the delegate it installs for exactly the span
// between its create pass and its spawn pass. What is left for this function is
// the RUNTIME path -- `ent_create point_template`, with no map being parsed --
// and TemplateStore.Capture's own comment says what belongs there: "stock's
// answer there is 'nothing', which is what the engine's own TemplateCapture
// gives." This is that engine-side TemplateCapture.
//
// KEPT RATHER THAN DELETED because SourceSharp_TemplateCapture
// (shell_entityapi.cpp:1791) is an ABI entry point and calls it, and because a
// symbol that vanishes is a link error somebody fixes by writing a new empty
// body -- which is the silent no-op plan_entity.md 7 forbids. This one says
// what it is.
//-----------------------------------------------------------------------------
int MapEntity_TemplateCapture( const char *pszSourceName, bool bRemoveSources,
                               int *pOutIndices, float *pOutOriginsXYZ,
                               float *pOutAnglesPYR, int nMax )
{
	return 0;
}

//-----------------------------------------------------------------------------

bool MapEntity_ClaimPendingNonBaseEntity( IGameEntity *pEntity )
{
	// The FIRST match only, as the loop this replaced removed: nothing stops the
	// same entity being queued twice, so erase-remove would not be the same.
	std::vector< IGameEntity * >::iterator it =
		std::find( g_PendingNonBaseSpawns.begin(), g_PendingNonBaseSpawns.end(), pEntity );

	if ( it == g_PendingNonBaseSpawns.end() )
		return false;

	g_PendingNonBaseSpawns.erase( it );
	return true;
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


