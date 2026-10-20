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
#include "standalone_basevelocity.h"
#include "standalone_relays.h"
#include "said_once_per_id.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "vphysics_interface.h"
#include "ss_effects.h"
#include "mathlib_port.h"      // ss_mathlib::CalcSqrDistanceToAABB
#include "ss_effect_dispatch.h"
#include "engine/ivmodelinfo.h"
#include "model_types.h"
#include "ss_physics_shared.h"   // physprops
#include "engine/IEngineSound.h"  // SOUND_FROM_WORLD
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
int UTIL_PrecacheDecal( const char *name, bool preload = false );									// server_util_library.cpp
void UTIL_Tracer( const Vector &vecStart, const Vector &vecEnd, int iEntIndex = 0,
                  int iAttachment = ss::kTracerDontUseAttachment, float flVelocity = 0,
                  bool bWhiz = false, const char *pCustomTracerName = NULL, int iParticleID = 0 );	// util_shared_library.cpp
void PrecacheMaterial( const char *pMaterialName );												// shell_stringtables.cpp
void PrecacheParticleSystem( const char *pParticleSystemName );									// shell_stringtables.cpp

// The lifted sound statics. They used to be reached as CBaseEntity::Foo()
// through one-line forwarders in game/shared/baseentity_sound_forwarders.cpp;
// that quarantine is retired and the calls below name GameSound_* directly.
#include "gamesound.h"


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

//-----------------------------------------------------------------------------
// A line through the world -- THE BODY BOTH TRACE SLOTS SHARE.
//
// Every output is optional because the callers want different parts of the
// answer. A blast testing for cover asks only whether it arrived; an impact
// needs where it landed and which way the surface faced, so it knows which way
// to shove what it hit.
//
// A BULLET impact needs the surfaceprop as well, because the decal and the
// sound are chosen from the MATERIAL and not from the geometry.
//
// NOT ITSELF A SLOT, and it carries no SOURCESHARP_TRACE of its own: the two
// public arms below carry SOURCESHARP_TRACE_HOT, so a trace is counted once by
// the name the caller actually asked for rather than twice, or under a name no
// ABI has.
//
// HOT, and it was measured rather than guessed. Phase A11's census reads this
// slot at 1.573 crossings per server frame at rest, and with a bare
// SOURCESHARP_TRACE on it a 2.5-minute dm_lockdown session wrote 26,015 console
// lines about this one function — 78 % of the whole log. Everything else the
// session had to say was buried under it, which is the exact failure
// SOURCESHARP_TRACE_HOT exists for. Ask for it with verbosity 2.
//-----------------------------------------------------------------------------
static int32_t SourceSharp_TraceLineInto( const float *pStartXYZ, const float *pEndXYZ,
                                         int32_t nMask, uint32_t hIgnore,
                                         float *pOutFraction, float *pOutEndXYZ,
                                         float *pOutNormalXYZ, uint32_t *pOutHit,
                                         int32_t *pOutSurfaceProps,
                                         int32_t *pOutSurfaceFlags,
                                         int32_t *pOutHitgroup )
{
	if ( pOutFraction )
		*pOutFraction = 1.0f;
	if ( pOutHit )
		*pOutHit = 0;
	// -1 AND NOT 0 FOR "no surface". Index 0 is a real surfaceprop (`default`),
	// so a zero here would be a plausible answer a caller could act on -- the
	// shape this project has been caught by before. -1 is what
	// SurfacePropGameMaterial already refuses.
	if ( pOutSurfaceProps )
		*pOutSurfaceProps = -1;
	if ( pOutSurfaceFlags )
		*pOutSurfaceFlags = 0;
	if ( pOutHitgroup )
		*pOutHitgroup = 0;

	if ( !pStartXYZ || !pEndXYZ )
		return 0;

	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );

	// Brushes only is the mask that means "does a wall shield you from this".
	// It deliberately ignores props, so a blast is not stopped by the crate it
	// is about to throw.
	int nTraceMask = MASK_SOLID_BRUSHONLY;
	if ( nMask == SOURCESHARP_TRACE_SHOT )
		nTraceMask = MASK_SHOT;
	else if ( nMask == SOURCESHARP_TRACE_BRUSHES_AND_WATER )
		nTraceMask = MASK_SOLID_BRUSHONLY | MASK_WATER;
	// C-Phase 3 lane CX added SOURCESHARP_TRACE_ALL for the CLIENT's trace,
	// and it is mapped here as well rather than only there. The vocabulary is
	// one enumeration serving two implementations: a value this arm did not
	// know would fall through to the default above and answer a brushes-only
	// trace to a caller that asked for everything -- silently, and plausibly.
	else if ( nMask == SOURCESHARP_TRACE_ALL )
		nTraceMask = MASK_ALL;
	// And SOURCESHARP_TRACE_SHOT_HULL, added by the ragdoll blast push
	// (behaviour ledger row C1) for the same reason and mapped here for the
	// same one. It is MASK_SHOT without the hitbox bit and with the grate bit,
	// so it is not MASK_SHOT and must not fall through to it either.
	else if ( nMask == SOURCESHARP_TRACE_SHOT_HULL )
		nTraceMask = MASK_SHOT_HULL;
	// C-Phase 6 lane FRAME added SOURCESHARP_TRACE_SOLID for the legacy tempent
	// pool's collision arm, and it is mapped here as well for the same reason
	// SOURCESHARP_TRACE_ALL is.
	else if ( nMask == SOURCESHARP_TRACE_SOLID )
		nTraceMask = MASK_SOLID;
	// MASK_BLOCKLOS -- "is the line of sight between these two points broken".
	// Added for the debug-draw commands' colour, which is the question stock's
	// UTIL_AddDebugLine asked and the only mask that answers it: the default
	// above stops on glass and does not stop on a blocklos volume, so it would
	// colour the line by a different question. Wired here as well as on the
	// client arm for SOURCESHARP_TRACE_ALL's reason.
	else if ( nMask == SOURCESHARP_TRACE_BLOCKLOS )
		nTraceMask = MASK_BLOCKLOS;

	// The entity to skip may be on either hierarchy, and it usually IS: the
	// caller is almost always an emitter excluding itself. CTraceFilterSimple
	// takes an IHandleEntity, which both kinds already are, so nothing needs
	// converting -- only resolving.
	trace_t tr;
	ss::UTIL_TraceLine( vecStart, vecEnd, nTraceMask,
	                SourceSharp_ResolveEntity( hIgnore ).ToHandleEntity(),
	                COLLISION_GROUP_NONE, &tr );

	if ( pOutFraction )
		*pOutFraction = tr.fraction;

	if ( pOutEndXYZ )
	{
		pOutEndXYZ[0] = tr.endpos.x;
		pOutEndXYZ[1] = tr.endpos.y;
		pOutEndXYZ[2] = tr.endpos.z;
	}

	if ( pOutNormalXYZ )
	{
		pOutNormalXYZ[0] = tr.plane.normal.x;
		pOutNormalXYZ[1] = tr.plane.normal.y;
		pOutNormalXYZ[2] = tr.plane.normal.z;
	}

	if ( pOutHit )
	{
		// The entity of EITHER hierarchy, where this reported only a
		// CBaseEntity. A handle names an entity rather than a C++ class, so
		// there was never a reason to answer nothing for half of them -- and it
		// was not harmless: PhysicsEmitter.IsShielded and phys_magnet both ask
		// "is what the line hit the thing I aimed at", and against a managed
		// target that comparison could not come out true, so every blast read as
		// blocked and every magnet as obstructed.
		*pOutHit = SourceSharp_HandleFromAnyEntity( ss::TraceHit( tr ) );
	}

	// THE SURFACE ONLY WHEN THE LINE WAS STOPPED BY ONE. A trace that ran its
	// whole length has a csurface_t full of whatever the engine left there, and
	// handing that on as "the surface the bullet hit" is how a shot into the
	// open sky comes back naming concrete.
	if ( tr.DidHit() )
	{
		if ( pOutSurfaceProps )
			*pOutSurfaceProps = tr.surface.surfaceProps;
		if ( pOutSurfaceFlags )
			*pOutSurfaceFlags = tr.surface.flags;
		if ( pOutHitgroup )
			*pOutHitgroup = tr.hitgroup;
	}

	// Starting inside something counts as a hit: an emitter buried in a wall has
	// not got a clear line, and reporting otherwise would let it fire through.
	return ( tr.fraction < 1.0f || tr.startsolid ) ? 1 : 0;
}

//-----------------------------------------------------------------------------
// The two public arms over that one body.
//
// ONE DESCRIPTION OF WHAT A TRACE IS, and that is the whole reason the body
// above was hoisted rather than copied: the mask vocabulary in it has grown
// four times (SOURCESHARP_TRACE_ALL, _SHOT_HULL, _SOLID, and the brushes-only
// default), and each time a second copy would have been one a later value fell
// through silently -- which is the exact failure that comment block warns
// about. A copy could not be told from the original by any gate in this tree.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask,
                               uint32_t hIgnore, float *pOutFraction, float *pOutEndXYZ,
                               float *pOutNormalXYZ, uint32_t *pOutHit )
{
	SOURCESHARP_TRACE_HOT();

	return SourceSharp_TraceLineInto( pStartXYZ, pEndXYZ, nMask, hIgnore,
	                                  pOutFraction, pOutEndXYZ, pOutNormalXYZ,
	                                  pOutHit, NULL, NULL, NULL );
}

//-----------------------------------------------------------------------------
// The same line, and the SURFACE that stopped it.
//
// HOT FOR THE SAME REASON ITS NARROW ARM IS -- a shotgun asks this once per
// pellet -- so it carries SOURCESHARP_TRACE_HOT and not a bare
// SOURCESHARP_TRACE. A 2.5-minute session wrote 26,015 console lines about the
// narrow arm when that was got wrong, 78 % of the whole log.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TraceLineSurface( const float *pStartXYZ, const float *pEndXYZ,
                                      int32_t nMask, uint32_t hIgnore,
                                      float *pOutFraction, float *pOutEndXYZ,
                                      float *pOutNormalXYZ, uint32_t *pOutHit,
                                      int32_t *pOutSurfaceProps,
                                      int32_t *pOutSurfaceFlags,
                                      int32_t *pOutHitgroup )
{
	SOURCESHARP_TRACE_HOT();

	return SourceSharp_TraceLineInto( pStartXYZ, pEndXYZ, nMask, hIgnore,
	                                  pOutFraction, pOutEndXYZ, pOutNormalXYZ,
	                                  pOutHit, pOutSurfaceProps, pOutSurfaceFlags,
	                                  pOutHitgroup );
}

// SourceSharp_EntityApplyForceCenter (ApplyForceCenter, then Wake) is
// managed's since plan_physics_bones_native_zero (the last thin forwards,
// 2026-10-12): EntityPhysicsMotion.ApplyForceCenter, through PhysicsObjectVtbl.
// Its slot left with it.

//-----------------------------------------------------------------------------
// Shoving a player.
//
// Base velocity is the engine's "carried along by something" channel — a
// conveyor, a lift, a blast — and it is the only one that survives the movement
// code, which recomputes ordinary velocity from the player's input every tick.
//
// The ground check is not incidental. A player standing on the floor absorbs an
// upward push entirely: the move code sees them supported and cancels it. So
// anything pushing up has to lift them off first, which is what the nudge does.
//
// TWO ARMS, and the second one is the fix. This resolved `.pBase` and returned
// in silence when that was NULL — which is EVERY PLAYER IN THIS MOD, because the
// standalone player is on the parallel hierarchy. So the one channel that can
// move a person did nothing for people:
//
//   Riders.Ride    -- every func_conveyor and every stationary belt carried
//                     nobody. Measured at d85f4f0d: a player stood on
//                     ss_sandbox's belts_belt (speed 100) with the belt
//                     correctly resolved as their ground and did not move one
//                     unit in four seconds; ss_ground read base=(0.0 0.0 0.0)
//                     throughout.
//   PointPush      -- point_push moved no player, flag set or not.
//   PhysExplosion  -- env_physexplosion's "push player" shoved nobody.
//
// Not one of the three reported anything. That silence is the thing being
// removed here as much as the no-op is: a subject this cannot find now SAYS SO,
// once per entity, because a push that quietly does not happen is the exact
// failure mode that let this survive for as long as the standalone player has
// existed.
//
// The arithmetic is standalone_basevelocity.h, shared by both arms and gtested,
// because two arms are what drift — one accumulating where the other overwrites,
// or one releasing the ground where the other does not, shows up only as a belt
// that works for crates and not for people.
//-----------------------------------------------------------------------------
void SourceSharp_EntityAddBaseVelocity( uint32_t hEntity, const float *pVelocityXYZ )
{
	SOURCESHARP_TRACE();

	if ( !pVelocityXYZ )
		return;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	const Vector vecRaw( pVelocityXYZ[0], pVelocityXYZ[1], pVelocityXYZ[2] );

	if ( ent.pStandalone )
	{
		// Only a MOVEMENT player has this channel on this side. A managed brush
		// is moved by its own stepper or by the solver, and neither reads base
		// velocity — so a push aimed at one is not a thing that silently fails,
		// it is a thing that was never going to work, and it says so.
		if ( !ent.pStandalone->IsPlayer() )
		{
			// NO COMPILED CALLER CAN REACH THIS TODAY, and that is written down
			// rather than left to be rediscovered. Riders.Ride and PointPush
			// both filter on MOVETYPE_WALK or _STEP before they push, and
			// StandaloneMoveType_Report gives a non-player standalone entity
			// VPHYSICS or NONE and nothing else; PhysExplosion's push arm is
			// behind the PLAYER flag. So this is a guard against the day one of
			// those rules changes, not a live message.
			//
			// It is here anyway for the reason the view-effects lane gave for
			// fixing a dead UTIL_ScreenShakeObject: leaving one of two arms
			// silent is the exact trap that produced the whole NULL-subject
			// inventory. The branch BELOW is the one that was live and mute --
			// every player, every tick, for as long as this function has
			// existed.
			//
			// Once per entity, not once per tick: a push field runs at 20Hz and
			// a belt every frame, so an unconditional Warning here would be the
			// noise that gets a diagnostic switched off. The set is keyed by
			// handle, which is what makes "this brush" distinguishable from
			// "that brush" in a log full of both.
			static CSaidOncePerId s_SaidNotAPlayer;

			if ( s_SaidNotAPlayer.FirstTime( hEntity ) )
			{
				Warning( "[SourceSharp] AddBaseVelocity: '%s' is on the parallel hierarchy "
				         "and is not a player, so it has no base-velocity channel; the push "
				         "was ignored. Push a physics object with ApplyForceCenter, or move "
				         "it.\n",
				         StandaloneEntity_Name( ent.pStandalone ) );
			}

			return;
		}

		CStandalonePlayer *pPlayer = SourceSharp_PlayerOf( ent.pStandalone );

		const bool bUnconsumed = ( pPlayer->MvGetFlags() & FL_BASEVELOCITY ) != 0;
		const Vector &vecCurrent = pPlayer->MvGetBaseVelocity();

		Vector vecPush;
		StandaloneBaseVelocity_Accumulate( bUnconsumed,
		                                   vecCurrent.x, vecCurrent.y, vecCurrent.z,
		                                   vecRaw.x, vecRaw.y, vecRaw.z,
		                                   &vecPush.x, &vecPush.y, &vecPush.z );

		if ( StandaloneBaseVelocity_ReleasesGround( vecPush.z,
		                                            ( pPlayer->MvGetFlags() & FL_ONGROUND ) != 0 ) )
		{
			// The ground is an IHandleEntity on this side, which the riders lane
			// established -- a standalone player's floor is as often a managed
			// brush as a native one.
			pPlayer->MvSetGroundEntity( NULL );
			pPlayer->MvRemoveFlag( FL_ONGROUND );

			Vector vecOrigin = pPlayer->GetAbsOrigin();
			vecOrigin.z += SOURCESHARP_BASEVELOCITY_UNSTICK;
			pPlayer->SetAbsOrigin( vecOrigin );
		}

		pPlayer->MvSetBaseVelocity( vecPush );
		pPlayer->MvAddFlag( FL_BASEVELOCITY );
		return;
	}

	IGameEntity *pEntity = ent.pBase;

	if ( !pEntity )
	{
		// Neither arm. The handle named nothing at all -- a removed entity or a
		// reused slot -- and a push into the void used to be indistinguishable
		// from a push that landed.
		static CSaidOncePerId s_SaidNoEntity;

		if ( s_SaidNoEntity.FirstTime( hEntity ) )
		{
			Warning( "[SourceSharp] AddBaseVelocity: handle %u resolves to no entity on "
			         "either hierarchy; the push was ignored.\n", hEntity );
		}

		return;
	}

	// THE SDK ARM. Base velocity is SDK movement state end to end -- GetFlags,
	// GetBaseVelocity, SetBaseVelocity, AddFlag and SetGroundEntity are all
	// CBaseEntity's own, and igameentity.h's "THE TWELVE" answers GetFlags for
	// the whole family: FL_* is SDK movement state and never crosses. The
	// parallel hierarchy's answer is the MvSet*/MvGet* block above, which is
	// where a standalone player's base velocity already lives.
	//
	// ARMED THIS TAIL IS UNREACHABLE, not merely unwritten. ent.pBase comes
	// from IServerUnknown::GetBaseEntity() (shell_standalone_base.cpp), which
	// answers only for the SDK's hierarchy -- so armed it is NULL for every
	// handle and the `!pEntity` warning above is the arm's only exit. That
	// warning is also the right thing to say there: on a tree with no SDK
	// entities, a handle that reached this line named nothing on either
	// hierarchy, which is exactly what it reports.
}

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

//-----------------------------------------------------------------------------
// The explosion TEMP ENTITY, and nothing else.
//
// env_explosion is EnvExplosion.cs now. Everything that entity decided -- which
// sprite, how big, how far, which of its spawnflags suppress which part of the
// presentation -- is decided in C#, and this is the one step C# cannot take:
// writing a temp entity to everyone who can see the point.
//
// THE FILTER IS WHY THIS IS NATIVE AND IS THE WHOLE RISK. CPASFilter is a PVS
// query around the blast; managed code has no way to name that set. An
// explosion written to an EMPTY filter is silently invisible -- the damage
// lands, the scorch mark appears, the sound plays for nobody and no fireball is
// ever drawn. This mod shipped precisely that bug once, so the filter is built
// here from the origin and is never a parameter.
//
// nFlags is TE_EXPLFLAG_*, NOT the entity's spawnflags. The two number
// different bits and the translation lives in EnvExplosion.EffectFlags.
//-----------------------------------------------------------------------------
void SourceSharp_ExplosionEffect( const float *pOrigin, int32_t nModelIndex, float flScale,
                                  int32_t nFrameRate, int32_t nFlags, int32_t nRadius,
                                  int32_t nMagnitude )
{
	SOURCESHARP_TRACE();

	if ( !pOrigin )
		return;

	Vector vecOrigin( pOrigin[0], pOrigin[1], pOrigin[2] );

	ss::CPASFilter filter( vecOrigin );


	TE_Explosion( filter, 0.0, &vecOrigin, nModelIndex, flScale, nFrameRate,
	              nFlags, nRadius, nMagnitude );
}

//-----------------------------------------------------------------------------
// What is at a point.
//
// env_explosion asks only "did I go off in water", but the whole CONTENTS_ mask
// comes back: the same call answers a dozen such questions and a predicate per
// caller is how an ABI grows without getting more useful.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PointContents( const float *pOrigin )
{
	SOURCESHARP_TRACE();

	if ( !pOrigin )
		return 0;

	return (int32_t)ss::UTIL_PointContents( Vector( pOrigin[0], pOrigin[1], pOrigin[2] ) );
}

//-----------------------------------------------------------------------------
// A swept box, where TraceLine is a swept ray.
//
// The question a ray cannot answer is "would this FIT here", and that is the
// question anything about to place an object in the world has to ask before it
// places one. env_entity_maker asks it every half second.
//
// startsolid is reported separately rather than folded into the return, because
// the two ways of being blocked want different handling and the caller can tell
// them apart only if this does. A hull that STRUCK something names what it
// struck; a hull that began inside something often names nothing at all, since
// there was no surface to cross. A caller that treated the second as "clear
// because no entity came back" would spawn inside a wall.
//-----------------------------------------------------------------------------
int32_t SourceSharp_TraceHull( const float *pStartXYZ, const float *pEndXYZ,
                               const float *pMinsXYZ, const float *pMaxsXYZ,
                               int32_t nMask, uint32_t hIgnore,
                               uint32_t *pOutHit, int32_t *pOutStartSolid )
{
	SOURCESHARP_TRACE();

	if ( pOutHit )
		*pOutHit = 0;
	if ( pOutStartSolid )
		*pOutStartSolid = 0;

	if ( !pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ )
		return 0;

	const int nTraceMask = ( nMask == SOURCESHARP_TRACE_SHOT ) ? MASK_SHOT : MASK_SOLID;

	trace_t tr;
	ss::UTIL_TraceHull( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ),
	                Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ),
	                Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ),
	                nTraceMask, SourceSharp_ResolveEntity( hIgnore ).ToHandleEntity(),
	                COLLISION_GROUP_NONE, &tr );

	if ( pOutStartSolid )
		*pOutStartSolid = tr.startsolid ? 1 : 0;

	if ( pOutHit )
		*pOutHit = SourceSharp_HandleFromAnyEntity( ss::TraceHit( tr ) );

	return ( tr.fraction < 1.0f || tr.startsolid ) ? 1 : 0;
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
// A shower of sparks.
//
// A temp entity written straight to the wire, which is the cheapest thing the
// engine can send and has no server-side object at all. Nothing about it is a
// decision except when it happens and how big it is.
//-----------------------------------------------------------------------------
void SourceSharp_SparkEffect( const float *pOriginXYZ, int32_t nMagnitude, int32_t nTrailLength,
                              int32_t bMakeSound, const float *pDirXYZ )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ )
		return;

	// vec3_origin is stock's "no particular direction", not a direction of zero
	// length that would have to be special-cased downstream.
	const Vector vecDir = pDirXYZ
		? Vector( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] )
		: vec3_origin;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );

	// NOT DoSpark. That helper takes the sparking entity and calls EmitSound on
	// it WITHOUT a null check, and a managed env_spark is on the parallel
	// hierarchy -- there is no CBaseEntity to hand it. Passing NULL segfaulted
	// the server the first time one sparked audibly.
	//
	// The two halves it does are both reachable directly, and the sound wants to
	// come from the POSITION anyway rather than from an entity, which is what
	// SOUND_FROM_WORLD means.
	ss::Effects()->Sparks( vecOrigin, nMagnitude, nTrailLength, &vecDir );

	if ( !bMakeSound )
		return;

	ss::CPASAttenuationFilter filter( vecOrigin );

	ss::EmitSound_t params;
	params.m_pSoundName = "DoSpark";
	params.m_pOrigin    = &vecOrigin;
	params.m_flVolume   = VOL_NORM;
	params.m_nPitch     = PITCH_NORM;

	GameSound_Emit( filter, SOUND_FROM_WORLD, params );
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

//-----------------------------------------------------------------------------
// The decals a MAP places.
//
// A decal is an index into the engine's table, claimed during level load like
// any other precache. -1 means the material would not load, which is a
// mapper's typo; the caller reports it, because the caller is the one that
// knows which entity wrote the key.
//
// HOT IN THE LOAD WINDOW, and it is named here because the number once
// invited the opposite mistake. This was the loudest surviving bare trace —
// 298 lines on a dm_lockdown load, more than any other except the three A11
// demoted, and tools/slot-census put it at 0.612 lines per server frame in
// the load window. The comment here used to argue that 298 map decals
// precached once is exactly what the trace is FOR and demoting it would hide
// the level-load census to save 0.9 % of a log; the census then showed what
// 0.9 % of a log costs per map change in a session that reloads maps, so the
// trace is gated like the others. Verbosity 2 keeps the level-load census.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PrecacheDecal( const char *pName )
{
	// SOURCESHARP_TRACE_HOT, not bare: tools/slot-census measured this slot at
	// 0.612 lines per server frame in a dm_lockdown load window — all 298 of
	// its calls, 13.9 % of that load's 2,143 trace lines. The comment above
	// argued this trace should stay for the level-load census; the census is
	// what found it, and verbosity 2 keeps it for tools/slot-census while the
	// shipped default no longer writes 298 lines of a map's decals per load.
	SOURCESHARP_TRACE_HOT();

	if ( !pName || !pName[0] )
		return -1;

	return UTIL_PrecacheDecal( pName, true );
}

//-----------------------------------------------------------------------------
// Paints one map decal, in whichever of the four forms was asked for.
//
// The trace is here for BloodDecal's reason carried one step further: the
// engine's StaticDecal wants the position in the LOCAL SPACE of whatever was
// hit, together with that entity's index and its model index, and not one of
// the three survives a round trip through the managed TraceResult. WHICH decal,
// WHERE, and whether it is permanent are the decisions, and all three arrive as
// arguments.
//
// pAnglesXYZ picks the form. NULL traces a short line through the position and
// stamps what it found, which is infodecal. Non-NULL throws the mark along that
// facing, which is info_projecteddecal and needs no trace here because the
// engine's own projection does one.
//-----------------------------------------------------------------------------
int32_t SourceSharp_PaintDecal( int32_t nTexture, const float *pOriginXYZ,
                                const float *pAnglesXYZ, float flProjectDistance,
                                int32_t bPermanent, int32_t bLowPriority )
{
	SOURCESHARP_TRACE();

	if ( nTexture < 0 || !pOriginXYZ )
		return 0;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );

	if ( pAnglesXYZ )
	{
		// The projected form. Both deliveries are the same temp entity; what
		// differs is the filter, and MakeInitMessage is what puts it in the
		// signon data so a late joiner still sees it.
		const QAngle angles( pAnglesXYZ[0], pAnglesXYZ[1], pAnglesXYZ[2] );

		ss::CBroadcastRecipientFilter filter;

		if ( bPermanent )
			filter.MakeInitMessage();

			return 1;

		TE_ProjectDecal( filter, 0.0f, &vecOrigin, &angles, flProjectDistance, nTexture );
		return 1;
	}

	// The traced form. Five units either side of the entity's own position,
	// which is stock's box and is deliberately tiny: a decal is placed ON a
	// surface by the mapper, not thrown at one.
	trace_t trace;
	ss::CTraceFilterSimple traceFilter( NULL, COLLISION_GROUP_NONE );

	ss::UTIL_TraceLine( vecOrigin - Vector( 5, 5, 5 ), vecOrigin + Vector( 5, 5, 5 ),
	                bPermanent ? MASK_SOLID : MASK_SOLID_BRUSHONLY,
	                &traceFilter, &trace );

	// The surface of EITHER hierarchy. This used to be TraceEntity( trace ),
	// and that is the narrowing shape: the helper's job is to answer NULL for
	// anything outside the SDK's tree, and util_shared.h calls that NULL "the
	// SAFE answer -- it reads as hit the world". Safe for a caller about to
	// dereference; wrong HERE, because this site does not dereference it. It
	// turns it into an ENTITY INDEX, and index 0 is the world -- a real entity,
	// with a real identity transform. So a permanent decal aimed at a managed
	// brush was silently stored against the world at world coordinates: the
	// mark stayed where the brush had been, did not move with it, and "painted
	// on the crate" and "painted on nothing" produced the identical call.
	//
	// The handle round trip is the funnel, not a hand-rolled cast: a handle
	// names an entity rather than a C++ class, and both hierarchies have had
	// one all along. Something in the partition that is not an entity at all --
	// a static prop -- has no valid handle, resolves to neither arm, and comes
	// out of EntityIndex() as 0, which is the answer it always had.
	const SourceSharpEntity_t hit =
		SourceSharp_ResolveEntity( SourceSharp_HandleFromAnyEntity( ss::TraceHit( trace ) ) );

	const int nEntityIndex = hit.EntityIndex();

	if ( !bPermanent )
	{
		// The sprayed form: one temp entity, in world space, to whoever is
		// connected. The engine works out the surface for itself.
		ss::CBroadcastRecipientFilter filter;

			return 1;

		TE_BSPDecal( filter, 0.0f, &vecOrigin, nEntityIndex, nTexture );
		return 1;
	}

	// The permanent form. The engine stores it against an entity and a model,
	// so the position has to be expressed in that entity's frame -- otherwise a
	// decal on a door travels to wherever the door's local origin happens to
	// put it.
	Vector vecPosition = vecOrigin;
	int nModelIndex = 0;

	if ( nEntityIndex && hit.IsValid() )
	{
		nModelIndex = hit.ModelIndex();
		VectorITransform( vecOrigin, hit.EntityToWorld(), vecPosition );

		// Stock warns and paints nothing. An entity with no model has no
		// surface for the decal to be stored against, and painting it anyway
		// stamps the mark wherever the identity transform lands it.
		if ( nModelIndex == 0 )
		{
			Warning( "[SourceSharp] decal would have hit entity %i (class:%s, name:%s) with modelindex 0; suppressed\n",
			         nEntityIndex, hit.Classname(), hit.Name() );
			return 0;
		}
	}

	engine->StaticDecal( vecPosition, nTexture, nEntityIndex, nModelIndex, bLowPriority != 0 );
	return 1;
}

//-----------------------------------------------------------------------------
// A line of text drawn in the world at a position.
//
// The engine's debug-overlay channel, which is how point_message has always
// drawn: it is a mapper's annotation belonging to a PLACE rather than to a
// screen, and there is no other way to put text at a world position without
// building a font renderer, which this plan rules out by name.
//
// EntityTextAtPosition rather than CBaseEntity::EntityText, and the difference
// is the whole reason this takes a position: EntityText derives one from the
// entity's collision bounds, and an entity on the parallel hierarchy with no
// model and no collision has none to derive from.
//-----------------------------------------------------------------------------
void SourceSharp_DrawWorldText( const float *pOriginXYZ, int32_t nLine, const char *pText,
                                float flDuration, int32_t r, int32_t g, int32_t b, int32_t a )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ || !pText )
		return;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );

	// 4c: the drawing moved to sourcesharp/game/shared/overlay_library, which is
	// entity-free, so that game/shared/debugoverlay_shared.cpp can leave the
	// server's vpc. Same call, same arguments, same engine entry underneath.
	Overlay_TextAtPosition( vecOrigin, nLine, pText, flDuration, r, g, b, a );
}

//-----------------------------------------------------------------------------
// One bullet streak, drawn between two points.
//
// An entity index of zero and no attachment, which is stock's env_tracer
// exactly: the streak is not coming out of a gun and has nothing to hang off.
//-----------------------------------------------------------------------------
void SourceSharp_TracerEffect( const float *pStartXYZ, const float *pEndXYZ )
{
	SOURCESHARP_TRACE();

	if ( !pStartXYZ || !pEndXYZ )
		return;

	UTIL_Tracer( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ),
	             Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ) );
}

//-----------------------------------------------------------------------------
// Bubbles rising out of a volume.
//
// te->Bubbles rather than te->Fizz, and the reason is structural rather than a
// preference: TE_Fizz networks an entity INDEX and the client resolves it with
// cl_entitylist->GetEnt, which answers C_BaseEntity * — NULL for everything on
// the parallel hierarchy. The effect would arrive, find nothing and emit
// nothing, with no diagnostic anywhere. te->Bubbles carries the corners
// themselves and needs no entity on the far side.
//
// What that costs is the HEIGHT: stock's client works it out per client from
// the entity it looked up, and this works it out once, here. The trace is the
// same one — straight up out of the top of the box until the water ends.
//-----------------------------------------------------------------------------
void SourceSharp_BubbleEffect( uint32_t hEntity, int32_t nCount, float flSpeed )
{
	SOURCESHARP_TRACE();

	Vector vecMins, vecMaxs;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	// CollisionProp() is a CBaseEntity member, so armed this arm cannot be
	// spelled -- and it also cannot be REACHED: ent.pBase is NULL for every
	// handle once GetBaseEntity() has no SDK hierarchy to answer for. The
	// standalone arm below is then the live one, which is the arm this effect
	// was written for in the first place (see the header above: te->Bubbles
	// exists here precisely because the parallel hierarchy has no client-side
	// entity to look up).
	//
	// NOT folded into SourceSharpEntity_t::Bounds(). That projection answers
	// ORIGIN-RELATIVE bounds and this call wants the world-space AABB, so the
	// substitution would be Origin() + Bounds() -- a subtract and an add back,
	// which is not exactly the identity in floating point. The guard keeps the
	// unarmed numbers bit-for-bit what they were.
	if ( ent.pStandalone )
	{
		ent.pStandalone->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );
	}
	else
	{
		return;
	}

	// The bubble's own sprite, not the brush's model: the brush is invisible and
	// drawing its model index would draw the volume itself.
	const int nBubbleModel = engine->PrecacheModel( "sprites/bubble.vmt" );

	if ( nBubbleModel <= 0 )
		return;

	// How far up the bubbles travel: out of the top of the volume until the
	// water ends. A volume with no water above it gets its own height, which is
	// what stock's client falls back to.
	Vector vecTop( ( vecMins.x + vecMaxs.x ) * 0.5f,
	               ( vecMins.y + vecMaxs.y ) * 0.5f,
	               vecMaxs.z );

	float flHeight = vecMaxs.z - vecMins.z;

	trace_t tr;
	ss::UTIL_TraceLine( vecTop, vecTop + Vector( 0, 0, 1024 ), MASK_WATER,
	                NULL, COLLISION_GROUP_NONE, &tr );

	if ( tr.fraction < 1.0f )
		flHeight = tr.endpos.z - vecMins.z;

	ss::CBroadcastRecipientFilter filter;


	TE_Bubbles( filter, 0.0f, &vecMins, &vecMaxs, flHeight,
	            nBubbleModel, nCount, flSpeed );
}

//-----------------------------------------------------------------------------
// The funnel of sprites that converges on — or flies out of — a point.
//-----------------------------------------------------------------------------
void SourceSharp_FunnelEffect( const float *pOriginXYZ, int32_t nSpriteIndex,
                               int32_t bReversed )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ || nSpriteIndex <= 0 )
		return;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );

	// Broadcast rather than PAS, which is stock's own choice and is not an
	// oversight: a funnel is large, silent and meant to be seen from across a
	// map, so a filter that asks whether you could HEAR it is the wrong
	// question.
	ss::CBroadcastRecipientFilter filter;


	TE_LargeFunnel( filter, 0.0f, &vecOrigin, nSpriteIndex, bReversed ? 1 : 0 );
}

//-----------------------------------------------------------------------------
// A puff of dust, thrown along a direction.
//
// Normalised HERE as well as in C#, because ss::Effects()->Dust asserts on a
// direction that is not a unit vector and a debug build stops on it. That
// assert is stock's; what stock does not guard against is a ZERO vector, which
// is what a mapper who left the angles alone hands it.
//-----------------------------------------------------------------------------
void SourceSharp_DustEffect( const float *pOriginXYZ, const float *pDirXYZ,
                             float flSize, float flSpeed )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ || !pDirXYZ )
		return;

	Vector vecDir( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] );

	if ( vecDir.LengthSqr() < 1e-6f )
		vecDir.Init( 0.0f, 0.0f, 1.0f );
	else
		VectorNormalize( vecDir );

	ss::Effects()->Dust( Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] ),
	                  vecDir, flSize, flSpeed );
}

//-----------------------------------------------------------------------------
// A splash of water at a point.
//-----------------------------------------------------------------------------
void SourceSharp_SplashEffect( const float *pOriginXYZ, float flScale, int32_t bInSlime )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ )
		return;

	ss::CEffectData data;

	data.m_vOrigin = Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	data.m_vNormal = Vector( 0, 0, 1 );
	data.m_flScale = flScale;
	data.m_fFlags  = bInSlime ? ss::kEffectFlagWaterInSlime : 0;

	DispatchEffect( "watersplash", data );
}

//-----------------------------------------------------------------------------
// A muzzle flash at a point, pointed somewhere.
//
// THE REASON THIS ENTITY COULD CROSS AND ITS NEIGHBOURS COULD NOT. Every other
// row in the particle/trail/smoke family delivers its effect by BEING a
// networked entity whose stock client class runs an emitter, and a managed
// entity never appears on the wire as that class. This one is a temp entity
// carrying a position and a set of angles, which the client reads straight out
// of the message — no entity index, nothing to resolve, nothing that can be
// NULL for a managed entity.
//
// The angles arrive as pitch/yaw/roll in the order QAngle stores them, which is
// the order C#'s Vector already holds an entity's angles in.
//-----------------------------------------------------------------------------
void SourceSharp_MuzzleFlashEffect( const float *pOriginXYZ, const float *pAnglesXYZ,
                                    float flScale, int32_t nType )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ || !pAnglesXYZ )
		return;

	ss::Effects()->MuzzleFlash( Vector( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] ),
	                         QAngle( pAnglesXYZ[0], pAnglesXYZ[1], pAnglesXYZ[2] ),
	                         flScale, nType );
}

//-----------------------------------------------------------------------------
// Where the water beneath a point is, and what kind.
//
// One call for three answers, because the trace has to run to give any of them
// and running it twice would let the two answers disagree about the same water.
//-----------------------------------------------------------------------------
int32_t SourceSharp_WaterSurfaceBelow( const float *pStartXYZ, float *pOutXYZ,
                                       int32_t *pOutSlime )
{
	SOURCESHARP_TRACE();

	if ( pOutSlime )
		*pOutSlime = 0;

	if ( !pStartXYZ )
		return 0;

	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );

	// Already under. Answered first and separately, because a downward trace
	// from inside water finds the BOTTOM of it and would read as a surface.
	if ( ss::UTIL_PointContents( vecStart ) & MASK_WATER )
		return -1;

	trace_t tr;
	ss::UTIL_TraceLine( vecStart, vecStart - Vector( 0, 0, 4096 ),
	                ( MASK_WATER | MASK_SOLID_BRUSHONLY ), NULL,
	                COLLISION_GROUP_NONE, &tr );

	if ( tr.fraction >= 1.0f )
		return 0;

	if ( pOutXYZ )
	{
		pOutXYZ[0] = tr.endpos.x;
		pOutXYZ[1] = tr.endpos.y;
		pOutXYZ[2] = tr.endpos.z;
	}

	if ( pOutSlime )
		*pOutSlime = ( tr.contents & CONTENTS_SLIME ) ? 1 : 0;

	// A trace carrying MASK_WATER stops on the floor as readily as on the
	// water, so "hit something" is not "found water". The contents say which.
	return ( tr.contents & MASK_WATER ) ? 1 : 0;
}

//-----------------------------------------------------------------------------
// A material, by name, with nothing handed back.
//
// The neighbour of SourceSharp_PrecacheModel and deliberately NOT the same
// call. A model index is a number both sides of the wire agree on, which is why
// that one answers with one; a material is only ever a name the renderer
// resolves at draw time, and there is no index to give. Returning something
// here would have invited a caller to network it.
//
// skybox_swapper is the first caller: swapping a sky means having loaded the
// six faces of the new one before the convar changes, or the first frame after
// the swap draws a checkerboard.
//-----------------------------------------------------------------------------
void SourceSharp_PrecacheMaterial( const char *pMaterialName )
{
	SOURCESHARP_TRACE();

	if ( !pMaterialName || !pMaterialName[0] )
		return;

	PrecacheMaterial( pMaterialName );
}

//-----------------------------------------------------------------------------
// A particle system, by name, with nothing handed back.
//
// The third precache and the one that had been missing. A particle system is
// neither a model nor a material: it goes in its own string table, and the
// client resolves a name against the .pcf files the manifest loaded. Nothing
// else here can stand in for it.
//
// WHY IT WAS MISSING, which is worth recording because the symptom is silence.
// Stock precaches the fire systems from CFire::Precache. env_fire is a C# class
// in this build and LINK_ENTITY_TO_CLASS( env_fire, CFire ) is inside
// `#ifndef SOURCESHARP`, so no CFire is ever constructed and that function has
// not run since the day the classname moved. Nothing warned: an unprecached
// particle system does not refuse, it simply is not in the table the client
// looks in.
//
// ss_firesmoke is the first caller, and it precaches all eight fire systems
// rather than the one its size picked -- which one a fire needs depends on a
// spawnflag and on a size that a map can change, and precache time is before
// either is known.
//-----------------------------------------------------------------------------
void SourceSharp_PrecacheParticleSystem( const char *pszName )
{
	SOURCESHARP_TRACE();

	if ( !pszName || !pszName[0] )
		return;

	PrecacheParticleSystem( pszName );
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
// The world's one game-logic key (Phase 12).
//
// worldspawn is a permanent native adapter — it is edict 0, the engine names
// it by index — but its KEYS are C#'s to read. Of the thirteen keys CWorld parses --
// nine DEFINE_KEYFIELDs and four handled in KeyValue --, exactly one drives a game decision rather than engine plumbing:
// chaptertitle, which stock spends in CWorld::Precache on a native
// env_message. env_message is managed here, so C# reads the key at level init
// through this slot and owns the announcement; see ChapterTitle.cs.
//
// Returns a pooled string that lives as long as the level, or "" when the map
// set no title or the world does not exist yet. Never NULL, so the managed
// marshaller has nothing to special-case.
//-----------------------------------------------------------------------------
const char *SourceSharp_WorldChapterTitle( void )
{
	CWorld *pWorld = GetWorldEntity();
	if ( !pWorld )
		return "";

	string_t isz = pWorld->GetChapterTitle();
	return ( isz == NULL_STRING ) ? "" : STRING( isz );
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

//-----------------------------------------------------------------------------
// A surfaceprop by name, or -1. What a map key naming a KIND of surface —
// func_breakable_surf's surfacetype, and nothing else yet — resolves through.
//-----------------------------------------------------------------------------
int32_t SourceSharp_SurfacePropIndex( const char *pszName )
{
	SOURCESHARP_TRACE();

	if ( !physprops || !pszName || !pszName[0] )
		return -1;

	return physprops->GetSurfaceIndex( pszName );
}

//-----------------------------------------------------------------------------
// The game material of a surfaceprop — 'Y' glass, 'W' wood, 'C' concrete.
//
// Here so C# can SAY what it resolved. A surfaceprop index is opaque and
// build-dependent; the letter is the thing a reader and an assertion can both
// check, and it is the same letter env_player_surface_trigger already matches
// ground against.
//-----------------------------------------------------------------------------
int32_t SourceSharp_SurfacePropGameMaterial( int32_t nSurfaceProp )
{
	SOURCESHARP_TRACE();

	if ( !physprops || nSurfaceProp < 0 )
		return 0;

	surfacedata_t *pSurface = physprops->GetSurfaceData( nSurfaceProp );

	return pSurface ? pSurface->game.material : 0;
}

//-----------------------------------------------------------------------------
// The engine's own bullet impact at a point.
//
// CBaseEntity::ImpactTrace's body, with one change: the surfaceprop is a
// PARAMETER. Stock reads it off the trace, which is why a shot into the back of
// a pane of glass leaves a concrete decal and a concrete noise — the back of the
// pane really is nodraw. A caller that knows better says so; a caller that does
// not passes a negative and gets stock's answer.
//
// NOTHING IS TRACED HERE, deliberately. The client re-traces start->origin
// itself in fx_impact.cpp's Impact() to find the plane it decals, and the SOUND
// — the half this call exists for — is chosen from the surfaceprop the server
// SENT rather than from what that trace found: PlayImpactSound reads
// nServerSurfaceProp first and only falls back to the trace. A server-side trace
// would therefore buy nothing and cost the one thing that matters here — a
// breakable surface stops being solid to MASK_SHOT the moment its first pane
// goes, so the re-trace would sail through the pane the shot just hit and put
// the impact on the wall behind it.
//-----------------------------------------------------------------------------
void SourceSharp_ImpactEffect( const float *pStartXYZ, const float *pEndXYZ,
                               int32_t nSurfaceProp, int32_t nDamageType,
                               uint32_t hEntity )
{
	SOURCESHARP_TRACE();

	if ( !pStartXYZ || !pEndXYZ )
		return;

	SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	ss::CEffectData data;
	data.m_vStart       = Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	data.m_vOrigin      = Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );
	data.m_nSurfaceProp = nSurfaceProp;
	data.m_nDamageType  = nDamageType;
	data.m_nHitBox      = 0;

	// The entity index the client attaches the impact to. A server-only entity
	// has none, and 0 — the world — is what stock writes for anything the client
	// cannot name.
	const int nIndex    = ent.SoundSourceIndex();
	data.m_nEntIndex    = ( nIndex == SOUND_FROM_WORLD ) ? 0 : nIndex;

	DispatchEffect( "Impact", data );
}

//-----------------------------------------------------------------------------
// UTIL_TraceEntity's sweep for a managed caller (physics stock parity B7): the
// hull from start to end with a mask and collision group, ignoring the entity
// itself. The end point into pOutEndXYZ; bit 0 of the answer is startsolid and
// bit 1 allsolid. VPhysicsShadowUpdate's "does the player fit here" and "how
// far toward there" are both this.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntityTraceHull( uint32_t hEntity, const float *pStartXYZ, const float *pEndXYZ,
                                     const float *pMinsXYZ, const float *pMaxsXYZ,
                                     int32_t nMask, int32_t nCollisionGroup, float *pOutEndXYZ )
{
	SOURCESHARP_TRACE_HOT();

	if ( !pStartXYZ || !pEndXYZ || !pMinsXYZ || !pMaxsXYZ || !pOutEndXYZ )
		return 0;

	trace_t tr;
	ss::UTIL_TraceHull( Vector( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] ),
	                Vector( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] ),
	                Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ),
	                Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ),
	                (unsigned int)nMask, SourceSharp_ResolveEntity( hEntity ).ToHandleEntity(),
	                nCollisionGroup, &tr );

	pOutEndXYZ[0] = tr.endpos.x;
	pOutEndXYZ[1] = tr.endpos.y;
	pOutEndXYZ[2] = tr.endpos.z;
	return ( tr.startsolid ? 1 : 0 ) | ( tr.allsolid ? 2 : 0 );
}
