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

#include "mathlib/ssemath.h"

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

//-----------------------------------------------------------------------------
// client.so's (plan_client_novalve C9): the members its live sections call.
//-----------------------------------------------------------------------------

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

void AngleQuaternion( const QAngle &angles, Quaternion &outQuat )
{
	ss_mathlib::AngleQuaternion( angles, outQuat );
}

void VectorAngles( const Vector &forward, const Vector &pseudoup, QAngle &angles )
{
	ss_mathlib::VectorAngles( forward, pseudoup, angles );
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

float Approach( float target, float value, float speed )
{
	return ss_mathlib::Approach( target, value, speed );
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

float CalcFovY( float flFovX, float flScreenAspect )
{
	return ss_mathlib::CalcFovY( flFovX, flScreenAspect );
}

void GeneratePerspectiveFrustum( const Vector &origin, const QAngle &angles, float flZNear, float flZFar, float flFovX, float flAspectRatio, Frustum_t &frustum )
{
	ss_mathlib::GeneratePerspectiveFrustum( origin, angles, flZNear, flZFar, flFovX, flAspectRatio, frustum );
}

void GeneratePerspectiveFrustum( const Vector &origin, const Vector &forward, const Vector &right, const Vector &up, float flZNear, float flZFar, float flFovX, float flFovY, Frustum_t &frustum )
{
	ss_mathlib::GeneratePerspectiveFrustum( origin, forward, right, up, flZNear, flZFar, flFovX, flFovY, frustum );
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

VMatrix VMatrix::operator*( const VMatrix &vm ) const
{
	VMatrix ret;
	ss_mathlib::MatrixMultiply( *this, vm, ret );
	return ret;
}

void AngleIMatrix( const QAngle &angles, const Vector &position, matrix3x4_t &mat )
{
	ss_mathlib::AngleIMatrix( angles, position, mat );
}

void MatrixVectors( const matrix3x4_t &matrix, Vector *pForward, Vector *pRight, Vector *pUp )
{
	ss_mathlib::MatrixVectors( matrix, pForward, pRight, pUp );
}

void BasisToQuaternion( const Vector &vecForward, const Vector &vecRight, const Vector &vecUp, Quaternion &q )
{
	ss_mathlib::BasisToQuaternion( vecForward, vecRight, vecUp, q );
}

void Catmull_Rom_Spline( const Vector &p1, const Vector &p2, const Vector &p3, const Vector &p4, float t, Vector &output )
{
	ss_mathlib::Catmull_Rom_Spline( p1, p2, p3, p4, t, output );
}

#ifdef CLIENT_DLL
// Data only client.so's inlines read; server.so's object carries none of it
// (its globals ledger lists what server.so defines).

// sseconst.cpp's constants (ssemath.h).
const fltx4 Four_Zeros = { 0.0f, 0.0f, 0.0f, 0.0f };
const fltx4 Four_Ones = { 1.0f, 1.0f, 1.0f, 1.0f };
const fltx4 Four_Epsilons = { FLT_EPSILON, FLT_EPSILON, FLT_EPSILON, FLT_EPSILON };
const fltx4 Four_FLT_MAX = { FLT_MAX, FLT_MAX, FLT_MAX, FLT_MAX };
const fltx4 Four_Negative_FLT_MAX = { -FLT_MAX, -FLT_MAX, -FLT_MAX, -FLT_MAX };
const ALIGN16 uint32 g_SIMD_signmask[4] ALIGN16_POST = { 0x80000000, 0x80000000, 0x80000000, 0x80000000 };
const ALIGN16 uint32 g_SIMD_clear_wmask[4] ALIGN16_POST = { 0xffffffff, 0xffffffff, 0xffffffff, 0 };

// color_conversion.cpp's exponent table, which mathlib.h's TexLightToLinear
// reads: entry i is 2^(i - 128) / 255 (ss_mathlib::kPower2N, compile-time).
#define SS_P2( i ) ss_mathlib::kPower2N.v[ i ]
#define SS_P2_8( b ) SS_P2( b ), SS_P2( b + 1 ), SS_P2( b + 2 ), SS_P2( b + 3 ), SS_P2( b + 4 ), SS_P2( b + 5 ), SS_P2( b + 6 ), SS_P2( b + 7 )
#define SS_P2_64( b ) SS_P2_8( b ), SS_P2_8( b + 8 ), SS_P2_8( b + 16 ), SS_P2_8( b + 24 ), SS_P2_8( b + 32 ), SS_P2_8( b + 40 ), SS_P2_8( b + 48 ), SS_P2_8( b + 56 )
ALIGN128 float power2_n[256] = { SS_P2_64( 0 ), SS_P2_64( 64 ), SS_P2_64( 128 ), SS_P2_64( 192 ) };
#undef SS_P2_64
#undef SS_P2_8
#undef SS_P2
#endif // CLIENT_DLL
