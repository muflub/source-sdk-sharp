//========= SourceSharp — client.so's own tier0 profile and clock ============//
//
// tier0_vprof_client.cpp: client.so's g_VProfCurrentProfile is its own and
// never enabled (user ruling 2026-10-18), so the VPROF macros in the fork TUs
// still on its link do nothing, and the CVProfNode members they name are never
// reached. tier0_thread_client.cpp's Plat_FloatTime: seconds on a steady clock,
// for particlemgr's one difference of two readings.
//
// The binary links no tier0: a VPROF macro that reached a node would call
// tier0_vprof_client.cpp's members, which stop the process.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier0/platform.h"
#include "tier0/vprof.h"

#include "death_test_no_core.h"

#include <chrono>
#include <thread>

namespace
{
int ScopedWork( int n )
{
	VPROF_BUDGET( "tier0_client_test", VPROF_BUDGETGROUP_OTHER_UNACCOUNTED );
	return n + 1;
}

// While a fact makes this thread the profile's target, and back after.
struct TargetThisThread
{
	TargetThisThread() { g_VProfCurrentProfile.SetTargetThreadId( ThreadGetCurrentId() ); }

	~TargetThisThread() { g_VProfCurrentProfile.SetTargetThreadId( 0 ); }
};
} // namespace

TEST( Tier0VProfClient, TheProfileIsNotEnabled )
{
	EXPECT_FALSE( g_VProfCurrentProfile.IsEnabled() );
}

TEST( Tier0VProfClient, ABudgetScopeDoesNothing )
{
	// A scope that entered would reach CVProfNode, which stops the process.
	EXPECT_EQ( 2, ScopedWork( 1 ) );
	EXPECT_FALSE( g_VProfCurrentProfile.IsEnabled() );
}

TEST( Tier0VProfClient, DirectEnterAndExitDoNothingEvenOnTheTargetThread )
{
	// With this thread the target, only m_enabled and m_fAtRoot stand between
	// these calls and a null current node.
	TargetThisThread target;
	ASSERT_TRUE( g_VProfCurrentProfile.InTargetThread() );
	g_VProfCurrentProfile.EnterScope( "tier0_client_test", 0, VPROF_BUDGETGROUP_OTHER_UNACCOUNTED, false, 0 );
	g_VProfCurrentProfile.ExitScope();
	SUCCEED();
}

TEST( Tier0VProfClientDeathTest, ANodeMemberStopsTheProcessNamingTheRuling )
{
	SS_DEATH_NO_CORE();
	alignas( CVProfNode ) unsigned char storage[sizeof( CVProfNode )] = {};
	CVProfNode *pNode = reinterpret_cast<CVProfNode *>( storage );
	EXPECT_DEATH( pNode->EnterScope(),
				  "CVProfNode::EnterScope called in the client, whose vprof profile is never enabled" );
	EXPECT_DEATH( (void)pNode->ExitScope(), "CVProfNode::ExitScope called in the client" );
	EXPECT_DEATH( (void)pNode->GetSubNode( "a", 0, "b", 0 ), "CVProfNode::GetSubNode called in the client" );
}

TEST( Tier0ClockClient, PlatFloatTimeCountsSeconds )
{
	const double flStart = Plat_FloatTime();
	std::this_thread::sleep_for( std::chrono::milliseconds( 60 ) );
	const double flElapsed = Plat_FloatTime() - flStart;
	EXPECT_GE( flElapsed, 0.05 );
	EXPECT_LT( flElapsed, 2.0 ) << "not seconds";
}

TEST( Tier0ClockClient, PlatFloatTimeNeverGoesBack )
{
	double flLast = Plat_FloatTime();
	for ( int i = 0; i < 1000; ++i )
	{
		const double flNow = Plat_FloatTime();
		ASSERT_GE( flNow, flLast );
		flLast = flNow;
	}
}
