//========= SourceSharp — the client asks the group matrix too ================//
//
// The client's prediction clips the solid entities the client's engine trace
// cannot see (the shells are in no spatial partition), and decides which of
// them a movement trace may stop on as the server's trace filter does: by the
// pair of collision groups (CollisionRules). A debris can or a pushaway prop
// is solid on both sides and still passed by a player's movement, so without
// the group on the wire the client stopped on every can the server walked
// through. Stock sends the same word (baseentity.cpp: SendPropInt(SENDINFO(
// m_CollisionGroup), 5, SPROP_UNSIGNED)), so the row is named and sized for it.
//
// WHAT THIS FILE HOLDS. The row is on the shared list both sides' builders
// subscript, at the tail (the list's order is the wire format), as an INT of
// five bits: enough for every shared group and TF2's, which end below 32.
//
//=============================================================================//

#include "gtest/gtest.h"

// What standalone_nettable.h wants from the engine, as
// standalone_bounds_wire_test.cpp spells it.
#define MAX_EDICT_BITS 11
#define EF_MAX_BITS 10
#define ARRAYSIZE(p) ((int)(sizeof(p) / sizeof((p)[0])))

#include "../game/shared/standalone_nettable.h"

namespace
{

TEST(StandaloneCollisionGroupWire, TheWireCarriesTheCollisionGroup)
{
	int nCount = 0;
	const StandaloneNetField_t* pFields = StandaloneNetFields(&nCount);
	ASSERT_EQ(STANDALONE_NET_FIELD_COUNT, nCount);

	EXPECT_STREQ("m_CollisionGroup", pFields[STANDALONE_NET_FIELD_COLLISIONGROUP].pName)
		<< "the enum's COLLISIONGROUP index does not name the group row, so "
		   "the offset written at this index lands on another prop.";
	EXPECT_EQ(STANDALONE_NET_INT, pFields[STANDALONE_NET_FIELD_COLLISIONGROUP].kind);
}

TEST(StandaloneCollisionGroupWire, FiveBitsHoldEveryGroup)
{
	int nCount = 0;
	const StandaloneNetField_t* pFields = StandaloneNetFields(&nCount);
	ASSERT_EQ(STANDALONE_NET_FIELD_COUNT, nCount);

	// Stock's width. TF2's last group (TFCOLLISION_GROUP_ROCKET_BUT_NOT_WITH_
	// OTHER_ROCKETS, tf_shareddefs.h) is LAST_SHARED_COLLISION_GROUP (20) + 9.
	EXPECT_EQ(5, pFields[STANDALONE_NET_FIELD_COLLISIONGROUP].nBits);
	EXPECT_LT(20 + 9, 1 << pFields[STANDALONE_NET_FIELD_COLLISIONGROUP].nBits);
}

TEST(StandaloneCollisionGroupWire, TheGroupIsTheTail)
{
	EXPECT_EQ(STANDALONE_NET_FIELD_COUNT - 1, STANDALONE_NET_FIELD_COLLISIONGROUP)
		<< "a row inserted before the tail renumbers every later row on both "
		   "sides at once.";
}

} // namespace
