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
// The one caller today never rotates -- SourceSharp_CreateSmokeVolume sets an
// origin and no angles, and nothing else writes them -- so it takes the first
// branch. This header exists so that the day something does rotate, the
// precondition is written down where the call is, with a test that says what
// goes wrong instead of a silently wrong bounding box that makes a smoke volume
// vanish at certain camera angles.
//
// Engine-free: two vector adds. world_aligned_bounds_test.cpp checks them
// against the real mathlib TransformAABB rather than against a restatement of
// them.
//
//=============================================================================//

#pragma once

#include "mathlib/vector.h"

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
