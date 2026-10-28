//========= SourceSharp — the three gates, and the ways a gate can lie ========//
//
// plan_entity.md Phase 0b builds `tools/entity-gates`: the three checks in the
// plan's "Definition of done" — no base entity, no entity list, no entity casts
// — run against two built DLLs, answering PASS or FAIL rather than reporting
// movement.
//
// A GATE HAS A FAILURE MODE THE CENSUS DOES NOT. The census (0a) reports
// numbers, and any number is a legitimate answer. This tool reports a VERDICT,
// and one of its two verdicts — PASS — is the one that ends the port.
//
// THE TREE HAS CROSSED. For most of this port every gate FAILED by
// construction — the definition of done was the end state, not that tree — and
// this file's job was to keep the never-executed PASS path honest with a
// fixture. The close inverts it: `tools/entity-gates` now exits 0 and every
// gated row reads 0/0 on the real binaries, so it is the FAILURE path that no
// longer runs on this tree. The fixtures therefore change job rather than
// retire: they are now the only thing exercising "a gate fails", "the first
// failing gate is named", and "--only skips rather than passes the others".
// Both halves stay executed, which is the whole point of having them.
//
// This file therefore holds four kinds of fact:
//
//   (a) TODAY'S TREE PASSES, AND PASSES ON EVERY ROW — not on the summary
//       line alone. plan_entity.md §1 and Appendix A's numbers survive as the
//       `[reconcile]` rows, which are printed and not gated, so the §1 numbers
//       stay reproducible from this tool after the gated rows hit zero.
//
//   (b) BOTH VERDICTS ARE REACHABLE, and the fixtures are what keeps that
//       true. The all-zero fixture prints PASS and exits 0; the one-derivation,
//       one-gEntList and one-cast fixtures exit 1, name the first failing gate,
//       and mark the untouched gates SKIPPED rather than PASS. Now that the
//       real tree passes, these dirty fixtures are the ONLY readers of the
//       failure path — the same argument that built them when the real tree
//       was the only reader of the pass path, pointing the other way.
//
//   (c) THE ALLOWLIST MECHANISM WORKS BOTH WAYS. §5 says the slot-table
//       resolver's one static_cast is allowlisted by file and line. The
//       mechanism is exercised against a fixture with two cast sites, where the
//       test that matters is the one where ONE is allowlisted and the other is
//       still counted -- an allowlist that excused everything and an allowlist
//       that excused nothing would both pass a one-sided test. Against the REAL
//       tree the allowlist now carries TWO entries, one resolver per side (1c's
//       server arm, 5a's client arm), and each is pinned by path so a hole
//       cannot move to a file no serial-reuse suite covers.
//
//   (d) THE TOOL REFUSES WHAT IT CANNOT MEASURE. A missing DLL, a missing
//       object table, an empty pattern file, a file with no symbol table: each
//       must exit 2, because each would otherwise produce all-zero counts,
//       which is indistinguishable from a finished port.
//
// Everything here shells out to the real tool, for the reason entity_census_test
// gives: a C++ reimplementation would be a second thing to keep in step, and
// the bug it could not catch is precisely the one where the two drift.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the worktree root. buildcpptests runs each binary with src/ as the
// working directory, and that src/ is the compat farm's (farm/<config>/src,
// mounted at its host path in the sniper container), so ".." is the farm --
// the old single-repo root, tools/ link included. The list also lets the binary run by hand from
// the root itself, and keeps "no tree found" distinguishable from "tree found,
// tool missing".
//-----------------------------------------------------------------------------
const char *WorktreeRoot()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string szProbe = std::string( kCandidates[ i ] ) + "/tools/entity-gates";
		struct stat st;
		if ( stat( szProbe.c_str(), &st ) == 0 && S_ISREG( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string RootPath( const char *pszRelative )
{
	const char *pszRoot = WorktreeRoot();
	if ( !pszRoot )
		return std::string();
	return std::string( pszRoot ) + "/" + pszRelative;
}

//-----------------------------------------------------------------------------
// Running a command from the worktree root. popen reports the wait(2) status,
// so the exit code has to be pulled back out of it; a test that compared the
// raw status against 1 would be comparing against 256 and never fail.
//-----------------------------------------------------------------------------
struct Run_t
{
	int m_nExit;
	std::string m_szOutput;
};

Run_t RunAtRoot( const std::string &szCommand )
{
	Run_t run;
	run.m_nExit = -1;

	const char *pszRoot = WorktreeRoot();
	if ( !pszRoot )
		return run;

	const std::string szFull = std::string( "cd '" ) + pszRoot + "' && " + szCommand + " 2>&1";
	FILE *pPipe = popen( szFull.c_str(), "r" );
	if ( !pPipe )
		return run;

	char rgchBuf[ 4096 ];
	while ( fgets( rgchBuf, sizeof( rgchBuf ), pPipe ) )
		run.m_szOutput += rgchBuf;

	const int nStatus = pclose( pPipe );
	run.m_nExit = WIFEXITED( nStatus ) ? WEXITSTATUS( nStatus ) : -1;
	return run;
}

const char *const kToolRelative      = "tools/entity-gates";
//-----------------------------------------------------------------------------
// A numbered row out of the COMMITTED census, section 1.
//
// The rows there are "<label padded out> <server> <client>", and the label may
// wrap onto a continuation line that carries no numbers -- the entity-list row
// does exactly that. So the match is on the line that STARTS with the label
// prefix, and the two numbers are read off that line only; a continuation is
// never mistaken for a row of its own because it has nothing to read.
//
// false rather than a zero on a miss. A missing row and a row reading zero are
// opposite findings: the first is this test looking at the wrong file, and the
// second is the port being finished.
//-----------------------------------------------------------------------------
// The exit codes the tool promises. 1 is "a gate failed", 2 is "I could not
// take the measurement". A test that accepted "non-zero" for both would pass
// on the run where a measurement turned into a crash.
const int kExitAllPass = 0;
const int kExitRefused = 2;

//-----------------------------------------------------------------------------
// One run of the tool against today's real, built DLLs, shared by every fact
// that reads it. It takes about a second — nm over an 85MB client.so — and
// twelve facts each paying for their own would make this leg the slow one for
// no extra coverage.
//-----------------------------------------------------------------------------
const Run_t &RealTreeTsv()
{
	static Run_t run = RunAtRoot( std::string( kToolRelative ) + " --tsv" );
	return run;
}

const Run_t &RealTreeHuman()
{
	static Run_t run = RunAtRoot( kToolRelative );
	return run;
}

//-----------------------------------------------------------------------------
// The same tree, gate 3 only, with the allowlist TAKEN AWAY.
//
// Step 1c put the slot-table resolver's static_cast on the allowlist, which is
// what §5's gate-3 row says it must be -- and an allowlisted site is subtracted
// from the gate's count while the census, which has no allowlist at all, still
// counts it. So the two measurements stopped agreeing, and the fact below that
// checks them against each other has to compare like with like.
//
// /dev/null is not a regular file, so the tool's `[ -f ]` test treats it as no
// allowlist and every site is counted. That is the census's own reading, which
// is exactly what the comparison wants.
//-----------------------------------------------------------------------------
// A --tsv row, looked up by gate and metric label. Returns false when the row
// is absent, which is itself worth failing on: a metric that stopped being
// printed is a gate that stopped being checked.
bool TsvRow( const std::string &szOutput, const char *pszGate, const char *pszMetric,
             long *pnServer, long *pnClient, std::string *pszVerdict )
{
	const std::string szKey = std::string( pszGate ) + "\t" + pszMetric + "\t";
	size_t iPos = 0;
	while ( iPos < szOutput.size() )
	{
		size_t iEnd = szOutput.find( '\n', iPos );
		if ( iEnd == std::string::npos )
			iEnd = szOutput.size();
		const std::string szLine = szOutput.substr( iPos, iEnd - iPos );
		iPos = iEnd + 1;

		// gate<TAB>role<TAB>metric<TAB>srv<TAB>cli<TAB>verdict
		size_t t1 = szLine.find( '\t' );
		if ( t1 == std::string::npos )
			continue;
		size_t t2 = szLine.find( '\t', t1 + 1 );
		if ( t2 == std::string::npos )
			continue;
		const std::string szHead = szLine.substr( 0, t1 ) + "\t" + szLine.substr( t2 + 1 );
		if ( szHead.compare( 0, szKey.size(), szKey ) != 0 )
			continue;

		std::vector< std::string > fields;
		size_t iField = 0;
		while ( iField <= szLine.size() )
		{
			size_t iTab = szLine.find( '\t', iField );
			if ( iTab == std::string::npos )
			{
				fields.push_back( szLine.substr( iField ) );
				break;
			}
			fields.push_back( szLine.substr( iField, iTab - iField ) );
			iField = iTab + 1;
		}
		if ( fields.size() < 6 )
			continue;

		if ( pnServer )   *pnServer = strtol( fields[ 3 ].c_str(), NULL, 10 );
		if ( pnClient )   *pnClient = strtol( fields[ 4 ].c_str(), NULL, 10 );
		if ( pszVerdict ) *pszVerdict = fields[ 5 ];
		return true;
	}
	return false;
}

//=============================================================================
// The fixture: a tiny source tree whose every gated count is zero, plus a
// second object table over one file with two cast sites in it.
//
// It exists so the PASS path is executed. Everything in it is written at test
// time rather than checked in, so it cannot drift out of step with the tool's
// argument list, and it is torn down with the process.
//
// The two DLLs the fixture gates are THIS TEST BINARY. It is an ELF with a full
// symbol table (buildcpptests builds at -g and never strips), it defines no
// CBaseEntity and no entity list, and using it means the fixture needs no
// compiler at test time. `/proc/self/exe` is resolved HERE and passed as a real
// path: handed to the tool as a literal it would name bash, not this process.
//=============================================================================
struct Fixture_t
{
	std::string m_szRoot;        // holds src/ and the allowlists
	// Each object table is a CMake build dir (<case>/build) whose
	// compile_commands.json names the TUs, beside <case>/src (a link to the
	// shared <root>/src) and <case>/repo, the stand-in repo whose CMakeLists list
	// the same sources. See WriteObjectTable.
	std::string m_szCleanNinja;  // object table over two entity-free TUs
	std::string m_szDirtyNinja;  // ... over one TU with casts on lines 4 and 5
	std::string m_szDerivedNinja;// ... over one TU with ONE entity derivation
	std::string m_szChainNinja;  // ... over one TU with a two-step derivation
	std::string m_szKeptNinja;   // ... over a TU naming IClientEntityList only
	std::string m_szEntListNinja;// ... over a TU naming gEntList
	std::string m_szSelfExe;     // this binary, standing in for both DLLs
	bool m_bOk;
};

// Registered with atexit the first time the fixture is built. It reads the
// static below rather than taking an argument, which is what atexit allows.
std::string g_szFixtureRoot;
//=============================================================================
// (0) The scanner is itself checked: the subjects exist and are not stubs.
//=============================================================================

TEST( EntityGates, TheWorktreeRootIsFound )
{
	ASSERT_TRUE( WorktreeRoot() != NULL )
		<< "no tools/entity-gates above the working directory; every fact below "
		   "would be vacuous.";
}

TEST( EntityGates, TheToolIsExecutable )
{
	ASSERT_TRUE( WorktreeRoot() != NULL );
	struct stat st;
	ASSERT_EQ( 0, stat( RootPath( kToolRelative ).c_str(), &st ) );
	EXPECT_TRUE( ( st.st_mode & S_IXUSR ) != 0 )
		<< kToolRelative << " is not executable; make check would run nothing.";
}

//-----------------------------------------------------------------------------
// TWO ENTRIES, ONE PER SIDE, AND BOTH ARE THE SLOT-TABLE RESOLVER.
//
// This fact has moved twice, and each move is recorded here because each was a
// step landing rather than a rule being relaxed.
//
//   It required ZERO first, and said why: "the slot-table resolver arrives in
//   step 1c; until then the only correct number of entries is zero."
//
//   1c made it ONE -- the server's resolver.
//
//   5a makes it TWO, and the second is the CLIENT's. plan_entity.md §3.16's
//   "the only permitted downcast on an entity path is the one line inside the
//   slot-table resolver" is one line per resolver, and §3.3 puts one table on
//   each side ("one array per side, owned by the side's game-system object")
//   because the shell type is per side. There is no arrangement in which one
//   static_cast serves both: the two sides' shells are unrelated types and the
//   shared header cannot name either.
//
// PINNED AT TWO AND NAMED PER SIDE, not relaxed to "some". The whole argument
// for allowing either -- that a serial-reuse suite covers that exact site -- is
// an argument about THAT function in THAT file, so a count that did not also
// check the paths would let the hole move somewhere nothing covers. A THIRD
// entry is the port ending with a downcast nobody argued for and a green gate,
// which is what this fact exists to stop.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE ALLOWLIST ACTUALLY SUBTRACTS, and this is what makes the entries above
// measured holes rather than lines in a file nobody reads.
//
// The same tree, gate 3, twice: once with the allowlist and once without it.
// Both entries name a static_cast site, one per side, so BOTH columns of the
// static_cast row must fall by exactly the number of entries on that side. If a
// column did not move, the entry for that side would be inert -- and the day
// somebody moves a resolver's line number, that is exactly what it becomes.
//
// PER COLUMN AND NOT ON THE TOTAL, deliberately. Before 5a the client column was
// the control: the only entry was a server one, so the client's count had to
// stay still. Now that both sides have an entry there is no still column left,
// and a test on the sum would pass if the two entries excused two SERVER sites.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// And no entry is stale. The tool fails on an entry matching no cast site, and
// that is the failure mode a merge above the resolver produces.
//-----------------------------------------------------------------------------
TEST( EntityGates, NoAllowlistEntryIsStale )
{
	long nStale = -1;
	ASSERT_TRUE( TsvRow( RealTreeTsv().m_szOutput, "3",
	                     "stale allowlist entries (match no cast site)",
	                     &nStale, NULL, NULL ) )
		<< RealTreeTsv().m_szOutput;
	EXPECT_EQ( 0, nStale )
		<< "an allowlisted line that matches no cast site is no longer excusing the "
		   "cast it was written for -- it is excusing whatever now sits on that line.";
}

//=============================================================================
// (a) TODAY'S TREE PASSES, AND PASSES ON EVERY ROW.
//
// THIS BLOCK WAS THE OPPOSITE ASSERTION UNTIL THE CLOSE. It read
// `TodaysBinariesFailTheDefinitionOfDone` and demanded exit 1, because for the
// whole port the definition of done was the END state and a green run could
// only mean the gate had stopped measuring. ROUTE_TO_DONE.md §5 is what turns
// it round -- "the three gates read zero ... entity_gates_test's literals
// become == 0" -- and the facts are TURNED ROUND, not deleted: the exit code is
// still pinned to one exact value, and the rows are still read one at a time so
// that a summary line printed over unmeasured gates cannot pass.
//
// The guard against "the gate stopped measuring" does not disappear with the
// literals; it moves. It now lives in (d)'s refusal facts (an unmeasurable
// input exits 2, never 0) and in the dirty fixtures of (b), which are the only
// readers of the failure path left on a tree where the real binaries pass.
//=============================================================================

TEST( EntityGates, TodaysBinariesPassTheDefinitionOfDone )
{
	const Run_t &run = RealTreeHuman();
	ASSERT_NE( -1, run.m_nExit ) << "the tool did not run at all.";
	EXPECT_EQ( kExitAllPass, run.m_nExit )
		<< "plan_entity.md's definition of done is met on these binaries and the "
		   "tool must say so with exit 0. A non-zero exit here is the port coming "
		   "back apart.\n" << run.m_szOutput;
	EXPECT_NE( std::string::npos,
	           run.m_szOutput.find( "all gates PASS -- no base entity, no entity list, "
	                                "no entity casts" ) ) << run.m_szOutput;
}

TEST( EntityGates, EveryGatedRowOnTodaysBinariesReadsZeroOnBothSides )
{
	// The summary is three words over fourteen measurements. This walks the
	// --tsv rows themselves and requires 0/0 and PASS on every GATED one, so a
	// verdict computed over a row that stopped being printed cannot slip
	// through. The [reconcile] rows are skipped by role, not by name: §1's
	// looser readings are printed and NOT gated, and ToEngineIface (the other
	// direction) is a live non-zero number the port never drove to zero.
	const std::string &szTsv = RealTreeTsv().m_szOutput;
	ASSERT_FALSE( szTsv.empty() ) << "the tool produced no --tsv output.";

	int cGatedRows = 0;
	size_t iPos = 0;
	while ( iPos < szTsv.size() )
	{
		size_t iEnd = szTsv.find( '\n', iPos );
		if ( iEnd == std::string::npos )
			iEnd = szTsv.size();
		const std::string szLine = szTsv.substr( iPos, iEnd - iPos );
		iPos = iEnd + 1;

		std::vector< std::string > fields;
		size_t iField = 0;
		while ( iField <= szLine.size() )
		{
			const size_t iTab = szLine.find( '\t', iField );
			if ( iTab == std::string::npos )
			{
				fields.push_back( szLine.substr( iField ) );
				break;
			}
			fields.push_back( szLine.substr( iField, iTab - iField ) );
			iField = iTab + 1;
		}
		if ( fields.size() < 6 || fields[ 1 ] != "gate" )
			continue;

		cGatedRows++;
		EXPECT_EQ( "0", fields[ 3 ] )
			<< "gate " << fields[ 0 ] << " row '" << fields[ 2 ] << "' reads "
			<< fields[ 3 ] << " on the server.";
		EXPECT_EQ( "0", fields[ 4 ] )
			<< "gate " << fields[ 0 ] << " row '" << fields[ 2 ] << "' reads "
			<< fields[ 4 ] << " on the client.";
		EXPECT_EQ( "PASS", fields[ 5 ] )
			<< "gate " << fields[ 0 ] << " row '" << fields[ 2 ] << "' is not PASS.";
	}

	EXPECT_GE( cGatedRows, 20 )
		<< "only " << cGatedRows << " gated rows were printed. A run that stopped "
		   "printing rows would satisfy every assertion in the loop above by "
		   "never entering it.\n" << szTsv;
}

TEST( EntityGates, NoGateFailsSoNoFirstFailingGateIsNamed )
{
	// READ THE ASSERTIONS, NOT THE HISTORY. This fact's other half --
	// TheFirstFailingGateIsNamed, below -- is what still checks the naming
	// machinery; it moved to the fixtures, where a gate still fails. Here the
	// point is the absence AND the live successor: the human run must NOT carry
	// a "FIRST FAILURE" line, and the --tsv verdict row must positively read
	// `none`. Asserting only the absence would also pass over an empty output.
	const Run_t &run = RealTreeHuman();
	EXPECT_EQ( std::string::npos, run.m_szOutput.find( "FIRST FAILURE" ) )
		<< "a gate failed on today's binaries.\n" << run.m_szOutput;

	std::string szVerdict;
	ASSERT_TRUE( TsvRow( RealTreeTsv().m_szOutput, "all", "first failure",
	                     NULL, NULL, &szVerdict ) )
		<< "the tool stopped printing the first-failure verdict row.\n"
		<< RealTreeTsv().m_szOutput;
	EXPECT_EQ( "none", szVerdict );
}

TEST( EntityGates, GateOneCountsEntityMembersWithTheSymbolTypeFilter )
{
	// THE STRICT READING IS THE GATE. plan_entity.md §1's table says 1,207/602
	// and its own Appendix A says 1,185/584; both reproduce on these binaries,
	// and they differ only in whether `awk '$2 ~ /^[TWVDBRt]$/'` is applied.
	// Weak inline emissions read as definitions without it, so the untyped count
	// is the one that can be inflated by something nobody wrote.
	long nSrv = -1, nCli = -1;
	std::string szVerdict;
	ASSERT_TRUE( TsvRow( RealTreeTsv().m_szOutput, "1",
	                     "CBaseEntity:: / C_BaseEntity:: members", &nSrv, &nCli, &szVerdict ) )
		<< RealTreeTsv().m_szOutput;
	// 1185 -> 1181 BY plan_entity.md step 4a, and the four are named because a
	// gate number that moves without an account is the thing this file exists
	// to stop. game/server/gib.cpp left the server build, and it was the ONLY
	// definer of two CBaseEntity members -- IsInWorld (CGib::WaitTillLand's)
	// and TouchSet (CGib::Spawn's SetTouch) -- plus two EntityFuncSlot template
	// instantiations that carry "CBaseEntity::" inside their own names:
	//
	//   void (CBaseEntity::*EntityFuncSlot<CBaseEntity,
	//         void (CBaseEntity::*)(inputdata_t&), CGib, ...>(...))(inputdata_t&)
	//
	// Every other CBaseEntity:: symbol gib.o emitted is weak and emitted by
	// other TUs as well, which is why 768 lines of C++ leaving costs the count
	// four and not four hundred.
	//
	// THE CLIENT NUMBER DID NOT MOVE AT 4a, and that was the control: step 4a
	// touched the server's project script only.
	//
	// 584 -> 578 BY plan_entity.md step 7g, and the six are accounted for in two
	// groups, each MEASURED on a build of its own rather than reasoned about:
	//
	//   584  the baseline (git show b18d8d96a:docs/entity-census.txt)
	//   582  the twelve not-shipped TUs out of client_csharp.vpc, with
	//        entity_client_tools.cpp still stock
	//   578  and with entity_client_tools.cpp rewritten as a refusal stub
	//
	// FOUR OF THE SIX ARE THE STUB, and they are exactly the tool bridge's
	// bookkeeping on the entity -- nothing else in the client calls them:
	//
	//   C_BaseEntity::SetToolHandle(unsigned int)
	//   C_BaseEntity::EnableInToolView(bool)
	//   C_BaseEntity::SetToolRecording(bool)
	//   C_BaseEntity::ShouldRecordInTools() const
	//
	// THE OTHER TWO are the twelve TUs and the HLTV/achievement call sites 7g
	// removed from four stock TUs, and only one is namable from the object
	// tables: hltvcamera.o was the sole emitter of
	// C_BaseEntity::Instance(CBaseHandle), which is now defined nowhere in
	// client.so (`nm -C --defined-only ... | grep -c` -> 0). Every other
	// CBaseEntity:: symbol those objects carried is weak and emitted by other
	// TUs too, which is why 3,900 lines of C++ leaving costs the count two --
	// gib.cpp's four out of 768 lines, above, is the same arithmetic.
	//
	// 578 -> 577 AT plan_entity.md STEP 7E, AND THE ONE SYMBOL IS NAMABLE:
	// C_BaseEntity::Instance(int). It is defined INLINE in c_baseentity.h:1295
	// (`{ return ClientEntityList().GetBaseEntity( iEnt ); }`), so it is emitted
	// only into objects that call it, and sound_emitter.cpp's client
	// actor-gender arm was the whole client build's only caller. 7e retyped
	// that arm to ask the entity list directly, and the symbol is now defined
	// NOWHERE in client.so:
	//
	//     nm -C --defined-only client.so | awk '$2 ~ /^[TWVDBRt]$/'
	//         | grep -c 'C_BaseEntity::Instance'      -> 0
	//
	//     (one pipeline; the line break here carries no backslash on purpose,
	//      because a trailing backslash in a // comment is a line splice and
	//      -Wcomment says so)
	//
	// The other Instance overloads are untouched: c_particle_system.cpp and
	// tf_fx_explosions.cpp call Instance(CBaseHandle), which is a different
	// function.
	//
	// 577 -> 576 AT plan_entity.md STEP 8f-remove, AND THE ONE SYMBOL IS
	// NAMABLE: C_BaseEntity::IsFloating(). game/shared/gamemovement.cpp left
	// BOTH vpcs -- 5,492 lines and 95 CGameMovement:: definitions -- and
	// gamemovement.o was the client build's ONLY emitter of that weak inline:
	//
	//     find .../objs/client_sourcesharp -name '*.o' -print0
	//         | xargs -0 -r nm -C --defined-only --print-file-name
	//         | grep 'C_BaseEntity::IsFloating'
	//     -> gamemovement.o:0000000000000000 W C_BaseEntity::IsFloating()
	//
	// Measured by A/B on this tree rather than inferred: the row was put back,
	// the tree rebuilt, the two symbol sets diffed, and `comm -23` named
	// exactly one symbol. It is now defined nowhere in client.so:
	//
	//     nm -C --defined-only client.so | grep -c 'C_BaseEntity::IsFloating'
	//     -> 0
	//
	// The SERVER's 1181 does not move, and that is the same arithmetic seen
	// from the other side: gamemovement.cpp is SHARED, so the server compiled
	// it too, but the server's CBaseEntity:: emissions from it are all emitted
	// by other TUs as well. 5,492 lines of C++ leaving costs the client one
	// symbol and the server none.
	//
	// 576 -> 575 AT CLIENT_FLIP LANE 2 (`c8-entitystatics`), AND THE ONE SYMBOL
	// IS NAMABLE: C_BaseEntity::IsDormantPredictable() const.
	//
	// It is NOT a symbol this lane deleted. It is defined in c_baseentity.cpp
	// (:4925) and this lane did not touch that definition; what went is its
	// last REFERENCE. EntityRender_AddVisibleEntities called it once, to skip a
	// dormant client-created predictable, and that function is RETIRED -- it
	// walked a predictables list only C_BaseEntity ever added to. With the last
	// reference gone the linker dropped the definition.
	//
	// A/B ON THIS TREE, not inferred: the four changed sources were restored
	// from entity-port's tip, the tree rebuilt, both symbol sets captured with
	// the pipeline above, and `comm` run both ways --
	//
	//     ONLY IN BEFORE   1   t C_BaseEntity::IsDormantPredictable() const
	//     ONLY IN AFTER    0
	//
	// -- so the count moved by exactly one symbol and no others moved in either
	// direction, which is what stops "one fell" hiding "one fell and one rose".
	//
	// AND THE OBJECT TABLE AGREES with the linker about which reference it was:
	//
	//     nm -C --print-file-name over the BEFORE object tree
	//     -> c_baseentity.o          T C_BaseEntity::IsDormantPredictable() const
	//        c_baseentity_statics.o  U C_BaseEntity::IsDormantPredictable() const
	//
	// The only other caller in the tree is prediction.cpp:1113, and
	// prediction.cpp is not compiled into the client: it has no row in
	// client_csharp.vpc and no object on the link line (grep -c, both zero).
	//
	// MERGE NOTE, because this is a VALUE and not an append: two lanes moving
	// this literal must AGREE ON THE TREE, not stack their claims. Re-count it
	// with the command above rather than merging both numbers.
	//
	// 1181 -> 1180 by the CLIENT FLIP, lane c8-shared6, AND THE ONE SYMBOL IS
	// NAMED, by the A/B this comment block's own precedent demands rather than
	// by inference:
	//
	//     CBaseEntity::IsCurrentlyTouching() const
	//
	// It is defined in game/shared/physics_main_shared.cpp and its whole body is
	// `return HasDataObjectType( TOUCHLINK );` (:487-495).
	// CCollisionProperty::CheckForUntouch was its last surviving caller, and that
	// site now asks the data-object table directly --
	// `EntityDataObject_Has( TOUCHLINK, m_pOuter )` -- because the table moved to
	// physics_shared.cpp (which STAYS) and takes the entity as an opaque key.
	// With no caller left, --gc-sections drops the definition.
	//
	// MEASURED BY A/B ON THIS TREE, both arms built and both counted:
	//
	//     call put back    -> 1181, and `nm -C --defined-only server.so |
	//                         grep -c 'CBaseEntity::IsCurrentlyTouching'` = 1
	//     call as it is    -> 1180, and the same grep = 0
	//
	// The CLIENT's column does not move FOR THAT SYMBOL, and that is the same
	// arithmetic from the other side: collisionproperty.cpp is SHARED so the
	// client compiled the change too, but CheckForUntouch's body is inside
	// `#ifndef CLIENT_DLL`, so there was never a client caller for the client
	// to lose.
	//
	// 576 -> 575 IS A SECOND, INDEPENDENT SYMBOL, from lane c8-entitystatics,
	// and it is named to the same standard:
	//
	//     C_BaseEntity::IsDormantPredictable() const
	//
	// THAT LANE DID NOT DELETE IT. The definition at c_baseentity.cpp:4925 is
	// untouched; what went is its last REFERENCE.
	// EntityRender_AddVisibleEntities called it once, to skip a dormant
	// client-created predictable, and that function RETIRES -- it walked a
	// predictables list only C_BaseEntity ever added to, so after the flip the
	// loop runs over nothing. With the last reference gone, --gc-sections drops
	// the definition. The object table agrees with the linker about which
	// reference it was: c_baseentity.o carries it `T`, c_baseentity_statics.o
	// `U`. The only other caller, prediction.cpp:1113, is not compiled into the
	// client -- no vpc row, no object on the link line, both grepped, both zero.
	//
	// Its A/B was run BOTH WAYS, because "one fell" does not exclude "one fell
	// and one rose": ONLY IN BEFORE = 1 (that symbol), ONLY IN AFTER = 0.
	// 1180 -> 1179 AND 575 -> 574 BY LANE f-physics, and for once it is ONE
	// symbol moving BOTH columns, named to the same standard as the two above:
	//
	//     CBaseEntity::PhysicsMarkEntitiesAsTouchingEventDriven( CBaseEntity*, CGameTrace& )
	//     C_BaseEntity::PhysicsMarkEntitiesAsTouchingEventDriven( C_BaseEntity*, CGameTrace& )
	//
	// THAT LANE DID NOT DELETE IT either. The definition in
	// game/shared/physics_main_shared.cpp is untouched; what went is its last
	// REFERENCE, and on BOTH sides it was the same one --
	// CCollisionEvent::DispatchStartTouch in game/{client,server}/physics.cpp,
	// now a declared, printed §7 refusal of the event-driven CONTACT touchlink.
	// nm over every object of both projects named that symbol in physics.o and
	// nowhere else, so removing the call removed the demand, and --gc-sections
	// dropped the definition.
	//
	// MEASURED BY A/B, BOTH ARMS BUILT AND BOTH COUNTED, and run BOTH WAYS
	// because "one fell" does not exclude "one fell and one rose":
	//
	//     ONLY IN BEFORE   server 1, client 1   (that symbol, each side)
	//     ONLY IN AFTER    server 0, client 0
	//
	// A NOTE ON THE PIPELINE, because it cost a wrong reading first: the
	// symbol-type filter matches lowercase `t` on these binaries, not `T`.
	// Both DLLs are built `-fvisibility=hidden`, so every one of these is
	// LOCAL, and a filter written as `$2=="T"` reads ZERO for both columns --
	// which looks like "nothing to count" rather than like a broken filter.
	// AND THEN THE FLIP TOOK BOTH COLUMNS TO ZERO, WHICH IS WHAT THIS RATCHET
	// WAS COUNTING DOWN TO ALL ALONG. game\server\baseentity.cpp and
	// game\client\c_baseentity.cpp left their vpcs with the rest of the
	// hierarchy, so there is no CBaseEntity:: or C_BaseEntity:: member left in
	// either DLL to count: 1179 -> 0 and 574 -> 0 in one commit, and the verdict
	// with them. This row is PASS.
	//
	// THE WHOLE HISTORY ABOVE IS KEPT, DELIBERATELY. Every number in it was
	// argued for by the lane that moved it, and an argued fall is what makes a
	// later RISE readable. A zero with no history is a row nobody can audit.
	//
	// AND ZERO IS THE ONE VALUE AN INSTRUMENT PRODUCES WHEN IT BREAKS. nm
	// against a file it cannot open, an empty root list, a symbol-type filter
	// written `$2=="T"` on a -fvisibility=hidden binary: all three answer 0, and
	// all three have happened on this project. So this fact no longer stands on
	// its own -- TheBinariesStillCarryATypeinfoTableToWalk below is its control,
	// and it is what makes these zeros evidence rather than silence.
	EXPECT_EQ( 0, nSrv );
	EXPECT_EQ( 0, nCli );
	EXPECT_EQ( "PASS", szVerdict );
}

TEST( EntityGates, GateOneCarriesTheLooseReadingAsReconciliationOnly )
{
	// §1's 1,207/602, measured rather than recited, and marked "reconcile" so
	// nobody reports progress against it or re-opens which number is right.
	const std::string &szOut = RealTreeTsv().m_szOutput;
	long nSrv = -1, nCli = -1;
	std::string szVerdict;
	ASSERT_TRUE( TsvRow( szOut, "1", "same, no symbol-type filter (\xC2\xA7""1's reading: 1207/602)",
	                     &nSrv, &nCli, &szVerdict ) ) << szOut;
	// 1207 -> 1203, the same four, seen through the loose reading. The two
	// counts moving by the SAME amount is what says the four are real
	// definitions rather than weak emissions the filter was hiding.
	//
	// AND 602 -> 596 at step 7g, which is the same test applied to the client:
	// the filtered reading above moved by six and this one moves by six, so all
	// six are real definitions rather than weak emissions.
	//
	// AND 596 -> 595 at step 7e, which is the same test again: the filtered
	// reading above moved by one and this one moves by one, so
	// C_BaseEntity::Instance(int) was a real definition rather than a weak
	// emission the filter was hiding.
	//
	// AND 594 -> 593 at CLIENT_FLIP lane 2, once more, and the agreement is the
	// point rather than the number: the filtered reading above moved by one and
	// this one moves by one, so C_BaseEntity::IsDormantPredictable() was a real
	// definition and not a weak emission the filter was hiding. Which is what
	// the object table already said -- c_baseentity.o carried it as `T`.
	//
	// AND 595 -> 594 at step 8f-remove, once more: the filtered reading moved
	// by one and this one moves by one, so C_BaseEntity::IsFloating() was a
	// real definition too. Note what the agreement is FOR -- IsFloating is a
	// weak inline, and the whole point of the symbol-type filter is that weak
	// emissions read as definitions when several TUs carry a copy. Only one TU
	// carried this one, so removing that TU removes the definition, and both
	// readings agreeing is what says so.
	//
	// AND 1203 -> 1202 by the CLIENT FLIP, lane c8-shared6, once more: the
	// filtered reading above moved by ONE and this one moves by ONE, so
	// `CBaseEntity::IsCurrentlyTouching() const` was a real definition rather
	// than a weak emission the filter was hiding. That agreement is the whole
	// point of carrying both readings, and it is what makes the A/B recorded
	// above a measurement of the symbol rather than of the filter.
	//
	// AND 594 -> 593 on the client, by the same argument for the other symbol:
	// the filtered reading moved by one and this one moves by one, so
	// `C_BaseEntity::IsDormantPredictable() const` was a real definition too --
	// which the object table had already said, carrying it as `T`.
	// AND 1202 -> 1201 AND 593 -> 592 by lane f-physics, once more, on both
	// columns at once: the filtered reading above moved by ONE on each side and
	// this one moves by ONE on each side, so
	// `PhysicsMarkEntitiesAsTouchingEventDriven` was a real definition on both
	// sides rather than a weak emission the filter was hiding. This is the
	// first time the agreement has been available on BOTH columns for a SINGLE
	// symbol -- it is one member of a shared file, compiled into each side, and
	// its last caller on each side was that side's physics.cpp.
	// AND THE FLIP TOOK THIS ONE TO ZERO TOO, BY THE SAME ROWS -- so the
	// agreement this whole comment is about holds one last time and at its
	// strongest: the filtered reading went to 0 and the loose one went to 0, so
	// every member that was left was a real definition in a TU that left, and
	// not a weak emission the filter was hiding.
	EXPECT_EQ( 0, nSrv );
	EXPECT_EQ( 0, nCli );
	EXPECT_EQ( "-", szVerdict ) << "the loose reading must not carry a verdict.";
	EXPECT_NE( std::string::npos, szOut.find( "1\treconcile\t" ) );
}

//-----------------------------------------------------------------------------
// THE WIDE READING WAS PINNED AT 62/120 AND IS NOT ANY MORE, and the reason is
// worth more than the number was.
//
// §1's entity-list row omits CBaseEntityList:: and reads 33/91; Appendix A's
// command includes it and read 62/120 the day this file was written. The
// definition of done's clause 2 names CBaseEntityList explicitly, so the WIDER
// pattern is the gate -- that is the fact, and it was expressed as a literal.
//
// A LITERAL IS THE WRONG SHAPE FOR IT. The wide reading is exactly the number
// this plan drives to zero: step 1d took it to 61 by routing CSoundPatch::Build
// through the slot table, which left
// CBaseEntityList::LookupEntityByNetworkIndex(int) with no caller in the build
// and dropped it out of server.so. Every later step moves it again. A literal
// here makes each of them edit this same line, and turns a fact about WHICH
// PATTERN the gate uses into a running total any lane can quietly re-baseline
// -- the "armed site count is not monotonic" hazard, arriving through the check
// written to prevent drift.
//
// SO IT IS PINNED AGAINST THE COMMITTED CENSUS, the same like-for-like
// arrangement EveryCastFamilyAgreesWithTheCommittedCensusBaseline uses below:
// the two tools read the same binaries with the same pattern and must get the
// same numbers, whatever those numbers are today. A gate that stopped counting
// CBaseEntityList:: would disagree with the census at once -- which is the
// failure this test exists for -- and it is now caught on ANY tree rather than
// only on the one current when the literal was written.
//
// AND THE PATTERN CLAIM IS CHECKED DIRECTLY rather than inferred from a total:
// the wide row must EXCEED the narrow one. That is what "includes
// CBaseEntityList::" MEANS, it cannot be satisfied by a stale literal, and it
// is the one thing a pure agreement check could not see -- two tools that had
// both dropped CBaseEntityList:: would agree with each other perfectly.
//-----------------------------------------------------------------------------
TEST( EntityGates, GateTwoCarriesTheNarrowReadingAsReconciliationOnly )
{
	long nSrv = -1, nCli = -1;
	std::string szVerdict;
	ASSERT_TRUE( TsvRow( RealTreeTsv().m_szOutput, "2",
	                     "same, without CBaseEntityList:: (\xC2\xA7""1's reading: 33/91)",
	                     &nSrv, &nCli, &szVerdict ) );
	// 91 -> 90 at plan_entity.md step 7g. The narrow reading omits
	// CBaseEntityList::, so what moved is a CClientEntityList member, and the
	// only one the twelve deleted objects emitted is
	// CClientEntityList::GetEnt(int) -- still defined once in client.so, so what
	// went was a second weak emission of it rather than the definition. The
	// SERVER number did not move, which is the control: 7g touched the client
	// project only.
	//
	// 90 -> 89 at step p7-exit, and it is the SAME symbol a second time.
	// Eleven stock client TUs stopped asking CClientEntityList and now ask the
	// slot table (shell_cliententitylist.h), and `GetEnt` is a one-line inline
	// in cliententitylist.h -- `{ return GetBaseEntity( entnum ); }` -- so it
	// only ever occupies a symbol at all when some caller forces an
	// out-of-line emission. Taking movehelper_client.cpp, fx.cpp,
	// particles_new.cpp and c_te_legacytempents.cpp off it took the last one.
	// Measured, not inferred: the whole gated set diffed against a build of
	// entity-port's own sources is exactly two lines, both LOST, and the other
	// is CBaseEntityList::GetNetworkableHandle(int) const, whose only compiled
	// caller was movehelper_client.cpp's IsWorldEntity. Hence 118 -> 116 on the
	// wide row and 90 -> 89 here: the wide row counts both, the narrow one
	// omits CBaseEntityList:: and so sees only GetEnt.
	//
	// The SERVER number is again the control at 33: p7-exit touched no file
	// under game/server, game/shared, sourcesharp/game/shared or public.
	// AND THE FLIP TOOK BOTH TO ZERO. 33 -> 0 and 89 -> 0: entitylist.cpp,
	// cliententitylist.cpp and entitylist_base.cpp all left their vpcs in one
	// commit, so the narrow reading has no CGlobalEntityList:: or
	// CClientEntityList:: member left to see either. The history above is kept
	// for the reason gate 1's is.
	EXPECT_EQ( 0, nSrv );
	EXPECT_EQ( 0, nCli );
	EXPECT_EQ( "-", szVerdict );
}

TEST( EntityGates, EveryGateCarriesASourceRowAndNotOnlyAnNmRow )
{
	// plan_entity.md §5: "nm counts ... are a floor, not a proof: --gc-sections
	// drops what nothing reaches, so the source scan over the compiled list is
	// the sufficient check and nm is the fast one." A gate whose only row came
	// out of the binary could be passed by deleting a call, not a class.
	const std::string &szOut = RealTreeTsv().m_szOutput;
	EXPECT_TRUE( TsvRow( szOut, "1", "\": public <entity>\" decls (TUs + shim headers)",
	                     NULL, NULL, NULL ) ) << szOut;
	EXPECT_TRUE( TsvRow( szOut, "2", "gEntList / ClientEntityList() / IEntityListener",
	                     NULL, NULL, NULL ) ) << szOut;
	EXPECT_TRUE( TsvRow( szOut, "3", "TOTAL, families summed (one line can hit two)",
	                     NULL, NULL, NULL ) ) << szOut;
}

TEST( EntityGates, TheDerivedClassClosureFindsSubclassesOnTodaysBinaries )
{
	// gate 1 counts members "of any class whose vtable references theirs". The
	// subclass list is DERIVED from the binary's typeinfo graph rather than
	// hardcoded, so this fact is the one that fails if the graph walk breaks and
	// silently returns nothing — which would read as a gate one step closer to
	// green.
	//
	// AND THE FLIP EMPTIED THE LIST, WHICH IS EXACTLY WHAT THIS FACT WAS AFRAID
	// OF AND ALSO EXACTLY WHAT THE PORT IS FOR. Both roots are gone from both
	// DLLs -- there is no `typeinfo for CBaseEntity` and no
	// `typeinfo for C_BaseEntity` to walk from -- so the closure is empty for
	// the RIGHT reason, and the two readings the comment above warns about are
	// now indistinguishable from each other by this row alone.
	//
	// "which would read as a gate one step closer to green" was the warning, and
	// it is now the situation. So the fact is INVERTED and its control is moved
	// somewhere it can still fail: zero here, and
	// TheBinariesStillCarryATypeinfoTableToWalk underneath, which reads the same
	// typeinfo objects out of the same two files and requires hundreds of them.
	// A broken nm, an unreadable .so or a walk that returns nothing reddens that
	// one; only a genuinely entity-free binary satisfies both.
	long nSrv = -1, nCli = -1;
	ASSERT_TRUE( TsvRow( RealTreeTsv().m_szOutput, "1",
	                     "classes derived from them (typeinfo closure)", &nSrv, &nCli, NULL ) );
	EXPECT_EQ( 0, nSrv )
		<< "the server DLL has classes deriving from CBaseEntity again. The flip "
		   "removed the base; anything derived from it is a hierarchy that came "
		   "back.";
	EXPECT_EQ( 0, nCli )
		<< "the client DLL has classes deriving from C_BaseEntity again.";

	// AND THE UPPER BOUND, which is a regression pin for a real bug. mawk treats
	// a hex address whose digits happen to be all decimal (0000000000326948) as
	// the NUMBER 326,948 while one containing a-f stays a string, so the
	// containment test that attributes a relocation to the typeinfo object
	// holding it answered at random. Every address in the tool now carries a
	// leading 'x' to keep it a string. Before that, the client's list came back
	// with 24 classes in it — eleven material proxies, C_BaseSimpleCollision,
	// ITextureRegenerator and the rest, none of which derives from an entity.
	// The correct answer is 10.
	EXPECT_LT( nCli, 20 ) << "the client's derived-class list is inflated: the "
	                         "relocation containment test is comparing addresses "
	                         "as numbers again.";
}

TEST( EntityGates, TheDerivedClassMembersAreCountedSeparately )
{
	// ZERO CLASSES HAVE ZERO MEMBERS. This row is the closure's members, so it
	// follows the row above exactly and for the same reason; it is asserted
	// separately because the two are computed separately and a nonzero member
	// count against an empty class list would mean the attribution is matching
	// something it should not -- which is the 31,481 bug in the tool's own
	// header, where an empty root list made `grep -f` match every line.
	long nSrv = -1, nCli = -1;
	ASSERT_TRUE( TsvRow( RealTreeTsv().m_szOutput, "1", "... their members", &nSrv, &nCli, NULL ) );
	EXPECT_EQ( 0, nSrv );
	EXPECT_EQ( 0, nCli );
}

//-----------------------------------------------------------------------------
// THE CONTROL FOR EVERY ZERO ABOVE.
//
// Gate 1's nm rows and gate 2's are all 0/0 now, and so is the typeinfo
// closure. That is the port arriving, and it is ALSO what a broken instrument
// prints: nm against a path that does not exist, a readelf that found no
// relocations, a symbol-type filter written for `T` on a -fvisibility=hidden
// binary. Every one of those has happened here, and every one answers 0.
//
// So the zeros are given something that must NOT be zero, read out of the same
// two files with the same tool. The DLLs are full of polymorphic classes that
// have nothing to do with entities, and each one has a `typeinfo for X` object;
// if nm cannot see those, it could not have seen an entity's either.
//
// The bounds are deliberately loose -- this is a liveness check on the reader,
// not another ratchet for a lane to re-baseline. It is the shape the header of
// this file calls the fixture-vs-native assertion: two readings of one thing,
// where the one that must be nonzero is what makes the one that must be zero
// mean anything.
//-----------------------------------------------------------------------------
TEST( EntityGates, TheBinariesStillCarryATypeinfoTableToWalk )
{
	static const char *const kSo[] = { "game/mod_sharp/bin/linux64/server.so",
	                                   "game/mod_sharp/bin/linux64/client.so" };

	for ( size_t i = 0; i < sizeof( kSo ) / sizeof( kSo[ 0 ] ); i++ )
	{
		const Run_t run = RunAtRoot( std::string( "nm -S --defined-only '" ) + kSo[ i ] +
			"' | awk 'NF==4 && $4 ~ /^_ZTI/ && $4 !~ /^_ZTIP/' | wc -l" );
		ASSERT_EQ( 0, run.m_nExit ) << kSo[ i ] << ":\n" << run.m_szOutput;

		const long nTypeinfo = strtol( run.m_szOutput.c_str(), NULL, 10 );
		EXPECT_GT( nTypeinfo, 100 )
			<< kSo[ i ] << " has " << nTypeinfo << " non-pointer typeinfo "
			   "objects. The derived-class closure walks exactly these, so a "
			   "number this low means nm is not reading this binary -- and every "
			   "zero the gate rows assert is then a silence rather than a "
			   "measurement.";

		// AND THE ENTITY ROOTS ARE NOT AMONG THEM, which is the half that says
		// the zeros above are about the entity hierarchy specifically and not
		// about the walk having stopped.
		const Run_t roots = RunAtRoot( std::string( "nm -S --defined-only '" ) + kSo[ i ] +
			"' | awk 'NF==4 && $4 ~ /^_ZTI/ {print $4}' | c++filt "
			"| grep -cE '^typeinfo for C_?BaseEntity$' || true" );
		ASSERT_EQ( 0, roots.m_nExit ) << kSo[ i ] << ":\n" << roots.m_szOutput;

		// THE SERVER'S ONE EXCEPTION IS THE ENTITY FACE (plan_server_native_v2
		// D1): on stock public headers igameentity.h's six-member interface is
		// named CBaseEntity there, so a debug server.so (RTTI on) carries its
		// typeinfo. It is admitted on the gate's own evidence and on nothing
		// looser: the header declares the face, and every CBaseEntity:: symbol
		// the binary defines is that interface's implicit constructor or its
		// destructor. A stock member back and the root counts again.
		long nAllowed = 0;
		if ( i == 0 )
		{
			// The face is spelled CBaseEntity, or SS_GAME_ENTITY where
			// igameentity_fwd.h names it CBaseEntity on the server.
			const Run_t face = RunAtRoot( "{ grep -qx 'abstract_class CBaseEntity' src/public/igameentity.h || "
				"{ grep -qx 'abstract_class SS_GAME_ENTITY' src/public/igameentity.h && "
				"grep -qx '#define SS_GAME_ENTITY CBaseEntity' src/public/igameentity_fwd.h; }; } && echo 1 || echo 0" );
			const Run_t members = RunAtRoot( std::string( "nm -C --defined-only '" ) + kSo[ i ] +
				"' | awk '$2 ~ /^[TWVDBRt]$/' | grep 'CBaseEntity::' "
				"| grep -cv 'CBaseEntity::CBaseEntity()\\|CBaseEntity::~CBaseEntity()' || true" );
			if ( strtol( face.m_szOutput.c_str(), NULL, 10 ) == 1 &&
			     strtol( members.m_szOutput.c_str(), NULL, 10 ) == 0 )
				nAllowed = 1;
		}
		EXPECT_LE( strtol( roots.m_szOutput.c_str(), NULL, 10 ), nAllowed )
			<< kSo[ i ] << " still carries a typeinfo for CBaseEntity or "
			   "C_BaseEntity that is not the server's entity face, so the empty "
			   "closure above is the WALK failing rather than the hierarchy "
			   "being gone.";
	}
}

//=============================================================================
// The cast census and the cast gate must not drift apart.
//
// They read the same compiled TU list with the same pattern file, so for every
// family the gate's server column must equal the census's "server stock" plus
// "server shim", and likewise for the client. This is the fixture-vs-native
// assertion in miniature: two measurements of one thing, checked against each
// other rather than each trusted on its own.
//=============================================================================

TEST( EntityGates, TheEntityToInterfaceFamilyIsReportedButNotGated )
{
	// ToEngineIface counts casts in the OTHER direction — an entity forced to
	// IHandleEntity, IServerEntity and the rest. The end state still has those:
	// the shell IS an IServerEntity. Gating on it would demand zero of something
	// the definition of done does not mention, so it is printed and not gated —
	// and this fact pins that choice, because silently gating it later would
	// make the gate unpassable and silently dropping it would lose §3.16's note
	// about the cast that becomes a reinterpret_cast when the base goes away.
	const std::string &szOut = RealTreeTsv().m_szOutput;
	long nSrv = -1, nCli = -1;
	std::string szVerdict;
	ASSERT_TRUE( TsvRow( szOut, "3", "ToEngineIface (the other direction -- not gated)",
	                     &nSrv, &nCli, &szVerdict ) ) << szOut;
	EXPECT_GT( nSrv, 0 );
	EXPECT_EQ( "-", szVerdict );
}

//=============================================================================
// (b) THE PASS PATH RUNS.
//=============================================================================

//=============================================================================
// (c) THE ALLOWLIST MECHANISM WORKS BOTH WAYS.
//=============================================================================

//=============================================================================
// (d) THE TOOL REFUSES WHAT IT CANNOT MEASURE.
//
// Every refusal here would otherwise produce all-zero counts, and all-zero
// counts are the finished-port answer.
//=============================================================================

TEST( EntityGates, AnUnknownArgumentIsRefused )
{
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --not-an-option" );
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
}

TEST( EntityGates, OneDllPathIsRefusedRatherThanGuessedAt )
{
	// Two positional paths mean server and client. One means the caller thinks
	// it said something the tool did not hear, and defaulting the other half
	// would gate a build nobody asked about.
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " game/mod_sharp/bin/linux64/server.so" );
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
	EXPECT_NE( std::string::npos, run.m_szOutput.find( "expected two DLL paths" ) ) << run.m_szOutput;
}

TEST( EntityGates, OnlyOnAPassingTreeStillSkipsTheOthersRatherThanPassingThem )
{
	// The other side of the fact above, and the one the real tree can answer
	// now that it passes. `--only 2` over today's binaries must print gate 2 as
	// PASS and the other two as SKIPPED. If the tool reported a skipped gate as
	// PASS, this run -- three PASSes on one measurement -- is precisely the
	// report somebody would use to declare the port finished, and on a passing
	// tree it is indistinguishable from a real full run by exit code alone.
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --only 2 --tsv" );
	ASSERT_EQ( kExitAllPass, run.m_nExit ) << run.m_szOutput;

	std::string szVerdict;
	ASSERT_TRUE( TsvRow( run.m_szOutput, "2", "entity list", NULL, NULL, &szVerdict ) ) << run.m_szOutput;
	EXPECT_EQ( "PASS", szVerdict );
	ASSERT_TRUE( TsvRow( run.m_szOutput, "1", "base entity", NULL, NULL, &szVerdict ) );
	EXPECT_EQ( "SKIPPED", szVerdict )
		<< "gate 1 was not measured by this run but is reported as " << szVerdict << ".";
	ASSERT_TRUE( TsvRow( run.m_szOutput, "3", "entity casts", NULL, NULL, &szVerdict ) );
	EXPECT_EQ( "SKIPPED", szVerdict )
		<< "gate 3 was not measured by this run but is reported as " << szVerdict << ".";
}

TEST( EntityGates, AnOutOfRangeGateNumberIsRefused )
{
	const Run_t run = RunAtRoot( std::string( kToolRelative ) + " --only 4" );
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
}

} // namespace
