// The player's and worldspawn's field sets, compiled in ONE translation unit as a unity
// build compiles them. Each file once defined its own static ResolveOffset and
// ResolvePacked, so the second did not compile beside the first; both now resolve over
// SourceSharpNetClassResolve_t, each under its own class name.

#include "gtest/gtest.h"

#include "standalone_player_sync.cpp"
#include "world_net_sync.cpp"

#include <set>
#include <string>
#include <vector>

namespace
{
// What the fake net-state area was asked, and the one field it answers as unpublished.
std::vector<std::string> g_AskedClasses;
std::vector<std::string> g_AskedFields;
std::string g_Unpublished;

int32_t AnswerOffset(IManagedNetState*, const char* pClassName, const char* pFieldName)
{
	g_AskedClasses.push_back(pClassName);
	g_AskedFields.push_back(pFieldName);
	return g_Unpublished == pFieldName ? -1 : 16;
}

// One bit at shift 0: the placement word is shift | width << 8.
int32_t AnswerBits(IManagedNetState*, const char*, const char* pFieldName)
{
	return g_Unpublished == pFieldName ? -1 : 0x100;
}

class NetClassResolve : public ::testing::Test
{
protected:
	NetClassResolve()
	{
		g_AskedClasses.clear();
		g_AskedFields.clear();
		g_Unpublished.clear();
		m_Table.NetStateFieldOffset = AnswerOffset;
		m_Table.NetStateFieldBits = AnswerBits;
	}

	std::set<std::string> AskedClasses() const { return {g_AskedClasses.begin(), g_AskedClasses.end()}; }

	SSManagedNetStateTable m_Table{};
	IManagedNetState m_Area{&m_Table};
	SourceSharpPlayerFields_t m_Player;
	SourceSharpWorldFields_t m_World;
};
} // namespace

TEST_F(NetClassResolve, PlayerFieldsAreAskedUnderThePlayerClass)
{
	ASSERT_TRUE(SourceSharpPlayerFields_Resolve(&m_Player, 3, &m_Area));

	EXPECT_EQ(30u, g_AskedClasses.size());
	EXPECT_EQ((std::set<std::string>{"player"}), AskedClasses());
	EXPECT_EQ(16, m_Player.nHealth);
	EXPECT_TRUE(m_Player.Alive.IsResolved());
	EXPECT_TRUE(m_Player.IsCurrent(3));
}

TEST_F(NetClassResolve, WorldFieldsAreAskedUnderWorldspawn)
{
	ASSERT_TRUE(SourceSharpWorldFields_Resolve(&m_World, 3, &m_Area));

	EXPECT_EQ(10u, g_AskedClasses.size());
	EXPECT_EQ((std::set<std::string>{SOURCESHARP_WORLD_NETCLASS}), AskedClasses());
	EXPECT_EQ(16, m_World.nWaveHeight);
	EXPECT_TRUE(m_World.ColdWorld.IsResolved());
	EXPECT_TRUE(m_World.IsCurrent(3));
}

TEST_F(NetClassResolve, AnUnpublishedOffsetFailsThePlayerSet)
{
	g_Unpublished = "Velocity";

	EXPECT_FALSE(SourceSharpPlayerFields_Resolve(&m_Player, 3, &m_Area));
	EXPECT_EQ(-1, m_Player.nVelocity);
	EXPECT_FALSE(m_Player.IsCurrent(3));
}

TEST_F(NetClassResolve, AnUnpublishedPackedFieldFailsTheWorldSet)
{
	g_Unpublished = "StartDark";

	EXPECT_FALSE(SourceSharpWorldFields_Resolve(&m_World, 3, &m_Area));
	EXPECT_FALSE(m_World.StartDark.IsResolved());
	EXPECT_FALSE(m_World.IsCurrent(3));
}

// A failed field does not stop the set: every field is still asked, so the one warning
// covers them all.
TEST_F(NetClassResolve, EveryFieldIsAskedAfterOneFails)
{
	SourceSharpNetStateAsk_t ask = {m_Area};
	SourceSharpNetClassResolve_t resolve = {ask, "player", true};
	g_Unpublished = "Health";

	EXPECT_EQ(-1, resolve.Offset("Health"));
	EXPECT_FALSE(resolve.m_bAll);
	EXPECT_EQ(16, resolve.Offset("MaxSpeed"));
	EXPECT_FALSE(resolve.m_bAll);
	EXPECT_EQ((std::vector<std::string>{"Health", "MaxSpeed"}), g_AskedFields);
}
