//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Questions the movement loop asks of the ground, which arrives from a trace as an
// IHandleEntity: a brush, a physics prop or a standalone entity. Each is answered once
// for both hierarchies.
#pragma once

#if defined(CLIENT_DLL)

#include "slot_table.h"
#else

#include "ss_entityhandle.h"
#endif

#include "world_subject.h"
#include "trace_hit.h"

#if !defined(CLIENT_DLL)
#include "standalone_relays.h"
#endif

// What a downward trace found to stand on, or NULL. A static prop is not an entity, so,
// as in stock, standing on one reads as standing on nothing. The world is checked first.
#if defined(CLIENT_DLL)
inline IHandleEntity* MovementGroundFromTrace(const trace_t& tr)
{
	if (!tr.m_pEnt)
		return NULL;

	if (SourceSharp_IsTheWorld(ss::TraceHit(tr)))
		return ss::TraceHit(tr);

	// The client asks the slot table: its C_StandaloneEntity is no IGameEntity, so
	// TraceGameEntity answers NULL there.
	if (SourceSharp_ClientShellFromTraceHit(ss::TraceHit(tr)) != NULL)
		return ss::TraceHit(tr);

	return NULL;
}

// How fast the ground is moving, in world space. Zero: the client's ground is never a
// game entity it can ask.
inline Vector MovementGroundVelocity(IHandleEntity* pGround)
{
	return vec3_origin;
}
#else
// pWorld is the level's world (NULL between levels), pStaticProps the engine's.
inline IHandleEntity* MovementGroundFromTrace(const trace_t& tr, const IHandleEntity* pWorld,
											  IStaticPropMgrServer* pStaticProps)
{
	if (!tr.m_pEnt)
		return NULL;

	if (SourceSharp_IsTheWorldHandle(ss::TraceHit(tr), pWorld))
		return ss::TraceHit(tr);

	if (ss::TraceGameEntity(pStaticProps, tr) != NULL)
		return ss::TraceHit(tr);

	return NULL;
}

// How fast the ground is moving, in world space. Zero for no ground.
inline Vector MovementGroundVelocity(IHandleEntity* pGround, IStaticPropMgrServer* pStaticProps)
{
	if (!pGround)
		return vec3_origin;

	if (IGameEntity* pGameEntity = ss::GameEntityFromEntityHandle(pStaticProps, pGround))
		return StandaloneEntity_Velocity(pGameEntity);

	return vec3_origin;
}
#endif

// Whether landing on it should hurt less because it gave under the weight. Always false:
// stock's IsFloating needs the SDK physics bridge, which standalone entities skip.
inline bool MovementGroundIsFloating(IHandleEntity* pGround)
{
	if (!pGround)
		return false;

	return false;
}
