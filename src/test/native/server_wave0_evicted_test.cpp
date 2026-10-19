//====== SourceSharp — plan_server_all.md Wave 0: the free ones ==============//
//
// Wave 0 moves no logic. It exists to prove the eviction loop end to end and
// to correct the meter, which was counting things it should not have been.
// Three separate facts land here, and each is a fact about a file somebody may
// change for their own reasons — red is not automatically a regression, but it
// does mean the arithmetic in plan_server_all.md section 1 is stale.
//
// 1. FOUR FILES WERE NEVER BORROWED C++.  entity_bases.cpp,
//    baseentity_statics.cpp, precache_model.cpp and toucher.cpp open with
//    "//========= SourceSharp" and were written by this port in August 2026,
//    carved out of baseentity.cpp ahead of its removal. They lived at
//    game/server/ and were therefore counted by the meter as stock Valve code.
//    toucher.cpp was the contradiction in visible form: it was already listed
//    inside server_csharp.vpc's own "SourceSharp" folder while living at
//    game/server/. Moving all four is a rename — same text, same include
//    resolution, both directories are on $AdditionalIncludeDirectories.
//
// 2. TWO $File LINES NAMED TRANSLATION UNITS THAT CANNOT BE BUILT.
//    replay_gamestats_shared.cpp sat behind [$BUILD_REPLAY] and
//    haptic_utils.cpp behind [$WINDOWS]. Neither conditional is set in either
//    configuration this project generates, so ninja has never emitted a rule
//    for either. A $File line for a TU that cannot be built is a claim, not a
//    dependency.
//
// 3. THREE FILES PROBED CLEAN IN BOTH CONFIGURATIONS.  h_export.cpp,
//    dt_utlvector_send.cpp and interpolatortypes.cpp. The rationale block
//    lived in server_csharp.vpc, which is gone; each verdict is restated in
//    its assertion message below.
//
//=============================================================================//

#include <set>
#include <string>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

bool Exists( const std::string &strPath )
{
	struct stat st;
	return stat( strPath.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt). Paths are farm-relative.
//
// "This file is out" means NO compiled source has that basename, under any
// directory -- the old VPC reader matched a bare basename for the same reason
// (a file listed at either spelling answers). Never a suffix match.
bool ServerCompilesBasename( const char *pszBasename )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	const std::string base( pszBasename );
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		const size_t slash = it->find_last_of( '/' );
		if ( ( slash == std::string::npos ? *it : it->substr( slash + 1 ) ) == base )
			return true;
	}
	return false;
}

// The proof-of-read, asserted by every test before its absences: the port's own
// shim, which no eviction can take.
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

} // namespace

//-----------------------------------------------------------------------------
// 1. The four SourceSharp-authored files moved, and the build follows them.
//-----------------------------------------------------------------------------
TEST( ServerWave0, TheFourOwnFilesLiveUnderSourceSharp )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	// baseentity_statics and entity_bases LEFT THIS LIST
	// (plan_server_native_min P1): both .cpp files were deleted, and their
	// absence at both paths is asserted below.
	static const char *kOurs[] = { "precache_model" };   // toucher.cpp went in plan_server_native_v2 T2

	for ( size_t i = 0; i < sizeof( kOurs ) / sizeof( kOurs[ 0 ] ); i++ )
	{
		const std::string strBase = kOurs[ i ];
		const std::string strStock = std::string( pszRoot ) + "/game/server/" + strBase;
		const std::string strMine  = std::string( pszRoot )
			+ "/sourcesharp/game/server/" + strBase;

		EXPECT_TRUE( Exists( strMine + ".cpp" ) )
			<< strBase << ".cpp is not under sourcesharp/game/server/. It "
			<< "opens with \"//========= SourceSharp\" and was written by this "
			<< "port; while it sits in game/server/ the meter in "
			<< "plan_server_all.md section 1 counts it as borrowed C++.";
		EXPECT_TRUE( Exists( strMine + ".h" ) )
			<< strBase << ".h did not move with its .cpp.";

		EXPECT_FALSE( Exists( strStock + ".cpp" ) )
			<< strBase << ".cpp is back under game/server/ as well. Two copies "
			<< "on two include paths -- both directories are on this project's "
			<< "$AdditionalIncludeDirectories -- is how a stale definition wins "
			<< "silently.";
		EXPECT_FALSE( Exists( strStock + ".h" ) )
			<< strBase << ".h is back under game/server/ as well.";
	}

	// DELETED, not moved: one unreferenced send proxy and one write-only
	// global (plan_server_native_min P1). Neither path may hold it.
	static const char *kGone[] = { "baseentity_statics", "entity_bases" };
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		const std::string strGone = kGone[ i ];
		EXPECT_FALSE( Exists( std::string( pszRoot ) + "/sourcesharp/game/server/" + strGone + ".cpp" ) )
			<< strGone << ".cpp is back under sourcesharp/game/server/.";
		EXPECT_FALSE( Exists( std::string( pszRoot ) + "/game/server/" + strGone + ".cpp" ) )
			<< strGone << ".cpp is back under game/server/.";
	}
}

TEST( ServerWave0, TheServerCompilesTheFourAtTheirNewPaths )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	// ONE OF THE FOUR LEFT THE BUILD WITH THE FLIP, and it is asserted below
	// rather than dropped from the list. `entity_bases.cpp` held the C++ entity
	// BASE CLASSES, so the flip took its row out of the server's list with
	// the rest of the entity hierarchy -- the row is gone, and the .cpp and .h
	// are both still on disk, which the move facts above still check.
	//
	// The claim this test makes is unchanged for the other three, and for
	// entity_bases.cpp it is INVERTED and still two-sided: the sourcesharp path
	// must not be listed (the flip removed it), the bare basename must not be
	// listed either (the old path, same as the other three), and the file must
	// still be where the move put it.
	static const char *kOurs[] = { "precache_model.cpp" };   // toucher.cpp went in plan_server_native_v2 T2

	for ( size_t i = 0; i < sizeof( kOurs ) / sizeof( kOurs[ 0 ] ); i++ )
	{
		const std::string want = std::string( "sourcesharp/game/server/" ) + kOurs[ i ];

		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, want ) )
			<< kOurs[ i ] << " is not listed at its sourcesharp path. If it is "
			<< "not listed at all the server loses a definition; if it is "
			<< "listed at game/server/ it is being read from where it no longer "
			<< "lives.";

		// ...and not ALSO at the old one. A build list is happy to compile a
		// file twice; the linker is not.
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, std::string( "game/server/" ) + kOurs[ i ] ) )
			<< kOurs[ i ] << " is compiled from game/server/ as well as from its "
			<< "sourcesharp path -- that is two translation units for one "
			<< "file and a duplicate-symbol link.";
	}

	// baseentity_statics.cpp was deleted (plan_server_native_min P1), so no
	// spelling of it may be compiled.
	EXPECT_FALSE( ServerCompilesBasename( "baseentity_statics.cpp" ) )
		<< "a baseentity_statics.cpp is compiled again; it was deleted.";

	// THE FOURTH, which the flip took. Neither spelling may be present.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/entity_bases.cpp" ) )
		<< "entity_bases.cpp is listed at its sourcesharp path again. The flip "
		<< "removed that row: it defines the C++ entity base classes, and "
		<< "compiling it again puts the hierarchy back on the server link line.";
	EXPECT_FALSE( ServerCompilesBasename( "entity_bases.cpp" ) )
		<< "an entity_bases.cpp is compiled from another directory, so it is being compiled "
		<< "out of game/server/ -- where it no longer lives, and where the flip "
		<< "removed it from anyway.";

	// AND THE HALF THAT IS NOT AN ABSENCE is the two kOurs rows above, which
	// must be compiled. entity_bases.cpp itself was deleted
	// (plan_server_native_min P1): the flip had already taken its build row.
}

//-----------------------------------------------------------------------------
// 2. The two entries no configuration could ever build.
//-----------------------------------------------------------------------------
TEST( ServerWave0, NoFileLineNamesATranslationUnitThisBuildCannotProduce )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	EXPECT_FALSE( ServerCompilesBasename( "replay_gamestats_shared.cpp" ) )
		<< "replay_gamestats_shared.cpp is listed again. It is guarded by "
		<< "[$BUILD_REPLAY], which this project sets in neither configuration, "
		<< "so VPC's ninja never emitted a rule for it; a row for it in "
		<< "src/server/native/CMakeLists.txt would be compiled for the first time.";

	EXPECT_FALSE( ServerCompilesBasename( "haptic_utils.cpp" ) )
		<< "haptic_utils.cpp is listed again. It is guarded by [$WINDOWS] and "
		<< "this port builds Linux only.";

	// The conditionals themselves are the reason, so if they ever start being
	// set this test should be the thing that says so. VPC spelled it as a
	// per-row [$BUILD_REPLAY] conditional; CMake has no per-row conditional, so
	// the analogue is the target's own CMakeLists mentioning it at all (an
	// if(BUILD_REPLAY) block or a -DBUILD_REPLAY).
	const std::string list = cmake_graph::ListText( cmake_graph::kServer );
	ASSERT_NE( list.find( "set(server_sourcesharp_sources" ), std::string::npos )
		<< "src/server/native/CMakeLists.txt does not hold the server's source "
		<< "list -- the reader is not reading it";
	EXPECT_EQ( list.find( "BUILD_REPLAY" ), std::string::npos )
		<< "src/server/native/CMakeLists.txt names BUILD_REPLAY -- the premise "
		<< "of the removal above was that nothing in this project is "
		<< "conditional on it.";
}

//-----------------------------------------------------------------------------
// 3. The three probe verdicts.
//-----------------------------------------------------------------------------
TEST( ServerWave0, TheThreeProbedFilesAreOutOfTheServerFileList )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "the reader is not reading server_sourcesharp's source list";

	EXPECT_FALSE( ServerCompilesBasename( "h_export.cpp" ) )
		<< "h_export.cpp is back in the server list. On Linux its entire body "
		<< "is inside #if defined(_WIN32) && !defined(_XBOX), so it compiles "
		<< "to an EMPTY object -- nm reports 0 defined and 0 undefined "
		<< "symbols. It is the only genuinely free file in the section 8 "
		<< "survey and it buys the build nothing.";

	EXPECT_FALSE( ServerCompilesBasename( "dt_utlvector_send.cpp" ) )
		<< "dt_utlvector_send.cpp is back. It supplies SendPropUtlVector and "
		<< "four send proxies whose every caller -- choreoscene, choreoevent, "
		<< "sceneentity, BaseAnimatingOverlay, player.cpp, econ_item_view -- "
		<< "has already left this build.";

	EXPECT_FALSE( ServerCompilesBasename( "interpolatortypes.cpp" ) )
		<< "interpolatortypes.cpp is back. Its ten Interpolator_* curve "
		<< "helpers are named only by choreo* and keyframe.cpp, all gone from "
		<< "the server.";
}
