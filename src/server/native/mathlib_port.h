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

} // namespace ss_mathlib

#endif // SS_MATHLIB_PORT_H
