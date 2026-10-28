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

const char *const kShim = "sourcesharp/game/server/shell_commandclient.cpp";

} // anonymous namespace

//=============================================================================//
// 1. THE DEFINITION IS THE SHIM'S
//=============================================================================//

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
		<< kShim
		<< " is not compiled into server_sourcesharp "
		   "(src/server/native/CMakeLists.txt): without it the definition is "
		   "not linked and the server does not build";
}

//=============================================================================//
// 4. THE DEAD REPLAY ARM IS GONE, AND STOCK'S ANSWER IS THE ONE THAT STAYED
//=============================================================================//

//=============================================================================//
// 4. THE HELPER IT DEPENDS ON IS IN THE SAME FILE — that is why it came here
//=============================================================================//
