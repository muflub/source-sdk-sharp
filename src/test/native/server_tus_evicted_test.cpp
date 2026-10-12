//========= SourceSharp — the first two stock TUs out of the server vpc =======//
//
// plan_entity.md Phase 4 EXIT. tools/p4-exit-batches measures the stock link
// graph's one CYCLE — the set of stock translation units that must leave
// server_csharp.vpc in a single commit — and the two files this file is about
// are NOT in it. Each is a single-TU batch that can leave on its own, and each
// has now left:
//
//     game/server/movehelper_server.cpp   428 lines   EVICTED, left on disk
//     game/server/playerlocaldata.cpp     284 lines   MOVED to the shim
//
// They are the first stock TUs of the Phase 4 component region to leave either
// vpc at all. Steps 4a–4f each landed real code moves and removed none.
//
// WHY EACH NEEDS A FACT RATHER THAN A GREEN BUILD.
//
//   1. movehelper_server.cpp was EVICTED, not moved, and an eviction is the
//      recorded `a compile gate selects for deletion` shape. What makes it
//      safe is a specific measured claim: the file contributed exactly ONE
//      thing to this build, the storage IMPLEMENT_MOVEHELPER() declares, and
//      its CMoveHelperServer is never constructed because MoveHelperServer()'s
//      only callers are in game/server/player.cpp, which this project does not
//      compile. If a caller of MoveHelperServer() ever appears in a compiled
//      file, that claim is dead and the eviction is a silent loss of the touch
//      list, the impact processing and the falling-damage path. The fact below
//      is what notices.
//
//   2. The evicted file is STILL ON DISK and still carries its own
//      IMPLEMENT_MOVEHELPER(). Two definitions of IMoveHelper::sm_pSingleton
//      is a duplicate-symbol link error, which is loud — but only if the row
//      stays out. A row put back is not a compile error in any file this suite
//      reads, so the source list is asserted directly.
//
//   3. playerlocaldata.cpp MOVED rather than being evicted, because 4g
//      measured it as LIVE port code. A move that left a copy behind would
//      give the link two definitions of the send table DT_Local, and a move
//      whose new file is not compiled gives it none.
//
//=============================================================================//

#include <cstdio>
#include <fstream>
#include <sstream>
#include <string>
#include <sys/stat.h>

#include "gtest/gtest.h"
#include "cmake_graph.h"

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

bool Exists( const char *pPath )
{
	struct stat st;
	return stat( pPath, &st ) == 0;
}

// WHAT EACH SIDE COMPILES is its CMake target's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/{server,client}/native/CMakeLists.txt). The shim row below doubles as
// the server side's proof-of-read.
const char *const kMoveHelperRow  = "game/server/movehelper_server.cpp";
const char *const kLocalDataRow   = "game/server/playerlocaldata.cpp";
const char *const kShimLocalRow   = "sourcesharp/game/server/shell_playerlocaldata.cpp";
const char *const kProofOfRead    = "sourcesharp/game/shared/shell_shared.cpp";

const char *const kShimPlayer        = "sourcesharp/game/server/shell_standalone_player.cpp";

} // anonymous namespace

//=============================================================================//
// 1. THE ROWS ARE OUT
//=============================================================================//

TEST( ServerTusEvicted, TheServerNoLongerCompilesMoveHelperServer )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kProofOfRead ) )
		<< "server_sourcesharp does not compile " << kProofOfRead
		<< " -- the reader is not reading the server's source list";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kMoveHelperRow ) )
		<< kMoveHelperRow << " is back in server_sourcesharp "
		   "(src/server/native/CMakeLists.txt), which is a duplicate definition "
		   "of IMoveHelper::sm_pSingleton against the one in "
		   "shell_standalone_player.cpp";
}

TEST( ServerTusEvicted, TheServerNoLongerCompilesStockPlayerLocalData )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kProofOfRead ) )
		<< "server_sourcesharp does not compile " << kProofOfRead;
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kLocalDataRow ) )
		<< "the stock playerlocaldata.cpp is back in server_sourcesharp; with the "
		   "shim also compiled that is two definitions of the DT_Local send table";
}

TEST( ServerTusEvicted, TheShimPlayerLocalDataIsCompiledAndOnlyOnTheServer )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kProofOfRead ) )
		<< "client_sourcesharp does not compile " << kProofOfRead
		<< " -- the reader is not reading the client's source list";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShimLocalRow ) )
		<< "without " << kShimLocalRow << " in server_sourcesharp nothing defines "
		   "UpdateAllClientData and the server does not link";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kShimLocalRow ) )
		<< "this is game/server code: it was never in the client build and must "
		   "not arrive in client_sourcesharp with the move";
}

TEST( ServerTusEvicted, TheStockPlayerLocalDataSourceIsGoneFromDisk )
{
	// A MOVE, not a copy: the .cpp does not stay. (The header half -- "the
	// header stays, every includer is unchanged" -- is retired:
	// game/server/playerlocaldata.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6.)
	//
	// That half was also what kept the absence below from being vacuous, so a
	// sibling in the same directory that IS in the farm takes over that job: a
	// cwd from which game/server cannot be seen at all reddens here.
	ASSERT_TRUE( Exists( "game/server/activator.h" ) )
		<< "game/server/activator.h is not visible from the test's cwd, so the "
		   "absence below would prove nothing about game/server";
	EXPECT_FALSE( Exists( "game/server/playerlocaldata.cpp" ) )
		<< "a copy of the file is still at its old address; it is not compiled, "
		   "so it does not build, but the next reader has two files to choose "
		   "between and one of them is stale";
}

//=============================================================================//
// 2. WHAT MADE THE EVICTION SAFE — the claim, not the outcome
//=============================================================================//

TEST( ServerTusEvicted, NoCompiledFileCallsMoveHelperServer )
{
	// THE CLAIM THE EVICTION RESTS ON. CMoveHelperServer is constructed by
	// exactly one function-local static, inside MoveHelperServer(), so if
	// nothing calls MoveHelperServer() the object never exists and the file's
	// touch list, impact processing and falling-damage path were never reached.
	//
	// The needle carries the CALL PARENTHESES so the declaration in
	// game/server/movehelper.h cannot satisfy it — 4f's recorded
	// SceneManager_ClientActive shape.
	//
	// The two files checked are the ones that could plausibly acquire such a
	// call: the shim player, which owns the live IMoveHelper, and the shim
	// frame steps, which drive the command loop. game/server/player.cpp does
	// call it and is deliberately NOT read here: server_sourcesharp does not compile it
	// and the fact would be red for a file that does not build.
	const std::string player = Code( ReadFile( kShimPlayer ) );
	ASSERT_FALSE( player.empty() );
	EXPECT_EQ( 0u, CountOf( player, "MoveHelperServer()" ) )
		<< kShimPlayer << " now calls MoveHelperServer(), whose CMoveHelperServer "
		   "left the build with movehelper_server.cpp";

	const std::string steps = Code( ReadFile( "sourcesharp/game/server/shell_frame.cpp" ) );
	ASSERT_FALSE( steps.empty() );
	EXPECT_EQ( 0u, CountOf( steps, "MoveHelperServer()" ) )
		<< "shell_frame.cpp now calls MoveHelperServer()";
}

TEST( ServerTusEvicted, TheSingletonStorageIsDeclaredExactlyOnceAndInTheShim )
{
	const std::string player = Code( ReadFile( kShimPlayer ) );
	ASSERT_FALSE( player.empty() );
	// P5: the storage is ss::IMoveHelper's (ss_movehelper.h), defined in full
	// where IMPLEMENT_MOVEHELPER() used to expand.
	EXPECT_EQ( 1u, CountOf( player, "ss::IMoveHelper *ss::IMoveHelper::sm_pSingleton = 0;" ) )
		<< "imovehelper.h says to put this in the file that IMPLEMENTS the "
		   "IMoveHelper, and CStandaloneMoveHelper here is the only one this "
		   "build constructs";

	// The half that re-read the evicted movehelper_server.cpp for its own
	// IMPLEMENT_MOVEHELPER() was culled when that file left the source tree
	// (server.so does not compile it).
}

TEST( ServerTusEvicted, TheStandaloneMoveHelperIsStillTheOneThatInstallsItself )
{
	const std::string player = Code( ReadFile( kShimPlayer ) );
	ASSERT_FALSE( player.empty() );
	// With the ARGUMENT: `SetSingleton( this )` is the install, and a bare
	// `SetSingleton` would also match the declaration in imovehelper.h if this
	// file ever grew one.
	EXPECT_EQ( 1u, CountOf( player, "SetSingleton( this );" ) )
		<< "nothing installs an IMoveHelper any more, so MoveHelper() answers "
		   "NULL and the movement loop's AddToTouched dereferences it";
	EXPECT_EQ( 1u, CountOf( player, "s_StandaloneMoveHelper.Install( this );" ) )
		<< "and the install site the command loop scopes is gone";
}
