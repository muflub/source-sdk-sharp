//========= Copyright Valve Corporation, All rights reserved. ============//
// VMatrix always postmultiply vectors as in Ax = b.
// Given a set of basis vectors ((F)orward, (L)eft, (U)p), and a (T)ranslation, 
// a matrix to transform a vector into that space looks like this:
// Fx Lx Ux Tx
// Fy Ly Uy Ty
// Fz Lz Uz Tz
// 0   0  0  1

// Note that concatenating matrices needs to multiply them in reverse order.
// ie: if I want to apply matrix A, B, then C, the equation needs to look like this:
// C * B * A * v
// ie:
// v = A * v;
// v = B * v;
// v = C * v;

#pragma once

#include <string.h>
#include "mathlib/vector.h"
#include "mathlib/vplane.h"
#include "mathlib/vector4d.h"
#include "mathlib/mathlib.h"

struct cplane_t;

class VMatrix
{
public:

	VMatrix() = default;
	VMatrix(
		vec_t m00, vec_t m01, vec_t m02, vec_t m03,
		vec_t m10, vec_t m11, vec_t m12, vec_t m13,
		vec_t m20, vec_t m21, vec_t m22, vec_t m23,
		vec_t m30, vec_t m31, vec_t m32, vec_t m33
		);

	// Creates a matrix where the X axis = forward
	// the Y axis = left, and the Z axis = up
	VMatrix( const Vector& forward, const Vector& left, const Vector& up );
	VMatrix( const Vector& forward, const Vector& left, const Vector& up, const Vector& translation );
	
	// Construct from a 3x4 matrix
	VMatrix( const matrix3x4_t& matrix3x4 );

	// Set the values in the matrix.
	void		Init( 
		vec_t m00, vec_t m01, vec_t m02, vec_t m03,
		vec_t m10, vec_t m11, vec_t m12, vec_t m13,
		vec_t m20, vec_t m21, vec_t m22, vec_t m23,
		vec_t m30, vec_t m31, vec_t m32, vec_t m33 
		);

	// Initialize from a 3x4
	void		Init( const matrix3x4_t& matrix3x4 );

	// array access
	inline float* operator[](int i)
	{ 
		return m[i]; 
	}

	inline const float* operator[](int i) const
	{ 
		return m[i]; 
	}

	// Get a pointer to m[0][0]
	inline float *Base()
	{
		return &m[0][0];
	}

	inline const float *Base() const
	{
		return &m[0][0];
	}

	void		SetLeft(const Vector &vLeft);
	void		SetUp(const Vector &vUp);
	void		SetForward(const Vector &vForward);

	void		GetBasisVectors(Vector &vForward, Vector &vLeft, Vector &vUp) const;
	void		SetBasisVectors(const Vector &vForward, const Vector &vLeft, const Vector &vUp);

	// Get/set the translation.
	Vector &	GetTranslation( Vector &vTrans ) const;
	void		SetTranslation(const Vector &vTrans);

	const matrix3x4_t& As3x4() const;
	void		CopyFrom3x4( const matrix3x4_t &m3x4 );
	void		Set3x4( matrix3x4_t& matrix3x4 ) const;

	bool		operator==( const VMatrix& src ) const;

// Matrix->vector operations.
public:

#ifndef VECTOR_NO_SLOW_OPERATIONS

	// Multiply by a vector (divides by w, assumes input w is 1).
	Vector		operator*(const Vector &vVec) const;

#endif

// Matrix->plane operations.
public:
	// Transform the plane. The matrix can only contain translation and rotation.
	void		TransformPlane( const VPlane &inPlane, VPlane &outPlane ) const;

#ifndef VECTOR_NO_SLOW_OPERATIONS
	// Just calls TransformPlane and returns the result.
	VPlane		operator*(const VPlane &thePlane) const;
#endif

// Matrix->matrix operations.
public:

	VMatrix&	operator=(const VMatrix &mOther);
	
	// Multiply two matrices (out = this * vm).
	void		MatrixMul( const VMatrix &vm, VMatrix &out ) const;

#ifndef VECTOR_NO_SLOW_OPERATIONS
	// Just calls MatrixMul and returns the result.	
	VMatrix		operator*(const VMatrix &mOther) const;

#endif

// Matrix operations.
public:
	// Set to identity.
	void		Identity();

	// Setup a matrix for origin and angles.
	void		SetupMatrixOrgAngles( const Vector &origin, const QAngle &vAngles );
	
	// General inverse. This may fail so check the return!
	bool		InverseGeneral(VMatrix &vInverse) const;
	
public:
	// The matrix.
	vec_t		m[4][4];
};

// Helper functions.

 // ** Note: this generates a temporary, don't hold reference!

// These methods are faster. Use them if you want faster code
void MatrixSetIdentity( VMatrix &dst );
void MatrixTranspose( const VMatrix& src, VMatrix& dst );
void MatrixCopy( const VMatrix& src, VMatrix& dst );
void MatrixMultiply( const VMatrix& src1, const VMatrix& src2, VMatrix& dst );

void MatrixSetColumn( VMatrix &src, int nCol, const Vector &column );

// Vector3DMultiply treats src2 as if it's a direction vector
void Vector3DMultiply( const VMatrix& src1, const Vector& src2, Vector& dst );

// Vector3DMultiplyPositionProjective treats src2 as if it's a point 
// and does the perspective divide at the end
void Vector3DMultiplyPositionProjective( const VMatrix& src1, const Vector &src2, Vector& dst );

void MatrixBuildTranslation( VMatrix& dst, const Vector &translation );

void MatrixBuildRotationAboutAxis( VMatrix& dst, const Vector& vAxisOfRot, float angleDegrees );

// Builds a scale matrix
void MatrixBuildScale( VMatrix &dst, float x, float y, float z );

// Given an inverse projection matrix, take the extremes of the space in transformed into world space and
// get a bounding box.
void CalculateAABBFromProjectionMatrixInverse( const VMatrix &volumeToWorld, Vector *pMins, Vector *pMaxs );

// Given an inverse projection matrix, take the extremes of the space in transformed into world space and
// get a bounding sphere.
void CalculateSphereFromProjectionMatrixInverse( const VMatrix &volumeToWorld, Vector *pCenter, float *pflRadius );

// Setup a matrix from euler angles. 
void MatrixFromAngles( const QAngle& vAngles, VMatrix& dst );

// Does a fast inverse, assuming the matrix only contains translation and rotation.
void MatrixInverseTR( const VMatrix& src, VMatrix &dst );

// Inverts any matrix at all
bool MatrixInverseGeneral(const VMatrix& src, VMatrix& dst);

// Computes the inverse transpose
void MatrixInverseTranspose( const VMatrix& src, VMatrix& dst );

// VMatrix inlines.
inline VMatrix::VMatrix(
	vec_t m00, vec_t m01, vec_t m02, vec_t m03,
	vec_t m10, vec_t m11, vec_t m12, vec_t m13,
	vec_t m20, vec_t m21, vec_t m22, vec_t m23,
	vec_t m30, vec_t m31, vec_t m32, vec_t m33)
{
	Init(
		m00, m01, m02, m03,
		m10, m11, m12, m13,
		m20, m21, m22, m23,
		m30, m31, m32, m33
		);
}

inline VMatrix::VMatrix( const matrix3x4_t& matrix3x4 )
{
	Init( matrix3x4 );
}

// Creates a matrix where the X axis = forward
// the Y axis = left, and the Z axis = up
inline VMatrix::VMatrix( const Vector& xAxis, const Vector& yAxis, const Vector& zAxis )
{
	Init(
		xAxis.x, yAxis.x, zAxis.x, 0.0f,
		xAxis.y, yAxis.y, zAxis.y, 0.0f,
		xAxis.z, yAxis.z, zAxis.z, 0.0f,
		0.0f, 0.0f, 0.0f, 1.0f
		);
}

inline VMatrix::VMatrix( const Vector& xAxis, const Vector& yAxis, const Vector& zAxis, const Vector& translation )
{
	Init(
		xAxis.x, yAxis.x, zAxis.x, translation.x,
		xAxis.y, yAxis.y, zAxis.y, translation.y,
		xAxis.z, yAxis.z, zAxis.z, translation.z,
		0.0f, 0.0f, 0.0f, 1.0f
		);
}

inline void VMatrix::Init(
	vec_t m00, vec_t m01, vec_t m02, vec_t m03,
	vec_t m10, vec_t m11, vec_t m12, vec_t m13,
	vec_t m20, vec_t m21, vec_t m22, vec_t m23,
	vec_t m30, vec_t m31, vec_t m32, vec_t m33
	)
{
	m[0][0] = m00;
	m[0][1] = m01;
	m[0][2] = m02;
	m[0][3] = m03;

	m[1][0] = m10;
	m[1][1] = m11;
	m[1][2] = m12;
	m[1][3] = m13;

	m[2][0] = m20;
	m[2][1] = m21;
	m[2][2] = m22;
	m[2][3] = m23;

	m[3][0] = m30;
	m[3][1] = m31;
	m[3][2] = m32;
	m[3][3] = m33;
}

// Initialize from a 3x4
inline void VMatrix::Init( const matrix3x4_t& matrix3x4 )
{
	memcpy(m, matrix3x4.Base(), sizeof( matrix3x4_t ) );

	m[3][0] = 0.0f;
	m[3][1] = 0.0f;
	m[3][2] = 0.0f;
	m[3][3] = 1.0f;	
}

// Methods related to the basis vectors of the matrix

inline void VMatrix::SetForward(const Vector &vForward)
{
	m[0][0] = vForward.x;
	m[1][0] = vForward.y;
	m[2][0] = vForward.z;
}

inline void VMatrix::SetLeft(const Vector &vLeft)
{
	m[0][1] = vLeft.x;
	m[1][1] = vLeft.y;
	m[2][1] = vLeft.z;
}

inline void VMatrix::SetUp(const Vector &vUp)
{
	m[0][2] = vUp.x;
	m[1][2] = vUp.y;
	m[2][2] = vUp.z;
}

inline void VMatrix::GetBasisVectors(Vector &vForward, Vector &vLeft, Vector &vUp) const
{
	vForward.Init( m[0][0], m[1][0], m[2][0] );
	vLeft.Init( m[0][1], m[1][1], m[2][1] );
	vUp.Init( m[0][2], m[1][2], m[2][2] );
}

inline void VMatrix::SetBasisVectors(const Vector &vForward, const Vector &vLeft, const Vector &vUp)
{
	SetForward(vForward);
	SetLeft(vLeft);
	SetUp(vUp);
}

// Methods related to the translation component of the matrix

inline Vector& VMatrix::GetTranslation( Vector &vTrans ) const
{
	vTrans.x = m[0][3];
	vTrans.y = m[1][3];
	vTrans.z = m[2][3];
	return vTrans;
}

inline void VMatrix::SetTranslation(const Vector &vTrans)
{
	m[0][3] = vTrans.x;
	m[1][3] = vTrans.y;
	m[2][3] = vTrans.z;
}

inline const matrix3x4_t& VMatrix::As3x4() const
{
	return *((const matrix3x4_t*)this);
}

inline void VMatrix::CopyFrom3x4( const matrix3x4_t &m3x4 )
{
	memcpy( m, m3x4.Base(), sizeof( matrix3x4_t ) );
	m[3][0] = m[3][1] = m[3][2] = 0;
	m[3][3] = 1;
}

inline void	VMatrix::Set3x4( matrix3x4_t& matrix3x4 ) const
{
	memcpy(matrix3x4.Base(), m, sizeof( matrix3x4_t ) );
}

// Vector transformation

#ifndef VECTOR_NO_SLOW_OPERATIONS

inline Vector VMatrix::operator*(const Vector &vVec) const
{
	Vector vRet;
	vRet.x = m[0][0]*vVec.x + m[0][1]*vVec.y + m[0][2]*vVec.z + m[0][3];
	vRet.y = m[1][0]*vVec.x + m[1][1]*vVec.y + m[1][2]*vVec.z + m[1][3];
	vRet.z = m[2][0]*vVec.x + m[2][1]*vVec.y + m[2][2]*vVec.z + m[2][3];

	return vRet;
}

#endif // VECTOR_NO_SLOW_OPERATIONS

// Plane transformation
inline void	VMatrix::TransformPlane( const VPlane &inPlane, VPlane &outPlane ) const
{
	Vector vTrans;
	Vector3DMultiply( *this, inPlane.m_Normal, outPlane.m_Normal );
	outPlane.m_Dist = inPlane.m_Dist * DotProduct( outPlane.m_Normal, outPlane.m_Normal );
	outPlane.m_Dist += DotProduct( outPlane.m_Normal, GetTranslation( vTrans ) );
}

// Other random stuff
inline void VMatrix::Identity()
{
	MatrixSetIdentity( *this );
}

inline void MatrixSetColumn( VMatrix &src, int nCol, const Vector &column )
{
	Assert( (nCol >= 0) && (nCol <= 3) );

	src.m[0][nCol] = column.x;
	src.m[1][nCol] = column.y;
	src.m[2][nCol] = column.z;
}

void MatrixBuildOrtho( VMatrix& dst, double left, double top, double right, double bottom, double zNear, double zFar );
void MatrixBuildPerspectiveX( VMatrix& dst, double flFovX, double flAspect, double flZNear, double flZFar );
void MatrixBuildPerspectiveOffCenterX( VMatrix& dst, double flFovX, double flAspect, double flZNear, double flZFar, double bottom, double top, double left, double right );
void MatrixBuildPerspectiveZRange( VMatrix& dst, double flZNear, double flZFar );
