//========= SourceSharp ======================================================//
//
// The engine-boundary probe's own counters -- plan_entity.md Phase 0 step 0e.
//
// WHY THIS EXISTS. docs/engine-boundary.md's verdict column is read by Phase 1
// and Phase 5 to decide which native methods may answer NULL. A probe that
// silently recorded no callers, or collapsed two call sites into one, would
// produce a doc that says "one caller, and it ignores the result" about a
// method with six callers that dereference it. The counts are the evidence, so
// the counter is the thing that has to be checked.
//
//===========================================================================//

#include "gtest/gtest.h"

#include "shell_boundary_probe.h"

#include <set>
#include <string>

namespace
{
// Distinct fake return addresses. Their VALUES never matter -- the probe
// only ever compares them -- so any distinct pointers will do.
const void* FakeCaller(int n)
{
	static const char s_Bytes[64] = {0};
	return (const void*)(s_Bytes + n);
}
}

TEST(BoundaryProbe, FreshProbeCountsNothing)
{
	CBoundaryProbe probe;
	for (int id = 0; id < kBoundaryProbeCount; ++id)
	{
		EXPECT_EQ(0u, probe.Calls(id)) << BoundaryProbeName(id);
		EXPECT_EQ(0, probe.CallerCount(id)) << BoundaryProbeName(id);
	}
}

TEST(BoundaryProbe, HitCountsTheCall)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(0));
	EXPECT_EQ(1u, probe.Calls(kBoundaryProbe_Ents_CheckTransmit));
}

TEST(BoundaryProbe, HitCountsOnlyTheIdGiven)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(0));
	EXPECT_EQ(0u, probe.Calls(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, HitRecordsTheCaller)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(3));
	ASSERT_EQ(1, probe.CallerCount(kBoundaryProbe_Ents_CheckTransmit));
	EXPECT_EQ(FakeCaller(3), probe.Caller(kBoundaryProbe_Ents_CheckTransmit, 0));
}

TEST(BoundaryProbe, RepeatedCallerCollapsesToOneSlot)
{
	CBoundaryProbe probe;
	for (int i = 0; i < 5; ++i)
		probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(3));

	EXPECT_EQ(1, probe.CallerCount(kBoundaryProbe_Ents_CheckTransmit));
	EXPECT_EQ(5u, probe.CallerHits(kBoundaryProbe_Ents_CheckTransmit, 0));
}

TEST(BoundaryProbe, DistinctCallersTakeDistinctSlots)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(1));
	probe.Hit(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(2));

	ASSERT_EQ(2, probe.CallerCount(kBoundaryProbe_Ents_EdictToBaseEntity));
	EXPECT_EQ(FakeCaller(1), probe.Caller(kBoundaryProbe_Ents_EdictToBaseEntity, 0));
	EXPECT_EQ(FakeCaller(2), probe.Caller(kBoundaryProbe_Ents_EdictToBaseEntity, 1));
}

TEST(BoundaryProbe, CallerTableStopsAtItsCapacity)
{
	CBoundaryProbe probe;
	for (int i = 0; i < CBoundaryProbe::kMaxCallers + 4; ++i)
		probe.Hit(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(i));

	EXPECT_EQ((int)CBoundaryProbe::kMaxCallers, probe.CallerCount(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, CallersPastCapacityAreCountedAsOverflow)
{
	CBoundaryProbe probe;
	for (int i = 0; i < CBoundaryProbe::kMaxCallers + 4; ++i)
		probe.Hit(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(i));

	EXPECT_EQ(4u, probe.CallerOverflow(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, OverflowStillCountsTheCall)
{
	CBoundaryProbe probe;
	for (int i = 0; i < CBoundaryProbe::kMaxCallers + 4; ++i)
		probe.Hit(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(i));

	EXPECT_EQ((uint64_t)(CBoundaryProbe::kMaxCallers + 4), probe.Calls(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, NullResultCountsAsNull)
{
	CBoundaryProbe probe;
	probe.HitPointerResult(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(0), true);
	EXPECT_EQ(1u, probe.NullResults(kBoundaryProbe_Ents_EdictToBaseEntity));
	EXPECT_EQ(0u, probe.NonNullResults(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, NonNullResultCountsAsNonNull)
{
	CBoundaryProbe probe;
	probe.HitPointerResult(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(0), false);
	EXPECT_EQ(0u, probe.NullResults(kBoundaryProbe_Ents_EdictToBaseEntity));
	EXPECT_EQ(1u, probe.NonNullResults(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, VoidHitCountsNeitherNullNorNonNull)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(0));
	EXPECT_EQ(0u, probe.NullResults(kBoundaryProbe_Ents_CheckTransmit));
	EXPECT_EQ(0u, probe.NonNullResults(kBoundaryProbe_Ents_CheckTransmit));
}

TEST(BoundaryProbe, ResetClearsCounts)
{
	CBoundaryProbe probe;
	probe.HitPointerResult(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(0), true);
	probe.Reset();
	EXPECT_EQ(0u, probe.Calls(kBoundaryProbe_Ents_EdictToBaseEntity));
	EXPECT_EQ(0u, probe.NullResults(kBoundaryProbe_Ents_EdictToBaseEntity));
}

TEST(BoundaryProbe, ResetClearsTheCallerTable)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(0));
	probe.Reset();
	EXPECT_EQ(0, probe.CallerCount(kBoundaryProbe_Ents_EdictToBaseEntity));
	EXPECT_EQ((const void*)NULL, probe.Caller(kBoundaryProbe_Ents_EdictToBaseEntity, 0));
}

TEST(BoundaryProbe, AnIdOutsideTheEnumIsIgnored)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbeCount, FakeCaller(0));
	probe.Hit(-1, FakeCaller(0));
	EXPECT_EQ(0u, probe.Calls(kBoundaryProbeCount));
	EXPECT_EQ(0u, probe.Calls(-1));
}

TEST(BoundaryProbe, EveryIdHasItsOwnName)
{
	std::set<std::string> seen;
	for (int id = 0; id < kBoundaryProbeCount; ++id)
	{
		const std::string name = BoundaryProbeName(id);
		EXPECT_STRNE("<unknown probe id>", name.c_str()) << "id " << id;
		EXPECT_TRUE(seen.insert(name).second) << "duplicate name: " << name;
	}
}

TEST(BoundaryProbe, AnIdPastTheEnumHasNoName)
{
	EXPECT_STREQ("<unknown probe id>", BoundaryProbeName(kBoundaryProbeCount));
}

TEST(BoundaryProbe, FormatLineNamesTheMethod)
{
	CBoundaryProbe probe;
	char buf[512];
	probe.FormatLine(kBoundaryProbe_Clients_GetPlayerState, buf, sizeof(buf));
	EXPECT_NE((const char*)NULL, strstr(buf, "IServerGameClients::GetPlayerState")) << buf;
}

TEST(BoundaryProbe, FormatLineCarriesTheCounts)
{
	CBoundaryProbe probe;
	probe.HitPointerResult(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(0), true);
	probe.HitPointerResult(kBoundaryProbe_Ents_EdictToBaseEntity, FakeCaller(0), false);

	char buf[512];
	probe.FormatLine(kBoundaryProbe_Ents_EdictToBaseEntity, buf, sizeof(buf));
	EXPECT_NE((const char*)NULL, strstr(buf, "calls=2")) << buf;
	EXPECT_NE((const char*)NULL, strstr(buf, "null=1")) << buf;
	EXPECT_NE((const char*)NULL, strstr(buf, "nonnull=1")) << buf;
}

TEST(BoundaryProbe, FormatLineSaysNoneWhenNothingCalled)
{
	CBoundaryProbe probe;
	char buf[512];
	probe.FormatLine(kBoundaryProbe_Ents_EdictToBaseEntity, buf, sizeof(buf));
	EXPECT_NE((const char*)NULL, strstr(buf, "callers=none")) << buf;
}

TEST(BoundaryProbe, FormatLineCountsHitsPerCaller)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(0));
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(0));
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(1));

	char buf[512];
	probe.FormatLine(kBoundaryProbe_Ents_CheckTransmit, buf, sizeof(buf));
	EXPECT_NE((const char*)NULL, strstr(buf, "x2")) << buf;
	EXPECT_NE((const char*)NULL, strstr(buf, "x1")) << buf;
}

TEST(BoundaryProbe, FormatLineReportsCallerOverflow)
{
	CBoundaryProbe probe;
	for (int i = 0; i < CBoundaryProbe::kMaxCallers + 3; ++i)
		probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(i));

	char buf[1024];
	probe.FormatLine(kBoundaryProbe_Ents_CheckTransmit, buf, sizeof(buf));
	EXPECT_NE((const char*)NULL, strstr(buf, "+3 calls from further sites")) << buf;
}

TEST(BoundaryProbe, FormatLineTruncatesRatherThanOverruns)
{
	CBoundaryProbe probe;
	probe.Hit(kBoundaryProbe_Ents_CheckTransmit, FakeCaller(0));

	char buf[24];
	memset(buf, 0x7f, sizeof(buf));
	const int nWould = probe.FormatLine(kBoundaryProbe_Ents_CheckTransmit, buf, 16);

	EXPECT_GT(nWould, 16); // it wanted more room
	EXPECT_EQ('\0', buf[15]); // and stopped inside it
	EXPECT_EQ(0x7f, (unsigned char)buf[16]); // without touching the rest
}

TEST(BoundaryProbe, DescribeAddressNamesTheModuleOfARealFunction)
{
	// dladdr resolves a code address to the .so it lives in. This test's own
	// code is the only address it can be sure is loaded.
	char buf[256];
	CBoundaryProbe::DescribeAddress((const void*)&BoundaryProbeName, buf, sizeof(buf));
	EXPECT_NE((const char*)NULL, strchr(buf, '+')) << buf;
	EXPECT_EQ((const char*)NULL, strchr(buf, '/')) << buf; // basename only
}

TEST(BoundaryProbe, DescribeAddressFallsBackToThePointerForNull)
{
	char buf[256];
	CBoundaryProbe::DescribeAddress(NULL, buf, sizeof(buf));
	EXPECT_EQ((const char*)NULL, strchr(buf, '+')) << buf;
}
