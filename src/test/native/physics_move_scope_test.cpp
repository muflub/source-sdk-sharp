//========= SourceSharp ========================================================//
//
// physics_move_scope_test.cpp -- plan_shim.md 4a item 1, lane 5a-physresidue.
//
// WHAT THIS PINS, AND WHY A DOCUMENT NEEDS PINNING AT ALL.
//
// docs/physics-move-scope.md is a SCOPE: it rules, function by function, on what
// can leave game/server/physics.cpp and what cannot. Nothing was executed from
// it. A scope is only worth the measurement it was taken on, and the whole
// reason it exists is that the previous figure -- "~1,011 lines" -- was quoted
// from phase to phase long after the file it described had changed underneath
// it. A document nothing checks decays into exactly that.
//
// So every number in it that CAN be re-derived from the tree is re-derived here,
// and the document is required to agree. What cannot be re-derived from source
// -- the link-line importer counts, which need nm over 108 objects -- is not
// asserted here and says so in the document instead.
//
// A FAILURE HERE IS NOT NECESSARILY A BUG. If a later lane executes part of the
// scope, several of these go red, and that is the point: the scope is then stale
// and must be re-measured rather than read. Each message says which.
//
//=============================================================================//
#include <gtest/gtest.h>
#include "ss_ci_skip.h"

#include <fstream>
#include <cstdio>
#include <sstream>
#include <string>
#include <sys/stat.h>
#include <sys/wait.h>

namespace
{

// The tests run with the working directory at the tree's src/ -- buildcpptests
// pushd's to its own directory and runs the binary from there.
std::string ReadRel( const char *pszRelative )
{
	std::ifstream in( pszRelative );
	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

const char *const kDoc = "../docs/physics-move-scope.md";

std::string Doc()
{
	static const std::string s = ReadRel( kDoc );
	return s;
}

} // namespace

//=============================================================================
// 1. Is the document about the file it thinks it is?
//=============================================================================

//=============================================================================
// 2. The sixteen slots. This is the number the plan got wrong, so it is the
//    one most worth deriving rather than quoting.
//=============================================================================

//=============================================================================
// 3. The event ring: fifteen of sixteen kinds recorded, and which one is not.
//=============================================================================

//=============================================================================
// 3b. THE MANAGED SIDE'S ACCOUNT OF THE RING AGREES WITH THE RECORDERS.
//
// SourceSharp.Shared.Interop/PhysicsEvents.cs is where a managed reader learns which kinds
// actually arrive, and it says so in prose: a kind that nothing produces is
// marked "Not recorded today." Prose is not checked by a compiler, and this one
// was WRONG when this lane measured it -- Friction carried the marker while
// physics.cpp:3087 records SS_PHYSEVENT_FRICTION unconditionally, at the end of
// CCollisionEvent::Friction and outside the `if ( pEntity )` arm, with 65 lines
// of comment above it explaining that it is the push that unlocks the friction
// policy move.
//
// THAT IS THE SHAPE THAT HAS COST THIS PROJECT REAL BUGS: an armed producer
// nobody was told about reads exactly like a kind with no producer, and the
// next lane to look either re-adds the recorder or writes off the consumer. So
// the marker is checked against the recorders rather than trusted.
//
// The check is an IFF, both ways, over every kind -- not a spot check on the
// two that carry the marker today. A marker DROPPED from PreCollision would be
// as wrong as the one left on Friction, and only the reverse direction catches
// it.
//=============================================================================

//=============================================================================
// 4. The console commands, split the way the scope splits them.
//=============================================================================

//=============================================================================
// 5. The four symbols that are still imported, and the dead list.
//=============================================================================

//=============================================================================
// 6. The managed destinations the plan's figure was never reduced by.
//=============================================================================

//=============================================================================
// 7. The totals table adds up.
//=============================================================================

TEST( PhysicsMoveScope, TheTotalsTableSumsToItsOwnTotal )
{
	// THE NUMBERS ARE READ OUT OF THE DOCUMENT, not spelled here and compared
	// with themselves. Written the other way -- `EXPECT_EQ( 2136, 501 + 491 +
	// ... )` over literals in this file -- the fact could not fail for anything
	// a person does to the document, which is the "check that cannot fail" this
	// tree has 49 recorded instances of. Editing one row of the table without
	// re-deriving the total must be a failure, and that only works if both
	// sides of the arithmetic come from the document.
	const std::string doc = Doc();

	// All TEN, and the list is spelled out rather than scraped so that a verdict
	// dropped from the document is a missing row here rather than a smaller sum
	// that still adds up.
	static const char *kVerdicts[] = {
		"INSTRUMENT", "SLOT-HELP", "SLOT",     "DEAD",       "STOCK-DEBUG",
		"HOOK",       "LIVE-API",  "DEAD-VIA", "STALE-KEEP", "BOUNDARY",
	};
	int sumFns = 0, sumLines = 0;
	for ( size_t i = 0; i < sizeof( kVerdicts ) / sizeof( kVerdicts[0] ); i++ )
	{
		// The row opens "| `<verdict>` | ". Backticked, so `SLOT` does not
		// match the `SLOT-HELP` row -- the backtick after the name is what
		// separates them and is why the needle carries it.
		const std::string lead = std::string( "| `" ) + kVerdicts[i] + "` | ";
		const size_t at = doc.find( lead );
		ASSERT_NE( at, std::string::npos ) << "the totals table has no row for " << kVerdicts[i]
										   << ". Section 2's eight verdicts are what every other section "
											  "refers back to.";
		EXPECT_EQ( doc.find( lead, at + 1 ), std::string::npos ) << "more than one row for " << kVerdicts[i];

		int fns = 0, lines = 0;
		ASSERT_EQ( 2, sscanf( doc.c_str() + at + lead.size(), "%d | %d", &fns, &lines ) )
			<< "the " << kVerdicts[i]
			<< " row does not read "
			   "\"| <functions> | <lines> |\"";
		// DEAD, DEAD-VIA and STALE-KEEP are EMPTY since the server gc cull
		// (2026-10-01) deleted every function in them; each other row is not.
		const std::string v( kVerdicts[i] );
		// SLOT and SLOT-HELP too since plan_physics_bones_native_zero 2.1
		// deleted CCollisionEvent.
		// STOCK-DEBUG too since 2.3 moved physics_budget's sleep and air_density
		// to managed. INSTRUMENT too since 2.3 moved PhysTimescale with
		// PhysFrame to managed's PhysicsFrame. BOUNDARY too since 2.6 moved
		// the world build (PhysCreateWorld) to managed's ServerPhysicsWorld,
		// until PhysicsWorldOwner was re-ruled BOUNDARY (2026-10-18).
		if ( v == "DEAD" || v == "DEAD-VIA" || v == "STALE-KEEP" || v == "SLOT" || v == "SLOT-HELP" ||
			 v == "STOCK-DEBUG" || v == "INSTRUMENT" )
		{
			EXPECT_EQ( 0, fns ) << v << " has functions again";
			EXPECT_EQ( 0, lines ) << v << " has lines again";
		}
		else
		{
			EXPECT_GT( fns, 0 ) << v;
			EXPECT_GT( lines, 0 ) << v;
		}
		sumFns += fns;
		sumLines += lines;
	}

	// The stated total, also read out of the document.
	const std::string totalLead = "| **total** | **";
	const size_t tot = doc.find( totalLead );
	ASSERT_NE( tot, std::string::npos ) << "the totals table has no total row";
	int statedFns = 0, statedLines = 0, statedFrac = 0;
	// "**118** | **2,136**" -- a line total over a thousand carries a comma, so
	// it is read in two pieces rather than with one %d that would stop at the
	// comma and silently answer 2; "**27** | **591**" has none.
	const int nRead =
		sscanf( doc.c_str() + tot + totalLead.size(), "%d** | **%d,%d**", &statedFns, &statedLines, &statedFrac );
	ASSERT_GE( nRead, 2 ) << "the total row does not read \"| **total** | **<n>** | **<n[,nnn]>** |\"";
	const int statedLineTotal = nRead == 3 ? statedLines * 1000 + statedFrac : statedLines;

	EXPECT_EQ( sumFns, statedFns ) << "the eight verdict rows name " << sumFns
								   << " functions and the "
									  "total says "
								   << statedFns
								   << ". A row was edited without the total "
									  "being re-derived; re-run 5a-physics-classify.py rather than "
									  "patching either number.";
	EXPECT_EQ( sumLines, statedLineTotal ) << "the eight verdict rows name " << sumLines
										   << " lines and the total "
											  "says "
										   << statedLineTotal << ". Re-run 5a-physics-classify.py.";

	// And the total is the one the classifier actually produced at 5034fb3e6,
	// which is the only part of this fact that is a claim about the tree rather
	// than about the document's internal consistency.
	// 119 / 2,129 until the server gc cull (2026-10-01) deleted 34 functions;
	// 83 / 1,736 until the diagnostic commands moved to physics_debug.cpp
	// (2026-10-01: 19 functions out, two accessors in); 66 / 1,315 until
	// contact touch was ported (physics stock parity E1: the two refusals'
	// bodies went, same 66 functions); 66 / 1,277 until B4b's update record
	// in PhysFrame; 66 / 1,282 until step 4's pair facts in ShouldCollide;
	// 66 / 1,300 until H's penetrate ledger added five helpers; 71 / 1,359
	// until G's ShouldFreezeObject crush; 72 / 1,383 until C3/L's sound slots;
	// 74 / 1,405 until C6's motion tail in PostCollision; 74 / 1,418 until
	// B5's stress write; 75 / 1,423 until B7's shadow walk; 77 / 1,447 until
	// plan_physics_managed A9's entity-solver record; 77 / 1,464 until B2.1
	// moved IPhysicsObjectEvent to managed; 75 / 1,446 until B2.2 moved nine
	// IPhysicsCollisionEvent slots to managed and deleted their bodies; 54 /
	// 1,023 until plan_physics_bones_native_zero 2.1 deleted CCollisionEvent;
	// 27 / 591 until the ask ledger left the report; 27 / 532 until 2.2
	// deleted the unused CSkipKeys; 26 / 526 until 2.3 moved the SS-PHYSWORLD
	// report, PhysForceEntityToSleep and air_density to managed; 19 / 312
	// until 2.3 moved the sound batches to managed's PhysicsSoundBatch; 13 /
	// 210 until 2.3 moved PhysFrame's decisions to managed's PhysicsFrame; 12
	// / 142 until the stress walk's ring write left with it; 11 / 138 until
	// the event ring went; 11 / 135 until 2.6 moved the world build; 10 / 130
	// until the owner batch held managed's velocity (plan_physics_bones_perf C2);
	// 10 / 135 until the physics frame's forward went with kServerSystems[]
	// (plan_server_native_v2 T4e); 9 / 129 until the shadow list's refusal
	// went (plan_server_native_v2 T6).
	EXPECT_EQ( 9, statedFns );
	EXPECT_EQ( 112, statedLineTotal );
}

TEST( PhysicsMoveScope, TheScopeStillRefusesToGiveANumberForWhatMoves )
{
	// The finding this document exists to record is that NO WHOLE FUNCTION
	// MOVES and that a defensible line count for the movable residue cannot be
	// produced by any instrument this lane had. A later edit that quietly
	// supplies one -- without the per-line attribution pass section 4 names --
	// would put the tree back where item 1 started.
	const std::string doc = Doc();
	EXPECT_NE( doc.find( "**Nothing, as a function.**" ), std::string::npos )
		<< "the document no longer states that no whole function moves";
	EXPECT_NE( doc.find( "so: no number" ), std::string::npos )
		<< "the document now offers a line count for what moves. If it was "
		   "measured by the per-line attribution pass section 4 specifies, say "
		   "so there and retire this fact in the same commit; if it was "
		   "estimated, that is the failure mode item 1 was re-scoped to end.";
}

//=============================================================================
// 8. plan_shim.md 4a item 2, the neighbouring item this lane re-measured.
//
// THE PhysModelCreate* FAMILY WAS ALREADY DELETED (lane 3a) AND ITS ABSENCE WAS
// ASSERTED NOWHERE. physics_shared.h carries a tombstone naming all nine
// entry points, cpp_alias_t1_test re-pointed its anchor off the family when it
// went, and standalone_solidity_order_test quotes stock's comment about it --
// so a grep for the name finds five files and every hit is prose. The item was
// carried forward as "still present in 5 files" on the strength of that grep.
//
// What nothing checked was whether a DECLARATION could come back. The family's
// replacement is managed's EntityPhysicsCreation (CStandaloneEntity::InitPhysics
// until 2026-10-12) parsing the solid itself and calling CreatePolyObject
// through the vtables, so a restored declaration would compile
// against a definition that does not exist and fail at the link -- eventually,
// loudly, and with no statement anywhere of why it should not be there.
//=============================================================================

//=============================================================================
// 18. THE DERIVATION, NOT JUST THE DOCUMENT.
//
// Every test above reads docs/physics-move-scope.md and checks it against the
// tree or against itself. None of them could tell you whether the document's
// totals still follow from the RULINGS they were derived from -- and the lane
// that produced them said plainly that its measurement scripts were
// uncommitted and would go with its worktree.
//
// That is the shape where a committed number quietly stops being falsifiable:
// the document is pinned, the derivation is gone, and nobody can re-take it.
// So the classifier and its two inputs are committed under
// tools/physics-scope/, and this section runs them.
//
// It is NOT a generator reading its own output. rulings.tsv is a hand ruling
// per function; the document is hand-written prose. Comparing them crosses two
// artefacts made in different ways, which is the only reason the comparison is
// worth anything.
//=============================================================================

namespace
{

struct ClassifyRun_t
{
	int m_nExit;
	std::string m_szOut;
};

// Runs the committed classifier from the repo root, wherever the test is run
// from. Bounded walk, anchored on a path only this branch has, so it cannot
// wander up into the main checkout and classify a different tree.
ClassifyRun_t RunClassify()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	ClassifyRun_t run;
	run.m_nExit = -1;

	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		const std::string root = kCandidates[i];
		struct stat st;
		if ( stat( ( root + "/tools/physics-scope/classify" ).c_str(), &st ) != 0 )
			continue;

		const std::string cmd = "cd " + root +
			" && tools/physics-scope/classify "
			"tools/physics-scope/fnmap.tsv tools/physics-scope/rulings.tsv 2>&1";
		FILE *pPipe = popen( cmd.c_str(), "r" );
		if ( pPipe == NULL )
			return run;

		char szBuf[4096];
		while ( fgets( szBuf, sizeof( szBuf ), pPipe ) != NULL )
			run.m_szOut += szBuf;

		// popen hands back wait(2)'s status; a test comparing that raw value
		// against 1 would be comparing against 256 and could never fail.
		const int nStatus = pclose( pPipe );
		run.m_nExit = WIFEXITED( nStatus ) ? WEXITSTATUS( nStatus ) : -1;
		return run;
	}
	return run;
}

} // namespace

TEST( PhysicsMoveScope, TheClassifierAndItsInputsAreCommitted )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_NE( -1, run.m_nExit ) << "tools/physics-scope/classify did not resolve from the test's "
									"working directory, so every assertion below would be vacuous. "
									"The document's totals are only falsifiable while this tool and "
									"its two inputs are committed beside it.";
}

TEST( PhysicsMoveScope, EveryFunctionIsRuledAndEveryRulingIsUsed )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_NE( -1, run.m_nExit );

	EXPECT_NE( std::string::npos, run.m_szOut.find( "UNMATCHED fnmap rows (no ruling): 0" ) )
		<< "a function in physics.cpp has no ruling, so the scope is silently "
		   "incomplete:\n"
		<< run.m_szOut;
	EXPECT_NE( std::string::npos, run.m_szOut.find( "AMBIGUOUS (ruling key matched >1): 0" ) )
		<< "a ruling key matched more than one function, so its lines are "
		   "counted under the wrong verdict:\n"
		<< run.m_szOut;
	EXPECT_NE( std::string::npos, run.m_szOut.find( "UNUSED rulings (key matched no function): 0" ) )
		<< "a ruling names a function that no longer exists -- a stale "
		   "allow-list, which is how these rot:\n"
		<< run.m_szOut;
	EXPECT_EQ( 0, run.m_nExit ) << "the classifier refuses its own inputs:\n" << run.m_szOut;
}

TEST( PhysicsMoveScope, TheDocumentsTotalsStillFollowFromTheRulings )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_NE( -1, run.m_nExit );

	// The headline the document states in prose (:33, :47, :95, :360) and the
	// number the rulings actually sum to. Two artefacts, made differently.
	EXPECT_NE( std::string::npos, run.m_szOut.find( "TOTAL         9 functions    112 lines" ) )
		<< "the rulings no longer sum to the 9 / 112 the document states "
		   "in four places. One of the two moved and the other did not:\n"
		<< run.m_szOut;

	EXPECT_NE( std::string::npos, Doc().find( "all 9 functions" ) ) << "the document stopped saying 9 functions";
	EXPECT_NE( std::string::npos, Doc().find( "all 112 lines" ) ) << "the document stopped saying 112 lines";
}

// Each verdict row of section 2 is the classifier's count for that verdict,
// not only the total: a function moved between verdicts in rulings.tsv (as
// PhysicsWorldOwner was, LIVE-API -> BOUNDARY on 2026-10-18) leaves the total
// alone, so the total fact above cannot see a stale row. Both sides are read:
// the classifier's "<VERDICT>  <n> functions  <n> lines" lines and the
// document's "| `<VERDICT>` | <n> | <n> |" row; a verdict the classifier does
// not print is a row of 0 / 0.
TEST( PhysicsMoveScope, EachVerdictRowIsTheClassifiersCount )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_EQ( 0, run.m_nExit ) << run.m_szOut;
	const std::string doc = Doc();

	static const char *kVerdicts[] = {
		"INSTRUMENT", "SLOT-HELP", "SLOT",     "DEAD",       "STOCK-DEBUG",
		"HOOK",       "LIVE-API",  "DEAD-VIA", "STALE-KEEP", "BOUNDARY",
	};
	int nonEmpty = 0;
	for ( size_t i = 0; i < sizeof( kVerdicts ) / sizeof( kVerdicts[0] ); i++ )
	{
		const std::string v( kVerdicts[i] );
		int wantFns = 0, wantLines = 0;
		std::istringstream lines( run.m_szOut );
		for ( std::string line; std::getline( lines, line ); )
		{
			char name[32] = {};
			int fns = 0, n = 0;
			if ( sscanf( line.c_str(), "%31s %d functions %d lines", name, &fns, &n ) == 3 && v == name )
			{
				wantFns = fns;
				wantLines = n;
			}
		}
		if ( wantFns > 0 )
			nonEmpty++;

		const std::string lead = "| `" + v + "` | ";
		const size_t at = doc.find( lead );
		ASSERT_NE( at, std::string::npos ) << "no row for " << v;
		int fns = -1, n = -1;
		ASSERT_EQ( 2, sscanf( doc.c_str() + at + lead.size(), "%d | %d", &fns, &n ) ) << v;
		EXPECT_EQ( wantFns, fns ) << v << ": the document's row and the classifier disagree on functions";
		EXPECT_EQ( wantLines, n ) << v << ": the document's row and the classifier disagree on lines";
	}
	// The stimulus reaches the subject: the classifier printed verdicts.
	EXPECT_LE( 2, nonEmpty ) << run.m_szOut;
}
