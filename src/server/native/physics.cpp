//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON e  a synchronous mid-step answer
// Purpose: Interface layer for ipion IVP physics.
//
// SOURCESHARP, plan_shim.md Phase 2a (lane 2-physics) — RELOCATED, REASON (e),
// with (b) in Init.
//
// The file did not change; its path did. Phase 2's exit condition is that
// `server_csharp.vpc` names no row under `$SRCDIR\game\`, and this is the last
// of that project's physics rows to leave. The three-way split N1 ruled —
// delete the SS-PHYSWORLD scaffolding and the `#if 0` blocks, move the
// penetration/friction/damage policy and the ConVars to C#, keep the VPhysics
// callback surface — is content and is NOT in this commit.
//
// (e), A SYNCHRONOUS MID-STEP ANSWER. `CCollisionEvent` is handed to vphysics
// as three vtables — `IPhysicsCollisionSolver` (5 slots),
// `IPhysicsCollisionEvent` (10) and `IPhysicsObjectEvent` (2) — and vphysics
// calls them FROM INSIDE ITS OWN SOLVE. `ShouldCollide` is asked per candidate
// pair per step and must answer before the solver can continue;
// `ShouldSolvePenetration` likewise. A crossing on those paths is not
// expensive, it is wrong: managed code asked mid-solve could ask the engine a
// question whose answer is undefined until the step finishes.
// physics_shell.h's opening block is where that reasoning is written out, and
// this file is the translation unit that defines
// SOURCESHARP_PHYSICS_SHELL_ENGINE and includes its engine half.
//
// (b) IN Init. `CPhysicsHook::Init` resolves "VPhysics031",
// "VPhysicsCollision007" and "VPhysicsSurfaceProps001" by name from the engine
// factory. An interface resolved by name at load is (b) by definition.
//
// WHAT IS NOT A REASON, and the file is full of it today: the SS-PHYSWORLD
// report scaffolding, the `#if 0` blocks and the collision shake/dust/warp that
// `PhysicsCollisionRules.cs` already duplicates are none of the above. They are
// this lane's next commits, not this one's.
//
//===========================================================================//


#include "ss_physics.h"           // physics.h's records and entry points, server.so's
#include "shell_serversystem.h"
#include "overlay_library.h"
#include "ss_physics_shared.h"    // the physics pointers, the game-flag bits
#include "ss_recipient_filter.h"
#include "igamerules.h"
#include "ss_shareddefs.h"
#include <vstdlib/random.h>
#include "coordsize.h"
#include "physicsowner.h"
// f-physics: how a refused path says what it is and counts how often it is
// reached. §7 forbids the silent no-op, and the count is what makes "nothing
// can reach this" a measurement instead of a claim.
#include "physics_bridge_refusal.h"
// f-physics: the per-entity side table. c8-shared6 re-homed the system out of
// physics_main_shared.cpp (which leaves) into game/shared/physics_shared.cpp
// (which stays), keeping both accessor names; `EntityDataObject_Has` is the
// question `CBaseEntity::HasDataObjectType`'s bit used to cache.
#include "data_object_access.h"
#include "ss_util.h"
#include "vcollide_parse.h"
// ss_soundcontroller.h went with shell_soundenvelope.cpp (plan_server_native_v2 T4a).
extern IVEngineServer *engine;
#include "utlvector.h"
#include "ss_init_factory.h"
extern CGlobalVars *gpGlobals;
extern IVModelInfo *modelinfo;
extern class IStaticPropMgrServer *staticpropmgr;
#include "engine/IEngineSound.h"
#include "ss_world.h"
float GetCurrentGravity( void );   // movevars_shared.cpp
#include "vphysics/vehicles.h"
using ss::gamevcollisionevent_t;
// SOURCESHARP: IsPrecacheAllowed and IsSimulatingOnAlternateTicks are STATIC
// members of CBaseEntity -- they take no `this` and read no entity. Their
// bodies already live as free functions (entity_shared_statics.h); what stood
// in baseentity_shared_forwarders.cpp was a one-line forwarder that dies with
// the class. These call sites ask the free function directly.
#include "entity_shared_statics.h"
#include "ss_physsaverestore.h"
#include "engine/IStaticPropMgr.h"
using ss::masscenteroverride_t;
#include "vphysics/object_hash.h"
#include "vphysics/collision_set.h"
#include "vphysics/friction.h"
#include "fmtstr.h"
#include "vphysics/performance.h"
#include "tier1/callqueue.h"
#include "vphysics/constraints.h"

// plan_entity.md §3.8, step 3a -- THE PHYSICS SHELL.
//
// The environment, the four callback vtables, the event ring and the rule table
// ShouldCollide is evaluated from. The arms of THIS file that managed them are
// forwarded to it below; the header says why each one moved and what stayed.
//
// The ENGINE define opens the half of the header that names IPhysicsEnvironment.
// Everything above that guard is engine-free and is what the gtest compiles.
#define SOURCESHARP_PHYSICS_SHELL_ENGINE 1
#include "physics_shell.h"


// The lifted touch/ground link walkers and the velocity clip. They used to
// be reached as CBaseEntity::Foo() through one-line forwarders in
// game/shared/baseentity_physics_forwarders.cpp; that quarantine is retired
// and the calls below name the free functions directly.
#include "physicslink.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "imanaged_areas.h"       // IManagedPhysicsFrame: the frame, managed
#include "ss_string.h"
#include "tier0/memdbgon.h"

// phys_speeds and phys_timescale are read by managed's PhysicsFrame
// (plan_physics_bones_native_zero 2.3), which decides every physics step.

// defined in phys_constraint


CEntityList *g_pShadowEntities = NULL;

// THE SERVER'S VPHYSICS POINTERS, HELD (plan_physics_bones_native_zero 2.6):
// stock's physics_shared.cpp globals, filled by CPhysicsHook below and read by
// physicsowner.h's world compare, the shells and physics_debug.cpp.
IPhysics				*physics = NULL;
IPhysicsObject			*g_PhysWorldObject = NULL;
IPhysicsCollision		*physcollision = NULL;
IPhysicsEnvironment		*physenv = NULL;
IPhysicsSurfaceProps	*physprops = NULL;






class CPhysicsHook : public CServerSystem
{
public:
	virtual const char *Name() { return "CPhysicsHook"; }

	virtual bool Init();
	virtual void LevelInitPreEntity();
	virtual void LevelInitPostEntity();
	virtual void LevelShutdownPreEntity();
	virtual void LevelShutdownPostEntity();

	void FlushVehicleScripts() {}

	bool ShouldSimulate()
	{
		return (physenv && !m_bPaused) ? true : false;
	}

	CUtlVector<masscenteroverride_t>	m_massCenterOverrides;

	bool		m_bPaused;

	// plan_entity.md §3.8: "one IPhysicsEnvironment per level ... owned by the
	// game-system instance". THIS is that game-system instance, so the shell's
	// environment arm is a member of it rather than another file-scope global
	// beside physenv -- plan.md §1a's rule, and the reason the create/destroy
	// pair below is four lines instead of twenty.
	CSourceSharpPhysicsEnvironment m_ShellEnv;
};


CPhysicsHook	g_PhysicsHook;

//-----------------------------------------------------------------------------
// Singleton access
//-----------------------------------------------------------------------------
CServerSystem* PhysicsGameSystem()
{
	return &g_PhysicsHook;
}


//-----------------------------------------------------------------------------
// Purpose: The physics hook callback implementations
//-----------------------------------------------------------------------------
bool CPhysicsHook::Init( void )
{
	ss::factorylist_t factories;
	
	// Get the list of interface factories to extract the physics DLL's factory
	ss::FactoryList_Retrieve( factories );

	if ( !factories.physicsFactory )
		return false;

	if ((physics = (IPhysics *)factories.physicsFactory( VPHYSICS_INTERFACE_VERSION, NULL )) == NULL ||
		(physcollision = (IPhysicsCollision *)factories.physicsFactory( VPHYSICS_COLLISION_INTERFACE_VERSION, NULL )) == NULL ||
		(physprops = (IPhysicsSurfaceProps *)factories.physicsFactory( VPHYSICS_SURFACEPROPS_INTERFACE_VERSION, NULL )) == NULL
		)
		return false;

	// THE SURFACE PROPERTY MANIFEST IS READ IN C# on the server
	// (SourceSharp.Server.SurfacePropertyManifest, plan_server_novalve.md P2.4),
	// at module startup, before any map; each file still goes to VPhysics'
	// ParseSurfaceData. PhysParseSurfaceData is the client's now.

	return true;
}


// The SS-PHYSWORLD report, physics_budget and air_density are managed's
// (plan_physics_bones_native_zero 2.3): PhysWorldReport, PhysicsBudget and
// AirDensityCommand. sourcesharp_physworld_report's automatic run is
// PhysWorldReport's OnLevelInitPostEntity.

// shell_standalone_player.cpp's detector. See its block: a player shadow that
// is still live when a NEW environment is created is three pointers into the
// old one, and the only guard against using them was a pointer compare.
extern int g_nSourceSharpPlayerShadowsLive;
extern int g_nSourceSharpPlayerShadowsOutlived;

void CPhysicsHook::LevelInitPreEntity() 
{
	// plan_entity.md §3.8, step 3a -- ENVIRONMENT CREATION IS THE SHELL'S.
	//
	// The twenty lines this replaces did two unrelated things in one function:
	// they built the environment and wired its four callback vtables, and they
	// built four pieces of SDK bookkeeping that happen to have the same
	// lifetime. Only the first is environment management, so only the first
	// moved; the object-pair hash, the world's physics object, the shadow list
	// and the sound precache are still below and leave with their own files in
	// Phase 4.
	//
	// THE FOUR HANDLERS ARE PASSED IN rather than reached for from inside the
	// shell. That is what makes CCollisionEvent replaceable without touching
	// the shell: it does not know what implements the vtables it installs.
	ss::factorylist_t factories;
	ss::FactoryList_Retrieve( factories );

	// BEFORE the new environment exists, because that is the only moment the
	// question can be asked: any shadow still live here belongs to the
	// environment that has just been destroyed. Lane 3f-shadow's detector.
	if ( g_nSourceSharpPlayerShadowsLive > 0 )
		g_nSourceSharpPlayerShadowsOutlived += g_nSourceSharpPlayerShadowsLive;

	physenv = g_PhysicsHook.m_ShellEnv.Create(
		physics,
		NULL,                   // IPhysicsCollisionSolver: managed's own table (PhysicsSolver), installed at pre-entity init
		NULL,                   // IPhysicsCollisionEvent: managed's own table, the same
		NULL,                   // IPhysicsObjectEvent: managed's own table (PhysicsCallbacks), installed at pre-entity init
		NULL,                   // IPhysicsConstraintEvent: managed's own table (PhysicsConstraints), installed at pre-entity init
		factories.engineFactory,
		gpGlobals->interval_per_tick,   // 15 ms per tick
		GetCurrentGravity(),            // HL game gravity, not real-world gravity
		10 );                           // maxCollisionsPerObjectPerTimestep


	// THE GROUP-MATRIX SEED WAS HERE. STEP 3G DELETED IT, WHICH IS WHAT 3A ASKED.
	//
	// requests/3a.md §4: until the RegisterCollisionRules slot was retargeted,
	// managed code's matrix arrived at CSourceSharpGameRules and the shell's
	// evaluator read its own table, so this function copied one into the other
	// once per level -- four hundred calls to a virtual that stock called
	// thousands of times a frame.
	//
	// SourceSharp_RegisterCollisionRules wrote the rows into the shell's table
	// until plan_physics_bones_native_zero 2.3 deleted it; the rules are managed's
	// PhysicsSolverRules, so there is nothing to copy and no level boundary.
	// The four hundred virtual calls per level init are gone with the block.
	//
	// IT DID NOT BECOME A FALLBACK, and that is the part worth saying out loud.
	// §7 decides there is no path from ShouldCollide back out to anything, per
	// pair or otherwise; 3a's bridge was a level-init seed for exactly that
	// reason and its replacement is a registration-time write. Nothing on the
	// evaluation path asks anybody anything.
	// physics_bridge_test.cpp holds both halves as facts.

	// THE WORLD'S PHYSICS IS MANAGED'S (2.6): ServerPhysicsWorld makes the
	// static props' and the world's objects; native holds the world's object.
	// No g_EntityCollisionHash: the pairs are EntityPairCollisions' since 2.1.
	IManagedPhysicsFrame *pPhysicsFrame = ManagedPhysicsFrame();
	g_PhysWorldObject = pPhysicsFrame ? static_cast< IPhysicsObject * >( pPhysicsFrame->CreateWorld() ) : NULL;
	if ( !g_PhysWorldObject )
		Warning( "[SourceSharp] the level has no world physics object: %s\n",
		         pPhysicsFrame ? "managed built none" : "no managed load to build it" );

	//=========================================================================//
	// f-physics: A DECLARED, PRINTED REFUSAL (§7). THE SDK SHADOW-ENTITY LIST
	// IS REMOVED AND IT IS SAID PLAINLY. See PhysAddShadow below for the
	// accessors, which keep their signatures because physics.h declares them.
	//
	// `CEntityList` is four out-of-line members defined in game/server/cbase.cpp,
	// which LEAVES, and `game/server/physics.cpp` is THE ONLY FILE IN THE TREE
	// THAT USES THE TYPE AT ALL -- measured, not assumed:
	//
	//     grep -rn '\bCEntityList\b' game/ sourcesharp/
	//       -> cbase.cpp (the four definitions), entitylist.h (the declaration),
	//          physics.cpp and physics.h (g_pShadowEntities) and nothing else.
	//
	// RE-HOMING IT LOOKS RIGHT AND IS A TRAP. The list stores `entitem_t`, whose
	// member is an EHANDLE -- which resolves through `g_pEntityList`, the entity
	// list the flip replaces (§3.3) -- and whose `operator new`/`delete` come
	// from `g_EntListMemPool`, also in cbase.cpp. Carrying the type here would
	// carry both of those into a file §6 keeps, trading four symbols owed for
	// two different ones owed. That is moving the debt rather than paying it,
	// and it is the same mistake as routing a transform through ICollideable.
	//
	// AND THE LIST IS ALREADY EMPTY ON THIS TREE, which is what makes the
	// refusal cost nothing today rather than only after the flip:
	//
	//     PhysAddShadow    referenced by NO object of the server project
	//     PhysHasShadow    referenced by NO object of the server project
	//     PhysRemoveShadow referenced by baseentity.o and baseentity_shared.o,
	//                      both inside the removal set
	//
	// Nothing fills it, so the walk in PhysicsSimulate has always had nothing to
	// walk, and `VPhysicsShadowUpdate` -- the SDK's own step-movement follow-up,
	// NOT the per-tick VPhysicsUpdate that is on the boundary and is driven by
	// the active-object loop -- has never had a subject here.
	//
	// WHAT WOULD HAVE TO EXIST: shadow controllers are named in physicsowner.h's
	// list of "the machinery that only a CBaseEntity has", so this needs the
	// managed hierarchy to own a shadow controller first. plan_entity.md §3.8
	// already routes shadow controllers through ids allocated by PhysicsShell,
	// which is where the replacement list belongs.
	//=========================================================================//
	static CPhysicsBridgeRefusal s_RefuseShadowList(
		"the SDK shadow-entity list, and the per-step VPhysicsShadowUpdate it drives",
		"CEntityList is defined in cbase.cpp, which leaves, and physics.cpp is the only "
		"file in the tree that uses the type. Re-homing it would carry entitem_t's "
		"EHANDLE and g_EntListMemPool into a file §6 keeps -- moving the debt, not "
		"paying it. The list is already empty on this tree: no object of the server "
		"project references PhysAddShadow or PhysHasShadow at all, and the only "
		"references to PhysRemoveShadow are in baseentity.o and baseentity_shared.o, "
		"both inside the removal set. Shadow controllers are CBaseEntity-only machinery "
		"by physicsowner.h's own list; plan_entity.md §3.8 routes them through ids "
		"allocated by PhysicsShell, which is where the replacement list belongs." );

	if ( s_RefuseShadowList.Reached() )
	{
		Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT,
		         s_RefuseShadowList.What(), s_RefuseShadowList.Refusal() );
	}

	// The surface sounds' precache and handles, managed's too (2.6).
	if ( pPhysicsFrame )
		pPhysicsFrame->PrecacheSurfaceSounds();

	m_bPaused = true;
}



void CPhysicsHook::LevelInitPostEntity() 
{
	m_bPaused = false;
}

void CPhysicsHook::LevelShutdownPreEntity() 
{
	if ( !physenv )
		return;
	physenv->SetQuickDelete( true );
}

void CPhysicsHook::LevelShutdownPostEntity() 
{
	if ( !physenv )
		return;

	ss::PhysSaveRestore_ForgetAllModels();

	g_PhysicsHook.m_ShellEnv.Destroy( physics );
	physenv = NULL;

	physics->DestroyAllCollisionSets();

	g_PhysWorldObject = NULL;

	// Nothing was allocated -- see the refusal in LevelInitPreEntity -- so there
	// is nothing to delete. The pointer is cleared anyway, because leaving a
	// stale non-NULL here is the one way this refusal could become a crash.
	g_pShadowEntities = NULL;
	m_massCenterOverrides.Purge();
	FlushVehicleScripts();
}



// THE PHYSICS FRAME IS MANAGED'S (plan_physics_bones_native_zero 2.3), AND SO
// IS ITS POSITION (plan_server_native_v2 T4e): SourceSharp.Server
// PhysicsFrame decides whether to step, how long, and what the step tells the
// game, and ServerSystems.FramePostThink runs it at this system's position in
// the post-entity-think pass, before the managed frame. CPhysicsHook's
// FrameUpdatePostEntityThink only forwarded there, so it went with the walk.

//-----------------------------------------------------------------------------
// THE QUEUED IMPACT AND BREAK SOUNDS ARE MANAGED'S (plan_physics_bones_native_zero
// 2.3): PhysicsSoundBatch queues them, folds them and plays them every 50 ms at
// OnPreClientUpdate, through vphysics' surface table and the one emit slot
// (EmitWorldSoundScript, shell_sound.cpp). CPhysicsHook no longer has a
// PreClientUpdate of its own.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// WHICH OBJECT IS THE WORLD'S -- physicsowner.h's third arm.
//
// One pointer compare against the singleton the function below assigns. Not a
// registry and not a game-data read: there is exactly one world object per
// level, and identity against a singleton cannot answer "maybe".
//
// BOTH SIDES NULL ANSWERS NO, which is the same clause world_subject.h carries
// and for the same reason: g_PhysWorldObject is NULL before LevelInitPreEntity
// creates it, and `NULL == NULL` would make every object with no physics the
// world for the length of level load.
//-----------------------------------------------------------------------------
CWorld *PhysicsWorldOwner( const IPhysicsObject *pObject )
{
	if ( !pObject || !g_PhysWorldObject )
		return NULL;

	return ( pObject == g_PhysWorldObject ) ? GetWorldEntity() : NULL;
}

//-----------------------------------------------------------------------------
// THE WORLD'S PHYSICS OBJECT, AND THE GAME DATA IT CARRIES IS NULL ON PURPOSE.
// (The build is managed's since plan_physics_bones_native_zero 2.6 --
// ServerPhysicsWorld, which passes 0 as the game data -- and the argument
// below is why.)
//
// Both of stock's builders want the same thing from their argument -- a `void *` to
// store as the object's game data -- and stock stored the CBaseEntity, which is
// how "which entity is this object" was answered: PhysicsOwnerOf casts the game
// data back with static_cast< CBaseEntity * >.
//
// worldspawn is not a CBaseEntity (plan_cworld_trace.md T3), so storing it there
// would hand every reader of that cast a pointer to a class it is not -- the
// exact wrong-vtable shape handle_cross.h exists to prevent, arriving through a
// `void *` where no compiler can see it. NULL is stored instead, and the world
// is identified the way the parallel hierarchy already is: not by what is in the
// game data, but by asking. PhysicsOwnerOf compares against g_PhysWorldObject,
// which is a singleton, one pointer, and exact -- the same shape as the third
// arm world_subject.h added for traces.
//
// STATIC PROPS TAKE THE SAME NULL, and for them it changes nothing at all:
// their game data was the world only so that PhysicsOwnerOf would answer
// "the world" for a static prop, which is what stock wanted and what
// EntityFromEntityHandle already refuses on the trace side.
//-----------------------------------------------------------------------------
// The per-tick transform update, carried out for managed's PhysicsFrame
// (plan_physics_bones_native_zero 2.3): each active object's owner is told
// the solver moved it (PhysicsOwner_t::VPhysicsUpdate, which writes the
// entity's engine-block transform), and the entities each object is the OWN
// object of are written out, in order, for managed's UPDATE. A player's
// movement shadow is on the active list too and is not its entity's object.
//
// pVelocities (three floats per object) is the velocity MANAGED says each
// object's entity moves at, held on the entity for the movement's ground read
// (StandaloneEntity_Velocity) -- plan_physics_bones_perf C2, 2026-10-12.
int32_t SourceSharp_PhysicsUpdateOwners( void **ppObjects, float *pVelocities, int32_t nCount,
                                         uint32_t *pOutOwners )
{
	if ( !ppObjects || !pVelocities || !pOutOwners || nCount <= 0 )
		return 0;

	int32_t nOwned = 0;
	for ( int32_t i = 0; i < nCount; i++ )
	{
		IPhysicsObject *pObject = static_cast< IPhysicsObject * >( ppObjects[ i ] );
		if ( !pObject )
			continue;

		PhysicsOwner_t owner = PhysicsOwnerOf( pObject );
		if ( !owner.IsValid() )
			continue;

		owner.VPhysicsUpdate( pObject );

		if ( owner.pGame && owner.pServer && StandaloneEntity_PhysicsObject( owner.pGame ) == pObject )
		{
			StandaloneEntity_HoldMoveVelocity( owner.pGame,
				Vector( pVelocities[ i * 3 + 0 ], pVelocities[ i * 3 + 1 ], pVelocities[ i * 3 + 2 ] ) );
			pOutOwners[ nOwned++ ] = owner.pServer->GetRefEHandle().ToInt();
		}
	}

	return nOwned;
}
//-----------------------------------------------------------------------------


//-----------------------------------------------------------------------------
// External interface to collision sounds
//-----------------------------------------------------------------------------

//=============================================================================//
// SOURCESHARP, plan_shim.md Phase 2a (lane 2-physics): THE COLLISION SHAKE,
// THE DUST AND THEIR THREE ConVars ARE GONE, AND THE "DOUBLE SHAKE" QUESTION
// IS ANSWERED WITH THEM.
//
// The open question this closes (plan_shim.md §10, lane S3's) was: physics.cpp
// calls UTIL_ScreenShake natively with no SOURCESHARP guard while
// PhysicsCollisionRules.Shake also shakes, so does one heavy collision shake
// the screen twice?
//
// IT DOES NOT, AND IT NEVER DID ON THIS TREE. Measured on 89f333db4 with
// tools/shim-census over the ninja link line:
//
//   PhysCollisionScreenShake   server 0 importers   client 0 importers
//   PhysCollisionDust          server 0 importers   client 0 importers
//
// and neither had a caller inside this file either. Their only caller is
// baseentity.cpp:3284/:3294, a translation unit NEITHER vpc compiles -- it left
// with the entity hierarchy. So the native shake has been unreachable since the
// entity port and the managed one is the only shake there is. A live count
// would have read one before and one after, which is why the census is the
// stronger instrument here and the live run would only have confirmed it.
//
// THE THREE ConVars GO WITH THEM. collision_shake_amp/freq/time had exactly one
// reader between them -- PhysCollisionScreenShake -- so keeping them would have
// left three console knobs that change nothing, which is worse than no knob: an
// operator who sets one gets no effect and no error. Every number is already
// carried, with its citation, by SourceSharp.Server/PhysicsCollisionRules.cs
// (ShakeAmplitudeScale, ShakeFrequency, ShakeDuration, DustSoftMinSpeed,
// DustConcreteMinSpeed, DustSize and the rest), as the constants the managed
// rule uses. Re-declaring them as MANAGED ConVars so an operator can tune the
// managed rule is plan_shim.md Phase 5's ConVar ledger, not this step's: the
// policy class is deliberately static and pure so it can be tested with no
// engine, and threading a registry through it is a design change rather than a
// deletion.
//
// game/server/physics.h still DECLARES both functions. Left alone for the same
// reason as PhysCollisionWarpEffect's declaration: their only caller is in a TU
// neither vpc compiles, and physics.h is not this lane's file.
//=============================================================================//

