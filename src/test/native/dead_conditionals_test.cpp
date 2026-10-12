//========= SourceSharp ======================================================//
//
// THE DEAD-CONDITIONAL CHECK, and the fixtures that prove it can fail.
//
// tools/dead-conditionals scans every compiled SourceSharp shim translation
// unit for conditional identifiers this build never defines. It exists because
// of what cdll_client_int.cpp arrived carrying when Phase 5 step 5b moved it:
// thirteen distinct conditional identifiers, of which exactly ONE (_DEBUG) is
// defined by this build -- nineteen references to TF_CLIENT_DLL, fifteen to
// REPLAY_ENABLED, six to SIXENSE, fifty-six references in all to code that
// cannot execute. Phase 7 ports a lot more stock files and every one of them
// will arrive the same way. Fifty-six is not a number anybody catches by eye.
//
// THREE KINDS OF FACT LIVE HERE, and the middle one is the load-bearing one.
//
//   1. THE TREE IS CLEAN. tools/dead-conditionals exits 0 over the real tree.
//      This is the fact that fails when a newly ported file arrives dirty.
//
//   2. THE CHECK CAN FAIL, AND FAILS FOR THE RIGHT REASON. Every fixture below
//      drives the tool over a scratch tree it builds itself: a mod guard is
//      reported naming file and define; an allowlisted portability guard
//      standing NEXT TO IT in the same file is not; a live define is not; each
//      of the three staleness rules fires. Without these, "exits 0" is a check
//      that cannot fail -- and this project has counted forty-three of those.
//
//   3. THE ALLOWLIST IS THE RIGHT SHAPE. WIN32 is allowed and says why; no mod
//      guard is. An allowlist is the obvious way to make a failing check pass,
//      and the one thing that must never be waived is the thing the check is
//      for.
//
// WHY A GTEST DRIVING A SCRIPT rather than a new test leg: this is exactly
// entity_census_test's shape -- a tool in tools/, run by popen from the native
// leg, with fixtures for the states it must refuse -- and the native leg
// already runs source-reading checks over the shim. A new leg would need its
// own tools/leg-floor row and its own line in `make test` for no new coverage.
//
//===========================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#include <stdio.h>
#include <stdlib.h>

#include <map>
#include <string>
#include <utility>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the worktree root. The test runner runs each binary with src/ as the
// working directory, and that src/ is the compat farm's (farm/<config>/src), so
// ".." is the farm -- the old single-repo root, tools/ link included, and the
// CMake build (build/) the tool reads by default beside it. entity_census_test's list,
// for its reasons.
//-----------------------------------------------------------------------------
const char *WorktreeRoot()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string szProbe = std::string( kCandidates[ i ] ) + "/tools/dead-conditionals";
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

std::string ReadFile( const std::string &szPath )
{
	FILE *pFile = fopen( szPath.c_str(), "rb" );
	if ( !pFile )
		return std::string();

	std::string szOut;
	char rgchBuf[ 8192 ];
	size_t cRead;
	while ( ( cRead = fread( rgchBuf, 1, sizeof( rgchBuf ), pFile ) ) > 0 )
		szOut.append( rgchBuf, cRead );
	fclose( pFile );
	return szOut;
}

bool WriteFile( const std::string &szPath, const std::string &szBody )
{
	FILE *pFile = fopen( szPath.c_str(), "wb" );
	if ( !pFile )
		return false;
	const size_t cWrote = fwrite( szBody.data(), 1, szBody.size(), pFile );
	fclose( pFile );
	return cWrote == szBody.size();
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

bool Contains( const std::string &szHaystack, const char *pszNeedle )
{
	return szHaystack.find( pszNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// Every `#if / #ifdef / #ifndef / #elif` line of a file, comments stripped and
// one per line -- the same slice tools/dead-conditionals looks at. Prose that
// merely MENTIONS a define is not a conditional and must not read as one.
//-----------------------------------------------------------------------------
std::string ConditionalLines( const std::string &szText )
{
	std::string szOut;
	size_t at = 0;
	while ( at < szText.size() )
	{
		size_t nEnd = szText.find( '\n', at );
		if ( nEnd == std::string::npos )
			nEnd = szText.size();

		std::string szLine = szText.substr( at, nEnd - at );
		at = nEnd + 1;

		size_t nFirst = szLine.find_first_not_of( " \t" );
		if ( nFirst == std::string::npos || szLine[ nFirst ] != '#' )
			continue;

		const size_t nWord = szLine.find_first_not_of( " \t", nFirst + 1 );
		if ( nWord == std::string::npos )
			continue;

		const std::string szRest = szLine.substr( nWord );
		if ( szRest.compare( 0, 3, "if " ) != 0 &&
		     szRest.compare( 0, 6, "ifdef " ) != 0 &&
		     szRest.compare( 0, 7, "ifndef " ) != 0 &&
		     szRest.compare( 0, 5, "elif " ) != 0 )
			continue;

		const size_t nComment = szRest.find( "//" );
		szOut += ( nComment == std::string::npos ) ? szRest : szRest.substr( 0, nComment );
		szOut += "\n";
	}
	return szOut;
}

const char *const kTool   = "tools/dead-conditionals";
const char *const kLedger = "tools/dead-conditionals-ledger";

// The exit codes the tool promises. 2 is "I could not take the measurement",
// and it is a DIFFERENT thing from 1, "I took it and it is bad". A test that
// accepted "non-zero" for both would pass when the scan turned into a crash.
const int kExitClean   = 0;
const int kExitFindings = 1;
const int kExitRefused = 2;

//=============================================================================
// A SCRATCH TREE, laid out the way the tool expects: <root>/src/sourcesharp
// with the fixture sources in it, and beside it <root>/build, a CMake build
// dir holding only the compile_commands.json the tool reads (one entry per TU,
// its object under CMakeFiles/<target>.dir/, its command line carrying the
// target's -D set). The ledger goes wherever we like; it is passed on the
// command line. The fixture targets are not ones tools/cmakegraph.py knows a
// CMakeLists list for, so the tool reads them as given.
//
// Every fixture database also carries one TU OUTSIDE sourcesharp/, so a
// fixture with no shim source is a database that names no shim source -- not
// an empty database, which is refused on a different path.
//
// The tool reads EVERY #define under <root>/src to decide whether a header
// somewhere provides a name. A fixture tree is therefore the only place these
// facts can be stated precisely: over the real tree, "not reported" could mean
// "allowlisted" or "some unrelated header spells it", and the two are not the
// same claim at all.
//=============================================================================
class Fixture
{
public:
	explicit Fixture( const char *pszName )
	{
		char rgchBuf[ 256 ];
		snprintf( rgchBuf, sizeof( rgchBuf ), "/tmp/dead_conditionals_test_%d_%s",
		          (int)getpid(), pszName );
		m_szRoot = rgchBuf;

		Shell( std::string( "rm -rf '" ) + m_szRoot + "'" );
		Shell( std::string( "mkdir -p '" ) + m_szRoot + "/src/sourcesharp'" );
	}

	~Fixture()
	{
		Shell( std::string( "rm -rf '" ) + m_szRoot + "'" );
	}

	// One compiled TU under sourcesharp/, in one project (CMake target).
	void Source( const char *pszProject, const char *pszName, const std::string &szBody )
	{
		EXPECT_TRUE( WriteFile( m_szRoot + "/src/sourcesharp/" + pszName, szBody ) );
		m_vecSources.push_back( std::make_pair( std::string( pszProject ),
		                                        std::string( "sourcesharp/" ) + pszName ) );
	}

	void Defines( const char *pszProject, const std::string &szFlags )
	{
		m_mapDefines[ pszProject ] = szFlags;
	}

	void Ledger( const std::string &szBody )
	{
		EXPECT_TRUE( WriteFile( m_szRoot + "/ledger", szBody ) );
	}

	Run_t Scan()
	{
		Shell( std::string( "mkdir -p '" ) + m_szRoot + "/build'" );

		std::vector< std::pair< std::string, std::string > > vecAll = m_vecSources;
		vecAll.push_back( std::make_pair( std::string( "other_fixture" ),
		                                  std::string( "game/not_a_shim.cpp" ) ) );

		std::string szDb = "[\n";
		for ( size_t i = 0; i < vecAll.size(); i++ )
		{
			const std::string &szProject = vecAll[ i ].first;
			const std::string szFile = m_szRoot + "/src/" + vecAll[ i ].second;
			const std::string szObject = "CMakeFiles/" + szProject + ".dir/" + vecAll[ i ].second + ".o";
			std::map< std::string, std::string >::const_iterator it = m_mapDefines.find( szProject );
			const std::string szFlags = ( it == m_mapDefines.end() ) ? std::string() : it->second;

			szDb += std::string( i ? ",\n" : "" ) +
				"{\"directory\": \"" + m_szRoot + "/build\", " +
				"\"command\": \"c++ " + szFlags + " -o " + szObject + " -c " + szFile + "\", " +
				"\"file\": \"" + szFile + "\", " +
				"\"output\": \"" + szObject + "\"}";
		}
		szDb += "\n]\n";
		EXPECT_TRUE( WriteFile( m_szRoot + "/build/compile_commands.json", szDb ) );

		struct stat st;
		if ( stat( ( m_szRoot + "/ledger" ).c_str(), &st ) != 0 )
			Ledger( "" );

		return RunAtRoot( std::string( kTool ) +
		                  " --root '" + m_szRoot + "'" +
		                  " --build-dir '" + m_szRoot + "/build'" +
		                  " --ledger '" + m_szRoot + "/ledger'" );
	}

	const std::string &Root() const { return m_szRoot; }

private:
	static void Shell( const std::string &szCommand )
	{
		const int nRet = system( szCommand.c_str() );
		(void)nRet;
	}

	std::string m_szRoot;
	std::map< std::string, std::string > m_mapDefines;
	std::vector< std::pair< std::string, std::string > > m_vecSources;
};

//=============================================================================
// (0) The subjects exist. Every other fact here would be vacuously true if the
//     tool or the ledger were missing, and a suite that passes on a missing
//     subject is the failure mode this whole directory is about.
//=============================================================================

TEST( DeadConditionals, TheWorktreeRootIsFound )
{
	ASSERT_TRUE( WorktreeRoot() != NULL )
		<< "No directory among '.', '..', '../..' holds tools/dead-conditionals.";
}

TEST( DeadConditionals, TheToolIsExecutable )
{
	const std::string szPath = RootPath( kTool );
	ASSERT_FALSE( szPath.empty() );
	struct stat st;
	ASSERT_EQ( 0, stat( szPath.c_str(), &st ) ) << szPath;
	EXPECT_TRUE( ( st.st_mode & S_IXUSR ) != 0 ) << szPath << " is not executable";
}

TEST( DeadConditionals, TheLedgerExists )
{
	EXPECT_FALSE( ReadFile( RootPath( kLedger ) ).empty() )
		<< kLedger << " is missing or empty. The tool refuses to run without it.";
}

//=============================================================================
// (1) THE TREE IS CLEAN. This is the fact a dirty Phase 7 port fails.
//=============================================================================

TEST( DeadConditionals, TheShimCarriesNoUnledgeredDeadConditional )
{
	const Run_t run = RunAtRoot( kTool );
	EXPECT_EQ( kExitClean, run.m_nExit )
		<< run.m_szOutput
		<< "\nA conditional this build never defines is dead code. Delete it,\n"
		   "keeping the arm the build takes -- or, if it is legitimately\n"
		   "conditional here, add a row to " << kLedger << " saying why.";
}

TEST( DeadConditionals, TheScanSawTheWholeShimAndNotAHandfulOfIt )
{
	const Run_t run = RunAtRoot( kTool );
	ASSERT_EQ( kExitClean, run.m_nExit ) << run.m_szOutput;

	// A scan that matched almost nothing would also exit 0, which is precisely
	// the "check that cannot fail" shape. The shim is well over a hundred
	// compiled translation units; a run reporting a handful has a broken
	// object-table parse, not a clean tree.
	int nTUs = 0;
	const size_t at = run.m_szOutput.find( "dead-conditionals: " );
	ASSERT_NE( std::string::npos, at ) << run.m_szOutput;
	EXPECT_EQ( 1, sscanf( run.m_szOutput.c_str() + at,
	                      "dead-conditionals: %d compiled shim TU(s)", &nTUs ) )
		<< run.m_szOutput;
	EXPECT_GT( nTUs, 100 ) << run.m_szOutput;
}

//=============================================================================
// (2) THE CHECK CAN FAIL, AND FOR THE RIGHT REASON.
//=============================================================================

TEST( DeadConditionals, AModGuardIsReportedNamingFileAndDefine )
{
	Fixture fx( "modguard" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL -DLINUX" );
	fx.Source( "client_fixture", "ported.cpp",
	           "#ifdef TF_CLIENT_DLL\n"
	           "void OnlyInTeamFortress();\n"
	           "#endif\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitFindings, run.m_nExit ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "sourcesharp/ported.cpp" ) ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "TF_CLIENT_DLL" ) ) << run.m_szOutput;
}

TEST( DeadConditionals, AnAllowlistedPortabilityGuardIsNotReported )
{
	// THE ALLOWLIST'S OWN JOB, stated over a file that carries BOTH kinds of
	// guard. WIN32 is dead here only because this build targets linux64; the
	// surrounding SDK still carries Windows. TF_CLIENT_DLL is dead in a
	// different way -- this is not that game. The check must tell them apart,
	// or Phase 7 turns it off on the first file that has a WIN32 in it.
	Fixture fx( "allowlisted" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL -DLINUX" );
	fx.Source( "client_fixture", "ported.cpp",
	           "#if defined( WIN32 )\n"
	           "void OnWindows();\n"
	           "#endif\n"
	           "#ifdef TF_CLIENT_DLL\n"
	           "void OnlyInTeamFortress();\n"
	           "#endif\n" );
	fx.Ledger( "allow\tWIN32\tportability, not a mod guard\n" );

	const Run_t run = fx.Scan();
	EXPECT_FALSE( Contains( run.m_szOutput, "WIN32 is never defined" ) )
		<< "the allowlist did not waive WIN32:\n" << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "TF_CLIENT_DLL" ) )
		<< "waiving WIN32 also silenced the mod guard beside it:\n" << run.m_szOutput;
	EXPECT_EQ( kExitFindings, run.m_nExit ) << run.m_szOutput;
}

TEST( DeadConditionals, ADefineTheBuildCarriesIsNotReported )
{
	Fixture fx( "live" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL -DSOURCESHARP" );
	fx.Source( "client_fixture", "ported.cpp",
	           "#ifdef SOURCESHARP\n"
	           "void Live();\n"
	           "#endif\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitClean, run.m_nExit ) << run.m_szOutput;
}

TEST( DeadConditionals, ASideMarkerIsJudgedAgainstItsOwnProjectsCommandLine )
{
	// A shared file is compiled into BOTH projects. CLIENT_DLL is on the
	// client's command line only, so from the server's compile it looks
	// undefined -- correctly. Merging the two -D lists would call it live
	// everywhere and hide a real finding on the other side; judging per
	// project is what makes the allowlist row for it necessary and honest.
	Fixture fx( "sidemarker" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL" );
	fx.Defines( "server_fixture", "-DGAME_DLL" );
	fx.Source( "client_fixture", "shared.cpp",
	           "#ifdef CLIENT_DLL\n"
	           "void OnTheClient();\n"
	           "#endif\n" );
	fx.Source( "server_fixture", "shared.cpp",
	           "#ifdef CLIENT_DLL\n"
	           "void OnTheClient();\n"
	           "#endif\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitFindings, run.m_nExit )
		<< "the server's compile of a shared file sees CLIENT_DLL as undefined "
		   "and the tool must say so unless a row waives it:\n" << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "CLIENT_DLL" ) ) << run.m_szOutput;
}

TEST( DeadConditionals, ACommentOnADirectiveLineIsNotAnIdentifier )
{
	// The first pass of this scanner reported "the", "to" and "a" as undefined
	// macros, out of `#endif // ... the ...` trailers. A checker whose report
	// is mostly noise gets switched off.
	Fixture fx( "comments" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL" );
	fx.Source( "client_fixture", "ported.cpp",
	           "#ifdef CLIENT_DLL // because the client is the one that draws\n"
	           "void Draw();\n"
	           "#endif\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitClean, run.m_nExit ) << run.m_szOutput;
}

TEST( DeadConditionals, AFileThatDefinesItsOwnSwitchIsNotReported )
{
	Fixture fx( "ownswitch" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL" );
	fx.Source( "client_fixture", "ported.cpp",
	           "#define SHELL_LOCAL_SWITCH 1\n"
	           "#ifdef SHELL_LOCAL_SWITCH\n"
	           "void Local();\n"
	           "#endif\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitClean, run.m_nExit ) << run.m_szOutput;
}

//=============================================================================
// (3) THE THREE STALENESS RULES. An allowlist without them is the trap the
//     integrator named: an entry that has quietly stopped meaning anything.
//=============================================================================

TEST( DeadConditionals, APendingRowForASiteThatIsGoneIsStale )
{
	Fixture fx( "stalepending" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL" );
	fx.Source( "client_fixture", "ported.cpp", "void Clean();\n" );
	fx.Ledger( "pending\tsourcesharp/ported.cpp\tTF_CLIENT_DLL\tcleaned up, row left behind\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitFindings, run.m_nExit ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "STALE ledger row" ) ) << run.m_szOutput;
}

TEST( DeadConditionals, AnAllowRowNothingUsesIsStale )
{
	Fixture fx( "staleallowunused" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL" );
	fx.Source( "client_fixture", "ported.cpp", "void Clean();\n" );
	fx.Ledger( "allow\tSIXENSE\tnothing conditions on this any more\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitFindings, run.m_nExit ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "STALE allow row" ) ) << run.m_szOutput;
}

TEST( DeadConditionals, AnAllowRowTheBuildDefinesEverywhereIsStale )
{
	// The dangerous kind: a guard that became LIVE while its waiver stayed.
	// After that, the check is silent about a conditional that really does
	// have two arms, which is the opposite of what it is for.
	Fixture fx( "staleallowlive" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL -DNOW_LIVE" );
	fx.Source( "client_fixture", "ported.cpp",
	           "#ifdef NOW_LIVE\n"
	           "void Live();\n"
	           "#endif\n" );
	fx.Ledger( "allow\tNOW_LIVE\tstale: the build defines this now\n" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitFindings, run.m_nExit ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "DOES define" ) ) << run.m_szOutput;
}

//=============================================================================
// (4) CANNOT MEASURE IS NOT A PASS.
//=============================================================================

TEST( DeadConditionals, AMissingObjectTableIsRefusedRatherThanReportedClean )
{
	const Run_t run = RunAtRoot( std::string( kTool ) +
	                             " --build-dir /nonexistent/build" );
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "CANNOT MEASURE" ) ) << run.m_szOutput;
}

TEST( DeadConditionals, AnObjectTableNamingNoShimSourceIsRefused )
{
	Fixture fx( "emptytable" );
	fx.Defines( "client_fixture", "-DCLIENT_DLL" );

	const Run_t run = fx.Scan();
	EXPECT_EQ( kExitRefused, run.m_nExit ) << run.m_szOutput;
	EXPECT_TRUE( Contains( run.m_szOutput, "CANNOT MEASURE" ) ) << run.m_szOutput;
}

//=============================================================================
// (5) THE REAL ALLOWLIST IS THE RIGHT SHAPE. Allowlisting is the obvious way
//     to make this check pass, so the shape of the list is itself pinned.
//=============================================================================

TEST( DeadConditionals, WIN32IsAllowedAndSaysWhy )
{
	const std::string szLedger = ReadFile( RootPath( kLedger ) );
	ASSERT_FALSE( szLedger.empty() );

	EXPECT_TRUE( Contains( szLedger, "allow\tWIN32\t" ) )
		<< "WIN32 must be allowed: it is a portability guard, not a mod guard, "
		   "and a check that flags it will be switched off on the first Phase 7 "
		   "file that has one.";
	EXPECT_TRUE( Contains( szLedger, "portability" ) )
		<< "the WIN32 row must carry its reason, not just the name";
}

TEST( DeadConditionals, NoModGuardIsAllowed )
{
	// The one thing that must never be waived is the thing the check is for.
	// These four are what cdll_client_int.cpp arrived carrying.
	static const char *const kModGuards[] = {
		"allow\tTF_CLIENT_DLL", "allow\tTF_DLL",
		"allow\tSIXENSE", "allow\tREPLAY_ENABLED",
		"allow\tPORTAL", "allow\tHL1MP_CLIENT_DLL",
	};

	const std::string szLedger = ReadFile( RootPath( kLedger ) );
	ASSERT_FALSE( szLedger.empty() );

	for ( size_t i = 0; i < sizeof( kModGuards ) / sizeof( kModGuards[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Contains( szLedger, kModGuards[ i ] ) )
			<< kModGuards[ i ] << " is a MOD guard for a game this is not. "
			   "Waiving it defeats the check; delete the conditional instead.";
	}
}

//=============================================================================
// (6) 5b-split's own result, pinned. The eleven identifiers it yanked out of
//     shell_clientdll.cpp must not come back, and WIN32 -- which it deliberately
//     KEPT -- must still be there.
//=============================================================================

TEST( DeadConditionals, TheClientShellCarriesNoneOfTheElevenYankedIdentifiers )
{
	static const char *const kYanked[] = {
		"TF_CLIENT_DLL", "REPLAY_ENABLED", "SIXENSE", "USES_ECON_ITEMS",
		"NO_STEAM", "_X360", "PORTAL", "HL1MP_CLIENT_DLL", "_XBOX",
		"DEMOPOLISH_ENABLED", "NO_ENTITY_PREDICTION",
	};

	const std::string szShell =
		ReadFile( RootPath( "src/sourcesharp/game/client/shell_clientdll.cpp" ) );
	ASSERT_FALSE( szShell.empty() );

	// THE CONDITIONAL LINES ONLY, and that is not a weakening. The file's
	// refusal warning QUOTES two of these names in prose -- it says the two
	// refused methods' bodies were "entirely inside #if defined( TF_CLIENT_DLL )"
	// -- and a whole-file substring search would fail on the explanation for
	// why the code is gone. What must not come back is a DIRECTIVE.
	const std::string szDirectives = ConditionalLines( szShell );
	EXPECT_FALSE( szDirectives.empty() )
		<< "shell_clientdll.cpp has no conditional directives at all, so this "
		   "test would pass whatever the file said. It should still have its "
		   "two WIN32 guards and its two _DEBUG ones.";

	for ( size_t i = 0; i < sizeof( kYanked ) / sizeof( kYanked[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Contains( szDirectives, kYanked[ i ] ) )
			<< kYanked[ i ] << " is back in a conditional in shell_clientdll.cpp. "
			   "This build defines none of the eleven; keep the arm the build "
			   "takes.\nThe directives are:\n" << szDirectives;
	}
}

TEST( DeadConditionals, TheClientShellKeepsItsWIN32Guards )
{
	// The reverse of the fact above, and it is not redundant with it: a lane
	// that "cleaned up" WIN32 too would pass the deletion test and quietly drop
	// Windows portability the surrounding SDK still carries.
	const std::string szShell =
		ReadFile( RootPath( "src/sourcesharp/game/client/shell_clientdll.cpp" ) );
	ASSERT_FALSE( szShell.empty() );

	int nGuards = 0;
	for ( size_t at = szShell.find( "#if defined( WIN32 )" );
	      at != std::string::npos;
	      at = szShell.find( "#if defined( WIN32 )", at + 1 ) )
	{
		nGuards++;
	}
	EXPECT_EQ( 2, nGuards )
		<< "shell_clientdll.cpp had two WIN32 guards -- ConnectHaptics in Init "
		   "and DisconnectHaptics in Shutdown -- and they are portability, not "
		   "dead mod code.";
}

} // namespace
