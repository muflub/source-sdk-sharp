//========= SourceSharp — the shared-random family's new address ==============//
//
// plan_entity.md Phase 4 EXIT, the second of the two moves that shrank the
// stock link graph's cycle. See admin_command_move_test.cpp for what the cycle
// is and why a member of one cannot leave the vpc alone.
//
// WHAT MOVED. SeedFileLineHash (file-static), SharedRandomFloat,
// SharedRandomInt, SharedRandomVector and SharedRandomAngle, out of
// game/shared/util_shared.cpp and into
// sourcesharp/game/shared/util_shared_library.cpp — the shim TU step 7f
// created, compiled into BOTH server.so and client.so.
//
// WHY. SharedRandomInt was game/shared/animation.cpp's ONLY tie into the
// cycle. One symbol was holding a 1,097-line translation unit inside the Phase
// 4 exit's atomic commit.
//
// FOUR FAILURES THIS FILE IS FOR, all of which compile and three of which link:
//
//   1. THE FAMILY SPLIT UP. SeedFileLineHash is FILE-STATIC storage. Leave it
//      in util_shared.cpp and take the four, and the build is green and the
//      seed hash is a different function — which for a PREDICTION seed means
//      the client and the server disagree about a random number and nothing
//      says so. This is 7f's own stated rule for a family and 4e's
//      r_visualizetraces the other way round, and it is the first fact here.
//
//   2. A COPY LEFT BEHIND. Two definitions; stock's callers keep stock's.
//      animation.cpp is a stock caller, so the edge this move exists to cut
//      would still be there with every test green.
//
//   3. A ONE-SIDED SOURCE ROW. util_shared_library.cpp is compiled into server.so
//      AND client.so, and so was util_shared.cpp. A row in one list only gives
//      that side two definitions and the other none. The side with none fails
//      loudly at link; the side with two silently keeps stock's.
//
//   4. THE DECLARATIONS MOVED TOO, which would make this a rewrite of every
//      caller rather than a relocation.
//
// Comments are stripped before every read: the move left a tombstone in
// util_shared.cpp that names all four functions in prose.
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

const char *const kLibrary   = "sourcesharp/game/shared/util_shared_library.cpp";
// THE FAMILY'S HOME since plan_server_native_v2 T2 (2026-10-18). Its bodies
// were #ifdef CLIENT_DLL in kLibrary -- server.so never compiled them, and its
// callers are the client's -- so they left the shared TU for the client's
// utility library (a shared TU keeps only what both DLLs import).
const char *const kHome      = "sourcesharp/game/client/client_util_library.cpp";
const char *const kUtilShrC  = "game/shared/util_shared.cpp";
const char *const kUtilShrH  = "game/shared/util_shared.h";
// What each side compiles is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt). kLibrary is farm-relative, the spelling
// those lists use.

// Definition starts. A declaration in util_shared.h ends in `;` and carries a
// default argument, so none of these can match it.
// Definition starts, spelled WITHOUT the `/*=0*/` each signature carries: the
// reads below are comment-STRIPPED (the move left a tombstone in
// util_shared.cpp naming all four in prose), so a needle containing a comment
// can never match and the fact would fail for the wrong reason. Each still
// carries the full parameter list, so the declaration in util_shared.h -- which
// spells the default as `= 0` and ends in `;` -- cannot satisfy it.
const char *const kMoved[] = {
	"static int SeedFileLineHash( int seedvalue, const char *sharedname, int additionalSeed )",
	"float SharedRandomFloat( const char *sharedname, float flMinVal, float flMaxVal, int additionalSeed",
	"int SharedRandomInt( const char *sharedname, int iMinVal, int iMaxVal, int additionalSeed",
	"Vector SharedRandomVector( const char *sharedname, float minVal, float maxVal, int additionalSeed",
	"QAngle SharedRandomAngle( const char *sharedname, float minVal, float maxVal, int additionalSeed",
};
const size_t kNumMoved = sizeof( kMoved ) / sizeof( kMoved[ 0 ] );

const char *const kStillDeclared[] = {
	"float\tSharedRandomFloat( const char *sharedname, float flMinVal, float flMaxVal, int additionalSeed = 0 );",
	"int\t\tSharedRandomInt( const char *sharedname, int iMinVal, int iMaxVal, int additionalSeed = 0 );",
	"Vector\tSharedRandomVector( const char *sharedname, float minVal, float maxVal, int additionalSeed = 0 );",
	"QAngle\tSharedRandomAngle( const char *sharedname, float minVal, float maxVal, int additionalSeed = 0 );",
};
const size_t kNumDeclared = sizeof( kStillDeclared ) / sizeof( kStillDeclared[ 0 ] );

} // anonymous namespace

//=============================================================================//
// 1. THE FAMILY MOVED WHOLE — the file-static came with the four that read it
//=============================================================================//

TEST( SharedRandomMove, TheFileStaticSeedHashCameWithTheFamily )
{
	const std::string lib = Code( ReadFile( kHome ) );
	ASSERT_FALSE( lib.empty() ) << kHome << " did not load";

	EXPECT_EQ( 1u, CountOf( lib, "static int SeedFileLineHash(" ) )
		<< "SeedFileLineHash is FILE-STATIC storage; left behind, the four "
		   "functions here would hash with a different function and the "
		   "prediction seed would disagree between the two sides";
	// kUtilShrC was deleted by plan_shim.md Phase 2c, lane 2-utilshared, so the
	// "not still there" half is the absence of the file. Kept rather than
	// dropped: a file coming back with a second SeedFileLineHash in it is
	// precisely the split this fact exists to forbid, and it is not a compile
	// or link error.
	EXPECT_FALSE( FileExists( kUtilShrC ) )
		<< kUtilShrC << " exists again, and it is where SeedFileLineHash and "
		<< "the four were before. A second file-static seed hash is a second "
		<< "random stream nothing reports.";

	// Each of the four still calls it, so the family is whole rather than
	// merely co-located. With the ARGUMENT, so a forward declaration cannot
	// satisfy the needle.
	EXPECT_EQ( 4u, CountOf( lib, "SeedFileLineHash( EntityPrediction_Seed(), sharedname, additionalSeed )" ) )
		<< "all four bodies must still hash through it";
}

//=============================================================================//
// 2. EXACTLY ONE DEFINITION EACH, AND IT IS THE SHIM'S
//=============================================================================//

TEST( SharedRandomMove, EveryMovedDefinitionIsInTheShimFile )
{
	const std::string lib = Code( ReadFile( kHome ) );
	ASSERT_FALSE( lib.empty() ) << kHome << " did not load";
	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMoved[ i ] ) )
			<< kMoved[ i ] << " is not defined exactly once in " << kHome;
	}
}

TEST( SharedRandomMove, NoMovedDefinitionWasLeftBehindInUtilShared )
{
	// The file is gone (Phase 2c, lane 2-utilshared), so the absence is the
	// file's. The library is proved read in the same fact so this cannot pass
	// over two missing files instead of one.
	EXPECT_FALSE( FileExists( kUtilShrC ) )
		<< kUtilShrC << " exists again — stock's callers, animation.cpp among "
		<< "them, would bind whichever of the two bodies the linker picked and "
		<< "the link edge this move cut would be back.";

	const std::string lib = Code( ReadFile( kHome ) );
	ASSERT_FALSE( lib.empty() ) << kHome << " did not load";
	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMoved[ i ] ) )
			<< kMoved[ i ] << " is not in " << kHome << " either.";
	}

	// Nor in the shared library it left: a second body there, compiled into
	// server.so as well, is the split the first half of this fact forbids.
	const std::string shared = Code( ReadFile( kLibrary ) );
	ASSERT_FALSE( shared.empty() ) << kLibrary << " did not load";
	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 0u, CountOf( shared, kMoved[ i ] ) )
			<< kMoved[ i ] << " is defined in " << kLibrary << " again.";
	}
}

//=============================================================================//
// 3. THE DECLARATIONS DID NOT MOVE, SO NO CALLER ON EITHER SIDE CHANGED
//=============================================================================//

TEST( SharedRandomMove, TheDeclarationsStayInUtilSharedH )
{
	const std::string h = ReadFile( kUtilShrH );
	ASSERT_FALSE( h.empty() ) << kUtilShrH << " did not load";
	for ( size_t i = 0; i < kNumDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( h, kStillDeclared[ i ] ) )
			<< kStillDeclared[ i ] << " must still be declared in " << kUtilShrH;
	}
}

//=============================================================================//
// 4. BOTH TARGETS COMPILE THE LIBRARY — the failure that is silent on one side
//=============================================================================//

TEST( SharedRandomMove, TheServerCompilesIt )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibrary ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) must compile "
		<< kLibrary;
}

TEST( SharedRandomMove, TheClientCompilesItToo )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kLibrary ) )
		<< "client_sourcesharp (src/client/native/CMakeLists.txt) must compile "
		<< kLibrary << " as well: the family's callers are on both sides, so a "
		   "one-sided row leaves one side with a definition and the other with "
		   "none";
	// And the family's own home, which only the client compiles.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kHome ) )
		<< "client_sourcesharp must compile " << kHome
		<< ", where the shared-random family is defined";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kHome ) )
		<< "server_sourcesharp compiles the client's " << kHome;
}

//=============================================================================//
// 5. THE INCLUDES THE MOVED BODIES NEED CAME WITH THEM
//=============================================================================//

// The tier0/memdbgon.h ordering half of this test went with the include itself
// (plan_server_novalve.md P0): memdbgon.h is a no-op on POSIX (memalloc.h
// defines USE_MEM_DEBUG only for _MEMTEST on _WIN32), and server.so is leaving
// tier0, so there is no last-include position left to hold.
TEST( SharedRandomMove, TheLibraryIncludesWhatTheMovedBodiesRead )
{
	const std::string raw = ReadFile( kHome );
	ASSERT_FALSE( raw.empty() ) << kHome << " did not load";

	EXPECT_NE( std::string::npos, raw.find( "#include \"checksum_crc.h\"" ) );
	EXPECT_NE( std::string::npos, raw.find( "#include \"entity_prediction_seed.h\"" ) );
	EXPECT_NE( std::string::npos, raw.find( "#include <vstdlib/random.h>" ) );
}
