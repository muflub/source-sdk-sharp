//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the shortcut a de-entified volume takes ============//
//
// C_FuncSmokeVolume stopped being an entity and lost its collision property with
// it, so `CollisionProp()->WorldSpaceAABB( &mins, &maxs )` became two vector
// adds (world_aligned_bounds.h). Two vector adds are either exactly right or
// silently too small, and "too small" is a bounding box that culls the effect
// out of leaves it is actually in -- smoke that vanishes when the camera moves
// rather than an error anybody could see in a log.
//
// SO THE SHORTCUT IS CHECKED AGAINST THE REAL THING, not against a restatement
// of itself. The reference here is mathlib's own TransformAABB fed by
// AngleMatrix -- which is what CCollisionProperty::CollisionAABBToWorldAABB
// calls on its other branch, through CollisionToWorldTransform(). A test that
// re-derived `mins + origin` a second way and compared the two would pass
// forever and prove nothing; see docs/writing-tests.md, "A fixture that stands
// in for native must be checked against it".
//
// AND THE PRECONDITION IS TESTED AS WELL AS THE RULE. The addition is the right
// answer when the angles are identity. The second half of this file shows what
// it costs when they are not, so the comment in the header saying "the day
// something rotates, this line has to grow a TransformAABB" is a measured claim
// rather than a worry.
//
// Links mathlib, for the reason template_transform_test.cpp does: the claim is
// about what mathlib computes, and a hand-written stand-in for it would be the
// very thing being guarded against.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"

#include "mathlib_test_env.h"

#include "../game/shared/world_aligned_bounds.h"

namespace
{

const float kEps = 0.001f;

//-----------------------------------------------------------------------------
// What CCollisionProperty does on its OTHER branch: build the owner's transform
// and rotate the box through it. At identity angles this must agree with the
// shortcut, exactly; off identity it is the only correct answer.
//-----------------------------------------------------------------------------
void ReferenceTransform(const Vector& vecLocalMins, const Vector& vecLocalMaxs, const Vector& vecOrigin,
						const QAngle& angAngles, Vector* pWorldMins, Vector* pWorldMaxs)
{
	matrix3x4_t mat;
	AngleMatrix(angAngles, vecOrigin, mat);
	TransformAABB(mat, vecLocalMins, vecLocalMaxs, *pWorldMins, *pWorldMaxs);
}

void ExpectVectorNear(const Vector& a, const Vector& b, const char* pWhat)
{
	EXPECT_NEAR(a.x, b.x, kEps) << pWhat << " x";
	EXPECT_NEAR(a.y, b.y, kEps) << pWhat << " y";
	EXPECT_NEAR(a.z, b.z, kEps) << pWhat << " z";
}

struct Case
{
	Vector mins, maxs, origin;
};

const Case kCases[] = {
	// A brush at the world origin, which is the ordinary func_smokevolume: a
	// brush model's bounds are already where the brush is.
	{Vector(-64, -64, 0), Vector(64, 64, 128), Vector(0, 0, 0)},

	// The same brush, moved. This is the case the addition exists for.
	{Vector(-64, -64, 0), Vector(64, 64, 128), Vector(512, -256, 64)},

	// Asymmetric, so a bug that averages or mirrors shows up.
	{Vector(-10, -200, -3), Vector(400, 5, 7), Vector(-33, 77, 12.5f)},

	// Degenerate: a point. The grid would fill nothing, but the arithmetic must
	// still be the arithmetic.
	{Vector(0, 0, 0), Vector(0, 0, 0), Vector(9, -9, 0.25f)},

	// Fractional, because origins are interpolated floats and not integers.
	{Vector(-1.5f, -2.25f, -0.125f), Vector(1.5f, 2.25f, 0.125f), Vector(0.5f, 0.5f, 0.5f)},
};

//=============================================================================
// The rule
//=============================================================================

TEST(WorldAlignedBounds, AgreesWithMathlibTransformAABBAtIdentityAngles)
{
	for (size_t i = 0; i < ARRAYSIZE(kCases); i++)
	{
		const Case& c = kCases[i];

		Vector shortcutMins, shortcutMaxs;
		SourceSharp_BoundsToWorld(c.mins, c.maxs, c.origin, &shortcutMins, &shortcutMaxs);

		Vector refMins, refMaxs;
		ReferenceTransform(c.mins, c.maxs, c.origin, vec3_angle, &refMins, &refMaxs);

		ExpectVectorNear(shortcutMins, refMins, "mins");
		ExpectVectorNear(shortcutMaxs, refMaxs, "maxs");
	}
}

TEST(WorldAlignedBounds, TheOriginIsAddedRatherThanReplacingTheBox)
{
	// The failure a "close enough" implementation makes: writing the origin into
	// the output and forgetting the box, which for a brush at the world origin
	// looks completely correct.
	Vector mins, maxs;
	SourceSharp_BoundsToWorld(Vector(-64, -64, 0), Vector(64, 64, 128), Vector(100, 200, 300), &mins, &maxs);

	ExpectVectorNear(mins, Vector(36, 136, 300), "mins");
	ExpectVectorNear(maxs, Vector(164, 264, 428), "maxs");
}

TEST(WorldAlignedBounds, AZeroOriginLeavesTheBoxAlone)
{
	Vector mins, maxs;
	SourceSharp_BoundsToWorld(Vector(-3, -4, -5), Vector(6, 7, 8), vec3_origin, &mins, &maxs);

	ExpectVectorNear(mins, Vector(-3, -4, -5), "mins");
	ExpectVectorNear(maxs, Vector(6, 7, 8), "maxs");
}

//=============================================================================
// The precondition
//=============================================================================

TEST(WorldAlignedBounds, RotatingTheOwnerMakesTheShortcutTOOSMALL)
{
	// The header's stated limit, measured. A rotated box needs a LARGER
	// axis-aligned box around it, and the shortcut does not grow -- so the day a
	// smoke volume acquires angles, this is the bug: an effect culled out of
	// leaves it occupies.
	const Vector mins(-64, -64, 0);
	const Vector maxs(64, 64, 128);
	const Vector origin(512, -256, 64);
	const QAngle angles(0, 45, 0);

	Vector shortcutMins, shortcutMaxs;
	SourceSharp_BoundsToWorld(mins, maxs, origin, &shortcutMins, &shortcutMaxs);

	Vector refMins, refMaxs;
	ReferenceTransform(mins, maxs, origin, angles, &refMins, &refMaxs);

	// Strictly larger in x and y -- 45 degrees about z is the worst case for a
	// square footprint, and z is untouched by a yaw.
	EXPECT_LT(refMins.x, shortcutMins.x - 1.0f);
	EXPECT_LT(refMins.y, shortcutMins.y - 1.0f);
	EXPECT_GT(refMaxs.x, shortcutMaxs.x + 1.0f);
	EXPECT_GT(refMaxs.y, shortcutMaxs.y + 1.0f);

	EXPECT_NEAR(refMins.z, shortcutMins.z, kEps);
	EXPECT_NEAR(refMaxs.z, shortcutMaxs.z, kEps);
}

TEST(WorldAlignedBounds, ARotationOfAWHOLETURNIsStillIdentity)
{
	// Not a curiosity: it is the check that the disagreement above is about the
	// ROTATION rather than about TransformAABB and the shortcut simply being two
	// different computations that never agree.
	const Vector mins(-10, -200, -3);
	const Vector maxs(400, 5, 7);
	const Vector origin(-33, 77, 12.5f);

	Vector shortcutMins, shortcutMaxs;
	SourceSharp_BoundsToWorld(mins, maxs, origin, &shortcutMins, &shortcutMaxs);

	Vector refMins, refMaxs;
	ReferenceTransform(mins, maxs, origin, QAngle(0, 360, 0), &refMins, &refMaxs);

	ExpectVectorNear(shortcutMins, refMins, "mins");
	ExpectVectorNear(shortcutMaxs, refMaxs, "maxs");
}

//=============================================================================
// The whole rule, as the standalone shells use it
//=============================================================================

// dm_lockdown's controlroom_desk001b: its model box, its origin, yaw 135.
const Vector kDeskMins(-21.35f, -55.89f, -16.80f);
const Vector kDeskMaxs(20.60f, 55.90f, 17.09f);
const Vector kDeskOrigin(-3778.31f, 4451.8f, 16.744f);
const QAngle kDeskAngles(0, 135, 0);

TEST(CollisionBoundsToWorld, ARotatedVPhysicsBoxIsRotatedAndReBounded)
{
	Vector mins, maxs;
	SourceSharp_CollisionBoundsToWorld(SOLID_VPHYSICS, 0, kDeskMins, kDeskMaxs, kDeskOrigin, kDeskAngles, &mins, &maxs);

	Vector refMins, refMaxs;
	ReferenceTransform(kDeskMins, kDeskMaxs, kDeskOrigin, kDeskAngles, &refMins, &refMaxs);

	ExpectVectorNear(mins, refMins, "mins");
	ExpectVectorNear(maxs, refMaxs, "maxs");

	// The control: the addition alone is the box the desk was traced through.
	Vector addedMins, addedMaxs;
	SourceSharp_BoundsToWorld(kDeskMins, kDeskMaxs, kDeskOrigin, &addedMins, &addedMaxs);
	EXPECT_LT(mins.x, addedMins.x - 10.0f) << "the turned desk reaches past its unturned box";
}

TEST(CollisionBoundsToWorld, ABBoxStaysWorldAlignedWhateverItsAngles)
{
	// A player's hull and a pickup's trigger box: SOLID_BBOX is world-aligned.
	Vector mins, maxs;
	SourceSharp_CollisionBoundsToWorld(SOLID_BBOX, 0, kDeskMins, kDeskMaxs, kDeskOrigin, kDeskAngles, &mins, &maxs);

	ExpectVectorNear(mins, kDeskOrigin + kDeskMins, "mins");
	ExpectVectorNear(maxs, kDeskOrigin + kDeskMaxs, "maxs");
}

TEST(CollisionBoundsToWorld, ForcedWorldAlignedStaysWorldAligned)
{
	Vector mins, maxs;
	SourceSharp_CollisionBoundsToWorld(SOLID_VPHYSICS, FSOLID_FORCE_WORLD_ALIGNED, kDeskMins, kDeskMaxs, kDeskOrigin,
									   kDeskAngles, &mins, &maxs);

	ExpectVectorNear(mins, kDeskOrigin + kDeskMins, "mins");
	ExpectVectorNear(maxs, kDeskOrigin + kDeskMaxs, "maxs");
}

TEST(CollisionBoundsToWorld, AnEntitySpaceBoxAtIdentityAnglesIsTheAddition)
{
	Vector mins, maxs;
	SourceSharp_CollisionBoundsToWorld(SOLID_BSP, 0, kDeskMins, kDeskMaxs, kDeskOrigin, vec3_angle, &mins, &maxs);

	ExpectVectorNear(mins, kDeskOrigin + kDeskMins, "mins");
	ExpectVectorNear(maxs, kDeskOrigin + kDeskMaxs, "maxs");
}

TEST(CollisionBoundsToWorld, OnlyBBoxNoneAndForcedAlignedAreWorldSpace)
{
	EXPECT_FALSE(SourceSharp_BoundsInEntitySpace(SOLID_NONE, 0));
	EXPECT_FALSE(SourceSharp_BoundsInEntitySpace(SOLID_BBOX, 0));
	EXPECT_TRUE(SourceSharp_BoundsInEntitySpace(SOLID_BSP, 0));
	EXPECT_TRUE(SourceSharp_BoundsInEntitySpace(SOLID_OBB, 0));
	EXPECT_TRUE(SourceSharp_BoundsInEntitySpace(SOLID_VPHYSICS, 0));
	EXPECT_FALSE(SourceSharp_BoundsInEntitySpace(SOLID_VPHYSICS, FSOLID_FORCE_WORLD_ALIGNED));
}

} // namespace

//-----------------------------------------------------------------------------
// The trigger bloat (CCollisionProperty::WorldSpaceTriggerBounds and the tail
// of ComputeSurroundingBox). Found by stock-ab's solidity census (2026-10-26):
// stock's items and weapons carry FSOLID_USE_TRIGGER_BOUNDS, and a player
// picks one up 24 (36) units before reaching its box.
//-----------------------------------------------------------------------------

TEST(TriggerBounds, WithoutTheFlagTheBoxIsUntouched)
{
	Vector vecMins(-10, -10, 0), vecMaxs(10, 10, 20);
	SourceSharp_TriggerBounds(FSOLID_TRIGGER, 24, false, &vecMins, &vecMaxs);
	EXPECT_EQ(Vector(-10, -10, 0), vecMins);
	EXPECT_EQ(Vector(10, 10, 20), vecMaxs);
}

TEST(TriggerBounds, TheDefaultBloatsTheSidesAndHalfTheTopButNotTheBottom)
{
	Vector vecMins(-10, -10, 0), vecMaxs(10, 10, 20);
	SourceSharp_TriggerBounds(FSOLID_TRIGGER | FSOLID_USE_TRIGGER_BOUNDS, 24, false, &vecMins, &vecMaxs);
	EXPECT_EQ(Vector(-34, -34, 0), vecMins);
	EXPECT_EQ(Vector(34, 34, 32), vecMaxs);
}

TEST(TriggerBounds, UniformBloatGrowsEverySide)
{
	Vector vecMins(-10, -10, 0), vecMaxs(10, 10, 20);
	SourceSharp_TriggerBounds(FSOLID_USE_TRIGGER_BOUNDS, 36, true, &vecMins, &vecMaxs);
	EXPECT_EQ(Vector(-46, -46, -36), vecMins);
	EXPECT_EQ(Vector(46, 46, 56), vecMaxs);
}

TEST(TriggerBounds, TheSurroundingBoxTakesInTheTriggerBox)
{
	Vector vecMins(-10, -10, 0), vecMaxs(10, 10, 20);
	SourceSharp_SurroundingBounds(FSOLID_USE_TRIGGER_BOUNDS, 24, false, &vecMins, &vecMaxs);
	EXPECT_EQ(Vector(-34, -34, 0), vecMins);
	EXPECT_EQ(Vector(34, 34, 32), vecMaxs);

	Vector vecPlainMins(-10, -10, 0), vecPlainMaxs(10, 10, 20);
	SourceSharp_SurroundingBounds(0, 24, false, &vecPlainMins, &vecPlainMaxs);
	EXPECT_EQ(Vector(-10, -10, 0), vecPlainMins);
	EXPECT_EQ(Vector(10, 10, 20), vecPlainMaxs);
}
