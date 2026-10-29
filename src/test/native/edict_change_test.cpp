// SourceSharp_MarkEdictChanged over a real change list and accessor: what one networked
// write records for the engine, and when the whole edict is sent instead.

#include "gtest/gtest.h"

#include "edict_change.h"
#include "edict.h"

namespace
{
class EdictChange : public ::testing::Test
{
protected:
	EdictChange()
	{
		m_Edict.m_fStateFlags = 0;
		m_Shared.m_nChangeInfos = 0;
		m_Accessor.SetChangeInfo(0);
		m_Accessor.SetChangeInfoSerialNumber(0);
	}

	void Mark(int nOffset) { SourceSharp_MarkEdictChanged(&m_Accessor, m_Shared, &m_Edict, nOffset); }

	const CEdictChangeInfo& Info() const { return m_Shared.m_ChangeInfos[m_Accessor.GetChangeInfo()]; }

	edict_t m_Edict;
	CSharedEdictChangeInfo m_Shared;
	IChangeInfoAccessor m_Accessor;
};
} // namespace

TEST_F(EdictChange, AnOffsetTakesThisFramesChangeInfo)
{
	Mark(40);

	EXPECT_TRUE(m_Edict.m_fStateFlags & FL_EDICT_CHANGED);
	EXPECT_FALSE(m_Edict.m_fStateFlags & FL_FULL_EDICT_CHANGED);
	EXPECT_EQ(1, m_Shared.m_nChangeInfos);
	EXPECT_EQ(m_Shared.m_iSerialNumber, m_Accessor.GetChangeInfoSerialNumber());
	ASSERT_EQ(1, Info().m_nChangeOffsets);
	EXPECT_EQ(40, Info().m_ChangeOffsets[0]);
}

TEST_F(EdictChange, TheSameOffsetIsRecordedOnce)
{
	Mark(40);
	Mark(40);
	Mark(44);

	EXPECT_EQ(1, m_Shared.m_nChangeInfos);
	ASSERT_EQ(2, Info().m_nChangeOffsets);
	EXPECT_EQ(44, Info().m_ChangeOffsets[1]);
}

TEST_F(EdictChange, AFullOffsetListSendsTheWholeEdict)
{
	for (int i = 0; i < MAX_CHANGE_OFFSETS; i++)
		Mark(4 * i);

	EXPECT_FALSE(m_Edict.m_fStateFlags & FL_FULL_EDICT_CHANGED);

	Mark(4 * MAX_CHANGE_OFFSETS);

	EXPECT_TRUE(m_Edict.m_fStateFlags & FL_FULL_EDICT_CHANGED);
	EXPECT_EQ(0, m_Accessor.GetChangeInfoSerialNumber());
}

TEST_F(EdictChange, NoChangeInfoLeftSendsTheWholeEdict)
{
	m_Shared.m_nChangeInfos = MAX_EDICT_CHANGE_INFOS;

	Mark(40);

	EXPECT_TRUE(m_Edict.m_fStateFlags & FL_FULL_EDICT_CHANGED);
	EXPECT_EQ(0, m_Accessor.GetChangeInfoSerialNumber());
	EXPECT_EQ(MAX_EDICT_CHANGE_INFOS, m_Shared.m_nChangeInfos);
}

TEST_F(EdictChange, AWholeEdictTakesNoMoreOffsets)
{
	Mark(-1);
	Mark(40);

	EXPECT_TRUE(m_Edict.m_fStateFlags & (FL_EDICT_CHANGED | FL_FULL_EDICT_CHANGED));
	EXPECT_EQ(0, m_Shared.m_nChangeInfos);
	EXPECT_EQ(0, m_Accessor.GetChangeInfoSerialNumber());
}

TEST_F(EdictChange, ALastFramesChangeInfoIsNotReused)
{
	Mark(40);
	m_Shared.m_iSerialNumber++;
	m_Shared.m_nChangeInfos = 0;

	Mark(44);

	EXPECT_EQ(1, m_Shared.m_nChangeInfos);
	EXPECT_EQ(m_Shared.m_iSerialNumber, m_Accessor.GetChangeInfoSerialNumber());
	ASSERT_EQ(1, Info().m_nChangeOffsets);
	EXPECT_EQ(44, Info().m_ChangeOffsets[0]);
}
