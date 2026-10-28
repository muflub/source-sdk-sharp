//========= SourceSharp — the game DLL's two heap tiers, as facts ===========//
//
// server_state.h's CStateTier is what makes the level reset structural
// (plan_globals.md §2, plan_server_native_v2 T6): LevelInit begins the level
// tier, LevelShutdown ends it, and a second LevelInit must start from a NEW
// object, never the old one with its fields left over. These facts hold that
// over a probe type whose fields show reuse, and over the two real tiers.
//
//=============================================================================//

#include <gtest/gtest.h>

#include "server_state.h"

namespace
{

// A tier member whose state shows whether an object was reused, and whose
// destructor reports what the tier answered while it ran.
struct Probe_t
{
	static int s_nLive;
	static int s_nDestroyed;
	static const CStateTier<Probe_t> *s_pTier;
	static const Probe_t *s_pSeenDuringDestroy;

	int m_nField = 0;

	Probe_t() { ++s_nLive; }

	~Probe_t()
	{
		--s_nLive;
		++s_nDestroyed;
		s_pSeenDuringDestroy = s_pTier ? s_pTier->Get() : nullptr;
	}
};

int Probe_t::s_nLive = 0;
int Probe_t::s_nDestroyed = 0;
const CStateTier<Probe_t> *Probe_t::s_pTier = nullptr;
const Probe_t *Probe_t::s_pSeenDuringDestroy = nullptr;

void ResetProbe( const CStateTier<Probe_t> *pTier )
{
	Probe_t::s_nLive = 0;
	Probe_t::s_nDestroyed = 0;
	Probe_t::s_pTier = pTier;
	Probe_t::s_pSeenDuringDestroy = reinterpret_cast<const Probe_t *>( 1 );
}

} // namespace

TEST( ServerStateTier, IsEmptyBeforeBeginAndAfterEnd )
{
	CStateTier<Probe_t> tier;
	ResetProbe( &tier );

	EXPECT_EQ( nullptr, tier.Get() ) << "a tier exists before its window opens";

	tier.Begin();
	ASSERT_NE( nullptr, tier.Get() ) << "Begin built no object";
	EXPECT_EQ( 1, Probe_t::s_nLive );

	tier.End();
	EXPECT_EQ( nullptr, tier.Get() ) << "a tier outlives its window";
	EXPECT_EQ( 0, Probe_t::s_nLive ) << "End did not destroy the object";
}

// THE RESET. A second LevelInit after a LevelShutdown starts from a fresh
// object: a field the first level wrote is not there.
TEST( ServerStateTier, ASecondBeginAfterEndStartsFromAFreshObject )
{
	CStateTier<Probe_t> tier;
	ResetProbe( &tier );

	tier.Begin().m_nField = 42;
	ASSERT_EQ( 42, tier.Get()->m_nField );
	tier.End();

	tier.Begin();
	ASSERT_NE( nullptr, tier.Get() );
	EXPECT_EQ( 0, tier.Get()->m_nField ) << "the second level saw the first level's field: the tier was reused";
	EXPECT_EQ( 1, Probe_t::s_nDestroyed ) << "the first level's object was not destroyed";
	tier.End();
}

// A Begin with no End before it (a LevelInit the engine sent without a
// LevelShutdown) still starts fresh, and destroys the stale object.
TEST( ServerStateTier, ABeginWithoutAnEndReplacesTheStaleObject )
{
	CStateTier<Probe_t> tier;
	ResetProbe( &tier );

	tier.Begin().m_nField = 7;
	tier.Begin();

	ASSERT_NE( nullptr, tier.Get() );
	EXPECT_EQ( 0, tier.Get()->m_nField ) << "a missed End left the stale object in use";
	EXPECT_EQ( 1, Probe_t::s_nDestroyed ) << "the stale object leaked";
	EXPECT_EQ( 1, Probe_t::s_nLive );
	tier.End();
}

// End clears the pointer before the destructor runs, so code the destructor
// reaches sees no tier rather than a half-destroyed one.
TEST( ServerStateTier, EndClearsThePointerBeforeTheDestructorRuns )
{
	CStateTier<Probe_t> tier;
	ResetProbe( &tier );

	tier.Begin();
	tier.End();

	EXPECT_EQ( 1, Probe_t::s_nDestroyed ) << "the destructor never ran: nothing was observed";
	EXPECT_EQ( nullptr, Probe_t::s_pSeenDuringDestroy )
		<< "the destructor saw the tier still pointing at the object being destroyed";
}

TEST( ServerStateTier, EndOnAnEmptyTierIsANoOp )
{
	CStateTier<Probe_t> tier;
	ResetProbe( &tier );

	tier.End();
	EXPECT_EQ( nullptr, tier.Get() );
	EXPECT_EQ( 0, Probe_t::s_nDestroyed );
}

// The two real tiers instantiate and follow the same window.
TEST( ServerStateTier, TheDllAndLevelTiersOpenAndClose )
{
	CStateTier<CServerDllState> dll;
	CStateTier<CServerLevelState> level;

	dll.Begin();
	level.Begin();
	CServerLevelState *pFirst = level.Get();
	EXPECT_NE( nullptr, dll.Get() );
	EXPECT_NE( nullptr, pFirst );

	level.End();
	EXPECT_EQ( nullptr, level.Get() );
	EXPECT_NE( nullptr, dll.Get() ) << "ending the level ended the DLL tier";

	dll.End();
	EXPECT_EQ( nullptr, dll.Get() );
}

//-----------------------------------------------------------------------------
// ENTITYSLOTS in the DLL tier (plan_server_native_v2 T6): the control block and
// the slot table were file-scope statics, and are CServerDllState's members.
// A DLL tier starts with nothing open and nothing bound, and a second DLL tier
// (DLLShutdown then DLLInit in one process) does not inherit the first's.
//-----------------------------------------------------------------------------
TEST( ServerStateTier, ANewDllTierStartsWithNoSlotOpenAndNothingBound )
{
	CStateTier<CServerDllState> dll;
	CServerDllState &first = dll.Begin();

	EXPECT_EQ( 0, first.m_EntitySlots.ActiveCount() );
	EXPECT_EQ( 0, first.m_SlotTable.BoundCount() );

	// The positive arm: the first tier really did open a slot and bind an entry.
	uint32_t nSerial = 0;
	ASSERT_TRUE( first.m_EntitySlots.Open( 5, 0x12345u, &nSerial ) );
	int nShell = 0;
	ASSERT_TRUE( first.m_SlotTable.BindAt( first.m_SlotTable.IssueHandle( 7 ), &nShell ) );
	ASSERT_EQ( 1, first.m_EntitySlots.ActiveCount() );
	ASSERT_EQ( 1, first.m_SlotTable.BoundCount() );

	dll.End();
	CServerDllState &second = dll.Begin();

	EXPECT_EQ( 0, second.m_EntitySlots.ActiveCount() ) << "a second DLL tier inherited the first's open entity slot";
	EXPECT_EQ( 0, second.m_SlotTable.BoundCount() ) << "a second DLL tier inherited the first's slot-table binding";
	dll.End();
}

// Two DLL tiers alive at once (the two-instance fact plan_globals G2 asks of
// every subsystem): a slot opened in one is not open in the other.
TEST( ServerStateTier, TwoDllTiersShareNoEntitySlotState )
{
	CStateTier<CServerDllState> a;
	CStateTier<CServerDllState> b;
	a.Begin();
	b.Begin();

	uint32_t nSerial = 0;
	ASSERT_TRUE( a.Get()->m_EntitySlots.Open( 3, 0x2003u, &nSerial ) );
	EXPECT_EQ( 1, a.Get()->m_EntitySlots.ActiveCount() );
	EXPECT_EQ( 0, b.Get()->m_EntitySlots.ActiveCount() ) << "the two DLL tiers share one control block";

	a.End();
	b.End();
}

//-----------------------------------------------------------------------------
// NETTYPES in the DLL tier (T6): the published type table was a function-local
// static. A DLL tier starts with no type published, a second one does not
// inherit the first's, and two live tiers do not share one.
//-----------------------------------------------------------------------------
TEST( ServerStateTier, ANewDllTierStartsWithNoNetTypePublished )
{
	CStateTier<CServerDllState> dll;
	CServerDllState &first = dll.Begin();
	EXPECT_EQ( 0, first.m_NetTypes.Count() );

	// The positive arm: the first tier really published one.
	ASSERT_GE( first.m_NetTypes.Publish( "prop_t6_probe", 0, 0x1234u, 64, 0 ), 0 );
	ASSERT_EQ( 1, first.m_NetTypes.Count() );
	ASSERT_NE( nullptr, first.m_NetTypes.ByName( "prop_t6_probe" ) );

	dll.End();
	CServerDllState &second = dll.Begin();
	EXPECT_EQ( 0, second.m_NetTypes.Count() ) << "a second DLL tier inherited the first's published types";
	EXPECT_EQ( nullptr, second.m_NetTypes.ByName( "prop_t6_probe" ) );
	dll.End();
}

TEST( ServerStateTier, TwoDllTiersShareNoNetTypes )
{
	CStateTier<CServerDllState> a;
	CStateTier<CServerDllState> b;
	a.Begin();
	b.Begin();

	ASSERT_GE( a.Get()->m_NetTypes.Publish( "prop_t6_probe", 0, 0x1234u, 64, 0 ), 0 );
	EXPECT_EQ( 1, a.Get()->m_NetTypes.Count() );
	EXPECT_EQ( 0, b.Get()->m_NetTypes.Count() ) << "the two DLL tiers share one type table";

	a.End();
	b.End();
}

//-----------------------------------------------------------------------------
// TEMPENTS in the DLL tier (T6): the built tables, their names, the closed
// latch and the encode scratch were file-scope statics. A DLL tier starts with
// nothing built and registration open; a second one does not inherit the
// first's; two live tiers share nothing.
//-----------------------------------------------------------------------------
TEST( ServerStateTier, ANewDllTierStartsWithNoTempEntBuiltAndRegistrationOpen )
{
	CStateTier<CServerDllState> dll;
	CServerDllState &first = dll.Begin();
	EXPECT_EQ( 0, first.m_TempEnts.m_nBuilt );
	EXPECT_FALSE( first.m_TempEnts.m_bClosed );
	EXPECT_TRUE( first.m_TempEnts.m_Names.empty() );
	EXPECT_EQ( nullptr, first.m_TempEnts.m_Built[0].pClassName );

	// The positive arm: the first tier really registered and closed.
	first.m_TempEnts.m_Built[0].pClassName = first.m_TempEnts.m_Names.emplace( "CTEProbe" ).first->c_str();
	first.m_TempEnts.m_nBuilt = 1;
	first.m_TempEnts.m_bClosed = true;
	first.m_TempEnts.m_Scratch[0] = 0xA5;

	dll.End();
	CServerDllState &second = dll.Begin();
	EXPECT_EQ( 0, second.m_TempEnts.m_nBuilt ) << "a second DLL tier inherited the first's tables";
	EXPECT_FALSE( second.m_TempEnts.m_bClosed ) << "a second DLL tier starts with registration closed";
	EXPECT_TRUE( second.m_TempEnts.m_Names.empty() ) << "a second DLL tier inherited the first's names";
	EXPECT_EQ( nullptr, second.m_TempEnts.m_Built[0].pClassName );
	EXPECT_EQ( 0, second.m_TempEnts.m_Scratch[0] );
	dll.End();
}

TEST( ServerStateTier, TwoDllTiersShareNoTempEntState )
{
	CStateTier<CServerDllState> a;
	CStateTier<CServerDllState> b;
	a.Begin();
	b.Begin();

	a.Get()->m_TempEnts.m_nBuilt = 3;
	a.Get()->m_TempEnts.m_bClosed = true;
	EXPECT_EQ( 0, b.Get()->m_TempEnts.m_nBuilt ) << "the two DLL tiers share one TE registry";
	EXPECT_FALSE( b.Get()->m_TempEnts.m_bClosed );

	a.End();
	b.End();
}
