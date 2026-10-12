//========= SourceSharp — the area-portal list, driven for real ==============//
//
// plan_entity.md step 4g took `CSourceSharpAreaPortal : CFuncAreaPortalBase :
// CBaseEntity` off the SDK hierarchy and made it a record. The record's handle
// stopped being an entity handle at the same moment, and THAT is the part with
// a bug in it if anybody gets it wrong:
//
//   * an entity handle carries a serial the slot table checks (§3.3), so a
//     recycled slot cannot answer an old handle;
//   * a list position carries nothing.
//
// So the token is an id from a counter that is never reused, resolved through a
// map a record leaves when it is destroyed. The failure it prevents is a
// func_areaportal that was removed and a second one created in the same frame,
// after which the FIRST entity's stale token opens and closes the SECOND one's
// hole. That is invisible until a map
// cycles, which is exactly the class of bug a live gate does not reach: every
// live gate this project has loads ONE map.
//
// THE SUBJECT IS THE PRODUCTION CODE. Everything asserted below is defined in
// sourcesharp/game/server/areaportal_list.h and compiled from it — the same
// constructor, destructor, token and resolver the server links. What is NOT
// here is CAreaPortal::UpdateVisibility's backface arm, which asks the engine
// for the portal's plane and so stays in shell_areaportal.cpp; its two other
// arms are ClosedByManagedState and they ARE here.
//
// g_AreaPortals and g_AreaPortalsById are defined by this file. The list is one object with one
// definition (shell_areaportal.cpp's, in the game), and a test binary that does
// not link the game DLL has to supply its own — which is also what lets each
// test start from an empty list rather than from whatever the last one left.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "mathlib_test_env.h"

#include "../game/server/areaportal_list.h"

#include <vector>

// The one definition this binary needs. See the header note above.
std::list< CAreaPortal * > g_AreaPortals;
std::unordered_map< uint32_t, CAreaPortal * > g_AreaPortalsById;

namespace
{

// Every test starts from an empty list. Without this a test's answers depend on
// which tests ran before it, which is how two of this project's twenty-two
// checks-that-could-not-fail passed.
class AreaPortalList : public ::testing::Test
{
protected:
	void SetUp() override
	{
		while ( !g_AreaPortals.empty() )
			delete g_AreaPortals.front();
	}

	void TearDown() override
	{
		while ( !g_AreaPortals.empty() )
			delete g_AreaPortals.front();
	}
};

} // namespace

//=============================================================================//
// MEMBERSHIP — the whole reason any of this is native.
//=============================================================================//

TEST_F( AreaPortalList, ConstructingAPortalPutsItInTheList )
{
	EXPECT_EQ( g_AreaPortals.size(), 0u );

	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );

	EXPECT_EQ( g_AreaPortals.size(), 1u );
	EXPECT_EQ( g_AreaPortals.front(), pPortal );
}

TEST_F( AreaPortalList, DestroyingAPortalTakesItOutOfTheList )
{
	delete new CAreaPortal( 7, 1 );

	EXPECT_EQ( g_AreaPortals.size(), 0u )
		<< "a freed record left in the list is dereferenced once per client "
		   "per frame by the visibility walk.";
	EXPECT_EQ( g_AreaPortalsById.size(), 0u ) << "and its token must not outlive it";
}

TEST_F( AreaPortalList, TheListKeepsEveryPortalNotJustTheLast )
{
	new CAreaPortal( 1, 1 );
	new CAreaPortal( 2, 1 );
	new CAreaPortal( 3, 1 );

	EXPECT_EQ( g_AreaPortals.size(), 3u );
}

// The visibility walk sends the engine one state per portal in list order, so
// the order is creation order -- and stays so when one leaves from the middle.
TEST_F( AreaPortalList, TheWalkIsCreationOrderAfterARemovalFromTheMiddle )
{
	CAreaPortal *a = new CAreaPortal( 1, 1 );
	CAreaPortal *b = new CAreaPortal( 2, 1 );
	CAreaPortal *c = new CAreaPortal( 3, 1 );
	CAreaPortal *d = new CAreaPortal( 4, 1 );

	delete b;
	CAreaPortal *e = new CAreaPortal( 5, 1 );

	const std::vector< CAreaPortal * > walked( g_AreaPortals.begin(), g_AreaPortals.end() );
	const std::vector< CAreaPortal * > expected = { a, c, d, e };
	EXPECT_EQ( walked, expected );
}

TEST_F( AreaPortalList, DestructionRemovesExactlyThatPortal )
{
	CAreaPortal *a = new CAreaPortal( 1, 1 );
	CAreaPortal *b = new CAreaPortal( 2, 1 );
	CAreaPortal *c = new CAreaPortal( 3, 1 );
	const uint32_t ha = AreaPortalTokenOf( a ), hc = AreaPortalTokenOf( c );

	delete b;

	EXPECT_EQ( g_AreaPortals.size(), 2u );
	EXPECT_EQ( g_AreaPortals.front(), a );
	EXPECT_EQ( g_AreaPortals.back(), c );
	EXPECT_EQ( AreaPortalFromToken( ha ), a );
	EXPECT_EQ( AreaPortalFromToken( hc ), c );
}

//=============================================================================//
// THE TOKEN -- an id that is never reused.
//=============================================================================//

// FuncAreaPortal.cs tests `Portal == 0` for "this entity never got one" and
// calls AreaPortalDestroy(0) on an entity whose spawn refused.
TEST_F( AreaPortalList, ZeroIsNeverAValidToken )
{
	new CAreaPortal( 7, 1 );

	EXPECT_TRUE( AreaPortalFromToken( 0 ) == NULL );
}

TEST_F( AreaPortalList, ATokenResolvesToItsOwnPortal )
{
	CAreaPortal *pFirst  = new CAreaPortal( 11, 0 );
	CAreaPortal *pSecond = new CAreaPortal( 22, 1 );

	EXPECT_EQ( AreaPortalFromToken( AreaPortalTokenOf( pFirst ) ), pFirst );
	EXPECT_EQ( AreaPortalFromToken( AreaPortalTokenOf( pSecond ) ), pSecond );
}

TEST_F( AreaPortalList, TokensAreUniqueAndNonZero )
{
	std::vector< uint32_t > tokens;
	for ( int i = 0; i < 64; i++ )
		tokens.push_back( AreaPortalTokenOf( new CAreaPortal( i, 1 ) ) );

	for ( size_t i = 0; i < tokens.size(); i++ )
	{
		EXPECT_NE( tokens[i], 0u );
		for ( size_t j = i + 1; j < tokens.size(); j++ )
			EXPECT_NE( tokens[i], tokens[j] ) << "portals " << i << " and " << j;
	}
}

TEST_F( AreaPortalList, ATokenStopsResolvingWhenItsPortalGoes )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	const uint32_t hPortal = AreaPortalTokenOf( pPortal );

	delete pPortal;

	EXPECT_TRUE( AreaPortalFromToken( hPortal ) == NULL );
}

//-----------------------------------------------------------------------------
// THE ONE THIS FILE EXISTS FOR: a removed portal's token must not open or close
// the portal made after it. Tokens are never reused, so the new portal has a
// token of its own and the old one names nothing.
//-----------------------------------------------------------------------------
TEST_F( AreaPortalList, AStaleTokenDoesNotResolveToAPortalMadeAfterIt )
{
	CAreaPortal *pFirst = new CAreaPortal( 7, 1 );
	const uint32_t hStale = AreaPortalTokenOf( pFirst );

	delete pFirst;

	CAreaPortal *pSecond = new CAreaPortal( 9, 1 );

	EXPECT_NE( AreaPortalTokenOf( pSecond ), hStale ) << "the removed portal's token was handed out again";
	EXPECT_TRUE( AreaPortalFromToken( hStale ) == NULL )
		<< "a removed portal's token resolves to the portal made after it. The "
		   "removed entity's Open/Close inputs would drive somebody else's hole.";
	EXPECT_EQ( AreaPortalFromToken( AreaPortalTokenOf( pSecond ) ), pSecond );
}

TEST_F( AreaPortalList, ATokenNobodyWasGivenResolvesToNothing )
{
	new CAreaPortal( 7, 1 );

	EXPECT_TRUE( AreaPortalFromToken( 0xDEADBEEFu ) == NULL );
}

//-----------------------------------------------------------------------------
// THE ARITHMETIC NEVER PRODUCES 0. The counter is a function-local static, so
// no test in this binary can see its first value; the step is a pure function
// and is checked at its edges instead, the wrap included (unreachable in a
// process lifetime, and 0 is skipped there anyway).
//-----------------------------------------------------------------------------
TEST_F( AreaPortalList, TheTokenArithmeticCountsUpAndWrapsToOneNotZero )
{
	EXPECT_EQ( AreaPortalTokenAfter( 0 ), 1u );
	EXPECT_EQ( AreaPortalTokenAfter( 1 ), 2u );
	EXPECT_EQ( AreaPortalTokenAfter( 41 ), 42u );
	EXPECT_EQ( AreaPortalTokenAfter( 0xFFFFFFFEu ), 0xFFFFFFFFu );
	EXPECT_EQ( AreaPortalTokenAfter( 0xFFFFFFFFu ), 1u );
}

//=============================================================================//
// WHAT C# DECIDED — the two arms of UpdateVisibility with no engine in them.
//
// Both of these close the hole ON THE CLIENT as well as for flow purposes. The
// third arm, the backface test, deliberately does not, and it is the one that
// is not here because it reads the portal's plane out of the engine.
//=============================================================================//

TEST_F( AreaPortalList, AnOpenDoorPortalIsNotClosedByManagedState )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );

	bool bIsOpenOnClient = true;
	EXPECT_FALSE( pPortal->ClosedByManagedState( Vector( 0, 0, 0 ), 1.0f, bIsOpenOnClient ) );
	EXPECT_TRUE( bIsOpenOnClient );
}

TEST_F( AreaPortalList, AShutDoorPortalIsClosedOnTheClientToo )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	pPortal->m_bOpen = false;

	bool bIsOpenOnClient = true;
	EXPECT_TRUE( pPortal->ClosedByManagedState( Vector( 0, 0, 0 ), 1.0f, bIsOpenOnClient ) );
	EXPECT_FALSE( bIsOpenOnClient )
		<< "a shut door portal that stayed open on the client would let the "
		   "client draw the room the server has stopped sending.";
}

TEST_F( AreaPortalList, AWindowIsOpenInsideItsFadeDistance )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	pPortal->m_bWindow       = true;
	pPortal->m_flFadeDist    = 100.0f;
	pPortal->m_vecVolumeMins = Vector( -10, -10, -10 );
	pPortal->m_vecVolumeMaxs = Vector(  10,  10,  10 );

	bool bIsOpenOnClient = true;
	// 50 units from the origin, so 40 from the nearest face.
	EXPECT_FALSE( pPortal->ClosedByManagedState( Vector( 50, 0, 0 ), 1.0f, bIsOpenOnClient ) );
	EXPECT_TRUE( bIsOpenOnClient );
}

TEST_F( AreaPortalList, AWindowShutsPastItsFadeDistancePlusTheBuffer )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	pPortal->m_bWindow       = true;
	pPortal->m_flFadeDist    = 100.0f;
	pPortal->m_vecVolumeMins = Vector( -10, -10, -10 );
	pPortal->m_vecVolumeMaxs = Vector(  10,  10,  10 );

	bool bIsOpenOnClient = true;
	// 200 from the origin is 190 from the face, past 100 + 10.
	EXPECT_TRUE( pPortal->ClosedByManagedState( Vector( 200, 0, 0 ), 1.0f, bIsOpenOnClient ) );
	EXPECT_FALSE( bIsOpenOnClient );
}

//-----------------------------------------------------------------------------
// The buffer is not decoration: it is the ten units of hysteresis that stop the
// server cutting the far side off at the exact frame the pane goes opaque, which
// under net lag shows as popping. A viewer INSIDE the buffer is still sent to.
//-----------------------------------------------------------------------------
TEST_F( AreaPortalList, AWindowIsStillOpenInsideTheFadeBuffer )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	pPortal->m_bWindow       = true;
	pPortal->m_flFadeDist    = 100.0f;
	pPortal->m_vecVolumeMins = Vector( 0, 0, 0 );
	pPortal->m_vecVolumeMaxs = Vector( 0, 0, 0 );

	bool bIsOpenOnClient = true;
	// 105 units out: past the fade distance, inside the ten-unit buffer.
	EXPECT_FALSE( pPortal->ClosedByManagedState( Vector( 105, 0, 0 ), 1.0f, bIsOpenOnClient ) );
	EXPECT_TRUE( bIsOpenOnClient );
}

//-----------------------------------------------------------------------------
// The FOV factor multiplies the DISTANCE, not the threshold. A zoomed-in player
// sees further through a window, so the same viewer at the same place is inside
// the fade at one FOV and outside it at another.
//-----------------------------------------------------------------------------
TEST_F( AreaPortalList, TheFovFactorScalesTheMeasuredDistance )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	pPortal->m_bWindow       = true;
	pPortal->m_flFadeDist    = 100.0f;
	pPortal->m_vecVolumeMins = Vector( 0, 0, 0 );
	pPortal->m_vecVolumeMaxs = Vector( 0, 0, 0 );

	bool bIsOpenOnClient = true;
	EXPECT_FALSE( pPortal->ClosedByManagedState( Vector( 80, 0, 0 ), 1.0f, bIsOpenOnClient ) );

	bIsOpenOnClient = true;
	// The same 80 units, read as 160 by a factor of two: now past 100 + 10.
	EXPECT_TRUE( pPortal->ClosedByManagedState( Vector( 80, 0, 0 ), 2.0f, bIsOpenOnClient ) );
	EXPECT_FALSE( bIsOpenOnClient );
}

//-----------------------------------------------------------------------------
// A portal that is NOT a window ignores the fade distance entirely, however far
// away the viewer is. This is what keeps a door portal from shutting on a
// player across the map.
//-----------------------------------------------------------------------------
TEST_F( AreaPortalList, ADoorPortalIgnoresTheFadeDistance )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );
	pPortal->m_bWindow    = false;
	pPortal->m_flFadeDist = 1.0f;

	bool bIsOpenOnClient = true;
	EXPECT_FALSE( pPortal->ClosedByManagedState( Vector( 9000, 0, 0 ), 1.0f, bIsOpenOnClient ) );
	EXPECT_TRUE( bIsOpenOnClient );
}

//=============================================================================//
// WHAT A NEW PORTAL STARTS AS. The managed side sets the state right after
// creating one, so a wrong default is only visible for the frames between --
// but "open" is the value that cannot break a map, and it is stock's.
//=============================================================================//

TEST_F( AreaPortalList, ANewPortalStartsOpenAndIsNotAWindow )
{
	CAreaPortal *pPortal = new CAreaPortal( 7, 1 );

	EXPECT_TRUE( pPortal->m_bOpen );
	EXPECT_FALSE( pPortal->m_bWindow );
}

TEST_F( AreaPortalList, APortalRemembersItsNumberAndVersion )
{
	CAreaPortal *pPortal = new CAreaPortal( 41, 2 );

	EXPECT_EQ( pPortal->m_portalNumber, 41 );
	EXPECT_EQ( pPortal->m_iPortalVersion, 2 );
}
