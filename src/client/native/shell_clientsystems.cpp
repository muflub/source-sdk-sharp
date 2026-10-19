//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON b  an interface resolved by name at load
//
// THE CLIENT'S NAMED LIST -- plan_cworld_trace.md Phase T4.
//
// The mirror of sourcesharp/game/server/shell_serversystems.cpp. Stock's client
// runs its game systems off an ANONYMOUS list built two ways at once:
// eleven hand-written IGameSystem::Add calls in CHLClient::Init, and every other
// system appending ITSELF from CAutoGameSystem's constructor, in link order.
// The order was therefore partly a decision and mostly an accident of which
// object file the linker happened to place first.
//
// kClientSystems[] below is that list, written down. One row per system, in the
// order it runs, in one file. Every stage pass walks these rows; the four
// shutdown stages walk the SAME rows backwards, so "shut down in reverse order
// from which they are initialized" -- igamesystem.h's own comment -- stops being
// a promise the second list has to keep and becomes a property of the table.
//
//=============================================================================
// THE ORDER IS THIS BUILD'S OBSERVED ORDER, MEASURED, NOT PART 9a'S AND NOT
// STOCK'S SOURCE ORDER.
//
// Part 9a's inventory of game systems has been measured wrong six times in this
// port, so nothing here was read off a document. A temporary census inside
// IGameSystem::InitAllSystems printed s_GameSystems in order, by typeid, on a
// live dm_lockdown load at 2761d5d6 -- the probe applied and reverted by exact
// inverse edits with igamesystem.cpp sha256-identical afterwards. It reported
// FORTY systems and they are the forty rows below, in the order it printed
// them.
//
// Two things that census got right which a source grep does not:
//
//   * the eleven hand-Added systems come FIRST, in CHLClient::Init's order,
//     because IGameSystem::Add appends and InitAllSystems only drains the
//     auto-registered list afterwards. Rows 0-10.
//   * the auto-registered twenty-nine follow in REVERSE construction order,
//     because CAutoGameSystem's constructor pushes onto the head of a singly
//     linked list. Rows 11-39. That is the order that was actually running, so
//     it is the order preserved here.
//
// AND ONE THE CENSUS COULD NOT SEE, recorded because it is the interesting one:
// CSecureLaunchSystem is a CAutoGameSystem in stock and is NOT a row here. It is
// constructed by RegisterSecureLaunchProcessFunc at the END of CHLClient::Init,
// AFTER InitAllSystems has already run and set s_bSystemsInitted -- so it took
// the Add() branch, joined the list too late for its own Init() ever to be
// called, and overrides no stage anyway. It was a registrant that ran nothing.
// It is now a plain class (secure_command_line.h) with the reasoning written
// beside it.
//
//=============================================================================
// A ROW IS A NAME AND ONE FUNCTION POINTER
//
// An accessor returning the CClientSystem *, exactly as kServerSystems[] does,
// and for the reason Phase 7a recorded: forty hand-written per-stage thunks
// would be forty chances to omit one, and an omitted stage is precisely the
// silent failure this phase exists to prevent. The row carries ONE accessor and
// the vtable keeps the dispatch.
//
// NO FILE-SCOPE STATE. kClientSystems[] is `static const` -- a constant, not
// state -- which is this project's standing rule for SourceSharp C++. This file
// defines no game system of its own (the one it did, C_BaseAnimatingGameSystem,
// went with the bone leaf library).
//
//=============================================================================

#include "cbase.h"
#include "shell_clientsystem.h"
#include "datacache/imdlcache.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// The accessors.
//
// Declared here rather than in a header, the way cdll_client_int.cpp:236-237
// declares the two it used: these functions have no header between them and
// this table, and giving them one would be a header whose only reader is this
// file.
//
// The first eleven are the ones stock's CHLClient::Init hand-Adds; five of them
// already existed and six are one-line adapters below, because stock's own
// accessor answers a more specific type. The rest were added by this phase, one
// line beside each instance, because the instance has internal linkage and this
// table is the only other thing that wants it.
//-----------------------------------------------------------------------------
CClientSystem *GameStringSystem();
CClientSystem *SoundEmitterSystem();
CClientSystem *ToolFrameworkClientSystem();
CClientSystem *ViewportClientSystem();
CClientSystem *ClientSoundscapeSystem();

CClientSystem *ClientSystem_StandaloneClient();
CClientSystem *ClientSystem_GlowOverlay();
CClientSystem *ClientSystem_DecalEmitter();
CClientSystem *ClientSystem_DirtySpatialPartition();
CClientSystem *ClientSystem_BulletWhizTimer();
CClientSystem *ClientSystem_PixelVisibility();
CClientSystem *ClientSystem_Sprite();
CClientSystem *ClientSystem_WorldText();
CClientSystem *ClientSystem_Rope();
CClientSystem *ClientSystem_ManagedParticles();
CClientSystem *ClientSystem_StandaloneInterp();
CClientSystem *ClientSystem_SourceSharpManaged();
CClientSystem *ClientSystem_Physics();

//-----------------------------------------------------------------------------
// The five adapters.
//
// Stock's accessor for each of these answers a MORE SPECIFIC type -- an
// interface, or the concrete class -- and stock's CHLClient::Init relied on the
// implicit conversion to IGameSystem *. The conversion is to CClientSystem *
// now and is still implicit; these exist only so the table's column is uniform,
// which is the same reason kServerSystems[] has one for
// CPostFrameNavigationHook.
//-----------------------------------------------------------------------------
class IClientLeafSystem;
class IDetailObjectSystem;
class IClientShadowMgr;
class CClientEffectPrecacheSystem;

IClientLeafSystem *ClientLeafSystem();
IDetailObjectSystem *DetailObjectSystem();
CClientEffectPrecacheSystem *ClientEffectPrecacheSystem( void );
extern IClientShadowMgr *g_pClientShadowMgr;

#include "clientleafsystem.h"
#include "detailobjectsystem.h"
#include "iclientshadowmgr.h"
#include "clienteffectprecachesystem.h"

static CClientSystem *ClientLeafGameSystem()          { return ClientLeafSystem(); }
static CClientSystem *DetailObjectGameSystem()        { return DetailObjectSystem(); }
static CClientSystem *ClientShadowGameSystem()        { return g_pClientShadowMgr; }
static CClientSystem *ClientEffectPrecacheGameSystem(){ return ClientEffectPrecacheSystem(); }

//-----------------------------------------------------------------------------
// THE ONE ROW THIS FILE DEFINED ITSELF, `C_BaseAnimatingGameSystem`, IS GONE
// (2026-10-18). Ported out of c_baseanimating.cpp at the client flip, its one
// stage ran the bone leaf library's level-shutdown sweeps, and no sweep was
// ever registered; the library and the row went together
// (docs/client_evictions.md). The frame's bones are ClientSkeletons'.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The map name -- see the comment on the declaration in shell_clientsystem.h.
//-----------------------------------------------------------------------------
const char *SourceSharp_ClientMapName( char *pszOut, int nOutBytes )
{
	if ( pszOut == NULL || nOutBytes <= 0 )
		return "";

	pszOut[ 0 ] = 0;

	const char *pszLevel = engine ? engine->GetLevelName() : NULL;
	if ( pszLevel == NULL || pszLevel[ 0 ] == 0 )
		return pszOut;

	V_FileBase( pszLevel, pszOut, nOutBytes );
	return pszOut;
}

//=============================================================================
// THE FLIP RULING, ONE LINE PER ROW -- plan_entity.md §6, lane c8-clientsystems.
//
// WHY IT IS HERE AND NOT IN A DOCUMENT. Every row below is a system this client
// actually runs, and the question the client flip asks of each one is "does the
// translation unit that DEFINES it survive the removal of the entity
// hierarchy?". That question is answered per row, and the answer is only useful
// beside the row it is about; a document would drift from the table in one
// merge. `client_flip_ruling_test.cpp` parses these lines and the table itself
// and refuses to let the two disagree.
//
// THE VERDICT IS ABOUT THE FILE, NOT ABOUT THE ROW. Every one of the 38 rows
// below still runs after the flip. STAYS means the file that defines the row's
// system is native after the flip -- de-entitied in place, never deleted.
// LEAVES means that file is on §6's seventeen-file LEAVE list, so the system's
// definition has to be re-homed by whoever owns the file. PORTED means this lane
// moved it, and the row's system is defined in THIS file above.
//
// HOW EACH ROW WAS RESOLVED, and it was not read off a filename. For each row
// the symbol `shell_clientsystems.o` is undefined on was taken from
// `nm --undefined-only shell_clientsystems.o` and joined against
// `nm --defined-only -A *.o` over all 287 objects on the client link line,
// filtered on symbol type so a weak inline emission is not read as a definition.
// The four adapters above resolve to their TARGET's object, which is why three
// of the rows name a datum (`g_pClientLeafSystem`, `g_pClientShadowMgr`,
// `g_ClientThinkList`) rather than an accessor: `ClientLeafSystem()` and
// `ClientThinkList()` are header inlines and define no symbol of their own.
//
// SIXTEEN of the 38 resolve into the 78-TU atomic SCC. Fourteen of those sixteen
// are §6 STAYS-NATIVE files, which is the wave brief's point restated as a
// measurement: a closure is not a deletion list.
//
//   RULING | <row name> | <verdict> | <the file that defines it> | <symbol>
//
// RULING | CGameStringPool | STAYS | sourcesharp/game/shared/gamestringpool.cpp | GameStringSystem
//   ^ RELOCATED, not re-ruled: plan_shim.md Phase 2c moved the file under
//     sourcesharp/ so server_csharp.vpc carries no stock rows. The verdict is
//     still STAYS and the reason letter in its banner is (d, weak) -- a
//     string_t IS an address, and the pool is what makes two spellings of one
//     name compare equal by pointer.
// RULING | CSoundEmitterSystem | STAYS | sourcesharp/game/shared/sound_emitter.cpp | SoundEmitterSystem
// RULING | CToolFrameworkClient | STAYS | game/client/toolframework_client.cpp | ToolFrameworkClientSystem
// RULING | CClientLeafSystem | STAYS | game/client/clientleafsystem.cpp | g_pClientLeafSystem
// RULING | CDetailObjectSystem | STAYS | game/client/detailobjectsystem.cpp | DetailObjectSystem
// RULING | CViewportClientSystem | STAYS | game/client/vgui_game_viewport.cpp | ViewportClientSystem
// RULING | CClientEffectPrecacheSystem | STAYS | sourcesharp/game/client/shell_effects_list_client.cpp | ClientEffectPrecacheSystem
// RULING | CClientShadowMgr | STAYS | game/client/clientshadowmgr.cpp | g_pClientShadowMgr
// RULING | C_SoundscapeSystem | STAYS | sourcesharp/game/client/shell_soundscape_audio_client.cpp | ClientSoundscapeSystem
// RULING | CSourceSharpStandaloneClientSystem | STAYS | sourcesharp/game/client/shell_standalone_client.cpp | ClientSystem_StandaloneClient
// RULING | CGlowOverlaySystem | STAYS | game/client/glow_overlay.cpp | ClientSystem_GlowOverlay
// RULING | CDecalEmitterSystem | STAYS | sourcesharp/game/client/shell_decals_client.cpp | ClientSystem_DecalEmitter
// RULING | CDirtySpatialPartitionEntityList | STAYS | sourcesharp/game/shared/collisionproperty.cpp | ClientSystem_DirtySpatialPartition
// RULING | CBulletWhizTimer | STAYS | sourcesharp/game/client/shell_effects_list_client.cpp | ClientSystem_BulletWhizTimer
// RULING | CPixelVisibilitySystem | STAYS | game/client/c_pixel_visibility.cpp | ClientSystem_PixelVisibility
// RULING | CSourceSharpSpriteSystem | STAYS | sourcesharp/game/client/shell_sprite_render.cpp | ClientSystem_Sprite
// RULING | CSourceSharpWorldTextSystem | STAYS | sourcesharp/game/client/shell_worldtext_render.cpp | ClientSystem_WorldText
// RULING | CSourceSharpRopeSystem | STAYS | sourcesharp/game/client/shell_rope_render.cpp | ClientSystem_Rope
// RULING | CSourceSharpManagedParticlesSystem | STAYS | sourcesharp/game/client/shell_managed_particles.cpp | ClientSystem_ManagedParticles
// RULING | CSourceSharpStandaloneInterpSystem | STAYS | sourcesharp/game/client/shell_standalone_client.cpp | ClientSystem_StandaloneInterp
// RULING | SourceSharp | STAYS | sourcesharp/game/shared/sourcesharp_gamesystem.cpp | ClientSystem_SourceSharpManaged
// RULING | CPhysicsSystem | STAYS | sourcesharp/game/client/shell_physics_client.cpp | ClientSystem_Physics
//
// THE ONE LEAVES went. `ClientSystem_DataObjectAccess()` was re-homed into
// shell_physics_shared_client.cpp when physics_main_shared.cpp left, a
// registration with nothing behind it; that file left with fork physics.cpp,
// whose watcher asks were its only reason (2026-10-18). Its row went with it.
//
// THE ROW THAT LOOKS LIKE IT SHOULD LEAVE AND DOES NOT.
// `CPhysicsSystem` stays at its stock position. Fork physics.cpp, which defined
// it, left client.so (2026-10-18, docs/client_evictions.md); the row is now
// shell_physics_client.cpp's, forwarding its four level events to managed's
// ClientPhysicsSystem.
//
// IT WAS TWO. `CRagdollLRURetirement` (ragdoll_shared.cpp) stood beside it, on
// §6's bone-leaf-library row with the ragdoll builder. The BUILDER is still
// there and still stays; the LRU was deleted by plan_shim.md 4a item 3 (lane
// 3a) because `MoveToTopOfLRU`, the only thing that ever put a ragdoll in its
// list, had zero importers on either link line -- so the per-frame Update
// walked an empty list. Its row and its RULING line went in that commit.
//
//=============================================================================
// THE TABLE.
//
// Read top to bottom for init, bottom to top for shutdown. A row's position is
// the only thing about it that is a decision, so a reader who disagrees with one
// argues with one line in one file.
//=============================================================================
struct ClientSystemRow_t
{
	const char *m_pszName;
	CClientSystem *( *m_pfnSystem )();
};

static const ClientSystemRow_t kClientSystems[] =
{
	//-------------------------------------------------------------------------
	// Rows 0-10: stock's eleven hand-Adds, in CHLClient::Init's order, with
	// stock's two load-bearing comments kept because they are the only two
	// orderings in this half that anybody wrote a reason for.
	//-------------------------------------------------------------------------
	{ "CGameStringPool",                    GameStringSystem },
	{ "CSoundEmitterSystem",                SoundEmitterSystem },
	{ "CToolFrameworkClient",               ToolFrameworkClientSystem },

	// "Client Leaf System has to be initialized first, since DetailObjectSystem
	// uses it" -- stock's comment above its Add block. It is not literally first
	// here and was not literally first there either; what the comment means is
	// BEFORE DetailObjectSystem, and the next row is what it is about.
	{ "CClientLeafSystem",                  ClientLeafGameSystem },
	{ "CDetailObjectSystem",                DetailObjectGameSystem },

	{ "CViewportClientSystem",              ViewportClientSystem },
	{ "CClientEffectPrecacheSystem",        ClientEffectPrecacheGameSystem },
	{ "CClientShadowMgr",                   ClientShadowGameSystem },

	// "NOTE: This must happen prior to ClientThinkList (color correction is
	// updated there)" -- stock's comment, on stock's line, kept on this one.
	// `{ "CClientThinkList", ... }` stood here: no object on the link line
	// scheduled a client think, so it cleared an empty list (C8, lane c6-input).

	{ "C_SoundscapeSystem",                 ClientSoundscapeSystem },

	//-------------------------------------------------------------------------
	// Rows 11-39: the twenty-nine that auto-registered, in the order the live
	// census printed them.
	//-------------------------------------------------------------------------
	{ "CSourceSharpStandaloneClientSystem", ClientSystem_StandaloneClient },
	// CRawInputConvarOnetimeReset left with in_mouse.cpp (lane c6-input): its
	// PostInit is ClientInput.RawInputOnetimeReset, run at the input's Init.
	// `{ "CPropData", ... }` stood here: its LevelInitPreEntity parsed
	// propdata.txt into a chunk table nothing on the client read, and its
	// "precache" was modelinfo->GetModelIndex, a lookup (C9; the file is gone).
	// `{ "CDataObjectAccessSystem", ClientSystem_DataObjectAccess }` STOOD HERE:
	// a registration with nothing behind it (nothing in client.so creates a data
	// object), kept only for fork physics.cpp's watcher asks. It went with
	// shell_physics_shared_client.cpp when that file left (2026-10-18).
	// `{ "CEZParticleInit", ... }` stood here: particles_ez.cpp left with lane
	// c5-particles (FX_Dust is C#'s DustPuff; DrawParticleSingletons draws its
	// executors from shell_managed_particles.cpp).
	{ "CGlowOverlaySystem",                 ClientSystem_GlowOverlay },
	{ "CDecalEmitterSystem",                ClientSystem_DecalEmitter },
	{ "CDirtySpatialPartitionEntityList",   ClientSystem_DirtySpatialPartition },
	{ "CBulletWhizTimer",                   ClientSystem_BulletWhizTimer },
	{ "CPixelVisibilitySystem",             ClientSystem_PixelVisibility },
	// `{ "C_BaseAnimatingGameSystem", ClientSystem_BaseAnimating }` STOOD HERE:
	// its stage ran the bone leaf library's sweeps, none registered (2026-10-18).
	// `{ "CSourceSharpPlayerResourceSystem", ... }` stood here: it published
	// g_PR, the native IGameResources, which nothing on the link line and no
	// engine binary read (lane c6-input); the scoreboard is managed's.
	{ "CSourceSharpSpriteSystem",           ClientSystem_Sprite },
	{ "CSourceSharpWorldTextSystem",        ClientSystem_WorldText },
	{ "CSourceSharpRopeSystem",             ClientSystem_Rope },
	{ "CSourceSharpManagedParticlesSystem", ClientSystem_ManagedParticles },
	// `{ "CSourceSharpDustSystem", ... }` stood here: the dust
	// motes are DustVolumeSim's (lane c5-particles), ticked by the particle
	// executor's Emit, with no per-frame system of their own.
	{ "CSourceSharpStandaloneInterpSystem", ClientSystem_StandaloneInterp },

	// THE MANAGED FRAME'S ROW, in the position the live census measured it in.
	// The CLR is up long before this table is walked -- SourceSharp_ClientEarlyInit
	// runs from CHLClient::Init ahead of gHUD.Init() -- so this row is the frame,
	// not the runtime's start-up.
	{ "SourceSharp",                        ClientSystem_SourceSharpManaged },

	// `{ "CBasePresence", ClientSystem_Presence }` STOOD HERE and is gone with
	// game/client/basepresence.cpp -- plan_entity.md step 7g, "not shipped".
	// Every one of its IGameSystem bodies on PC was empty ("TODO: Implement for
	// PC"), so the row cost a construction, an Init and a Shutdown to do
	// nothing. IBaseClientDLL's five presence methods forwarded to it and are
	// now DECLARED REFUSALS in shell_clientdll.cpp, which is the difference
	// that matters: they used to be silent no-ops and now they name themselves.
	// `{ "CRagdollLRURetirement", ClientSystem_RagdollLRU }` STOOD HERE and
	// went with the class -- plan_shim.md 4a item 3, lane 3a. Its list had no
	// writer on either side (`MoveToTopOfLRU`: 0 importers, server and client),
	// so the per-frame Update walked an empty list. Its RULING line above went
	// in the same commit, and so did the row count this file's test pins.
	{ "CPhysicsSystem",                     ClientSystem_Physics },
	// `{ "CBaseGameStats_Driver", ClientSystem_GameStats }` STOOD HERE and is
	// gone with the file that defined its accessor -- plan_shared.md Phase 4,
	// docs/client_evictions.md. It was the LAST row, so nothing above it moved:
	// the walk order every other row depends on is unchanged.
};

static const int kClientSystemCount = ARRAYSIZE( kClientSystems );

int SourceSharp_ClientSystemCount( void )
{
	return kClientSystemCount;
}

const char *SourceSharp_ClientSystemNameAt( int nIndex )
{
	if ( nIndex < 0 || nIndex >= kClientSystemCount )
		return NULL;

	return kClientSystems[ nIndex ].m_pszName;
}

//-----------------------------------------------------------------------------
// Every stage, forwards and backwards.
//
// The two walkers are the whole mechanism. Everything below them is one line
// naming a member function, and the DIRECTION is the only thing that differs
// between an init stage and a shutdown one.
//-----------------------------------------------------------------------------
typedef void ( CClientSystem::*ClientSystemStage_t )();
typedef void ( CClientSystemPerFrame::*ClientSystemFrameStage_t )();

static void WalkForward( ClientSystemStage_t fn )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem *pSys = kClientSystems[ i ].m_pfnSystem();
		( pSys->*fn )();
	}
}

static void WalkBackward( ClientSystemStage_t fn )
{
	for ( int i = kClientSystemCount; --i >= 0; )
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem *pSys = kClientSystems[ i ].m_pfnSystem();
		( pSys->*fn )();
	}
}

//-----------------------------------------------------------------------------
// The per-frame walk, which is the same rows filtered to the ones that have a
// per-frame half.
//
// Stock keeps a SECOND list for this (s_GameSystemsPerFrame) built by the same
// Add() in the same order, so the relative order of the per-frame systems is
// identical either way. Filtering the one table instead of keeping a second list
// is the same choice this file makes about shutdown: one set of rows, no second
// copy that can drift.
//
// dynamic_cast, which is what stock's IGameSystem::Add used to decide the same
// question when it built its second list. CClientSystem::IsPerFrame() answers
// the same question without RTTI and is deliberately NOT used here: two ways to
// ask one question is how the two stop agreeing.
//-----------------------------------------------------------------------------
static void WalkForwardPerFrame( ClientSystemFrameStage_t fn )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		CClientSystemPerFrame *pSys =
			dynamic_cast< CClientSystemPerFrame * >( kClientSystems[ i ].m_pfnSystem() );

		if ( pSys == NULL )
			continue;

		MDLCACHE_CRITICAL_SECTION();
		( pSys->*fn )();
	}
}

//-----------------------------------------------------------------------------
// Init, in the table's order, stopping on the first refusal.
//
// Stock's InitAllSystems returns false the moment one says no and so does this,
// for stock's reason: a system that could not init is a subsystem that will be
// dereferenced later by something that has no idea it failed. On the client that
// matters more than it does on the server, because the symptom is a frame that
// draws wrong rather than a process that stops.
//-----------------------------------------------------------------------------
bool SourceSharp_ClientSystemsInit( void )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem *pSys = kClientSystems[ i ].m_pfnSystem();

		if ( !pSys->Init() )
		{
			Warning( "[SourceSharp] client system '%s' refused to init; the client "
			         "cannot come up without it\n", kClientSystems[ i ].m_pszName );
			return false;
		}
	}

	return true;
}

void SourceSharp_ClientSystemsPostInit( void )
{
	WalkForward( &CClientSystem::PostInit );
}

void SourceSharp_ClientSystemsShutdown( void )
{
	WalkBackward( &CClientSystem::Shutdown );
}

void SourceSharp_ClientSystemsLevelInitPreEntity( void )
{
	WalkForward( &CClientSystem::LevelInitPreEntity );
}

void SourceSharp_ClientSystemsLevelInitPostEntity( void )
{
	WalkForward( &CClientSystem::LevelInitPostEntity );
}

void SourceSharp_ClientSystemsLevelShutdownPreEntity( void )
{
	WalkBackward( &CClientSystem::LevelShutdownPreEntity );
}

void SourceSharp_ClientSystemsLevelShutdownPostEntity( void )
{
	WalkBackward( &CClientSystem::LevelShutdownPostEntity );
}

//-----------------------------------------------------------------------------
// The three per-frame stages.
//
// PreRender and Update both reach real bodies every frame in a shipping run --
// eight SourceSharp render systems and the managed frame between them. PostRender
// reaches exactly one body, CToolFrameworkClient's, and that body is
// `if ( m_pTools ) ...` with m_pTools NULL unless the game was launched under the
// tool framework. It is wired anyway, because removing it would silently break
// the tool framework; it is written down here because "wired" and "does
// something" are two different claims and Phase 7a paid for the difference.
//-----------------------------------------------------------------------------
void SourceSharp_ClientSystemsPreRender( void )
{
	WalkForwardPerFrame( &CClientSystemPerFrame::PreRender );
}

void SourceSharp_ClientSystemsPostRender( void )
{
	WalkForwardPerFrame( &CClientSystemPerFrame::PostRender );
}

//-----------------------------------------------------------------------------
// Update takes an argument, so it does not go through WalkForwardPerFrame's
// member-pointer typedef. Stock's UpdateAllSystems opened with a call to
// SafeRemoveIfDesiredAllSystems, which is gone with the stage -- nothing
// registers itself here, so nothing can unregister itself either.
//-----------------------------------------------------------------------------
void SourceSharp_ClientSystemsUpdate( float frametime )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		CClientSystemPerFrame *pSys =
			dynamic_cast< CClientSystemPerFrame * >( kClientSystems[ i ].m_pfnSystem() );

		if ( pSys == NULL )
			continue;

		MDLCACHE_CRITICAL_SECTION();
		pSys->Update( frametime );
	}
}
