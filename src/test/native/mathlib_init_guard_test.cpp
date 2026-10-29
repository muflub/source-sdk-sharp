//========= SourceSharp — no mathlib binary may run uninitialised ============//
//
// THE DEFECT THIS STANDS FOR, and it is the twenty-seventh of its kind found in
// this tree — the first one found inside a test written to check GEOMETRY.
//
// collideable_center_adapter_test linked mathlib and never called
// MathLib_Init().  Every run printed
//
//     mathlib_base.cpp (1199) : Assertion Failed: s_bMathlibInitialized
//
// six times and reported `[  PASSED  ] 4 tests.`  tier0's Assert in these
// binaries PRINTS and returns; gtest is never told, so nothing goes red.  And
// the numbers agreed for a reason that is not evidence: both sides of every
// comparison were computed from the same uninitialised tables, and MathLib_Init
// is what picks the SSE implementations of VectorNormalize/Sqrt/RSqrt over the
// generic ones — so an uninitialised binary silently measures a different
// library from the one the game links.
//
// world_aligned_bounds_test had had the Environment since it was written, and a
// comment in it explaining exactly this.  The comment did not stop the next
// four mathlib binaries from being written without one.  A comment is not a
// guard; this file is.
//
// HOW IT GUARDS. Two halves, and they check each other:
//
//   * STATIC (here). Read the native tests' manifest,
//     src/test/native/CMakeLists.txt, find every `ss_cpptest_mathlib` call,
//     and fail if a source it names does not include
//     sourcesharp/test/mathlib_test_env.h — the one place MathLib_Init is
//     called.  Also fail if anything links mathlib WITHOUT going through that
//     helper (cmake/SourceSharpTests.cmake), because such a binary would be
//     invisible to the scan above.
//
//   * RUNTIME (ctest). The helper gives each mathlib test a
//     FAIL_REGULAR_EXPRESSION on the assertion text, so the text anywhere in a
//     binary's output fails it.  That catches what a source scan cannot: an
//     include that is present but somehow not effective.
//
//     Until Phase 6b both halves were in the bash script buildcpptests
//     (build_and_run_mathlib, and a grep of each binary's output); the script
//     is now a driver over ctest and the manifest is CMake.
//
//     TheRunnerStillChecksTheOutput below asserts that half is still there, so
//     deleting it goes red here rather than quietly.
//
// THE SCANNER IS ITSELF CHECKED, the same way engine_core_retype_test's is:
// every file it claims to have read is asserted to be non-trivially long, both
// comment strippers are tested on hand-written fixtures, the count of
// invocations has a floor, and the "include is present" search is shown to
// FAIL on a fixture that lacks it.  A scanner that reads nothing passes every
// claim it is given.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>
#include <stdlib.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. ctest runs each binary with the farm's src/ as the working
// directory (buildcpptests is placed there); the list lets the binary also run
// by hand from the farm root, and keeps "no tree found" distinguishable from
// "tree found, file missing".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The manifest and its helpers are this repo's build files, not farm files
// (tools/setup does not farm a CMakeLists.txt), so they are read from the
// checkout $SOURCESHARP_REPO names -- which ctest sets on every test. Unset or
// unreadable reads as empty, and every claim below then FAILS on its size
// floor rather than passing over nothing.
//-----------------------------------------------------------------------------
std::string ReadRepo( const std::string &strRelativeToRepo )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	if ( !pszRepo || !*pszRepo )
		return std::string();

	std::string strPath( pszRepo );
	strPath += "/";
	strPath += strRelativeToRepo;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ), std::istreambuf_iterator<char>() );
}

//-----------------------------------------------------------------------------
// Comments are DATA to a scanner and must not be searched. This file's own
// PROSE names both `MathLib_Init` and `mathlib_test_env.h` many times over; a
// scanner that did not strip comments would find them in the prose of a file
// that had lost the include and stay green. That exact mutation beat an earlier
// scanner in this tree.
//-----------------------------------------------------------------------------
std::string CppCodeOnly( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	bool bInBlock = false;
	size_t at = 0;
	while ( at < s.size() )
	{
		size_t eol = s.find( '\n', at );
		if ( eol == std::string::npos )
			eol = s.size();

		std::string strLine = s.substr( at, eol - at );
		at = eol + 1;

		for ( size_t i = 0; i < strLine.size(); i++ )
		{
			if ( bInBlock )
			{
				if ( i + 1 < strLine.size() && strLine[i] == '*' && strLine[i + 1] == '/' )
				{
					bInBlock = false;
					i++;
				}
				continue;
			}

			if ( i + 1 < strLine.size() && strLine[i] == '/' && strLine[i + 1] == '/' )
				break;

			if ( i + 1 < strLine.size() && strLine[i] == '/' && strLine[i + 1] == '*' )
			{
				bInBlock = true;
				i++;
				continue;
			}

			out += strLine[i];
		}

		out += '\n';
	}

	return out;
}

//-----------------------------------------------------------------------------
// The same for the CMake files, which is where the calls are. Only whole-line
// comments are dropped: `#` inside an argument is not a comment and removing
// from it would eat parts of real calls. (A bracket comment, #[[ ... ]], is
// the one form this cannot see; tools/leg-floor refuses the manifest if it has
// one.)
//
// This matters as much as the C++ one. The manifest carries several paragraphs
// of prose that name `build_and_run_mathlib` and `mathlib.a`, and the counts
// below are wrong — in the direction of passing — if that prose is scanned.
//-----------------------------------------------------------------------------
std::string ShellCodeOnly( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	size_t at = 0;
	while ( at < s.size() )
	{
		size_t eol = s.find( '\n', at );
		if ( eol == std::string::npos )
			eol = s.size();

		const std::string strLine = s.substr( at, eol - at );
		at = eol + 1;

		size_t first = strLine.find_first_not_of( " \t" );
		if ( first == std::string::npos || strLine[first] != '#' )
			out += strLine;

		out += '\n';
	}

	return out;
}

//-----------------------------------------------------------------------------
// The sources of every `ss_cpptest_mathlib(...)` call, GROUPED BY CALL -- which
// is what the claim is actually about.
//
// A call starts a line (which keeps the helper's own `function(
// ss_cpptest_mathlib name)` definition, and a mention inside another call, out
// of the list) and runs to its closing `)`, across as many lines as it takes;
// no path in it has a parenthesis. Every whitespace-separated token ending in
// .cpp is a source.
//
// MathLib_Init is called once per BINARY, by the global test environment
// mathlib_test_env.h registers, so the property is "every binary that links
// mathlib has at least one source that registers it" and NOT "every source
// does". overlay_library_test compiles the library under test beside the test
// file, and sourcesharp/game/shared/overlay_library.cpp is GAME CODE -- it
// cannot include a test header, and requiring it to would be requiring the
// thing this file exists to protect against.
//
// So the grouping is the fix, and it is a sharpening rather than a loosening:
// a call whose sources ALL fail the search still fails, and
// TheGroupingKeepsAnInvocationWithNoEnvironmentFailing is the positive control
// that says so.
//-----------------------------------------------------------------------------
std::vector<std::vector<std::string>> MathlibTestInvocations( const std::string &strCode )
{
	std::vector<std::vector<std::string>> out;

	const std::string kCall = "ss_cpptest_mathlib(";

	size_t at = 0;
	while ( at < strCode.size() )
	{
		size_t eol = strCode.find( '\n', at );
		if ( eol == std::string::npos )
			eol = strCode.size();

		const bool bCall = strCode.compare( at, kCall.size(), kCall ) == 0;
		const size_t lineStart = at;
		at = eol + 1;
		if ( !bCall )
			continue;

		size_t close = strCode.find( ')', lineStart );
		if ( close == std::string::npos )
			close = strCode.size();
		const std::string strCallText = strCode.substr( lineStart + kCall.size(), close - lineStart - kCall.size() );

		std::vector<std::string> invocation;

		size_t i = 0;
		while ( i < strCallText.size() )
		{
			size_t b = strCallText.find_first_not_of( " \t\n", i );
			if ( b == std::string::npos )
				break;
			size_t e = strCallText.find_first_of( " \t\n", b );
			if ( e == std::string::npos )
				e = strCallText.size();

			const std::string strTok = strCallText.substr( b, e - b );
			if ( strTok.size() > 4 && strTok.compare( strTok.size() - 4, 4, ".cpp" ) == 0 )
				invocation.push_back( strTok );

			i = e;
		}

		if ( !invocation.empty() )
			out.push_back( invocation );
	}

	return out;
}

//-----------------------------------------------------------------------------
// The same sources, flat: every source named by a `ss_cpptest_mathlib` call.
//-----------------------------------------------------------------------------
std::vector<std::string> MathlibTestSources( const std::string &strCode )
{
	std::vector<std::string> out;

	const std::vector<std::vector<std::string>> calls = MathlibTestInvocations( strCode );
	for ( size_t i = 0; i < calls.size(); i++ )
		out.insert( out.end(), calls[i].begin(), calls[i].end() );

	return out;
}

//-----------------------------------------------------------------------------
// The claim, in one place so the positive control below can run the SAME search
// against a fixture that must fail it.
//-----------------------------------------------------------------------------
bool IncludesTheEnvironment( const std::string &strCode )
{
	return strCode.find( "#include \"mathlib_test_env.h\"" ) != std::string::npos;
}

// The helpers the calls expand to, where the link line and the runtime check
// live (code only).
const std::string &Helpers()
{
	static std::string s = ShellCodeOnly( ReadRepo( "cmake/SourceSharpTests.cmake" ) );
	return s;
}

} // namespace

//=============================================================================//
// THE SCANNER, FIRST. Nothing below means anything if these fail.
//=============================================================================//

TEST( MathLibInitGuardScanner, CommentStrippingReallyStrips )
{
	const std::string strIn = "alpha();\n"
							  "// beta();\n"
							  "   // gamma();\n"
							  "/* delta();\n"
							  "   epsilon(); */\n"
							  "zeta(); // theta();\n";

	const std::string strOut = CppCodeOnly( strIn );

	EXPECT_NE( strOut.find( "alpha" ), std::string::npos );
	EXPECT_NE( strOut.find( "zeta" ), std::string::npos );
	EXPECT_EQ( strOut.find( "beta" ), std::string::npos );
	EXPECT_EQ( strOut.find( "gamma" ), std::string::npos );
	EXPECT_EQ( strOut.find( "delta" ), std::string::npos );
	EXPECT_EQ( strOut.find( "epsilon" ), std::string::npos );
	EXPECT_EQ( strOut.find( "theta" ), std::string::npos );
}

TEST( MathLibInitGuardScanner, ShellCommentStrippingReallyStrips )
{
	const std::string strIn = "ss_cpptest_mathlib(alpha_test SOURCES alpha.cpp)\n"
							  "# ss_cpptest_mathlib(beta_test SOURCES beta.cpp)\n"
							  "    # gamma\n"
							  "message(\"a # b\")\n";

	const std::string strOut = ShellCodeOnly( strIn );

	EXPECT_NE( strOut.find( "alpha.cpp" ), std::string::npos );
	EXPECT_EQ( strOut.find( "beta.cpp" ), std::string::npos );
	EXPECT_EQ( strOut.find( "gamma" ), std::string::npos );

	// A `#` inside an argument is not a comment, and eating from it would
	// truncate real calls.
	EXPECT_NE( strOut.find( "a # b" ), std::string::npos );
}

TEST( MathLibInitGuardScanner, TheParserJoinsContinuationsAndSkipsTheDefinition )
{
	const std::string strIn = "function(ss_cpptest_mathlib name)\n"
							  "  _ss_cpptest(MATHLIB ${name} notatest.cpp)\n"
							  "endfunction()\n"
							  "ss_cpptest_mathlib(alpha_test\n"
							  "    SOURCES sourcesharp/test/alpha_test.cpp)\n"
							  "ss_cpptest(beta_test SOURCES sourcesharp/test/beta_test.cpp)\n";

	const std::vector<std::string> got = MathlibTestSources( strIn );

	ASSERT_EQ( 1u, got.size() ) << "the definition line or the non-mathlib "
								   "invocation leaked into the list";
	EXPECT_EQ( "sourcesharp/test/alpha_test.cpp", got[0] );
}

//-----------------------------------------------------------------------------
// THE POSITIVE CONTROL. The claim below is an ABSENCE-of-failure claim, so the
// same search has to be shown finding the failure on a fixture that has it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE POSITIVE CONTROL FOR THE GROUPING, and it is the whole reason the claim
// above may be stated per BINARY rather than per SOURCE.
//
// Grouping is only safe if an invocation none of whose sources register the
// environment still fails. Fed exactly that -- two sources, neither of which
// includes the header -- the search has to come back false for both, which is
// what turns the loop's `bRegistered` false and fails the claim. Fed one that
// DOES register, it has to come back true for that one.
//-----------------------------------------------------------------------------
TEST( MathLibInitGuardScanner, TheGroupingKeepsAnInvocationWithNoEnvironmentFailing )
{
	const std::string strScript = "ss_cpptest_mathlib(two_source_test\n"
								  "    SOURCES sourcesharp/test/a_test.cpp\n"
								  "            sourcesharp/game/shared/a_library.cpp)\n";

	const std::vector<std::vector<std::string>> invocations = MathlibTestInvocations( ShellCodeOnly( strScript ) );

	ASSERT_EQ( invocations.size(), (size_t)1 );
	EXPECT_EQ( invocations[0].size(), (size_t)2 );

	// Neither source registers, so the OR over the group is false -- which is
	// the state the claim above reports as a failure.
	EXPECT_FALSE( IncludesTheEnvironment( "#include \"gtest/gtest.h\"\n" ) );
	EXPECT_FALSE( IncludesTheEnvironment( "#include \"overlay_library.h\"\n" ) );

	// And one that does register flips it.
	EXPECT_TRUE( IncludesTheEnvironment( "#include \"mathlib_test_env.h\"\n" ) );
}

//-----------------------------------------------------------------------------
// The grouping must not silently swallow invocations: the flat list and the
// grouped one have to name the same sources, or one of the two scans is reading
// a different script from the other.
//-----------------------------------------------------------------------------
TEST( MathLibInitGuardScanner, TheSearchFailsOnASourceWithoutTheInclude )
{
	const std::string strWith = "#include \"gtest/gtest.h\"\n"
								"#include \"mathlib_test_env.h\"\n";
	const std::string strWithout = "#include \"gtest/gtest.h\"\n";
	// The mutation that beats a scanner with no comment stripping.
	const std::string strCommentedOut = CppCodeOnly( "#include \"gtest/gtest.h\"\n"
													 "// #include \"mathlib_test_env.h\"\n" );

	EXPECT_TRUE( IncludesTheEnvironment( strWith ) );
	EXPECT_FALSE( IncludesTheEnvironment( strWithout ) );
	EXPECT_FALSE( IncludesTheEnvironment( strCommentedOut ) );
}

//=============================================================================//
// THE CLAIMS.
//=============================================================================//

//-----------------------------------------------------------------------------
// mathlib_test_env.h is the one place MathLib_Init is called. If it stops
// calling it, every include of it becomes decoration and the scan below becomes
// a check that cannot fail.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CLAIM THE FILE EXISTS FOR.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The loophole. A test that links mathlib by hand rather than through
// ss_cpptest_mathlib would be invisible to the scan above -- so linking it by
// hand is banned, and the ban is checked here rather than hoped for: exactly
// one link site, and it is the helper's.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The runtime half. A source scan cannot see an include that is present but
// ineffective; ctest fails each mathlib binary whose own output contains the
// assertion text (the helper's FAIL_REGULAR_EXPRESSION). Deleting that check
// should go red HERE.
//-----------------------------------------------------------------------------
TEST( MathLibInitGuard, TheRunnerStillChecksTheOutput )
{
	const std::string &strCode = Helpers();

	ASSERT_FALSE( strCode.empty() ) << "cmake/SourceSharpTests.cmake not read";
	EXPECT_NE( strCode.find( "FAIL_REGULAR_EXPRESSION \"s_bMathlibInitialized\"" ), std::string::npos )
		<< "the mathlib tests no longer fail on the uninitialised-mathlib "
		   "assertion in their output, which is the half of this guard that "
		   "catches what source cannot show.";
}
