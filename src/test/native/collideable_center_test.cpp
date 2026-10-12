//========= SourceSharp — tests for WorldSpaceCenter over an ICollideable =====//
//
// plan_baseentity.md Phase 13 precondition (e), client half. Call sites that
// held a C_BaseEntity * for WorldSpaceCenter() now hold the interface and ask
// collideable_center_math.h instead. The identity claimed there — same arm,
// same expression, term for term, as CCollisionProperty::WorldSpaceCenter —
// is an argument about two other files, so what is PINNED here is everything
// that argument depends on this header actually doing:
//
//   - the midpoint is VectorLerp's expression, not (a+b)/2;
//   - the add arm adds the collision origin and reads nothing else;
//   - the transform arm is VectorTransform's expression against a 3x4 row-major
//     matrix, translation in column 3;
//   - the arm is selected by the angles flag alone.
//
// Each clause has a case that goes red if the clause is broken. The transform
// arm's expected values are computed by hand in the test, not by the header,
// so the two cannot agree by both being wrong the same way.
//
// No engine, no mathlib: the header takes floats, deliberately, so this binary
// can exist — the same shape as bone_cache_key_test.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/collideable_center_math.h"

namespace
{

const float kPoison = -8192.5f; // A transform that must never be read.

//-----------------------------------------------------------------------------
// The add arm: world-aligned or unrotated bounds. Center is the box midpoint
// plus the collision origin.
//-----------------------------------------------------------------------------
TEST( CollideableCenter, AddArmIsMidpointPlusOrigin )
{
	const float mins[3]   = { -16.0f, -16.0f, 0.0f };
	const float maxs[3]   = {  16.0f,  16.0f, 72.0f };
	const float origin[3] = { 100.0f, -50.0f, 8.0f };
	float xform[12];
	for ( int i = 0; i < 12; ++i ) xform[i] = kPoison;

	float out[3];
	CollideableCenter_Compute( mins, maxs, true, origin, xform, out );

	EXPECT_FLOAT_EQ( 100.0f, out[0] );
	EXPECT_FLOAT_EQ( -50.0f, out[1] );
	EXPECT_FLOAT_EQ(  44.0f, out[2] ); // 36 up the box, 8 up the origin
}

//-----------------------------------------------------------------------------
// The midpoint is a lerp, and the box need not be symmetric around its origin.
// An implementation that halves the maxs, or midpoints toward zero, fails here.
//-----------------------------------------------------------------------------
TEST( CollideableCenter, MidpointHonoursAsymmetricBounds )
{
	const float mins[3]   = { -2.0f, -4.0f,  0.0f };
	const float maxs[3]   = {  6.0f,  4.0f, 10.0f };
	const float origin[3] = {  0.0f,  0.0f,  0.0f };

	float out[3];
	CollideableCenter_Compute( mins, maxs, true, origin, NULL, out );

	EXPECT_FLOAT_EQ( 2.0f, out[0] );
	EXPECT_FLOAT_EQ( 0.0f, out[1] );
	EXPECT_FLOAT_EQ( 5.0f, out[2] );
}

//-----------------------------------------------------------------------------
// A point box collapses to its point: the degenerate case decals and beams
// actually produce (SOLID_NONE entities report zero bounds).
//-----------------------------------------------------------------------------
TEST( CollideableCenter, PointBoundsCenterOnTheOrigin )
{
	const float mins[3]   = { 0.0f, 0.0f, 0.0f };
	const float maxs[3]   = { 0.0f, 0.0f, 0.0f };
	const float origin[3] = { -320.0f, 64.0f, 128.25f };

	float out[3];
	CollideableCenter_Compute( mins, maxs, true, origin, NULL, out );

	EXPECT_FLOAT_EQ( -320.0f, out[0] );
	EXPECT_FLOAT_EQ(   64.0f, out[1] );
	EXPECT_FLOAT_EQ(  128.25f, out[2] );
}

//-----------------------------------------------------------------------------
// The transform arm: a 90-degree yaw about Z, plus a translation. World +x
// becomes +y. Expected values are hand-computed from the matrix here, not
// derived through the header.
//
//   yaw 90:  row0 = ( 0, -1, 0 | tx )
//            row1 = ( 1,  0, 0 | ty )
//            row2 = ( 0,  0, 1 | tz )
//-----------------------------------------------------------------------------
TEST( CollideableCenter, TransformArmRotatesAndTranslates )
{
	const float mins[3]   = { 0.0f, -8.0f, 0.0f };
	const float maxs[3]   = { 20.0f, 8.0f, 4.0f }; // center in OBB space: (10, 0, 2)
	const float origin[3] = { kPoison, kPoison, kPoison }; // must not be read on this arm
	const float xform[12] = { 0.0f, -1.0f, 0.0f, 50.0f,
	                          1.0f,  0.0f, 0.0f, -30.0f,
	                          0.0f,  0.0f, 1.0f, 7.0f };

	float out[3];
	CollideableCenter_Compute( mins, maxs, false, origin, xform, out );

	// (10, 0, 2) rotated: (0*10 + -1*0, 1*10 + 0*0, 2) = (0, 10, 2); translated.
	EXPECT_FLOAT_EQ( 50.0f, out[0] );
	EXPECT_FLOAT_EQ( -20.0f, out[1] );
	EXPECT_FLOAT_EQ(  9.0f, out[2] );
}

//-----------------------------------------------------------------------------
// The two arms agree when the transform happens to be a pure translation —
// which is the case CCollisionProperty builds for world-aligned bounds
// (SetIdentityMatrix + MatrixSetColumn(origin, 3)). This is the agreement the
// header's guard simplification leans on, stated as a test rather than only as
// a comment.
//-----------------------------------------------------------------------------
TEST( CollideableCenter, ArmsAgreeOnPureTranslation )
{
	const float mins[3]   = { -3.0f, -5.0f, -7.0f };
	const float maxs[3]   = {  9.0f, 11.0f, 13.0f };
	const float origin[3] = { 12.0f, -34.0f, 56.0f };
	const float xform[12] = { 1.0f, 0.0f, 0.0f, 12.0f,
	                          0.0f, 1.0f, 0.0f, -34.0f,
	                          0.0f, 0.0f, 1.0f, 56.0f };

	float outAdd[3], outXform[3];
	CollideableCenter_Compute( mins, maxs, true,  origin, NULL,  outAdd );
	CollideableCenter_Compute( mins, maxs, false, origin, xform, outXform );

	EXPECT_FLOAT_EQ( outAdd[0], outXform[0] );
	EXPECT_FLOAT_EQ( outAdd[1], outXform[1] );
	EXPECT_FLOAT_EQ( outAdd[2], outXform[2] );
}

} // namespace
