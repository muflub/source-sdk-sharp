//========= SourceSharp — the client utility library ==========================//
//
// plan_entity.md Phase 7, step 7f. `game/client/cdll_util.cpp` owed the shim
// six symbols; they now live in
// `sourcesharp/game/client/client_util_library.cpp`, with every DECLARATION
// left where it was in `game/client/cdll_util.h`, so not one caller changed.
//
// WHAT CAN GO WRONG HERE, AND WHY IT COMPILES WHEN IT DOES:
//
//   1. A COPY LEFT BEHIND. `ScreenWidth`, `VarArgs` and `g_MakingDevShots`
//      each defined twice in one link line does not link -- but a definition
//      restored inside an `#ifdef` arm this build does not take DOES link,
//      and reads as a move that was never made. The file-content assertions
//      below fail on the text, not on the link, so they catch that shape.
//
//   2. THE VISION-FLAG FAMILY SPLIT UP. The two counters
//      `g_nLocalPlayerVisionFlagsWeaponsCheck` and `g_nLocalPlayerVisionFlags`
//      are FILE STATICS. `UpdateLocalPlayerVisionFlags` writes them and
//      `GetLocalPlayerVisionFilterFlags` reads them, so the writer and the
//      reader have to be in the SAME translation unit or one of them silently
//      addresses a different pair of zeroes. Splitting them compiles and
//      links; the HUD then just never sees a vision flag. Both halves, the
//      predicate that consumes them, and the ConVar that predicate publishes
//      to are asserted to be here and asserted not to be there.
//
//   3. THE BUILD ROW MISSING. `client_util_library.cpp` is CLIENT ONLY: it holds
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

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
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

const char *const kLibrary   = "sourcesharp/game/client/client_util_library.cpp";
const char *const kStockCpp  = "game/client/cdll_util.cpp";
const char *const kStockHdr  = "game/client/cdll_util.h";

// The definitions that moved, spelled as the start of a DEFINITION so that the
// declaration in cdll_util.h (which stays) cannot match: a declaration ends in
// `;` and has no `(` body after it on the same shape.
const char *const kMovedDefinitions[] = {
	"bool g_MakingDevShots = false;",
	"char *VarArgs( const char *format, ... )",
	"int GetLocalPlayerVisionFilterFlags( bool bWeaponsCheck",
	"void UpdateLocalPlayerVisionFlags()",
	"bool IsLocalPlayerUsingVisionFilterFlags( int nFlags,",
	"client_textmessage_t *TextMessageGet( const char *pName )",
	"int ScreenHeight( void )",
	"int ScreenWidth( void )",
};
const size_t kNumMoved = sizeof( kMovedDefinitions ) / sizeof( kMovedDefinitions[ 0 ] );

// The two file statics and the ConVar. These are not symbols any other TU can
// name, which is exactly why their placement needs asserting rather than
// linking.
const char *const kMovedStatics[] = {
	"static int g_nLocalPlayerVisionFlagsWeaponsCheck = 0;",
	"static int g_nLocalPlayerVisionFlags = 0;",
	"ConVar localplayer_visionflags( \"localplayer_visionflags\"",
};
const size_t kNumStatics = sizeof( kMovedStatics ) / sizeof( kMovedStatics[ 0 ] );

// What cdll_util.h must still declare, so that no caller changed.
const char *const kStillDeclared[] = {
	"extern bool g_MakingDevShots;",
	"int		ScreenHeight( void );",
	"int		ScreenWidth( void );",
	"void	UpdateLocalPlayerVisionFlags();",
	"int		GetLocalPlayerVisionFilterFlags( bool bWeaponsCheck = false );",
	"bool	IsLocalPlayerUsingVisionFilterFlags( int nFlags, bool bWeaponsCheck = false );",
};
const size_t kNumDeclared = sizeof( kStillDeclared ) / sizeof( kStillDeclared[ 0 ] );

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

TEST( ClientUtilLibrary, EveryMovedDefinitionIsInTheShimFile )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not defined exactly once in "
			<< kLibrary;
	}
}

TEST( ClientUtilLibrary, CdllUtilLeftSoNothingCanBeLeftBehindInIt )
{
	// Plan_client_novalve C8 (lane c6-input): cdll_util.cpp left client.so with
	// no importer, so no moved definition (and no second pair of vision
	// statics, below) can come back through it. Absence read from the farm,
	// which holds only what client.so compiles; the library is the control.
	ASSERT_FALSE( ReadFile( kLibrary ).empty() ) << kLibrary << " did not load";
	std::ifstream stock( kStockCpp );
	EXPECT_FALSE( stock.good() ) << kStockCpp << " is back in the farm";
}

//=============================================================================//
// 2. THE VISION-FLAG FAMILY IS WHOLE, IN ONE TRANSLATION UNIT
//
// The failure this catches links and runs: the reader addresses a pair of
// zeroes the writer never touches, and the HUD simply never sees a flag.
//=============================================================================//

TEST( ClientUtilLibrary, TheVisionCountersLiveWithBothTheirReaderAndTheirWriter )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumStatics; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedStatics[ i ] ) )
			<< kMovedStatics[ i ] << " is not in " << kLibrary;
	}

	// and the reader and the writer are both in the same file as them
	EXPECT_EQ( 1u, CountOf( lib, "int GetLocalPlayerVisionFilterFlags( bool bWeaponsCheck" ) );
	EXPECT_EQ( 1u, CountOf( lib, "void UpdateLocalPlayerVisionFlags()" ) );
}

//=============================================================================//
// 3. ONE BUILD ROW, AND IT IS THE CLIENT'S
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
// 4. NOT ONE CALLER CHANGED
//=============================================================================//

TEST( ClientUtilLibrary, TheDeclarationsStayedInTheStockHeader )
{
	const std::string hdr = ReadFile( kStockHdr );
	ASSERT_FALSE( hdr.empty() ) << kStockHdr << " did not load";

	for ( size_t i = 0; i < kNumDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( hdr, kStillDeclared[ i ] ) )
			<< kStillDeclared[ i ] << " is no longer declared in " << kStockHdr
			<< ". The point of the move is that callers did not have to know "
			   "about it.";
	}
}
