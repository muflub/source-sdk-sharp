//========= SourceSharp — gamemovement.cpp leaves the build ==================//
//
// plan_entity.md step 8f-remove, the last of the OWNER RULING's three parts.
// 8f-movetypes gave the three declared refusals managed bodies; 8f-igamemovement
// moved the IGameMovement publication onto a standalone CShellGameMovement and
// deleted the "managed has not started" fallback; this step takes the file out
// of the build.
//
// IT IS TWO `$File` ROWS AND THAT IS THE POINT. The two lanes before it were
// arranged so that the removal needs no other edit: nothing in the shim names a
// symbol gamemovement.cpp defines, so the file's going leaves nothing undefined.
// The rows were in the SERVER vpc and in the CLIENT vpc (now server_sourcesharp
// and client_sourcesharp, src/{server,client}/native/CMakeLists.txt), and a ONE-SIDED removal
// is silently wrong — the side that kept the row still links a complete
// CGameMovement, sourcesharp_movement_managed still selects it there, and the
// two sides then run different movement code under prediction. That is a
// divergence the game does not announce.
//
// WHY THESE ARE SOURCE SCANS. Everything this step can get wrong is invisible to
// a game that plays correctly:
//
//   * a one-sided removal        — both sides start, both move, they disagree
//   * a `$File` row that no longer resolves — the build breaks LATER, in a
//                                  worktree that is not the one the edit was
//                                  made in
//   * the loan machinery left behind — `if ( g_pStockMover )` reads as a live
//                                  fallback to the next maintainer, and it can
//                                  only ever be NULL
//   * a retired gate arm left armed — a check that fails forever, or one
//                                  everybody learns to ignore
//   * a probe pointed at an uncompiled file — the only live evidence for the
//                                  three move-type arms silently stops existing
//
// The last one is not hypothetical. tools/8f-probe-movetypes was ALREADY dead at
// the entity-port tip this step branched from: 8f-igamemovement moved its three
// anchors into the shim, and the probe went on naming gamemovement.cpp. It
// asserted "anchor not unique (0)" and applied nothing, and nothing in the tree
// knew. TheProbePatchesAFileThatIsStillCompiled and
// TheProbeAnchorsResolveInTheFileItNames are that failure, written down.
//
//=============================================================================//
#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <sys/stat.h>

#include <algorithm>
#include <cctype>
#include <fstream>
#include <sstream>
#include <set>
#include <string>
#include <vector>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string s = std::string( kCandidates[ i ] ) + "/sourcesharp/host/sourcesharp.h";
		if ( stat( s.c_str(), &st ) == 0 )
			return kCandidates[ i ];
	}
	return NULL;
}

// The SRC root, which is what a vpc script's $SRCDIR expands to. tools/ is a
// SIBLING of it, so ReadTool below reaches through "../tools" rather than
// carrying a second root of its own that could go stale independently.
std::string Under( const std::string &rel )
{
	const char *pRoot = SrcRoot();
	return pRoot ? std::string( pRoot ) + "/" + rel : rel;
}

bool Exists( const std::string &path )
{
	struct stat st;
	return stat( path.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

//-----------------------------------------------------------------------------
// EVERY TRANSLATION UNIT ONE SIDE COMPILES, farm-relative, read through
// cmake_graph.h (compile_commands.json cross-checked against the target's
// CMakeLists source list; it throws on a missing, empty or stale input).
// Until Phase 6b this walked the `$File` rows of server_csharp.vpc and
// client_csharp.vpc.
//-----------------------------------------------------------------------------
std::vector<std::string> CompiledRows( const char *pszTarget )
{
	const std::set<std::string> s = cmake_graph::CompiledSources( pszTarget );
	return std::vector<std::string>( s.begin(), s.end() );
}

// The proof-of-read for each side: the shim this whole step moved the movement
// publication into, compiled on both sides. A reader that saw nothing cannot
// then answer "gamemovement.cpp is not compiled" for free.
const char *const kShimProofOfRead = "sourcesharp/game/shared/shell_movement.cpp";

bool NamesBasename( const std::vector<std::string> &rows, const std::string &base )
{
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		const size_t slash = rows[ i ].rfind( '/' );
		if ( rows[ i ].substr( slash == std::string::npos ? 0 : slash + 1 ) == base )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// THE SCAN REALLY SCANNED. Under VPC this was a second, independent reading of
// each script (a loose `$File` line count) that the quote-walking scan had to
// meet. RETIRED IN 6b: cmake_graph::CompiledSources already reads each list
// twice (compile_commands.json against the CMakeLists source list) and throws
// when they disagree or either is empty. The proof-of-read stays, per side.
//-----------------------------------------------------------------------------
void AssertRowsAreAFileList( const char *pszTarget, const std::vector<std::string> &rows )
{
	ASSERT_TRUE( std::find( rows.begin(), rows.end(), std::string( kShimProofOfRead ) ) != rows.end() )
		<< pszTarget << " does not compile " << kShimProofOfRead
		<< ", so what was read is not this side's source list.";
}

} // namespace

//=============================================================================
// PART 1 — the two rows.
//=============================================================================

TEST( GameMovementEvicted, TheFileIsInNeitherBuild )
{
	const std::vector<std::string> server = CompiledRows( cmake_graph::kServer );
	const std::vector<std::string> client = CompiledRows( cmake_graph::kClient );

	// A GUARD BEFORE THE CLAIM. A scan that resolved nothing at all would report
	// "in neither build" about a file that is in both, and that exact shape -- a
	// perfectly clean, perfectly meaningless zero -- is why 8f-movetypes' own
	// scan carries one.
	ASSERT_NO_FATAL_FAILURE( AssertRowsAreAFileList( cmake_graph::kServer, server ) );
	ASSERT_NO_FATAL_FAILURE( AssertRowsAreAFileList( cmake_graph::kClient, client ) );

	// EACH SIDE NAMED SEPARATELY, because the dangerous failure is asymmetric:
	// a build with the row on ONE side links a second complete CGameMovement
	// there and nowhere else, so the two sides run different movement code and
	// the only symptom is prediction disagreeing. Matched by BASENAME, as the
	// vpc scan matched `\gamemovement.cpp`, so a copy moved elsewhere counts.
	EXPECT_FALSE( NamesBasename( server, "gamemovement.cpp" ) )
		<< "gamemovement.cpp is still compiled by server_sourcesharp "
		   "(src/server/native/CMakeLists.txt). If the client no longer has it, "
		   "the two sides now have different movement implementations available "
		   "to sourcesharp_movement_managed.";
	EXPECT_FALSE( NamesBasename( client, "gamemovement.cpp" ) )
		<< "gamemovement.cpp is still compiled by client_sourcesharp "
		   "(src/client/native/CMakeLists.txt). Same asymmetry, other way round.";
}

TEST( GameMovementEvicted, EveryCompiledSourceInBothBuildsResolvesOnDisk )
{
	// THE CHECK THAT HAS CAUGHT A WOULD-BE BUILD BREAK THREE TIMES IN THIS
	// PROJECT. Removing a row cannot break this one; MOVING a file, renaming
	// one, or landing a row for a file a peer lane has not merged yet all do.
	// The failure without it is a build that breaks in somebody else's worktree.
	//
	// The walk-read-the-whole-list relation this used to carry (resolved paths
	// >= loose `$File` lines) is RETIRED IN 6b: cmake_graph::CompiledSources
	// cross-checks its two readings itself and throws on a short or empty one.
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };

	for ( size_t v = 0; v < 2; v++ )
	{
		const std::vector<std::string> rows = CompiledRows( kTargets[ v ] );
		ASSERT_NO_FATAL_FAILURE( AssertRowsAreAFileList( kTargets[ v ], rows ) );

		for ( size_t i = 0; i < rows.size(); i++ )
		{
			EXPECT_TRUE( Exists( Under( rows[ i ] ) ) )
				<< kTargets[ v ] << " compiles '" << rows[ i ] << "', which is not on "
				<< "disk at " << Under( rows[ i ] );
		}
	}
}

//=============================================================================
// PART 2 — the loan, and its going.
//=============================================================================

//=============================================================================
// PART 3 — the two tools that read the file, and the gate arm that read the
// convar. All three die or move with it, and a tool nobody notices dying is
// how this step found its own worst bug.
//=============================================================================

