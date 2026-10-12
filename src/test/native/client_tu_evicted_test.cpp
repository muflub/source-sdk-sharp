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

std::string Code( const std::string &in );

std::string Read( const std::string &rel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();
	std::ifstream f( ( std::string( pszRoot ) + "/" + rel ).c_str() );
	if ( !f.is_open() )
		return std::string();
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
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

std::string Code( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	for ( size_t i = 0; i < in.size(); )
	{
		if ( in.compare( i, 2, "//" ) == 0 )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
		}
		else if ( in.compare( i, 2, "/*" ) == 0 )
		{
			i += 2;
			while ( i + 1 < in.size() && in.compare( i, 2, "*/" ) != 0 ) i++;
			i = ( i + 1 < in.size() ) ? i + 2 : in.size();
		}
		else
		{
			out += in[ i++ ];
		}
	}
	return out;
}

const char *const kClientCpp  = "game/server/client.cpp";
const char *const kOrphans    = "sourcesharp/game/server/shell_client_orphans.cpp";
const char *const kRetiredCs  = "sourcesharp/managed/SourceSharp.Server/DevCommandsRetired.cs";
// Phase 1b: the shim .cpp is deleted and all three registrations are managed.
// kOrphans above is kept as a PATH ONLY, so the source-list facts can still say
// "this row is not there"; nothing reads its text any more.
const char *const kConVarsCs  = "sourcesharp/managed/SourceSharp.Server/ConVars.cs";
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

TEST( ClientTuEvicted, ThePointServerCommandGateIsStillRegistered )
{
	// REPOINTED, NOT RELAXED. The registration moved from C++ to
	// SourceSharp.Server/ConVars.cs in Phase 1b, and the reason it could is a
	// measurement rather than a re-reading: the C++ argued it had to be native
	// so that an operator's server.cfg setting was not discarded, and the
	// managed side registers inside CServerGameDLL::DLLInit -- which is the
	// first call the engine makes into the game DLL, and long before it can
	// execute a config whose name is itself a game-DLL ConVar (servercfgfile,
	// game/server/game.cpp). PolicyKnobDeclarationTests pins the default, the
	// flags, the help and the C++ half being gone; this is the same claim read
	// out of the source, from the gtest side, so neither suite is the only one
	// holding it.
	const std::string s = Code( Read( kConVarsCs ) );
	ASSERT_FALSE( s.empty() );
	EXPECT_NE( s.find( "[ConVar(\"sv_allow_point_servercommand\", \"always\"" ),
	           std::string::npos )
		<< "the sv_allow_point_servercommand registration is gone or its default "
		   "changed. PointServerCommand.cs reads it BY NAME through "
		   "IEngineConsole.FindConVarValue; with no registration the lookup "
		   "fails and the gate on the most dangerous entity in the SDK is "
		   "decided by whatever Allows() does with a missing value.";
	EXPECT_NE( s.find( "disallow : Always disallow" ), std::string::npos )
		<< "the help text is not stock's, and the help text is where an operator "
		   "learns what values the gate takes";
}

TEST( ClientTuEvicted, TheGateIsStillReadByName )
{
	// The other half. Deleting the READER would leave the registration above
	// green while the gate stopped being consulted.
	const std::string s = Read( "sourcesharp/managed/SourceSharp.Server/Entities/Points/PointServerCommand.cs" );
	ASSERT_FALSE( s.empty() );
	EXPECT_NE( s.find( "\"sv_allow_point_servercommand\"" ), std::string::npos )
		<< "PointServerCommand.cs stopped naming the convar";
	EXPECT_NE( s.find( "FindConVarValue(AllowConVar)" ), std::string::npos )
		<< "PointServerCommand stopped reading the gate before running a "
		   "map-supplied server command";
}

TEST( ClientTuEvicted, TheTwoDebugDrawCommandsAreLiveManagedCommandsAgain )
{
	// THIS FACT HAS NOW SAID THREE THINGS, and each change was a real change to
	// the build rather than a rewrite of the assertion.
	//
	//  1. TheTwoDebugDrawCommandsCameAcrossWhole read the two NATIVE bodies out
	//     of shell_client_orphans.cpp and counted five UTIL_AddDebugLine calls,
	//     one for drawline and four for drawcross.
	//  2. TheTwoDebugDrawCommandsAreDeclaredRetirements, when Phase 1b deleted
	//     that file. The moved file's own comment had said the two "needed no
	//     entity, no ABI slot and no decision"; the middle third was wrong --
	//     UTIL_AddDebugLine puts a line into the ENGINE's debug overlay and the
	//     ABI published no line primitive at all, DrawEntityOverlay being TEXT
	//     on an ENTITY -- so there was nothing for a managed body to call. They
	//     became declared, PRINTED refusals, each naming the one appended slot
	//     that would lift it: AddLineOverlay.
	//  3. This. Lane 1e-abi appended that slot, so both are ordinary managed
	//     DevCommands and they DRAW.
	//
	// WHAT THIS FILE CAN AND CANNOT SEE. It is a text scan, so it asserts that
	// the pair is registered as live commands, that the retirement classes are
	// gone (two registrations of one word is a console that keeps whichever it
	// saw first and drops the other in silence), and that the slot they call is
	// declared and WIRED -- a slot assigned nowhere is a null pointer Engine.cs
	// skips without a word, which would put the two commands straight back into
	// the state this fact has spent two revisions describing. What they draw is
	// SourceSharp.Tests/DebugDrawCommandTests.cs's, against a recorded engine.
	const std::string s = Read( kRetiredCs );
	ASSERT_FALSE( s.empty() );
	EXPECT_EQ( s.find( "class DrawLineRetired" ), std::string::npos )
		<< "drawline is still registered as a retirement. With the live command "
		   "beside it the console has two classes for one word.";
	EXPECT_EQ( s.find( "class DrawCrossRetired" ), std::string::npos )
		<< "drawcross is still registered as a retirement.";

	const std::string live = Read( "sourcesharp/managed/SourceSharp.Server/DevCommandsLive.cs" );
	ASSERT_FALSE( live.empty() );
	EXPECT_NE( live.find( "[ConCommand(\"drawline\"" ), std::string::npos )
		<< "drawline is answered by nothing. Deleting the registration makes the "
		   "console say \"Unknown command\", which reads as a typo rather than as "
		   "a deliberate absence.";
	EXPECT_NE( live.find( "[ConCommand(\"drawcross\"" ), std::string::npos )
		<< "drawcross is answered by nothing.";
	EXPECT_NE( live.find( "class DrawLineCommand : DebugDrawCommand" ), std::string::npos );
	EXPECT_NE( live.find( "class DrawCrossCommand : DebugDrawCommand" ), std::string::npos );

	// A server shim-area method since plan_engineapi_vtables C6c: declared on
	// IShimDebugOverlay and wired by its generated forward.
	const std::string abi = Read( "sourcesharp/game/shared/ishim_areas.h" );
	ASSERT_FALSE( abi.empty() );
	EXPECT_NE( abi.find( " AddLineOverlay(" ), std::string::npos )
		<< "the server's areas no longer declare the line primitive the two commands call.";

	const std::string wiring = Read( "sourcesharp/game/shared/shell_shim_areas.inc" );
	ASSERT_FALSE( wiring.empty() );
	EXPECT_NE( wiring.find( "{ return ::SourceSharp_AddLineOverlay(" ), std::string::npos )
		<< "AddLineOverlay's area method does not forward to its body -- both commands would "
		   "print success and draw nothing, which is the exact state they were "
		   "retired OUT LOUD to avoid.";
}

TEST( ClientTuEvicted, TheTwoUnportedCommandsAreDeclaredRetirements )
{
	// §7: a method the port does not implement is a declared, PRINTED refusal,
	// never a silent no-op. bug_swap in particular was already a silent no-op --
	// its native body has been `return;` under SOURCESHARP since step 13c -- so
	// this is strictly better than what the eviction removed.
	const std::string s = Read( kRetiredCs );
	ASSERT_FALSE( s.empty() );
	EXPECT_NE( s.find( "[ConCommand(\"groundlist\"" ), std::string::npos )
		<< "groundlist went with client.cpp and nothing says so";
	EXPECT_NE( s.find( "[ConCommand(\"bug_swap\"" ), std::string::npos )
		<< "bug_swap went with client.cpp and nothing says so";
	EXPECT_NE( s.find( "class GroundListRetired(ILog log) : RetiredDevCommand" ), std::string::npos );
	EXPECT_NE( s.find( "class BugSwapRetired(ILog log) : RetiredDevCommand" ), std::string::npos );
}

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
