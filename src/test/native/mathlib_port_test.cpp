//========= SourceSharp — server.so's mathlib port, on its own ===============//
//
// mathlib_port.cpp is what server.so links in place of libmathlib.a. This
// binary links it and NOT the archive, so it builds only if the port is
// self-contained, and it runs on Windows too, where no mathlib binary can
// (ss_cpptest_mathlib). The values are worked by hand from the geometry, not
// read off an implementation: mathlib_port_stock_test is where the port meets
// stock bit for bit, on Linux.
//=============================================================================//

#include "gtest/gtest.h"

#include "mathlib/mathlib.h"
#include "mathlib/vector.h"

namespace
{

const float kTol = 1e-5f;

void ExpectNear( const Vector &got, float x, float y, float z )
{
	EXPECT_NEAR( got.x, x, kTol );
	EXPECT_NEAR( got.y, y, kTol );
	EXPECT_NEAR( got.z, z, kTol );
}

TEST( MathlibPort, Vec3OriginAndVec3AngleAreZero )
{
	EXPECT_EQ( vec3_origin, Vector( 0, 0, 0 ) );
	EXPECT_EQ( vec3_angle, QAngle( 0, 0, 0 ) );
}

TEST( MathlibPort, SetIdentityMatrixOverwritesEveryCell )
{
	matrix3x4_t m;
	for ( int r = 0; r < 3; ++r )
		for ( int c = 0; c < 4; ++c )
			m[r][c] = 9.0f;
	SetIdentityMatrix( m );
	for ( int r = 0; r < 3; ++r )
		for ( int c = 0; c < 4; ++c )
			EXPECT_EQ( m[r][c], r == c ? 1.0f : 0.0f ) << r << "," << c;
}

TEST( MathlibPort, AngleVectorsOfZeroIsTheAxes )
{
	Vector f, r, u;
	AngleVectors( QAngle( 0, 0, 0 ), &f, &r, &u );
	ExpectNear( f, 1, 0, 0 );
	ExpectNear( r, 0, -1, 0 );   // right is -Y: Y is left
	ExpectNear( u, 0, 0, 1 );
}

TEST( MathlibPort, YawTurnsForwardAboutZ )
{
	Vector f;
	AngleVectors( QAngle( 0, 90, 0 ), &f );
	ExpectNear( f, 0, 1, 0 );
	AngleVectors( QAngle( 0, 180, 0 ), &f );
	ExpectNear( f, -1, 0, 0 );
}

TEST( MathlibPort, PositivePitchLooksDown )
{
	Vector f;
	AngleVectors( QAngle( 90, 0, 0 ), &f );
	ExpectNear( f, 0, 0, -1 );
}

TEST( MathlibPort, RollTurnsRightAndUpButNotForward )
{
	Vector f, r, u;
	AngleVectors( QAngle( 0, 0, 90 ), &f, &r, &u );
	ExpectNear( f, 1, 0, 0 );
	ExpectNear( r, 0, 0, -1 );
	ExpectNear( u, 0, -1, 0 );
}

TEST( MathlibPort, AngleVectorsSkipsTheVectorsNotAskedFor )
{
	Vector u( 7, 7, 7 );
	AngleVectors( QAngle( 10, 20, 30 ), nullptr, nullptr, &u );
	Vector all;
	AngleVectors( QAngle( 10, 20, 30 ), nullptr, nullptr, &all );
	EXPECT_EQ( u, all );
	EXPECT_NE( u, Vector( 7, 7, 7 ) );
}

TEST( MathlibPort, AngleMatrixColumnsAreForwardLeftUp )
{
	const QAngle a( 20, 30, 40 );
	Vector f, r, u;
	AngleVectors( a, &f, &r, &u );
	matrix3x4_t m;
	AngleMatrix( a, m );
	Vector col;
	MatrixGetColumn( m, 0, col );
	ExpectNear( col, f.x, f.y, f.z );
	MatrixGetColumn( m, 1, col );
	ExpectNear( col, -r.x, -r.y, -r.z );
	MatrixGetColumn( m, 2, col );
	ExpectNear( col, u.x, u.y, u.z );
	MatrixGetColumn( m, 3, col );
	EXPECT_EQ( col, Vector( 0, 0, 0 ) );
}

TEST( MathlibPort, AngleMatrixWithPositionSetsTheTranslation )
{
	matrix3x4_t m;
	AngleMatrix( QAngle( 0, 90, 0 ), Vector( 1, 2, 3 ), m );
	Vector col;
	MatrixGetColumn( m, 3, col );
	EXPECT_EQ( col, Vector( 1, 2, 3 ) );
}

TEST( MathlibPort, MatrixAnglesInvertsAngleMatrix )
{
	const QAngle in( 20, 30, 40 );
	matrix3x4_t m;
	AngleMatrix( in, m );
	QAngle out;
	MatrixAngles( m, out );   // mathlib.h's inline, over the float* member
	EXPECT_NEAR( out.x, 20, 1e-4f );
	EXPECT_NEAR( out.y, 30, 1e-4f );
	EXPECT_NEAR( out.z, 40, 1e-4f );
}

TEST( MathlibPort, MatrixAnglesStraightDownReadsYawFromLeftAndNoRoll )
{
	// Pitch 90 with yaw 30: forward is -Z, so its horizontal length is under
	// the threshold and yaw and roll are one turn; roll reads 0.
	matrix3x4_t m;
	AngleMatrix( QAngle( 90, 30, 0 ), m );
	float out[3];
	MatrixAngles( m, out );
	EXPECT_NEAR( out[0], 90, 1e-3f );
	EXPECT_NEAR( out[1], 30, 1e-3f );
	EXPECT_EQ( out[2], 0.0f );
}

TEST( MathlibPort, VectorAnglesOfTheAxes )
{
	QAngle a;
	VectorAngles( Vector( 1, 0, 0 ), a );
	EXPECT_EQ( a, QAngle( 0, 0, 0 ) );
	VectorAngles( Vector( -1, 0, 0 ), a );
	EXPECT_NEAR( a.y, 180, kTol );
	VectorAngles( Vector( 0, -1, 0 ), a );
	EXPECT_NEAR( a.y, 270, kTol );   // [0, 360), never negative
	VectorAngles( Vector( 1, 0, -1 ), a );
	EXPECT_NEAR( a.x, 45, kTol );    // down is positive pitch
	VectorAngles( Vector( 1, 0, 1 ), a );
	EXPECT_NEAR( a.x, 315, kTol );
	EXPECT_EQ( a.z, 0.0f );
}

TEST( MathlibPort, VectorAnglesStraightUpAndDown )
{
	QAngle a;
	VectorAngles( Vector( 0, 0, 5 ), a );
	EXPECT_EQ( a, QAngle( 270, 0, 0 ) );
	VectorAngles( Vector( 0, 0, -5 ), a );
	EXPECT_EQ( a, QAngle( 90, 0, 0 ) );
	VectorAngles( Vector( 0, 0, 0 ), a );
	EXPECT_EQ( a, QAngle( 90, 0, 0 ) );
}

TEST( MathlibPort, VectorTransformRotatesThenTranslates )
{
	matrix3x4_t m;
	AngleMatrix( QAngle( 0, 90, 0 ), Vector( 10, 20, 30 ), m );
	Vector out;
	VectorTransform( Vector( 1, 0, 0 ), m, out );   // mathlib.h's Vector& inline
	ExpectNear( out, 10, 21, 30 );
}

TEST( MathlibPort, VectorITransformUndoesVectorTransform )
{
	matrix3x4_t m;
	AngleMatrix( QAngle( 15, 25, 35 ), Vector( -5, 6, 7 ), m );
	const Vector in( 3, -4, 5 );
	Vector there, back;
	VectorTransform( in, m, there );
	VectorITransform( there, m, back );
	ExpectNear( back, 3, -4, 5 );
}

TEST( MathlibPort, VectorIRotateIgnoresTheTranslation )
{
	matrix3x4_t m;
	AngleMatrix( QAngle( 0, 90, 0 ), Vector( 100, 100, 100 ), m );
	Vector out;
	VectorIRotate( Vector( 0, 1, 0 ), m, out );   // the inverse of a 90-degree yaw
	ExpectNear( out, 1, 0, 0 );
}

TEST( MathlibPort, CalcSqrDistanceToAABBIsZeroInsideAndOnTheFaces )
{
	const Vector mins( -1, -2, -3 ), maxs( 1, 2, 3 );
	EXPECT_EQ( CalcSqrDistanceToAABB( mins, maxs, Vector( 0, 0, 0 ) ), 0.0f );
	EXPECT_EQ( CalcSqrDistanceToAABB( mins, maxs, Vector( 1, -2, 3 ) ), 0.0f );
}

TEST( MathlibPort, CalcSqrDistanceToAABBAddsEachAxisOvershoot )
{
	const Vector mins( -1, -2, -3 ), maxs( 1, 2, 3 );
	EXPECT_EQ( CalcSqrDistanceToAABB( mins, maxs, Vector( 4, 0, 0 ) ), 9.0f );           // past +X by 3
	EXPECT_EQ( CalcSqrDistanceToAABB( mins, maxs, Vector( 0, -6, 0 ) ), 16.0f );         // past -Y by 4
	EXPECT_EQ( CalcSqrDistanceToAABB( mins, maxs, Vector( -4, 6, -15 ) ), 9.0f + 16.0f + 144.0f );
}

} // namespace
