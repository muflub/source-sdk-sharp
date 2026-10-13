//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — mathlib's members, as server.so uses them =========//
// SS_REASON g  leaf math over mdl/bsp data
//
// The global names mathlib.h declares, for server.so, which links no
// libmathlib.a. Each forwards to its ss_mathlib:: twin in mathlib_port.h,
// where the arithmetic and the reasons are. The set is what server.so's
// objects leave undefined that the archive defined: a function missing here is
// a link error, and one nothing calls is dropped by --gc-sections.
//=============================================================================//
#include "mathlib_port.h"

const Vector vec3_origin( 0, 0, 0 );
const QAngle vec3_angle( 0, 0, 0 );

void AngleMatrix( const QAngle &angles, matrix3x4_t &mat )
{
	ss_mathlib::AngleMatrix( angles, mat );
}

void AngleMatrix( const QAngle &angles, const Vector &position, matrix3x4_t &mat )
{
	ss_mathlib::AngleMatrix( angles, position, mat );
}

void AngleVectors( const QAngle &angles, Vector *forward )
{
	ss_mathlib::AngleVectors( angles, forward );
}

void AngleVectors( const QAngle &angles, Vector *forward, Vector *right, Vector *up )
{
	ss_mathlib::AngleVectors( angles, forward, right, up );
}

void VectorAngles( const Vector &forward, QAngle &angles )
{
	ss_mathlib::VectorAngles( forward, angles );
}

void MatrixAngles( const matrix3x4_t &matrix, float *angles )
{
	ss_mathlib::MatrixAngles( matrix, angles );
}

void SetIdentityMatrix( matrix3x4_t &mat )
{
	ss_mathlib::SetIdentityMatrix( mat );
}

void MatrixGetColumn( const matrix3x4_t &in, int column, Vector &out )
{
	ss_mathlib::MatrixGetColumn( in, column, out );
}

void VectorTransform( const float *in1, const matrix3x4_t &in2, float *out )
{
	ss_mathlib::VectorTransform( in1, in2, out );
}

void VectorITransform( const float *in1, const matrix3x4_t &in2, float *out )
{
	ss_mathlib::VectorITransform( in1, in2, out );
}

void VectorIRotate( const float *in1, const matrix3x4_t &in2, float *out )
{
	ss_mathlib::VectorIRotate( in1, in2, out );
}

float CalcSqrDistanceToAABB( const Vector &mins, const Vector &maxs, const Vector &point )
{
	return ss_mathlib::CalcSqrDistanceToAABB( mins, maxs, point );
}
