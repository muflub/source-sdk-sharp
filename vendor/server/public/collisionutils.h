//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: Common collision utility methods
//
// $Header: $
// $NoKeywords: $
//=============================================================================//

#ifndef COLLISIONUTILS_H
#define COLLISIONUTILS_H

#include "tier0/platform.h"

#ifdef _WIN32
#pragma once
#endif

#include "mathlib/ssemath.h"

//-----------------------------------------------------------------------------
// forward declarations
//-----------------------------------------------------------------------------
struct Ray_t;
class Vector;
class Vector2D;
class Vector4D;
struct cplane_t;
class QAngle;
class CBaseTrace;
struct matrix3x4_t;


































#ifdef _X360
// inline version:
#include "mathlib/ssemath.h"
inline bool IsBoxIntersectingBoxExtents( const fltx4 boxCenter1, const fltx4 boxHalfDiagonal1, 
								 const fltx4 boxCenter2, const fltx4 boxHalfDiagonal2 );
#endif



//-----------------------------------------------------------------------------
// 
// IsBoxIntersectingRay
//
// returns true if there's an intersection between box and ray
//
//-----------------------------------------------------------------------------





// On the PC, we can't pass fltx4's in registers like this. On the x360, it is 
// much better if we do.
#ifdef _X360
bool FASTCALL IsBoxIntersectingRay( fltx4 boxMin, fltx4 boxMax, 
								   fltx4 origin, fltx4 delta, fltx4 invDelta, // ray parameters
								   fltx4 vTolerance = LoadZeroSIMD() ///< eg from ReplicateX4(flTolerance)
								   );
#else
#endif













//-----------------------------------------------------------------------------
//
// PointInQuadBarycentric
//
//	Given a point and a quad in a plane return the u and v (barycentric) positions
//  of the point relative to the quad.  The points (v1,v2,v3,v4) should be given
//  in a counter-clockwise order with v1 acting as the primary corner (u=0, v=0).
//  Thus, u0 = v2 - v1, and v0 = v4 - v1.
//
//-----------------------------------------------------------------------------




















//-----------------------------------------------------------------------------
// INLINES
//-----------------------------------------------------------------------------


#ifdef _X360
inline bool IsBoxIntersectingBoxExtents( const fltx4 boxCenter1, const fltx4 boxHalfDiagonal1, 
								 const fltx4 boxCenter2, const fltx4 boxHalfDiagonal2 )
{
	fltx4 vecDelta, vecSize;

	vecDelta = SubSIMD(boxCenter1, boxCenter2);
	vecSize = AddSIMD(boxHalfDiagonal1, boxHalfDiagonal2);

	uint condition;
	XMVectorInBoundsR(&condition, vecDelta, vecSize);
	// we want the top three words to be all 1's ; that means in bounds


	return XMComparisonAllInBounds( condition );
}
#endif


#endif // COLLISIONUTILS_H
