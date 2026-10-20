//========= SourceSharp — the shadow manager's pending queue ==================//
//
// C7 cluster 12 S2 (docs/plans/c7_shadow_manager.md). shell_shadows_client.cpp
// queues what an entity's shell tells the managed shadow manager
// (shadow_pending_queue.h) and hands it over with the manager's next call. Its
// three rules, each a fact: a second dirty mark collapses into the first; a
// destroy drops the entity's earlier entries; a full queue is handed over
// before the next entry goes in, and a hand-over empties it.
//=============================================================================//

#include "gtest/gtest.h"

#include "shadow_pending_queue.h"

#include <vector>

namespace
{

struct Sink
{
	std::vector< std::vector< int32_t > > batches;

	static void Take( const int32_t *pEntries, int32_t nCount, void *pContext )
	{
		static_cast< Sink * >( pContext )->batches.emplace_back( pEntries, pEntries + 3 * nCount );
	}
};

std::vector< int32_t > Entries( const CShadowPendingQueue &q )
{
	return std::vector< int32_t >( q.Entries(), q.Entries() + 3 * q.Count() );
}

} // namespace

TEST( ShadowPendingQueue, ADirtyMarkIsOneEntry )
{
	CShadowPendingQueue q;
	q.Dirty( 0x10007u, 0 );
	EXPECT_EQ( ( std::vector< int32_t >{ CShadowPendingQueue::DIRTY, 0x10007, 0 } ), Entries( q ) );
}

TEST( ShadowPendingQueue, ASecondDirtyMarkCollapsesIntoTheFirstAndKeepsTheTextureBit )
{
	CShadowPendingQueue q;
	q.Dirty( 7u, 0 );
	q.Dirty( 9u, 0 );
	q.Dirty( 7u, 1 );
	q.Dirty( 7u, 0 );
	// One entry per entity, the texture bit ORed in and not cleared by a later
	// mark without it.
	EXPECT_EQ( ( std::vector< int32_t >{ CShadowPendingQueue::DIRTY, 7, 1, CShadowPendingQueue::DIRTY, 9, 0 } ), Entries( q ) );
}

TEST( ShadowPendingQueue, RefreshesAreKeptInOrder )
{
	CShadowPendingQueue q;
	q.Refresh( 7u, 1 | ( 2 << 8 ) );
	q.Dirty( 7u, 0 );
	q.Refresh( 7u, 0 );
	EXPECT_EQ( ( std::vector< int32_t >{ CShadowPendingQueue::REFRESH, 7, 1 | ( 2 << 8 ), CShadowPendingQueue::DIRTY, 7, 0,
	                                     CShadowPendingQueue::REFRESH, 7, 0 } ),
	           Entries( q ) );
}

TEST( ShadowPendingQueue, ADestroyDropsTheEntitysEarlierEntriesAndNoOthers )
{
	CShadowPendingQueue q;
	q.Refresh( 7u, 1 );
	q.Dirty( 9u, 1 );
	q.Dirty( 7u, 1 );
	q.Destroy( 7u );
	EXPECT_EQ( ( std::vector< int32_t >{ CShadowPendingQueue::DIRTY, 9, 1, CShadowPendingQueue::DESTROY, 7, 0 } ), Entries( q ) );

	// And an entry after the destroy (a new entity on the same handle) stays.
	q.Refresh( 7u, 2 );
	EXPECT_EQ( 3, q.Count() );
}

TEST( ShadowPendingQueue, TakeHandsTheEntriesToTheCallAndEmptiesTheQueue )
{
	CShadowPendingQueue q;
	q.Dirty( 3u, 0 );
	q.Dirty( 4u, 1 );
	int32_t p[ CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX ];
	const int32_t n = q.Take( p );
	ASSERT_EQ( 2, n );
	EXPECT_EQ( 3, p[ 1 ] );
	EXPECT_EQ( 4, p[ 4 ] );
	EXPECT_EQ( 0, q.Count() );
}

// The call that carries the entries makes the manager work, and that work may
// move an entity, whose shell queues again before the call has read them all
// (the live A/B at c7c7a17c8 lost entity 264's dirty mark after a reload: its
// shadow was never projected, and went at the next RTT switch).
TEST( ShadowPendingQueue, WhatIsQueuedWhileTheCallReadsTheTakenEntriesLeavesThemIntact )
{
	CShadowPendingQueue q;
	q.Refresh( 7u, 2 );
	q.Dirty( 7u, 1 );
	int32_t p[ CShadowPendingQueue::WORDS * CShadowPendingQueue::MAX ];
	const int32_t n = q.Take( p );
	q.Dirty( 9u, 0 );
	q.Destroy( 8u );
	ASSERT_EQ( 2, n );
	EXPECT_EQ( CShadowPendingQueue::REFRESH, p[ 0 ] );
	EXPECT_EQ( 7, p[ 1 ] );
	EXPECT_EQ( 2, p[ 2 ] );
	EXPECT_EQ( CShadowPendingQueue::DIRTY, p[ 3 ] );
	EXPECT_EQ( 7, p[ 4 ] );
	EXPECT_EQ( 1, p[ 5 ] );
	EXPECT_EQ( 2, q.Count() );
}

TEST( ShadowPendingQueue, AHandOverReachesTheSinkOnceAndEmptiesTheQueue )
{
	Sink sink;
	CShadowPendingQueue q;
	q.SetSink( &Sink::Take, &sink );
	q.HandOver();
	EXPECT_EQ( 0u, sink.batches.size() ) << "an empty queue is handed over";

	q.Dirty( 3u, 1 );
	q.Destroy( 5u );
	q.HandOver();
	ASSERT_EQ( 1u, sink.batches.size() );
	EXPECT_EQ( ( std::vector< int32_t >{ CShadowPendingQueue::DIRTY, 3, 1, CShadowPendingQueue::DESTROY, 5, 0 } ), sink.batches[ 0 ] );
	EXPECT_EQ( 0, q.Count() );
	EXPECT_EQ( 1, q.HandedOver() );
}

TEST( ShadowPendingQueue, AFullQueueIsHandedOverBeforeTheNextEntry )
{
	Sink sink;
	CShadowPendingQueue q;
	q.SetSink( &Sink::Take, &sink );
	for ( int i = 0; i < CShadowPendingQueue::MAX; ++i )
		q.Refresh( (uint32_t)i, 1 );
	EXPECT_EQ( 0u, sink.batches.size() ) << "a queue at its size is not yet full";
	EXPECT_EQ( CShadowPendingQueue::MAX, q.Count() );

	q.Refresh( 1000u, 1 );
	ASSERT_EQ( 1u, sink.batches.size() );
	EXPECT_EQ( (size_t)( 3 * CShadowPendingQueue::MAX ), sink.batches[ 0 ].size() );
	EXPECT_EQ( 0, sink.batches[ 0 ][ 1 ] ) << "the oldest entry first";
	EXPECT_EQ( 1, q.Count() );
	EXPECT_EQ( 1000, q.Entries()[ 1 ] ) << "the entry that did not fit is the queue's first now";
}

TEST( ShadowPendingQueue, WithNoSinkAFullQueueDropsWhatItHeld )
{
	CShadowPendingQueue q;
	for ( int i = 0; i < CShadowPendingQueue::MAX; ++i )
		q.Refresh( (uint32_t)i, 1 );
	q.Refresh( 1000u, 1 );
	EXPECT_EQ( 1, q.Count() );
	EXPECT_EQ( 1000, q.Entries()[ 1 ] );
}

TEST( ShadowPendingQueue, TheSinkReadsACopySoWhatItQueuesIsKept )
{
	struct Requeue
	{
		CShadowPendingQueue *q;
		std::vector< int32_t > seen;
		static void Take( const int32_t *pEntries, int32_t nCount, void *pContext )
		{
			Requeue *self = static_cast< Requeue * >( pContext );
			self->q->Dirty( 99u, 0 );
			self->seen.assign( pEntries, pEntries + 3 * nCount );
		}
	} requeue;
	CShadowPendingQueue q;
	requeue.q = &q;
	q.SetSink( &Requeue::Take, &requeue );
	q.Dirty( 3u, 0 );
	q.HandOver();
	EXPECT_EQ( ( std::vector< int32_t >{ CShadowPendingQueue::DIRTY, 3, 0 } ), requeue.seen );
	EXPECT_EQ( 1, q.Count() );
	EXPECT_EQ( 99, q.Entries()[ 1 ] );
}
