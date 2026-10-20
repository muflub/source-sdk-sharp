//========= SourceSharp — nothing sets a prediction suppress host ============//
//
// A SOURCE SCANNER, like laneR_sound_dead_paths_test and soundemitter_lift_test,
// and for the same reason: the claim is about the TEXT the compiler was handed,
// and that text needs a whole engine to run.
//
// WHAT IT DEFENDS. plan_entity.md step 4b DELETED three blocks that read
// IPredictionSystem's suppress host:
//
//   sourcesharp/game/server/shell_recipientfilter.cpp
//       - the suppression test in AddRecipientByPlayerIndex
//       - the ToBasePlayer cull in UsePredictionRules
//   sourcesharp/game/server/shell_tempents.cpp
//       - the whole body of SourceSharpTe_SuppressFilter, which was §3.16
//         FAMILY 5: `(CBasePlayer *)g_TePrediction.GetSuppressHost()`
//
// Each was deleted rather than refused, on ONE argument: the suppress host is
// NULL on every path in this build, so every one of the three already computed
// what it computes now. That argument is a claim about OTHER FILES, it is not
// visible from the files that were changed, and it stops being true the moment
// somebody puts a writer back. This is where it is checked.
//
// WHY IT IS NOT A REFUSAL. §7's rule is that a method the port does not
// implement is a DECLARED refusal rather than a silent no-op. These are not
// unimplemented: they are branches whose condition is false, in a build whose
// shape makes it false, and the honest form of that is a deletion plus a test
// that fails when the shape changes. A refusal entry would say "sounds may
// reach a client who already predicted them", which is not true of anything
// this build does.
//
// THE THREE WRITERS, WHICH IS THE WHOLE INVENTORY:
//
//   game/server/physics_main.cpp        SuppressHostEvents( playerowner ) --
//                                       inside `#ifndef SOURCESHARP`
//   game/server/player.cpp              SuppressHostEvents( this ) -- the TU is
//                                       in neither build
//   game/shared/takedamageinfo.cpp      SetSuppressHost( host ) -- writes back
//                                       the value it read four lines earlier
//
// A fourth, or player.cpp or the stock takedamageinfo.cpp becoming compiled,
// fails a fact below by name. (physics_main.cpp's own fact read the file, and
// went with it when the farm stopped carrying stock files neither DLL
// compiles.)
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, so the fixtures at the bottom assert that the build
// really listed translation units, that the files really opened, and that
// the SOURCESHARP guard detector answers DIFFERENTLY for the same token on the
// two arms of a conditional -- which is the one thing every claim here rests
// on.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <dirent.h>
#include <sys/stat.h>
#include <unistd.h>

#include <algorithm>
#include <cstdlib>
#include <fstream>
#include <set>
#include <string>
#include <vector>


namespace
{

//-----------------------------------------------------------------------------
// Locating src/. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string strProbe( kCandidates[ i ] );
		strProbe += "/game/shared";
		DIR *pDir = opendir( strProbe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadSrcFile( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// WHAT THE COMPILER WAS ACTUALLY GIVEN.
//
// The CMake build's compiled sources: the union of server_sourcesharp,
// client_sourcesharp and sourcesharp (libmanaged_host.so), read through
// cmake_graph.h -- compile_commands.json cross-checked against each target's
// CMakeLists source list. Farm-relative paths, the spelling ReadSrcFile takes.
//
// THIS USED TO READ VPC'S NINJA GRAPH (_vpc_/ninja/sdk_csharp_debug.ninja),
// with a freshness guard of its own: the graph's `regenerate_build` provenance
// row, each input's mtime against the graph's, a second loose `: cxx ` reading
// that the walk had to agree with, and a set of scratch-directory fixtures
// driving every arm of that guard. RETIRED IN PHASE 6b, all of it: that was VPC
// graph provenance. Missing, empty and stale inputs of the CMake graph are
// cmake_graph.h's own hard failures (CompiledSources() throws, and throws when
// compile_commands.json disagrees with the CMakeLists -- the second reading the
// loose scan used to be), and cmake_graph_test is where those arms are driven.
//
// What stays here is the proof-of-read: every fact that asks IsBuilt() opens
// by requiring the port's own shim to be in the set, so an absence is never
// the answer of a reader that saw nothing.
//-----------------------------------------------------------------------------
const char *const kBuildProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

const std::set< std::string > &BuiltSources()
{
	static std::set< std::string > s;
	static bool bDone = false;
	if ( bDone )
		return s;

	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };
	std::set< std::string > all;
	for ( size_t i = 0; i < sizeof( kTargets ) / sizeof( kTargets[ 0 ] ); i++ )
	{
		const std::set< std::string > one = cmake_graph::CompiledSources( kTargets[ i ] );
		all.insert( one.begin(), one.end() );
	}
	s.swap( all );
	bDone = true;
	return s;
}

bool IsBuilt( const char *pszRelative )
{
	return BuiltSources().count( std::string( pszRelative ) ) > 0;
}

// Every fact that asks IsBuilt() opens with this. Not a helper call that could
// be forgotten quietly: without it the fact's absences are satisfied by a
// reader that read nothing.
#define REQUIRE_THE_BUILD_READ()                                        \
	do {                                                                \
		ASSERT_TRUE( IsBuilt( kBuildProofOfRead ) )                     \
			<< "the CMake build's compiled sources do not include "     \
			<< kBuildProofOfRead << " -- the reader is not reading the "  \
			   "build, and every IsBuilt() absence would pass for free."; \
	} while ( 0 )

//-----------------------------------------------------------------------------
// Which LINES of a file this build does not compile, because a SOURCESHARP
// conditional excluded them.
//
// Only the SOURCESHARP question is tracked; every other conditional is
// transparent, which is deliberate -- `#ifdef _WIN32` around a suppress-host
// writer would still be a writer this test must see, because some
// configuration of this tree takes it.
//-----------------------------------------------------------------------------
std::vector< bool > SourceSharpOffLines( const std::string &strSource )
{
	std::vector< bool > out;

	struct Frame
	{
		bool bIsSourceSharpQuestion;
		bool bOnExcludedArm;
	};
	std::vector< Frame > stack;
	int nDepth = 0;

	size_t i = 0;
	while ( i <= strSource.size() )
	{
		size_t nEnd = strSource.find( '\n', i );
		if ( nEnd == std::string::npos )
			nEnd = strSource.size();
		const std::string strLine = strSource.substr( i, nEnd - i );

		const size_t nFirst = strLine.find_first_not_of( " \t" );
		const std::string strTrim = ( nFirst == std::string::npos )
		                            ? std::string() : strLine.substr( nFirst );

		const bool bDirective = ( !strTrim.empty() && strTrim[ 0 ] == '#' );
		const bool bLineIsOff = ( nDepth > 0 );

		if ( bDirective )
		{
			// Normalise `#  ifndef` to `ifndef`.
			std::string strD;
			for ( size_t k = 1; k < strTrim.size(); k++ )
			{
				if ( strTrim[ k ] != ' ' && strTrim[ k ] != '\t' )
					strD += strTrim[ k ];
				else if ( !strD.empty() )
					break;
			}
			const std::string strRest = strTrim.substr( 1 );
			const bool bNames = ( strRest.find( "SOURCESHARP" ) != std::string::npos );

			if ( strD == "if" || strD == "ifdef" || strD == "ifndef" )
			{
				Frame f;
				f.bIsSourceSharpQuestion = bNames;
				// The arm this build does NOT take is the one that asks for
				// SOURCESHARP to be undefined: `#ifndef SOURCESHARP` and
				// `#if !defined( SOURCESHARP )`.
				const bool bNegated = ( strD == "ifndef" )
				                    || ( strRest.find( '!' ) != std::string::npos );
				f.bOnExcludedArm = bNames && bNegated;
				if ( f.bOnExcludedArm )
					nDepth++;
				stack.push_back( f );
			}
			else if ( strD == "else" || strD == "elif" )
			{
				if ( !stack.empty() )
				{
					Frame &f = stack.back();
					if ( f.bIsSourceSharpQuestion )
					{
						if ( f.bOnExcludedArm )
							nDepth--;
						else
							nDepth++;
						f.bOnExcludedArm = !f.bOnExcludedArm;
					}
				}
			}
			else if ( strD == "endif" )
			{
				if ( !stack.empty() )
				{
					if ( stack.back().bOnExcludedArm )
						nDepth--;
					stack.pop_back();
				}
			}
		}

		out.push_back( bDirective ? false : bLineIsOff );

		if ( nEnd == strSource.size() )
			break;
		i = nEnd + 1;
	}

	return out;
}

//-----------------------------------------------------------------------------
// Every line of a file that names `pszNeedle` in CODE and that this build
// compiles. Comments are dropped per line: this test's own subject appears in
// prose all over the tree, including in the two files it is about.
//-----------------------------------------------------------------------------
std::vector< std::string > CompiledLinesNaming( const std::string &strSource,
                                                const char *pszNeedle )
{
	const std::vector< bool > off = SourceSharpOffLines( strSource );
	std::vector< std::string > hits;

	size_t i = 0, nLine = 0;
	while ( i <= strSource.size() && nLine < off.size() )
	{
		size_t nEnd = strSource.find( '\n', i );
		if ( nEnd == std::string::npos )
			nEnd = strSource.size();
		std::string strLine = strSource.substr( i, nEnd - i );

		const size_t nComment = strLine.find( "//" );
		if ( nComment != std::string::npos )
			strLine = strLine.substr( 0, nComment );

		if ( !off[ nLine ] && strLine.find( pszNeedle ) != std::string::npos )
			hits.push_back( strLine );

		if ( nEnd == strSource.size() )
			break;
		i = nEnd + 1;
		nLine++;
	}

	return hits;
}

//-----------------------------------------------------------------------------
// The files this test reads by name, so a rename fails loudly rather than
// silently reading nothing.
//-----------------------------------------------------------------------------
const char *const kRecipientFilter =
	"sourcesharp/game/server/shell_recipientfilter.cpp";
const char *const kTempEnts =
	"sourcesharp/game/server/shell_tempents.cpp";
// The Phase 4 exit's cluster-E lane (p4-util) moved every definition
// game/shared/takedamageinfo.cpp held into the shim, WHOLE, so the save/restore
// pair below is at this address now. plan_shim.md Phase 1d then took the empty
// stock TU out of BOTH vpcs, so the claim here -- "the ONE compiled unguarded
// writer is a save/restore pair" -- no longer has a second compiled file to
// worry about. TheStockTakeDamageInfoTuIsNotCompiledAtAll is what holds that
// premise still true; it is the eviction that is pinned now, not the contents.
const char *const kTakeDamageInfo    = "sourcesharp/game/shared/takedamageinfo_library.cpp";
const char *const kTakeDamageInfoTu  = "game/shared/takedamageinfo.cpp";
const char *const kPlayer         = "game/server/player.cpp";

} // namespace

//=============================================================================
// The inventory of writers.
//=============================================================================

//-----------------------------------------------------------------------------
// player.cpp's two writers are unguarded, and the TU is in neither build.
// This is the fact that changes if somebody puts the SDK player back.
//-----------------------------------------------------------------------------
TEST( SuppressHostDead, PlayerCppIsNotCompiledAtAll )
{
	REQUIRE_THE_BUILD_READ();
	EXPECT_FALSE( IsBuilt( kPlayer ) )
		<< kPlayer << " is compiled now. It calls "
		   "IPredictionSystem::SuppressHostEvents( this ) with a live player, "
		   "so the suppress host stops being NULL and the three blocks step 4b "
		   "deleted have to come back -- see shell_recipientfilter.cpp and "
		   "shell_tempents.cpp.";
}

//-----------------------------------------------------------------------------
// takedamageinfo.cpp WROTE, a save/restore pair inside ApplyMultiDamage, and
// that was the one compiled, unguarded writer in the tree. plan_server_native_v2
// T6 made the multi-damage accumulator client-only (nothing on the server ever
// filled it), so the server compiles NO writer here at all -- a stronger form
// of "the host is always NULL" than the pair was.
//-----------------------------------------------------------------------------
TEST( SuppressHostDead, TakeDamageInfoCompilesNoSuppressHostWrite )
{
	REQUIRE_THE_BUILD_READ();
	ASSERT_TRUE( IsBuilt( kTakeDamageInfo ) )
		<< kTakeDamageInfo << " is not compiled; this fact measures nothing.";

	const std::string s = ReadSrcFile( kTakeDamageInfo );
	ASSERT_FALSE( s.empty() ) << kTakeDamageInfo << " read as empty";

	// The positive arm: the reader still sees the file's compiled code.
	ASSERT_FALSE( CompiledLinesNaming( s, "ImpulseScale(" ).empty() )
		<< "the scanner found no compiled ImpulseScale in " << kTakeDamageInfo
		<< "; a zero below would be the scanner's, not the file's.";

	const std::vector< std::string > writes =
		CompiledLinesNaming( s, "SetSuppressHost(" );
	EXPECT_EQ( (size_t)0, writes.size() )
		<< kTakeDamageInfo << " compiles " << writes.size() << " suppress-host "
		<< "writes again. If one CHOOSES a host, the host is no longer always NULL.";
}

//-----------------------------------------------------------------------------
// The assertion the repoint above would otherwise have lost. The claim is that
// there is ONE compiled unguarded writer; a copy left at the old address makes
// two, and the fact above would still read the shim's and pass.
//-----------------------------------------------------------------------------
TEST( SuppressHostDead, TheStockTakeDamageInfoTuIsNotCompiledAtAll )
{
	// THE PREMISE MOVED UNDER THIS FACT AT plan_shim.md PHASE 1d, and the
	// honest version is weaker, so it is written as the weaker thing rather
	// than left looking like the old one.
	//
	// What this asked was whether the stock TU, which WAS compiled, had kept
	// a suppress-host write of its own -- because two compiled writers would
	// make "the one compiled unguarded writer is a save/restore pair" false.
	// Phase 1d deleted both of its vpc rows (0 objects import it on either
	// side; it defines no strong symbols at all), so it is not compiled, and
	// a file that is not compiled cannot be a compiled writer whatever it
	// contains. Keeping the old body would have been a check that cannot
	// fail: its needles would read an uncompiled file forever.
	//
	// What is worth pinning is the premise itself, so that the day someone
	// puts the row back the claim above is re-examined rather than silently
	// weakened.
	REQUIRE_THE_BUILD_READ();
	EXPECT_FALSE( IsBuilt( kTakeDamageInfoTu ) )
		<< kTakeDamageInfoTu << " is compiled again. It was evicted from both "
		   "sides in Phase 1d as an empty translation unit; if it is back, it "
		   "is a second compiled file that may write the suppress host, and "
		   "TakeDamageInfosOnlyWriteIsARestoreOfWhatItRead stops being about "
		   "the ONLY compiled writer -- re-measure it rather than trust it.";
}

//=============================================================================
// The three deleted readers stay deleted.
//=============================================================================

TEST( SuppressHostDead, TheRecipientFilterReadsNoSuppressHost )
{
	const std::string s = ReadSrcFile( kRecipientFilter );
	ASSERT_FALSE( s.empty() ) << kRecipientFilter << " read as empty";

	const std::vector< std::string > hits =
		CompiledLinesNaming( s, "GetSuppressHost" );

	EXPECT_TRUE( hits.empty() )
		<< kRecipientFilter << " reads the suppress host again (" << hits.size()
		<< " site(s), first: " << ( hits.empty() ? std::string() : hits[ 0 ] )
		<< "). Step 4b deleted both readers, and with them one C-style cast to "
		   "a base entity pointer and one ToBasePlayer downcast -- §3.16 sites "
		   "the census counts over this file's text.";
}

TEST( SuppressHostDead, TheTempEntSuppressFilterReadsNoSuppressHost )
{
	const std::string s = ReadSrcFile( kTempEnts );
	ASSERT_FALSE( s.empty() ) << kTempEnts << " read as empty";

	const std::vector< std::string > hits =
		CompiledLinesNaming( s, "GetSuppressHost" );

	EXPECT_TRUE( hits.empty() )
		<< kTempEnts << " reads the suppress host again (" << hits.size()
		<< " site(s)). That read is what carried §3.16 family 5 into this file: "
		   "the host is an IPredictionSystem's base-entity pointer and the cull "
		   "wanted a player.";
}

//-----------------------------------------------------------------------------
// AND NO SENDER IS SUPPRESSED. SourceSharpTe_SuppressFilter answered what
// stock's body computed for a NULL host, false ("there is at least one
// recipient, do not suppress"), so every caller's suppress arm was dead.
// plan_server_native_min P1 deleted the function and those arms together.
//
// The risk this fact kept out is unchanged: a suppress check that can answer
// true with no host would silently drop EVERY temp entity in the game. With
// the function gone, the guard is that nothing in the file asks one again.
//-----------------------------------------------------------------------------
TEST( SuppressHostDead, NoTempEntSenderConsultsASuppressFilter )
{
	const std::string s = ReadSrcFile( kTempEnts );
	ASSERT_FALSE( s.empty() ) << kTempEnts << " read as empty";
	ASSERT_FALSE( CompiledLinesNaming( s, "SourceSharpTe_" ).empty() )
		<< kTempEnts << " names no SourceSharpTe_ function, so it is not the "
		   "temp-entity file this fact means to read";

	const std::vector< std::string > hits =
		CompiledLinesNaming( s, "SuppressFilter" );

	EXPECT_TRUE( hits.empty() )
		<< kTempEnts << " consults a suppress filter again (" << hits.size()
		<< " site(s), first: " << ( hits.empty() ? std::string() : hits[ 0 ] )
		<< "). With no suppress host on this server it can only answer false; "
		   "an answer of true drops every temp entity in the game.";
}

//=============================================================================
// The scanner's own fixtures. Everything above is worthless if these fail.
//=============================================================================

TEST( SuppressHostDeadScanner, TheBuildListedTranslationUnits )
{
	ASSERT_TRUE( SrcRoot() != NULL )
		<< "no src/ found from the working directory -- every claim above "
		   "passed on an empty read";

	// WAS `> 200u`, then a second loose reading of VPC's ninja graph that the
	// walk had to agree with. The second reading is cmake_graph.h's own now
	// (compile_commands.json against the CMakeLists); what is left is that the
	// set is the build's, which the proof-of-read says.
	REQUIRE_THE_BUILD_READ();
}

TEST( SuppressHostDeadScanner, TheThreePathsAllOpened )
{
	// A FLOOR PLUS AN ANCHOR PER PATH. The floor alone was `> 1000 bytes`, and
	// a size is the wrong instrument twice over: it says nothing about WHICH
	// file was read, and it cannot tell a truncated file from a SHORTENED one.
	// These are shim files this port keeps splitting up -- the comment below
	// already worked out, for another path, that a byte floor can assert the
	// opposite of what a move achieved.
	//
	// RE-ANCHORED when the farm stopped carrying stock files neither DLL
	// compiles: this was FIVE paths, and the two stock ones --
	// game/server/physics_main.cpp and game/server/player.cpp -- are gone from
	// it. Nothing left in this file reads either one's contents;
	// PlayerCppIsNotCompiledAtAll asks the build, not the file.
	//
	// The floor drops to a value only an UNOPENED or TRUNCATED read can miss --
	// ReadSrcFile answers "" for a path it cannot open, which is the defect
	// this fact exists to catch -- and each anchor does the identifying work
	// the size was standing in for and could never do.
	//
	// kTakeDamageInfoTu is deliberately NOT in this list: the file is 81 lines
	// of banner and stock's own DMG_ commentary, so a "> 1000 bytes" floor
	// would be asserting the opposite of what that move achieved. Since
	// Phase 1d nothing reads its CONTENTS at all -- the fact that names it
	// asks the build whether it is compiled, and REQUIRE_THE_BUILD_READ is
	// what stops that one passing on a reader that read nothing.
	struct Subject_t { const char *pszPath; const char *pszAnchor; };
	static const Subject_t kAll[] = {
		{ kRecipientFilter, "CRecipientFilter::" },
		{ kTempEnts,        "SourceSharp_TempEntCloseRegistration" },
		{ kTakeDamageInfo,  "CTakeDamageInfo::Init" },
	};

	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[ 0 ] ); i++ )
	{
		const std::string text = ReadSrcFile( kAll[ i ].pszPath );

		EXPECT_GT( text.size(), (size_t)200 )
			<< kAll[ i ].pszPath << " read as empty or trivial -- a scanner that "
			   "reads nothing passes every claim it is given";
		EXPECT_NE( text.find( kAll[ i ].pszAnchor ), std::string::npos )
			<< kAll[ i ].pszPath << " does not contain `" << kAll[ i ].pszAnchor
			<< "`, so what was read is not the file this scanner means to be "
			   "reading -- every claim it is given passes over the wrong text.";
	}
}

//-----------------------------------------------------------------------------
// THE GUARD DETECTOR ANSWERS DIFFERENTLY ON THE TWO ARMS.
//
// Every claim above is "X does not appear in the part of this file the build
// compiles", which is the purest shape of a check that cannot fail: a detector
// that reported EVERY line as excluded would make all five green. So the same
// token is put on both arms of a conditional and the answers are required to
// differ.
//-----------------------------------------------------------------------------
TEST( SuppressHostDeadScanner, TheGuardDetectorSeparatesTheTwoArms )
{
	const std::string strFixture =
		"int a = GetSuppressHost();\n"
		"#ifndef SOURCESHARP\n"
		"int b = GetSuppressHost();\n"
		"#else\n"
		"int c = GetSuppressHost();\n"
		"#endif\n";

	const std::vector< std::string > hits =
		CompiledLinesNaming( strFixture, "GetSuppressHost" );

	ASSERT_EQ( (size_t)2, hits.size() )
		<< "the detector saw " << hits.size() << " compiled sites in a fixture "
		   "with exactly two";
	EXPECT_NE( std::string::npos, hits[ 0 ].find( "int a" ) );
	EXPECT_NE( std::string::npos, hits[ 1 ].find( "int c" ) );
}

TEST( SuppressHostDeadScanner, TheGuardDetectorHandlesTheDefinedSpelling )
{
	const std::string strFixture =
		"#if !defined( SOURCESHARP )\n"
		"int b = GetSuppressHost();\n"
		"#endif\n"
		"int a = GetSuppressHost();\n";

	const std::vector< std::string > hits =
		CompiledLinesNaming( strFixture, "GetSuppressHost" );

	ASSERT_EQ( (size_t)1, hits.size() );
	EXPECT_NE( std::string::npos, hits[ 0 ].find( "int a" ) );
}

TEST( SuppressHostDeadScanner, AnUnrelatedConditionalStaysTransparent )
{
	// A writer hidden behind `#ifdef _WIN32` is still a writer some
	// configuration of this tree takes, and this detector must see it.
	const std::string strFixture =
		"#ifdef _WIN32\n"
		"int a = GetSuppressHost();\n"
		"#endif\n";

	EXPECT_EQ( (size_t)1, CompiledLinesNaming( strFixture, "GetSuppressHost" ).size() );
}

TEST( SuppressHostDeadScanner, CommentsAreNotReadAsCode )
{
	// This test's own subject is discussed at length in the comments of both
	// files it reads. A scanner that counted prose would fail on a correct tree.
	const std::string strFixture =
		"// GetSuppressHost was here and it is gone\n"
		"int a = 0; // GetSuppressHost\n";

	EXPECT_TRUE( CompiledLinesNaming( strFixture, "GetSuppressHost" ).empty() );
}

//=============================================================================
// (Z) THE INSTRUMENT'S OWN PROVENANCE -- RETIRED IN PHASE 6b.
//
// Fourteen facts stood here: TheReleaseGraphIsNotReadByThisFile,
// TheDebugGraphIsTheOneConfigurationRead, TheRealDebugGraphIsPresentAndCurrent,
// AMissingGraphIsAHardFailure, AnEmptyGraphIsAHardFailure,
// AGraphThatWillNotSayWhatItCameFromIsAHardFailure,
// AGraphOlderThanOneOfItsOwnInputsIsStale,
// TheStaleMessageNamesTheGuiltyScriptAndBothMtimes,
// AGraphNewerThanEveryInputIsAccepted,
// AnInputTheTreeNoLongerHasDoesNotCountAsStale,
// TheProvenanceRowIsReadOutOfTheGraphAndNotGuessed,
// TheProvenanceRowStopsAtNinjasImplicitDeps,
// AGraphWithNoProvenanceRowYieldsNoInputs and
// TheRealGraphNamesTheTwoGameProjectVpcs. Every one was about VPC's ninja
// graph: which configuration's graph was read, and whether it was present,
// non-empty and newer than the .vpc/.vgc scripts its `regenerate_build` row
// named. That is VPC graph provenance, and VPC is gone. The CMake build is
// read per configuration through SOURCESHARP_BUILD_DIR, and its missing,
// empty and stale inputs are cmake_graph.h's own hard failures, driven by
// cmake_graph_test.
//=============================================================================
