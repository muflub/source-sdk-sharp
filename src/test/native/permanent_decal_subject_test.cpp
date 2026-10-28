//========= SourceSharp — what a permanent decal is allowed to be STORED ON ===//
//
// THE BUG. SourceSharp_PaintDecal traces a short line through the position it
// was given and stores the mark against whatever it found. What it found is a
// pointer the ENGINE wrote, at the entity's IHandleEntity offset, and the shell
// narrowed it:
//
//     CBaseEntity *pHit = TraceEntity( trace );                // shell_worldquery.cpp
//     const int nEntityIndex = pHit ? pHit->entindex() : 0;
//
// TraceEntity()'s job is to answer NULL for anything outside the SDK's tree,
// and util_shared.h calls that NULL "the SAFE answer for an entity outside the
// SDK — it reads as hit the world". It is safe for a caller about to
// dereference a CBaseEntity. This caller does not dereference: it turns the
// answer into an ENTITY INDEX, and the index it turns NULL into is 0 — which is
// the world, a real entity with a real (identity) transform.
//
// So a permanent decal aimed at a managed brush was stored against the world at
// world coordinates. Two things then went wrong at once and neither said so:
//
//   * the wrong SUBJECT. "Painted on the managed crate" and "painted on
//     nothing" produced the identical engine call, so the mark did not travel
//     with the crate and did not go when the crate went.
//   * the wrong FRAME. Stock expresses the position in the hit entity's own
//     space precisely so a decal on a door does not stay behind when the door
//     opens. Against index 0 that frame is the identity, so the local position
//     was silently the world position.
//
// THE FIX crosses by the handle — `SourceSharp_ResolveEntity(
// SourceSharp_HandleFromAnyEntity( trace.m_pEnt ) )` — and asks the result for
// the index, the model and the frame. Not a hand-rolled cast: a handle names an
// entity rather than a C++ class, and a static prop, which is in the partition
// and is not an entity at all, has no valid handle and still comes out as 0.
//
// TWO HALVES, because the fault has two halves.
//
//   THE FRAME is real arithmetic and is exercised with real mathlib. The
//   projection's two arms ask two DIFFERENT functions, and the reason has to be
//   demonstrated rather than asserted: ICollideable::CollisionToWorldTransform
//   is NOT CBaseEntity::EntityToWorldTransform. CCollisionProperty's version
//   drops the rotation on the branch it takes for a SOLID_NONE, a SOLID_BBOX or
//   an FSOLID_FORCE_WORLD_ALIGNED entity, and keeps only the translation.
//   CStandaloneEntity's version has no such branch — SetAbsTransform builds
//   m_Transform with AngleMatrix from the origin AND the angles, every time —
//   so on THAT arm the two really are one matrix. The cases below measure the
//   gap, and measure that it closes at zero rotation, which is why an
//   axis-aligned test map could never have found this.
//
//   THE SUBJECT is a source scan, in impact_subject_test.cpp's idiom and for
//   its reason: what must stay true is a SHAPE at one call site, and no value a
//   gtest can compute would notice somebody spelling the narrowing back. Read
//   with whole-line comments stripped, because this file's own prose names
//   TraceEntity repeatedly and a check a comment can satisfy is a check about
//   comments.
//
// PROVEN RED. Documented per test at each TEST body.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"

#include "mathlib_test_env.h"

namespace
{

const float kEps = 0.001f;

//=============================================================================//
// HALF ONE — the frame, on real mathlib.
//=============================================================================//

// What CBaseEntity::EntityToWorldTransform hands back, and what
// CStandaloneEntity::CollisionToWorldTransform hands back: the same
// construction, m_rgflCoordinateFrame on one side and m_Transform on the other,
// both built from the origin and the angles together.
matrix3x4_t EntityFrame( const Vector &vecOrigin, const QAngle &angles )
{
	matrix3x4_t mat;
	AngleMatrix( angles, vecOrigin, mat );
	return mat;
}

// What CCollisionProperty::CollisionToWorldTransform hands back on the OTHER
// branch — !IsBoundsDefinedInEntitySpace(), which is a SOLID_NONE, a SOLID_BBOX
// or an FSOLID_FORCE_WORLD_ALIGNED entity. Identity, with the collision origin
// dropped into column three, and the rotation gone.
matrix3x4_t WorldAlignedFrame( const Vector &vecOrigin )
{
	matrix3x4_t mat;
	SetIdentityMatrix( mat );
	MatrixSetTranslation( vecOrigin, mat );
	return mat;
}

// The one line of SourceSharp_PaintDecal this half is about: a world point
// expressed in the frame the decal will be stored against.
Vector LocalPositionThrough( const matrix3x4_t &frame, const Vector &vecWorld )
{
	Vector vecLocal;
	VectorITransform( vecWorld, frame, vecLocal );
	return vecLocal;
}

// PROVEN RED: make WorldAlignedFrame call AngleMatrix instead of
// SetIdentityMatrix and this fails — which is the mutation that says the two
// really are one matrix, i.e. the mistake the projection exists to prevent.
TEST( PermanentDecalFrame, TheTwoTransformsDisagreeOnARotatedEntity )
{
	const Vector vecOrigin( 128.0f, -64.0f, 32.0f );
	const QAngle angles( 0.0f, 90.0f, 0.0f );          // a yawed brush
	const Vector vecHit( 160.0f, -64.0f, 40.0f );      // a point on its face

	const Vector vecEntitySpace =
		LocalPositionThrough( EntityFrame( vecOrigin, angles ), vecHit );
	const Vector vecWorldAligned =
		LocalPositionThrough( WorldAlignedFrame( vecOrigin ), vecHit );

	// 32 units along world +X from the origin. In the entity's own frame that
	// is 32 units along its LOCAL -Y, because the entity is yawed 90 degrees.
	EXPECT_NEAR( 0.0f,  vecEntitySpace.x, kEps );
	EXPECT_NEAR( -32.0f, vecEntitySpace.y, kEps );
	EXPECT_NEAR( 8.0f,  vecEntitySpace.z, kEps );

	// The world-aligned frame keeps the translation and nothing else, so the
	// same point comes out on +X.
	EXPECT_NEAR( 32.0f, vecWorldAligned.x, kEps );
	EXPECT_NEAR( 0.0f,  vecWorldAligned.y, kEps );
	EXPECT_NEAR( 8.0f,  vecWorldAligned.z, kEps );

	// And therefore: substituting one for the other moves the mark. 45.25
	// units, on a brush 32 units wide — a decal off the surface entirely.
	EXPECT_GT( ( vecEntitySpace - vecWorldAligned ).Length(), 1.0f );
}

// THE REASON THIS NEEDED A TEST RATHER THAN A READING. At zero rotation the two
// frames are the same matrix, so every axis-aligned map, and every hand check
// on one, agrees. The bug is invisible until something is turned.
//
// PROVEN RED: give `angles` a non-zero yaw here and the EXPECT_NEAR on y fails.
TEST( PermanentDecalFrame, TheyAgreeExactlyWhenNothingIsRotated )
{
	const Vector vecOrigin( 128.0f, -64.0f, 32.0f );
	const QAngle angles( 0.0f, 0.0f, 0.0f );
	const Vector vecHit( 160.0f, -64.0f, 40.0f );

	const Vector vecEntitySpace =
		LocalPositionThrough( EntityFrame( vecOrigin, angles ), vecHit );
	const Vector vecWorldAligned =
		LocalPositionThrough( WorldAlignedFrame( vecOrigin ), vecHit );

	EXPECT_NEAR( vecEntitySpace.x, vecWorldAligned.x, kEps );
	EXPECT_NEAR( vecEntitySpace.y, vecWorldAligned.y, kEps );
	EXPECT_NEAR( vecEntitySpace.z, vecWorldAligned.z, kEps );
}

// The OLD behaviour, priced. Narrowing answered NULL for a managed brush, the
// index came out 0, and index 0's frame is the identity — no translation
// either. So the "local" position handed to the engine was the raw world
// position, which for a brush anywhere but the map origin is not close.
//
// PROVEN RED: change the identity here to WorldAlignedFrame( vecOrigin ) — i.e.
// pretend the narrowed path at least kept the translation — and the
// EXPECT_NEARs on the world position fail.
TEST( PermanentDecalFrame, TheNarrowedAnswerStoredTheWorldPositionUntouched )
{
	const Vector vecOrigin( 128.0f, -64.0f, 32.0f );
	const QAngle angles( 0.0f, 90.0f, 0.0f );
	const Vector vecHit( 160.0f, -64.0f, 40.0f );

	matrix3x4_t matWorld;
	SetIdentityMatrix( matWorld );

	const Vector vecAgainstTheWorld = LocalPositionThrough( matWorld, vecHit );

	EXPECT_NEAR( vecHit.x, vecAgainstTheWorld.x, kEps );
	EXPECT_NEAR( vecHit.y, vecAgainstTheWorld.y, kEps );
	EXPECT_NEAR( vecHit.z, vecAgainstTheWorld.z, kEps );

	// And how far that is from where the mark belonged.
	const Vector vecCorrect =
		LocalPositionThrough( EntityFrame( vecOrigin, angles ), vecHit );

	EXPECT_GT( ( vecAgainstTheWorld - vecCorrect ).Length(), 100.0f );
}

//=============================================================================//
// HALF TWO — the subject, read out of the source.
//=============================================================================//

struct Hit_t
{
	int nLine;
	std::string strText;
};

} // namespace
