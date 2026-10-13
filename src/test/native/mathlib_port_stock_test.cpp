//========= SourceSharp — server.so's mathlib port, bit for bit against stock ==//
//
// server.so links no libmathlib.a: mathlib_port.cpp defines the mathlib
// functions it calls, each forwarding to an ss_mathlib:: inline in
// mathlib_port.h. This binary links the real libmathlib.a, so the global names
// below are STOCK, and calls each ss_mathlib:: twin on the same inputs in the
// same process: one CPU, one libm, one set of flags for the sine and the
// arctangent. Every output float must have the same bits. A port that rounds
// one product differently, swaps a branch or drops a sign is red here.
//
// The inputs are a fixed list of edge cases (the axes, straight up and down,
// the 0.001 gimbal threshold MatrixAngles switches at, signed zeros) and a few
// thousand pseudo-random ones from a fixed seed, so a failure reproduces.
//
// Linux only, as every mathlib binary is (ss_cpptest_mathlib): MathLib_Init
// needs a tier0 the SDK ships only for Linux. mathlib_port_test runs the port
// alone on both platforms.
//=============================================================================//

#include "mathlib_test_env.h"

#include "gtest/gtest.h"

#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib_port.h"

#include <stdint.h>
#include <string.h>

#include <vector>

namespace
{

uint32_t Bits( float f )
{
	uint32_t u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

// A fixed-seed generator: the same inputs on every run and every machine.
class Inputs
{
public:
	float Uniform( float lo, float hi )
	{
		m_state = m_state * 6364136223846793005ull + 1442695040888963407ull;
		const float unit = (float)( m_state >> 40 ) / (float)( 1ull << 24 );
		return lo + ( hi - lo ) * unit;
	}

private:
	uint64_t m_state = 0x5eed5eedull;
};

std::vector<QAngle> Angles()
{
	std::vector<QAngle> out = {
		QAngle( 0, 0, 0 ), QAngle( 90, 0, 0 ), QAngle( -90, 0, 0 ),
		QAngle( 0, 90, 0 ), QAngle( 0, 180, 0 ), QAngle( 0, -180, 0 ),
		QAngle( 0, 0, 90 ), QAngle( 45, 45, 45 ), QAngle( -30, 270, 15 ),
		QAngle( 89.9f, 10, 5 ), QAngle( 90, 33, 77 ), QAngle( -90, -33, 12 ),
		QAngle( 360, 720, -360 ), QAngle( -0.0f, -0.0f, -0.0f ),
	};
	Inputs rng;
	for ( int i = 0; i < 2000; ++i )
		out.push_back( QAngle( rng.Uniform( -720, 720 ), rng.Uniform( -720, 720 ), rng.Uniform( -720, 720 ) ) );
	return out;
}

std::vector<Vector> Vectors()
{
	std::vector<Vector> out = {
		Vector( 0, 0, 0 ), Vector( 1, 0, 0 ), Vector( -1, 0, 0 ),
		Vector( 0, 1, 0 ), Vector( 0, -1, 0 ), Vector( 0, 0, 1 ),
		Vector( 0, 0, -1 ), Vector( 1, 1, 1 ), Vector( -3, 4, -5 ),
		Vector( 1e-7f, 0, 1 ), Vector( 0, -1e-7f, -1 ), Vector( -0.0f, 0.0f, -0.0f ),
		Vector( 16384, -16384, 4096 ),
	};
	Inputs rng;
	for ( int i = 0; i < 2000; ++i )
		out.push_back( Vector( rng.Uniform( -4096, 4096 ), rng.Uniform( -4096, 4096 ), rng.Uniform( -4096, 4096 ) ) );
	return out;
}

// Rotations AngleMatrix builds, with a translation, and a few hand-made
// matrices: the identity, a pure translation, and forward along +Z and -Z,
// with horizontal lengths either side of MatrixAngles' 0.001 threshold.
std::vector<matrix3x4_t> Matrices()
{
	std::vector<matrix3x4_t> out;
	matrix3x4_t m;
	SetIdentityMatrix( m );
	out.push_back( m );
	m[0][3] = 10; m[1][3] = -20; m[2][3] = 30;
	out.push_back( m );
	const float nearVertical[] = { 0.0f, 0.0005f, 0.000999f, 0.001001f, 0.002f };
	for ( float h : nearVertical )
	{
		for ( float up : { 1.0f, -1.0f } )
		{
			matrix3x4_t g( h, 0.6f, 0.0f, 1.0f,
			               0.0f, 0.8f, 0.0f, 2.0f,
			               -up, 0.0f, 0.3f, 3.0f );
			out.push_back( g );
		}
	}
	Inputs rng;
	for ( const QAngle &a : Angles() )
	{
		const Vector at( rng.Uniform( -4096, 4096 ), rng.Uniform( -4096, 4096 ), rng.Uniform( -4096, 4096 ) );
		AngleMatrix( a, at, m );
		out.push_back( m );
	}
	return out;
}

::testing::AssertionResult SameBits( const float *port, const float *stock, int n, const char *what, int index )
{
	for ( int i = 0; i < n; ++i )
	{
		if ( Bits( port[i] ) != Bits( stock[i] ) )
		{
			return ::testing::AssertionFailure()
				<< what << " input #" << index << " component " << i << ": port "
				<< port[i] << " (0x" << std::hex << Bits( port[i] ) << ") stock "
				<< stock[i] << " (0x" << Bits( stock[i] ) << ")";
		}
	}
	return ::testing::AssertionSuccess();
}

::testing::AssertionResult SameMatrix( const matrix3x4_t &port, const matrix3x4_t &stock, const char *what, int index )
{
	return SameBits( port.Base(), stock.Base(), 12, what, index );
}

TEST( MathlibPortStock, TheInputListsReachEveryBranch )
{
	// The stimulus: both arms of MatrixAngles' threshold and of VectorAngles'
	// vertical case are in the lists, or the comparisons below prove less.
	int gimbal = 0, upright = 0, vertical = 0, horizontal = 0;
	for ( const matrix3x4_t &m : Matrices() )
	{
		const float h = sqrtf( m[0][0] * m[0][0] + m[1][0] * m[1][0] );
		( h > 0.001f ? upright : gimbal )++;
	}
	for ( const Vector &v : Vectors() )
		( v.x == 0 && v.y == 0 ? vertical : horizontal )++;
	EXPECT_GE( gimbal, 4 );
	EXPECT_GE( upright, 2000 );
	EXPECT_GE( vertical, 3 );
	EXPECT_GE( horizontal, 2000 );
}

TEST( MathlibPortStock, Vec3OriginAndVec3AngleAreZero )
{
	// The port defines its own; stock's are these two.
	EXPECT_EQ( vec3_origin, Vector( 0, 0, 0 ) );
	EXPECT_EQ( vec3_angle, QAngle( 0, 0, 0 ) );
}

TEST( MathlibPortStock, AngleMatrixMatchesStock )
{
	const std::vector<QAngle> angles = Angles();
	for ( int i = 0; i < (int)angles.size(); ++i )
	{
		matrix3x4_t port, stock;
		ss_mathlib::AngleMatrix( angles[i], port );
		AngleMatrix( angles[i], stock );
		ASSERT_TRUE( SameMatrix( port, stock, "AngleMatrix", i ) );

		const Vector at( 1.5f * i, -2.5f * i, 3.5f );
		ss_mathlib::AngleMatrix( angles[i], at, port );
		AngleMatrix( angles[i], at, stock );
		ASSERT_TRUE( SameMatrix( port, stock, "AngleMatrix(position)", i ) );
	}
}

TEST( MathlibPortStock, AngleVectorsForwardMatchesStock )
{
	const std::vector<QAngle> angles = Angles();
	for ( int i = 0; i < (int)angles.size(); ++i )
	{
		Vector port, stock;
		ss_mathlib::AngleVectors( angles[i], &port );
		AngleVectors( angles[i], &stock );
		ASSERT_TRUE( SameBits( port.Base(), stock.Base(), 3, "AngleVectors(forward)", i ) );
	}
}

TEST( MathlibPortStock, AngleVectorsBasisMatchesStock )
{
	const std::vector<QAngle> angles = Angles();
	for ( int i = 0; i < (int)angles.size(); ++i )
	{
		Vector pf, pr, pu, sf, sr, su;
		ss_mathlib::AngleVectors( angles[i], &pf, &pr, &pu );
		AngleVectors( angles[i], &sf, &sr, &su );
		ASSERT_TRUE( SameBits( pf.Base(), sf.Base(), 3, "AngleVectors forward", i ) );
		ASSERT_TRUE( SameBits( pr.Base(), sr.Base(), 3, "AngleVectors right", i ) );
		ASSERT_TRUE( SameBits( pu.Base(), su.Base(), 3, "AngleVectors up", i ) );
	}
}

TEST( MathlibPortStock, VectorAnglesMatchesStock )
{
	const std::vector<Vector> dirs = Vectors();
	for ( int i = 0; i < (int)dirs.size(); ++i )
	{
		QAngle port, stock;
		ss_mathlib::VectorAngles( dirs[i], port );
		VectorAngles( dirs[i], stock );
		ASSERT_TRUE( SameBits( port.Base(), stock.Base(), 3, "VectorAngles", i ) );
	}
}

TEST( MathlibPortStock, MatrixAnglesMatchesStock )
{
	const std::vector<matrix3x4_t> mats = Matrices();
	for ( int i = 0; i < (int)mats.size(); ++i )
	{
		float port[3], stock[3];
		ss_mathlib::MatrixAngles( mats[i], port );
		MatrixAngles( mats[i], stock );
		ASSERT_TRUE( SameBits( port, stock, 3, "MatrixAngles", i ) );
	}
}

TEST( MathlibPortStock, SetIdentityMatrixMatchesStock )
{
	matrix3x4_t port, stock;
	memset( port.Base(), 0x7f, sizeof( port ) );
	memset( stock.Base(), 0x7f, sizeof( stock ) );
	ss_mathlib::SetIdentityMatrix( port );
	SetIdentityMatrix( stock );
	EXPECT_TRUE( SameMatrix( port, stock, "SetIdentityMatrix", 0 ) );
}

TEST( MathlibPortStock, MatrixGetColumnMatchesStock )
{
	const std::vector<matrix3x4_t> mats = Matrices();
	for ( int i = 0; i < (int)mats.size(); ++i )
	{
		for ( int column = 0; column < 4; ++column )
		{
			Vector port, stock;
			ss_mathlib::MatrixGetColumn( mats[i], column, port );
			MatrixGetColumn( mats[i], column, stock );
			ASSERT_TRUE( SameBits( port.Base(), stock.Base(), 3, "MatrixGetColumn", i ) );
		}
	}
}

TEST( MathlibPortStock, TheThreeTransformsMatchStock )
{
	const std::vector<matrix3x4_t> mats = Matrices();
	const std::vector<Vector> points = Vectors();
	for ( int i = 0; i < (int)mats.size(); ++i )
	{
		const Vector &in = points[i % points.size()];
		float port[3], stock[3];

		ss_mathlib::VectorTransform( in.Base(), mats[i], port );
		VectorTransform( in.Base(), mats[i], stock );
		ASSERT_TRUE( SameBits( port, stock, 3, "VectorTransform", i ) );

		ss_mathlib::VectorITransform( in.Base(), mats[i], port );
		VectorITransform( in.Base(), mats[i], stock );
		ASSERT_TRUE( SameBits( port, stock, 3, "VectorITransform", i ) );

		ss_mathlib::VectorIRotate( in.Base(), mats[i], port );
		VectorIRotate( in.Base(), mats[i], stock );
		ASSERT_TRUE( SameBits( port, stock, 3, "VectorIRotate", i ) );
	}
}

TEST( MathlibPortStock, CalcSqrDistanceToAABBMatchesStock )
{
	// Points inside, on a face, and past each face on one, two and three axes.
	const std::vector<Vector> points = Vectors();
	const Vector mins( -100, -200, -300 ), maxs( 100, 200, 300 );
	for ( int i = 0; i < (int)points.size(); ++i )
	{
		const float port = ss_mathlib::CalcSqrDistanceToAABB( mins, maxs, points[i] );
		const float stock = CalcSqrDistanceToAABB( mins, maxs, points[i] );
		ASSERT_TRUE( SameBits( &port, &stock, 1, "CalcSqrDistanceToAABB", i ) );
	}
	const Vector onFace( 100, 0, 0 );
	EXPECT_EQ( Bits( ss_mathlib::CalcSqrDistanceToAABB( mins, maxs, onFace ) ),
	           Bits( CalcSqrDistanceToAABB( mins, maxs, onFace ) ) );
}

} // namespace
