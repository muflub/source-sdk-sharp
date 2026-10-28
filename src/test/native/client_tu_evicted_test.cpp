//====== SourceSharp — game/server/client.cpp leaves the server build =========//
//
// plan_entity.md Phase 4's exit. `game/server/client.cpp` is 1,841 lines and
// left `server_csharp.vpc` (today: is not in server_sourcesharp); it was the
// ONE row of the exit's removal set that could move on its own, because it
// was batch 7 in the exit's computed removal order and the instrument that
// read what the exit still OWED read ZERO for it.
//
// AND THAT ZERO IS THE TRAP THIS FILE EXISTS FOR.
//
// THE INSTRUMENTS THEMSELVES ARE RETIRED, and this file no longer drives one.
// The close deleted all seven removal-set instruments once the exit had landed,
// along with the two gtests -- relink_exit_contract_test and exit_batches_test
// -- whose whole subject they were. They are described below in the past tense
// on purpose: the ARGUMENT is what this file is built on, and the argument does
// not need the tool to still exist. The last two facts assert that the
// retirement is complete and that the row it was pointed at really did move.
//
// The owed instrument was `nm --undefined-only` over every other object on the
// server link line, so it could only see a definition something else NAMES. A ConVar and a
// ConCommand are named by nothing: each registers itself from a static
// constructor at DLL load. Six of them were in this file and the instrument
// reported none. One of the six is `sv_allow_point_servercommand`, whose own
// note in client.cpp says deleting it "would silently open the most dangerous
// entity in the SDK".
//
// That is the same blind spot that hid `EffectsPrecache` from step 4f's first
// pass and that took `UTIL_DropToFloor`'s behaviour out from under the gateseq
// lane's fixture. So the eviction is checked as an ENUMERATION and not as a
// link: every registration the file made in this build is named here with where
// it went, and that something else now makes it is asserted.
//
//   sv_allow_point_servercommand  ConVar     -> shell_client_orphans.cpp
//   drawline                      ConCommand -> shell_client_orphans.cpp
//   drawcross                     ConCommand -> shell_client_orphans.cpp
//   groundlist                    ConCommand -> RETIRED, printed, managed
//   bug_swap                      ConCommand -> RETIRED, printed, managed
//   phys_swap                     ConCommand -> never compiled: it is inside
//                                               `#ifdef HL2_DLL`, which this
//                                               project does not define
//
// THE BUILD IS READ AS ITS COMPILED SOURCE SET, NOT AS TEXT (cmake_graph.h:
// compile_commands.json cross-checked against the CMakeLists). When it was a
// .vpc it was read as parsed `$File` rows for the same reason: a substring scan
// reads an explanation as a row; 8c's third needle-in-a-comment was exactly
// that, and the mutation that proved it deleted a row while leaving the name
// in a comment.
//
//=============================================================================//

#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

//-----------------------------------------------------------------------------
// Does the target compile this farm-relative source? The target's CMake source
// list, read through cmake_graph.h (compile_commands.json cross-checked against
// src/{server,client}/native/CMakeLists.txt), which throws on a missing, empty
// or stale input. The proof-of-read -- the port's own shim, which both sides
// compile and no eviction can take -- is asserted first, so "not compiled" is
// never the answer of a reader that saw nothing.
//
// (This used to parse `$File` rows out of the .vpc, stripping comments first
// because `.vpc` prose parsed as a row. A CMake source list carries no such
// prose inside the block cmake_graph.h reads, and it drops `#` comments.)
//-----------------------------------------------------------------------------
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

bool Builds( const char *pszTarget, const char *pszPath )
{
	EXPECT_TRUE( cmake_graph::Compiles( pszTarget, kProofOfRead ) )
		<< pszTarget << " does not compile " << kProofOfRead
		<< " -- the reader is not reading its source list";
	return cmake_graph::Compiles( pszTarget, pszPath );
}

const char *const kClientCpp  = "game/server/client.cpp";
const char *const kOrphans    = "sourcesharp/game/server/shell_client_orphans.cpp";
} // anonymous namespace

//=============================================================================
// 1. THE ROW IS OUT, AND ITS REPLACEMENT IS IN
//=============================================================================

TEST( ClientTuEvicted, TheServerNoLongerCompilesStockClientCpp )
{
	EXPECT_FALSE( Builds( cmake_graph::kServer, kClientCpp ) )
		<< "game/server/client.cpp is back in server_sourcesharp "
		   "(src/server/native/CMakeLists.txt). It brings 38 §3.16 cast sites "
		   "and six registrations, three of which managed code now makes -- so "
		   "this is also a duplicate ConVar registration for "
		   "sv_allow_point_servercommand.";
}

TEST( ClientTuEvicted, TheShimOrphanFileIsInNeitherBuild )
{
	// WAS TheShimOrphanFileIsCompiledAndOnlyOnTheServer, which required the row
	// to be in the server vpc because the three registrations were in it. Phase
	// 1b moved all three to managed code and deleted the file, so the claim
	// inverts: the row must be in NEITHER target's source list, because a row
	// naming a file that is not there is a build failure and a row naming a
	// file that came back is a second registration of names managed code now
	// owns.
	//
	// The three registrations are asserted against their managed homes below, so
	// this direction is not the only thing standing between the gate and nothing.
	EXPECT_FALSE( Builds( cmake_graph::kServer, kOrphans ) )
		<< kOrphans << " is back in server_sourcesharp. Its ConVar and its two "
		   "ConCommands are declared in managed code now; compiling it again "
		   "registers sv_allow_point_servercommand twice, and nothing says which "
		   "one PointServerCommand's by-name read finds.";
	EXPECT_FALSE( Builds( cmake_graph::kClient, kOrphans ) )
		<< kOrphans << " is in client_sourcesharp. (The client has its OWN file of "
		   "that basename, sourcesharp/game/client/shell_client_orphans.cpp, "
		   "which is a different row and stays -- this is the server path.)";
}

// RETIRED IN PHASE 6b: TheStockBuildStillCompilesClientCpp, which asserted that
// game/server/server_base.vpc still named client.cpp so that "we removed one
// row" and "we deleted it everywhere" did not look the same. server_base.vpc
// describes Valve's stock build, which this repo never builds and the farm no
// longer carries.

//=============================================================================
// 3. WHAT EACH REGISTRATION BECAME
//=============================================================================

//=============================================================================
// 5. THE INSTRUMENT THIS LANE ADDED, RETIRED.
//
// `tools/p4-exit-owed` carried a hermetic `--self-test`, and the two facts here
// USED TO DRIVE IT -- exit 0, the words "self-test PASSED", and at least ten
// `ok` lines -- because a self-test that checks nothing is exit 0 from a tool
// that measured nothing, and this project has found that shape more than twenty
// times. A `--self-test` nobody invokes is a mode that rots.
//
// THE TOOL IS DELETED. Its one job -- separating the exit's real work list from
// the vtable floors -- is finished, and it went with the other six removal-set
// instruments and with the two gtests whose whole subject they were.
//
// The two facts are RE-POINTED rather than deleted, at the two things that
// outlive the tool: that the row it was pointed at really did move, and that
// the retirement is COMPLETE. Both mattered to the tool's own reasoning. A tool
// left in the tree with its inputs gone is worse than one nobody runs, because
// it still answers and the next reader takes the answer.
//=============================================================================

namespace
{

// Does a path exist, relative to the REPOSITORY root? SrcRoot() names the `src`
// directory's parent candidate, so the tools directory is one level up from it
// in every case the finder accepts -- the same arithmetic the deleted RunOwed
// helper did to invoke the tool.
//
// An existence probe and NOT a read: a tool is an executable script, and "reads
// as empty" and "is not there" have to stay different answers.
bool RepoPathExists( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;

	struct stat st;
	const std::string path = std::string( pszRoot ) + "/../" + pszRelative;
	return stat( path.c_str(), &st ) == 0;
}

} // anonymous namespace

TEST( ClientTuEvicted, TheRetirementProbeAnswersBothWays )
{
	// Without this, every absence below passes on a probe that answers false for
	// everything -- a wrong SrcRoot(), a typo in a path constant, a binary run
	// from a directory the finder does not accept.
	EXPECT_TRUE( RepoPathExists( "src/buildcpptests" ) )
		<< "the existence probe cannot see a file that is definitely there, so "
		   "every absence asserted with it is passing vacuously.";
	EXPECT_FALSE( RepoPathExists( "tools/no_such_tool_9d3f" ) )
		<< "the existence probe reports a path that was never in this tree.";
}

TEST( ClientTuEvicted, TheOwedInstrumentIsRetiredAndTheRowItMeasuredHasMoved )
{
	// WAS TheOwedInstrumentsSelfTestPassesAndRunsChecks, which ran
	// `tools/p4-exit-owed --self-test` and read exit 0, the words "self-test
	// PASSED" and at least ten `ok` lines out of its report. The tool is DELETED,
	// so what that fact ran is `sh: 1: tools/p4-exit-owed: not found` -- exit 127
	// -- which is a red that says nothing about this tree.
	//
	// "The tool is gone" on its own is half a fact: it is also satisfied by a
	// tree with nothing in it. The other half is the only part of that
	// instrument's output that was ever a claim about this tree rather than about
	// the tool -- it read ZERO for `client`, and the row it read zero for is out
	// of the server build with something else compiled in its place.
	EXPECT_FALSE( RepoPathExists( "tools/p4-exit-owed" ) )
		<< "tools/p4-exit-owed is back. If the Phase 4 exit needs an owed list "
		   "again, say what in the commit -- and drive its --self-test from "
		   "somewhere, because a mode nobody invokes rots.";

	EXPECT_FALSE( Builds( cmake_graph::kServer, kClientCpp ) )
		<< kClientCpp << " is back in server_sourcesharp. That is the row the "
		   "owed instrument read zero for and the one this whole file is the "
		   "record of; with it back, the six registrations enumerated above are "
		   "made twice.";

	EXPECT_FALSE( Builds( cmake_graph::kServer, kOrphans ) )
		<< kOrphans << " is back in server_sourcesharp. Phase 1b moved all "
		   "three registrations to managed code and deleted it; compiling it "
		   "again makes each of them twice.";
}

TEST( ClientTuEvicted, TheWholeRemovalSetInstrumentFamilyWentTogether )
{
	// WAS TheOwedInstrumentRefusesWithoutALog, which handed the tool no relink
	// log and required a non-zero exit, because a tool that printed an empty work
	// list would say "nothing is owed" -- the answer the whole Phase 4 exit is
	// trying to reach and the one it must not be able to fake. Run against a
	// deleted tool that fact was GREEN, on exit 127, which is the check that
	// cannot fail in its purest form.
	//
	// THE SAME HAZARD, ONE LEVEL UP. A half-retired instrument family is a fake
	// nobody can even run: a tool left in the tree with its inputs gone answers
	// from stale state or from nothing, and the next reader takes the answer. All
	// seven went together, with the two gtests whose whole subject they were.
	// This says it stayed that way.
	const char *const kRetired[] = {
		"tools/4a-owes",
		"tools/p4-exit-owed",
		"tools/p4-exit-batches",
		"tools/client-flip-relink",
		"tools/client-flip-closure",
		"tools/c7-leaf-cycle",
		"src/p4-exit-relink",
		"src/sourcesharp/test/relink_exit_contract_test.cpp",
		"src/sourcesharp/test/exit_batches_test.cpp",
	};
	for ( size_t i = 0; i < sizeof( kRetired ) / sizeof( kRetired[ 0 ] ); i++ )
	{
		EXPECT_FALSE( RepoPathExists( kRetired[ i ] ) )
			<< kRetired[ i ] << " is back. The seven removal-set instruments and "
			   "their two gtests were retired as one thing because they read one "
			   "another's output; restoring one alone gives an answer computed "
			   "from inputs that are not there.";
	}
}
