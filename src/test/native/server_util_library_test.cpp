//==== SourceSharp — the server utility mechanism that names no entity =======//
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/server/util.cpp` is one of
// SCC 11's members, and seven families of entity-free mechanism left it for
// `sourcesharp/game/server/server_util_library.cpp`.
//
// FIVE FAILURES THIS PINNED, none of which any build or gate here can see.
// util.cpp has since left the source tree (server.so does not compile it), so
// the halves of 1-3 that read it were culled; what is left pins the library.
//
//   1. A COPY LEFT BEHIND in `util.cpp`. Two bodies for one function link when
//      only one is reachable, and a test that reads the NEW address stays green
//      while the game runs the old one.
//
//   2. `g_NullSurface` SPLIT INTO TWO. Stock's was a FILE STATIC, and
//      `UTIL_SetTrace` — which could not travel, because it calls
//      `CBaseEntity::Instance`, a symbol that is not a cross-edge today, so
//      moving it would ADD one — still reads it. So the object moved with
//      EXTERNAL linkage and util.cpp declares it `extern`. Copying it instead
//      is neither a compile error nor a link error: it is two "**empty**"
//      surfaces that drift.
//
//   3. THE CHECK-CLIENT SUBSYSTEM SPLIT FROM ITS STATE. The shim named exactly
//      two of its eight members (`UTIL_SetClientVisibilityPVS` and the accessor
//      `shell_serversystems.cpp` registers). The other six all read
//      `g_CheckClient`'s fields directly and three are file statics no other TU
//      can name, so moving the two the shim named would have EXPORTED the state
//      rather than moved it.
//
//   4. A ONE-SIDED SOURCE ROW, in the other direction from the shared libraries':
//      `util.cpp` is SERVER-ONLY, so this file must be in server_sourcesharp's
//      source list (src/server/native/CMakeLists.txt) and NOT in
//      client_sourcesharp's (src/client/native/CMakeLists.txt). A client row would compile `engine->PrecacheDecal`
//      and `CServerSystem` into client.so.
//
//   5. `UTIL_IsCommandIssuedByServerAdmin` CLAIMED TWICE. It fits every
//      sentence above and is deliberately NOT here: lane `phase4-exit` has
//      already relocated it to `shell_commandclient.cpp`, which defines the one
//      symbol its body needs. Two lanes moving one body to two addresses is a
//      duplicate definition, so this suite asserts this file does NOT hold it.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <sstream>
#include <string>

namespace
{

const char *const kStockCpp = "game/server/util.cpp";

// What each target compiles is read through cmake_graph.h: compile_commands.json
// cross-checked against the target's CMakeLists source list. Paths are
// farm-relative, the CMakeLists spelling.
const char *const kLibraryRow = "sourcesharp/game/server/server_util_library.cpp";

// The client's proof-of-read: a shim the client compiles and no eviction can
// take, so "the client does not compile the library" is never answered by a
// reader that saw nothing.
const char *const kClientProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

//=============================================================================//
// 2. THE NULL SURFACE IS ONE OBJECT, NOT TWO
//=============================================================================//

//=============================================================================//
// 3. THE CHECK-CLIENT SUBSYSTEM CAME AS A UNIT
//=============================================================================//

//=============================================================================//
// 4. THE SERVER COMPILES IT AND THE CLIENT DOES NOT
//=============================================================================//

TEST( ServerUtilLibrary, TheServerCompilesIt )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibraryRow ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not "
		   "compile "
		<< kLibraryRow << ": "
		<< "server.so still compiles util.cpp, which no longer defines "
		   "UTIL_ClearTrace.";
}

TEST( ServerUtilLibrary, TheClientDoesNotCompileIt )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kClientProofOfRead ) )
		<< "client_sourcesharp does not compile " << kClientProofOfRead
		<< " -- the reader is not reading the client's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kLibraryRow ) )
		<< "client_sourcesharp (src/client/native/CMakeLists.txt) compiles " << kLibraryRow
		<< ": "
		   "util.cpp is server-only and so is this file; a client row would "
		   "compile CServerSystem and engine->PrecacheDecal into client.so.";
}

TEST( ServerUtilLibrary, TheStockTranslationUnitHasLeftAndTheLibraryHasNot )
{
	// INVERTED, NOT DELETED. This read `EXPECT_EQ( 1u, ... )` with the note
	// "the Phase 4 exit removes SCC 11's vpc rows in ONE commit; this lane moves
	// behaviour and leaves every stock row exactly where it is." That exit has
	// LANDED -- armed-rehearsal's fd5f3bc8d, "the removal-set rows out of both
	// vpcs" -- so the fact came true and the assertion went red, which is the
	// only way anyone would have found out that it had.
	//
	// The fact is worth keeping in BOTH polarities, so it is stated as the pair
	// it always was: the stock TU is gone AND the library that took its
	// behaviour is still compiled. Asserting only the absence would pass on a
	// build that had dropped both, which is exactly the accident this file
	// exists to catch -- every UTIL_ symbol below would then be measured against
	// a translation unit nothing links.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "game/server/util.cpp is in server_sourcesharp's source list again. It is a removal-set file "
		   "and its symbols are this library's now; compiling both gives the "
		   "link two definitions of every one of them.";

	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibraryRow ) )
		<< "the stock TU is gone AND so is the library that replaced it. Every "
		   "UTIL_ fact below is then about code nothing compiles.";
}

//=============================================================================//
// 5. THE CONTESTED SYMBOL IS NOT CLAIMED HERE
//=============================================================================//

//=============================================================================//
// 6. THE HEADER DID NOT MOVE, SO NO CALLER DID -- RETIRED
//
// EveryDeclarationStaysInUtilH asserted six declarations stayed in
// game/server/util.h. game/server/util.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, so the fact lost its
// subject.
//=============================================================================//

//=============================================================================//
// 7. THE NEEDLES ARE CODE, NOT PROSE
//=============================================================================//

//=============================================================================//
// 8. THE EMITTERS' MODEL INDICES ARE DECLARED HERE AND DEFINED IN combatweapon
//
// g_sModelIndexSmoke is combatweapon.cpp's (world_adapter.cpp's now; its
// three siblings had no reader left and went in plan_server_native_v2 T6). util.cpp reached them by
// `extern short`, and so does this file. DEFINING one here instead links --
// a `short x;` at namespace scope is a tentative definition -- and gives the
// emitters a model index that is always zero, which draws nothing and reports
// nothing.
//=============================================================================//

//=============================================================================//
// 9. THE ONE CONVERSION THE EMITTER MOVE COST, RECORDED RATHER THAN HIDDEN
//
// UTIL_BubbleTrail calls IntersectRayWithAAPlane, which public/collisionutils.cpp
// defines: that file went from one shim->stock edge to two. It is one of the
// four leaves plan_entity.md 6 says STAYS in the build, so the edge is not a
// debt any lane has to clear -- but it is a cost and it is named.
//
// The German-blood arm of UTIL_BloodStream reads `developer`-style ConVar
// g_Language, and it was NOT deleted although it looks unreachable. Deleting an
// arm that looks dead is the shape of all six recorded silent losses.
//=============================================================================//

//=============================================================================//
// THE NOTARGET KNOB: DECLARED IN MANAGED CODE, READ NATIVELY BY NAME
//
// plan_entity.md §6: registration is mechanism the engine owns, but no
// ConVar's default or semantics is declared natively. sv_strict_notarget
// decides whether a notarget entity is invisible to the AI rather than merely
// untargeted, which is as squarely "decides what the game does" as a convar
// gets -- so the declaration moved and the rule did not.
//
// Both halves are asserted, because either alone is a bug that runs: a
// declaration with no reader is a setting that does nothing, and a reader with
// no declaration reads zero forever.
//=============================================================================//
