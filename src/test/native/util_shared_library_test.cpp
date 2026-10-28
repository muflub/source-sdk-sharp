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

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// WHETHER THE PATH IS THERE AT ALL, which ReadFile above cannot answer: it
// EXPECTs the open, so asking it about a file that is supposed to be gone fails
// on the open instead of reporting the absence. Added at plan_shim.md Phase 2c,
// lane 2-utilshared, where game/shared/util_shared.cpp stopped existing and
// several facts here changed from "this needle is not in that file" to "that
// file is not in the tree" -- the stronger claim, and the one that cannot be
// satisfied vacuously.
bool FileExists( const char *pPath )
{
	std::ifstream f( pPath );
	return f.good();
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

const char *const kLibrary   = "sourcesharp/game/shared/util_shared_library.cpp";
const char *const kStockCpp  = "game/shared/util_shared.cpp";
const char *const kStockHdr  = "game/shared/util_shared.h";

const char *const kMovedDefinitions[] = {
	// UTIL_StringToFloatArray and UTIL_StringToVector STOOD HERE and are DELETED
	// by C9 K3: their one importer, shell_propdata_system.o, left both sides.
	// Their absence is asserted as a pair in section 4.
	// UTIL_StringToIntArray and UTIL_StringToColor32 STOOD HERE and are DELETED
	// by plan_shim.md Phase 2c, lane 2-utilshared. Lane H2 read the pair as
	// dead and it is: neither appears in any importer's symbol list on either
	// link line (tools/shim-census who-imports over this file), and
	// `nm -C -u {server,client}_sourcesharp/*.o` counts 0 undefined references
	// to either. Their callers -- game/server/saverestore_gamedll.cpp:93 and
	// game/shared/baseentity_shared.cpp:384 -- are compiled into neither target.
	//
	// The row is REMOVED rather than re-pointed because there is nowhere to
	// point it: this table is "every definition that moved into the shim", and
	// these two are not in the shim any more. Their absence has its own fact
	// below, so the deletion does not become invisible.
	// UTIL_IsLowViolence, UTIL_GetActiveHolidayString and
	// UTIL_GetActiveOperationString STOOD HERE. They were #ifdef CLIENT_DLL, so
	// plan_server_native_v2 T2 (2026-10-18) moved them on to the client's
	// utility library (a shared TU keeps only what both DLLs import); they are
	// kClientMovedDefinitions below, asserted there and absent here.
	"bool UTIL_ShouldShowBlood( int color )",
	// GetCleanMapName left (plan_server_native_min P1): no caller in either
	// DLL. util_shared.h is the fork's and still declares it; nothing links
	// against the declaration.
};
const size_t kNumMoved = sizeof( kMovedDefinitions ) / sizeof( kMovedDefinitions[ 0 ] );

const char *const kClientHome = "sourcesharp/game/client/client_util_library.cpp";
const char *const kClientMovedDefinitions[] = {
	"bool UTIL_IsLowViolence( void )",
	// UTIL_GetActiveHolidayString and UTIL_GetActiveOperationString moved here
	// too and are deleted: they answered NULL, and their one caller,
	// CGameClientExports, answers its NULL directly (lane M1-B, 2026-10-25).
};
const size_t kNumClientMoved = sizeof( kClientMovedDefinitions ) / sizeof( kClientMovedDefinitions[ 0 ] );

// The four violence knobs, now declared in MANAGED code.
//
// The declarations moved to SourceSharp.Server/ConVars.cs and
// SourceSharp.Client/ConVars.cs -- one per DLL, which is exactly the two
// registrations the `static ConVar` lines produced when this shared file was
// compiled into both. The PREDICATES did not move: UTIL_IsLowViolence and
// UTIL_ShouldShowBlood are asked per blood effect on both sides and read the
// same convars out of the engine's registry by name.
//
// Failure 3 in this file's banner is therefore ASSERTED MORE STRONGLY than
// before, not less. It used to be "the four statics must be in the same file as
// the predicates"; it is now "there must be no native declaration of them
// anywhere, and exactly one managed declaration per side". A second
// registration under one name is neither a compile error nor a link error --
// util_shared_library.cpp's own note on `developer` records that "neither the
// console nor the game says which one it read".
const char *const kViolenceNames[] = {
	"violence_hblood",
	"violence_hgibs",
	"violence_ablood",
	"violence_agibs",
};
const size_t kNumConVars = sizeof( kViolenceNames ) / sizeof( kViolenceNames[ 0 ] );

const char *const kServerConVarsCs = "sourcesharp/managed/SourceSharp.Server/ConVars.cs";
const char *const kClientConVarsCs = "sourcesharp/managed/SourceSharp.Client/ConVars.cs";

// What util_shared.h must still declare, so that no caller changed.
const char *const kStillDeclared[] = {
	"void\t\tUTIL_StringToVector( float *pVector, const char *pString );",
	"bool\t\tUTIL_IsLowViolence( void );",
	"bool\t\tUTIL_ShouldShowBlood( int bloodColor );",
	"EUniverse GetUniverse();",
	"const char\t\t   *UTIL_GetActiveHolidayString();",
	"const char *GetCleanMapName( const char *pszUnCleanMapName, char (&pszTmp)[256] );",
};
const size_t kNumDeclared = sizeof( kStillDeclared ) / sizeof( kStillDeclared[ 0 ] );

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

TEST( UtilSharedLibrary, EveryMovedDefinitionIsInTheShimFile )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not defined exactly once in "
			<< kLibrary;
	}

	const std::string client = ReadFile( kClientHome );
	ASSERT_FALSE( client.empty() ) << kClientHome << " did not load";
	for ( size_t i = 0; i < kNumClientMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( client, kClientMovedDefinitions[ i ] ) )
			<< kClientMovedDefinitions[ i ] << " is not defined exactly once in "
			<< kClientHome;
		EXPECT_EQ( 0u, CountOf( lib, kClientMovedDefinitions[ i ] ) )
			<< kClientMovedDefinitions[ i ] << " is defined in the shared "
			<< kLibrary << " again; it is the client's";
	}
}

TEST( UtilSharedLibrary, NoMovedDefinitionWasLeftBehindInUtilShared )
{
	// plan_shim.md Phase 2c, lane 2-utilshared: kStockCpp no longer exists, so
	// the absence is asserted of the FILE. An absence over a file that is not
	// there is vacuous -- which is why the library is proved read below rather
	// than this being a bare EXPECT_TRUE(empty).
	EXPECT_FALSE( FileExists( kStockCpp ) )
		<< kStockCpp << " exists again -- two bodies, and the one the game runs "
		<< "is not the one this suite reads.";

	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";
	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not in " << kLibrary << " either.";
	}
	const std::string client = ReadFile( kClientHome );
	ASSERT_FALSE( client.empty() ) << kClientHome << " did not load";
	for ( size_t i = 0; i < kNumClientMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( client, kClientMovedDefinitions[ i ] ) )
			<< kClientMovedDefinitions[ i ] << " is not in " << kClientHome << " either.";
	}
}

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

TEST( UtilSharedLibrary, TheFourViolenceConVarsAreDeclaredInManagedCodeOnNeitherSide )
{
	// The engine defines all four (no game-DLL flag in the live table), so a
	// managed declaration would alias the engine's own; the registry refuses
	// one at startup (convar_ownership.h). The library reads them by name.
	const std::string server = ReadFile( kServerConVarsCs );
	const std::string client = ReadFile( kClientConVarsCs );
	ASSERT_FALSE( server.empty() ) << kServerConVarsCs << " did not load";
	ASSERT_FALSE( client.empty() ) << kClientConVarsCs << " did not load";

	for ( size_t i = 0; i < kNumConVars; i++ )
	{
		const std::string attr =
			std::string( "[ConVar(\"" ) + kViolenceNames[ i ] + "\"";

		EXPECT_EQ( 0u, CountOf( server, attr ) )
			<< kViolenceNames[ i ] << " is declared in " << kServerConVarsCs
			<< ", which would alias the engine's own convar.";
		EXPECT_EQ( 0u, CountOf( client, attr ) )
			<< kViolenceNames[ i ] << " is declared in " << kClientConVarsCs
			<< ", which would alias the engine's own convar.";
	}
}

TEST( UtilSharedLibrary, TheLibraryReadsTheFourByNameRatherThanDeclaringThem )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumConVars; i++ )
	{
		// The predicates are still HERE and still read all four, through a ref
		// into the engine's registry rather than through a declaration.
		EXPECT_EQ( 1u, CountOf( lib, std::string( "( \"" ) + kViolenceNames[ i ] + "\" );" ) )
			<< kViolenceNames[ i ] << " has no by-name ref in " << kLibrary
			<< ". UTIL_ShouldShowBlood is still here and still reads it.";
	}

	// Exactly four REFS, counted by the declaration spelling rather than by the
	// type name: the type is also named in this file's banner and in its
	// include, and subtracting those out was a needle that had to be kept in
	// step with the prose beside it.
	EXPECT_EQ( 4u, CountOf( lib, "static CManagedConVarRef\tviolence_" ) )
		<< "expected exactly four by-name refs in " << kLibrary;

	// UTIL_IsLowViolence moved to the client (plan_server_native_v2 T2) and
	// reads the same four the same way, through refs of its own file.
	const std::string client = ReadFile( kClientHome );
	ASSERT_FALSE( client.empty() ) << kClientHome << " did not load";
	for ( size_t i = 0; i < kNumConVars; i++ )
	{
		EXPECT_EQ( 1u, CountOf( client, std::string( "( \"" ) + kViolenceNames[ i ] + "\" );" ) )
			<< kViolenceNames[ i ] << " has no by-name ref in " << kClientHome
			<< ", where UTIL_IsLowViolence reads it.";
	}
	EXPECT_EQ( 4u, CountOf( client, "static CManagedConVarRef\tviolence_" ) )
		<< "expected exactly four by-name refs in " << kClientHome;
}

TEST( UtilSharedLibrary, TheFourViolenceConVarsAreDeclaredNativelyNowhereAtAll )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	// kStockCpp is gone (Phase 2c, lane 2-utilshared), so the "not declared
	// there" half is the absence of the file. It is worth keeping rather than
	// dropping: that file is where all four USED to live, so its return is
	// exactly the shape this fact watches for.
	EXPECT_FALSE( FileExists( kStockCpp ) )
		<< kStockCpp << " exists again, and it is where all four of these used "
		<< "to be declared. A second registration under one name is neither a "
		<< "compile error nor a link error, and neither the console nor the "
		<< "game says which one it read.";

	for ( size_t i = 0; i < kNumConVars; i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, std::string( "static ConVar\t" ) + kViolenceNames[ i ] + "(" ) )
			<< kViolenceNames[ i ] << " is declared natively in " << kLibrary
			<< " again -- and it is also declared in managed code, so the game "
			   "now has two of it.";
	}
}

//=============================================================================//
// 4. THE STRING PARSER FAMILY IS WHOLE
//
// Moving UTIL_StringToVector without UTIL_StringToFloatArray converts one
// cross-edge into another and removes nothing.
//=============================================================================//

TEST( UtilSharedLibrary, BothStringParserPairsLeftAsPairs )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";
	// The vacuity guard: a definition this file still holds reads as one.
	ASSERT_EQ( 1u, CountOf( lib, "bool UTIL_ShouldShowBlood( int color )" ) )
		<< kLibrary << " is not the file this fact means to read";

	// THE FIRST PAIR WENT WITH ITS LAST IMPORTER (C9 K3). UTIL_StringToVector
	// is one line, a call to UTIL_StringToFloatArray, and both stayed while
	// shell_propdata_system.o imported the caller. That file left both sides,
	// `nm -u` over every object and archive on both link lines counted 0
	// references to either, and the pair was deleted as a pair.
	EXPECT_EQ( 0u, CountOf( lib, "void UTIL_StringToVector( float *pVector, const char *pString )" ) )
		<< "UTIL_StringToVector is back with no compiled caller.";
	EXPECT_EQ( 0u, CountOf( lib, "void UTIL_StringToFloatArray( float *pVector, int count, const char *pString )" ) )
		<< "UTIL_StringToFloatArray is back. It was UTIL_StringToVector's callee "
		   "(and BreakModelText.cs ports it for managed); a native caller that "
		   "reappears brings the pair back together.";
	// THE SECOND PAIR WENT OUT TOGETHER, which is the same rule read the other
	// way. UTIL_StringToColor32 is UTIL_StringToIntArray the way
	// UTIL_StringToVector is UTIL_StringToFloatArray, and plan_shim.md Phase 2c,
	// lane 2-utilshared, found no compiled caller for EITHER (0 undefined
	// references to both across every object on both link lines), so the pair
	// was deleted as a pair. Deleting only the caller would have left a private
	// helper with nothing to help; deleting only the callee would not compile.
	//
	// Asserted, because a body that came back would restore a dead cross-edge
	// quietly -- and because the pair-ness is the claim, not the individual
	// absences.
	EXPECT_EQ( 0u, CountOf( lib, "void UTIL_StringToColor32( color32 *color, const char *pString )" ) )
		<< "UTIL_StringToColor32 is back with no compiled caller.";
	EXPECT_EQ( 0u, CountOf( lib, "void UTIL_StringToIntArray( int *pVector, int count, const char *pString )" ) )
		<< "UTIL_StringToIntArray is back. It was UTIL_StringToColor32's callee "
		   "and nothing else's; if a caller for it has reappeared, the pair "
		   "comes back together and gets a row in kMovedDefinitions again.";
}

//=============================================================================//
// 5. THE STEAM HALF LEFT FOR MANAGED
//
// plan_client_novalve C6, Steam to managed: GetSteamUtils, GetUniverse and
// the four UTIL_GetFilteredPlayerName* were the last Steam API calls in this
// file, and they are managed now (SteamPlayerNameFilter, ClientSteamContext).
// Asserted on whole definitions, and on the accessors themselves, because one
// of them coming back puts libsteam_api back on client.so's link line.
//=============================================================================//

TEST( UtilSharedLibrary, TheSteamHalfLeftForManaged )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";
	// Proof of read: a definition that stayed.
	ASSERT_EQ( 1u, CountOf( lib, "bool UTIL_ShouldShowBlood( int color )" ) );

	const char *const kGone[] = {
		"ISteamUtils* GetSteamUtils()",
		"EUniverse GetUniverse()",
		"char *UTIL_GetFilteredPlayerName( int iPlayerIndex, char *pszName )",
		"char *UTIL_GetFilteredPlayerName( const CSteamID &steamID, char *pszName )",
		"wchar_t *UTIL_GetFilteredPlayerNameAsWChar( int iPlayerIndex, const char *pszName, wchar_t *pwszName )",
		"wchar_t *UTIL_GetFilteredPlayerNameAsWChar( const CSteamID &steamID, const char *pszName, wchar_t *pwszName )",
		"SteamUtils()->",
		"steamapicontext",
		"#include \"steam/steam_api.h\"",
	};
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, kGone[ i ] ) )
			<< kGone[ i ] << " is back in " << kLibrary << ": the Steam half is managed's";
	}

	// The lookup the by-index wrappers had dropped stays dropped.
	EXPECT_EQ( 0u, CountOf( lib, "UTIL_PlayerByIndex(" ) );
}

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

TEST( UtilSharedLibrary, NoModGuardCameAcrossWithTheMovedCode )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	const char *const kModGuards[] = { "TF_DLL", "TF_CLIENT_DLL", "USES_ECON_ITEMS" };

	for ( size_t i = 0; i < 3; i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, std::string( "#ifdef " ) + kModGuards[ i ] ) )
			<< kModGuards[ i ] << " is conditioned on in " << kLibrary;
		EXPECT_EQ( 0u, CountOf( lib, std::string( "defined( " ) + kModGuards[ i ] + " )" ) )
			<< kModGuards[ i ] << " is conditioned on in " << kLibrary;
	}
}

//=============================================================================//
// 6. NOT ONE CALLER CHANGED
//=============================================================================//

TEST( UtilSharedLibrary, TheDeclarationsStayedInTheStockHeader )
{
	const std::string hdr = ReadFile( kStockHdr );
	ASSERT_FALSE( hdr.empty() ) << kStockHdr << " did not load";

	for ( size_t i = 0; i < kNumDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( hdr, kStillDeclared[ i ] ) )
			<< kStillDeclared[ i ] << " is no longer declared in " << kStockHdr;
	}
}
