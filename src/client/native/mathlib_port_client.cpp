//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — mathlib's members only client.so calls ============//
// SS_REASON g  leaf math over mdl/bsp data
//
// The global names mathlib.h declares that client.so's objects leave
// undefined and server.so's do not (plan_server_native_v2 T5: a shared TU
// keeps only what both DLLs import). Each forwards to its ss_mathlib:: twin in
// mathlib_port.h; src/shared/native/mathlib_port.cpp holds the ones both DLLs
// call. client.so links no libmathlib.a.
//=============================================================================//
#include "mathlib_port.h"

#include "mathlib/ssemath.h"

void AngleMatrix( const QAngle &angles, matrix3x4_t &mat )
{
	ss_mathlib::AngleMatrix( angles, mat );
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

void CrossProduct( const float *v1, const float *v2, float *cross )
{
	ss_mathlib::CrossProduct( v1, v2, cross );
}

void VectorRotate( const float *in1, const matrix3x4_t &in2, float *out )
{
	ss_mathlib::VectorRotate( in1, in2, out );
}

void MatrixSetColumn( const Vector &in, int column, matrix3x4_t &out )
{
	ss_mathlib::MatrixSetColumn( in, column, out );
}

void MatrixCopy( const matrix3x4_t &in, matrix3x4_t &out )
{
	ss_mathlib::MatrixCopy( in, out );
}

void MatrixInvert( const matrix3x4_t &in, matrix3x4_t &out )
{
	ss_mathlib::MatrixInvert( in, out );
}

void AngleIMatrix( const QAngle &angles, matrix3x4_t &matrix )
{
	ss_mathlib::AngleIMatrix( angles, matrix );
}

void ConcatTransforms( const matrix3x4_t &in1, const matrix3x4_t &in2, matrix3x4_t &out )
{
	ss_mathlib::ConcatTransforms( in1, in2, out );
}

void QuaternionMatrix( const Quaternion &q, matrix3x4_t &matrix )
{
	ss_mathlib::QuaternionMatrix( q, matrix );
}

void QuaternionAngles( const Quaternion &q, QAngle &angles )
{
	ss_mathlib::QuaternionAngles( q, angles );
}

void VectorVectors( const Vector &forward, Vector &right, Vector &up )
{
	ss_mathlib::VectorVectors( forward, right, up );
}

void ClearBounds( Vector &mins, Vector &maxs )
{
	ss_mathlib::ClearBounds( mins, maxs );
}

void AddPointToBounds( const Vector &v, Vector &mins, Vector &maxs )
{
	ss_mathlib::AddPointToBounds( v, mins, maxs );
}

void TransformAABB( const matrix3x4_t &transform, const Vector &vecMinsIn, const Vector &vecMaxsIn, Vector &vecMinsOut, Vector &vecMaxsOut )
{
	ss_mathlib::TransformAABB( transform, vecMinsIn, vecMaxsIn, vecMinsOut, vecMaxsOut );
}

int SignbitsForPlane( cplane_t *out )
{
	return ss_mathlib::SignbitsForPlane( out );
}

int __cdecl BoxOnPlaneSide( const float *emins, const float *emaxs, const cplane_t *plane )
{
	return ss_mathlib::BoxOnPlaneSide( emins, emaxs, plane );
}

bool R_CullBox( const Vector &mins, const Vector &maxs, const Frustum_t &frustum )
{
	return ss_mathlib::R_CullBox( mins, maxs, frustum );
}

void GeneratePerspectiveFrustum( const Vector &origin, const QAngle &angles, float flZNear, float flZFar, float flFovX, float flAspectRatio, Frustum_t &frustum )
{
	ss_mathlib::GeneratePerspectiveFrustum( origin, angles, flZNear, flZFar, flFovX, flAspectRatio, frustum );
}

float LinearToGammaFullRange( float linear )
{
	return ss_mathlib::LinearToGammaFullRange( linear );
}

void MatrixSetIdentity( VMatrix &dst )
{
	ss_mathlib::MatrixSetIdentity( dst );
}

void MatrixTranspose( const VMatrix &src, VMatrix &dst )
{
	ss_mathlib::MatrixTranspose( src, dst );
}

VMatrix VMatrix::Transpose() const
{
	return ss_mathlib::Transposed( *this );
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

void MatrixBuildScale( VMatrix &dst, float x, float y, float z )
{
	ss_mathlib::MatrixBuildScale( dst, x, y, z );
}

void MatrixBuildPerspective( VMatrix &dst, float fovX, float fovY, float zNear, float zFar )
{
	ss_mathlib::MatrixBuildPerspective( dst, fovX, fovY, zNear, zFar );
}

bool MatrixInverseGeneral( const VMatrix &src, VMatrix &dst )
{
	return ss_mathlib::MatrixInverseGeneral( src, dst );
}

void CalculateAABBFromProjectionMatrix( const VMatrix &worldToVolume, Vector *pMins, Vector *pMaxs )
{
	ss_mathlib::CalculateAABBFromProjectionMatrix( worldToVolume, pMins, pMaxs );
}

void AngleIMatrix( const QAngle &angles, const Vector &position, matrix3x4_t &mat )
{
	ss_mathlib::AngleIMatrix( angles, position, mat );
}

// sseconst.cpp's constants (ssemath.h).
const fltx4 Four_Zeros = { 0.0f, 0.0f, 0.0f, 0.0f };
const fltx4 Four_Ones = { 1.0f, 1.0f, 1.0f, 1.0f };
const fltx4 Four_Epsilons = { FLT_EPSILON, FLT_EPSILON, FLT_EPSILON, FLT_EPSILON };
const fltx4 Four_FLT_MAX = { FLT_MAX, FLT_MAX, FLT_MAX, FLT_MAX };
const fltx4 Four_Negative_FLT_MAX = { -FLT_MAX, -FLT_MAX, -FLT_MAX, -FLT_MAX };
const ALIGN16 uint32 g_SIMD_clear_wmask[4] ALIGN16_POST = { 0xffffffff, 0xffffffff, 0xffffffff, 0 };
