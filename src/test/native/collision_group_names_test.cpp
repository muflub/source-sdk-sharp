//========= SourceSharp — tests for the collision-group vocabulary ===========//
//
// C# names three of the engine's thirty collision groups and only the NUMBER
// crosses the boundary, so something has to translate. It used to be a switch;
// it became two the moment a projectile needed to ask whether the trigger it had
// just flown into was a dropped weapon — stock's own exemption in
// CMissile::MissileTouch — and two switches is a group written as one thing and
// read back as another, silently.
//
// So it is one table read both ways, and what is pinned here is that the
// lookups behave the way that arrangement promises:
//
//   - every name in the table survives the round trip, both directions;
//   - a name that is not in the table falls back to the FIRST row, in both
//     directions, so the two agree about the fallback as well;
//   - the mapping is many-to-one from the engine and never invents a name;
//   - an empty or absent table answers zero rather than reading off the end.
//
// The engine's own numbers are NOT here: const.h drags in the whole tier0 chain
// and this binary has no engine. The table below is the test's own, and that is
// the point — the rules hold for any table, so a row added to the real one in
// shell_entityapi.cpp cannot break them. The real table's numbers are checked
// from the C# side, where CollisionGroupParityTests reads both files.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/collision_group_names.h"

namespace
{

// A stand-in for the engine's values, deliberately NOT 0/1/2 — a lookup that
// returned its argument, or its index, would pass against 0/1/2 and fail here.
const int kEngineNone = 0;
const int kEngineVehicleClip = 9;
const int kEngineWeapon = 17;
const int kEngineDebris = 24;
const int kEngineInteractive = 25;
const int kEngineProjectile = 13;
const int kEngineBreakableGlass = 6;

const CollisionGroupName_t kNames[] = {
	{SS_COLLISION_GROUP_NONE, kEngineNone},
	{SS_COLLISION_GROUP_VEHICLE_CLIP, kEngineVehicleClip},
	{SS_COLLISION_GROUP_WEAPON, kEngineWeapon},
	{SS_COLLISION_GROUP_DEBRIS, kEngineDebris},
	{SS_COLLISION_GROUP_INTERACTIVE, kEngineInteractive},
	{SS_COLLISION_GROUP_PROJECTILE, kEngineProjectile},
	{SS_COLLISION_GROUP_BREAKABLE_GLASS, kEngineBreakableGlass},
};

const int kCount = sizeof(kNames) / sizeof(kNames[0]);

//-----------------------------------------------------------------------------
// Out and back as itself. This is the property the arrangement exists to have.
//-----------------------------------------------------------------------------
TEST(CollisionGroupNames, EveryNameSurvivesTheRoundTrip)
{
	for (int i = 0; i < kCount; ++i)
	{
		const int nGroup = kNames[i].nSourceSharp;

		EXPECT_EQ(nGroup, CollisionGroup_FromEngine(CollisionGroup_ToEngine(nGroup, kNames, kCount), kNames, kCount))
			<< "SourceSharp group " << nGroup << " did not come back as itself";
	}
}

//-----------------------------------------------------------------------------
// And the engine values are the table's, not the argument passed through. A
// lookup written as "return nGroup" passes the round trip above and puts a
// managed brush in whatever group happens to share its number.
//-----------------------------------------------------------------------------
TEST(CollisionGroupNames, TheEngineValueIsTheTablesOwn)
{
	EXPECT_EQ(kEngineNone, CollisionGroup_ToEngine(SS_COLLISION_GROUP_NONE, kNames, kCount));
	EXPECT_EQ(kEngineVehicleClip, CollisionGroup_ToEngine(SS_COLLISION_GROUP_VEHICLE_CLIP, kNames, kCount));
	EXPECT_EQ(kEngineWeapon, CollisionGroup_ToEngine(SS_COLLISION_GROUP_WEAPON, kNames, kCount));
	EXPECT_EQ(kEngineDebris, CollisionGroup_ToEngine(SS_COLLISION_GROUP_DEBRIS, kNames, kCount));
	EXPECT_EQ(kEngineInteractive, CollisionGroup_ToEngine(SS_COLLISION_GROUP_INTERACTIVE, kNames, kCount));
	EXPECT_EQ(kEngineProjectile, CollisionGroup_ToEngine(SS_COLLISION_GROUP_PROJECTILE, kNames, kCount));
	EXPECT_EQ(kEngineBreakableGlass, CollisionGroup_ToEngine(SS_COLLISION_GROUP_BREAKABLE_GLASS, kNames, kCount));

	EXPECT_EQ(SS_COLLISION_GROUP_VEHICLE_CLIP, CollisionGroup_FromEngine(kEngineVehicleClip, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_WEAPON, CollisionGroup_FromEngine(kEngineWeapon, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_DEBRIS, CollisionGroup_FromEngine(kEngineDebris, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_INTERACTIVE, CollisionGroup_FromEngine(kEngineInteractive, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_PROJECTILE, CollisionGroup_FromEngine(kEngineProjectile, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_BREAKABLE_GLASS, CollisionGroup_FromEngine(kEngineBreakableGlass, kNames, kCount));
}

//-----------------------------------------------------------------------------
// The engine has twenty-seven groups this mod cannot name. Every one of them
// reads as the first row rather than as an integer nobody chose — and so does a
// SourceSharp number that has not been defined yet, so the two directions agree
// about the fallback as well as about the names.
//-----------------------------------------------------------------------------
TEST(CollisionGroupNames, AnythingUnnamedFallsBackToTheFirstRow)
{
	EXPECT_EQ(SS_COLLISION_GROUP_NONE, CollisionGroup_FromEngine(3, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_NONE, CollisionGroup_FromEngine(29, kNames, kCount));
	EXPECT_EQ(SS_COLLISION_GROUP_NONE, CollisionGroup_FromEngine(-1, kNames, kCount));

	EXPECT_EQ(kEngineNone, CollisionGroup_ToEngine(99, kNames, kCount));
	EXPECT_EQ(kEngineNone, CollisionGroup_ToEngine(-7, kNames, kCount));
}

//-----------------------------------------------------------------------------
// The fallback is the first ROW, not the value zero. A table whose neutral pair
// is not zero on either side still falls back to that pair — which is what makes
// "the first row is the neutral one" a rule rather than a coincidence of the
// numbers the real table happens to use.
//-----------------------------------------------------------------------------
TEST(CollisionGroupNames, TheFallbackIsTheFirstRowAndNotZero)
{
	const CollisionGroupName_t offset[] = {
		{40, 50},
		{41, 51},
	};

	EXPECT_EQ(50, CollisionGroup_ToEngine(999, offset, 2));
	EXPECT_EQ(40, CollisionGroup_FromEngine(999, offset, 2));

	// And zero, which is a name in neither vocabulary here, is not special.
	EXPECT_EQ(50, CollisionGroup_ToEngine(0, offset, 2));
	EXPECT_EQ(40, CollisionGroup_FromEngine(0, offset, 2));
}

//-----------------------------------------------------------------------------
// No table at all. Both read off the end otherwise, which is the whole reason
// the fallback is spelt as pNames[0] rather than as a size-unaware default.
//-----------------------------------------------------------------------------
TEST(CollisionGroupNames, NoTableAnswersZeroRatherThanReadingOffTheEnd)
{
	EXPECT_EQ(0, CollisionGroup_ToEngine(SS_COLLISION_GROUP_WEAPON, NULL, 0));
	EXPECT_EQ(0, CollisionGroup_FromEngine(17, NULL, 0));
	EXPECT_EQ(0, CollisionGroup_ToEngine(SS_COLLISION_GROUP_WEAPON, kNames, 0));
	EXPECT_EQ(0, CollisionGroup_FromEngine(17, kNames, -1));
}

//-----------------------------------------------------------------------------
// The numbers C# holds a copy of. Only the number crosses, so a renumbering
// here that nobody mirrored would silently turn one name into another.
//-----------------------------------------------------------------------------
TEST(CollisionGroupNames, TheNumbersAreTheOnesCSharpHolds)
{
	EXPECT_EQ(0, (int)SS_COLLISION_GROUP_NONE);
	EXPECT_EQ(1, (int)SS_COLLISION_GROUP_VEHICLE_CLIP);
	EXPECT_EQ(2, (int)SS_COLLISION_GROUP_WEAPON);
	EXPECT_EQ(3, (int)SS_COLLISION_GROUP_DEBRIS);
	EXPECT_EQ(4, (int)SS_COLLISION_GROUP_INTERACTIVE);
}

} // namespace
