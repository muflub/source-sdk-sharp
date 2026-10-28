//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// The arithmetic of WorldSpaceCenter, over the answers an ICollideable gives.
//
// CCollisionProperty::WorldSpaceCenter() is CollisionToWorldSpace( OBBCenter() )
// (collisionproperty.h), and both halves read only what the ICollideable
// interface already carries. This header is that computation with the values
// unwrapped to floats, so the rule can be pinned by a test binary with no
// mathlib and no engine in it — the same reason bone_cache_key.h is shaped the
// way it is. The adapter that asks a real ICollideable is
// collideable_center.h, beside this one; both are shared, and were in
// game/client/ until a server caller needed them.
//
// The identity, checked against collisionproperty.h/.cpp rather than assumed:
//
//   OBBCenter()              = VectorLerp( OBBMins(), OBBMaxs(), 0.5 )
//   CollisionToWorldSpace(p) = p + GetCollisionOrigin()          [add arm]
//                              when !IsBoundsDefinedInEntitySpace()
//                                   or GetCollisionAngles() == vec3_angle
//                            = VectorTransform( p, CollisionToWorldTransform() )
//                              otherwise                         [transform arm]
//
// The add arm's guard here is "angles are zero" ALONE, and that selects the
// same arm in every reachable case, because world-aligned bounds already
// answer vec3_angle from GetCollisionAngles() — that is
// CCollisionProperty::GetCollisionAngles() in collisionproperty.cpp:
//
//   bounds world-aligned             -> angles read as zero -> add        same
//   entity-space, entity unrotated   -> angles are zero     -> add        same
//   entity-space, entity rotated     -> angles are not zero -> transform  same
//
// (Entity-space-and-unrotated takes the add arm in stock too, through the
// guard's second disjunct.)
//
// Both arms are spelled term for term in stock's own evaluation order:
// VectorLerp is src1 + (src2 - src1) * t, and VectorTransform is
// DotProduct( in, row ) + row[3].
//
// gtest: sourcesharp/test/collideable_center_test.cpp.
//
//=============================================================================//
#pragma once

//-----------------------------------------------------------------------------
// flMins/flMaxs: OBBMins()/OBBMaxs(). bAnglesAreZero: GetCollisionAngles()
// compared equal to vec3_angle. flOrigin: GetCollisionOrigin(). flXform: the
// 3x4 CollisionToWorldTransform(), row-major (matrix3x4_t::Base()); read only
// when bAnglesAreZero is false, exactly as stock reads it.
//-----------------------------------------------------------------------------
inline void CollideableCenter_Compute( const float flMins[3], const float flMaxs[3],
                                       bool bAnglesAreZero,
                                       const float flOrigin[3],
                                       const float flXform[12],
                                       float flOut[3] )
{
	// OBBCenter: VectorLerp( mins, maxs, 0.5f ), in VectorLerp's own form.
	float flCenter[3];
	for ( int i = 0; i < 3; ++i )
	{
		flCenter[i] = flMins[i] + ( flMaxs[i] - flMins[i] ) * 0.5f;
	}

	if ( bAnglesAreZero )
	{
		// The add arm. The transform is deliberately not read.
		for ( int i = 0; i < 3; ++i )
		{
			flOut[i] = flCenter[i] + flOrigin[i];
		}
		return;
	}

	// The transform arm: VectorTransform( center, xform ), which is
	// DotProduct( center, row ) + row[3] per row, in that order.
	for ( int i = 0; i < 3; ++i )
	{
		const float *flRow = &flXform[i * 4];
		flOut[i] = flCenter[0] * flRow[0]
		         + flCenter[1] * flRow[1]
		         + flCenter[2] * flRow[2]
		         + flRow[3];
	}
}
