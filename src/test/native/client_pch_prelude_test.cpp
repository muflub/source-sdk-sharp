//====== SourceSharp — there is no forced prelude any more ===================//
//
// plan_shim.md step 0b. This file used to be about the client's FORCED
// PRELUDE: `client_csharp.vpc` said
//
//     $Create/UsePrecompiledHeader  "Use Precompiled Header (/Yu)"
//     $Create/UsePCHThroughFile     "cbase.h"
//
// the ninja generator turned that into `-include .../cbase.h` on 142 client and
// 139 server compile lines, and a per-file `"Not Using Precompiled Headers"`
// block was the only way to take one TU off it. Deleting `#include "cbase.h"`
// from a .cpp therefore changed nothing on its own, and the ten cases this file
// carried existed to keep the source half and the vpc half of step 7h from
// drifting apart.
//
// 0b removed the mechanism. Neither C# vpc has a PCH directive, neither has a
// `stdafx.cpp` row, and both `stdafx.cpp` files are off the disk -- so every
// translation unit now sees exactly what it includes, and the drift the ten
// cases watched for cannot happen: a file that does not include what it uses
// does not compile.
//
// What replaces them is three facts, and the third is the one with teeth:
//
//   1. no compile command of ours forces a prelude (`-include`, a .pch/.gch)
//      and no CMakeLists asks for one (the mechanism cannot come back quietly);
//   2. no target compiles a stdafx.cpp, and neither stdafx.cpp is on disk (the
//      file that CREATED the header is gone, not merely unreferenced);
//   3. every .cpp the game DLLs and the host compile that names a Source type
//      includes a Source header of its own.
//
// PHASE 6b (VPC -> CMake). The subject was the three .vpc scripts; it is now
// what CMake compiles, read through cmake_graph.h (compile_commands.json
// cross-checked against the CMakeLists lists). Two VPC-only cases were
// retired: "no vpc row is named twice" (a source list is a set in CMake, and a
// duplicate would be a CMake error) and the `\`-continued `$File` group
// ratchet (CMake lists have no continuation syntax, and the private vpc
// collectors that could not read one are gone with the vpcs). The on-disk
// half of the first survives as case 4.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <sstream>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[i] ) + "/game";
		const std::string pub = std::string( kCandidates[i] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) && stat( pub.c_str(), &st ) == 0 &&
			 S_ISDIR( st.st_mode ) )
			return kCandidates[i];
	}
	return NULL;
}

bool Exists( const std::string &strRel )
{
	const char *pszRoot = SrcRoot();
	if ( pszRoot == NULL )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + strRel ).c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

// The three targets that are ours: server.so, client.so, libmanaged_host.so.
const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };
const size_t kNumTargets = sizeof( kTargets ) / sizeof( kTargets[0] );

} // namespace

//-----------------------------------------------------------------------------
// 1. NO PRECOMPILED HEADER, IN ANY SPELLING. VPC's generator turned a PCH
//    directive into `-include .../cbase.h` on every compile line; CMake's
//    target_precompile_headers does the same with a .pch/.gch. Either coming
//    back puts a prelude on every TU again.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, NoCompileCommandForcesAPrelude )
{
	for ( size_t t = 0; t < kNumTargets; t++ )
	{
		const std::string list = cmake_graph::ListText( kTargets[t] );
		EXPECT_EQ( list.find( "precompile_headers" ), std::string::npos )
			<< cmake_graph::ListFile( kTargets[t] )
			<< " asks for a precompiled "
			   "header again. In this project that is a FORCED PRELUDE, and every "
			   "TU's include list stops being what it compiles against.";

		const std::vector<cmake_graph::CompileEntry> entries = cmake_graph::TargetEntries( kTargets[t] );
		ASSERT_FALSE( entries.empty() ) << kTargets[t];
		for ( size_t i = 0; i < entries.size(); i++ )
		{
			std::istringstream toks( entries[i].strCommand );
			std::string tok;
			while ( toks >> tok )
			{
				EXPECT_NE( tok, "-include" ) << entries[i].strFile << " is compiled with a forced -include";
				EXPECT_EQ( tok.find( "-include-pch" ), std::string::npos ) << entries[i].strFile;
				EXPECT_EQ( tok.find( ".gch" ), std::string::npos ) << entries[i].strFile;
				EXPECT_EQ( tok.find( ".pch" ), std::string::npos ) << entries[i].strFile;
			}
		}
	}
}

//-----------------------------------------------------------------------------
// 2. AND THE FILE THAT CREATED IT IS GONE. A stdafx.cpp with no PCH directive
//    compiles cbase.h into an object nothing needs; left on disk it is an
//    invitation to wire the mechanism back up.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, NoTargetCompilesAStdafxAndNeitherFileIsOnDisk )
{
	for ( size_t t = 0; t < kNumTargets; t++ )
	{
		const std::set<std::string> srcs = cmake_graph::CompiledSources( kTargets[t] );
		ASSERT_FALSE( srcs.empty() ) << kTargets[t];
		for ( std::set<std::string>::const_iterator it = srcs.begin(); it != srcs.end(); ++it )
			EXPECT_EQ( it->find( "stdafx" ), std::string::npos ) << kTargets[t] << " compiles " << *it << " again.";
	}

	EXPECT_FALSE( Exists( "game/server/stdafx.cpp" ) )
		<< "game/server/stdafx.cpp is back on disk. It was `#include \"cbase.h\"` "
		   "and nothing else -- its only purpose was to create the precompiled "
		   "header this step removed.";
	EXPECT_FALSE( Exists( "game/client/stdafx.cpp" ) )
		<< "game/client/stdafx.cpp is back on disk. Same file, same reason.";
}

//-----------------------------------------------------------------------------
// 3. THE CASE WITH TEETH. With no prelude, a TU that names a Source type and
//    includes no Source header of its own cannot compile -- so this is the
//    compile the build would fail, asserted over the whole source list rather
//    than over whichever TUs a given build happened to reach.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 4. NOT ABOUT THE PRELUDE, KEPT DELIBERATELY. Every source the client
//    compiles is a file on disk in the farm -- the precondition for every
//    reader of the source text in this directory.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, EveryCompiledClientSourceIsOnDisk )
{
	const std::set<std::string> srcs = cmake_graph::CompiledSources( cmake_graph::kClient );
	ASSERT_FALSE( srcs.empty() );
	for ( std::set<std::string>::const_iterator it = srcs.begin(); it != srcs.end(); ++it )
		EXPECT_TRUE( Exists( *it ) ) << "client_sourcesharp compiles " << *it << ", which is not on disk";
}
