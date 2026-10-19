//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what the movement loop asks of the ground ==========//
//
// The player's ground is the one entity in the movement loop that the loop does
// not own, does not create, and cannot choose the hierarchy of. It arrives out
// of a trace, and a trace hits anything in the spatial partition — a brush, a
// physics prop, and since this port, a CStandaloneEntity.
//
// IT USED TO ARRIVE AS A CBaseEntity AND SO USUALLY DID NOT ARRIVE AT ALL.
// CGameMovement::SetGroundEntity resolved the trace with TraceEntity(), which
// answers NULL for everything on the parallel hierarchy, and in this build
// every brush mover — func_plat, func_platrot, func_movelinear, func_train,
// func_tracktrain, func_door — is on that hierarchy. So a player standing on a
// lift had NO ground: not "a ground moving at zero", none. The whole
// `if ( newGround )` tail of SetGroundEntity was skipped, FL_ONGROUND was never
// set, and every later ask — friction, fall damage, the base velocity a moving
// floor hands its rider — read the answer for somebody standing in mid-air.
//
// The fix is that the loop now carries its ground as an IHandleEntity, the one
// base BOTH hierarchies share and the exact type the trace already held. Every
// `ground != NULL` test in gamemovement.cpp keeps meaning what it says, and the
// asks that need more than existence come HERE, where each one is answered once
// for both kinds.
//
// WHY A HEADER AND NOT A METHOD ON IMovementPlayer. These are questions about
// the GROUND, not about the player. Putting them on the player would make each
// player class answer them, which is three implementations of one rule and the
// shape that produced the velocity-home defect (two pieces of state wearing
// one name because two sites decided separately; managed decides it now).
//
// WHY NOT ON IGameEntity. Its membership rule is "what the engine asks", and
// the engine never asks an entity how fast its floor is moving. The SDK's
// movement loop does, so the crossing is a relay in standalone_relays.h, the
// same way the bullet path's and the map loader's are. See that header for the
// rule and for the promise that a relay dies with its callers.
//
//=============================================================================//
#ifndef MOVEMENT_GROUND_H
#define MOVEMENT_GROUND_H
#ifdef _WIN32
#pragma once
#endif

#include "mathlib/vector.h"
#if defined( CLIENT_DLL )
#include "util_shared.h"
#else
// The server's spellings of util_shared.h's two resolvers, on public/ headers
// (P5, lane E): this header is reached from server TUs on the R5 include set.
#include "gametrace.h"
#include "ss_entityhandle.h"
#endif

// The third arm of the partition. Both DLLs define it; see the header.
#include "world_subject.h"
#include "trace_hit.h"   // ss::TraceHit: the IHandleEntity the engine stored in trace_t::m_pEnt

#if !defined( CLIENT_DLL )
#include "standalone_relays.h"
#endif

//-----------------------------------------------------------------------------
// What a downward trace found to stand on, or NULL.
//
// trace_t::m_pEnt is already an IHandleEntity, so this returns the field itself
// and the whole job is deciding whether what it names is an ENTITY.
//
// A STATIC PROP IS NOT, and keeping that is the reason this is a function
// rather than `pm->m_pEnt`. The engine puts static props in the same partition
// and reports them in the same field, but they have no entity behind them at
// all -- EntityFromEntityHandle asks staticpropmgr and answers NULL. Stock has
// therefore always reported a player standing on one as having NO ground,
// which reads as standing on the world, and handing the raw field back would
// silently change that on every map with a prop_static you can walk on.
//
// The order after that is the SDK first, exactly as CTraceFilterSimple
// resolves: an entity that answers GetBaseEntity() is a CBaseEntity and nothing
// else needs asking. Only when that answers NULL is the second hierarchy tried,
// so no stock path changes shape and the added arm costs one virtual call on
// the frames where the first arm already said no.
//
//-----------------------------------------------------------------------------
// THE WORLD IS ASKED FIRST, AND THIS IS THE MOST EXPENSIVE SITE IN THE WHOLE
// TRACE CENSUS (plan_cworld_trace.md T1).
//
// The two arms below are the two arms of the partition, and the world is on
// NEITHER of them the day CWorld un-derives (T3). With no third arm this
// function returns NULL for a player standing on level geometry -- which is to
// say STANDING ON THE FLOOR BECOMES STANDING ON NOTHING, for every player and
// every prop on every map. No ground entity, so no FL_ONGROUND, so no friction,
// no fall damage, and no base velocity. Not a crash and not a NULL dereference:
// the game simply stops having floors.
//
// It is worth spelling out that this site is written ENTIRELY IN NULL TESTS.
// The census bucket it falls into is "asks existence", which the plan reasoned
// needs nothing because m_pEnt is non-NULL whatever the world's class -- and
// that reasoning is correct about m_pEnt and wrong about this function, because
// what this function tests is not m_pEnt but what m_pEnt RESOLVES TO.
//
// ASKED FIRST RATHER THAN LAST, deliberately. Last would be a fallback that
// runs only when both existing arms answer NULL, which is never until T3 lands
// -- dead code, first exercised on the day it would first be found wrong.
// First, it runs on every world hit in every frame from the moment it lands,
// and it returns exactly what the SDK arm below returns today, because today
// the world IS a CBaseEntity and that arm finds it too. Same answer, different
// route, live now.
//
// A STATIC PROP STILL ANSWERS NULL. SourceSharp_IsTheWorld is an identity
// compare against one pointer, so it says false for a static prop exactly as
// EntityFromEntityHandle's staticpropmgr question does, and stock's "standing
// on a prop_static is standing on nothing" survives unchanged.
//-----------------------------------------------------------------------------
inline IHandleEntity *MovementGroundFromTrace( const trace_t &tr )
{
	if ( !tr.m_pEnt )
		return NULL;

	if ( SourceSharp_IsTheWorld( ss::TraceHit( tr ) ) )
		return ss::TraceHit( tr );

	// ONE ARM. `TraceEntity( tr )` was here first and answered a constant NULL
	// -- it bottoms out in GetBaseEntity(), and every implementation of that in
	// both DLLs returns NULL -- so the server's second arm was the whole
	// question and the CLIENT had no arm at all: a player standing on a managed
	// entity resolved to no ground on the client. TraceGameEntity now answers on
	// both sides.
#if defined( CLIENT_DLL )
	if ( TraceGameEntity( tr ) != NULL )
#else
	if ( ss::TraceGameEntity( tr ) != NULL )
#endif
		return ss::TraceHit( tr );

	return NULL;
}

//-----------------------------------------------------------------------------
// How fast the ground is moving, in world space.
//
// Zero for no ground, which is the same answer as a ground standing still and
// is deliberately not distinguished: every caller adds it to a base velocity or
// compares its z against zero, and neither has anything different to do with
// "there is no floor" than with "the floor is not moving".
//
// The SDK arm is CBaseEntity::GetAbsVelocity, unchanged. The parallel arm goes
// through the relay, which answers the velocity managed holds on the entity
// (the object's while it has one, its own member otherwise; standalone_relays.h).
//-----------------------------------------------------------------------------
inline Vector MovementGroundVelocity( IHandleEntity *pGround )
{
	if ( !pGround )
		return vec3_origin;


#if !defined( CLIENT_DLL )
	if ( IGameEntity *pGameEntity = ss::GameEntityFromEntityHandle( pGround ) )
		return StandaloneEntity_Velocity( pGameEntity );
#endif

	return vec3_origin;
}

//-----------------------------------------------------------------------------
// Whether landing on it should hurt less because it gave under the weight.
//
// FALSE for a standalone ground, and that is an answer rather than a gap:
// CBaseEntity::IsFloating is EFL_TOUCHING_FLUID plus a density lookup on the
// physics object's material, and both of those are services of the SDK's
// physics bridge that this hierarchy deliberately does not take (see
// CStandaloneEntity's "Being simulated" note — the object's game data is NULL
// and the whole bridge skips it). A managed brush lift is not floating in
// water; nothing in this mod makes one that could be. If something ever does,
// it will need the water half of that bridge first, and this is where the ask
// will be waiting.
//-----------------------------------------------------------------------------
inline bool MovementGroundIsFloating( IHandleEntity *pGround )
{
	if ( !pGround )
		return false;


	return false;
}

#endif // MOVEMENT_GROUND_H
