//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON g  leaf math over mdl/bsp data
// The mathlib.h names only client.so imports; each forwards to its ss_mathlib
// twin. The ones both DLLs call are in src/shared/native/mathlib_port.cpp.
#include "mathlib_port.h"

void AngleMatrix( const QAngle &angles, matrix3x4_t &mat )
{
	ss_mathlib::AngleMatrix( angles, mat );
}

void VectorTransform( const float *in1, const matrix3x4_t &in2, float *out )
{
	ss_mathlib::VectorTransform( in1, in2, out );
}

float CalcSqrDistanceToAABB( const Vector &mins, const Vector &maxs, const Vector &point )
{
	return ss_mathlib::CalcSqrDistanceToAABB( mins, maxs, point );
}

void MatrixSetColumn( const Vector &in, int column, matrix3x4_t &out )
{
	ss_mathlib::MatrixSetColumn( in, column, out );
}

void MatrixCopy( const matrix3x4_t &in, matrix3x4_t &out )
{
	ss_mathlib::MatrixCopy( in, out );
}

void ClearBounds( Vector &mins, Vector &maxs )
{
	ss_mathlib::ClearBounds( mins, maxs );
}

void AddPointToBounds( const Vector &v, Vector &mins, Vector &maxs )
{
	ss_mathlib::AddPointToBounds( v, mins, maxs );
}

void TransformAABB( const matrix3x4_t &transform, const Vector &vecMinsIn, const Vector &vecMaxsIn, Vector &vecMinsOut,
					Vector &vecMaxsOut )
{
	ss_mathlib::TransformAABB( transform, vecMinsIn, vecMaxsIn, vecMinsOut, vecMaxsOut );
}

float LinearToGammaFullRange( float linear )
{
	return ss_mathlib::LinearToGammaFullRange( linear );
}

void MatrixSetIdentity( VMatrix &dst )
{
	ss_mathlib::MatrixSetIdentity( dst );
}

VMatrix &VMatrix::operator=( const VMatrix &mOther )
{
	memcpy( m, mOther.m, sizeof( m ) );
	return *this;
}

void MatrixMultiply( const VMatrix &src1, const VMatrix &src2, VMatrix &dst )
{
	ss_mathlib::MatrixMultiply( src1, src2, dst );
}

void Vector3DMultiply( const VMatrix &src1, const Vector &src2, Vector &dst )
{
	ss_mathlib::Vector3DMultiply( src1, src2, dst );
}

void Vector3DMultiplyPositionProjective( const VMatrix &src1, const Vector &src2, Vector &dst )
{
	ss_mathlib::Vector3DMultiplyPositionProjective( src1, src2, dst );
}

bool MatrixInverseGeneral( const VMatrix &src, VMatrix &dst )
{
	return ss_mathlib::MatrixInverseGeneral( src, dst );
}
