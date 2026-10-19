//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON a  a vtable the engine calls through
//
// THE NAMED LIST -- plan_gamedll.md Part 3a and Part 9a, Phase 7.
//
// This file replaces shell_gamesystems.cpp, which was labelled a crutch when it
// was written and is deleted with this one. The crutch called
// IGameSystem::InitAllSystems(), which walks an ANONYMOUS list built by
// CAutoGameSystem's constructor -- so the order was link order, plus four
// hand-prepended IGameSystem::Add calls in stock's DLLInit that exist BECAUSE
// link order was wrong for them (gameinterface.cpp:781-801).
//
// kServerSystems[] below is that list, written down. One row per system, in the
// order it runs, in one file. Every stage pass walks these rows; the four
// shutdown stages walk the SAME rows backwards, which is what makes stock's own
// comment -- GameStringSystem "must init first + shutdown last" -- stop being a
// special case and start being a property of the table.
//
//=============================================================================
// THE ORDER IS STOCK'S OBSERVED ORDER, NOT PART 9a'S.
//
// Part 9a writes an init order down and it is NOT what this build does. The
// list here was MEASURED: a temporary census in IGameSystem::InitAllSystems
// printed s_GameSystems in order on a live dm_lockdown load in delegate mode.
// It reported TWENTY-THREE systems where Part 9a says twenty-one, named six
// Part 9a does not mention at all (CPostFrameNavigationHook, CSoundscapeRegistry,
// CCheckClient, CPrecacheOtherList, CTemplatesHook, CRagdollLRURetirement) and
// did not contain one Part 9a does (CWatchForModelAccess, which this build
// never constructs).
//
// Two of Part 9a's positions are also wrong in a way that would change
// behaviour rather than only reading order:
//
//   * it puts SourceSharpSystem SECOND, "because it loads the CLR". In the
//     shell the CLR is up before this table is walked at all --
//     LoadManagedRuntime runs earlier in DLLInit -- so the justification does
//     not apply, and moving the managed frame from stock's position to second
//     would run it BEFORE CRagdollLRURetirement and CEntityListSystem in the
//     post-entity think pass instead of after them. Neither is a no-op.
//   * its tail order (DecalEmitter, PropData, SoundController,
//     SpatialPartitionDirtyList, EntityList, SoundEnt, StandaloneSystem) is not
//     the order stock runs them in either.
//
// Phase 7's brief says it plainly: there is no good order to preserve, only a
// working one to reproduce. This is the working one.
//
//=============================================================================
// WHAT A ROW IS, and the one deliberate divergence from Part 3a's wording.
//
// Part 3a says "name, and a function pointer per stage". A row here is a name
// and ONE function pointer -- an accessor returning the CServerSystem * -- and
// the stage dispatch is the virtual call that object already has.
//
// MEASURED REASON, not a preference. Fourteen of the twenty-three objects are
// file-scope instances in their own translation unit, so either shape needs an
// accessor per system; the per-stage shape needs the accessor AND roughly fifty
// stage thunks on top. Fifty hand-written thunks is fifty chances to omit one,
// and an omitted stage is EXACTLY the failure this phase exists to prevent: it
// does not warn, does not fail to link, and does not run. Dispatching every
// stage on every row through the vtable cannot omit one.
//
// What the table therefore contributes is the ORDER and the MEMBERSHIP, which
// were the two things nobody had decided. The stage matrix is recorded in
// docs/gamedll-ledger.md, where it was measured.
//
//=============================================================================
// NO FILE-SCOPE STATE. kServerSystems[] is `static const` -- a constant, not
// state, which is the same distinction docs/gamedll-ledger.md draws for the
// user-message dispatch table. The one object this file owns,
// CSourceSharpManagedSystem, has NO DATA MEMBERS AT ALL (static_assert below),
// so its instance is constant by construction too.
//
//=============================================================================

#include "edict.h"

// The engine's globals (connected in shell_gamedll.cpp).
extern CGlobalVars *gpGlobals;

// The engine's model cache (connected with the tiers); MDLCACHE_CRITICAL_SECTION
// below locks it by name.
class IMDLCache;
extern IMDLCache *mdlcache;
#include "shell_serversystem.h"
#include "tier0/icommandline.h"
#include "datacache/imdlcache.h"
#include "shell_server.h"

#include "sourcesharp.h"
#include "managed_runtime_server.h"
#include "imanaged_areas.h"   // plan_server_native_v2 T4a: the sound emitter system's Init, run first by the managed row


//-----------------------------------------------------------------------------
// EVERY ROW'S ACCESSOR, DECLARED.
//
// Declared here rather than in a header, the way gameinterface.cpp:209-211
// declares the four it uses: these functions have no header between them and
// this file, and giving them one would be a header whose only reader is this
// table.
//
// The first five already existed and are stock's own -- they are the ones
// stock's DLLInit hand-Adds. The rest were added by Phase 7, one line beside
// each instance, because the instance has internal linkage and this table is
// the only other thing that wants it.
//-----------------------------------------------------------------------------
CServerSystem *GameStringSystem();
CServerSystem *PhysicsGameSystem();
CServerSystem *GameLogSystem();

// "Due to dependencies, these are not autogamesystems" -- stock's comment, at
// the point stock calls it, immediately after InitAllSystems.

//-----------------------------------------------------------------------------
// The managed side's row.
//
// It is NOT CSourceSharpSystem. On the server CSourceSharpSystem no longer
// exists at all -- Phase 7a put the class and its instance behind
// #ifdef CLIENT_DLL (Part 3), so the client keeps its copy and the server's
// managed frame is this row and only this row.
//
// CServerSystemPerFrame is SourceSharp's own base (shell_serversystem.h): it
// has no constructor side effect, so this object appends itself to nothing and
// runs only because the table below names it.
//
// WHAT EACH STAGE FORWARDS TO is the SAME BODY the client's CSourceSharpSystem
// forwards to -- one body per event point, in sourcesharp_gamesystem.cpp. Two
// drivers was the design; two bodies would have been the bug.
//-----------------------------------------------------------------------------
class CSourceSharpManagedSystem : public CServerSystemPerFrame
{
public:
	virtual char const *Name() OVERRIDE { return "SourceSharpManaged"; }

	// The CLR is already up: LoadManagedRuntime ran earlier in the shell's
	// DLLInit, and OnSystemStart after it. There is nothing for an Init to do
	// about the RUNTIME, and calling SourceSharp_LoadManagedSide again would be
	// a second attempt at a sequence that has already succeeded.
	//
	// It bound the physics event ring's sink until plan_physics_bones_native_zero
	// 2.3: the ring's last producer was PhysFrame, which is managed's now, and
	// its events are posted to the router there with no crossing.
	//
	// WHAT IT DOES RUN IS THE SOUND EMITTER SYSTEM'S INIT, FIRST -- the row
	// above this one until plan_server_native_v2 T4a's precache path, which
	// made the system managed (SoundEmitterSystem: the script table's
	// ModInit). So it runs where its row ran, before anything of this row's,
	// and its answer is this row's: a table that will not load stops the
	// server as the native row's false did. Its other stages are managed's
	// own, at the same edges: the level's overrides and preload first in
	// IManagedRuntimeServer::OnMapLoadPreEntity, the level's end last in
	// IManagedFrame::LevelShutdown, and its Shutdown last in the managed
	// side's final unload, which DLLShutdown runs before this table's walk.
	virtual bool Init() OVERRIDE
	{
		IManagedSoundEmitter *pEmitter = ManagedSoundEmitter();
		if ( !pEmitter )
		{
			Warning( "[SourceSharp] SourceSharpManaged: no IManagedSoundEmitter, so the sound "
			         "scripts cannot load\n" );
			return false;
		}

		return pEmitter->ServerSoundSystemInit();
	}

	// The teardown is DLLShutdown's -- OnSystemStop, then UnloadManagedRuntime,
	// in that order and paired with the load. A Shutdown here would be a second
	// unload of one runtime.
	virtual void Shutdown() OVERRIDE {}

	// plan_gamedll_after.md Phase A3 -- THE PRE-ENTITY HALF, and it is new.
	//
	// This row had no LevelInitPreEntity until A3, because nothing managed ran
	// in that half: LifecycleDriver.LevelInitPreEntity existed from Part 3a's
	// stage split and ServerModule only ever called the PostEntity one, so a
	// participant declaring the pre-entity stage was never reached.
	//
	// A3's two systems are what needed it. CDecalEmitterSystem and CPropData
	// both PRECACHE in this half -- a decal index and a gib model -- and the
	// window that allows a precache is opened by the world entity's own
	// precache and closed by the first Spawn().
	//
	// AND THE POSITION IS THE SAME ONE THEIR ROWS HELD, which is the part worth
	// checking rather than assuming. This row sat at index 14; CPropData was 9
	// and CDecalEmitterSystem 11. The rows BETWEEN them --
	// CDataObjectAccessSystem, CDirtySpatialPartitionEntityList,
	// CSourceSharpStandaloneSystem -- declare no LevelInitPreEntity at all
	// (docs/gamedll-ledger.md's measured stage matrix), so NOTHING with a
	// pre-entity stage sat between the two rows that left and the row that
	// picked their work up. The pre-entity pass runs in the same order it did.
	virtual void LevelInitPreEntity() OVERRIDE
	{
		SourceSharp_ManagedLevelInitPreEntity( STRING( gpGlobals->mapname ) );
	}

	virtual void LevelInitPostEntity() OVERRIDE
	{
		// The map name from gpGlobals, not IGameSystem::MapName(): the shell does
		// not call LevelInitPreEntityAllSystems, which is what sets that.
		SourceSharp_ManagedLevelInit( STRING( gpGlobals->mapname ) );
	}

	// plan_gamedll_after.md Phase A3, the FIFTH "C" system -- THE PRE-ENTITY
	// HALF OF SHUTDOWN, and it is the exact mirror of LevelInitPreEntity above,
	// including the reason it did not exist.
	//
	// LifecycleDriver.LevelShutdownPreEntity has been there since Part 3a's stage
	// split and NOTHING HAS EVER CALLED IT: ServerModule calls the PostEntity
	// half and ServerGameDll.MapUnload calls the NATIVE pass. So a managed
	// participant declaring the pre-entity shutdown stage was registered, sorted
	// and never reached -- the third stage this plan has found in that state.
	// SoundController is the one that was already paying for it: its
	// OnLevelShutdownPreEntity is SystemReset, so from Phase A3's second pair
	// until this override, every live sound patch survived a level change.
	//
	// THE POSITION IS THE ROW'S, AND THAT IS WHY IT IS AN OVERRIDE HERE RATHER
	// THAN A CALL IN MapUnload. This pass is a BACKWARD walk of the table, so a
	// dispatch from outside it runs either before every row or after every one.
	// From the row, managed participants run at index 12 of the backward walk --
	// which is where CSourceSharpStandaloneSystem's own teardown ran at index 11,
	// with nothing declaring the stage between the two.
	virtual void LevelShutdownPreEntity() OVERRIDE
	{
		SourceSharp_ManagedLevelShutdownPreEntity();
	}

	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		SourceSharp_ManagedLevelShutdown();
	}

	virtual void FrameUpdatePreEntityThink() OVERRIDE
	{
		SourceSharp_ManagedFramePreThink();
	}

	virtual void FrameUpdatePostEntityThink() OVERRIDE
	{
		SourceSharp_ManagedFrameThink();
	}
};

//-----------------------------------------------------------------------------
// The row above's accessor. The instance is a function-local static and it is a
// CONSTANT rather than state: this class adds NO DATA MEMBER of its own, so the
// object is its base and nothing else. Asserted rather than claimed.
//
// The assert compares against the BASE's size rather than against
// sizeof( void * ), which is what it said until Phase 7a. CServerSystemPerFrame
// carries one immutable char const * -- the name a system answers with -- so
// "the object is a vtable pointer" stopped being the true sentence while
// "this row adds nothing to it" stayed true, and the second one is the one the
// assert was always for. A data member added below still fails it.
//-----------------------------------------------------------------------------
CServerSystem *SourceSharpManagedGameSystem()
{
	static CSourceSharpManagedSystem s_Managed;

	COMPILE_TIME_ASSERT( sizeof( CSourceSharpManagedSystem ) == sizeof( CServerSystemPerFrame ) );

	return &s_Managed;
}



//=============================================================================
// THE TABLE.
//
// Read top to bottom for init, bottom to top for shutdown. A row's position is
// the only thing about it that is a decision, so a reader who disagrees with
// one argues with one line in one file.
//=============================================================================
struct ServerSystemRow_t
{
	const char *m_pszName;
	CServerSystem *( *m_pfnSystem )();
};

static const ServerSystemRow_t kServerSystems[] =
{
	// "The string system must init first + shutdown last" -- stock's comment at
	// gameinterface.cpp:780. It is first here for the same reason, and it is
	// last on the way out because the table is walked backwards, not because
	// anything says so a second time.
	{ "CGameStringPool",                  GameStringSystem },

	// "Physics must occur before the sound envelope manager" -- stock's other
	// load-bearing comment. CPhysicsHook::Init is what assigns `physcollision`,
	// without which the first prop with a collision model dereferences NULL in
	// BuildPropList. Measured in Phase 6, from a core file.
	//
	// THE SOUND ENVELOPE MANAGER IS NOT A ROW ANY MORE (Phase A3, second pair)
	// and the constraint survives it rather than lapsing: the manager is a
	// managed IFrameLifecycle participant driven from
	// ServerGameDll.PreClientUpdate, which runs after the WHOLE table has been
	// walked. So it is no longer an ordering between two rows -- it is the
	// stronger property that every row is initialised before any managed frame
	// participant runs.
	// ServerSystemsTable.PhysicsComesBeforeTheSoundEnvelopeManager is the fact,
	// and it refuses a row named SoundControllerGameSystem coming back as well
	// as physics moving late.
	{ "CPhysicsHook",                     PhysicsGameSystem },

	// `{ "CPostFrameNavigationHook", PostFrameNavigationGameSystem }` STOOD
	// HERE and went with the hook (plan_server_native_min P1): its whole body
	// was a bool toggle nothing read.
	// `{ "CSoundEmitterSystem", SoundEmitterSystem }` STOOD HERE and went with
	// the system (plan_server_native_v2 T4a, the precache path): it is managed
	// (SoundEmitterSystem), and the SourceSharpManaged row below runs its Init
	// first and its other stages at the managed edges that row reaches, so
	// every stage runs where the row's did.
	// `{ "CCheckClient", CheckClientGameSystem }` STOOD HERE and went with the
	// system (plan_server_native_min P1): its two PVS bitsets had no reader and
	// its walk skipped every candidate.
	// `{ "CPrecacheOtherList", PrecacheOtherGameSystem }` STOOD HERE and went
	// with the class in the server gc cull (2026-10-01). It was a dedupe set for
	// UTIL_PrecacheOther, cleared at each level change; its one writer,
	// AddOrMarkPrecached, had no caller left, so the set was always empty.
	// `{ "CDataObjectAccessSystem", DataObjectAccessGameSystem }` STOOD HERE
	// and went with physics_shared.cpp (plan_physics_bones_native_zero 2.6).
	// The server's table held only the two watcher refusals; no kept server
	// object asked EntityDataObject_Get or _Has (nm), so nothing could reach
	// them, and the system had no stage but Init and Shutdown.
	// STOCK'S POSITION FOR THE MANAGED FRAME, and the reason it is not second
	// is at the top of this file. In stock's list CSourceSharpSystem sits
	// immediately after the standalone system, so the managed frame runs after
	// the standalone one and before the ragdoll retirement and the entity list.
	{ "SourceSharpManaged",               SourceSharpManagedGameSystem },

	// `{ "CRagdollLRURetirement", RagdollLRUGameSystem }` STOOD HERE and went
	// with the class -- plan_shim.md 4a item 3, lane 3a. Named rather than
	// silently dropped, for the same reason the rows below it are: a row
	// removed with no explanation cannot be told from one removed by accident.
	//
	// Its list had NO WRITER. `MoveToTopOfLRU` is the only thing that ever put
	// a ragdoll in it and it had zero importers on either link line, so the
	// Update walked an empty list every frame. The customer this row was
	// explicitly kept for -- CRagdollProp, whose LINK_ENTITY_TO_CLASS is
	// unguarded -- is in game/server/physics_prop_ragdoll.cpp, which has no row
	// in either vpc and no object on either link line. It left before the
	// system did. SourceSharp.Server/Corpses.cs is what retires bodies now.

	// ARMED, THIS ROW HAS NO SUBJECT AND ITS PER-LEVEL CLEAR WAS NEVER ITS OWN.
	//
	// Its five hooks served four managers -- the notify, touch, aim and
	// sim-think tables -- and all four are defined ONLY in the stock list's own
	// translation unit, so they leave with it and there is nothing left
	// uninitialised. Its HL2-only move-cache arm never compiled here; that macro
	// is not defined in this build. Its class-list walk has no armed instance.
	//
	// THE PER-LEVEL ENTITY CLEAR IS NOT LOST WITH IT: that belongs to the
	// managed hierarchy, which is a lifecycle participant reached through the
	// SourceSharpManaged row above, and it is untouched here. This row is
	// guarded, not deleted, so the unarmed build keeps the system exactly as it
	// was and the count is taken from the array either way.
	//
	// ONE REAL LOSS, and it announces itself: the respawn_entities cheat command
	// is registered by that translation unit, so armed the engine answers
	// "Unknown command" -- a refusal that prints by construction.
};

static const int kServerSystemCount = ARRAYSIZE( kServerSystems );

//=============================================================================
// THE NINE THAT ARE NOT IN THE TABLE, each with the reason it is not, because
// a row deleted without a reason is indistinguishable from one forgotten.
//
//   CPropData            -- plan_gamedll_after.md Phase A3. NOT DELETED: MOVED.
//                           `PropData` in SourceSharp.Server is an
//                           IGameLifecycle participant at
//                           LifecycleStage.PropData, and it is what runs the
//                           two stages this row used to. The BreakableModels
//                           chunk table, its random pick and its precache are
//                           C#'s; the propdata.txt KeyValues TREE is still
//                           native, with the blocker for that half argued in
//                           shell_propdata.cpp.
//
//   CDecalEmitterSystem  -- plan_gamedll_after.md Phase A3. NOT DELETED: MOVED,
//                           and completely. `DecalEmitter` in
//                           SourceSharp.Server is an IGameLifecycle participant
//                           at LifecycleStage.Decals holding the whole system:
//                           the script parse, the name table, the weighted pick
//                           and the game-material translation.
//                           game/shared/decals.cpp has left server_csharp.vpc,
//                           and what answers `decalsystem` here is
//                           shell_decals.cpp.
//
//   CSoundControllerImp  -- plan_gamedll_after.md Phase A3, second pair.
//                           NOT DELETED: MOVED. `SoundController` in
//                           SourceSharp.Server is an IFrameLifecycle
//                           participant at FrameStage.SoundController and an
//                           IGameLifecycle participant at
//                           LifecycleStage.SoundController, and between them
//                           they run the two stages this row used to: the
//                           measured stage matrix has CSoundControllerImp
//                           overriding PreClientUpdate and LevelShutdownPreEntity
//                           (plus Shutdown and OnRestore, which are the save
//                           cluster's and are refused by Part 7).
//
//                           AND THE PreClientUpdate ORDER IS PRESERVED, which
//                           took one line elsewhere rather than being assumed.
//                           The only other row declaring that stage is
//                           CPhysicsHook at index 1, and stock ran it FIRST.
//                           ServerGameDll.PreClientUpdate therefore dispatches
//                           the managed participants AFTER the native table
//                           walk, not before it.
//
//                           game/shared/soundenvelope.cpp has left
//                           server_csharp.vpc and what answers
//                           CSoundEnvelopeController::GetController() is
//                           shell_soundenvelope.cpp.
//
//   CSoundEnt            -- plan_gamedll_after.md Phase A3, second pair.
//                           NOT DELETED: MOVED. `SoundEnt` in
//                           SourceSharp.Server is an IFrameLifecycle
//                           participant at FrameStage.SoundEnt, and it is what
//                           runs the ONE stage this row used to: the measured
//                           stage matrix has CSoundEnt overriding
//                           FrameUpdatePreEntityThink and nothing else.
//
//                           AND THE PRE-THINK ORDER IS UNCHANGED, which is the
//                           part worth checking rather than assuming. The rows
//                           that declare FrameUpdatePreEntityThink were
//                           CPostFrameNavigationHook (2), SourceSharpManaged
//                           (12) and CSoundEnt (14). The only row between 12
//                           and 14 was CSoundControllerImp, whose per-frame
//                           stage is PreClientUpdate -- so nothing with a
//                           pre-think stage sat between the row that left and
//                           the row that picked its work up.
//
//                           Its init and teardown are NOT the managed row's:
//                           world_adapter.cpp and shell_level.cpp still
//                           spell CSoundEnt::InitSoundEnt and
//                           ShutdownSoundEnt, and shell_soundent.cpp is what
//                           answers them. game/server/soundent.cpp has left
//                           server_csharp.vpc.
//
//   CSourceSharpStandaloneSystem
//                         -- plan_gamedll_after.md Phase A3, the FIFTH and last
//                            "C" system. NOT DELETED: MOVED, and it is the only
//                            one of the five that was SourceSharp's own code, so
//                            the disposition is the one the other four did not
//                            need: taken apart BY STAGE.
//
//                            The measured stage matrix (docs/gamedll-ledger.md,
//                            Phase 7) has this row overriding Init, LSPre and
//                            FramePost, and the three had three different
//                            answers.
//
//                            Init was DEAD. RegisterStandaloneTiers had already
//                            run by the time this table was walked -- DLLInit
//                            loads the managed runtime at shell_gamedll.cpp:210
//                            and walks this table at :237, and the managed
//                            publish path reaches the registration from inside
//                            the first. The override is gone, with the order
//                            written where it was in shell_standalone.cpp.
//
//                            The other two are `StandaloneHierarchy` in
//                            SourceSharp.Server, an IFrameLifecycle participant
//                            at FrameStage.Standalone and an IGameLifecycle
//                            participant at LifecycleStage.Standalone, driving
//                            four named native steps:
//                            SourceSharp_FrameStandalonePlayerCommands,
//                            SourceSharp_FrameStandaloneUpdate and
//                            SourceSharp_FrameStandaloneFinish in
//                            shell_frame.cpp, and
//                            SourceSharp_LevelShutdownStandaloneEntities in
//                            shell_level.cpp.
//
//                            WHAT MOVED IS THE ORDER, WHICH IS ALL A ROW EVER
//                            CONTRIBUTES. The work is native and stays native --
//                            it walks a CUtlVector of native shells and replays
//                            CUserCmds around a saved gpGlobals->curtime. What
//                            was a C++ comment ("Players first, so an entity's
//                            think this tick sees where the players moved this
//                            tick") is two steps and a C# ordering now.
//
//                            AND THE POSITION IS THE ONE IT HELD. This row was
//                            index 11 and SourceSharpManaged is index 12, with
//                            nothing between them. FrameStage.Standalone is
//                            below every other frame stage, so the two steps run
//                            at the very start of the managed row's post-think
//                            body. The pre-entity SHUTDOWN pass is a BACKWARD
//                            walk, and the managed row now declares that stage
//                            too -- no row between 11 and 12 declared it, so the
//                            teardown runs where it always ran.
//
//   CSourceSharpEventLog  -- Part 9a group E. DELETED (plan_server_native_min
//                            P1): its Init and Shutdown were
//                            SOURCESHARP_TRACE() and nothing else. The requirement it used to
//                            meet -- something must listen, or an event fires
//                            with a null descriptor and the engine segfaults --
//                            is met by shell_gameevents.cpp's managed
//                            subscriptions, which happen during LoadSide, in
//                            DLLInit, long before anyone can connect.
//
//   CGlobalState          -- Part 9a group E. LevelShutdownPreEntity and
//                            LevelShutdownPostEntity save and reset the
//                            cross-level global state table. That table is the
//                            SAVE CLUSTER's (Part 7), and this build's engine
//                            answers "Can't save multiplayer games" to
//                            everything that could produce one.
//
//   CBaseGameStats_Driver -- Part 9a group E. Stat plumbing that uploads to a
//                            service this mod does not report to.
//
//   CToolFrameworkServer  -- Part 9a group E. Every one of its eight stages is
//                            guarded on m_pTools, which is only non-NULL under
//                            -tools on Windows. shell_tools_absent.cpp already
//                            answers the interface.
//
//   CSourceSharpSystem    -- Part 3 / Part 9a group E. Its job on the server is
//                            this table's SourceSharpManaged row, and having
//                            both would be the double drive this phase exists
//                            to end.
//
//   CTemplatesHook        -- plan_server_all.md Wave 1, docs/plan-audit.md D11.
//                            NOT DELETED: MOVED, and it is the smallest of
//                            these dispositions because the row did the least.
//                            Its two stages were LevelInitPreEntity and
//                            LevelShutdownPostEntity and BOTH of them only
//                            cleared lists -- g_PendingTemplatePrecaches and
//                            g_Templates -- and both lists are fields of
//                            SourceSharp.Server's TemplateStore now, cleared by
//                            the managed level lifecycle.
//
//                            THE ORDER IT HELD DOES NOT MATTER AND THAT IS
//                            MEASURED RATHER THAN ASSUMED. A row's contribution
//                            is WHEN it runs relative to the others, and this
//                            one's two stages have no interaction with any of
//                            them: nothing else in the table reads or writes the
//                            template store, and the only thing that does --
//                            SourceSharp_LevelPrecachePointTemplates -- is a
//                            LEVEL STEP in shell_level.cpp, ordered by C#
//                            against the other level steps and not by this
//                            table at all.
//
//                            game/server/TemplateEntities.cpp has left
//                            server_csharp.vpc; what answers Templates_Add and
//                            Templates_AddNamed is shell_templates.cpp.
//
//   CWatchForModelAccess  -- named by Part 9a and NOT PRESENT. The live census
//                            never saw it; precache_model.cpp:173 declares the
//                            class and this build constructs no instance.
//=============================================================================

//=============================================================================
// PHASE 7a: THERE IS NO LONGER A SECOND ANSWER.
//
// Phase 7 had SourceSharp_IsShellGameDll() here, a second read of -shellgamedll
// for the one caller that has no member to ask: world_adapter.cpp's pre-entity
// pass, reached from inside an entity's precache rather than from the shell.
// Its job was to pick between this table and stock's anonymous list.
//
// Both halves of that question are gone. The delegate is deleted (a delegating
// DLLInit is stock's DLLInit, and stock's DLLInit is what called
// IGameSystem::InitAllSystems -- with the calls removed it would have brought a
// server up with no game systems at all, which is the physcollision NULL that
// segfaulted Phase 6), and stock's list cannot be walked from here because
// igamesystem.cpp has left server_csharp.vpc. One list, one walk, no switch.
//=============================================================================

//-----------------------------------------------------------------------------
// Every stage, forwards and backwards.
//
// The two walkers are the whole mechanism. Everything below them is one line
// naming a member function, and the DIRECTION is the only thing that differs
// between an init stage and a shutdown one -- which is Part 3a's second
// determinism property expressed as code rather than as a convention.
//-----------------------------------------------------------------------------
typedef void ( CServerSystem::*ServerSystemStage_t )();
typedef void ( CServerSystemPerFrame::*ServerSystemFrameStage_t )();

static void WalkForward( ServerSystemStage_t fn )
{
	for ( int i = 0; i < kServerSystemCount; i++ )
	{
		MDLCACHE_CRITICAL_SECTION();

		CServerSystem *pSys = kServerSystems[ i ].m_pfnSystem();
		( pSys->*fn )();
	}
}

static void WalkBackward( ServerSystemStage_t fn )
{
	for ( int i = kServerSystemCount; --i >= 0; )
	{
		MDLCACHE_CRITICAL_SECTION();

		CServerSystem *pSys = kServerSystems[ i ].m_pfnSystem();
		( pSys->*fn )();
	}
}

//-----------------------------------------------------------------------------
// The per-frame walk, which is the same rows filtered to the ones that have a
// per-frame half.
//
// Stock keeps a SECOND list for this (s_GameSystemsPerFrame) built by the same
// Add() in the same order, so the relative order of the per-frame systems is
// identical either way. Filtering the one table instead of keeping a second
// list is the same choice this file makes about shutdown: one set of rows, no
// second copy that can drift.
//
// CServerSystem::IsPerFrame() decides it, and then a static_cast. This was a
// dynamic_cast (what stock's IGameSystem::Add used), with IsPerFrame()
// deliberately unused so there would not be two ways to ask. server.so is built
// without RTTI in release (2026-10-01), so IsPerFrame() is now the ONE way, and
// it is the class's own answer: CServerSystemPerFrame overrides it to true and
// nothing else does.
//-----------------------------------------------------------------------------
static void WalkForwardPerFrame( ServerSystemFrameStage_t fn )
{
	for ( int i = 0; i < kServerSystemCount; i++ )
	{
		CServerSystem *pBase = kServerSystems[ i ].m_pfnSystem();
		if ( pBase == NULL || !pBase->IsPerFrame() )
			continue;

		CServerSystemPerFrame *pSys = static_cast< CServerSystemPerFrame * >( pBase );

		MDLCACHE_CRITICAL_SECTION();
		( pSys->*fn )();
	}
}

//-----------------------------------------------------------------------------
// Init, in the table's order, stopping on the first refusal.
//
// Stock's InitAllSystems returns false the moment one says no and so does this,
// for stock's reason: a system that could not init is a subsystem that will be
// dereferenced later by something that has no idea it failed.
//-----------------------------------------------------------------------------
bool SourceSharp_ServerSystemsInit( void )
{
	for ( int i = 0; i < kServerSystemCount; i++ )
	{
		MDLCACHE_CRITICAL_SECTION();

		CServerSystem *pSys = kServerSystems[ i ].m_pfnSystem();

		if ( !pSys->Init() )
		{
			Warning( "[SourceSharp] game system '%s' refused to init; the server "
			         "cannot come up without it\n", kServerSystems[ i ].m_pszName );
			return false;
		}
	}

	// ModelSoundsCacheInit WAS HERE (stock's "Due to dependencies, these are
	// not autogamesystems"): the model sounds cache is gone, plan_server_novalve.md P3.

	return true;
}

void SourceSharp_ServerSystemsPostInit( void )
{
	WalkForward( &CServerSystem::PostInit );
}

void SourceSharp_ServerSystemsShutdown( void )
{
	WalkBackward( &CServerSystem::Shutdown );
}

//-----------------------------------------------------------------------------
// LevelInitPreEntity.
//
// REACHED FROM world_adapter.cpp AND NOT FROM THE SHELL, which is where the one
// landmine in this phase was. Stock calls
// IGameSystem::LevelInitPreEntityAllSystems from inside the WORLD ENTITY's
// precache (gameinterface.cpp's comment at :1101 says so), not from LevelInit --
// so with the crutch's InitAllSystems gone, s_GameSystems is empty in shell
// mode and that call would have walked nothing. Nine systems' pre-entity stage
// would have stopped, silently, including CPhysicsHook's -- which is what
// creates physenv.
//
// So world_adapter.cpp asks THIS instead.
//
// PHASE 7a: it no longer picks between two answers -- see the note above
// SourceSharp_ServerSystemCount. The name still carries stock's "AllSystems"
// suffix on purpose: this is the SAME MOMENT in the same sequence, and a
// reader comparing world_adapter.cpp with stock's world.cpp:622 should land on
// it without having to be told.
//
// pszMapName is unused now and kept in the signature deliberately. Stock's
// LevelInitPreEntityAllSystems used it to fill IGameSystem::MapName(), a static
// this build has no reader for; dropping the parameter would delete the record
// that the map name arrives HERE, from the world entity's own model name,
// before any other system has seen it.
//-----------------------------------------------------------------------------
void SourceSharp_LevelInitPreEntityAllSystems( const char *pszMapName )
{
	NOTE_UNUSED( pszMapName );

	WalkForward( &CServerSystem::LevelInitPreEntity );
}

void SourceSharp_ServerSystemsLevelInitPostEntity( void )
{
	WalkForward( &CServerSystem::LevelInitPostEntity );
}

void SourceSharp_ServerSystemsLevelShutdownPreClearSteamAPIContext( void )
{
	WalkBackward( &CServerSystem::LevelShutdownPreClearSteamAPIContext );
}

void SourceSharp_ServerSystemsLevelShutdownPreEntity( void )
{
	WalkBackward( &CServerSystem::LevelShutdownPreEntity );
}

void SourceSharp_ServerSystemsLevelShutdownPostEntity( void )
{
	WalkBackward( &CServerSystem::LevelShutdownPostEntity );
}

//-----------------------------------------------------------------------------
// The three per-frame passes.
//
// FORWARD, all three, including the two that surround the entity thinks:
// stock's InvokePerFrameMethod walks forwards for every one of them, and only
// the level and lifetime shutdowns are reversed.
//
// The managed frame is a ROW in these passes rather than a call around them,
// and that is measured rather than stylistic: Part 4's sketch puts
// _frames.PreThink and _frames.Think inline in the C# frame, which would move
// the managed frame ahead of CSoundEnt in the pre pass and ahead of
// CRagdollLRURetirement and CEntityListSystem in the post pass. None of those
// three is a no-op.
//-----------------------------------------------------------------------------
void SourceSharp_ServerSystemsFramePreThink( void )
{
	WalkForwardPerFrame( &CServerSystemPerFrame::FrameUpdatePreEntityThink );
}

void SourceSharp_ServerSystemsFramePostThink( void )
{
	WalkForwardPerFrame( &CServerSystemPerFrame::FrameUpdatePostEntityThink );
}

void SourceSharp_ServerSystemsPreClientUpdate( void )
{
	WalkForwardPerFrame( &CServerSystemPerFrame::PreClientUpdate );
}
