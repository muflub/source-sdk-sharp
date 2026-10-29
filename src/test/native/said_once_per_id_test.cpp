//========= SourceSharp. ============================================//
//
// The once-per-id rule for keyed console messages (said_once_per_id.h).
//
// The defect this pins down was live: the unpublished-type warning kept a
// single "last id" latch, and a map with TWO unpublished entities in the PVS
// alternated their ids every frame — each arrival overwrote the latch with the
// other id, and both ids warned once per update, ~110 lines apiece in a short
// session. The alternation test below is that exact traffic pattern, and it
// fails against the latch (which answers true 2N times) and passes against the
// set (which answers true exactly twice).
//
//===========================================================================//
#include <gtest/gtest.h>

#include "../game/shared/said_once_per_id.h"

TEST(SaidOncePerId, FirstArrivalOfAnIdSays)
{
	CSaidOncePerId tracker;

	EXPECT_TRUE(tracker.FirstTime(0x98ab094eu));
}

TEST(SaidOncePerId, SecondArrivalOfTheSameIdIsSilent)
{
	CSaidOncePerId tracker;

	EXPECT_TRUE(tracker.FirstTime(0x98ab094eu));
	EXPECT_FALSE(tracker.FirstTime(0x98ab094eu));
}

// The live traffic pattern: two entities, one update apiece per frame, ids
// alternating. A last-id latch says both every frame; the set says each once.
TEST(SaidOncePerId, AlternatingIdsSayOnceApiece)
{
	CSaidOncePerId tracker;

	const unsigned int nPropDynamic = 0x98ab094eu;
	const unsigned int nSsGrenade = 0x1545d3a2u;

	int nSaid = 0;

	for (int frame = 0; frame < 110; frame++)
	{
		if (tracker.FirstTime(nPropDynamic))
			nSaid++;
		if (tracker.FirstTime(nSsGrenade))
			nSaid++;
	}

	EXPECT_EQ(2, nSaid);
	EXPECT_EQ(2, tracker.Count());
}

// A map's worth of distinct ids all get their one line — dropping a distinct
// id's message would hide a real half-ported class.
TEST(SaidOncePerId, EveryDistinctIdSaysOnce)
{
	CSaidOncePerId tracker;

	for (unsigned int id = 1; id <= 80; id++)
		EXPECT_TRUE(tracker.FirstTime(id));

	for (unsigned int id = 1; id <= 80; id++)
		EXPECT_FALSE(tracker.FirstTime(id));

	EXPECT_EQ(80, tracker.Count());
}

// Past capacity the tracker degrades to saying, not to swallowing: repeating a
// line is recoverable noise, losing a distinct id's only line is not.
TEST(SaidOncePerId, OverflowSaysRatherThanSwallows)
{
	CSaidOncePerId tracker;

	// Fill it (capacity is 1024; ids 1..1024).
	for (unsigned int id = 1; id <= 1024; id++)
		EXPECT_TRUE(tracker.FirstTime(id));

	// A recorded id is still deduplicated.
	EXPECT_FALSE(tracker.FirstTime(7u));

	// An unrecordable new id says every time rather than never.
	EXPECT_TRUE(tracker.FirstTime(0xffff0001u));
	EXPECT_TRUE(tracker.FirstTime(0xffff0001u));
}
