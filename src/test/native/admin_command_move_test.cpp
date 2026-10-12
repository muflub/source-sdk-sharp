//========= SourceSharp — UTIL_IsCommandIssuedByServerAdmin's new address =====//
//
// plan_entity.md Phase 4 EXIT. The exit's unit of work is the set of stock
// translation units that must leave server_csharp.vpc in ONE commit, because
// the stock link graph has a cycle in it and no member of a cycle can leave
// alone. tools/p4-exit-batches measures that cycle; this file pins one of the
// two moves that made it smaller.
//
// WHAT THE MOVE WAS. UTIL_IsCommandIssuedByServerAdmin was the last body left
// in game/server/util.cpp whose only dependency, UTIL_GetCommandClientIndex,
// had already become the shim's (shell_commandclient.cpp,
// plan_gamedll_after.md Phase A4). It moved beside it.
//
// WHY THAT IS WORTH A TEST RATHER THAN A TIDY-UP. It was ALSO the single
// symbol tying game/server/globalstate.cpp and game/shared/gamestringpool.cpp
// to util.cpp, and util.cpp is inside the cycle. Each of those two owed util.cpp
// exactly one symbol and it was this one, so each sat in the Phase 4 exit's
// atomic batch on its account alone. Measured before and after:
//
//     cycle 31 TUs / 49,118 lines   ->   with this move and the shared-random
//                                        one, 27 TUs / 47,191 lines
//
// THE FAILURE THIS FILE IS FOR, which compiles:
//
//   THE DECLARATION MOVED TOO. If util.h stops declaring it, every caller
//   has to change, and the change stops being a relocation. Two callers are
//   in util.cpp itself and five more are in stock and shim files this lane
//   does not own. (Retired with util.h itself: see section 2.)
//
// Comments are stripped before every "is it still there" read, so prose
// cannot satisfy a needle. 4c's debugoverlays_move_test went red on exactly
// that.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <fstream>
#include <sstream>
#include <string>

namespace
{

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// Strip // and /* */ comments. Deliberately naive about string literals: no
// file this test reads carries a "//" inside one, and the check that matters is
// that PROSE cannot satisfy a needle.
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

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *const kShim      = "sourcesharp/game/server/shell_commandclient.cpp";

// The start of the DEFINITION. A declaration ends in `;` and has no brace, so
// this needle cannot be satisfied by util.h.
const char *const kDefinition = "bool UTIL_IsCommandIssuedByServerAdmin( void )\n{";

} // anonymous namespace

//=============================================================================//
// 1. THE DEFINITION IS THE SHIM'S
//=============================================================================//

TEST( AdminCommandMove, TheDefinitionIsInTheShimFile )
{
	const std::string shim = Code( ReadFile( kShim ) );
	ASSERT_FALSE( shim.empty() ) << kShim << " did not load";
	EXPECT_EQ( 1u, CountOf( shim, kDefinition ) )
		<< kDefinition << " is not defined exactly once in " << kShim;
}

// 2. RETIRED. "The declaration stays in util.h" read game/server/util.h, which
// is compiled by nothing since plan_server_novalve.md P5 and left the farm in
// P6: with no caller compiling against it, there is no caller to keep unchanged.

//=============================================================================//
// 3. THE SHIM FILE IS ACTUALLY COMPILED, AND ONLY INTO THE SERVER
//=============================================================================//

TEST( AdminCommandMove, TheServerCompilesTheShimFile )
{
	// cmake_graph throws on a missing, empty or stale compile database, so a
	// reader that saw nothing cannot answer here.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShim ) )
		<< kShim << " is not compiled into server_sourcesharp "
		   "(src/server/native/CMakeLists.txt): without it the definition is "
		   "not linked and the server does not build";
}

//=============================================================================//
// 4. THE DEAD REPLAY ARM IS GONE, AND STOCK'S ANSWER IS THE ONE THAT STAYED
//=============================================================================//

TEST( AdminCommandMove, TheDeadReplayGuardDidNotComeAcrossWithTheBody )
{
	// tools/dead-conditionals reports a shim file conditioning on a define no
	// link line carries as a PROBLEM, not as something to ledger, and its
	// ledger's own rule is that the pending list can only shrink. So the arm
	// this build takes was kept and the other deleted -- 4e's and 7f's call at
	// the same gate. This fact is what stops it coming back with the next edit
	// that copies a body out of a stock file.
	const std::string shim = Code( ReadFile( kShim ) );
	ASSERT_FALSE( shim.empty() );
	EXPECT_EQ( 0u, CountOf( shim, "REPLAY_ENABLED" ) )
		<< kShim << " conditions on REPLAY_ENABLED, which neither link line "
		   "defines; tools/dead-conditionals fails the native leg for it";
	// THE SECOND ASSERTION IS INVERTED AT plan_shim.md step 5c, and what it
	// claimed is retired with the code it pinned.
	//
	// It used to be `EXPECT_EQ( 1u, CountOf( shim, "const bool "
	// "bPlayerIsReplay = false;" ) )`, whose reason was that the arm this build
	// takes must still be here so the function still answers stock's question.
	// HLTV and Replay are now DROPPED rather than merely unbuilt, so stock's
	// question is not one this mod asks: there is no replay client to be at
	// slot 2. Keeping a `false` local to stand in for a feature that is gone
	// would be describing a decision as an accident.
	//
	// What survives is the ABSENCE, which is a ratchet: the local, the token
	// and the two-armed shape all have to stay gone.
	EXPECT_EQ( 0u, CountOf( shim, "bPlayerIsReplay" ) )
		<< kShim << " has the replay local back. Replay is dropped "
		   "(plan_shim.md 5c), so there is no client the second arm is for.";
	EXPECT_EQ( 0u, CountOf( shim, "issuingPlayerIndex > 2" ) )
		<< kShim << " tests for slot 2 again, which only a replay master "
		   "could ever have occupied.";
	EXPECT_EQ( 1u, CountOf( shim, "if ( issuingPlayerIndex > 1 )" ) )
		<< kShim << " no longer makes the single index test that is the whole "
		   "of the admin question now -- the function has changed shape, not "
		   "merely lost a branch.";
}

//=============================================================================//
// 4. THE HELPER IT DEPENDS ON IS IN THE SAME FILE — that is why it came here
//=============================================================================//

TEST( AdminCommandMove, ItsOneDependencyIsDefinedInTheSameFile )
{
	const std::string shim = Code( ReadFile( kShim ) );
	ASSERT_FALSE( shim.empty() ) << kShim << " did not load";
	EXPECT_EQ( 1u, CountOf( shim, "int UTIL_GetCommandClientIndex( void )\n{" ) )
		<< "the whole reason for the address: the body reads exactly one "
		   "symbol and it is defined here";
	// The MOVED body's own call, with its whole statement: the file has two
	// other calls to the same helper (UTIL_GetCommandClient and
	// UTIL_GetCommandClientActivator), so a bare-name or bare-call needle here
	// would be satisfied by either of them and could not fail.
	EXPECT_EQ( 1u, CountOf( shim, "int issuingPlayerIndex = UTIL_GetCommandClientIndex();" ) )
		<< "and the moved body still reads it";
}
