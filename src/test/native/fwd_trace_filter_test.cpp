// plan_server_native_v2 T4f: the ITraceFilter libss_fwd_server builds for the
// managed trace rule (fwd_trace_filter.cpp), in a block laid out as a managed
// block is, driven through ITraceFilter's virtuals as IEngineTrace::TraceRay
// drives it; HandleOf, which names the entity a trace hit; and the Ray_t and
// trace_t rows managed builds and reads (trace_layout.h).

#include "gtest/gtest.h"

#include "ss_fwd.h"

#include "engine/IEngineTrace.h"
#include "ihandleentity.h"
#include "basehandle.h"
#include "imanaged_physics_callbacks.h"
#include "sourcesharp/game/shared/trace_layout.h"

#include <stdint.h>

namespace
{

enum
{
	kSizeOf,
	kConstruct,
	kHandleOf,
	kTraceFilterSlots
};

const SSForwarderTable &Table()
{
	const SSForwarderTable *pTable = SSFwdServer_Table( "SSFwd_TraceFilter" );
	EXPECT_NE( nullptr, pTable );
	static const SSForwarderTable kNone = { nullptr, 0 };
	return pTable ? *pTable : kNone;
}

using ConstructFn = void *(*)( void *, IManagedTraceFilter *, int32_t );
using HandleOfFn = uint32_t ( * )( IHandleEntity * );

void Released( IManagedObject * ) {}

int32_t Validated( IManagedObject *, const uint32_t *, int32_t )
{
	return -1;
}

// What the managed side was asked, and what it answers.
void *g_pSelfAsked = nullptr;
uint32_t g_hAsked = 0;
int32_t g_nMaskAsked = 0;
int32_t g_nAnswer = 0;

const SSManagedTraceFilterTable kTable = {
	&Released,
	&Validated,
	[]( IManagedTraceFilter *self, uint32_t hEntity, int32_t nMask ) -> int32_t
	{
		g_pSelfAsked = self;
		g_hAsked = hEntity;
		g_nMaskAsked = nMask;
		return g_nAnswer;
	},
};

struct Block
{
	const SSManagedTraceFilterTable *pTable;
	alignas( void * ) unsigned char reserved[kManagedPhysicsCallbackWords * sizeof( void * )];
};

ITraceFilter *Build( Block &block, int32_t nTraceType = TRACE_EVERYTHING )
{
	auto construct = reinterpret_cast<ConstructFn>( Table().ppSlots[kConstruct] );
	return static_cast<ITraceFilter *>(
		construct( block.reserved, reinterpret_cast<IManagedTraceFilter *>( &block ), nTraceType ) );
}

// An entity as the partition hands one to a filter: an IHandleEntity that
// answers its own handle.
class FakeEntity : public IHandleEntity
{
public:
	explicit FakeEntity( int nEntry, int nSerial ) { m_Handle.Init( nEntry, nSerial ); }

	void SetRefEHandle( const CBaseHandle &handle ) override { m_Handle = handle; }

	const CBaseHandle &GetRefEHandle() const override { return m_Handle; }

private:
	CBaseHandle m_Handle;
};

uint32_t HandleOf( IHandleEntity *pEntity )
{
	return reinterpret_cast<HandleOfFn>( Table().ppSlots[kHandleOf] )( pEntity );
}

} // namespace

TEST( FwdTraceFilter, TheTableServesSizeOfConstructAndHandleOf )
{
	const SSForwarderTable &table = Table();
	ASSERT_NE( nullptr, table.ppSlots );
	EXPECT_EQ( (int32_t)kTraceFilterSlots, table.nSlots );

	const int nBytes = reinterpret_cast<int ( * )()>( table.ppSlots[kSizeOf] )();
	EXPECT_EQ( int( kManagedPhysicsCallbackWords * sizeof( void * ) ), nBytes )
		<< "the object is a vptr, one pointer and the trace type, in exactly the words a block reserves";
}

TEST( FwdTraceFilter, AConstructGivenNoMemoryOrNoTargetBuildsNothing )
{
	Block block = { &kTable, {} };
	auto construct = reinterpret_cast<ConstructFn>( Table().ppSlots[kConstruct] );

	EXPECT_EQ( nullptr, construct( nullptr, reinterpret_cast<IManagedTraceFilter *>( &block ), TRACE_EVERYTHING ) );
	EXPECT_EQ( nullptr, construct( block.reserved, nullptr, TRACE_EVERYTHING ) );
}

TEST( FwdTraceFilter, TheFilterIsBuiltInTheBlocksReservedWords )
{
	Block block = { &kTable, {} };
	EXPECT_EQ( static_cast<void *>( block.reserved ), static_cast<void *>( Build( block ) ) );
}

TEST( FwdTraceFilter, EachCandidateReachesTheManagedSideAsItsHandleAndTheMask )
{
	Block block = { &kTable, {} };
	ITraceFilter *pFilter = Build( block );
	FakeEntity crate( 37, 5 );

	g_pSelfAsked = nullptr;
	g_nAnswer = 1;
	EXPECT_TRUE( pFilter->ShouldHitEntity( &crate, 0x0200400B ) );
	EXPECT_EQ( static_cast<void *>( &block ), g_pSelfAsked ) << "the managed side is the block the filter was built in";
	EXPECT_EQ( (uint32_t)crate.GetRefEHandle().ToInt(), g_hAsked );
	EXPECT_EQ( 0x0200400B, g_nMaskAsked );
}

TEST( FwdTraceFilter, TheManagedAnswerIsTheEnginesAnswer )
{
	Block block = { &kTable, {} };
	ITraceFilter *pFilter = Build( block );
	FakeEntity crate( 12, 1 );

	g_nAnswer = 0;
	EXPECT_FALSE( pFilter->ShouldHitEntity( &crate, 1 ) ) << "zero is a miss";
	g_nAnswer = 7;
	EXPECT_TRUE( pFilter->ShouldHitEntity( &crate, 1 ) ) << "any non-zero is a hit";
}

TEST( FwdTraceFilter, ANullCandidateCrossesAsTheInvalidHandle )
{
	Block block = { &kTable, {} };
	ITraceFilter *pFilter = Build( block );

	g_hAsked = 0;
	g_nAnswer = 0;
	pFilter->ShouldHitEntity( nullptr, 1 );
	EXPECT_EQ( (uint32_t)INVALID_EHANDLE_INDEX, g_hAsked );
}

TEST( FwdTraceFilter, TheTraceTypeIsTheOneManagedBuiltItWith )
{
	Block entities = { &kTable, {} };
	Block everything = { &kTable, {} };

	EXPECT_EQ( TRACE_ENTITIES_ONLY, Build( entities, TRACE_ENTITIES_ONLY )->GetTraceType() );
	EXPECT_EQ( TRACE_EVERYTHING, Build( everything, TRACE_EVERYTHING )->GetTraceType() );
}

TEST( FwdTraceFilter, TheTraceTypeIsAnsweredWithoutAskingTheManagedSide )
{
	Block block = { &kTable, {} };
	ITraceFilter *pFilter = Build( block, TRACE_WORLD_ONLY );

	g_pSelfAsked = nullptr;
	EXPECT_EQ( TRACE_WORLD_ONLY, pFilter->GetTraceType() );
	EXPECT_EQ( nullptr, g_pSelfAsked ) << "the engine asks about four times a trace; none of them crosses";
}

TEST( FwdTraceFilter, HandleOfNamesTheEntityATraceHit )
{
	FakeEntity crate( 99, 3 );
	EXPECT_EQ( (uint32_t)crate.GetRefEHandle().ToInt(), HandleOf( &crate ) );
	EXPECT_NE( 0u, HandleOf( &crate ) );
	EXPECT_EQ( (uint32_t)INVALID_EHANDLE_INDEX, HandleOf( nullptr ) ) << "a trace that hit nothing names nothing";
}

namespace
{
struct LayoutRow
{
	const char *name;
	size_t offset;
	size_t size;
};

#define SS_TRACE_ROW( name, offset, size ) { #name, ( offset ), ( size ) },
const LayoutRow kRayRows[] = { SOURCESHARP_RAY_LAYOUT( SS_TRACE_ROW ) };
const LayoutRow kTraceRows[] = { SOURCESHARP_GAMETRACE_LAYOUT( SS_TRACE_ROW ) };
#undef SS_TRACE_ROW

template <size_t N>
void ExpectRowsAscendWithoutOverlapWithin( const LayoutRow ( &rows )[N], size_t nSizeOf, const char *pWhat )
{
	for ( size_t i = 1; i < N; ++i )
		EXPECT_LE( rows[i - 1].offset + rows[i - 1].size, rows[i].offset )
			<< pWhat << "::" << rows[i].name << " overlaps " << rows[i - 1].name;

	EXPECT_LE( rows[N - 1].offset + rows[N - 1].size, nSizeOf ) << pWhat << "'s last row runs past its size";
}
} // namespace

TEST( TraceLayout, TheRowsAscendWithoutOverlapInsideEachStruct )
{
	ExpectRowsAscendWithoutOverlapWithin( kRayRows, sizeof( Ray_t ), "Ray_t" );
	ExpectRowsAscendWithoutOverlapWithin( kTraceRows, sizeof( CGameTrace ), "CGameTrace" );
	EXPECT_EQ( 6u, sizeof( kRayRows ) / sizeof( kRayRows[0] ) );
	EXPECT_EQ( 14u, sizeof( kTraceRows ) / sizeof( kTraceRows[0] ) );
}
