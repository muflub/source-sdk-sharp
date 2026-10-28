//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a box put into the world, without a collision property //
//
// WHAT THIS IS AND WHY IT IS NOT INLINED AT ITS ONE CALL SITE.
//
// A de-entified effect that has a VOLUME -- func_smokevolume is the first --
// used to ask its collision property where that volume was:
//
//     CollisionProp()->WorldSpaceAABB( &vWorldMins, &vWorldMaxs );
//
// There is no collision property on something that is not an entity, and the
// replacement looks trivially like `mins + origin`. It is trivially that, and
// ONLY UNDER A PRECONDITION, which is exactly the shape of thing this project
// has been bitten by before: `GetAbsAngles() -> GetCollisionAngles()` was given
// to three agents as an identity and is not one.
//
// So here is the real rule, from CCollisionProperty::CollisionAABBToWorldAABB:
//
//     if ( !IsBoundsDefinedInEntitySpace() || GetCollisionAngles() == vec3_angle )
//         world = entity + GetCollisionOrigin()
//     else
//         world = TransformAABB( CollisionToWorldTransform(), entity )
//
// The addition is the answer WHENEVER THE ANGLES ARE IDENTITY, whatever the
// solid type; off identity it is the answer only for a world-aligned box, and
// for anything else the box has to be rotated and re-bounded, which is a
// strictly LARGER box rather than a shifted one.
//
// SourceSharp_CollisionBoundsToWorld is that whole rule, for the standalone
// shells' partition entry, surrounding bounds and trigger bounds. They took the
// addition alone, so a rotated prop was in the partition as its box UNROTATED:
// traces and touches clipped to that box, and dm_lockdown's desk (yaw 135, 112
// units long) could be traced through at both ends, where stock's weapon_357
// lands on it (tools/stock-ab, 2026-10-26).
//
// It turns the box with mathlib_port.h's AngleMatrix and TransformAABB, which
// server.so links where it links no mathlib. world_aligned_bounds_test.cpp
// checks both functions against the real mathlib TransformAABB rather than
// against a restatement of them.
//
//=============================================================================//

#pragma once

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"
#include "const.h"
#include "mathlib_port.h"

//-----------------------------------------------------------------------------
// A local box placed at an origin, with no rotation.
//
// PRECONDITION: the box is not rotated -- the owner's angles are vec3_angle, or
// its bounds are world-aligned. See the header note. Violate it and the answer
// is too SMALL, which reads as an effect that disappears when the camera moves
// rather than as an error.
//-----------------------------------------------------------------------------
inline void SourceSharp_BoundsToWorld( const Vector &vecLocalMins,
                                       const Vector &vecLocalMaxs,
                                       const Vector &vecOrigin,
                                       Vector *pWorldMins, Vector *pWorldMaxs )
{
	VectorAdd( vecLocalMins, vecOrigin, *pWorldMins );
	VectorAdd( vecLocalMaxs, vecOrigin, *pWorldMaxs );
}

//-----------------------------------------------------------------------------
// CCollisionProperty::IsBoundsDefinedInEntitySpace: a box turns with its owner
// unless it is SOLID_BBOX, SOLID_NONE or forced world-aligned.
//-----------------------------------------------------------------------------
inline bool SourceSharp_BoundsInEntitySpace( int nSolidType, int nSolidFlags )
{
	return ( nSolidFlags & FSOLID_FORCE_WORLD_ALIGNED ) == 0
		&& nSolidType != SOLID_BBOX && nSolidType != SOLID_NONE;
}

//-----------------------------------------------------------------------------
// CCollisionProperty::CollisionAABBToWorldAABB: the box the world sees, rotated
// and re-bounded when it is in entity space and the owner is turned.
//-----------------------------------------------------------------------------
inline void SourceSharp_CollisionBoundsToWorld( int nSolidType, int nSolidFlags,
                                                const Vector &vecLocalMins,
                                                const Vector &vecLocalMaxs,
                                                const Vector &vecOrigin,
                                                const QAngle &angAngles,
                                                Vector *pWorldMins, Vector *pWorldMaxs )
{
	if ( !SourceSharp_BoundsInEntitySpace( nSolidType, nSolidFlags ) || angAngles == vec3_angle )
	{
		SourceSharp_BoundsToWorld( vecLocalMins, vecLocalMaxs, vecOrigin, pWorldMins, pWorldMaxs );
		return;
	}

	matrix3x4_t matCollisionToWorld;
	ss_mathlib::AngleMatrix( angAngles, vecOrigin, matCollisionToWorld );
	ss_mathlib::TransformAABB( matCollisionToWorld, vecLocalMins, vecLocalMaxs, *pWorldMins, *pWorldMaxs );
}
