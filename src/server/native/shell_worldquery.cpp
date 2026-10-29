//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON e  a synchronous mid-step answer
// World queries for the emitter entities: what is within a radius, what an
// entity is, and the engine-side halves of damage and small effects. Managed
// makes the decisions; this is what needs the engine's spatial data, collision
// code and randomness.

#include "ss_util.h"
#include "ss_shareddefs.h"

#include "ss_recipient_filter.h"
#include "ss_world.h"

#include "shell_standalone_player.h"
#include "standalone_movetype.h"

#include "shell_diagnostics.h"

#include "mathlib_port.h"

#include "model_types.h"
#include "ss_physics_shared.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"

#include "ss_takedamageinfo.h"

// util.h's helpers, defined in our own TUs.

#include "trace_hit.h"

// Every live standalone entity; gEntList's traversals skip them.
#include "standalone_list.h"
#include "server_collision.h"
#include "server_entity_resolver.h"
#include "server_entities.h"
#include "server_entity_resolver.h"

// Everything within a radius, unfiltered: managed decides what to act on. The
// count is how many were written, so a full array means there may be more.
int32_t CServerEntities::EntitiesInSphere(const float* pCenterXYZ, float flRadius, uint32_t* pOutHandles, int32_t nMax)
{
	// Hot: driven by managed thinks, many times per tick.
	SOURCESHARP_TRACE_HOT();

	if (!pCenterXYZ || !pOutHandles || nMax <= 0)
		return 0;

	const Vector vecCenter(pCenterXYZ[0], pCenterXYZ[1], pCenterXYZ[2]);

	int32_t nCount = 0;

	// A registry walk, not a partition query: a standalone entity that is neither
	// solid nor a trigger is not in the partition. Tests the world-space AABB, and
	// includes server-only entities (an env_fire must find the next one).
	const CStandaloneList standalones = StandaloneEntities(m_Entities.Context());

	for (CStandaloneEntity* pStandalone : standalones)
	{
		if (nCount >= nMax)
			break;

		Vector vecMins, vecMaxs;
		pStandalone->WorldSpaceSurroundingBounds(&vecMins, &vecMaxs);

		// collisionutils.cpp's IsBoxIntersectingSphere, bit for bit.
		if (!(ss_mathlib::CalcSqrDistanceToAABB(vecMins, vecMaxs, vecCenter) < flRadius * flRadius))
			continue;

		pOutHandles[nCount++] = SourceSharp_HandleFromAnyEntity(pStandalone);
	}

	return nCount;
}

// What an entity is, as one answer: one crossing per candidate a radius query
// returns, rather than four.
int32_t CServerEntities::EntityGetState(uint32_t hEntity, int32_t* pOutMoveType)
{
	if (pOutMoveType)
		*pOutMoveType = SOURCESHARP_MOVETYPE_NONE;

	const SourceSharpEntity_t ent = m_Entities.Resolve(hEntity);
	if (!ent.IsValid())
		return 0;

	if (ent.pStandalone)
	{
		int32_t nStandaloneFlags = 0;

		if (ent.pStandalone->GetSolid() != SOLID_NONE && (ent.pStandalone->GetSolidFlags() & FSOLID_NOT_SOLID) == 0)
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_SOLID;

		if (ent.pStandalone->GetPhysics() != NULL)
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PHYSICS;

		// A player answers its own movetype; anything else with a physics object
		// the one managed wrote into its block (standalone_movetype.h).
		if (pOutMoveType)
		{
			CStandalonePlayer* pStandalonePlayer =
				ent.pStandalone->IsPlayer() ? SourceSharp_PlayerOf(ent.pStandalone) : NULL;

			*pOutMoveType = StandaloneMoveType_Report(
				pStandalonePlayer != NULL, pStandalonePlayer ? (int)pStandalonePlayer->MvGetMoveType() : 0,
				ent.pStandalone->GetPhysics() != NULL, ent.pStandalone->Block().Head.MoveType);
		}

		// Every standalone entity is offered damage; managed decides what lands.
		nStandaloneFlags |= SOURCESHARP_ENTSTATE_TAKESDAMAGE;

		if (ent.pStandalone->IsPlayer())
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PLAYER;

		// Only a player has ground on this hierarchy.
		if (ent.pStandalone->IsPlayer())
		{
			CStandalonePlayer* pStandalonePlayer = SourceSharp_PlayerOf(ent.pStandalone);

			if (pStandalonePlayer->MvGetFlags() & FL_ONGROUND)
				nStandaloneFlags |= SOURCESHARP_ENTSTATE_ONGROUND;
		}

		if (StandaloneHierarchy_IsParented(ent.pStandalone))
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PARENTED;

		return nStandaloneFlags;
	}

	// No SDK hierarchy remains, so a handle that is not standalone answers nothing.
	return 0;
}

// The collision box as offsets from the entity's origin (world-space AABB minus
// origin). Offsets, not a size: most props have their origin at the base.
void CServerCollision::EntityGetBounds(uint32_t hEntity, float* pOutMinsXYZ, float* pOutMaxsXYZ)
{
	// Untraced: [SuppressGCTransition].
	if (pOutMinsXYZ)
		pOutMinsXYZ[0] = pOutMinsXYZ[1] = pOutMinsXYZ[2] = 0.0f;
	if (pOutMaxsXYZ)
		pOutMaxsXYZ[0] = pOutMaxsXYZ[1] = pOutMaxsXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = m_Entities.Resolve(hEntity);
	if (!ent.IsValid())
		return;

	Vector vecMins, vecMaxs;
	ent.Bounds(&vecMins, &vecMaxs);

	if (pOutMinsXYZ)
	{
		pOutMinsXYZ[0] = vecMins.x;
		pOutMinsXYZ[1] = vecMins.y;
		pOutMinsXYZ[2] = vecMins.z;
	}

	if (pOutMaxsXYZ)
	{
		pOutMaxsXYZ[0] = vecMaxs.x;
		pOutMaxsXYZ[1] = vecMaxs.y;
		pOutMaxsXYZ[2] = vecMaxs.z;
	}
}
