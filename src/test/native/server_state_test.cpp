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
#include "standalone_list.h"

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

// The frame's overlap pairs (server_overlaps.h), on the level tier.

TEST( ServerOverlaps, APairReportedTwiceInEitherOrderIsListedOnce )
{
	CServerOverlaps overlaps;
	std::vector<uint32_t> list;

	overlaps.Append( list, 7, 9 );
	overlaps.Append( list, 9, 7 );
	overlaps.Append( list, 7, 9 );

	EXPECT_EQ( ( std::vector<uint32_t>{ 7, 9 } ), list );
}

TEST( ServerOverlaps, AnInvalidHandleOrAnEntityWithItselfIsNoPair )
{
	CServerOverlaps overlaps;
	std::vector<uint32_t> list;

	overlaps.Append( list, 0xFFFFFFFFu, 9 );
	overlaps.Append( list, 7, 0xFFFFFFFFu );
	overlaps.Append( list, 7, 7 );
	overlaps.Append( list, 3, 4 );

	EXPECT_EQ( ( std::vector<uint32_t>{ 3, 4 } ), list ) << "the control pair is listed";
}

TEST( ServerOverlaps, ForgettingAPairRemovesItInEitherOrderAndKeepsTheRest )
{
	CServerOverlaps overlaps;
	std::vector<uint32_t> list;
	overlaps.Append( list, 1, 2 );
	overlaps.Append( list, 3, 4 );

	overlaps.Forget( list, 2, 1 );

	EXPECT_EQ( ( std::vector<uint32_t>{ 3, 4 } ), list );
}

TEST( ServerOverlaps, DrainingMovesThePendingPairsIntoTheReportOnce )
{
	CServerOverlaps overlaps;
	overlaps.Append( overlaps.m_Pending, 1, 2 );
	overlaps.Append( overlaps.m_Report, 2, 1 );
	overlaps.Append( overlaps.m_Pending, 5, 6 );

	overlaps.DrainPending( overlaps.m_Report );

	EXPECT_EQ( ( std::vector<uint32_t>{ 2, 1, 5, 6 } ), overlaps.m_Report );
	EXPECT_TRUE( overlaps.m_Pending.empty() );
}

TEST( ServerOverlaps, AFullListRefusesMorePairsAndSaysSoOnce )
{
	CServerOverlaps overlaps;
	std::vector<uint32_t> list;

	for ( uint32_t i = 0; i < (uint32_t)CServerOverlaps::kMaxPairs; i++ )
		ASSERT_FALSE( overlaps.Append( list, 2 * i + 1, 2 * i + 2 ) );

	EXPECT_TRUE( overlaps.Append( list, 1000000, 1000001 ) ) << "the first refusal is reported";
	EXPECT_FALSE( overlaps.Append( list, 1000002, 1000003 ) ) << "and only the first";
	EXPECT_EQ( (size_t)CServerOverlaps::kMaxPairs * 2, list.size() );
}

TEST( ServerOverlaps, ClearForgetsEverythingIncludingTheFrameAndTheFullLatch )
{
	CServerOverlaps overlaps;
	overlaps.Append( overlaps.m_Pending, 1, 2 );
	overlaps.Append( overlaps.m_Report, 3, 4 );
	overlaps.Append( overlaps.m_SolverHeld, 5, 6 );
	overlaps.m_nReportFrame = 42;
	overlaps.m_bOverflowed = true;

	overlaps.Clear();

	EXPECT_TRUE( overlaps.m_Pending.empty() && overlaps.m_Report.empty() && overlaps.m_SolverHeld.empty() );
	EXPECT_EQ( -1, overlaps.m_nReportFrame );
	EXPECT_FALSE( overlaps.m_bOverflowed );
}

TEST( ServerOverlaps, ANewLevelStartsWithNoOverlapsLeftFromTheLast )
{
	CStateTier<CServerLevelState> level;

	CServerLevelState &first = level.Begin();
	first.m_Overlaps.Append( first.m_Overlaps.m_SolverHeld, 5, 6 );
	first.m_Overlaps.m_nReportFrame = 42;
	ASSERT_FALSE( first.m_Overlaps.m_SolverHeld.empty() ) << "the stimulus reached the first level";
	level.End();

	CServerLevelState &second = level.Begin();
	EXPECT_TRUE( second.m_Overlaps.m_SolverHeld.empty() ) << "a held pair survived the level change";
	EXPECT_EQ( -1, second.m_Overlaps.m_nReportFrame );
	level.End();
}

TEST( ServerStateTier, ANewDllTierHoldsNoStringTableOfTheLastOne )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &first = dll.Begin();
	EXPECT_EQ( nullptr, first.m_pMaterials );
	EXPECT_EQ( nullptr, first.m_pParticleEffectNames );
	first.m_pMaterials = reinterpret_cast<INetworkStringTable *>( 0x1000 );
	first.m_pParticleEffectNames = reinterpret_cast<INetworkStringTable *>( 0x2000 );
	dll.End();

	CServerDllState &second = dll.Begin();
	EXPECT_EQ( nullptr, second.m_pMaterials ) << "a second DLL tier kept the first's table";
	EXPECT_EQ( nullptr, second.m_pParticleEffectNames );
	dll.End();
}

TEST( ServerStateTier, ANewDllTierHasRegisteredNoStandaloneTier )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &first = dll.Begin();
	EXPECT_FALSE( first.m_StandaloneTiers.m_bRegistered );
	first.m_StandaloneTiers.m_bRegistered = true;
	first.m_StandaloneTiers.m_pClasses[0] = reinterpret_cast<ServerClass *>( 0x1000 );
	dll.End();

	CServerDllState &second = dll.Begin();
	EXPECT_FALSE( second.m_StandaloneTiers.m_bRegistered ) << "a second DLL tier thought its tiers were built";
	for ( int i = 0; i < SOURCESHARP_TIER_MAX; i++ )
	{
		EXPECT_EQ( nullptr, second.m_StandaloneTiers.m_pProps[i] );
		EXPECT_EQ( nullptr, second.m_StandaloneTiers.m_pTables[i] );
		EXPECT_EQ( nullptr, second.m_StandaloneTiers.m_pClasses[i] );
	}
	dll.End();
}

TEST( ServerStateTier, ANewDllTierHasDeclaredNoWorldNetTable )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &state = dll.Begin();
	EXPECT_EQ( nullptr, state.m_WorldNetTable.Class() ) << "worldspawn's class exists before C# declared its blob";
	EXPECT_EQ( 0, state.m_WorldNetTable.BlobBytes() );
	dll.End();
}

TEST( ServerStateTier, ANewDllTierHoldsNoPlayerStateOfTheLastOne )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &first = dll.Begin();
	first.m_Players.m_pPendingEdict = reinterpret_cast<edict_t *>( 0x1000 );
	first.m_Players.m_nShadowsLive = 2;
	first.m_Players.m_nShadowsOutlived = 1;
	first.m_Players.m_Fields.nGeneration = 9;
	dll.End();

	CServerDllState &second = dll.Begin();
	EXPECT_EQ( nullptr, second.m_Players.m_pPendingEdict ) << "a pending edict outlived its DLL tier";
	EXPECT_EQ( 0, second.m_Players.m_nShadowsLive );
	EXPECT_EQ( 0, second.m_Players.m_nShadowsOutlived );
	EXPECT_EQ( 0u, second.m_Players.m_Fields.nGeneration ) << "field placements carried into a new DLL tier";
	dll.End();
}

TEST( ServerStateTier, ANewLevelStartsWithNoPlayerOfTheLast )
{
	CStateTier<CServerLevelState> level;

	CServerLevelState &first = level.Begin();
	first.m_Players.m_pByIndex[1] = reinterpret_cast<CStandalonePlayer *>( 0x1000 );
	level.End();

	CServerLevelState &second = level.Begin();
	for ( int i = 0; i <= ss::kMaxPlayers; i++ )
		EXPECT_EQ( nullptr, second.m_Players.m_pByIndex[i] ) << "slot " << i << " kept a player of the last map";
	level.End();
}

TEST( ServerStateTier, ANewLevelHoldsNoShellOfTheLast )
{
	CStateTier<CServerLevelState> level;

	CServerLevelState &first = level.Begin();
	first.m_Shells.m_Live.push_back( reinterpret_cast<CStandaloneEntity *>( 0x1000 ) );
	first.m_Shells.m_PendingDeletes.push_back( reinterpret_cast<CStandaloneEntity *>( 0x1000 ) );
	level.End();

	CServerLevelState &second = level.Begin();
	EXPECT_TRUE( second.m_Shells.m_Live.empty() ) << "a shell of the last map is still listed";
	EXPECT_TRUE( second.m_Shells.m_PendingDeletes.empty() );
	level.End();
}

TEST( StandaloneList, IsEmptyWithNoLevelAndViewsTheLevelsShellsInOrder )
{
	const CStandaloneList none;
	EXPECT_EQ( 0u, none.size() );
	EXPECT_EQ( none.begin(), none.end() );

	CStandaloneEntity *const shells[] = { reinterpret_cast<CStandaloneEntity *>( 0x10 ),
										  reinterpret_cast<CStandaloneEntity *>( 0x20 ) };
	const CStandaloneList list( shells, 2 );

	std::vector<CStandaloneEntity *> seen;
	for ( CStandaloneEntity *p : list )
		seen.push_back( p );

	EXPECT_EQ( 2u, list.size() );
	EXPECT_EQ( ( std::vector<CStandaloneEntity *>{ shells[0], shells[1] } ), seen );
}

TEST( ServerStateTier, ANewDllTierHasIssuedNoWorldSerialAndLoadedNoSentences )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &state = dll.Begin();
	EXPECT_EQ( 0, state.m_nWorldSerial );
	EXPECT_FALSE( state.m_bSentencesInit );
	dll.End();
}

TEST( ServerStateTier, ANewLevelHasNoWorldUntilTheMapMakesOne )
{
	CStateTier<CServerLevelState> level;

	CServerLevelState &first = level.Begin();
	first.m_pWorld = reinterpret_cast<CWorld *>( 0x1000 );
	level.End();

	EXPECT_EQ( nullptr, level.Begin().m_pWorld ) << "the last map's world survived the level change";
	level.End();
}

TEST( ServerStateTier, ANewDllTierNamesPlayerOneAsTheCommandClient )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &first = dll.Begin();
	EXPECT_EQ( 0, first.m_nCommandClientSlot ) << "stock's initial slot is 0, player 1";
	first.m_nCommandClientSlot = -1;
	dll.End();

	EXPECT_EQ( 0, dll.Begin().m_nCommandClientSlot );
	dll.End();
}

TEST( ServerStateTier, ANewDllTierStartsWithItsCountsAndLatchesClear )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &first = dll.Begin();
	first.m_nInvisibleRangeReach = 7;
	first.m_bWarnedUnpublishedClass = true;
	first.m_pLastHandleComplaint = &first;
	first.m_szVariantString[0] = 'x';
	dll.End();

	CServerDllState &second = dll.Begin();
	EXPECT_EQ( 0, second.m_nInvisibleRangeReach );
	EXPECT_FALSE( second.m_bWarnedUnpublishedClass ) << "a warn-once latch stayed set into a new DLL tier";
	EXPECT_EQ( nullptr, second.m_pLastHandleComplaint );
	EXPECT_EQ( '\0', second.m_szVariantString[0] );
	dll.End();
}

TEST( ServerStateTier, ANewLevelWarnsAboutAShortBlobAgain )
{
	CStateTier<CServerLevelState> level;

	level.Begin().m_bWarnedShortBlob = true;
	level.End();

	EXPECT_FALSE( level.Begin().m_bWarnedShortBlob );
	level.End();
}

TEST( ServerStateTier, ANewDllTierHoldsNoEngineInterfaceOfTheLastOne )
{
	CStateTier<CServerDllState> dll;

	CServerDllState &first = dll.Begin();
	first.m_Engine.m_pEngine = reinterpret_cast<IVEngineServer *>( 0x1000 );
	first.m_Engine.m_pFileSystem = reinterpret_cast<IFileSystem *>( 0x1000 );
	first.m_Engine.m_pBaseFileSystem = reinterpret_cast<IBaseFileSystem *>( 0x1000 );
	first.m_Engine.m_pPartition = reinterpret_cast<ISpatialPartition *>( 0x1000 );
	first.m_Engine.m_pGameStats = reinterpret_cast<IUploadGameStats *>( 0x1000 );
	first.m_Engine.m_pStringTables = reinterpret_cast<INetworkStringTableContainer *>( 0x1000 );
	first.m_Engine.m_pModelInfo = reinterpret_cast<IVModelInfo *>( 0x1000 );
	first.m_Engine.m_pStaticProps = reinterpret_cast<IStaticPropMgrServer *>( 0x1000 );
	first.m_Engine.m_pTrace = reinterpret_cast<IEngineTrace *>( 0x1000 );
	first.m_Engine.m_pMdlCache = reinterpret_cast<IMDLCache *>( 0x1000 );
	first.m_Engine.m_pEvents = reinterpret_cast<IGameEventManager2 *>( 0x1000 );
	first.m_Engine.m_pOverlay = reinterpret_cast<IVDebugOverlay *>( 0x1000 );
	first.m_Engine.m_pRandom = reinterpret_cast<IUniformRandomStream *>( 0x1000 );
	first.m_Engine.m_pChangeInfo = reinterpret_cast<CSharedEdictChangeInfo *>( 0x1000 );
	dll.End();

	const CServerEngineInterfaces &engine = dll.Begin().m_Engine;
	EXPECT_EQ( nullptr, engine.m_pEngine ) << "the engine outlived the DLL that bound it";
	EXPECT_EQ( nullptr, engine.m_pFileSystem ) << "an engine interface outlived the DLL that acquired it";
	EXPECT_EQ( nullptr, engine.m_pBaseFileSystem );
	EXPECT_EQ( nullptr, engine.m_pPartition );
	EXPECT_EQ( nullptr, engine.m_pGameStats );
	EXPECT_EQ( nullptr, engine.m_pStringTables );
	EXPECT_EQ( nullptr, engine.m_pModelInfo );
	EXPECT_EQ( nullptr, engine.m_pStaticProps );
	EXPECT_EQ( nullptr, engine.m_pTrace );
	EXPECT_EQ( nullptr, engine.m_pMdlCache );
	EXPECT_EQ( nullptr, engine.m_pEvents );
	EXPECT_EQ( nullptr, engine.m_pOverlay );
	EXPECT_EQ( nullptr, engine.m_pRandom );
	EXPECT_EQ( nullptr, engine.m_pChangeInfo );
	dll.End();
}
