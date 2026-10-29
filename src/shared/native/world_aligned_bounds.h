//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Collision bounds placed in the world without a collision property, following
// CCollisionProperty::CollisionAABBToWorldAABB: a turned entity-space box is rotated and
// re-bounded, not just offset.

#pragma once

#include "const.h"
#include "mathlib_port.h"

// A local box placed at an origin. Only correct when the box is not rotated: the owner's
// angles are zero or its bounds are world-aligned.
inline void SourceSharp_BoundsToWorld(const Vector& vecLocalMins, const Vector& vecLocalMaxs, const Vector& vecOrigin,
									  Vector* pWorldMins, Vector* pWorldMaxs)
{
	VectorAdd(vecLocalMins, vecOrigin, *pWorldMins);
	VectorAdd(vecLocalMaxs, vecOrigin, *pWorldMaxs);
}

// CCollisionProperty::IsBoundsDefinedInEntitySpace: a box turns with its owner unless it
// is SOLID_BBOX, SOLID_NONE or forced world-aligned.
inline bool SourceSharp_BoundsInEntitySpace(int nSolidType, int nSolidFlags)
{
	return (nSolidFlags & FSOLID_FORCE_WORLD_ALIGNED) == 0 && nSolidType != SOLID_BBOX && nSolidType != SOLID_NONE;
}

// The box the world sees, rotated and re-bounded when it is in entity space and turned.
inline void SourceSharp_CollisionBoundsToWorld(int nSolidType, int nSolidFlags, const Vector& vecLocalMins,
											   const Vector& vecLocalMaxs, const Vector& vecOrigin,
											   const QAngle& angAngles, Vector* pWorldMins, Vector* pWorldMaxs)
{
	if (!SourceSharp_BoundsInEntitySpace(nSolidType, nSolidFlags) || angAngles == vec3_angle)
	{
		SourceSharp_BoundsToWorld(vecLocalMins, vecLocalMaxs, vecOrigin, pWorldMins, pWorldMaxs);
		return;
	}

	matrix3x4_t matCollisionToWorld;
	ss_mathlib::AngleMatrix(angAngles, vecOrigin, matCollisionToWorld);
	ss_mathlib::TransformAABB(matCollisionToWorld, vecLocalMins, vecLocalMaxs, *pWorldMins, *pWorldMaxs);
}

// CCollisionProperty::WorldSpaceTriggerBounds, in place: with FSOLID_USE_TRIGGER_BOUNDS the
// sides grow by the bloat and the top by half, or every side when uniform.
inline void SourceSharp_TriggerBounds(int nSolidFlags, int nBloat, bool bUniform, Vector* pWorldMins,
									  Vector* pWorldMaxs)
{
	if ((nSolidFlags & FSOLID_USE_TRIGGER_BOUNDS) == 0)
		return;

	const float flBloat = (float)nBloat;
	pWorldMins->x -= flBloat;
	pWorldMins->y -= flBloat;
	pWorldMaxs->x += flBloat;
	pWorldMaxs->y += flBloat;

	if (bUniform)
	{
		pWorldMins->z -= flBloat;
		pWorldMaxs->z += flBloat;
	}
	else
	{
		pWorldMaxs->z += flBloat * 0.5f;
	}
}

// The tail of CCollisionProperty::ComputeSurroundingBox, in place: the surrounding box takes
// in the trigger box.
inline void SourceSharp_SurroundingBounds(int nSolidFlags, int nBloat, bool bUniform, Vector* pWorldMins,
										  Vector* pWorldMaxs)
{
	if ((nSolidFlags & FSOLID_USE_TRIGGER_BOUNDS) == 0)
		return;

	Vector vecTriggerMins = *pWorldMins, vecTriggerMaxs = *pWorldMaxs;
	SourceSharp_TriggerBounds(nSolidFlags, nBloat, bUniform, &vecTriggerMins, &vecTriggerMaxs);
	for (int i = 0; i < 3; ++i)
	{
		(*pWorldMins)[i] = vecTriggerMins[i] < (*pWorldMins)[i] ? vecTriggerMins[i] : (*pWorldMins)[i];
		(*pWorldMaxs)[i] = vecTriggerMaxs[i] > (*pWorldMaxs)[i] ? vecTriggerMaxs[i] : (*pWorldMaxs)[i];
	}
}
