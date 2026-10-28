// SourceSharp_BoneBatchEntry (standalone_bone_batch.h, plan_physics_bones_perf
// A2): the frame's posed skeleton answers an ask only when the ask is the one
// it was posed for. One fact per stamp, each shown to refuse on its own.
#include "gtest/gtest.h"
#include "standalone_bone_batch.h"

#include <vector>

namespace
{
struct Fixture
{
	std::vector<int16_t> slots = std::vector<int16_t>( SS_BONE_BATCH_SLOTS, -1 );
	SourceSharpBoneBatchEntry_t entry = {};
	float matrices[12] = {};
	SourceSharpBoneBatch_t batch = {};

	float origin[3] = { 1.0f, 2.0f, 3.0f };
	float angles[3] = { 0.0f, 90.0f, 0.0f };
	float renderOrigin[3] = { 1.5f, 2.0f, 3.0f };
	float renderAngles[3] = { 0.0f, 91.0f, 0.0f };

	Fixture()
	{
		entry.hEntity = 0x00010005u;
		entry.nBoneMask = 0x0007FF00;
		entry.nBones = 1;
		entry.nModelIndex = 7;
		entry.nViewModel = 0;
		for ( int i = 0; i < 3; i++ )
		{
			entry.flOrigin[i] = origin[i];
			entry.flAngles[i] = angles[i];
			entry.flRenderOrigin[i] = renderOrigin[i];
			entry.flRenderAngles[i] = renderAngles[i];
		}
		slots[5] = 0;
		batch.nFrameCount = 100;
		batch.flCurrentTime = 2.5f;
		batch.nEntries = 1;
		batch.pSlotToEntry = slots.data();
		batch.pEntries = &entry;
		batch.pMatrices = matrices;
	}

	const SourceSharpBoneBatchEntry_t *Ask( uint32_t h = 0x00010005u, int32_t mask = 0x400, float time = 2.5f,
											int32_t frame = 100, int32_t model = 7, int32_t view = 0 )
	{
		return SourceSharp_BoneBatchEntry( &batch, h, mask, time, frame, model, view, origin, angles, renderOrigin,
										   renderAngles );
	}
};
} // namespace

TEST( BoneBatchEntry, TheAskItWasPosedForIsAnswered )
{
	Fixture f;
	EXPECT_EQ( &f.entry, f.Ask() );
}

TEST( BoneBatchEntry, AnotherFrameOrClockIsNot )
{
	Fixture f;
	EXPECT_EQ( nullptr, f.Ask( 0x00010005u, 0x400, 2.5f, 101 ) );
	EXPECT_EQ( nullptr, f.Ask( 0x00010005u, 0x400, 2.6f ) );
}

TEST( BoneBatchEntry, AMaskWiderThanThePosedOneIsNot )
{
	Fixture f;
	EXPECT_EQ( nullptr, f.Ask( 0x00010005u, 0x00100000 ) );
}

TEST( BoneBatchEntry, AnotherEntityInTheSameSlotIsNot )
{
	Fixture f;
	EXPECT_EQ( nullptr, f.Ask( 0x00020005u ) );
}

TEST( BoneBatchEntry, AnEmptySlotOrNoTableIsNot )
{
	Fixture f;
	EXPECT_EQ( nullptr, f.Ask( 0x00010006u ) );
	EXPECT_EQ( nullptr,
			   SourceSharp_BoneBatchEntry( nullptr, 0x00010005u, 0x400, 2.5f, 100, 7, 0, f.origin, f.angles,
										   f.renderOrigin, f.renderAngles ) );
}

TEST( BoneBatchEntry, AnotherModelOrViewModelBitIsNot )
{
	Fixture f;
	EXPECT_EQ( nullptr, f.Ask( 0x00010005u, 0x400, 2.5f, 100, 8 ) );
	EXPECT_EQ( nullptr, f.Ask( 0x00010005u, 0x400, 2.5f, 100, 7, 1 ) );
}

TEST( BoneBatchEntry, AnEntityThatMovedSinceThePoseIsNot )
{
	for ( int which = 0; which < 4; which++ )
	{
		Fixture f;
		float *moved[4] = { f.origin, f.angles, f.renderOrigin, f.renderAngles };
		moved[which][2] += 0.5f;
		EXPECT_EQ( nullptr, f.Ask() ) << "input " << which;
	}
}

TEST( BoneBatchEntry, AnEntryIndexPastTheTableIsNot )
{
	Fixture f;
	f.slots[5] = 1;
	EXPECT_EQ( nullptr, f.Ask() );
}
