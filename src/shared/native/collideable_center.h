//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// WorldSpaceCenter, asked of an ICollideable instead of an entity class.
//
// SHARED, and it was in game/client/ until a server caller needed it. The
// arithmetic never was client-specific: WorldSpaceCenter() is
// CollisionProp()->WorldSpaceCenter() on BOTH sides (baseentity_shared.cpp:189,
// c_baseentity.cpp), and GetCollideable() returns that same collision property
// — baseentity.h and c_baseentity.h both return &m_Collision from both.
//
// BUT WorldSpaceCenter is a VIRTUAL, and classes override it away from the
// collision property: CBeam answers the beam's midpoint (beam_shared.h, so on
// both sides) and C_RopeKeyframe answers its origin (c_rope.cpp, client only).
// So this helper is NOT a drop-in for pEnt->WorldSpaceCenter() — while an
// entity has a CBaseEntity / C_BaseEntity, its own virtual stays the authority,
// and callers resolve GetBaseEntity() first. This helper answers only an entity
// the class cannot see (CStandaloneEntity and C_StandaloneEntity implement
// ICollideable directly), where it is the first time the question has an answer
// at all.
//
// The arithmetic and the arm-for-arm identity argument live in
// collideable_center_math.h, where the test binary can reach them without
// mathlib; this adapter only unwraps the interface's answers.
//
//=============================================================================//
#pragma once

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"
#include "engine/ICollideable.h"
#include "collideable_center_math.h"

inline Vector UTIL_CollideableWorldSpaceCenter( const ICollideable *pCollide )
{
	const bool bAnglesAreZero = ( pCollide->GetCollisionAngles() == vec3_angle );

	// The transform is read only on the rotated arm, exactly as
	// CCollisionProperty::CollisionToWorldSpace reads it.
	const float *pXform = bAnglesAreZero ? NULL : pCollide->CollisionToWorldTransform().Base();

	Vector vecResult;
	CollideableCenter_Compute( pCollide->OBBMins().Base(), pCollide->OBBMaxs().Base(),
	                           bAnglesAreZero,
	                           pCollide->GetCollisionOrigin().Base(),
	                           pXform,
	                           vecResult.Base() );
	return vecResult;
}
