//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — mathlib's members, as server.so uses them =========//
//
// server.so links no libmathlib.a. These are the only mathlib functions its
// objects call (an `nm -u` sweep of the link edge, against what the archive
// defines), written out here so the archive can leave the link line, as tier1
// left it for tier1_port.cpp.
//
// WHY A HEADER OF INLINES IN A NAMESPACE, and the global symbols only in
// mathlib_port.cpp: the one way to hold this against stock bit for bit is to
// call both from one binary, on one CPU, with one libm. A binary that links
// libmathlib.a already has the global names; ss_mathlib:: does not collide
// with them, so mathlib_port_stock_test calls each pair side by side.
//
// WHAT IS LEFT OUT ON PURPOSE. Stock asserts s_bMathlibInitialized in most of
// these, and MathLib_Init's job is to pick the SSE or generic sqrt/rsqrt and
// fill the sine and gamma tables. Nothing here reads any of that: SinCos is
// sinf/cosf (math_pfns.h) and the one square root is sqrtf, which stock's
// FastSqrt also is. So there is no initialisation to forget, and server.so no
// longer calls MathLib_Init.
//
// The arithmetic is kept in stock's order and types (the float/double mix in
// VectorAngles included): a value that differed in the last bit would move an
// entity by it, and the stock test would say so.
//=============================================================================//
#ifndef SS_MATHLIB_PORT_H
#define SS_MATHLIB_PORT_H

#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"

#include <math.h>
#include <stdint.h>
#include <string.h>

namespace ss_mathlib
{

// The sines and cosines of a QAngle's three components, in degrees.
struct AngleTrig
{
	float sinPitch, cosPitch;
	float sinYaw, cosYaw;
	float sinRoll, cosRoll;
};

inline AngleTrig TrigOf( const QAngle &angles, bool bWithRoll )
{
	AngleTrig t;
	SinCos( DEG2RAD( angles[YAW] ), &t.sinYaw, &t.cosYaw );
	SinCos( DEG2RAD( angles[PITCH] ), &t.sinPitch, &t.cosPitch );
	if ( bWithRoll )
	{
		SinCos( DEG2RAD( angles[ROLL] ), &t.sinRoll, &t.cosRoll );
	}
	else
	{
		t.sinRoll = 0.0f;
		t.cosRoll = 1.0f;
	}
	return t;
}

// A pitch/yaw/roll in degrees to a rotation: column 0 is forward, 1 left and
// 2 up (yaw about Z, then pitch, then roll about forward); column 3 is zero.
inline void AngleMatrix( const QAngle &angles, matrix3x4_t &m )
{
	const AngleTrig t = TrigOf( angles, true );

	m[0][0] = t.cosPitch * t.cosYaw;
	m[1][0] = t.cosPitch * t.sinYaw;
	m[2][0] = -t.sinPitch;

	const float cosRollCosYaw = t.cosRoll * t.cosYaw;
	const float cosRollSinYaw = t.cosRoll * t.sinYaw;
	const float sinRollCosYaw = t.sinRoll * t.cosYaw;
	const float sinRollSinYaw = t.sinRoll * t.sinYaw;

	m[0][1] = t.sinPitch * sinRollCosYaw - cosRollSinYaw;
	m[1][1] = t.sinPitch * sinRollSinYaw + cosRollCosYaw;
	m[2][1] = t.sinRoll * t.cosPitch;

	m[0][2] = t.sinPitch * cosRollCosYaw + sinRollSinYaw;
	m[1][2] = t.sinPitch * cosRollSinYaw - sinRollCosYaw;
	m[2][2] = t.cosRoll * t.cosPitch;

	for ( int row = 0; row < 3; ++row )
	{
		m[row][3] = 0.0f;
	}
}

// The same rotation, with `position` as the translation column.
inline void AngleMatrix( const QAngle &angles, const Vector &position, matrix3x4_t &m )
{
	// Qualified: argument-dependent lookup also finds mathlib.h's global one.
	ss_mathlib::AngleMatrix( angles, m );
	m[0][3] = position.x;
	m[1][3] = position.y;
	m[2][3] = position.z;
}

// Forward only: roll does not move it.
inline void AngleVectors( const QAngle &angles, Vector *forward )
{
	Assert( forward );
	const AngleTrig t = TrigOf( angles, false );
	forward->x = t.cosPitch * t.cosYaw;
	forward->y = t.cosPitch * t.sinYaw;
	forward->z = -t.sinPitch;
}

// Forward, right and up, each optional. Right is the matrix's left column
// negated, which is why its terms carry stock's explicit -1 factors: the
// products keep their order and so their rounding.
inline void AngleVectors( const QAngle &angles, Vector *forward, Vector *right, Vector *up )
{
	const AngleTrig t = TrigOf( angles, true );
	const float sp = t.sinPitch, cp = t.cosPitch;
	const float sy = t.sinYaw, cy = t.cosYaw;
	const float sr = t.sinRoll, cr = t.cosRoll;

	if ( forward != nullptr )
	{
		forward->x = cp * cy;
		forward->y = cp * sy;
		forward->z = -sp;
	}
	if ( right != nullptr )
	{
		right->x = -1 * sr * sp * cy + -1 * cr * -sy;
		right->y = -1 * sr * sp * sy + -1 * cr * cy;
		right->z = -1 * sr * cp;
	}
	if ( up != nullptr )
	{
		up->x = cr * sp * cy + -sr * -sy;
		up->y = cr * sp * sy + -sr * cy;
		up->z = cr * cp;
	}
}

// A direction to a pitch and yaw in [0, 360), roll zero. Straight up or down
// has no yaw: it reads 0, with pitch 270 for up and 90 for down.
inline void VectorAngles( const Vector &dir, QAngle &angles )
{
	float pitch, yaw;
	if ( dir[0] == 0 && dir[1] == 0 )
	{
		yaw = 0;
		pitch = dir[2] > 0 ? 270 : 90;
	}
	else
	{
		yaw = ( atan2( dir[1], dir[0] ) * 180 / M_PI );
		if ( yaw < 0 )
		{
			yaw += 360;
		}
		const float horizontal = sqrtf( dir[0] * dir[0] + dir[1] * dir[1] );
		pitch = ( atan2( -dir[2], horizontal ) * 180 / M_PI );
		if ( pitch < 0 )
		{
			pitch += 360;
		}
	}
	angles[0] = pitch;
	angles[1] = yaw;
	angles[2] = 0;
}

// The inverse of AngleMatrix for a rotation, into a float[3] of pitch, yaw
// and roll. Near straight up or down (forward's horizontal length under
// 0.001) yaw and roll are the same turn: yaw is read from the left column and
// roll reads 0.
inline void MatrixAngles( const matrix3x4_t &m, float *angles )
{
	const float fx = m[0][0], fy = m[1][0], fz = m[2][0];
	const float lx = m[0][1], ly = m[1][1], lz = m[2][1];
	const float uz = m[2][2];

	const float horizontal = sqrtf( fx * fx + fy * fy );
	angles[0] = RAD2DEG( atan2f( -fz, horizontal ) );
	if ( horizontal > 0.001f )
	{
		angles[1] = RAD2DEG( atan2f( fy, fx ) );
		angles[2] = RAD2DEG( atan2f( lz, uz ) );
	}
	else
	{
		angles[1] = RAD2DEG( atan2f( -lx, ly ) );
		angles[2] = 0;
	}
}

inline void SetIdentityMatrix( matrix3x4_t &m )
{
	for ( int row = 0; row < 3; ++row )
	{
		for ( int col = 0; col < 4; ++col )
		{
			m[row][col] = row == col ? 1.0f : 0.0f;
		}
	}
}

inline void MatrixGetColumn( const matrix3x4_t &m, int column, Vector &out )
{
	out.x = m[0][column];
	out.y = m[1][column];
	out.z = m[2][column];
}

// in * rotation + translation. `in` and `out` must not overlap.
inline void VectorTransform( const float *in, const matrix3x4_t &m, float *out )
{
	Assert( in != out );
	for ( int row = 0; row < 3; ++row )
	{
		out[row] = DotProduct( in, m[row] ) + m[row][3];
	}
}

// The inverse of VectorTransform for an orthonormal matrix: subtract the
// translation, then multiply by the transpose.
inline void VectorITransform( const float *in, const matrix3x4_t &m, float *out )
{
	const float d0 = in[0] - m[0][3];
	const float d1 = in[1] - m[1][3];
	const float d2 = in[2] - m[2][3];
	for ( int col = 0; col < 3; ++col )
	{
		out[col] = d0 * m[0][col] + d1 * m[1][col] + d2 * m[2][col];
	}
}

// Rotate by the transpose (the inverse of a rotation). No translation.
inline void VectorIRotate( const float *in, const matrix3x4_t &m, float *out )
{
	Assert( in != out );
	for ( int col = 0; col < 3; ++col )
	{
		out[col] = in[0] * m[0][col] + in[1] * m[1][col] + in[2] * m[2][col];
	}
}

// The squared distance from a point to an axis-aligned box: zero inside it,
// and per axis only the overshoot past the nearer face counts.
inline float CalcSqrDistanceToAABB( const Vector &mins, const Vector &maxs, const Vector &point )
{
	float sum = 0.0f;
	for ( int axis = 0; axis < 3; ++axis )
	{
		float over = 0.0f;
		if ( point[axis] < mins[axis] )
		{
			over = mins[axis] - point[axis];
		}
		else if ( point[axis] > maxs[axis] )
		{
			over = point[axis] - maxs[axis];
		}
		else
		{
			continue;
		}
		sum += over * over;
	}
	return sum;
}

//-----------------------------------------------------------------------------
// client.so's members (plan_client_novalve C9: libmathlib.a off client.so's
// link line). The set is what the client's live sections leave undefined when
// the archive is taken away, plus what these call themselves. Same rules as
// above: stock's arithmetic order and types, held bit for bit by
// mathlib_port_stock_test.
//-----------------------------------------------------------------------------

// The cross product of two three-float arrays (neither may be the output).
inline void CrossProduct( const float *a, const float *b, float *out )
{
	Assert( a != out && b != out );
	out[0] = a[1] * b[2] - a[2] * b[1];
	out[1] = a[2] * b[0] - a[0] * b[2];
	out[2] = a[0] * b[1] - a[1] * b[0];
}

// The rotation part of a 3x4 applied to a vector (no translation).
inline void VectorRotate( const float *in, const matrix3x4_t &m, float *out )
{
	Assert( in != out );
	for ( int row = 0; row < 3; ++row )
	{
		out[row] = DotProduct( in, m[row] );
	}
}

inline void MatrixSetColumn( const Vector &in, int column, matrix3x4_t &out )
{
	out[0][column] = in.x;
	out[1][column] = in.y;
	out[2][column] = in.z;
}

inline void MatrixCopy( const matrix3x4_t &in, matrix3x4_t &out )
{
	memcpy( out.Base(), in.Base(), sizeof( float ) * 3 * 4 );
}

// The inverse of a rotation and translation: the transposed rotation, and the
// translation carried into the other space. Works in place.
inline void MatrixInvert( const matrix3x4_t &in, matrix3x4_t &out )
{
	if ( &in == &out )
	{
		V_swap( out[0][1], out[1][0] );
		V_swap( out[0][2], out[2][0] );
		V_swap( out[1][2], out[2][1] );
	}
	else
	{
		for ( int row = 0; row < 3; ++row )
		{
			for ( int col = 0; col < 3; ++col )
			{
				out[row][col] = in[col][row];
			}
		}
	}
	const float translation[3] = { in[0][3], in[1][3], in[2][3] };
	for ( int row = 0; row < 3; ++row )
	{
		out[row][3] = -DotProduct( translation, out[row] );
	}
}

// The inverse rotation (the transpose) of AngleMatrix's, translation zero.
inline void AngleIMatrix( const QAngle &angles, matrix3x4_t &m )
{
	const AngleTrig t = TrigOf( angles, true );
	const float sp = t.sinPitch, cp = t.cosPitch;
	const float sy = t.sinYaw, cy = t.cosYaw;
	const float sr = t.sinRoll, cr = t.cosRoll;

	m[0][0] = cp * cy;
	m[0][1] = cp * sy;
	m[0][2] = -sp;
	m[1][0] = sr * sp * cy + cr * -sy;
	m[1][1] = sr * sp * sy + cr * cy;
	m[1][2] = sr * cp;
	m[2][0] = ( cr * sp * cy + -sr * -sy );
	m[2][1] = ( cr * sp * sy + -sr * cy );
	m[2][2] = cr * cp;
	m[0][3] = 0.f;
	m[1][3] = 0.f;
	m[2][3] = 0.f;
}

// Two 3x4 transforms composed (in1 applied after in2), as stock's SSE
// version computes each element: the three products summed as
// p0 + ( p1 + p2 ), then the translation added -- a zero in the rotation
// columns, which turns a negative zero positive, as the vector add did.
inline void ConcatTransforms( const matrix3x4_t &in1, const matrix3x4_t &in2, matrix3x4_t &out )
{
	float result[3][4];
	for ( int row = 0; row < 3; ++row )
	{
		const float a0 = in1[row][0], a1 = in1[row][1], a2 = in1[row][2];
		for ( int col = 0; col < 4; ++col )
		{
			const float sum = a0 * in2[0][col] + ( a1 * in2[1][col] + a2 * in2[2][col] );
			result[row][col] = sum + ( col == 3 ? in1[row][3] : 0.0f );
		}
	}
	memcpy( out.Base(), result, sizeof( result ) );
}

// A unit quaternion to a rotation, translation zero. The first column is
// computed in double, as stock's literals made it.
inline void QuaternionMatrix( const Quaternion &q, matrix3x4_t &m )
{
	m[0][0] = 1.0 - 2.0 * q.y * q.y - 2.0 * q.z * q.z;
	m[1][0] = 2.0 * q.x * q.y + 2.0 * q.w * q.z;
	m[2][0] = 2.0 * q.x * q.z - 2.0 * q.w * q.y;

	m[0][1] = 2.0f * q.x * q.y - 2.0f * q.w * q.z;
	m[1][1] = 1.0f - 2.0f * q.x * q.x - 2.0f * q.z * q.z;
	m[2][1] = 2.0f * q.y * q.z + 2.0f * q.w * q.x;

	m[0][2] = 2.0f * q.x * q.z + 2.0f * q.w * q.y;
	m[1][2] = 2.0f * q.y * q.z - 2.0f * q.w * q.x;
	m[2][2] = 1.0f - 2.0f * q.x * q.x - 2.0f * q.y * q.y;

	m[0][3] = 0.0f;
	m[1][3] = 0.0f;
	m[2][3] = 0.0f;
}

// A quaternion to angles, through its matrix.
inline void QuaternionAngles( const Quaternion &q, QAngle &angles )
{
	matrix3x4_t m;
	ss_mathlib::QuaternionMatrix( q, m );
	ss_mathlib::MatrixAngles( m, &angles.x );
}

// Angles to a quaternion, each angle halved.
inline void AngleQuaternion( const QAngle &angles, Quaternion &out )
{
	float sr, sp, sy, cr, cp, cy;
	SinCos( DEG2RAD( angles.y ) * 0.5f, &sy, &cy );
	SinCos( DEG2RAD( angles.x ) * 0.5f, &sp, &cp );
	SinCos( DEG2RAD( angles.z ) * 0.5f, &sr, &cr );

	const float srCp = sr * cp, crSp = cr * sp;
	out.x = srCp * cy - crSp * sy;
	out.y = crSp * cy + srCp * sy;

	const float crCp = cr * cp, srSp = sr * sp;
	out.z = crCp * sy - srSp * cy;
	out.w = crCp * cy + srSp * sy;
}

// A forward direction and a rough up to angles, roll included. A forward
// that is almost vertical has no yaw of its own: it is read off the left.
inline void VectorAngles( const Vector &forward, const Vector &pseudoUp, QAngle &angles )
{
	Vector left;
	::CrossProduct( pseudoUp, forward, left );
	VectorNormalizeFast( left );

	const float xyDist = sqrtf( forward[0] * forward[0] + forward[1] * forward[1] );
	if ( xyDist > 0.001f )
	{
		angles[1] = RAD2DEG( atan2f( forward[1], forward[0] ) );
		angles[0] = RAD2DEG( atan2f( -forward[2], xyDist ) );
		const float upZ = ( left[1] * forward[0] ) - ( left[0] * forward[1] );
		angles[2] = RAD2DEG( atan2f( left[2], upZ ) );
	}
	else
	{
		angles[1] = RAD2DEG( atan2f( -left[0], left[1] ) );
		angles[0] = RAD2DEG( atan2f( -forward[2], xyDist ) );
		angles[2] = 0;
	}
}

// A right and an up for a forward. A vertical forward takes right -Y and an
// up along X opposite to it.
inline void VectorVectors( const Vector &forward, Vector &right, Vector &up )
{
	if ( forward[0] == 0 && forward[1] == 0 )
	{
		right[0] = 0;
		right[1] = -1;
		right[2] = 0;
		up[0] = -forward[2];
		up[1] = 0;
		up[2] = 0;
		return;
	}
	const Vector z( 0, 0, 1.0 );
	::CrossProduct( forward, z, right );
	VectorNormalize( right );
	::CrossProduct( right, forward, up );
	VectorNormalize( up );
}

inline void ClearBounds( Vector &mins, Vector &maxs )
{
	mins[0] = mins[1] = mins[2] = 99999;
	maxs[0] = maxs[1] = maxs[2] = -99999;
}

inline void AddPointToBounds( const Vector &v, Vector &mins, Vector &maxs )
{
	for ( int axis = 0; axis < 3; ++axis )
	{
		const float value = v[axis];
		if ( value < mins[axis] )
		{
			mins[axis] = value;
		}
		if ( value > maxs[axis] )
		{
			maxs[axis] = value;
		}
	}
}

// A box under a 3x4: its centre transformed, its half-extents projected on
// the rows' absolute values.
inline void TransformAABB( const matrix3x4_t &m, const Vector &minsIn, const Vector &maxsIn, Vector &minsOut, Vector &maxsOut )
{
	Vector centre;
	VectorAdd( minsIn, maxsIn, centre );
	centre *= 0.5f;

	Vector extents;
	VectorSubtract( maxsIn, centre, extents );

	Vector worldCentre;
	ss_mathlib::VectorTransform( centre.Base(), m, worldCentre.Base() );

	Vector worldExtents;
	worldExtents.x = DotProductAbs( extents, m[0] );
	worldExtents.y = DotProductAbs( extents, m[1] );
	worldExtents.z = DotProductAbs( extents, m[2] );

	VectorSubtract( worldCentre, worldExtents, minsOut );
	VectorAdd( worldCentre, worldExtents, maxsOut );
}

// Moves value toward target by at most speed.
inline float Approach( float target, float value, float speed )
{
	const float delta = target - value;
	if ( delta > speed )
	{
		value += speed;
	}
	else if ( delta < -speed )
	{
		value -= speed;
	}
	else
	{
		value = target;
	}
	return value;
}

// Which of a plane's normal components are negative, one bit each.
inline int SignbitsForPlane( const cplane_t *plane )
{
	int bits = 0;
	for ( int axis = 0; axis < 3; ++axis )
	{
		if ( plane->normal[axis] < 0 )
		{
			bits |= 1 << axis;
		}
	}
	return bits;
}

// Which side of a plane a box is on: 1 in front, 2 behind, 3 across. An
// axial plane compares one coordinate; any other takes the two corners its
// sign bits name.
inline int BoxOnPlaneSide( const float *mins, const float *maxs, const cplane_t *p )
{
	if ( p->type < 3 )
	{
		if ( p->dist <= mins[p->type] )
		{
			return 1;
		}
		if ( p->dist >= maxs[p->type] )
		{
			return 2;
		}
		return 3;
	}

	// The near corner takes maxs on each axis whose sign bit is clear, the
	// far corner the opposite.
	float d1 = 0, d2 = 0;
	if ( p->signbits >= 0 && p->signbits < 8 )
	{
		const float *nearX = ( p->signbits & 1 ) ? mins : maxs;
		const float *nearY = ( p->signbits & 2 ) ? mins : maxs;
		const float *nearZ = ( p->signbits & 4 ) ? mins : maxs;
		const float *farX = ( p->signbits & 1 ) ? maxs : mins;
		const float *farY = ( p->signbits & 2 ) ? maxs : mins;
		const float *farZ = ( p->signbits & 4 ) ? maxs : mins;
		d1 = p->normal[0] * nearX[0] + p->normal[1] * nearY[1] + p->normal[2] * nearZ[2];
		d2 = p->normal[0] * farX[0] + p->normal[1] * farY[1] + p->normal[2] * farZ[2];
	}
	else
	{
		Assert( 0 );
	}

	int sides = 0;
	if ( d1 >= p->dist )
	{
		sides = 1;
	}
	if ( d2 < p->dist )
	{
		sides |= 2;
	}
	Assert( sides != 0 );
	return sides;
}

// True when the box is wholly behind any of the frustum's six planes.
inline bool R_CullBox( const Vector &mins, const Vector &maxs, const Frustum_t &frustum )
{
	static const int kPlanes[] = { FRUSTUM_RIGHT, FRUSTUM_LEFT, FRUSTUM_TOP, FRUSTUM_BOTTOM, FRUSTUM_NEARZ, FRUSTUM_FARZ };
	for ( int plane : kPlanes )
	{
		if ( ss_mathlib::BoxOnPlaneSide( &mins.x, &maxs.x, frustum.GetPlane( plane ) ) == 2 )
		{
			return true;
		}
	}
	return false;
}

// The vertical field of view for a horizontal one at an aspect; outside
// (1, 179) the horizontal one is taken as 90.
inline float CalcFovY( float fovX, float aspect )
{
	if ( fovX < 1 || fovX > 179 )
	{
		fovX = 90;
	}
	float val = atan( tan( DEG2RAD( fovX ) * 0.5f ) / aspect );
	val = RAD2DEG( val ) * 2.0f;
	return val;
}

// The six planes of a perspective view: far and near along forward, then
// left/right and bottom/top tilted by the half angles' tangents. SetPlane is
// mathlib.h's inline; its sign bits are SignbitsForPlane's (this port's global).
inline void GeneratePerspectiveFrustum( const Vector &origin, const Vector &forward, const Vector &right, const Vector &up,
                                        float zNear, float zFar, float fovX, float fovY, Frustum_t &frustum )
{
	const float intercept = DotProduct( origin, forward );

	frustum.SetPlane( FRUSTUM_FARZ, PLANE_ANYZ, -forward, -zFar - intercept );
	frustum.SetPlane( FRUSTUM_NEARZ, PLANE_ANYZ, forward, zNear + intercept );

	fovX *= 0.5f;
	fovY *= 0.5f;
	const float tanX = tan( DEG2RAD( fovX ) );
	const float tanY = tan( DEG2RAD( fovY ) );

	Vector pos, neg;
	VectorMA( right, tanX, forward, pos );
	VectorMA( pos, -2.0f, right, neg );
	VectorNormalize( pos );
	VectorNormalize( neg );
	frustum.SetPlane( FRUSTUM_LEFT, PLANE_ANYZ, pos, pos.Dot( origin ) );
	frustum.SetPlane( FRUSTUM_RIGHT, PLANE_ANYZ, neg, neg.Dot( origin ) );

	VectorMA( up, tanY, forward, pos );
	VectorMA( pos, -2.0f, up, neg );
	VectorNormalize( pos );
	VectorNormalize( neg );
	frustum.SetPlane( FRUSTUM_BOTTOM, PLANE_ANYZ, pos, pos.Dot( origin ) );
	frustum.SetPlane( FRUSTUM_TOP, PLANE_ANYZ, neg, neg.Dot( origin ) );
}

inline void GeneratePerspectiveFrustum( const Vector &origin, const QAngle &angles, float zNear, float zFar,
                                        float fovX, float aspect, Frustum_t &frustum )
{
	Vector forward, right, up;
	ss_mathlib::AngleVectors( angles, &forward, &right, &up );
	const float fovY = ss_mathlib::CalcFovY( fovX, aspect );
	ss_mathlib::GeneratePerspectiveFrustum( origin, forward, right, up, zNear, zFar, fovX, fovY, frustum );
}

// Linear to gamma 2.2 over the whole range.
inline float LinearToGammaFullRange( float linear )
{
	return pow( linear, 1.0f / 2.2f );
}

// --- VMatrix ---------------------------------------------------------------

inline void MatrixSetIdentity( VMatrix &dst )
{
	for ( int row = 0; row < 4; ++row )
	{
		for ( int col = 0; col < 4; ++col )
		{
			dst[row][col] = row == col ? 1.0f : 0.0f;
		}
	}
}

inline void MatrixTranspose( const VMatrix &src, VMatrix &dst )
{
	if ( &src == &dst )
	{
		for ( int row = 0; row < 4; ++row )
		{
			for ( int col = row + 1; col < 4; ++col )
			{
				V_swap( dst[row][col], dst[col][row] );
			}
		}
		return;
	}
	for ( int row = 0; row < 4; ++row )
	{
		for ( int col = 0; col < 4; ++col )
		{
			dst[row][col] = src[col][row];
		}
	}
}

inline VMatrix Transposed( const VMatrix &m )
{
	VMatrix out;
	ss_mathlib::MatrixTranspose( m, out );
	return out;
}

// dst = src1 * src2, either source may be the destination.
inline void MatrixMultiply( const VMatrix &src1, const VMatrix &src2, VMatrix &dst )
{
	const VMatrix a = src1, b = src2;
	for ( int row = 0; row < 4; ++row )
	{
		for ( int col = 0; col < 4; ++col )
		{
			dst[row][col] = a[row][0] * b[0][col] + a[row][1] * b[1][col] + a[row][2] * b[2][col] + a[row][3] * b[3][col];
		}
	}
}

// The upper 3x3 applied to a vector.
inline void Vector3DMultiply( const VMatrix &m, const Vector &in, Vector &out )
{
	const Vector v = in;
	for ( int row = 0; row < 3; ++row )
	{
		out[row] = m[row][0] * v[0] + m[row][1] * v[1] + m[row][2] * v[2];
	}
}

// A point through the whole matrix, then divided by w (left as is at w 0).
inline void Vector3DMultiplyPositionProjective( const VMatrix &m, const Vector &in, Vector &out )
{
	const Vector v = in;
	float w = m[3][0] * v[0] + m[3][1] * v[1] + m[3][2] * v[2] + m[3][3];
	if ( w != 0.0f )
	{
		w = 1.0f / w;
	}
	out[0] = m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2] + m[0][3];
	out[1] = m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2] + m[1][3];
	out[2] = m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2] + m[2][3];
	out *= w;
}

inline void MatrixBuildScale( VMatrix &dst, float x, float y, float z )
{
	ss_mathlib::MatrixSetIdentity( dst );
	dst[0][0] = x;
	dst[1][1] = y;
	dst[2][2] = z;
}

// A perspective projection into [0, 1] screen space, X right and Y up: the
// frustum's matrix, then the X/Y negation, the w offset and the half scale,
// each a multiply on the left as stock composed them.
inline void MatrixBuildPerspective( VMatrix &dst, float fovX, float fovY, float zNear, float zFar )
{
	const float width = 2 * zNear * tan( fovX * ( M_PI / 180.0f ) * 0.5f );
	const float height = 2 * zNear * tan( fovY * ( M_PI / 180.0f ) * 0.5f );

	memset( dst.Base(), 0, sizeof( dst ) );
	dst[0][0] = 2.0F * zNear / width;
	dst[1][1] = 2.0F * zNear / height;
	dst[2][2] = -zFar / ( zNear - zFar );
	dst[3][2] = 1.0f;
	dst[2][3] = zNear * zFar / ( zNear - zFar );

	VMatrix step;
	ss_mathlib::MatrixSetIdentity( step );
	step[0][0] = -1.0f;
	step[1][1] = -1.0f;
	ss_mathlib::MatrixMultiply( step, dst, dst );

	ss_mathlib::MatrixSetIdentity( step );
	step[0][3] = 1.0f;
	step[1][3] = 1.0f;
	step[2][3] = 0.0f;
	ss_mathlib::MatrixMultiply( step, dst, dst );

	ss_mathlib::MatrixSetIdentity( step );
	step[0][0] = 0.5f;
	step[1][1] = 0.5f;
	ss_mathlib::MatrixMultiply( step, dst, dst );
}

// A general 4x4 inverse by Gauss-Jordan elimination with partial pivoting:
// false, the destination untouched, when a column's largest pivot is not over
// 0.00001.
inline bool MatrixInverseGeneral( const VMatrix &src, VMatrix &dst )
{
	float work[4][8];
	int order[4];
	for ( int i = 0; i < 4; ++i )
	{
		for ( int j = 0; j < 4; ++j )
		{
			work[i][j] = src[i][j];
			work[i][j + 4] = 0.0f;
		}
		work[i][i + 4] = 1.0f;
		order[i] = i;
	}

	for ( int pivot = 0; pivot < 4; ++pivot )
	{
		float largest = 0.00001f;
		int largestAt = -1;
		for ( int test = pivot; test < 4; ++test )
		{
			const float magnitude = (float)FloatMakePositive( work[order[test]][pivot] );
			if ( magnitude > largest )
			{
				largestAt = test;
				largest = magnitude;
			}
		}
		if ( largestAt == -1 )
		{
			return false;
		}

		V_swap( order[largestAt], order[pivot] );
		float *row = work[order[pivot]];

		const float scale = 1.0f / row[pivot];
		for ( int j = 0; j < 8; ++j )
		{
			row[j] *= scale;
		}
		row[pivot] = 1.0f;

		for ( int i = 0; i < 4; ++i )
		{
			if ( i == pivot )
			{
				continue;
			}
			float *other = work[order[i]];
			const float factor = -other[pivot];
			for ( int j = 0; j < 8; ++j )
			{
				other[j] += row[j] * factor;
			}
			other[pivot] = 0.0f;
		}
	}

	for ( int i = 0; i < 4; ++i )
	{
		for ( int j = 0; j < 4; ++j )
		{
			dst[i][j] = work[order[i]][j + 4];
		}
	}
	return true;
}

// The world box of a projection volume: its eight unit-cube corners taken
// back through the inverse.
inline void CalculateAABBFromProjectionMatrix( const VMatrix &worldToVolume, Vector *pMins, Vector *pMaxs )
{
	VMatrix volumeToWorld;
	ss_mathlib::MatrixInverseGeneral( worldToVolume, volumeToWorld );
	ss_mathlib::ClearBounds( *pMins, *pMaxs );
	for ( int corner = 0; corner < 8; ++corner )
	{
		const Vector unit( (float)( ( corner >> 2 ) & 1 ), (float)( ( corner >> 1 ) & 1 ), (float)( corner & 1 ) );
		Vector world;
		ss_mathlib::Vector3DMultiplyPositionProjective( volumeToWorld, unit, world );
		ss_mathlib::AddPointToBounds( world, *pMins, *pMaxs );
	}
}

// --- Called only from code --gc-sections drops; defined so that a
// GC_SECTIONS=0 link (tools/nogc-relink) still resolves. ------------------

// AngleIMatrix with the translation carried into the rotated space.
inline void AngleIMatrix( const QAngle &angles, const Vector &position, matrix3x4_t &m )
{
	ss_mathlib::AngleIMatrix( angles, m );
	Vector translation;
	ss_mathlib::VectorRotate( position.Base(), m, translation.Base() );
	translation *= -1.0f;
	ss_mathlib::MatrixSetColumn( translation, 3, m );
}

// The three basis columns, right being the matrix's left negated.
inline void MatrixVectors( const matrix3x4_t &m, Vector *pForward, Vector *pRight, Vector *pUp )
{
	ss_mathlib::MatrixGetColumn( m, 0, *pForward );
	ss_mathlib::MatrixGetColumn( m, 1, *pRight );
	ss_mathlib::MatrixGetColumn( m, 2, *pUp );
	*pRight *= -1.0f;
}

// A basis to a quaternion, through the angles of its matrix (stock's
// working version; column 3 is never read).
inline void BasisToQuaternion( const Vector &forward, const Vector &right, const Vector &up, Quaternion &q )
{
	Vector left;
	VectorMultiply( right, -1.0f, left );
	matrix3x4_t m;
	ss_mathlib::MatrixSetColumn( forward, 0, m );
	ss_mathlib::MatrixSetColumn( left, 1, m );
	ss_mathlib::MatrixSetColumn( up, 2, m );
	QAngle angles;
	ss_mathlib::MatrixAngles( m, &angles.x );
	ss_mathlib::AngleQuaternion( angles, q );
}

// The point at t on the Catmull-Rom segment from p2 to p3, the basis rows
// summed into the output in stock's order.
inline void Catmull_Rom_Spline( const Vector &p1, const Vector &p2, const Vector &p3, const Vector &p4, float t, Vector &output )
{
	const float tSqr = t * t * 0.5f;
	const float tCube = t * tSqr;
	t *= 0.5f;

	output.Init();
	Vector a, b, c, d;

	VectorScale( p1, -tCube, a );
	VectorScale( p2, tCube * 3, b );
	VectorScale( p3, tCube * -3, c );
	VectorScale( p4, tCube, d );
	VectorAdd( a, output, output );
	VectorAdd( b, output, output );
	VectorAdd( c, output, output );
	VectorAdd( d, output, output );

	VectorScale( p1, tSqr * 2, a );
	VectorScale( p2, tSqr * -5, b );
	VectorScale( p3, tSqr * 4, c );
	VectorScale( p4, -tSqr, d );
	VectorAdd( a, output, output );
	VectorAdd( b, output, output );
	VectorAdd( c, output, output );
	VectorAdd( d, output, output );

	VectorScale( p1, -t, a );
	VectorScale( p3, t, b );
	VectorAdd( a, output, output );
	VectorAdd( b, output, output );

	VectorAdd( p2, output, output );
}

// power2_n's entries: 2^(i - 128) / 255, the check stock's table carries.
// Stock's are literals, so the smallest are subnormal floats. Computed at
// compile time (constant-initialised, no static initialiser; and runtime
// arithmetic that yields a subnormal is flushed where denormals are off):
// the power of two by exact halvings or doublings, then one rounded divide.
constexpr float Power2NAt( int i )
{
	float p = 1.0f;
	for ( int k = i; k < 128; ++k )
	{
		p *= 0.5f;
	}
	for ( int k = 128; k < i; ++k )
	{
		p *= 2.0f;
	}
	return p / 255.0f;
}

struct Power2NTable
{
	float v[256];
	constexpr Power2NTable() : v()
	{
		for ( int i = 0; i < 256; ++i )
		{
			v[i] = Power2NAt( i );
		}
	}
};

inline constexpr Power2NTable kPower2N{};

} // namespace ss_mathlib

#endif // SS_MATHLIB_PORT_H
