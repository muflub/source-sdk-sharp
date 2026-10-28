//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// WorldSpaceCenter asked of an ICollideable, for entities with no CBaseEntity.
// Not a drop-in for the virtual: CBeam and C_RopeKeyframe override it, so
// callers with a base entity use its own WorldSpaceCenter.
#pragma once

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"
#include "engine/ICollideable.h"
#include "collideable_center_math.h"

inline Vector UTIL_CollideableWorldSpaceCenter( const ICollideable *pCollide )
{
	const bool bAnglesAreZero = ( pCollide->GetCollisionAngles() == vec3_angle );

	// The transform is read only on the rotated arm, as CollisionToWorldSpace does.
	const float *pXform = bAnglesAreZero ? NULL : pCollide->CollisionToWorldTransform().Base();

	Vector vecResult;
	CollideableCenter_Compute( pCollide->OBBMins().Base(), pCollide->OBBMaxs().Base(),
	                           bAnglesAreZero,
	                           pCollide->GetCollisionOrigin().Base(),
	                           pXform,
	                           vecResult.Base() );
	return vecResult;
}
