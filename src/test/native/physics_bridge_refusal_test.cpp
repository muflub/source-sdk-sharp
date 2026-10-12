//========= SourceSharp — a refused physics-bridge path ======================//
//
// `sourcesharp/game/shared/physics_bridge_refusal.h` is the type the two
// `physics.cpp` files use to say plainly that a path is refused. §7's rule is
// that a refusal is "an entry with a reason, never a silent no-op", and the
// type's whole job is to make the silent version impossible to write by
// accident.
//
// WHAT IS WORTH CHECKING, AND IT IS NOT THE LATCH ON ITS OWN.
//
//   * the print happens ONCE and the COUNT keeps rising. Those are two
//     different jobs in one call and an implementation that collapses them --
//     `if (said) return false;` without the increment -- passes any test that
//     only watches the first two calls. The count is the half that can be
//     measured on a live run, and it is the half a refusal argued vacuous is
//     gated on.
//   * a refusal with an empty reason is NOT declared. That is the stub wearing
//     the type, which is exactly what §7 forbids, and `IsDeclared` is what a
//     future edit trips over.
//
//=============================================================================//

#include "gtest/gtest.h"
#include <string>
#include "../game/shared/physics_bridge_refusal.h"

namespace
{

const char *const kWhat = "the event-driven contact touchlink";
const char *const kWhy  = "physics_main_shared.cpp is the only producer and the "
                          "only drainer, and it leaves both vpcs.";

//-----------------------------------------------------------------------------
// The print is once. Everything after the first reach answers false, so a site
// that prints on `Reached()` prints one line however hot the path is.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, TheFirstReachIsTheOnlyOneThatSaysSo )
{
	CPhysicsBridgeRefusal refusal( kWhat, kWhy );

	EXPECT_TRUE( refusal.Reached() );
	EXPECT_FALSE( refusal.Reached() );
	EXPECT_FALSE( refusal.Reached() );
}

//-----------------------------------------------------------------------------
// The COUNT is not the latch. This is the fact that separates "said once" from
// "reached once", and an implementation that returns early without counting
// passes the test above and fails this one.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, TheCountKeepsRisingAfterTheFirstPrint )
{
	CPhysicsBridgeRefusal refusal( kWhat, kWhy );

	for ( int i = 0; i < 17; i++ )
	{
		refusal.Reached();
	}

	EXPECT_EQ( 17, refusal.ReachCount() );
}

//-----------------------------------------------------------------------------
// A refusal nothing reached reads zero. This is the value every refusal this
// lane declares predicts after the flip, so it has to be distinguishable from
// "reached once" rather than being the same default.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, AnUnreachedRefusalCountsZero )
{
	CPhysicsBridgeRefusal refusal( kWhat, kWhy );

	EXPECT_EQ( 0, refusal.ReachCount() );

	refusal.Reached();
	EXPECT_EQ( 1, refusal.ReachCount() );
}

//-----------------------------------------------------------------------------
// §7: "a reason, never a silent no-op". A refusal carrying both strings is
// declared.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, ARefusalWithBothStringsIsDeclared )
{
	CPhysicsBridgeRefusal refusal( kWhat, kWhy );

	EXPECT_TRUE( refusal.IsDeclared() );
	EXPECT_EQ( std::string( kWhat ), std::string( refusal.What() ) );
	EXPECT_EQ( std::string( kWhy ), std::string( refusal.Refusal() ) );
}

//-----------------------------------------------------------------------------
// An EMPTY reason is the stub wearing the type. Not declared.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, AnEmptyReasonIsNotADeclaredRefusal )
{
	CPhysicsBridgeRefusal refusal( kWhat, "" );

	EXPECT_FALSE( refusal.IsDeclared() );
}

//-----------------------------------------------------------------------------
// And so is a missing one. NULL is what a site that forgot the argument
// entirely would leave, and it must not read as declared either -- nor crash
// the accessors, which run inside a physics callback.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, ANullReasonIsNotDeclaredAndReadsEmpty )
{
	CPhysicsBridgeRefusal refusal( kWhat, 0 );

	EXPECT_FALSE( refusal.IsDeclared() );
	EXPECT_EQ( std::string( "" ), std::string( refusal.Refusal() ) );
}

//-----------------------------------------------------------------------------
// An empty WHAT is not declared either. The two strings are not interchangeable
// -- a refusal that says why but not what is unfindable in a log -- so both
// halves are checked, and a needle that only watched the reason would miss this.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, AnEmptyCapabilityNameIsNotADeclaredRefusal )
{
	CPhysicsBridgeRefusal refusal( "", kWhy );

	EXPECT_FALSE( refusal.IsDeclared() );
}

//-----------------------------------------------------------------------------
// Two refusals are independent. Each site declares its own function-local
// static, and one path being reached must not consume another's first print --
// a shared latch would silence every refusal but the first one to fire.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, TwoRefusalsDoNotShareALatch )
{
	CPhysicsBridgeRefusal first( kWhat, kWhy );
	CPhysicsBridgeRefusal second( "the timed debug overlay", kWhy );

	EXPECT_TRUE( first.Reached() );
	EXPECT_TRUE( second.Reached() );

	EXPECT_EQ( 1, first.ReachCount() );
	EXPECT_EQ( 1, second.ReachCount() );
}

//-----------------------------------------------------------------------------
// The format string names both halves, in order. The two `physics.cpp` files
// print through it, so a grep for the phrase finds every refusal in the bridge
// -- and an edit that dropped one of the two `%s` would silently print half a
// refusal.
//-----------------------------------------------------------------------------
TEST( PhysicsBridgeRefusal, TheSharedFormatCarriesBothHalvesAndSaysItIsARefusal )
{
	const std::string fmt( SOURCESHARP_PHYSICS_REFUSAL_FMT );

	const size_t first = fmt.find( "%s" );
	ASSERT_NE( std::string::npos, first );
	const size_t second = fmt.find( "%s", first + 2 );
	EXPECT_NE( std::string::npos, second ) << "the reason has no slot to print in";

	EXPECT_NE( std::string::npos, fmt.find( "REFUSED" ) );
	EXPECT_NE( std::string::npos, fmt.find( "SourceSharp" ) );
}

} // namespace
