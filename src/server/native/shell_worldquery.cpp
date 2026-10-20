//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — asking the world what is there ======================//
// SS_REASON e  a synchronous mid-step answer
//
// The primitives the emitter entities are built on: what is within a radius,
// what a line hits, and how to push or hurt one of the answers.
//
// These are separated from the per-entity accessors in shell_scripted.cpp
// because they are not about an entity at all. A radius query has no subject —
// it is a question about the world, and the entity that asked it is only where
// the question was asked from.
//
// The division of labour is the same one the rest of the physics API follows.
// C# decides the shape of a blast: how far it reaches, what falls off with
// distance, whether a wall should stop it. What lives here is the part that
// needs the engine's own spatial index, its collision code, and its
// randomness — none of which can be reimplemented on the managed side without
// reimplementing the engine.
//
//=============================================================================//

#include "ss_util.h"            // engine, ss::UTIL_TraceLine/TraceHull/PointContents
#include "ss_shareddefs.h"       // ss::DONT_BLEED
#include "overlay_library.h"
#include "shell_tempents.h"
#include "ss_recipient_filter.h"
#include "ss_world.h"
#include "shell_standalone_class.h"
#include "shell_standalone_player.h"
#include "standalone_movetype.h"
#include "standalone_relays.h"
#include "said_once_per_id.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "vphysics_interface.h"
#include "ss_effects.h"
#include "mathlib_port.h"      // ss_mathlib::CalcSqrDistanceToAABB
#include "engine/ivmodelinfo.h"
#include "model_types.h"
#include "ss_physics_shared.h"   // physprops
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"

#include "precache_model.h"
#include "ss_takedamageinfo.h"	// server.so's damage info

// What cbase.h and util.h supplied, declared (plan_server_novalve.md P5): the
// engine's model info (shell_engineboundary.cpp), and the helpers our own TUs
// define, with util.h's signatures and defaults.
extern IVModelInfo *modelinfo;
void UTIL_BloodStream( const Vector &origin, const Vector &direction, int color, int amount );		// server_util_library.cpp
void UTIL_BloodSpray( const Vector &pos, const Vector &dir, int color, int amount, int flags );		// shell_absent.cpp
void UTIL_BloodDrips( const Vector &origin, const Vector &direction, int color, int amount );		// util_shared_library.cpp
void PrecacheMaterial( const char *pMaterialName );												// shell_stringtables.cpp
void PrecacheParticleSystem( const char *pParticleSystemName );									// shell_stringtables.cpp


// Defined by the game system. Declared here the way the other shells do it,
// rather than pulled in through a header that would drag the whole gamesystem
// into a file about spatial queries.
#include "imanaged_areas.h"   // plan_engineapi_vtables E9: IManagedEntities
#include "trace_hit.h"   // ss::TraceHit: the IHandleEntity the engine stored in trace_t::m_pEnt


//-----------------------------------------------------------------------------
// The right and up vectors of a surface facing a direction.
//
// The engine's own VectorAngles followed by AngleVectors, exposed rather than
// reimplemented in C# because it is the one piece of this arithmetic that has to
// agree EXACTLY across the boundary: the server divides a surface into panes
// with these two vectors and the client places the panes it draws with them, in
// native code, from the same normal. Two implementations agreeing to the last
// bit is not something to assume, and the failure would be panes drawn a
// fraction of a pane away from the ones that broke.
//-----------------------------------------------------------------------------
void SourceSharp_AngleVectorsFromForward( const float *pForwardXYZ,
                                          float *pOutRightXYZ, float *pOutUpXYZ )
{
	if ( !pForwardXYZ )
		return;

	QAngle vAngles;
	VectorAngles( Vector( pForwardXYZ[0], pForwardXYZ[1], pForwardXYZ[2] ), vAngles );

	Vector vRight, vUp;
	AngleVectors( vAngles, NULL, &vRight, &vUp );

	if ( pOutRightXYZ )
	{
		pOutRightXYZ[0] = vRight.x;
		pOutRightXYZ[1] = vRight.y;
		pOutRightXYZ[2] = vRight.z;
	}

	if ( pOutUpXYZ )
	{
		pOutUpXYZ[0] = vUp.x;
		pOutUpXYZ[1] = vUp.y;
		pOutUpXYZ[2] = vUp.z;
	}
}

// Every live entity on the parallel hierarchy. There is no gEntList to ask for
// them — its traversals answer CBaseEntity and correctly skip these — so the
// registry is what a sweep over "everything nearby" has to include.
std::vector< CStandaloneEntity * > &StandaloneEntities();

//-----------------------------------------------------------------------------
// Everything within a radius.
//
// The count is the whole contract: filling the caller's array and returning how
// many were written means a truncated answer is reported rather than disguised.
// A caller that gets back exactly what it asked for knows there may be more.
//
// Deliberately unfiltered. The emitters each discard a different subset — a
// push field ignores what it cannot move, an explosion ignores what cannot be
// hurt — and a filter here would have to be either a union of all of them or a
// parameter for each. Whether a thing is worth acting on is the managed side's
// decision; what is nearby is not.
//
// TWO walks, because the entity list is two lists. gEntList's own sphere query
// resolves each candidate to a CBaseEntity and correctly skips everything on
// the parallel hierarchy — which is every [ScriptedEntity] in this mod — so an
// env_physexplosion asking what was in range got back the players, the weapons,
// and none of the hundred props it was placed among. This is the second half of
// that walk, in the same shape as StandaloneEntities_FindByClassname.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitiesInSphere( const float *pCenterXYZ, float flRadius,
                                      uint32_t *pOutHandles, int32_t nMax )
{
	// HOT: driven by managed thinks, so it runs many times per tick. Measured at
	// 406,532 of 521,458 console lines in one ordinary session -- 78% of the
	// whole log, which is the denial of service the header warns about.
	SOURCESHARP_TRACE_HOT();

	if ( !pCenterXYZ || !pOutHandles || nMax <= 0 )
		return 0;

	const Vector vecCenter( pCenterXYZ[0], pCenterXYZ[1], pCenterXYZ[2] );

	int32_t nCount = 0;

	// THE SDK HALF, AND ARMED IT FINDS NOTHING -- entitylist.cpp:1176's own body
	// already carries this ruling and the same guard, in as many words: "ARMED,
	// THIS SDK WALK FINDS NOTHING ... CollisionProp() is CBaseEntity's own
	// property object, which IGameEntity has no ICollideable base to reach."
	// After the flip there is no SDK entity for it to return, so the standalone
	// sweep below is the whole of this function.
	//
	// THIS COMMENT USED TO SAY "FindEntityInSphere walks the engine's spatial
	// partition rather than the whole entity list, which is what makes this
	// affordable at the 20Hz a push field runs at". IT DOES NOT. Read the body:
	// it starts at FirstEntInfo() and follows m_pNext to the end of the entity
	// list, testing every entity's OBB against the sphere. The partition
	// spelling of the same question is UTIL_EntitiesInSphere /
	// CFlaggedEntitiesEnum, and neither is what this calls. The claim mattered
	// because it is the stated justification for a path marked
	// SOURCESHARP_TRACE_HOT and measured at 406,532 of 521,458 console lines in
	// one session; a reader budgeting this call was reading a partition query's
	// cost for a list walk's.

	// The other hierarchy, tested the same way: does the space this entity
	// occupies reach the sphere.
	//
	// NOT CAnyEntitySphereQuery, AND THAT IS A MEASURED REFUSAL RATHER THAN AN
	// OVERSIGHT. anyentity.h:501 is a partition enumerator that reaches both
	// hierarchies and would replace this whole loop with one
	// EnumerateElementsInSphere -- which is the obvious optimisation, and it
	// would silently remove a capability. CStandaloneEntity::UpdatePartition
	// (shell_standalone_base.cpp:1444) opens
	//
	//     const bool bIsSolid = ( Block().Head.SolidType != SOLID_NONE )
	//                        || ( Block().Head.SolidFlags & FSOLID_TRIGGER ) != 0;
	//     if ( !bIsSolid )  { DestroyHandle( m_Partition ); ... return; }
	//
	// so a standalone entity that is neither solid nor a trigger is OUT of the
	// partition entirely and no enumerator can find it. The registry is the only
	// thing that still can. That is exactly the case the paragraph below names:
	// an env_fire is not solid, and a sweep that could not see one could not
	// spread to the next.
	//
	// Left as a walk deliberately, and the cost is recorded rather than traded
	// away mid-port (LANE_BRIEF: "move it, then measure"). If the perf pass wants
	// the partition here it must first give a non-solid standalone a way to be
	// found -- a fourth partition list, or a registry index -- not delete the
	// loop.
	//
	// The world-space AABB rather than the OBB the SDK's half uses, which is the
	// same choice SourceSharpEntity_t::Bounds made and for the same reason — a
	// caller asking with a radius is asking what space a thing occupies, and the
	// two differ only for a rotated prop at the very edge of the reach.
	//
	// No edict test, unlike the SDK's half. There it excludes logical entities,
	// which have no position worth testing; here every entity has an origin and
	// bounds whether or not it was given an edict, and excluding the server-only
	// ones would leave one env_fire unable to find the next one to spread to.
	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pStandalone : standalones )
	{
		if ( nCount >= nMax )
			break;

		Vector vecMins, vecMaxs;
		pStandalone->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );

		// collisionutils.cpp's IsBoxIntersectingSphere, which was the server's
		// one reason to link that TU: the squared distance from the centre to
		// the box against the squared radius, strictly less, summed per axis in
		// the same order -- bit for bit the same answer.
		if ( !( ss_mathlib::CalcSqrDistanceToAABB( vecMins, vecMaxs, vecCenter ) < flRadius * flRadius ) )
			continue;

		pOutHandles[nCount++] = SourceSharp_HandleFromAnyEntity( pStandalone );
	}

	return nCount;
}

//-----------------------------------------------------------------------------
// What an entity is, as one answer.
//
// Four separate calls would be the natural shape here, and they would be wrong:
// a radius query hands back everything in range, most of which is discarded, so
// the discarding decides how much the whole query costs. One crossing per
// candidate rather than four is the difference.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityGetState( uint32_t hEntity, int32_t *pOutMoveType )
{
	if ( pOutMoveType )
		*pOutMoveType = SOURCESHARP_MOVETYPE_NONE;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return 0;

	// The parallel hierarchy first, because what it can answer is a SUBSET and
	// saying so once is clearer than five ternaries. Answering NOTHING, which is
	// what this did, told every emitter in the mod that a managed brush is not
	// solid, cannot be hurt and is not simulated -- so each of them skipped it
	// for a different reason and none of them said so.
	if ( ent.pStandalone )
	{
		int32_t nStandaloneFlags = 0;

		if ( ent.pStandalone->GetSolid() != SOLID_NONE &&
		     ( ent.pStandalone->GetSolidFlags() & FSOLID_NOT_SOLID ) == 0 )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_SOLID;

		if ( ent.pStandalone->GetPhysics() != NULL )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PHYSICS;

		// What MOVES it. For a standalone entity holding an object, the
		// movetype managed wrote into its block (VPHYSICS for a moveable one,
		// NONE beside a static one), and for a player the movement code's,
		// which keeps a real movetype of its own.
		//
		// The player arm is the one that was missing, and it was missing
		// silently: a standalone player has no physics object, so the only
		// write site here never fired and every player came back
		// MOVETYPE_NONE. Riders.Carry filters by movetype before it looks at
		// where anybody is standing, so a lift's passenger was discarded
		// before the geometry test ran and a rising brush went through them.
		// Precedence and reasoning in standalone_movetype.h; measured on
		// ss_sandbox's belts_lift.
		if ( pOutMoveType )
		{
			CStandalonePlayer *pStandalonePlayer =
				ent.pStandalone->IsPlayer()
					? SourceSharp_PlayerOf( ent.pStandalone )
					: NULL;

			*pOutMoveType = StandaloneMoveType_Report(
				pStandalonePlayer != NULL,
				pStandalonePlayer ? (int)pStandalonePlayer->MvGetMoveType() : 0,
				ent.pStandalone->GetPhysics() != NULL,
				ent.pStandalone->Block().Head.MoveType );
		}

		// Everything on this hierarchy is OFFERED damage and answers for itself:
		// CStandaloneEntity::TakeDamage forwards to managed code and takes the
		// amount that landed back, and "none" is a zero from there. There is no
		// second mechanism, so there is nothing else this flag could mean --
		// and reporting "cannot be hurt" had every emitter skip a managed
		// breakable before it got the chance to decide.
		nStandaloneFlags |= SOURCESHARP_ENTSTATE_TAKESDAMAGE;

		// IS IT A PLAYER. This was not written, and the comment that used to
		// stand below said the remaining flags were "absent rather than false:
		// neither notion exists on this side". That was wrong about all three
		// of them, and expensively so — the whole point of the standalone arm
		// is that this IS where the players are.
		//
		// EntityState.Player has eight live managed readers and every one of
		// them got FALSE for every player there has ever been:
		//   TriggerPlayerMovement  -- "players only" gate; autoduck and
		//                             auto-movement volumes did nothing at all
		//   PointPush              -- the "push players" spawnflag was never
		//                             consulted, so an unticked box pushed them
		//   PhysExplosion          -- the "push player" spawnflag likewise; a
		//                             player got blast DAMAGE where the mapper
		//                             asked for a shove
		//   TriggerWind            -- players are meant to be exempt
		//   TriggerVPhysicsMotion  -- three sites, all of them player-specific
		//   EnvFire                -- spread ignores players
		// Not one of them errored. Sixth-outbreak shape exactly (see
		// docs/divergences.md, the NULL-subject inventory): the subject is
		// unresolvable, the answer is a plausible-looking false, nothing says so.
		if ( ent.pStandalone->IsPlayer() )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PLAYER;

		// IS IT STANDING ON SOMETHING. Only a player has a notion of ground on
		// this hierarchy -- a managed brush is moved by its own stepper or by
		// the solver, neither of which has feet -- so the flag is the movement
		// player's own FL_ONGROUND, read through the same accessor the movement
		// loop writes it with.
		if ( ent.pStandalone->IsPlayer() )
		{
			CStandalonePlayer *pStandalonePlayer =
				SourceSharp_PlayerOf( ent.pStandalone );

			if ( pStandalonePlayer->MvGetFlags() & FL_ONGROUND )
				nStandaloneFlags |= SOURCESHARP_ENTSTATE_ONGROUND;
		}

		// AND IS IT HELD BY SOMETHING ELSE. "Nothing here has a parent to be
		// held by" was the old comment's claim and it is contradicted by an
		// entire file: shell_standalone_hierarchy.cpp is this hierarchy's
		// parenting, with its own registry, its own eager composition and its
		// own once-a-frame sweep. StandaloneHierarchy_IsParented is the
		// question, and it was there the whole time.
		//
		// Two readers, both of which were being told "no" about every parented
		// managed entity in the map: PointPush skips a child because pushing one
		// does nothing, and PointTeleport refuses to move one for the same
		// reason. A parented child that IS pushed or teleported does not stay
		// where it was put -- the next composition drags it back to its parent's
		// offset -- so both were quiet, self-undoing failures.
		if ( StandaloneHierarchy_IsParented( ent.pStandalone ) )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PARENTED;

		return nStandaloneFlags;
	}

	// THE SDK ARM. Every one of these seven asks is a CBaseEntity member the
	// alias removes, and igameentity.h states the answer for six of them by
	// name under "THE TWELVE" -- GetMoveType, GetFlags, GetMoveParent,
	// m_takedamage, VPhysicsGetObject and IsPlayer all "never cross", because
	// the engine never initiates them. There is nothing to retype them TO: the
	// questions are SDK movement and lifetime state, and armed there is no SDK
	// entity left to hold it.
	//
	// So the arm goes behind the guard rather than being rewritten, and the
	// armed answer is the one the function already gives for a handle that
	// resolves to nothing -- zero flags, with *pOutMoveType left at the
	// SOURCESHARP_MOVETYPE_NONE written on entry. That is not a gap being
	// papered over: armed, ent.pStandalone above IS the only hierarchy, so this
	// arm is unreachable rather than unanswered.
	return 0;
}

// SourceSharp_EntityApplyForceCenter (ApplyForceCenter, then Wake) is
// managed's since plan_physics_bones_native_zero (the last thin forwards,
// 2026-10-12): EntityPhysicsMotion.ApplyForceCenter, through PhysicsObjectVtbl.
// Its slot left with it.

// SourceSharp_EntityAddBaseVelocity (shoving a player: a conveyor, a lift, a
// blast, through the base-velocity channel the movement code does not undo)
// went in plan_server_native_v2 T4g. A player's base velocity is the managed
// command loop's record, so the push, the ground release and the unstick nudge
// are ServerPlayerMovement.AddBaseVelocity's, and so are the once-per-entity
// refusals for a push at anything else. Its IShimTransform slot left with it,
// and standalone_basevelocity.h's rule with its one caller.

//-----------------------------------------------------------------------------
// Damage from an emitter, and the force that comes with it.
//
// The direction arrives from C#; the magnitude does not. CalculateExplosiveDamageForce
// scales the force by the damage, clamps it so a large blast cannot fling a body
// at absurd speed, and then jitters it per victim so a crowd caught in one
// explosion does not fly apart in formation.
//
// That jitter is server randomness, and it stays here for the same reason every
// other use of random-> does: the managed side has no business being a source of
// values the demo playback has to reproduce.
//-----------------------------------------------------------------------------
void SourceSharp_EntityTakeDamage( uint32_t hEntity, uint32_t hAttacker, float flDamage,
                                   int32_t nDamageType, const float *pDirXYZ,
                                   const float *pForceOriginXYZ, int32_t bPhysicsOnly )
{
	SOURCESHARP_TRACE();

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	IGameEntity *pEntity = ent.pBase;
	CStandaloneEntity *pStandalone = ent.pStandalone;

	if ( !pEntity && !pStandalone )
		return;

	// On either hierarchy (A40): pBase alone named no managed attacker, and
	// the kill went to nobody.
	IGameEntity *pAttacker = SourceSharp_ResolveEntity( hAttacker ).GameEntity();

	// Falling back to the victim is CBaseEntity-shaped and cannot be done for an
	// entity that is not one. NULL is honest: nothing dereferences an attacker
	// without checking, and inventing one would name the wrong thing in a death
	// message.
	if ( !pAttacker )
		pAttacker = pEntity;

	ss::CTakeDamageInfo info( pAttacker, pAttacker, flDamage, nDamageType );

	if ( pDirXYZ && pForceOriginXYZ )
	{
		ss::CalculateExplosiveDamageForce( &info,
		                               Vector( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] ),
		                               Vector( pForceOriginXYZ[0], pForceOriginXYZ[1], pForceOriginXYZ[2] ) );
	}

	// The push without the health loss. An env_physexplosion set to do no damage
	// is a way to throw the furniture around without killing anyone standing in
	// it, and it is common enough in maps to be worth a flag rather than a
	// second entry point.
	if ( pStandalone )
	{
		// No VPhysics on this hierarchy yet, so the physics-only form has
		// nothing to push. Doing nothing is the right answer rather than
		// dealing the damage the caller asked NOT to deal.
		if ( !bPhysicsOnly )
			pStandalone->TakeDamage( info );

		return;
	}

	// THE SDK ARM, and armed it is unreachable rather than unwritten: the two
	// returns above take every path once ent.pBase is NULL for every handle,
	// which is what GetBaseEntity() answers on a tree with no SDK hierarchy.
	// TakeDamage is one of the members igameentity.h names explicitly -- it
	// left the boundary for StandaloneEntity_TakeDamage -- and the parallel
	// hierarchy's damage is the pStandalone->TakeDamage( info ) arm above.
}

// SourceSharp_RandomFloat IS GONE -- plan_native_api.md Phase 4.
//
// IEngineHost.RandomFloat calls IUniformRandomStream::RandomFloat through
// its own vtable now; nothing here is between the two. The rule the body
// carried has not gone anywhere -- managed code may ASK for a random number
// and may not be the SOURCE of one -- and it is enforced where it always
// really was: UniformRandomStream exposes no SetSeed, and
// VtableBindingTests.TheRandomStreamOffersNoSetSeed says so.

//-----------------------------------------------------------------------------
// The collision box, as offsets from the entity's own origin.
//
// The distinction from EntityGetBoundsSize is small to read and large in
// effect. A SIZE can only be re-centred on the origin by the caller, and most
// prop models put their origin on the BASE of the model rather than in the
// middle -- so a hull built by halving the size sinks through the floor, a room
// check made with it is blocked everywhere, and the entity it guards never
// spawns. That failure looks like a broken template rather than a bad hull.
//
// The world-space AABB minus the origin rather than the OBB, because a hull
// trace is axis-aligned and the caller is asking "what space does this occupy",
// not "what shape is it".
//-----------------------------------------------------------------------------
void SourceSharp_EntityGetBounds( uint32_t hEntity, float *pOutMinsXYZ, float *pOutMaxsXYZ )
{
	// Deliberately untraced, as above: [SuppressGCTransition]. Resolve a handle,
	// read two vectors, return.
	if ( pOutMinsXYZ )
		pOutMinsXYZ[0] = pOutMinsXYZ[1] = pOutMinsXYZ[2] = 0.0f;
	if ( pOutMaxsXYZ )
		pOutMaxsXYZ[0] = pOutMaxsXYZ[1] = pOutMaxsXYZ[2] = 0.0f;

	// Both hierarchies, because a managed brush asks this about ITSELF and the
	// zeros it used to get are a degenerate point rather than a box. That is why
	// game_zone_player counted nobody: every player was outside a zone with no
	// volume, and the entity had no way to find out.
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	Vector vecMins, vecMaxs;
	ent.Bounds( &vecMins, &vecMaxs );

	if ( pOutMinsXYZ )
	{
		pOutMinsXYZ[0] = vecMins.x;
		pOutMinsXYZ[1] = vecMins.y;
		pOutMinsXYZ[2] = vecMins.z;
	}

	if ( pOutMaxsXYZ )
	{
		pOutMaxsXYZ[0] = vecMaxs.x;
		pOutMaxsXYZ[1] = vecMaxs.y;
		pOutMaxsXYZ[2] = vecMaxs.z;
	}
}

//-----------------------------------------------------------------------------
// Precaching a model, and answering the index rather than nothing.
//
// The index is why this exists. Precaching alone could have been a void call;
// what a caller actually wants is the number, because both sides of the wire
// agree on model indices and an entity that networks its appearance should send
// one rather than a path. A beam's sprite is the first of those.
//
// -1 rather than 0 for a model that would not load: 0 is a legitimate index and
// silently networking it would draw the wrong thing rather than nothing.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PrecacheModel( const char *pszModel )
{
	// SOURCESHARP_TRACE_HOT, not bare: tools/slot-census measured this slot at
	// 0.405 lines per server frame in a dm_lockdown load window — all 197 of
	// its calls, 9.2 % of that load's 2,143 trace lines. The recursive precache
	// walk is a `for` over the model list, so "one call per model" is still
	// hundreds of lines every map change, which is the same bar that demoted
	// SourceSharp_TraceLine and the two A11 named. The level-load census the
	// demotion of a load-time slot seems to lose is at verbosity 2, where
	// tools/slot-census still takes it.
	SOURCESHARP_TRACE_HOT();

	if ( !pszModel || !pszModel[0] )
		return -1;

	const int nIndex = Precache_Model( pszModel );

	// PrecacheModel answers -1 itself when the model is missing, and says so in
	// the console. Passed straight through rather than softened, because the
	// caller is the one that knows whether a missing sprite is fatal.
	return nIndex;
}

// SourceSharp_ModelFrameCount AND SourceSharp_ModelType ARE GONE --
// plan_native_api.md Phase 4.
//
// Both were GetModel followed by one question about the model, and both are
// now IFwdModelInfo.FrameCountAt / TypeAt, which do the same two calls
// through the same interface with no shim in between. The DECISIONS moved
// with them and are tested there: -1 for an index that names no model and
// never 0, because 0 is a real frame count; and ModelType.Bad where -1 is
// not available at the type, because mod_bad IS zero.


//=============================================================================//
// The small effects.
//
// Everything below is what the effect entities in Entities/Effects actually DO
// once their decisions have been made — a temp entity or an effect dispatch
// written straight to the wire, with no server-side object behind it and
// nothing to keep afterwards.
//
// The split is SparkEffect's, further up this file: WHEN, HOW MUCH, in which
// DIRECTION and at which of several POSITIONS are decisions and live in C#;
// what is here is the part that needs the engine's own temp-entity channel, its
// trace and its water.
//=============================================================================//

//-----------------------------------------------------------------------------
// Blood, at a point and in a direction.
//
// Three effects behind one call, because a mapper's env_blood emits whichever
// of them its spawnflags name and stock emits them in this order. Splitting
// them into three slots would put the ORDER in C#, where it is not a decision
// anybody makes.
//-----------------------------------------------------------------------------
void SourceSharp_BloodEffect( const float *pOriginXYZ, const float *pDirXYZ,
                              int32_t nColor, float flAmount,
                              int32_t bStream, int32_t nSprayFlags )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ || !pDirXYZ )
		return;

	// DONT_BLEED is a legitimate colour and means exactly this. Checked here
	// rather than in C# because UTIL_BloodSpray checks it too, and two places
	// deciding it is two places that can come to disagree.
	if ( nColor == ss::DONT_BLEED )
		return;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	const Vector vecDir( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] );

	if ( bStream )
		UTIL_BloodStream( vecOrigin, vecDir, nColor, (int)flAmount );
	else
		UTIL_BloodDrips( vecOrigin, vecDir, nColor, (int)flAmount );

	if ( nSprayFlags )
		UTIL_BloodSpray( vecOrigin, vecDir, nColor, (int)flAmount, nSprayFlags );
}

// SourceSharp_EngineGetArea IS GONE -- plan_native_api.md Phase 4.
//
// IEngineWorldQuery.GetArea calls IVEngineServer::GetArea directly. The
// engine's own 255 is still passed through rather than softened: it is
// reserved further down the pipe to mean "no sky camera".

//-----------------------------------------------------------------------------
// The 3D skybox, as the managed sky_camera states it.
//
// Kept in a file-static rather than handed to each player as it arrives,
// because the two things happen at completely different rates: a map states its
// skybox once at level load, and ClientData_Update reads it for every player
// every time the server builds a snapshot. Pushing once and reading many is the
// only shape that does not pay for the difference.
//
// The consumer is six lines in playerlocaldata.cpp, guarded, and they run only
// when the SDK's own sky camera list is empty -- which with sky_camera owned by
// C# is always, and with a hypothetical native one still present would leave
// stock in charge. That ordering is deliberate: the native entity is the one
// that could also be transmitting something else.
//-----------------------------------------------------------------------------
static SourceSharpSkyboxParams g_SkyboxParams;
static bool g_bHaveSkyboxParams = false;

void SourceSharp_SetSkyboxParams( const SourceSharpSkyboxParams *pParams )
{
	SOURCESHARP_TRACE();

	if ( !pParams )
	{
		g_bHaveSkyboxParams = false;
		return;
	}

	g_SkyboxParams = *pParams;
	g_bHaveSkyboxParams = true;
}

//-----------------------------------------------------------------------------
// Fills the engine's own struct from the last thing C# pushed.
//
// Declared in shell_server.h and called from ClientData_Update. False when
// nothing has been pushed, which is a map with no sky_camera at all and is a
// perfectly ordinary state -- dm_lockdown is one.
//-----------------------------------------------------------------------------
// One packed RGBA word into the engine's colour type. Named rather than cast
// through a uint32_t*, because CNetworkColor32 has no address to write through
// -- it wants the four components set, and it wants to know they changed.
template < typename T >
static void SourceSharp_UnpackColor32( uint32_t packed, T &out )
{
	out.SetR( (unsigned char)( packed & 0xFF ) );
	out.SetG( (unsigned char)( ( packed >> 8 ) & 0xFF ) );
	out.SetB( (unsigned char)( ( packed >> 16 ) & 0xFF ) );
	out.SetA( (unsigned char)( ( packed >> 24 ) & 0xFF ) );
}

bool SourceSharp_FillSkyboxParams( ss::sky3dparams_t &out )
{
	if ( !g_bHaveSkyboxParams )
		return false;

	const SourceSharpSkyboxParams &in = g_SkyboxParams;

	out.origin = Vector( in.OriginXYZ[0], in.OriginXYZ[1], in.OriginXYZ[2] );
	out.scale  = in.Scale;
	out.area   = in.Area;

	out.fog.enable = in.FogEnable != 0;
	out.fog.blend  = in.FogBlend != 0;
	out.fog.radial = in.FogRadial != 0;
	out.fog.dirPrimary = Vector( in.FogDirPrimaryXYZ[0], in.FogDirPrimaryXYZ[1],
	                             in.FogDirPrimaryXYZ[2] );

	// color32 is four bytes; the managed side packs them the same way rather
	// than sending four floats, because the send table carries bytes and
	// rounding twice shows up as a colour the mapper did not type.
	SourceSharp_UnpackColor32( in.FogColorPrimary,   out.fog.colorPrimary );
	SourceSharp_UnpackColor32( in.FogColorSecondary, out.fog.colorSecondary );

	out.fog.start      = in.FogStart;
	out.fog.end        = in.FogEnd;
	out.fog.maxdensity = in.FogMaxDensity;

	return true;
}

//-----------------------------------------------------------------------------
// What a surface is made of, and the impact that reads it.
//
// A trace answers with the surfaceprop of the FACE it struck. That is right for
// a wall and wrong for a func_breakable_surf, which vbsp will not compile unless
// exactly ONE of its faces is drawn — so five faces out of six carry nodraw's
// surfaceprop, which is 0, which is concrete. Measured on ss_sandbox: props=28
// GLASS/GLASSWINDOWBREAK070A from the front, props=0 TOOLS/TOOLSNODRAW from four
// units behind the same pane. See docs/divergences.md.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The surfaceprop an entity's own MODEL declares, or -1.
//
// Reached exactly the way stock's CBreakableSurface::Spawn reaches
// $crackmaterial: modelinfo hands back the materials the model draws with, and
// the VMT keys are material vars on them.
//
// -1 IS THE COMMON ANSWER AND NOT A FAILURE. It means "nothing better than the
// face you struck", and there are three ways to get it, all of them right:
//
//   a studio model            its per-hitbox surfaceprop is already finer than
//                             any whole-model answer, and overriding it with
//                             the model's default would lose that
//
//   more than one material    there is no single answer to give. This is the
//                             same count stock refuses to spawn a breakable
//                             surface on ("a material applied to more than one
//                             surface"), so the entity this exists for can
//                             never be the one it happens to
//
//   no materials loaded       a dedicated server has none. The caller falls
//                             back to what the MAP said the surface is, which
//                             is coarser and never worse than concrete
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityModelSurfaceProp( uint32_t hEntity )
{
	SOURCESHARP_TRACE();

	SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( !ent.IsValid() || !physprops )
		return -1;

	// Both hierarchies are IServerEntity — single inheritance from
	// IHandleEntity, so the three views share an address. See shell_resolve.h.
	IServerEntity *pServerEntity = (IServerEntity *)ent.ToHandleEntity();

	if ( !pServerEntity )
		return -1;

	const model_t *pModel = modelinfo->GetModel( pServerEntity->GetModelIndex() );

	if ( !pModel || modelinfo->GetModelType( pModel ) != mod_brush )
		return -1;

	model_t *pMutable = const_cast<model_t *>( pModel );

	if ( modelinfo->GetModelMaterialCount( pMutable ) != 1 )
		return -1;

	IMaterial *pMaterial = NULL;
	modelinfo->GetModelMaterials( pMutable, 1, &pMaterial );

	if ( !pMaterial )
		return -1;

	bool bFound = false;
	IMaterialVar *pVar = pMaterial->FindVar( "$surfaceprop", &bFound, false );

	if ( !bFound || !pVar )
		return -1;

	const char *pszName = pVar->GetStringValue();

	if ( !pszName || !pszName[0] )
		return -1;

	return physprops->GetSurfaceIndex( pszName );
}
