//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Common collision utility methods

#pragma once

// forward declarations
struct Ray_t;
class Vector;
class Vector2D;
class Vector4D;
struct cplane_t;
class QAngle;
class CBaseTrace;
struct matrix3x4_t;

// IsBoxIntersectingRay
// returns true if there's an intersection between box and ray

// much better if we do.

// PointInQuadBarycentric
//	Given a point and a quad in a plane return the u and v (barycentric) positions
//  of the point relative to the quad.  The points (v1,v2,v3,v4) should be given
//  in a counter-clockwise order with v1 acting as the primary corner (u=0, v=0).
//  Thus, u0 = v2 - v1, and v0 = v4 - v1.

// INLINES
