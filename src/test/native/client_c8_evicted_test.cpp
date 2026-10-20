// plan_client_novalve C8 (lane c6-input): the util and registry TUs that left
// client.so with nothing to replace them, and the calls into them that went
// too. Read from the farm, which holds only what client.so compiles (tests run
// with cwd farm/<config>/src); a sibling that stays proves the root resolved.

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <cstdio>
#include <vector>

#include <fstream>
#include <sstream>
#include <string>

namespace
{
std::string Read( const char *pszRel )
{
	std::ifstream f( pszRel );
	if ( !f.good() )
		return std::string();
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

const char kShellDll[] = "sourcesharp/game/client/shell_clientdll.cpp";
} // namespace

TEST( ClientC8Evicted, TheRootResolves )
{
	ASSERT_FALSE( Read( kShellDll ).empty() ) << "the working directory is not farm/<config>/src";
}

// initializer.cpp's registry had no registrant: no object on the link line
// constructed an Initializer (nm), so InitializeAllObjects walked an empty list
// and answered true, and FreeAllObjects did nothing. crtmemdebug.cpp's two
// functions are empty unless USECRTMEMDEBUG, which is MSVC's CRT and never
// defined here.
TEST( ClientC8Evicted, TheEmptyRegistryAndTheCrtDebugHookLeft )
{
	const std::string shell = Read( kShellDll );
	ASSERT_FALSE( shell.empty() );

	EXPECT_TRUE( Read( "game/client/initializer.cpp" ).empty() ) << "initializer.cpp is back in the farm";
	EXPECT_TRUE( Read( "public/crtmemdebug.cpp" ).empty() ) << "crtmemdebug.cpp is back in the farm";
	EXPECT_EQ( std::string::npos, shell.find( "Initializer::InitializeAllObjects()" ) );
	EXPECT_EQ( std::string::npos, shell.find( "Initializer::FreeAllObjects()" ) );
	EXPECT_EQ( std::string::npos, shell.find( "InitCRTMemDebug()" ) );
}

// precache_register.cpp's list had no registrant either: no object on the link
// line constructs a CPrecacheRegister (nm), so C_World::Precache's walk ran
// nothing.
TEST( ClientC8Evicted, TheEmptyPrecacheRegistryLeft )
{
	const std::string world = Read( "sourcesharp/game/client/world_adapter_client.cpp" );
	ASSERT_FALSE( world.empty() );
	EXPECT_TRUE( Read( "game/shared/precache_register.cpp" ).empty() ) << "precache_register.cpp is back in the farm";
	EXPECT_EQ( std::string::npos, world.find( "CPrecacheRegister::Precache()" ) );
	// Retention: the precaches that did run still do.
	EXPECT_NE( std::string::npos, world.find( "W_Precache();" ) );
}

// cdll_util.cpp: no object on the link line imported any of its 54 strong
// definitions (nm); its callers' bodies had already moved to their own sides.
TEST( ClientC8Evicted, CdllUtilLeft )
{
	EXPECT_TRUE( Read( "game/client/cdll_util.cpp" ).empty() ) << "cdll_util.cpp is back in the farm";
	// The root resolved: a sibling in the same farm directory that is still
	// linked. This was clientshadowmgr.cpp until it, the client's last fork TU,
	// left client.so with C7 cluster 12 S2 (2026-10-19); no fork .cpp is
	// compiled under game/client now, so the sibling is cdll_util.cpp's own
	// header, which client.so's TUs still include (sdk-links/client.txt). An
	// unresolved root reads it empty, and the eviction check above could not
	// tell that from a gone file.
	EXPECT_FALSE( Read( "game/client/cdll_util.h" ).empty() ) << "the farm's game/client did not resolve";
}

// game_entity_relays.h's client sites (docs/client-shell.md 2.2), the two in
// files this lane owns. Both consumers had no caller on the link line (nm -u):
// the free AddRestoredEntity (game/shared/saverestore.cpp's proxy, which no
// compiled TU calls) and C_LocalTempEntity::SetTempEntOwner (the pool sets the
// owner by handle, SetTempEntOwnerHandle). The restore list they filled was
// therefore always empty, so DispatchOnRestore's walk went too; the slot stays,
// because the engine calls it.
TEST( ClientC8Evicted, TheTwoOwnedRelaySitesLeftWithTheirDeadConsumers )
{
	const std::string dll = Read( kShellDll );
	const std::string tempents = Read( "sourcesharp/game/client/shell_legacy_tempents_client.cpp" );
	ASSERT_FALSE( dll.empty() );
	ASSERT_FALSE( tempents.empty() );

	EXPECT_EQ( std::string::npos, dll.find( "GameEntity_RefHandle(" ) );
	EXPECT_EQ( std::string::npos, dll.find( "void AddRestoredEntity( IGameEntity *pEntity )" ) );
	EXPECT_EQ( std::string::npos, dll.find( "m_RestoredEntities" ) );
	EXPECT_EQ( std::string::npos, tempents.find( "GameEntity_RefHandle(" ) );
	EXPECT_EQ( std::string::npos, tempents.find( "C_LocalTempEntity::SetTempEntOwner( IGameEntity" ) );

	// Retention: the engine's slot is still answered.
	EXPECT_NE( std::string::npos, dll.find( "void CShellClientDLL::DispatchOnRestore()" ) );
}

namespace
{
// `nm -C --undefined-only` over every object on client_sourcesharp's link edge.
std::string UndefinedOnTheClientLink( size_t *pnObjects )
{
	const std::string strBuild = cmake_graph::BuildDir();
	const std::vector< std::string > rel = cmake_graph::LinkObjects( cmake_graph::kClient );
	std::string cmd = "nm -C --undefined-only";
	size_t n = 0;
	for ( size_t i = 0; i < rel.size(); i++ )
	{
		if ( rel[ i ].size() > 2 && rel[ i ].compare( rel[ i ].size() - 2, 2, ".o" ) == 0 )
		{
			cmd += " '" + ( rel[ i ][ 0 ] == '/' ? rel[ i ] : strBuild + "/" + rel[ i ] ) + "'";
			n++;
		}
	}
	*pnObjects = n;
	cmd += " 2>/dev/null";
	FILE *pPipe = popen( cmd.c_str(), "r" );
	if ( !pPipe )
		return std::string();
	std::string out;
	char buf[ 1024 ];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		out += buf;
	pclose( pPipe );
	return out;
}
} // namespace

// client_thinklist.cpp left (C8): no object on the link line schedules a
// client think, so CClientThinkList walked an empty list. A think scheduler
// added later must fail HERE rather than silently never run: these are the
// entry points one would import.
TEST( ClientC8Evicted, NothingOnTheLinkSchedulesAClientThink )
{
	size_t nObjects = 0;
	const std::string undefined = UndefinedOnTheClientLink( &nObjects );
	ASSERT_GT( nObjects, 100u ) << "the client's link edge named too few objects";
	// Proof of read: a symbol the link line does import from outside.
	ASSERT_NE( std::string::npos, undefined.find( "ConVar::" ) ) << "nm read nothing";

	EXPECT_EQ( std::string::npos, undefined.find( "SetNextClientThink" ) )
		<< "an object imports SetNextClientThink: a client think is scheduled and nothing runs it";
	EXPECT_EQ( std::string::npos, undefined.find( "AddToThinkList" ) )
		<< "an object imports AddToThinkList: a client think is scheduled and nothing runs it";
	EXPECT_EQ( std::string::npos, undefined.find( "g_ClientThinkList" ) );
	EXPECT_TRUE( Read( "game/client/client_thinklist.cpp" ).empty() ) << "client_thinklist.cpp is back in the farm";
}

// The value the two constructors now spell is the one the list's
// INVALID_THINK_HANDLE answered: CUtlLinkedList< ThinkEntry_t, unsigned short >
// ::InvalidIndex(), which is (unsigned short)-1.
TEST( ClientC8Evicted, TheInvalidThinkHandleIsTheListsInvalidIndex )
{
	EXPECT_EQ( 0xFFFFu, (unsigned)(unsigned short)-1 );
	const char *const kFiles[] = {
		"sourcesharp/game/client/shell_standalone_client.cpp",
		"sourcesharp/game/client/world_adapter_client.cpp",
	};
	for ( const char *pszFile : kFiles )
	{
		const std::string text = Read( pszFile );
		ASSERT_FALSE( text.empty() ) << pszFile;
		EXPECT_NE( std::string::npos, text.find( "m_hThink( (ClientThinkHandle_t)(uintp)0xFFFF )" ) ) << pszFile;
		EXPECT_EQ( std::string::npos, text.find( "INVALID_THINK_HANDLE )" ) ) << pszFile;
	}
}
