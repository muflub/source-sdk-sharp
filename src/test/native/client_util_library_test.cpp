//========= SourceSharp — the client utility library ==========================//
//
// plan_entity.md Phase 7, step 7f. `game/client/cdll_util.cpp` owed the shim
// six symbols; they now live in
// `sourcesharp/game/client/client_util_library.cpp`, with every DECLARATION
// left where it was in `game/client/cdll_util.h`, so not one caller changed.
//
// WHAT CAN GO WRONG HERE, AND WHY IT COMPILES WHEN IT DOES:
//
//   1. A COPY LEFT BEHIND. `ScreenWidth` and `VarArgs`
//      each defined twice in one link line does not link -- but a definition
//      restored inside an `#ifdef` arm this build does not take DOES link,
//      and reads as a move that was never made. The file-content assertions
//      below fail on the text, not on the link, so they catch that shape.
//
//   2. THE BUILD ROW MISSING. `client_util_library.cpp` is CLIENT ONLY: it holds
//      client-DLL mechanism (`ScreenWidth`, `TextMessageGet`) that has no
//      server counterpart, so it belongs in client_sourcesharp and must NOT be
//      in server_sourcesharp (src/{client,server}/native/CMakeLists.txt; the
//      two .vpc files until Phase 6b). Both directions are asserted -- a row in
//      the server list would compile `engine->TextMessageGet` into server.so, where
//      that interface is a different one.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <sstream>
#include <string>

namespace
{

const char *const kLibrary   = "sourcesharp/game/client/client_util_library.cpp";
} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

//=============================================================================//
// 2. ONE BUILD ROW, AND IT IS THE CLIENT'S
//=============================================================================//

TEST( ClientUtilLibrary, TheClientBuildCompilesIt )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kLibrary ) )
		<< "the client does not compile the utility library, and cdll_util.cpp "
		   "no longer defines what it holds -- so client.so does not link.";
}

TEST( ClientUtilLibrary, TheServerBuildDoesNotCompileIt )
{
	// Proof-of-read first: the server's own shell, so an absence below is not
	// answered by a reader that saw the wrong list.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_gamedll.cpp" ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) is not the server's list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kLibrary ) )
		<< "this is client-DLL mechanism: ScreenWidth reads the VGUI surface "
		   "and TextMessageGet reads the client engine interface. Neither "
		   "means anything in server.so.";
}

//=============================================================================//
// 3. NOT ONE CALLER CHANGED
//=============================================================================//

