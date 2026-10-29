// plan_server_native_v2 T4a: the IRecipientFilter libss_fwd_server builds for
// the managed SoundEmitter (fwd_recipient_filter.cpp), in a block laid out as
// a managed block is, driven through IRecipientFilter's virtuals as
// IEngineSound::EmitSound drives it.

#include "gtest/gtest.h"

#include "ss_fwd.h"

#include "irecipientfilter.h"
#include "imanaged_physics_callbacks.h"

#include <stdint.h>

namespace
{

enum
{
	kSizeOf,
	kConstruct,
	kRecipientFilterSlots
};

const SSForwarderTable &Table()
{
	const SSForwarderTable *pTable = SSFwdServer_Table( "SSFwd_RecipientFilter" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable kNone = { nullptr, 0 };
	return pTable ? *pTable : kNone;
}

using ConstructFn = void *(*)( void *, IManagedRecipientFilter * );

void Released( IManagedObject * ) {}

int32_t Validated( IManagedObject *, const uint32_t *, int32_t )
{
	return -1;
}

// The recipients the managed side chose, as its table answers them.
const int32_t kSlots[] = { 3, 1, 7 };
void *g_pSelfAsked = nullptr;
int32_t g_nIndexAsked = -99;

const SSManagedRecipientFilterTable kTable = {
	&Released,
	&Validated,
	[]( IManagedRecipientFilter *self ) -> int32_t
	{
		g_pSelfAsked = self;
		return 1;
	},
	[]( IManagedRecipientFilter * ) -> int32_t { return 0; },
	[]( IManagedRecipientFilter * ) -> int32_t { return 3; },
	[]( IManagedRecipientFilter *, int32_t nSlot ) -> int32_t
	{
		g_nIndexAsked = nSlot;
		return ( nSlot >= 0 && nSlot < 3 ) ? kSlots[nSlot] : -1;
	},
};

struct Block
{
	const SSManagedRecipientFilterTable *pTable;
	alignas( void * ) unsigned char reserved[kManagedPhysicsCallbackWords * sizeof( void * )];
};

IRecipientFilter *Build( Block &block )
{
	auto construct = reinterpret_cast<ConstructFn>( Table().ppSlots[kConstruct] );
	return static_cast<IRecipientFilter *>(
		construct( block.reserved, reinterpret_cast<IManagedRecipientFilter *>( &block ) ) );
}

} // namespace

TEST( FwdRecipientFilter, TheTableServesSizeOfAndConstruct )
{
	const SSForwarderTable &table = Table();
	ASSERT_NE( nullptr, table.ppSlots );
	EXPECT_EQ( (int32_t)kRecipientFilterSlots, table.nSlots );

	const int nBytes = reinterpret_cast<int ( * )()>( table.ppSlots[kSizeOf] )();
	EXPECT_EQ( int( kManagedPhysicsCallbackWords * sizeof( void * ) ), nBytes )
		<< "the object is a vptr and one pointer, in exactly the words a block reserves";
}

TEST( FwdRecipientFilter, AConstructGivenNoMemoryOrNoTargetBuildsNothing )
{
	Block block = { &kTable, {} };
	auto construct = reinterpret_cast<ConstructFn>( Table().ppSlots[kConstruct] );

	EXPECT_EQ( nullptr, construct( nullptr, reinterpret_cast<IManagedRecipientFilter *>( &block ) ) );
	EXPECT_EQ( nullptr, construct( block.reserved, nullptr ) );
}

TEST( FwdRecipientFilter, TheFilterIsBuiltInTheBlocksReservedWords )
{
	Block block = { &kTable, {} };
	EXPECT_EQ( static_cast<void *>( block.reserved ), static_cast<void *>( Build( block ) ) );
}

TEST( FwdRecipientFilter, TheEngineReadsTheRecipientsTheManagedSideChose )
{
	Block block = { &kTable, {} };
	const IRecipientFilter *pFilter = Build( block );

	g_pSelfAsked = nullptr;
	EXPECT_TRUE( pFilter->IsReliable() );
	EXPECT_EQ( static_cast<void *>( &block ), g_pSelfAsked ) << "the managed side is the block the filter was built in";
	EXPECT_FALSE( pFilter->IsInitMessage() );

	ASSERT_EQ( 3, pFilter->GetRecipientCount() );
	EXPECT_EQ( 3, pFilter->GetRecipientIndex( 0 ) );
	EXPECT_EQ( 1, pFilter->GetRecipientIndex( 1 ) );
	EXPECT_EQ( 7, pFilter->GetRecipientIndex( 2 ) );
	EXPECT_EQ( 2, g_nIndexAsked ) << "the slot asked reaches the managed side unchanged";
	EXPECT_EQ( -1, pFilter->GetRecipientIndex( 3 ) );
}
