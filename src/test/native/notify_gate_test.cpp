//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the notify list answers for itself =================//
//
// notify_gate.h replaced a cached bit on CBaseEntity (EFL_NOTIFY) with a
// question asked of the list of pairs. The change is only safe because of one
// property, and the property is invisible from either side on its own:
//
//   the cache was an OVER-approximation of list membership, never an under one
//
// AddEntity set the flag and added a pair, so anything in the list had the flag.
// RemoveEntity and ClearEntity dropped pairs and never cleared the flag, so the
// converse was false the moment anything was ever un-watched. A gate that lets
// through a superset can be replaced by the exact answer without changing what
// is delivered; a gate that let through a subset could not.
//
// So this file models both representations side by side, drives them through the
// operations CNotifyList performs, and pins:
//
//   1. the exact answer agrees with the scan the report loop is about to do,
//   2. the cached answer is a superset of it, at every point,
//   3. the cache is STRICTLY bigger after a removal — the divergence is real
//      rather than theoretical, so the exactness is a fix and not just tidying.
//
//=============================================================================//
#include <vector>
#include "gtest/gtest.h"

#include "../game/server/notify_gate.h"

namespace
{

// Stands in for the entities. Only its ADDRESS matters — the list stores
// pointers and compares them — so an empty struct is the honest shape.
struct FakeEntity
{
	int m_nUnused;
};

// Stands in for entitynotify_t. Same two members, same names.
struct FakeNotify
{
	FakeEntity *pNotify;
	FakeEntity *pWatched;
};

// Stands in for CUtlVector<entitynotify_t>: Count() and operator[], which is
// the whole of what NotifyList_Watches asks of a container.
//
// A std::vector would not do, because CUtlVector's size accessor is Count() and
// the template is written against that name. Wrapping one keeps the storage
// boring and the SHAPE the same as the game's.
class FakeList
{
public:
	int Count() const { return (int)m_entries.size(); }
	const FakeNotify &operator[]( int i ) const { return m_entries[i]; }

	void AddToTail( const FakeNotify &e ) { m_entries.push_back( e ); }
	void FastRemove( int i )
	{
		m_entries[i] = m_entries.back();
		m_entries.pop_back();
	}

private:
	std::vector<FakeNotify> m_entries;
};

//-----------------------------------------------------------------------------
// CNotifyList's three mutators, and the two answers, modelled exactly as
// entitylist.cpp has them.
//-----------------------------------------------------------------------------
class NotifyModel
{
public:
	void AddEntity( FakeEntity *pNotify, FakeEntity *pWatched )
	{
		// Stock did BOTH of these. The flag half is what left.
		m_flagged.push_back( pWatched );

		FakeNotify e;
		e.pNotify  = pNotify;
		e.pWatched = pWatched;
		m_list.AddToTail( e );
	}

	void RemoveEntity( FakeEntity *pNotify, FakeEntity *pWatched )
	{
		// Note what is NOT here, in stock either: nothing clears the flag.
		for ( int i = m_list.Count(); --i >= 0; )
		{
			if ( m_list[i].pNotify == pNotify && m_list[i].pWatched == pWatched )
				m_list.FastRemove( i );
		}
	}

	void ClearEntity( FakeEntity *pEntity )
	{
		for ( int i = m_list.Count(); --i >= 0; )
		{
			if ( m_list[i].pNotify == pEntity || m_list[i].pWatched == pEntity )
				m_list.FastRemove( i );
		}
	}

	// What the code does now.
	bool ExactGate( const FakeEntity *pWatched ) const
	{
		return NotifyList_Watches( m_list, pWatched );
	}

	// What EFL_NOTIFY answered.
	bool CachedGate( const FakeEntity *pWatched ) const
	{
		for ( size_t i = 0; i < m_flagged.size(); i++ )
		{
			if ( m_flagged[i] == pWatched )
				return true;
		}
		return false;
	}

	// What the report loop UNDER the gate would actually deliver to. The gate
	// is only allowed to skip when this is zero.
	int DeliveryCount( const FakeEntity *pWatched ) const
	{
		int n = 0;
		for ( int i = 0; i < m_list.Count(); i++ )
		{
			if ( m_list[i].pWatched == pWatched )
				n++;
		}
		return n;
	}

private:
	FakeList                  m_list;
	std::vector<FakeEntity *> m_flagged;
};

FakeEntity g_a, g_b, g_c, g_never;

} // namespace

//-----------------------------------------------------------------------------
// The empty list, which is every map this mod ships.
//-----------------------------------------------------------------------------
TEST( NotifyGate, NothingWatchedAnswersFalse )
{
	NotifyModel m;

	EXPECT_FALSE( m.ExactGate( &g_a ) );
	EXPECT_EQ( 0, m.DeliveryCount( &g_a ) );
}

//-----------------------------------------------------------------------------
// The gate must agree with the scan it guards — in both directions. An entity
// with deliveries must pass, an entity without must not.
//-----------------------------------------------------------------------------
TEST( NotifyGate, GateAgreesWithTheScanItGuards )
{
	NotifyModel m;
	m.AddEntity( &g_b, &g_a );          // b watches a
	m.AddEntity( &g_c, &g_a );          // c watches a too
	m.AddEntity( &g_a, &g_b );          // a watches b

	EXPECT_TRUE( m.ExactGate( &g_a ) );
	EXPECT_EQ( 2, m.DeliveryCount( &g_a ) );

	EXPECT_TRUE( m.ExactGate( &g_b ) );
	EXPECT_EQ( 1, m.DeliveryCount( &g_b ) );

	// c is a WATCHER, never watched. The gate is about the watched side.
	EXPECT_FALSE( m.ExactGate( &g_c ) );
	EXPECT_EQ( 0, m.DeliveryCount( &g_c ) );

	EXPECT_FALSE( m.ExactGate( &g_never ) );
}

//-----------------------------------------------------------------------------
// The property that makes the replacement safe: the cache never answered false
// where the exact answer is true. Checked over every operation, not just at the
// end, because the direction only breaks transiently if it breaks at all.
//-----------------------------------------------------------------------------
TEST( NotifyGate, CachedAnswerIsAlwaysASupersetOfTheExactOne )
{
	NotifyModel m;
	FakeEntity *all[] = { &g_a, &g_b, &g_c, &g_never };

	struct Step { FakeEntity *pNotify; FakeEntity *pWatched; int op; };
	const Step steps[] = {
		{ &g_b, &g_a, 0 },   // add
		{ &g_c, &g_a, 0 },   // add
		{ &g_a, &g_b, 0 },   // add
		{ &g_b, &g_a, 1 },   // remove one watcher of a
		{ &g_c, &g_a, 1 },   // remove the other — a is now unwatched
		{ &g_b, &g_c, 0 },   // add
		{ &g_b, NULL, 2 },   // clear b entirely
	};

	for ( size_t s = 0; s < sizeof( steps ) / sizeof( steps[0] ); s++ )
	{
		switch ( steps[s].op )
		{
		case 0: m.AddEntity( steps[s].pNotify, steps[s].pWatched ); break;
		case 1: m.RemoveEntity( steps[s].pNotify, steps[s].pWatched ); break;
		default: m.ClearEntity( steps[s].pNotify ); break;
		}

		for ( size_t e = 0; e < sizeof( all ) / sizeof( all[0] ); e++ )
		{
			// The exact answer is the scan.
			EXPECT_EQ( m.DeliveryCount( all[e] ) > 0, m.ExactGate( all[e] ) )
				<< "step " << s << " entity " << e;

			// And the cache never under-approximated it, which is the whole
			// argument that dropping the cache delivers no more and no less.
			if ( m.ExactGate( all[e] ) )
			{
				EXPECT_TRUE( m.CachedGate( all[e] ) )
					<< "step " << s << " entity " << e;
			}
		}
	}
}

//-----------------------------------------------------------------------------
// And the divergence is real: after the last watcher goes, EFL_NOTIFY still
// said yes. Stock cleared it nowhere. This is the case the exact answer fixes,
// and it is why the two are not interchangeable in the other direction.
//-----------------------------------------------------------------------------
TEST( NotifyGate, TheCacheStaysSetAfterTheLastWatcherLeaves )
{
	NotifyModel m;
	m.AddEntity( &g_b, &g_a );

	EXPECT_TRUE( m.ExactGate( &g_a ) );
	EXPECT_TRUE( m.CachedGate( &g_a ) );

	m.RemoveEntity( &g_b, &g_a );

	EXPECT_FALSE( m.ExactGate( &g_a ) );     // the list forgot
	EXPECT_TRUE( m.CachedGate( &g_a ) );     // the flag did not
	EXPECT_EQ( 0, m.DeliveryCount( &g_a ) ); // and there was nothing to deliver
}

//-----------------------------------------------------------------------------
// ClearEntity drops the pairs on BOTH sides — a destroyed entity is neither a
// watcher nor watched — so the gate must go false for it as watched, and its
// victims must lose a delivery.
//-----------------------------------------------------------------------------
TEST( NotifyGate, ClearEntityRemovesBothSides )
{
	NotifyModel m;
	m.AddEntity( &g_b, &g_a );   // b watches a
	m.AddEntity( &g_a, &g_b );   // a watches b

	m.ClearEntity( &g_a );

	EXPECT_FALSE( m.ExactGate( &g_a ) );
	EXPECT_FALSE( m.ExactGate( &g_b ) );
	EXPECT_EQ( 0, m.DeliveryCount( &g_a ) );
	EXPECT_EQ( 0, m.DeliveryCount( &g_b ) );
}
