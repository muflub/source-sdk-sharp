//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — IShimLevel, and the native level steps behind it ===//
// SS_REASON a  a vtable the engine calls through
//
// plan_shim.md §5 3a. ONE FILE, where there were two: shell_levelsteps.cpp held
// the free functions and shell_levelshim.cpp held a class whose methods each
// called one of them by name. The delegation layer bought a signature check the
// compiler was already giving us on the vtable itself, and cost twenty-three
// forward declarations, twenty-three one-line bodies and a second file header.
// The bodies live in the methods now.
//
// LEVELINIT, SERVERACTIVATE AND LEVELSHUTDOWN, REDUCED TO THEIR IRREDUCIBLE
// NATIVE STEPS -- plan_gamedll.md Part 4, which is plan_server.md Part 4's rule
// unchanged:
//
//     Native exposes one irreducible step per slot. C# owns the order, the
//     conditions and the early returns. A STEP IS NEVER A DECISION.
//
// So there is no SourceSharp_LevelInit() here. Each method is one thing stock's
// CServerGameDLL::LevelInit (gameinterface.cpp:1060-1195), ::ServerActivate
// (:1220-1275) or ::LevelShutdown (:1511-1559) does, and the ORDER of them
// lives in ServerGameDll.cs where a reader can see it, change it and test it.
// Crossing the boundary once per step rather than once per level is deliberate
// and is not a cost this phase is allowed to optimise -- Part 13 is the one
// pass permitted to reduce crossings, and it runs after the whole thing works.
//
//=============================================================================
// TWO RETIRED SLOTS, KEPT AT THEIR INDICES -- AND THE PLAN NAMED FOUR.
//
// plan_shim.md \u00a75 3a lists four "empty or refusal" level slots. Re-measured,
// two of them retire and two do not, and the two that do not fail for DIFFERENT
// reasons. Written out here because "the plan said four and the tree has two"
// is exactly the gap a later reader would otherwise have to re-derive.
//
// RETIRED -- ResetDeleteList and ActivateAllEntities. Each one's subject left
// the vpc: there is no SDK delete list for the first to count (it returned a
// literal 0) and every line of stock's activate walk reaches the evicted
// hierarchy. Both are marked in ishim_level.h and have rows in
// docs/abi-retirements.md, and both managed callers go in the same commit.
//
// NOT RETIRED, BODY EMPTIED -- PrecachePointTemplates. Its body did nothing and
// is gone, but a RETIRED SLOT IS NOT CALLED, and this call's POSITION is what
// two orderings are observed through: low violence before the precache
// (gameinterface.cpp:1049 then :1053) and the templates after the entities they
// read. The first was inverted in this port from the day the method was written
// and tools/gate5 found it -- no census, no live gate and no managed test could.
// Retiring the slot deletes the only marker either ordering has. One no-op
// crossing per LEVEL is the right price for that; see the method below.
//
// NOT RETIRED, UNTOUCHED -- SourceSharp_EntitiesCleanupDeleteList, whose body
// below is empty for the same reason as the others. It is NOT an IShimLevel
// method: it is a flat ABI slot, so its marker belongs in host/sourcesharp.h
// and Abi.cs, which is the SERIALISED tail this lane does not write into. Its
// body and its reasoning are left exactly where they are.
//
// THE SLOTS THEMSELVES STAY in every case: a vtable slot removed from the
// middle renumbers every slot after it, managed binds by index, and the span
// check does not catch a reorder. The retired pair goes in one deliberate pass,
// at a SourceSharpLevel002 version string.
//
//=============================================================================
// ONE FILE-SCOPE OBJECT, and the argument is shell_frame.cpp's in full:
// CShimLevel has NO FIELDS, the object exists only so there is an address whose
// first word is a vtable pointer, and that is the shape the engine's own
// EXPOSE_SINGLE_INTERFACE_GLOBALVAR has. It is baselined deliberately in
// docs/statics-audit/, where the class of entry is described once rather than
// re-argued per wave. What is NOT sanctioned is one of these growing a member.
//
// NO ENTRY COUNT: managed calls IShimLevel through SSFwd_ShimLevel
// (plan_forwarders_everywhere.md), whose table carries its own.
//
// WHAT IS NOT HERE, AND WHY
//
//   UpdateChapterRestrictions / UpdateRichPresence -- the chapter and X360 arms
//   Part 4 drops explicitly.
//
//   The loadGame arm in its entirety -- BeginRestoreEntities, LoadGameState,
//   LoadAdjacentEnts, ClearSaveDirAfterClientLoad and the logic_autosave queue.
//   That is the SAVE CLUSTER, refused by Part 7. The managed MapLoadTransition
//   and MapLoadSaveGame arms log and return true; a native step for a refused
//   arm would be a body with no caller pretending the refusal is temporary.
//
//   LevelInit_ParseAllEntities -- the mod-specific parse. This build's is
//   shell_server.cpp:200, a SOURCESHARP_STUB(). A step whose whole body is a
//   stub is a crossing that buys nothing; when it grows one it becomes the
//   tenth step here.
//
//   g_pServerBenchmark->StartBenchmark -- already #ifndef SOURCESHARP in stock,
//   gone with serverbenchmark_base.cpp.
//
//   The tail: g_AIFriendliesTalkSemaphore / g_AIFoesTalkSemaphore Release and
//   g_OneWayTransition = false. The two semaphores belong to the AI that left
//   this build (shell_ai_absent.cpp); g_OneWayTransition is read only by the
//   save cluster's transition arm, which is refused.
//
// NO FILE-SCOPE STATE. The only mutable thing named below is g_flServerCurTime,
// which is stock's, defined at gameinterface.cpp:1057 outside the class.
//
//=============================================================================

// P5 lane R: on the R5 include set. cbase.h, gameinterface.h, mapentities.h,
// particle_parse.h, entitylist.h, networkstringtable_gamedll.h and soundent.h
// are gone: the map parse comes from ss_mapentities.h, the sound-hint pool's
// shutdown from ss_soundent.h, and the engine's globals are declared below.
#include "eiface.h"
#include "networkstringtabledefs.h"
#include "filesystem.h"
#include "datacache/imdlcache.h"
#include "ss_mapentities.h"
#include "ss_soundent.h"
#include "entity_shared_statics.h"
#include "shell_server.h"
#include "ishim_level.h"
#include "shell_standalone.h"
#include "shell_resolve.h"
#include "ss_world.h"


// The parallel hierarchy's teardown, declared where it is used rather than
// given a header whose only reader would be this file. Defined in
// shell_standalone_base.cpp.
void StandaloneEntities_LevelShutdown();

extern INetworkStringTable *g_pStringTableInfoPanel;

// The engine's globals and interfaces (connected in shell_engineboundary.cpp).
extern CGlobalVars *gpGlobals;
extern IVEngineServer *engine;
extern IFileSystem *filesystem;
extern IMDLCache *mdlcache;

// gameinterface.cpp:1057, outside the class. Read by CBaseProjectile's
// CollideWithTeammates think (baseprojectile.cpp:231) -- a clock that is not
// affected by rollback, which is why it is not just gpGlobals->curtime at the
// point of use.
extern float g_flServerCurTime;

// The two steps whose bodies are in shell_propdata.cpp rather than here, and
// which therefore still need a declaration. Every other step is a method of the
// class below, defined where it is declared -- which is what the merge bought.




//=============================================================================
// THE RESIDUE FAMILY -- six of this file's calls are ONE thing.
//
// They are the path that makes, activates, holds and frees the entities the
// MANAGED map spawner did not claim: MapEntity_ParseAllEntities,
// MapEntity_PrecacheEntity, CGlobalEntityList::CleanupDeleteList / ResetDeleteList
// / NextEnt / Clear. MapSpawner.SpawnAll (MapSpawner.cs:225-261) splits the
// map's lump by whether ScriptedEntityRegistry owns the classname and hands the
// REST to the first of them.
//
// ARMED, THAT RESIDUE IS EMPTY, and it is MEASURED rather than asserted. The
// count is now ZERO: there is no LINK_ENTITY_TO_CLASS left in any compiled
// server TU at all.
//
// THIS BLOCK USED TO NAME ONE SURVIVOR and it was wrong on both halves. It read
// that `shell_abi_sdk_fixture.cpp:74` still carried
// `LINK_ENTITY_TO_CLASS( sourcesharp_abi_sdk_fixture, ... )` and that
// `shell_abi_sdk_fixture.o` SURVIVES the removal set. Lane `5e-testonly`
// measured both in September: the file was named by no .vpc and had an object
// on neither ninja graph -- its row went at fd5f3bc8d -- and its class and
// factory line had already been stripped by f288a5e11, leaving 65 lines that
// were comment and two #includes. The file is now deleted. The ARITHMETIC IS
// UNCHANGED and so is the conclusion: one survivor that appears in no map, or
// none at all, both leave the residue empty.
//
// The instrument still applies to the rest: a source scan must have comments
// STRIPPED. Run without stripping, the same grep returns nine files and eight
// of the nine hits are prose -- which is how the survivor above came to be
// believed in the first place, since the line it cited was itself a comment.
//
// SO THESE ARE NOT SILENT NO-OPS, AND §7'S DECLARATION IS ALREADY MADE -- by a
// mechanism that reads the real table rather than by a sentence written here.
// EntityFactories.Refresh() pulls the native dictionary after every map load,
// and EntityFactories.DeclareUnanswered() prints every classname the map named
// that nothing answers to (MapSpawner.cs:290-297). With the dictionary down to
// the fixture, that declaration covers the whole residue automatically. It is
// already what prints func_useableladder, info_ladder_dismount and info_node on
// dm_lockdown today.
//
// WHAT IT COSTS, PLAINLY: a map classname served by a NATIVE factory and not by
// ScriptedEntityRegistry stops existing. Today that set is the removal set's ten
// registrations -- env_entity_dissolver, env_ragdoll_boogie, physics_npc_solver,
// physics_entity_solver, physics_prop_ragdoll, prop_ragdoll, info_null,
// info_player_deathmatch, info_player_start, info_landmark -- and each is the
// responsibility of the commit that removes ITS vpc row, not of a guard here.
// These guards only stop the shim asking a dictionary that is gone.
//
// AND THE SLOT TABLE CANNOT REPLACE THE WALKS. §3.3: "It cannot iterate, has no
// listeners, and knows no names." Anything that walks the list answers only for
// the SDK hierarchy and answers nothing once that is gone -- so the two walks
// here are GUARDED rather than re-pointed, and their managed counterparts
// (_spawner.ActivateAll, EntityRemovalRoutes.FreeNow) already exist and already
// run beside them.
//=============================================================================


//=============================================================================
// STEP 5b -- THE MANAGED MAP SPAWNER'S ONE CREATION STEP.
//
// plan_entity.md step 2d, and it is the whole of what native does about an
// entity the managed map spawner owns: make the shell, and store the keys the
// SHELL keeps.
//
// WHY THIS EXISTS AT ALL, since SourceSharp_CreateEntity already creates by
// name. Because the keys are the point. Before step 2d the map's text lived
// only in the native parse, so every key had to be handed UP one at a time --
// CStandaloneEntity::KeyValue's unconditional OnManagedKeyValue, 5709 crossings
// on dm_lockdown, 72% of the whole map-load entity budget. The managed spawner
// has the text in managed memory and gives every key to
// ScriptedEntityRegistry itself, so what is left is the handful the SHELL keeps
// -- origin, angles, targetname, parentname, parentattachment, spawnflags,
// health, model -- and those come down here TOGETHER, once.
//
// NOTHING IS DECIDED HERE. Which keys the shell keeps is
// CStandaloneEntity::KeyValueShellOnly's if-chain, unchanged and unduplicated;
// this walks pairs and hands each to it. Managed code does not know that list
// and does not filter against it: EVERY key of the entity is sent, so
// ScriptedEntityRegistry.ReportUnclaimed still sees every key a mapper typed
// and the near-miss report is untouched. (That is the difference from the route
// step 2a refuted: a NATIVE filter would drop exactly the typos that report
// exists to catch. There is no filter, on either side.)
//
// THE PAYLOAD is key\0value\0key\0value\0..., nBytes long. Map text cannot
// contain a NUL -- the lump is a C string in the BSP -- so the separator cannot
// collide with a value, and the byte count means a trailing NUL is not needed.
//=============================================================================
// Defined in shell_entityapi.cpp. Declared here rather than given a header whose
// only reader would be this file -- the shape shell_frame.cpp uses, and a
// drifted signature fails to link.
uint32_t SourceSharp_CreateEntity( const char *pClassName );

uint32_t SourceSharp_MapSpawnCreate( const char *pClassName, const char *pPackedKeys,
                                     int32_t nBytes )
{
	// THROUGH THE TWO FUNCTIONS THAT ALREADY EXIST, AND NOT A THIRD SPELLING.
	//
	// SourceSharp_CreateEntity is CreateEntityByNameEither plus the "is it on
	// this hierarchy" classification and the warning for a name that is neither;
	// SourceSharp_ResolveEntity is THE resolve (shell_resolve.h: "there is ONE
	// resolve doing ONE entity-list question"). Repeating either here would be a
	// second copy of a decision, and it would ADD A CAST -- the entity census
	// counts every static_cast to an entity type in the shim, and the one from
	// IGameEntity down to CStandaloneEntity is spelled in exactly two places
	// this file is not.
	const uint32_t hEntity = SourceSharp_CreateEntity( pClassName );

	if ( hEntity == INVALID_EHANDLE_INDEX )
		return INVALID_EHANDLE_INDEX;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone == NULL )
	{
		// A CBaseEntity, or a handle that resolved to nothing. The managed
		// spawner only sends a classname the managed registry owns, so this is a
		// registration disagreement rather than a map problem -- and it is said
		// rather than silently producing an entity with none of its keys.
		Warning( "[SourceSharp] map spawn: '%s' is not on the parallel hierarchy; "
		         "its map keys were not applied\n", pClassName );
		return hEntity;
	}

	if ( pPackedKeys && nBytes > 0 )
	{
		const char *pAt  = pPackedKeys;
		const char *pEnd = pPackedKeys + nBytes;

		while ( pAt < pEnd )
		{
			const char *pKey = pAt;
			pAt += strlen( pAt ) + 1;

			// A key with no value after it. A truncated payload rather than a
			// map problem, and it ends the walk rather than reading past pEnd.
			if ( pAt >= pEnd )
				break;

			const char *pValue = pAt;
			pAt += strlen( pAt ) + 1;

			ent.pStandalone->KeyValueShellOnly( pKey, pValue );
		}
	}

	return hEntity;
}

//-----------------------------------------------------------------------------
// STEP 6's FREE FUNCTION IS GONE WITH THE BODY IT HELD -- gameinterface.cpp:1178.
//
// The slot itself is retired rather than removed (see the method below and the
// note in the file header); what is deleted here is the definition, which had
// no caller left once the method stopped delegating to it.
//
// WHAT IT DID, recorded because the reasoning outlives the code. Stock ran this
// AFTER the parse and said why in its own comment: point_template entities have
// to exist before their parameters can be read. game/server/TemplateEntities.cpp
// has left this build, so the deferred precache list is SourceSharp.Server's
// TemplateStore, and the templates arrived here as TEXT -- nCount NUL-terminated
// blocks laid end to end with the buffer size of each beside it. The loop over
// them advanced a pointer and did nothing else, because MapEntity_PrecacheEntity
// built the block's entity through the same dictionary in order to call
// Precache() on it, and a managed template entity is precached by the managed
// spawner.
//
// The instance number those blocks were stamped with was burned by the managed
// drain, for stock's reason: the entities that pass built were REAL for as long
// as it took to precache them, and sharing an instance number with the first
// thing actually spawned would give two live entities the same fixed-up
// targetname. That is still true of the managed drain and is its business now.
//-----------------------------------------------------------------------------


//-----------------------------------------------------------------------------
// STEP 8 -- gameinterface.cpp:1182, CServerGameDLL::LoadMessageOfTheDay:
// RETIRED (2026-09-29). It read the file named by `motdfile` / `motdfile_text`
// into g_pStringTableInfoPanel, and both convars were declared by stock's
// gameinterface.cpp, which this build does not compile, and are not the
// engine's. So in this build the step never loaded anything: every level load
// found no convar, logged "ConVarRef motdfile doesn't point to an existing
// ConVar" twice, and returned. The body is gone with the CUtlBuffer it read
// into (tier1's utlbuffer.cpp left server.so's link with it). The slot stays
// and is still called, empty, because its position in ServerGameDll.LevelInit
// is an ordering marker (see the call there). A message of the day, if the mod
// wants one, is a C# convar pair and a string-table write with user data.
//-----------------------------------------------------------------------------


//=============================================================================
// PHASE 7 -- SERVERACTIVATE AND LEVELSHUTDOWN, REDUCED THE SAME WAY.
//
// CServerGameDLL::ServerActivate (gameinterface.cpp:1220-1275) and
// ::LevelShutdown (:1511-1559), as steps. The ORDER of each lives in
// ServerGameDll.cs, and LevelShutdown's order is the ONLY thing in it -- stock's
// body is nine calls whose sequence is the whole content.
//
// WHAT IS NOT HERE, with the reason each was measured rather than assumed:
//
//   `if ( g_InRestore ) return;` -- ServerActivate's first line, and GameFrame's.
//   Nothing in a multiplayer build sets g_InRestore: it is the save/restore
//   cluster's flag, and this build's engine answers "Can't save multiplayer
//   games" to everything that could set it. plan_gamedll.md Part 11c #9 and
//   docs/gamedll-ledger.md's IsRestoring row. Dead code that reads as live.
//
//   The nav mesh -- TheNavMesh->Load() / OnServerActivate() in ServerActivate
//   and ->Reset() in LevelShutdown. Both are inside `#ifdef USE_NAV_MESH`, and
//   USE_NAV_MESH is defined NOWHERE in this build. Grepped, not assumed.
//
//   TheBots->ServerActivate() (CSTRIKE_DLL), TheNextBots().OnMapLoaded()
//   (NEXT_BOT), g_pServerBenchmark->EndBenchmark and InvalidateQueryCache --
//   all already guarded out of this build.
//
//   g_nCurrentChapterIndex = -1 -- the chapter arm Part 4 drops explicitly.
//
//   think_limit -- stock clamps it to 0 unless `developer` is set. That is a
//   CONDITION, so it is not a step: ServerGameDll.cs asks IEngineConsole for
//   `developer` and writes `think_limit` itself. A step that made the decision
//   would be exactly what Part 4's rule forbids.
//=============================================================================


//-----------------------------------------------------------------------------
// gameinterface.cpp:1233-1250. THE ACTIVATE WALK, and the whole reason
// dm_lockdown reads 590 entities on the shell path and 473 on stock's: five
// classes remove themselves during Activate() and four more are created by it.
//
// PART 4 SAYS THIS IS "already #ifndef SS_CBASEENTITY_ALIAS-guarded in stock,
// so check what this build actually still runs". CHECKED: SS_CBASEENTITY_ALIAS
// is defined NOWHERE -- not in a .vpc, not on a command line, not in a header
// this build reaches. Every line of the walk runs, and this is all of it.
//
// The chained-activate check is stock's and is DEBUG-only there too. The two
// flags were defined in gameinterface.cpp outside any class, so they survived
// Phase 7a; they are in sourcesharp/game/server/shell_engineboundary.cpp since
// plan_gamedll_after.md Phase A9 step 4 deleted that file.
// CBaseEntity::Activate (baseentity.cpp:1493) is what sets the second one.
//-----------------------------------------------------------------------------
#ifdef DEBUG
extern bool g_bReceivedChainedActivate;
extern bool g_bCheckForChainedActivate;
#endif














namespace
{

class CShimLevel : public IShimLevel
{
public:
	// NO FIELDS, AND NONE IS COMING.

	//-----------------------------------------------------------------------------
	// STEP 1 -- gameinterface.cpp:1064.
	//-----------------------------------------------------------------------------
	virtual void ResetServerTime() OVERRIDE
	{
		g_flServerCurTime = gpGlobals->curtime;
	}

	//-----------------------------------------------------------------------------
	// STEP 2 -- gameinterface.cpp:1096. The game's particle manifest.
	//
	// TWO STEPS AND NOT ONE, which is a deliberate divergence from Part 4's sketch
	// (it writes a single `Level.ParseParticleEffects(load.MapName)`). These are
	// two different files with two different lifetimes -- the game-wide manifest
	// and the map's own, which most maps do not have -- and they are two calls in
	// stock. Collapsing them into one native step would put the sequence in C++,
	// which is the one thing the rule above forbids.
	//
	// This closes half of the shell's TODO at shell_gamedll.cpp:364.
	//-----------------------------------------------------------------------------
	virtual void ParseParticleManifest() OVERRIDE
	{
		// RETIRED, not removed: IShimLevel is append-only. The manifests are
		// read in C# since plan_server_novalve.md P2.1 (ParticleManifest.cs)
		// and nothing calls this slot; server.so no longer links particles.a.
	}

	//-----------------------------------------------------------------------------
	// STEP 3 -- gameinterface.cpp:1099. Tony's per-map manifest, if the map has one.
	// The other half of the shell's TODO.
	//-----------------------------------------------------------------------------
	virtual void ParseParticleManifestForMap( const char *pMapName ) OVERRIDE
	{
		// RETIRED with ParseParticleManifest above, for the same reason.
		(void)pMapName;
	}

	//-----------------------------------------------------------------------------
	// STEP 5 -- gameinterface.cpp:1163-1164, RETIRED (plan_server_novalve.md P5).
	//
	// This took the map's entity residue as TEXT and parsed it here for the one
	// block native still builds, worldspawn. Parsing is a decision about the
	// text, so it moved to C#: MapSpawner picks the worldspawn block out of the
	// lump it has already split (MapEntityBlock) and calls SpawnWorld, at the
	// interface's tail, with the pairs. The slot keeps its index and REFUSES
	// out loud: a caller reaching it builds no world, and the level that
	// follows faults inside the engine on its first trace, so the console line
	// is the cheap place to learn why.
	//-----------------------------------------------------------------------------
	virtual void ParseAllEntities( const char *pMapEntities ) OVERRIDE
	{
		(void)pMapEntities;
		Warning( "[SourceSharp] IShimLevel::ParseAllEntities is retired: native no "
		         "longer parses map text. The world arrives parsed through "
		         "IShimLevel::SpawnWorld. No world was built.\n" );
	}

	//-------------------------------------------------------------------------
	// STEP 6 -- gameinterface.cpp:1178. AN EMPTY BODY THAT IS STILL CALLED, and
	// the difference between that and a retired slot is the whole of this note.
	//
	// The body WAS a loop over the map's template blobs that advanced a pointer
	// and did nothing else: MapEntity_PrecacheEntity built each block's entity
	// through the same dictionary in order to call Precache() on it, and a
	// managed template entity is precached by the managed spawner instead. So
	// there is no work, and the loop is gone.
	//
	// THE CALL STAYS BECAUSE ITS POSITION IS LOAD-BEARING. Two orderings are
	// observed through it and one of them was already wrong once: stock sets
	// low violence (:1049) BEFORE precaching templates (:1053), and this port
	// ran that pair the other way round from the day the method was written,
	// under a doc comment saying "the sequence is stock's" and a [Fact] that
	// pinned the inversion. tools/gate5 found it on its first real run -- no
	// census, no live gate and no managed test could see it, because the test
	// that would have was written from the same wrong belief as the code. The
	// other ordering is that templates are precached AFTER the entities they
	// read.
	//
	// A retired slot is not called, so retiring this one deletes the only
	// marker either ordering can be observed through. That is worth one no-op
	// crossing per LEVEL.
	//-------------------------------------------------------------------------
	virtual void PrecachePointTemplates( const char *pMapDataBlobs,
	                                     const int32_t *pStringSizes,
	                                     int32_t nCount ) OVERRIDE
	{
		(void)pMapDataBlobs; (void)pStringSizes; (void)nCount;
	}

	//-----------------------------------------------------------------------------
	// STEP 7 -- gameinterface.cpp:1175. THE STEP STAYS AND ITS BODY IS EMPTY.
	//
	// It used to be `g_RagdollLVManager.SetLowViolence( pMapName )`, and that
	// manager went with plan_shim.md 4a item 3 (lane 3a): its only reader was the
	// ragdoll LRU's ShouldRemoveThisRagdoll, whose list had no writer on either
	// link line. There is nothing left on this side for the answer to be stored in.
	//
	// NOTHING IS LOST, and this is not a refusal. The same decision is made on the
	// SAME map load, one step earlier, in managed code that was already running:
	// Corpses.ReadViolencePolicy calls RagdollViolencePolicy.LowViolenceForMap,
	// which is SetLowViolence rule for rule, and Corpses is what holds and trims
	// the bodies the flag is about.
	//
	// RESTORED AT THE MERGE. Lane 3a emptied this body in shell_levelsteps.cpp;
	// lane 3b folded that file into this one from a base that predated the
	// emptying, carrying the dead call back in. The build caught it -- an
	// undeclared identifier rather than anything subtle -- but the ORDER of the
	// two merges is what decided whether it was caught at all.
	//-----------------------------------------------------------------------------
	virtual void SetLowViolence( const char *pMapName ) OVERRIDE
	{
		(void)pMapName;
	}

	// RETIRED: see STEP 8 above. Empty, and still called for its position.
	virtual void LoadMessageOfTheDay() OVERRIDE
	{
	}

	//-----------------------------------------------------------------------------
	// gameinterface.cpp:1228. Stock tests the answer and prints; the TEST is the
	// decision, so this returns the count and C# decides what to say about it.
	//-----------------------------------------------------------------------------
	//
	// THE PARALLEL HIERARCHY'S QUEUE IS EMPTY BY THIS POINT and is not counted here
	// -- plan_entity.md step 2c. The last step of the map load above drains it
	// (EntityRemovalRoutes.FreeNow, paired with EntitiesCleanupDeleteList), so
	// there is nothing left for this to discard and no second count for C# to have
	// to add to stock's. If that ever stops being true the symptom is a managed
	// entity that spawns already marked, which EntityDirectory.PendingRemovalCount
	// reads directly.
	//-------------------------------------------------------------------------
	// RETIRED SLOT -- "how many SDK entities were still queued for deletion
	// when the level started".
	//
	// ZERO IS THE TRUE ANSWER ARMED, not a stub's, and that is exactly why the
	// slot is retired rather than kept: armed there is no SDK queue to hold
	// one, so the number cannot be anything else and asking costs a crossing
	// per level to be told a constant. ServerGameDll.ServerActivate tested it
	// and warned on a non-zero; the parallel hierarchy's own count is
	// EntityDirectory.PendingRemovalCount, which is managed and free to read.
	//-------------------------------------------------------------------------
	virtual int32_t ResetDeleteList() OVERRIDE
	{
		return 0;
	}

	//-------------------------------------------------------------------------
	// RETIRED SLOT -- stock's ServerActivate walk (gameinterface.cpp:1233-1250).
	//
	// THE SDK HALF OF THE WALK, and there is no SDK half: every line of stock's
	// walk reaches the hierarchy server_csharp.vpc evicts, so the body has been
	// empty since the eviction. The parallel hierarchy's half is
	// _spawner.ActivateAll() in ServerGameDll.ServerActivate, immediately after
	// where this call was, in managed memory with no crossing -- so armed, that
	// is the whole of ServerActivate's activation pass and the ORDER is
	// unchanged: the two hierarchies were already activated in the order their
	// entities were made.
	//-------------------------------------------------------------------------
	virtual void ActivateAllEntities() OVERRIDE
	{
	}

	//-----------------------------------------------------------------------------
	// gameinterface.cpp:1252 and :1253. "No more precaching after
	// PostEntityAllSystems!!!" is stock's own shout, and it is why these are two
	// steps in an order rather than one call: the close has to follow the pass.
	//
	// The named list's LevelInitPostEntity pass is where the MANAGED level init
	// happens too -- SourceSharpManaged is a row in it, at stock's position.
	//-----------------------------------------------------------------------------
	virtual void InitPostEntitySystems() OVERRIDE
	{
		SourceSharp_ServerSystemsLevelInitPostEntity();
	}

	virtual void SetPrecacheAllowed( int32_t bAllowed ) OVERRIDE
	{
		EntityPrecache_SetAllowed( bAllowed != 0 );
	}

	//-----------------------------------------------------------------------------
	// LEVELSHUTDOWN'S STEPS, gameinterface.cpp:1511-1559, in stock's order --
	// which C# owns. Every one of the six below is one line of that body.
	//-----------------------------------------------------------------------------
	virtual void ShutdownPreClearSteamSystems() OVERRIDE
	{
		SourceSharp_ServerSystemsLevelShutdownPreClearSteamAPIContext();
	}

	//-----------------------------------------------------------------------------
	// steamgameserverapicontext->Clear(). A SEPARATE step from the pass above it
	// because that is the whole reason the pass has "PreClearSteamAPIContext" in
	// its name: a system that reads the steam context has to be told before it goes.
	//-----------------------------------------------------------------------------
	// A2: this forwards now rather than holding its own ->Clear(). The other
	// caller is IServerGameDLL::GameServerSteamAPIShutdown, which arrives on a path
	// that is not a level shutdown at all; two spellings of one operation is the
	// disagreement TODO #5 refused, so there is ONE body and it lives in
	// shell_steamcontext.cpp beside its partner. Pinned by
	// SteamContextSources.TheContextIsClearedInExactlyOnePlace.
	virtual void ClearSteamAPIContext() OVERRIDE
	{
		// RETIRED, not removed: IShimLevel is append-only. The Steam game
		// server context is C#'s own (plan_server_novalve.md P2.5), and
		// ServerGameDll.MapUnload clears it at this point in the sequence.
	}

	virtual void ShutdownPreEntitySystems() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		SourceSharp_ServerSystemsLevelShutdownPreEntity();
	}

	//-----------------------------------------------------------------------------
	// THE PARALLEL HIERARCHY'S TEARDOWN -- plan_gamedll_after.md Phase A3, the
	// fifth "C" system, and it was CSourceSharpStandaloneSystem's
	// LevelShutdownPreEntity.
	//
	// gEntList cleans up the CBaseEntity world between maps and CANNOT SEE THESE:
	// the entity list is IHandleEntity-typed and its iteration walks CBaseEntity,
	// which correctly skips every entity on this hierarchy. So without this step an
	// entity from the previous map survives into the next one -- holding an edict,
	// answering to its name, and thinking.
	//
	// PRE-ENTITY, and the position is why the row it replaced existed at all: the
	// step above runs the pre-entity pass, THIS runs while everything the hierarchy
	// holds is still real, and SourceSharp_LevelClearEntityList below empties the
	// world. Reached from the StandaloneHierarchy participant at
	// LifecycleStage.Standalone, dispatched from INSIDE the backward walk by the
	// SourceSharpManaged row -- not from ServerGameDll.MapUnload, which is outside
	// the walk.
	//-----------------------------------------------------------------------------
	virtual void ShutdownStandaloneEntities() OVERRIDE
	{
		StandaloneEntities_LevelShutdown();
	}

	//-----------------------------------------------------------------------------
	// gameinterface.cpp:1527, and stock's comment is the reason it is not folded
	// into the entity list clear below it: "YWB: This entity pointer is going away
	// now and is corrupting memory on level transitions/restarts".
	//-----------------------------------------------------------------------------
	virtual void ShutdownSoundEnt() OVERRIDE
	{
		ss::SoundEnt_Shutdown();	// what CSoundEnt::ShutdownSoundEnt forwards to
	}

	//-----------------------------------------------------------------------------
	// THE WORLD IS RELEASED HERE NOW, AND IT IS THE FIRST THING THIS STEP DOES --
	// plan_entity.md §3.3, lane entlist-server.
	//
	// The world used to be released BY the walk below, and world_release_test.cpp
	// is the file that exists because getting this wrong shipped: the world is on
	// the non-SDK arm of Clear()'s fork, so Clear() reached it as
	// GetServerNetworkable( handle ) and called Release(), which is
	// `delete m_pOuter`. Everything downstream of ~CWorld hangs off that one call
	// -- the singleton is cleared, edict 0 gives its pointers back, and the NEXT
	// map's CWorldFactory::Create is allowed to build a world at all. When Release
	// was a no-op the second `map` command faulted inside the engine on its first
	// trace, and no gate saw it for weeks because every live gate loaded one map.
	//
	// It is not released by the walk any more because it is not in the list any
	// more: §3.3 moved the world's registration onto its own handle
	// (world_adapter.cpp, CWorld::AttachToEdictZero), so nothing is left to
	// enumerate it. The release is therefore relocated rather than replaced, and to
	// the same POSITION it had: the world sat at the head of the active list --
	// registered first, added to the tail of an empty list -- so it was the first
	// entity Clear() reached, and the SDK entities behind it go on the delete list
	// and are freed by the CleanupDeleteList at the end of Clear(). World first,
	// then the rest, exactly as before.
	//
	// TWO CALLS IN ONE STEP, deliberately, and this is the exception rather than a
	// widening of the rule. A step is one call because a sequence written in C++ is
	// a sequence C# cannot see; what is written here is not a new sequence but the
	// FIRST ITERATION of the loop below, taken out of it because its subject left
	// the collection. Splitting it into its own step would need a new IShimLevel
	// slot, which is an ABI change this lane does not own -- recorded in
	// .claude/entity-port/requests/entlist-server.md as the follow-on.
	//
	// THROUGH GetNetworkable(), not through the CWorld. That is the object the
	// entity purge held (world.h:242) and CWorldNetworkable::Release is
	// `delete m_pOuter` -- releasing the outer object directly would be a second
	// spelling of the same destruction, and two spellings is how the first one
	// became a no-op without anything noticing.
	//-----------------------------------------------------------------------------
	virtual void ClearEntityList() OVERRIDE
	{
		// THE WORLD RELEASE IS NOT GUARDED AND MUST NOT BE. It is not gEntList's --
		// §3.3 moved the world onto its own handle, which is why it is a line here
		// rather than the first iteration of Clear(). world_release_test.cpp exists
		// because getting this wrong shipped: with Release a no-op, the SECOND `map`
		// command faulted inside the engine on its first trace, and no gate saw it
		// for weeks because every live gate loaded one map.
		if ( CWorld *pWorld = GetWorldEntity() )
			pWorld->GetNetworkable()->Release();

		// The SDK list, which armed is empty and gone -- entitylist.cpp is a removal
		// set row. What Clear() would still have had to reach is the world, and the
		// line above already did that.
	}

	virtual void ShutdownPostEntitySystems() OVERRIDE
	{
		SourceSharp_ServerSystemsLevelShutdownPostEntity();
	}

	//-----------------------------------------------------------------------------
	// gameinterface.cpp:1545-1546, and stock's comment says why the first exists:
	// "Uncache all the particle systems on level shutdown otherwise we leak them
	// constantly on changelevel in the particle precache stringtable list."
	//
	// TWO STEPS, for the reason ParseParticleEffects and ParseParticleEffectsMap
	// are two: they are two calls doing two things, and one step would put the
	// sequence in C++.
	//-----------------------------------------------------------------------------
	virtual void UncacheAllParticleSystems() OVERRIDE
	{
		// RETIRED (plan_server_novalve.md P2.1): the manager these two told to
		// uncache and rebuild is not in server.so any more; ParticleManifest
		// forgets its definitions at the same point of the level shutdown.
	}

	virtual void RecreateParticleDictionary() OVERRIDE
	{
		// RETIRED with UncacheAllParticleSystems above.
	}

	virtual void PropDataLoadFile() OVERRIDE
	{
		// RETIRED, not removed: IShimLevel is append-only. propdata.txt is
		// C#'s on the server (plan_server_novalve.md P2.4): PropData parses it
		// and resolves a model's prop data itself, so no native tree is loaded.
	}

	virtual void PropDataClearFile() OVERRIDE
	{
		// RETIRED with PropDataLoadFile above, for the same reason.
	}

	//-----------------------------------------------------------------------------
	// THE SAVEGAME ARM'S ONE STEP, and it is one line of stock's LevelInit.
	//
	// gameinterface.cpp's loadGame arm is `if ( !engine->LoadGameState( pMapName,
	// 1 ) ) ...`, and this port has to make the same call for the same reason:
	// nothing else in the engine starts the load-game read, and
	// LoadGameState is what calls ReadRestoreHeaders, reads the 'Save Header' and
	// the light styles back, and then calls Restore. Stage 1 measured all three
	// never firing and recorded a guess about why; the answer is that nothing
	// asked.
	//
	// A STEP AND NOT A DECISION -- plan_server.md Part 4. What to do when it
	// answers false is C#'s: see ServerGameDll.MapLoadSaveGame, which spawns the
	// map FIRST so that a failed restore is a map that loads new rather than a
	// server with no world in it.
	//
	// createPlayers IS PASSED THROUGH rather than pinned to 1. Stock passes 1 and
	// so does the only caller here, but the engine's parameter is a real one -- it
	// reaches Restore's own argument -- and a step that dropped it would be a step
	// that decided something.
	//-----------------------------------------------------------------------------
	virtual int32_t LoadSaveGameState( const char *pMapName, int32_t bCreatePlayers ) OVERRIDE
	{
		if ( pMapName == NULL )
			return 0;

		return engine->LoadGameState( pMapName, bCreatePlayers != 0 ) ? 1 : 0;
	}

	//-----------------------------------------------------------------------------
	// plan_server_novalve.md P2.1. Open/Size/Close rather than Size( path ): on
	// this filesystem Size( path ) answers 0 for a missing file, which is the one
	// answer these may not give for one.
	//-----------------------------------------------------------------------------
	virtual int32_t FileSizeOnPath( const char *pPath, const char *pPathId ) OVERRIDE
	{
		if ( pPath == NULL || pPath[ 0 ] == '\0' || pPathId == NULL || filesystem == NULL )
			return -1;

		FileHandle_t hFile = filesystem->Open( pPath, "rb", pPathId );
		if ( hFile == FILESYSTEM_INVALID_HANDLE )
			return -1;

		unsigned int nSize = filesystem->Size( hFile );
		filesystem->Close( hFile );
		return nSize > 0x7fffffffu ? -1 : (int32_t)nSize;
	}

	virtual int32_t ReadFileOnPath( const char *pPath, const char *pPathId,
	                                void *pOut, int32_t nOutSize ) OVERRIDE
	{
		if ( pPath == NULL || pPath[ 0 ] == '\0' || pPathId == NULL || pOut == NULL ||
		     nOutSize < 0 || filesystem == NULL )
			return -1;

		FileHandle_t hFile = filesystem->Open( pPath, "rb", pPathId );
		if ( hFile == FILESYSTEM_INVALID_HANDLE )
			return -1;

		unsigned int nSize = filesystem->Size( hFile );
		if ( nSize > (unsigned int)nOutSize )
		{
			filesystem->Close( hFile );
			return -1;
		}

		int nRead = filesystem->Read( pOut, (int)nSize, hFile );
		filesystem->Close( hFile );
		return nRead;
	}

	//-----------------------------------------------------------------------------
	// plan_server_novalve.md P5. The world from its parsed keys: see ishim_level.h.
	// Carries out what managed decided; MapEntity_SpawnWorld (shell_mapparse.cpp)
	// is the factory, the key walk and the Spawn.
	//-----------------------------------------------------------------------------
	virtual int32_t SpawnWorld( const char *const *ppKeys, const char *const *ppValues,
	                            int32_t nKeys ) OVERRIDE
	{
		if ( nKeys < 0 || ( nKeys > 0 && ( ppKeys == NULL || ppValues == NULL ) ) )
			return 0;

		return MapEntity_SpawnWorld( ppKeys, ppValues, nKeys ) ? 1 : 0;
	}

	// plan_server_native_v2 T4a: the flag SetPrecacheAllowed writes, read back.
	virtual int32_t IsPrecacheAllowed() OVERRIDE
	{
		return EntityPrecache_IsAllowed() ? 1 : 0;
	}
};

} // namespace

// The one object. See the header comment, and shell_frame.cpp for the
// argument in full.
static CShimLevel g_ShimLevel;

//-----------------------------------------------------------------------------
// The internalFactory's row for this area: the pointer alone. Managed calls it
// through SSFwd_ShimLevel (plan_forwarders_everywhere.md phase 3), whose table
// carries its own count.
//-----------------------------------------------------------------------------
void *ShimLevel_Acquire()
{
	return static_cast< IShimLevel * >( &g_ShimLevel );
}
