//========= SourceSharp — tests for chain_state_notify.h =====================//
//
// plan_baseentity.md Phase 13 step 2. networkvar.h's chain holder used to be a
// CBaseEntity * and a dereference; it is now an owner captured at the
// assignment, because the typedef turns that class name into IGameEntity and
// IGameEntity has no NetworkStateChanged().
//
// WHAT COULD GO WRONG HERE IS SILENCE. A notifier that forwards to nobody
// compiles, runs, and reports nothing — the entity simply never re-encodes and
// the value sits on the server, correct and unsent. That is the same failure
// mode standalone_netserial.h was written for, and no build catches it, so it
// is checked here.
//
// PROVEN RED. Emptying CChainStateNotify::NetworkStateChanged() — the exact
// silent failure above — fails five of the seven:
//
//   BoundOwnerHearsIt                              Actual: 0  Expected: 1
//   OnlyTheBoundOwnerHears                         "the bound owner heard 0 times"
//   RebindingMovesTheNotification                  "the new owner was not told"
//   EachOwnerTypeIsCalledAsItself                  Actual: 0  Expected: 10
//   AnOwnerWhoseBasesAreNotAtItsAddressIsStillFound  Actual: 0  Expected: 10
//
// The two that stay green are the two about NOT calling — StartsUnbound and
// UnboundNotifiesNobody — which a holder that never calls anything satisfies
// trivially. That split is the point.
//
//=============================================================================//
#include <gtest/gtest.h>

#include "../../public/chain_state_notify.h"

namespace
{

// Two unrelated owner types. The holder names neither, and that is the whole
// design: the concrete type is known only where `= this` is written.
class OwnerA
{
public:
	OwnerA()
		: m_nHeard( 0 )
	{
	}

	void NetworkStateChanged() { m_nHeard++; }

	int m_nHeard;
};

// TWO bases, so that one of them sits at a nonzero offset inside the owner and
// the owner's own address is not the address of that base. A holder that cast
// the stored void * back to some fixed type rather than to the type it was
// given would land there and be wrong by the base's offset — the same hazard as
// the handle crossing, one header over. Single inheritance would put every base
// at offset zero, and the test below would pass whatever the holder did; it
// checks that it can tell the difference before checking anything else.
class Leading
{
public:
	virtual ~Leading() {}

	int m_nLead[3];
};

class Trailing
{
public:
	virtual ~Trailing() {}

	int m_nTrail[3];
};

class OwnerB : public Leading, public Trailing
{
public:
	OwnerB()
		: m_nHeard( 0 )
	{
	}

	void NetworkStateChanged() { m_nHeard += 10; }

	int m_nHeard;
};

// --- nobody bound -------------------------------------------------------------

TEST( ChainStateNotify, StartsUnbound )
{
	CChainStateNotify notify;
	EXPECT_FALSE( notify.IsBound() );
}

TEST( ChainStateNotify, UnboundNotifiesNobodyAndDoesNotCrash )
{
	// The window every embedded struct passes through: constructed, and the
	// containing entity has not run IMPLEMENT_NETWORKVAR_CHAIN yet. Stock
	// dereferenced NULL here.
	CChainStateNotify notify;
	notify.NetworkStateChanged();
	SUCCEED();
}

// --- bound --------------------------------------------------------------------

TEST( ChainStateNotify, BoundOwnerHearsIt )
{
	OwnerA owner;
	CChainStateNotify notify;
	notify = &owner;

	ASSERT_TRUE( notify.IsBound() );
	notify.NetworkStateChanged();

	EXPECT_EQ( 1, owner.m_nHeard ) << "the chain forwarded to nobody — the entity would never re-encode "
									  "and nothing anywhere would say so";
}

TEST( ChainStateNotify, OnlyTheBoundOwnerHears )
{
	OwnerA bound;
	OwnerA other;

	CChainStateNotify notify;
	notify = &bound;
	notify.NetworkStateChanged();

	EXPECT_EQ( 1, bound.m_nHeard ) << "the bound owner heard " << bound.m_nHeard << " times";
	EXPECT_EQ( 0, other.m_nHeard ) << "an owner nothing was bound to was told";
}

TEST( ChainStateNotify, RebindingMovesTheNotification )
{
	OwnerA first;
	OwnerA second;

	CChainStateNotify notify;
	notify = &first;
	notify = &second;
	notify.NetworkStateChanged();

	EXPECT_EQ( 0, first.m_nHeard ) << "the old owner is still being told";
	EXPECT_EQ( 1, second.m_nHeard ) << "the new owner was not told";
}

// --- the type is captured per assignment, not fixed ---------------------------

TEST( ChainStateNotify, EachOwnerTypeIsCalledAsItself )
{
	// The property that makes the holder able to name no class at all: one
	// holder, two unrelated owner types, each reached through its own
	// NetworkStateChanged. A fixed cast could serve at most one of them, and
	// OwnerB is laid out so serving it as one of its own bases would land a
	// vtable pointer and three ints away.
	OwnerA a;
	OwnerB b;

	CChainStateNotify notify;

	notify = &a;
	notify.NetworkStateChanged();

	notify = &b;
	notify.NetworkStateChanged();

	EXPECT_EQ( 1, a.m_nHeard );
	EXPECT_EQ( 10, b.m_nHeard ) << "the second owner was not reached as itself; OwnerB's counter is "
								   "past a base subobject and only a cast back to OwnerB finds it";
}

TEST( ChainStateNotify, AnOwnerWhoseBasesAreNotAtItsAddressIsStillFound )
{
	OwnerB owner;

	// The premise, asserted rather than assumed: the address the holder is
	// given is OwnerB's, and at least one of its bases is somewhere else. If
	// that stopped being true this test would pass whatever the holder did.
	ASSERT_NE( (const void *)&owner, (const void *)static_cast<Trailing *>( &owner ) )
		<< "OwnerB's bases are all at offset zero now, so this test cannot "
		   "tell a typed round trip from an untyped one";

	CChainStateNotify notify;
	notify = &owner;
	notify.NetworkStateChanged();

	EXPECT_EQ( 10, owner.m_nHeard );
}

} // namespace
