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

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"
#include "const.h"
#include "mathlib_port.h"

// A local box placed at an origin. Only correct when the box is not rotated: the owner's
// angles are zero or its bounds are world-aligned.
inline void SourceSharp_BoundsToWorld( const Vector &vecLocalMins,
                                       const Vector &vecLocalMaxs,
                                       const Vector &vecOrigin,
                                       Vector *pWorldMins, Vector *pWorldMaxs )
{
	VectorAdd( vecLocalMins, vecOrigin, *pWorldMins );
	VectorAdd( vecLocalMaxs, vecOrigin, *pWorldMaxs );
}

// CCollisionProperty::IsBoundsDefinedInEntitySpace: a box turns with its owner unless it
// is SOLID_BBOX, SOLID_NONE or forced world-aligned.
inline bool SourceSharp_BoundsInEntitySpace( int nSolidType, int nSolidFlags )
{
	return ( nSolidFlags & FSOLID_FORCE_WORLD_ALIGNED ) == 0
		&& nSolidType != SOLID_BBOX && nSolidType != SOLID_NONE;
}

// The box the world sees, rotated and re-bounded when it is in entity space and turned.
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
