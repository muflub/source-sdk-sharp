//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the ABI behind [ScriptedEntity] ====================//
// SS_REASON a  a vtable the engine calls through
//
// What C# calls to declare an entity class, and the free functions it calls on
// an entity afterwards: spawn flags, outputs, thinking, movement, spawn
// transform, mass, prop data, inputs, explosions.
//
// It used to be more than that. Nine CBaseEntity-derived shells lived here —
// Logical, Item, Brush, Prop, Projectile and four ...Base splits — with a
// bridge namespace and an entity factory to make them. None of it was reachable:
// the factory template was never instantiated, so nothing could construct a
// shell, so no shell ever backed a registered class. Every [ScriptedEntity] has
// been a CStandaloneEntity since SourceSharp_InstallStandaloneClass landed. The
// shells were removed in the commits around this one, with the live entity
// factory dumps unchanged at 313 server and 138 client names.
//
// What is left is the boundary itself. Most functions here answer for the
// parallel hierarchy first and fall through to CBaseEntity for the shells that
// ARE still real — the player, and stock's own entity classes. The
// runtime-networked shell used to be the other one; it is deleted.
//
// PHASE 13 STEP 5, SAID ONCE HERE INSTEAD OF EIGHT TIMES BELOW. Every `pBase`
// arm in this file is that fall-through, and `#ifndef SS_CBASEENTITY_ALIAS`
// takes it away armed. What makes that exact rather than a stub is where
// `pBase` comes from: SourceSharp_ResolveEntity (shell_standalone_base.cpp:166)
// sets it in the ELSE of `if ( GetGameEntity() )`, so it is never non-NULL for
// an entity on the parallel hierarchy — and armed there is no CBaseEntity for
// it to be non-NULL for either, because the shell the sentence above names is
// the `CPlayer`/`CBasePlayer` cluster, whose whole content is "stop deriving
// from the entity".
//
// So each armed arm here is the function MINUS a branch that cannot be taken,
// and the standalone arm beside it is untouched. None of these is a case where
// the guarded-out code was the only answer: every one of them already has the
// parallel hierarchy's answer written next to it, which is why the arms are
// substituting rather than whole-body. The two that are NOT retyped this way
// are called out at their own sites.
//
//=============================================================================//

#include "ss_util.h"            // engine
#include "vphysics_interface.h"
#include "ss_physics.h"
#include "ss_physics_shared.h"     // g_PhysWorldObject
#include "physicsowner.h"
#include "engine/ivmodelinfo.h"
#include "scripted_kinds.h"
#include "shell_standalone_class.h"
#include "shell_standalone.h"
#include "shell_standalone_player.h"
#include "shell_diagnostics.h"
#include "said_once_per_id.h"
#include "standalone_relays.h"
#include "sourcesharp.h"
#include "bone_setup.h"
#include "datacache/imdlcache.h"

// What cbase.h supplied, declared (plan_server_novalve.md P5): the engine's
// globals (shell_gamedll.cpp), its model info (shell_engineboundary.cpp) and
// the model cache (connected with the tiers).
extern CGlobalVars *gpGlobals;
extern IVModelInfo *modelinfo;
extern IMDLCache *mdlcache;
#include "shake.h"
#include "shell_scripted_internal.h"
#include "ss_string.h"



//-----------------------------------------------------------------------------
// The entity factory that used to live here is gone.
//
// CSourceSharpScriptedClass built a datamap_t per registered C# class and
// CSourceSharpScriptedClassOf<TShell> constructed the shell; between them they
// were the only path by which any CSourceSharpScripted* instance could come into
// existence. Nothing ever named the template — the linker emitted zero
// instantiations of it — so nothing was ever built and no factory was ever
// installed. The standalone hierarchy has done all of this since
// SourceSharp_InstallStandaloneClass landed; see shell_standalone_class.cpp,
// which builds its own per-class output table and its own factory.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Engine API, called from C#.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Declares an entity class. Called once per [ScriptedEntity] during managed
// startup, which is DLL init — comfortably before any map is parsed, which is
// the only ordering that matters.
//
// What each kind installs with is a lookup rather than a switch here, and the
// reason is worth keeping: the switch this replaced ended in
//
//     EntityFactoryDictionary()->InstallFactory( pClass, pClass->GetClassName() );
//
// on a `CSourceSharpScriptedClass *pClass = NULL` that no branch ever assigned.
// That statement dereferences NULL. It never ran, because every case returned
// before reaching it — the code was correct only by being unreachable, and
// nothing in the build could say so. See scripted_kinds.h and its test.
//-----------------------------------------------------------------------------

// The values scripted_kinds.h restates, pinned to the engine's own. This is the
// only translation unit that has both, so this is where a drift would be caught.
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_NONE     == (int)SOLID_NONE );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_BSP      == (int)SOLID_BSP );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_BBOX     == (int)SOLID_BBOX );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_VPHYSICS == (int)SOLID_VPHYSICS );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_FSOLID_NOT_SOLID     == (int)FSOLID_NOT_SOLID );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_FSOLID_TRIGGER       == (int)FSOLID_TRIGGER );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_FSOLID_NOT_STANDABLE == (int)FSOLID_NOT_STANDABLE );

void SourceSharp_RegisterScriptedEntity( const char *pClassName, const char **ppOutputNames,
                                         int32_t nOutputCount, int32_t nKind )
{
	if ( !pClassName || !pClassName[0] )
		return;

	const SourceSharpScriptedKindParams_t params = SourceSharp_ScriptedKindParams( nKind );

	// NO SHELL, SO NO FACTORY -- plan_entity.md step 2e, and this ONE LINE is
	// what "every class of kind Logical stops creating a shell" is enforced by.
	//
	// Not a check at each creation site. There are four ways a shell gets made
	// -- the managed map spawner, SourceSharp_CreateEntity, the native map
	// parse's residue, and ent_create -- and every one of them goes through
	// EntityFactoryDictionary()->Create(). A class with no factory in that
	// dictionary cannot be constructed by any of them, and the SDK's own
	// CEntityFactoryDictionary::Create already prints "Attempted to create
	// unknown entity type %s!" for one, which is a refusal that names itself
	// (§7) rather than a silent nothing.
	//
	// The managed side creates these in EntityDirectory's engine-invisible
	// range instead; see ShellLessRoutes and ScriptedEntityRegistry.Create.
	if ( params.bShellLess )
		return;

	SourceSharp_InstallStandaloneClass( pClassName, ppOutputNames, nOutputCount,
	                                    params.bServerOnly,
	                                    (SolidType_t)params.nSolidType, params.nSolidFlags,
	                                    params.bTouchEveryTick, params.bPlayer );
}

//-----------------------------------------------------------------------------
// THE ENGINE-INVISIBLE RANGE'S SAFETY COUNTER -- plan_entity.md step 2e's third
// gate clause: "a handle from the invisible range never reaches native".
//
// WHY IT IS COUNTED HERE AND NOT IN MANAGED CODE. A managed counter can only
// count the routes the managed side already knows it has to take, so it would
// read zero for exactly the case it exists to catch: an engine accessor nobody
// routed. SourceSharp_ResolveEntity is where every handle that crosses arrives,
// however it got here.
//
// WHAT IS COUNTED, and both halves matter:
//
//   - the entry index is >= MAX_EDICTS, i.e. the engine-invisible half; AND
//   - the ENGINE'S OWN entity list does not resolve it.
//
// The entry test alone would be wrong. A stock server-only CBaseEntity lives in
// this range too -- baseentity.cpp:545 calls AddNonNetworkableEntity -- and
// native code resolving one of those is ordinary. The lookup failing is what
// separates them: a managed shell-less entity was never added to that list, so
// its handle can never resolve there and every one that arrives is counted,
// while a live stock server-only entity resolves and is not.
//
// A stale handle to a DEAD server-only CBaseEntity is counted too, which is why
// the number is quoted against a measured baseline rather than assumed to be
// zero to begin with.
//
// The return address is kept rather than a name: it is one word, it costs
// nothing on the path that never fires, and `addr2line -f -e server.so <addr>`
// turns it into the ABI function that has to be routed. dladdr would name the
// nearest EXPORTED symbol, which for a static entry point is somebody else's.
//
// INSTRUMENTATION, AND IT IS DEBT: the performance pass owns dropping it, on
// the same terms as 1g's status.think and 2c's RemovedLastFrame.
//-----------------------------------------------------------------------------
static int32_t g_nInvisibleRangeReach = 0;

enum { SOURCESHARP_INVISIBLE_SITES = 8 };

// TWO FRAMES AND NOT ONE. The resolver has an internal caller of its own --
// SourceSharp_ResolveStandalone is a projection of it, used by most of the
// entity ABI -- so the immediate return address names that projection for every
// site behind it and the diagnostic stops one frame short of the answer. The
// second frame is the ABI entry point, which is the thing that has to be routed.
struct SourceSharpReachSite_t
{
	void *pImmediate;
};

static SourceSharpReachSite_t g_InvisibleReachSites[ SOURCESHARP_INVISIBLE_SITES ];
static int g_nInvisibleReachSites = 0;

void SourceSharp_NoteInvisibleRangeReach( void *pReturnAddress )
{
	g_nInvisibleRangeReach++;

	// DISTINCT sites, and at most eight: the point is to name the routes, and a
	// route that fires ten thousand times is one route. A ninth is counted and
	// not named, which is stated rather than silently dropped by the command.
	for ( int i = 0; i < g_nInvisibleReachSites; i++ )
	{
		if ( g_InvisibleReachSites[i].pImmediate == pReturnAddress )
		{
			return;
		}
	}

	if ( g_nInvisibleReachSites < SOURCESHARP_INVISIBLE_SITES )
	{
		g_InvisibleReachSites[ g_nInvisibleReachSites ].pImmediate = pReturnAddress;
		g_nInvisibleReachSites++;
	}
}

//-----------------------------------------------------------------------------
// The distinct sites, for cs_sv_invisible_reach -- a diagnostic that lives in
// shell_scripted_debug.cpp, which only a debug build compiles. Read through
// these two rather than the arrays themselves, which stay this file's.
//-----------------------------------------------------------------------------
int ShellScripted_InvisibleReachSiteCount( bool *pbCapped )
{
	if ( pbCapped )
		*pbCapped = ( g_nInvisibleReachSites >= SOURCESHARP_INVISIBLE_SITES );
	return g_nInvisibleReachSites;
}

void *ShellScripted_InvisibleReachSite( int i )
{
	return ( i >= 0 && i < g_nInvisibleReachSites ) ? g_InvisibleReachSites[i].pImmediate : NULL;
}

int32_t SourceSharp_InvisibleRangeReach( void )
{
	return g_nInvisibleRangeReach;
}

//-----------------------------------------------------------------------------
// WHERE A PLAYER ENTERS THE MAP, APPLIED -- plan_entity.md step 2e.
//
// CStandalonePlayer::Spawn asks managed code which spawn point a player gets
// (OnSelectSpawnPoint), then resolved the returned handle and read the SHELL's
// transform off it. Step 2e takes that shell away: every spawn-point class is
// of kind Logical, so the resolve answers NULL, the placement block is skipped,
// and the player spawns at the world origin. Measured on dm_lockdown, ss_player
// at (-3893.09, 1413.9, 0.83) before and (0, 0, 0) after.
//
// So the placement comes DOWN instead of the transform going UP, and managed
// code calls this from inside OnSelectSpawnPoint -- which is to say from inside
// CBasePlayer::Spawn at exactly the point the native block ran, before the
// player is made solid and before its hull and physics shadow exist. That
// moment is the whole constraint the ABI comment records; it is unchanged.
//
// THE BODY IS THE NATIVE BLOCK, MOVED AND NOT REWRITTEN. Same one-unit lift
// (applied managed-side, where the placement rule now lives), same yaw-only
// entity facing, same zeroed velocity, same fixangle snap.
//
// No cast: SourceSharp_ResolveStandalonePlayer is the shell_resolve.h-family
// projection that already exists for exactly this question, and it answers NULL
// for a handle that is not a standalone player.
//-----------------------------------------------------------------------------
void SourceSharp_PlayerPlaceAtSpawn( uint32_t hPlayer, const float *pOriginXYZ,
                                     const float *pAnglesPYR )
{
	if ( !pOriginXYZ || !pAnglesPYR )
		return;

	CStandalonePlayer *pPlayer = SourceSharp_ResolveStandalonePlayer( hPlayer );

	if ( !pPlayer )
		return;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	const QAngle angles( pAnglesPYR[0], pAnglesPYR[1], pAnglesPYR[2] );

	// YAW ONLY on the entity: a player does not spawn pitched or rolled, and
	// stock's placement drops the other two here rather than at the source --
	// the player STATE below takes all three, which is what the client's view
	// adopts.
	pPlayer->SetAbsTransform( vecOrigin, QAngle( 0.0f, angles[ YAW ], 0.0f ) );
	pPlayer->SetVelocity( vec3_origin );

	// SnapEyeAngles, standalone spelling: the engine reads fixangle out of
	// GetPlayerState and tells the client to adopt v_angle, which also keeps the
	// movement from overwriting it with the next command's stale viewangles.
	pPlayer->PlayerStatePtr()->v_angle  = angles;
	pPlayer->PlayerStatePtr()->fixangle = FIXANGLE_ABSOLUTE;
}

// cs_sv_invisible_reach, the readout of the counter above, is a diagnostic: it
// lives in shell_scripted_debug.cpp, which only a debug build compiles.

//-----------------------------------------------------------------------------
// Spawnflags, which the engine claims during map parsing and so never reach the
// managed keyvalue hook.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetSpawnFlags( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// The SDK fall-through. See the step-5 note at the head of this file.

	return ent.pStandalone ? ent.pStandalone->GetSpawnFlags() : 0;
}

//-----------------------------------------------------------------------------
// SourceSharp_FireScriptedOutput WAS HERE, AND IS DELETED -- plan_entity.md
// step 2a, with its ABI slot.
//
// It took an output name from C#, resolved it against the very list C# had
// declared, and handed it to CStandaloneEntity::FireNamedOutput -- which walked
// a native CEventAction list and posted into g_EventQueue, from where the input
// came back INTO managed code through ENTITY_OP_INPUT. Two crossings to carry a
// message between two managed objects, and a connection list no reload could
// read.
//
// The connections are parsed in managed keyvalue handling now and live on the
// entity's row in EntityDirectory; the queue is EntityIoQueue; the activator
// scope this function existed to set is a field on the queued event. See
// SourceSharp.Abi/EntityIo.cs.
//-----------------------------------------------------------------------------

void SourceSharp_EntityGetSpawnOrigin( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
	{
		const Vector &vec = pStandalone->GetSpawnOrigin();
		pOutXYZ[0] = vec.x; pOutXYZ[1] = vec.y; pOutXYZ[2] = vec.z;
	}
}

void SourceSharp_EntityGetSpawnAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	// A point entity answers with its CURRENT angles, which for one that never
	// moves are the mapper's. That distinction cost an afternoon once already:
	// GetSpawnAngles used to answer only for brush and prop shells, so a
	// logical entity asking silently got zero, and env_physimpact traced along
	// +X in a map that had pointed it along +Y.
	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
	{
		const QAngle &ang = pStandalone->GetSpawnAngles();
		pOutPYR[0] = ang.x; pOutPYR[1] = ang.y; pOutPYR[2] = ang.z;
	}
}

void SourceSharp_EntityGetBoundsSize( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		// From the collision bounds, which for a brush entity came from the
		// submodel the map compiled. A door works out how far to slide from its
		// own depth, so a zero here is a door that opens by nothing at all —
		// and says nothing about why.
		const Vector vecSize = ent.pStandalone->OBBMaxs() - ent.pStandalone->OBBMins();
		pOutXYZ[0] = vecSize.x;
		pOutXYZ[1] = vecSize.y;
		pOutXYZ[2] = vecSize.z;
		return;
	}

	if ( !ent.pBase )
		return;

	// The SDK fall-through. See the step-5 note at the head of this file.
}

void SourceSharp_EntitySetAngles( uint32_t hEntity, const float *pPYR )
{
	if ( !pPYR )
		return;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		pStandalone->SetAbsAngles( QAngle( pPYR[0], pPYR[1], pPYR[2] ) );

		// Through SetAbsOrigin with the position it already has, because that
		// is what rebuilds the transform and tells the client and the partition.
		pStandalone->SetAbsOrigin( pStandalone->GetAbsOrigin() );
		return;
	}

	if ( !ent.pBase )
		return;

	// The SDK fall-through. See the step-5 note at the head of this file.
}

//-----------------------------------------------------------------------------
// How fast it is turning: the shell's own member, degrees per second about the
// world's axes.
//
// WHICH HOME A VELOCITY LIVES IN IS MANAGED'S since plan_physics_bones_native_zero
// (the last thin forwards, 2026-10-12): EntityPhysicsMotion asks the entity's
// IPhysicsObject itself when there is one (through PhysicsObjectVtbl, the
// solver's spin converted between the object's axes and the world's) and
// crosses here only when there is not. So these keep the shell's own member
// and nothing else; a handle that names no shell is a no-op, and a getter
// answers zero for it.
//-----------------------------------------------------------------------------
void SourceSharp_EntitySetAngularVelocity( uint32_t hEntity, const float *pPYR )
{
	if ( !pPYR )
		return;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveEntity( hEntity ).pStandalone )
		pStandalone->SetAngularVelocity( QAngle( pPYR[0], pPYR[1], pPYR[2] ) );
}

void SourceSharp_EntityGetName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = 0;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// The SDK fall-through. See the step-5 note at the head of this file.

	if ( ent.pStandalone )
		ss::StrCopy( pOut, ent.pStandalone->GetEntityName(), nOutSize );
}

//-----------------------------------------------------------------------------
// Physics, called from C#. The simulation is entirely the engine's; these only
// say what to simulate.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The physics object behind a handle, on either hierarchy.
//
// Everything below asks only this, which is why they need no branch of their
// own: a physics object is a physics object, and which kind of entity owns it
// stops mattering the moment you have one. The half-dozen duplicated standalone
// branches that used to sit here said the same thing six times.
//-----------------------------------------------------------------------------
static IPhysicsObject *SourceSharp_PhysicsOf( uint32_t hEntity )
{
	return SourceSharp_ResolveEntity( hEntity ).Physics();
}

// SourceSharp_EntityGetMass and SourceSharp_EntitySetMass (the mass > 0 guard)
// are managed's since plan_physics_bones_native_zero (the last thin forwards,
// 2026-10-12): EntityPhysicsMotion makes each call on the entity's
// IPhysicsObject through PhysicsObjectVtbl, from the held pointer
// SourceSharp_EntityPhysicsObject answers below. Their slots left with them.

//-----------------------------------------------------------------------------
// SourceSharp_EntityFireInput WAS HERE, AND IS DELETED -- plan_entity.md step
// 2a, with its ABI slot.
//
// It fired an input at an entity from managed code: a crossing OUT of C# to
// reach an AcceptInput that crossed straight back IN. The delivery is managed
// now -- EntityDirectory resolves the target and the registry routes the name
// to its [ScriptedEvent] method -- so it costs nothing and never leaves C#.
//
// WHAT WENT WITH IT, said rather than discovered: managed code can no longer
// fire an input at a CBaseEntity. This function's `pTarget` arm was the only
// route to one, and nothing in this tree takes it -- every map entity is on the
// parallel hierarchy, and Phase 4 removes the hierarchy that could have been
// the target. `ent_fire` from the console is unaffected: it posts into
// g_EventQueue, which still reaches managed entities through
// StandaloneEntity_DeliverInput.
//-----------------------------------------------------------------------------

// THE PUNCH WALK (UTIL_ViewPunch and its slot body) went to managed in
// plan_server_native_v2 T4g: the punch is movement state, and
// ServerPlayerMovement.ViewPunch walks the managed records with the same
// reach rules (ServerPlayerMovementRulesTests).

//-----------------------------------------------------------------------------
// How fast it is turning, read back: the shell's own member, which a sensor
// watching a brush or other non-simulated mover reads. See the setter.
//-----------------------------------------------------------------------------
void SourceSharp_EntityGetAngularVelocity( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveEntity( hEntity ).pStandalone )
	{
		const QAngle &angVelocity = pStandalone->GetAngularVelocity();
		pOutPYR[0] = angVelocity[0];
		pOutPYR[1] = angVelocity[1];
		pOutPYR[2] = angVelocity[2];
	}
}

// EntityApplyForceOffset, EntityApplyTorque, EntityEnableMotion (and its
// wake on enable), EntityPhysicsWake, EntityPhysicsSleep (Wake and Sleep, NOT
// motion enable: EntityPhysicsMotionTests pins it) and EntityAddPhysicsGameFlags
// (stock's PhysSetGameFlags, then RecheckCollisionFilter) are managed's since
// plan_physics_bones_native_zero (the last thin forwards, 2026-10-12):
// EntityPhysicsMotion makes each call on the entity's IPhysicsObject through
// PhysicsObjectVtbl. Their slots left with them.

// The disabled entity pairs (stock's g_EntityCollisionHash, keyed by owner)
// are managed's since plan_physics_bones_native_zero 2.1: EntityPairCollisions
// records them and re-asks the filter through the object vtable, and
// managed's ShouldCollide reads them. SetPairCollisions and
// PairCollisionsDisabled left the ABI with them.

//-----------------------------------------------------------------------------
// The entity's IPhysicsObject, for managed to call through the generated
// PhysicsObjectVtbl (plan_physics_managed: managed calls vphysics directly).
// NULL for no object. Carry-out only: a lookup, nothing decided.
//-----------------------------------------------------------------------------
void *SourceSharp_EntityPhysicsObject( uint32_t hEntity )
{
	return SourceSharp_PhysicsOf( hEntity );
}

// The level's IPhysicsEnvironment *, or NULL between levels: managed installs
// its own vphysics callback tables on it (plan_physics_managed B2).
void *SourceSharp_PhysicsEnvironmentPointer()
{
	return physenv;
}

// The level's world physics object, or NULL between levels. Its game data is
// NULL on purpose (physics.cpp's PhysCreateWorld), so managed's solver answers
// know it by identity (plan_physics_bones_native_zero 2.1). A lookup only.
void *SourceSharp_PhysicsWorldObject()
{
	return g_PhysWorldObject;
}


// IPhysicsObject::GetInertia and IPhysicsCollision::CollideGetExtent are
// managed's own calls through the vtables (EngineSide.PhysicsInertia /
// PhysicsCollideExtent, 2026-10-12): a Vector returned by value is two SSE
// registers, which `delegate* unmanaged<..., Vector>` spells. The two
// proxies that stood here are gone.

//-----------------------------------------------------------------------------
// Animation.
//
// Sequences are looked up by NAME because that is what a mapper writes, and
// resolved to an index because that is what the engine plays. A name the model
// does not have comes back as -1 rather than silently playing sequence zero,
// which would look like the wrong animation rather than a missing one.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_EntityLookupSequence IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_EntityLookupSequenceByActivity IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_EntitySequenceDuration IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------


//-----------------------------------------------------------------------------
// SourceSharp_ServerAnimStudioHdr IS GONE WITH ITS CALLERS -- plan_shim.md 2d.
//
// It was the two-branch studio resolve the four sequence and event entry
// points shared: a CBaseAnimating knows its own header, a standalone entity
// has a model index and no CBaseAnimating to ask through. All four of those
// entry points are retired -- one ModelDescriptor crossing answers what they
// answered -- and it had no other caller, so it goes with them rather than
// standing as a static nobody calls.
//
// IT WAS ALSO THE LAST `ent.Animating()` SDK ARM in the sequence family, and
// that arm was UNREACHABLE rather than merely unused: Animating() is
// `pBase ? pBase->GetBaseAnimating() : NULL` and pBase is NULL for every
// entity this build makes. The descriptor crossing reads the model index,
// which is the fact both hierarchies really have.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_EntitySequenceEventCount IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// SourceSharp_EntitySequenceEvent IS RETIRED -- plan_shim.md Phase 2d.
//
// It answered ONE SCALAR PER CALL about a file that cannot change while the
// level is loaded. `ModelDescriptor` crosses the whole model once instead, and
// SourceSharp.Abi/ModelDescriptors.cs keeps the answer for the level.
//
// The SLOT is still in the struct, marked SS_RETIRED in both headers and
// listed in docs/abi-retirements.md: removing a field from the middle
// renumbers every slot after it and AbiParityTests pins names in order, so
// removals are batched into one serialised pass at the end of the phase.
//-----------------------------------------------------------------------------


//=============================================================================//
// THE ATTACHMENT, BONE-COUNT AND SEQUENCE DOORS ARE RETIRED (plan
// physics_bones_native_zero 1.2): both sides' skeletons are managed, each
// side's EngineEntityRender asks its own (ServerSkeletons, ClientSkeletons),
// and the shared IShimModels rows left with the client's flip. What is left
// below is the scale's store, which player movement reads.
//=============================================================================//

//-----------------------------------------------------------------------------
// How big a server-side skeleton is built.
//
// The same slot the client answers, and again NOT for the same reason. The
// client's copy is what the player sees; this one is what Studio_BuildMatrices
// is given, so it decides where an ATTACHMENT lands on a scaled model — a
// parented child's networked origin is composed off these matrices, so a
// skeleton built at size 1 under a model drawn at size 2 puts the child
// halfway to where it belongs. It is also the number scaled_collision_rule.h
// asks about.
//
// THE DECISION IS MANAGED (plan physics_bones_native_zero 1.2): ServerSkeletons
// refuses zero and below, keeps the skeleton's own copy and invalidates it, and
// EngineEntityRender reaches this slot only with a scale it took. What is left
// is the store player movement reads.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneSetModelScale( uint32_t hEntity, float flScale )
{
	if ( CStandaloneEntity *pEntity = SourceSharp_ResolveEntity( hEntity ).pStandalone )
		pEntity->SetModelScale( flScale );
}

