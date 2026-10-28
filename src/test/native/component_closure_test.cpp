//====== SourceSharp — the hermetic half of tools/4f-component ================//
//
// `tools/4f-component` answers "which translation units must leave a vpc
// TOGETHER". Step 4f built it because plan_entity.md's Phase 4 is written as
// eight independent steps and the link graph is not a DAG at that granularity:
// 4a, 4c, 4d, 4e, 4f and the Phase 2 and Phase 3 exits are one connected
// component, so none of them can take its stock TU out of server_csharp.vpc on
// its own. That is the measurement a Phase 4 exit lane has to be scoped by, and
// a tool that produced it wrongly would mis-scope a lane rather than fail.
//
// SAME ARGUMENT entity_gates_test.cpp makes for entity-gates, and the same
// shape: the tool's real mode reads a 166-object link and cannot be shown to be
// right from its own output, so the algebra is driven over a FIXTURE and this
// file is what makes `make check` run it. Without this, `--self-test` is a mode
// nobody invokes.
//
// THREE PROPERTIES, and each is a way a measuring tool fails silently:
//
//   1. the fixture checks pass, AND THERE ARE SOME. A self-test that ran zero
//      checks prints "0 failure(s)" and exits 0, which is the finished answer
//      from a tool that measured nothing -- the same false green entity-gates
//      refuses a stripped binary for.
//   2. a REFUSAL IS NOT A PASS. Handed a seed the side does not compile, the
//      tool must exit 2 rather than close it to the empty set and report a
//      clean separation. A closure of nothing is exactly what "this step is
//      independent" looks like.
//   3. `--hops 1` is a SUBSET of the fixpoint on the real tree. Those two
//      numbers are what the report quotes -- 50 break immediately, 130 in
//      total -- and if the bound could name something the fixpoint does not,
//      one of them is wrong. RED, not skipped, when there is no build to read:
//      the live gate's silent SKIP downgrade is on this project's list of
//      checks that could not fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <cstdio>
#include <sys/stat.h>
#include <sys/wait.h>
#include "gtest/gtest.h"

namespace
{

// The farm root (farm/<config>): the directory holding tools/4f-component,
// whose default --build-dir is `build` under it, the CMake build.
const char *Root()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string tool = std::string( kCandidates[i] ) + "/tools/4f-component";
		if ( stat( tool.c_str(), &st ) == 0 && S_ISREG( st.st_mode ) )
			return kCandidates[i];
	}
	return NULL;
}

struct Run_t
{
	int nExit;
	bool bRan;
	std::string strOut;
};

// popen gives the wait(2) status, so the exit code needs unpacking; a raw
// comparison against 2 there is off by a factor of 256 and passes nothing.
Run_t RunTool( const std::string &strArgs )
{
	Run_t r;
	r.nExit = -1;
	r.bRan = false;
	const char *pszRoot = Root();
	if ( !pszRoot )
		return r;

	const std::string cmd = "cd " + std::string( pszRoot ) + " && tools/4f-component " + strArgs + " 2>&1";
	FILE *pPipe = popen( cmd.c_str(), "r" );
	if ( !pPipe )
		return r;

	char buf[512];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		r.strOut += buf;

	const int status = pclose( pPipe );
	if ( WIFEXITED( status ) )
	{
		r.nExit = WEXITSTATUS( status );
		r.bRan = true;
	}
	return r;
}

bool HaveBuild()
{
	const char *pszRoot = Root();
	if ( !pszRoot )
		return false;
	struct stat st;
	const std::string ninja = std::string( pszRoot ) + "/build/build.ninja";
	const std::string db = std::string( pszRoot ) + "/build/compile_commands.json";
	return stat( ninja.c_str(), &st ) == 0 && stat( db.c_str(), &st ) == 0;
}

std::set<std::string> SourcesOf( const std::string &strTsv )
{
	std::set<std::string> out;
	size_t at = 0;
	while ( at < strTsv.size() )
	{
		const size_t eol = strTsv.find( '\n', at );
		const std::string line = strTsv.substr( at, ( eol == std::string::npos ? strTsv.size() : eol ) - at );
		at = ( eol == std::string::npos ) ? strTsv.size() : eol + 1;
		if ( line.compare( 0, 8, "closure\t" ) != 0 )
			continue;
		// closure<TAB>role<TAB>source<TAB>count
		const size_t a = line.find( '\t' );
		const size_t b = line.find( '\t', a + 1 );
		const size_t c = line.find( '\t', b + 1 );
		if ( b == std::string::npos || c == std::string::npos )
			continue;
		out.insert( line.substr( b + 1, c - b - 1 ) );
	}
	return out;
}

} // namespace

TEST( ComponentClosure, TheToolIsWhereThisTestExpectsIt )
{
	ASSERT_TRUE( Root() != NULL ) << "no tools/4f-component above the working directory; every fact below "
									 "would report a tool that cannot be run as a tool that passed";
}

TEST( ComponentClosure, TheFixtureChecksPassAndThereAreSome )
{
	const Run_t r = RunTool( "--self-test" );
	ASSERT_TRUE( r.bRan ) << "could not run tools/4f-component --self-test";
	EXPECT_EQ( r.nExit, 0 ) << "tools/4f-component --self-test failed:\n" << r.strOut;

	EXPECT_NE( r.strOut.find( "0 failure(s)" ), std::string::npos ) << "the self-test reported failures:\n" << r.strOut;

	// AND THERE ARE SOME. "0 check(s), 0 failure(s)" is exit 0 from a tool that
	// measured nothing, and it is the shape this project has found 24 times.
	EXPECT_EQ( r.strOut.find( " 0 check(s)" ), std::string::npos )
		<< "the self-test ran ZERO checks and still reported success:\n"
		<< r.strOut;
	EXPECT_NE( r.strOut.find( "check(s)" ), std::string::npos )
		<< "the self-test printed no check count at all; its report line has "
		   "been reworded and this fact has stopped reading anything";
}

TEST( ComponentClosure, ASeedThisSideDoesNotCompileIsRefusedNotClosed )
{
	// The dangerous false green: a closure of nothing reads exactly like "this
	// step is independent of everything else", which is the answer step 4f
	// exists to disprove.
	const Run_t r = RunTool( "game/server/no_such_file_4f.cpp" );
	ASSERT_TRUE( r.bRan ) << "could not run tools/4f-component";
	EXPECT_EQ( r.nExit, 2 ) << "a seed the server project does not compile must be REFUSED (exit 2), "
							   "not closed to the empty set. Got exit "
							<< r.nExit << ":\n"
							<< r.strOut;
	EXPECT_NE( r.strOut.find( "not compiled" ), std::string::npos ) << "the refusal does not say what was wrong:\n"
																	<< r.strOut;
}

TEST( ComponentClosure, TheBoundedClosureIsASubsetOfTheFixpoint )
{
	// A HARD REQUIREMENT rather than a skip, deliberately. This fact needs a
	// build to read, the native leg always has one, and this project has
	// recorded a live gate whose SILENT SKIP downgrade was one of its checks
	// that could not fail. If there is no build, say so and be red.
	ASSERT_TRUE( HaveBuild() ) << "no build/build.ninja + build/compile_commands.json under the farm "
								  "-- run `make debug` first. Passing here without a build "
								  "would be a check on nothing.";

	// THE SEED MOVED, AND THE PROPERTY UNDER TEST DID NOT.
	//
	// These two runs used to take the tool's DEFAULT seed, `SEED_4F`, whose first
	// entry is `game/server/baseentity.cpp`. THE FLIP TOOK THAT ROW OUT OF
	// server_csharp.vpc, so the tool now REFUSES the default with exit 2 -- "not
	// compiled into the server project" -- and this fact was reading a refusal
	// rather than a closure.
	//
	// The property this test is about is an ALGEBRAIC one and is seed-independent:
	// a bounded closure can never name something the unbounded closure does not.
	// So the assertion is untouched and only the seed moves, to the PORT'S OWN
	// standalone entity shell -- which is what `baseentity.cpp` was replaced BY,
	// and which no flip can take without there being no server entity at all. It
	// closes over 26 shim blockers at one hop, so the subset below is measured
	// over a real set rather than over two empty ones.
	//
	// The tool's own default is a separate file's problem; see the explicit
	// absence asserted underneath, which is what says so in this suite.
	const char *const kSeed = "sourcesharp/game/server/shell_standalone.cpp";

	const Run_t hop = RunTool( std::string( "--hops 1 --tsv " ) + kSeed );
	const Run_t all = RunTool( std::string( "--tsv " ) + kSeed );
	ASSERT_TRUE( hop.bRan && all.bRan );
	ASSERT_EQ( hop.nExit, 0 ) << hop.strOut;
	ASSERT_EQ( all.nExit, 0 ) << all.strOut;

	const std::set<std::string> setHop = SourcesOf( hop.strOut );
	const std::set<std::string> setAll = SourcesOf( all.strOut );

	ASSERT_FALSE( setHop.empty() ) << "`--hops 1` named no blocker at all for " << kSeed
								   << ". It breaks 26 "
									  "shim TUs immediately on this tree; an empty answer means the parse "
									  "below is reading nothing";
	EXPECT_LE( setHop.size(), setAll.size() );

	for ( std::set<std::string>::const_iterator it = setHop.begin(); it != setHop.end(); ++it )
	{
		EXPECT_TRUE( setAll.count( *it ) > 0 ) << *it
											   << " is in the `--hops 1` answer but not in the fixpoint. "
												  "The bound cannot name something the unbounded closure does not; "
												  "one of the two numbers the handover quotes is wrong.";
	}
}

//-----------------------------------------------------------------------------
// AND WHAT THE FLIP DID TO THE TOOL'S OWN SEED, SAID OUT LOUD RATHER THAN WORKED
// AROUND.
//
// `TheBoundedClosureIsASubsetOfTheFixpoint` above stopped using the default seed
// because the default seed stopped being compiled. That is a fact about this
// tree, not a detail of how this test invokes a script, and leaving it only as a
// comment is how a stale instrument survives: the suite would be green and
// nothing would record that tools/4f-component's `SEED_4F` names a file the
// server project has not compiled since the flip.
//
// It is asserted through the tool's OWN refusal path rather than by reading the
// build graph again here, so what is pinned is exactly what the next caller of
// the tool will hit. `ASeedThisSideDoesNotCompileIsRefusedNotClosed` proves the
// same exit code is reachable for a file that never existed; this one proves it
// for the file that used to be the seed, which is the case that matters.
//-----------------------------------------------------------------------------
TEST( ComponentClosure, TheOldFourFSeedIsNotCompiledAnyMoreAndTheToolSaysSo )
{
	ASSERT_TRUE( HaveBuild() ) << "no build/build.ninja + build/compile_commands.json under the farm "
								  "-- run `make debug` first.";

	const Run_t r = RunTool( "--hops 1 --tsv game/server/baseentity.cpp" );
	ASSERT_TRUE( r.bRan ) << "could not run tools/4f-component";

	EXPECT_EQ( r.nExit, 2 ) << "game/server/baseentity.cpp is compiled into the server project "
							   "again. The flip removed its vpc row with the rest of the entity "
							   "hierarchy; if it is back, tools/4f-component's SEED_4F default is "
							   "live again and the seed the test above uses is the workaround, not "
							   "this. Got exit "
							<< r.nExit << ":\n"
							<< r.strOut;
	EXPECT_NE( r.strOut.find( "not compiled" ), std::string::npos ) << "the refusal does not say what was wrong:\n"
																	<< r.strOut;

	// THE OTHER HALF, so this is not a one-sided "everything is gone". The tool
	// still measures: the shim that replaced the seed closes normally, and a
	// tool that refused EVERY seed would satisfy the exit-2 above for free.
	const Run_t ok = RunTool( "--hops 1 --tsv sourcesharp/game/server/shell_standalone.cpp" );
	ASSERT_TRUE( ok.bRan );
	EXPECT_EQ( ok.nExit, 0 ) << "tools/4f-component refuses the port's own shell_standalone.cpp too, "
								"so the refusal above is the tool being broken rather than the seed "
								"being gone:\n"
							 << ok.strOut;
}
