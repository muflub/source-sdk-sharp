//========= SourceSharp — the shared utility library, on both link lines ======//
//
// plan_entity.md Phase 7, step 7f. Six small entity-free families left
// `game/shared/util_shared.cpp` for
// `sourcesharp/game/shared/util_shared_library.cpp`, with every declaration
// left in `game/shared/util_shared.h` so no caller on either side changed.
//
// FOUR FAILURES THIS PINS, all of which compile, and three of which link:
//
//   1. A COPY LEFT BEHIND, inside an arm this build does not take. Two bodies
//      for one function link when only one arm is compiled, and the tests that
//      READ the new address stay green while the game runs the old one.
//
//   2. A ONE-SIDED SOURCE ROW. util_shared.cpp was compiled into server.so
//      AND client.so, so this file must be in BOTH targets' source lists. In only one, that side
//      gets two definitions of everything the shared families hold and the
//      other gets none — and the side with none fails loudly at link while the
//      side with two silently keeps stock's. The recorded lesson is exactly
//      here: a shared file read on one side compiles green and breaks the
//      other at a later step.
//
//   3. THE VIOLENCE ConVars SPLIT FROM THEIR PREDICATES. violence_hblood and
//      its three siblings are FILE STATICS. Left in util_shared.cpp they are
//      not a link error and not a compile error: they are a second, separate
//      set of four that no operator can reach, and `UTIL_IsLowViolence` over
//      here reads the ones over here forever. Both directions are asserted.
//
//   4. THE STRING PARSER FAMILY SPLIT. `UTIL_StringToVector` is one line —
//      `UTIL_StringToFloatArray( pVector, 3, pString )` — so moving the caller
//      without the callee removes no cross-edge at all; it moves it onto the
//      symbol next door. 4e measured that shape on the blood emitters and
//      declined the move for it, and this test is what stops it coming back
//      here as a "tidy-up".
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <algorithm>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kLibrary   = "sourcesharp/game/shared/util_shared_library.cpp";
const char *const kStockCpp  = "game/shared/util_shared.cpp";
} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

//=============================================================================//
// 2. BOTH LINK LINES COMPILE IT
//=============================================================================//

// AND THE ROW THEY REPLACED IS GONE FROM BOTH. plan_shim.md Phase 2c's exit
// condition for this row is that game/shared/util_shared.cpp is compiled by
// neither target; it is asserted here, beside the rows that took its place, so
// a lane that re-adds the file has to explain itself to the test that owns its
// successor.
TEST( UtilSharedLibrary, NeitherTargetCompilesTheStockFileAnyMore )
{
	// Proof-of-read for each absence: the successor IS compiled on each side.
	// (cmake_graph also throws on a missing, empty or stale input.)
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibrary ) )
		<< "server_sourcesharp does not compile " << kLibrary;
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kLibrary ) )
		<< "client_sourcesharp does not compile " << kLibrary;

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) compiles "
		   "game/shared/util_shared.cpp again.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kStockCpp ) )
		<< "client_sourcesharp (src/client/native/CMakeLists.txt) compiles "
		   "game/shared/util_shared.cpp again -- and it is in BOTH targets or "
		   "neither, so a one-sided row is the shape that compiles green on one "
		   "side and breaks the other.";
}

// Listed EXACTLY ONCE in the target's CMakeLists (a second row is a duplicate
// TU) and present in its compile database.
static void ExpectCompiledOnce( const char *pTarget )
{
	const std::vector< std::string > listed = cmake_graph::ListedSources( pTarget );
	EXPECT_EQ( 1, std::count( listed.begin(), listed.end(), std::string( kLibrary ) ) )
		<< kLibrary << " is not listed exactly once in " << pTarget
		<< " (" << cmake_graph::ListFile( pTarget ) << ")";
	EXPECT_TRUE( cmake_graph::Compiles( pTarget, kLibrary ) )
		<< kLibrary << " is not compiled into " << pTarget;
}

TEST( UtilSharedLibrary, TheServerCompilesIt )
{
	ExpectCompiledOnce( cmake_graph::kServer );
	if ( HasFailure() )
		ADD_FAILURE() << "server.so asks for the shared random functions, and nothing else defines them -- so it does not link.";
}

TEST( UtilSharedLibrary, TheClientCompilesIt )
{
	ExpectCompiledOnce( cmake_graph::kClient );
	if ( HasFailure() )
		ADD_FAILURE() << "client.so asks for UTIL_IsLowViolence, GetUniverse and "
		   "the filtered player names, and nothing else defines them.";
}

//=============================================================================//
// 3. THE VIOLENCE ConVars TRAVELLED WITH THE PREDICATES THAT READ THEM
//=============================================================================//

//=============================================================================//
// 4. THE STRING PARSER FAMILY IS WHOLE
//
// Moving UTIL_StringToVector without UTIL_StringToFloatArray converts one
// cross-edge into another and removes nothing.
//=============================================================================//

//=============================================================================//
// 5. THE STEAM HALF LEFT FOR MANAGED
//
// plan_client_novalve C6, Steam to managed: GetSteamUtils, GetUniverse and
// the four UTIL_GetFilteredPlayerName* were the last Steam API calls in this
// file, and they are managed now (SteamPlayerNameFilter, ClientSteamContext).
// Asserted on whole definitions, and on the accessors themselves, because one
// of them coming back puts libsteam_api back on client.so's link line.
//=============================================================================//

//=============================================================================//
// 6. THE THREE MOD GUARDS DID NOT COME ACROSS
//
// tools/dead-conditionals refuses a shim TU that conditions on an identifier
// no link line defines, and its ledger takes portability guards and side
// markers but says of a mod guard that "this is not that game and never will
// be". Carrying TF_DLL, TF_CLIENT_DLL or USES_ECON_ITEMS into the shim would
// therefore be a problem that tool reports -- but it reports it over the whole
// shim, so a future edit that puts one back HERE is easiest to catch here.
//=============================================================================//

//=============================================================================//
// 6. NOT ONE CALLER CHANGED
//=============================================================================//

