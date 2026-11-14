//========= SourceSharp — the clip needs a box to stop on =====================//
//
// The target ID's centre ray (TFHudTargetId's UpdateID arm) ends in the
// client's ClientClipRayToEntities, which clips the ray against each
// candidate's collideable. For SOLID_BBOX that clip stops on the head's
// Mins/Maxs (shell_standalone_client.h's OBBMins/OBBMaxs). The server wrote
// the player's standing hull into those words (SsPlayer's EntitySetBounds,
// the head box of the clip) — but the wire list stopped at m_nBody, so the
// client-side block answered an empty box and the ray passed through every
// teammate: the name plate never appeared at bottom centre on ctf_2fort.
//
// WHAT THIS FILE HOLDS. The pair is on the wire list, in the shape the clip
// reads it as. Stock sends the same pair for its collision property
// (collisionproperty.cpp: SendPropVector(SENDINFO(m_vecMins), ..., SPROP_NOSCALE)
// and the matching RECVINFO(m_vecMaxs)), so the rows are named for that.
// The compiled checks are over the real shared list both sides' builders
// subscript; the pair must be the tail, because the list's order is the
// wire format (see standalone_nettable.h), and a kind other than VECTOR
// makes the builders write four bytes where the head keeps twelve — mins
// would arrive as its x only, and the rest of the corners belong to the
// next fields.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string>

// What standalone_nettable.h wants from the engine, and no more — the same
// copies nodraw_visibility_test.cpp spells: MAX_EDICT_BITS from
// public/const.h (11) and EF_MAX_BITS from the entity-effects enum in the
// same header (10), plus ARRAYSIZE, which the shared list uses to report its
// own length. The header checks the first two against its own numbers.
#define MAX_EDICT_BITS 11
#define EF_MAX_BITS 10
#define ARRAYSIZE(p) ((int)(sizeof(p) / sizeof((p)[0])))

#include "../game/shared/standalone_nettable.h"

namespace
{

TEST(StandaloneBoundsWire, TheWireCarriesTheCollideableBox)
{
	int nCount = 0;
	const StandaloneNetField_t* pFields = StandaloneNetFields(&nCount);

	ASSERT_EQ(STANDALONE_NET_FIELD_COUNT, nCount)
		<< "the field ARRAY and the field ENUM disagree about how many props "
		   "there are. Both sides subscript the array by the enum, so the two "
		   "shells would build props for different fields.";

	// The pair the clip reads: OBBMins/OBBMaxs answer the head's Mins and
	// Maxs, so the wire has to write exactly those words. The names are the
	// handshake's, so a renamed row pairs with nothing on the other side.
	EXPECT_STREQ("m_vecMins", pFields[STANDALONE_NET_FIELD_MINS].pName)
		<< "the enum's MINS index does not name the mins vector. An offset "
		   "written at this index would be applied to whatever prop is here, "
		   "and the block word the wire wrote into would be somebody else's.";
	EXPECT_STREQ("m_vecMaxs", pFields[STANDALONE_NET_FIELD_MAXS].pName);

	// VECTOR, not INT: the vector arm copies the whole corner unscaled
	// (twelve bytes), where an int prop writes four and leaves the other
	// eight as stale zeros — an empty box again, which is the defect.
	EXPECT_EQ(STANDALONE_NET_VECTOR, pFields[STANDALONE_NET_FIELD_MINS].kind);
	EXPECT_EQ(STANDALONE_NET_VECTOR, pFields[STANDALONE_NET_FIELD_MAXS].kind);
}

TEST(StandaloneBoundsWire, ThePairFollowsTheAppendedRowsAtTheTail)
{
	// The list's order IS the wire format, so an insertion in the middle
	// renumbers every later row on both sides at once — the failure the tail
	// rule in standalone_nettable.h exists to avoid, and the one this order
	// check reads back.
	int nCount = 0;
	const StandaloneNetField_t* pFields = StandaloneNetFields(&nCount);
	ASSERT_EQ(STANDALONE_NET_FIELD_COUNT, nCount);

	EXPECT_STREQ("m_nSolidType", pFields[STANDALONE_NET_FIELD_SOLIDTYPE].pName);
	EXPECT_STREQ("m_nSolidFlags", pFields[STANDALONE_NET_FIELD_SOLIDFLAGS].pName);
	EXPECT_STREQ("m_nSkin", pFields[STANDALONE_NET_FIELD_SKIN].pName);
	EXPECT_STREQ("m_nBody", pFields[STANDALONE_NET_FIELD_BODY].pName);
	EXPECT_STREQ("m_vecMins", pFields[STANDALONE_NET_FIELD_MINS].pName);
	EXPECT_STREQ("m_vecMaxs", pFields[STANDALONE_NET_FIELD_MAXS].pName);

	// The pair really is the tail: nothing after it.
	EXPECT_EQ(STANDALONE_NET_FIELD_COUNT - 2, STANDALONE_NET_FIELD_MINS);
	EXPECT_EQ(STANDALONE_NET_FIELD_COUNT - 1, STANDALONE_NET_FIELD_MAXS);
}

} // namespace
