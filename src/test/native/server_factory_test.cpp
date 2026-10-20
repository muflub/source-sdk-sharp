//========= SourceSharp — server.so's interface factory ======================//
//
// shell_factory.cpp: one CreateInterface comparing the requested name (as a
// std::string_view, with ==) against a fixed table, in place of tier1's
// InterfaceReg list and the EXPOSE_*_INTERFACE macros. The behaviour facts link
// the real factory against stand-in rows carrying the engine's version names;
// the source facts check the real rows in the files that own the objects.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier1/interface.h"
#include "shell_factory.h"

#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <sys/stat.h>

namespace
{

int g_Objects[11];

template < int N >
void *StandIn()
{
	return &g_Objects[N];
}

// The engine's names, as the SDK headers spell them (eiface.h, iplayerinfo.h,
// IEffects.h, igamemovement.h). These are the contract with the engine and
// with server plugins, so they are written out here rather than included.
struct Expected
{
	const char *pszVersion;
	int nObject;
};

const Expected kExpected[] =
{
	{ "ServerGameDLL012", 0 },
	{ "ServerGameEnts001", 1 },
	{ "ServerGameClients005", 2 },
	{ "ServerGameClients004", 3 },
	{ "ServerGameClients003", 4 },
	{ "ServerGameTags001", 5 },
	{ "VServerDllSharedAppSystems001", 8 },
};

}

const SourceSharpExposed kExposeServerGameDll = { "ServerGameDLL012", StandIn< 0 > };
const SourceSharpExposed kExposeServerGameEnts = { "ServerGameEnts001", StandIn< 1 > };
const SourceSharpExposed kExposeServerGameClients = { "ServerGameClients005", StandIn< 2 > };
const SourceSharpExposed kExposeServerGameClients004 = { "ServerGameClients004", StandIn< 3 > };
const SourceSharpExposed kExposeServerGameClients003 = { "ServerGameClients003", StandIn< 4 > };
const SourceSharpExposed kExposeServerGameTags = { "ServerGameTags001", StandIn< 5 > };
const SourceSharpExposed kExposeServerDllSharedAppSystems = { "VServerDllSharedAppSystems001", StandIn< 8 > };

TEST( ServerFactory, EveryNameAnswersItsOwnObject )
{
	for ( const Expected &e : kExpected )
	{
		int rc = -1;
		EXPECT_EQ( (void *)&g_Objects[e.nObject], CreateInterface( e.pszVersion, &rc ) ) << e.pszVersion;
		EXPECT_EQ( IFACE_OK, rc ) << e.pszVersion;
	}
}

TEST( ServerFactory, AnUnknownNameAnswersNullAndFailed )
{
	int rc = IFACE_OK;
	EXPECT_EQ( nullptr, CreateInterface( "ServerGameDLL011", &rc ) );
	EXPECT_EQ( IFACE_FAILED, rc );
}

TEST( ServerFactory, ANullNameAnswersNullAndFailed )
{
	int rc = IFACE_OK;
	EXPECT_EQ( nullptr, CreateInterface( nullptr, &rc ) );
	EXPECT_EQ( IFACE_FAILED, rc );
}

TEST( ServerFactory, TheNameMatchesExactly )
{
	// strcmp's rule, as stock's registry used: no case folding, no prefix,
	// nothing trailing.
	EXPECT_EQ( nullptr, CreateInterface( "servergamedll012", nullptr ) );
	EXPECT_EQ( nullptr, CreateInterface( "ServerGameDLL01", nullptr ) );
	EXPECT_EQ( nullptr, CreateInterface( "ServerGameDLL0120", nullptr ) );
	EXPECT_EQ( nullptr, CreateInterface( "", nullptr ) );
}

TEST( ServerFactory, TheReturnCodeIsOptional )
{
	EXPECT_EQ( (void *)&g_Objects[5], CreateInterface( "ServerGameTags001", nullptr ) );
}

// Sys_GetFactoryThis went (plan_server_native_v2 T5): no server object
// imported it; the engine reaches the factory through the CreateInterface export.

// IEffects001 LEFT THE TABLE (P5 lane SP). The string is in no engine binary:
// of the 90 .so files under Source SDK Base 2013 Multiplayer only the stock
// game DLLs carry it, each exposing its own copy. Every server caller asks
// ss::Effects() now, so nothing asks the factory for it.
// PlayerInfoManager002 and PluginHelpersCheck001 LEFT THE TABLE with their
// files (plan_server_native_min P1, Decision 1): no engine binary asks for
// PlayerInfoManager002, and the plugin check's engine string is the one the
// ruling accepted dropping (docs/server_evictions.md, engine-boundary.md §6).

TEST( ServerFactory, TheEffectsInterfaceIsNotExposed )
{
	int rc = IFACE_OK;
	EXPECT_EQ( nullptr, CreateInterface( "IEffects001", &rc ) );
	EXPECT_EQ( IFACE_FAILED, rc );
	for ( int i = 0; i < SourceSharp_ExposedCount(); i++ )
		EXPECT_STRNE( "IEffects001", SourceSharp_ExposedAt( i ).m_pszVersion );
}

// GameMovement001 LEFT THE TABLE in plan_server_native_v2 T2 (ruling 4,
// 2026-10-11): the string is in no engine binary, and the server's callers
// use g_pGameMovement, never the factory.
TEST( ServerFactory, TheGameMovementInterfaceIsNotExposed )
{
	int rc = IFACE_OK;
	EXPECT_EQ( nullptr, CreateInterface( "GameMovement001", &rc ) );
	EXPECT_EQ( IFACE_FAILED, rc );
	for ( int i = 0; i < SourceSharp_ExposedCount(); i++ )
		EXPECT_STRNE( "GameMovement001", SourceSharp_ExposedAt( i ).m_pszVersion );
}

TEST( ServerFactory, TheTableHasSevenRowsAndNoNameTwice )
{
	ASSERT_EQ( 7, SourceSharp_ExposedCount() );
	std::set< std::string > names;
	for ( int i = 0; i < SourceSharp_ExposedCount(); i++ )
		EXPECT_TRUE( names.insert( SourceSharp_ExposedAt( i ).m_pszVersion ).second )
			<< SourceSharp_ExposedAt( i ).m_pszVersion << " is in the table twice";
	for ( const Expected &e : kExpected )
		EXPECT_EQ( 1u, names.count( e.pszVersion ) ) << e.pszVersion << " is not in the table";
}

TEST( ServerFactory, ThePluginInterfacesAreNotExposedEither )
{
	// The two the Decision 1 ruling dropped: answered by the same walk the
	// IEffects fact above uses, so a name coming back fails both.
	int rc = IFACE_OK;
	EXPECT_EQ( nullptr, CreateInterface( "PlayerInfoManager002", &rc ) );
	EXPECT_EQ( IFACE_FAILED, rc );
	EXPECT_EQ( nullptr, CreateInterface( "PluginHelpersCheck001", &rc ) );
	EXPECT_EQ( IFACE_FAILED, rc );
	for ( int i = 0; i < SourceSharp_ExposedCount(); i++ )
	{
		EXPECT_STRNE( "PlayerInfoManager002", SourceSharp_ExposedAt( i ).m_pszVersion );
		EXPECT_STRNE( "PluginHelpersCheck001", SourceSharp_ExposedAt( i ).m_pszVersion );
	}
}

//-----------------------------------------------------------------------------
// The real rows, in the files that own the objects.
//-----------------------------------------------------------------------------
namespace
{

std::string SrcRoot()
{
	for ( const char *p : { ".", "src", "../src" } )
	{
		struct stat st;
		if ( stat( ( std::string( p ) + "/sourcesharp/host/sourcesharp.h" ).c_str(), &st ) == 0 )
			return p;
	}
	return "";
}

std::string Read( const std::string &rel )
{
	std::ifstream f( SrcRoot() + "/" + rel );
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// Line comments out, so prose naming a macro cannot satisfy or fail a check.
std::string Code( const std::string &text )
{
	std::stringstream in( text ), out;
	std::string line;
	while ( std::getline( in, line ) )
	{
		const size_t at = line.find( "//" );
		out << ( at == std::string::npos ? line : line.substr( 0, at ) ) << "\n";
	}
	return out.str();
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos; at = hay.find( needle, at + needle.size() ) )
		n++;
	return n;
}

struct Site
{
	const char *pszFile;
	const char *pszRow;
};

const Site kSites[] =
{
	{ "sourcesharp/game/server/shell_gamedll.cpp", "kExposeServerGameDll" },
	{ "sourcesharp/game/server/shell_gameents.cpp", "kExposeServerGameEnts" },
	{ "sourcesharp/game/server/shell_gameclients.cpp", "kExposeServerGameClients" },
	{ "sourcesharp/game/server/shell_gameclients.cpp", "kExposeServerGameClients004" },
	{ "sourcesharp/game/server/shell_gameclients.cpp", "kExposeServerGameClients003" },
	{ "sourcesharp/game/server/shell_gametags.cpp", "kExposeServerGameTags" },
	// shell_playerinfo.cpp and plugin_check.cpp had rows here and went with
	// the plugin ABI (plan_server_native_min P1, Decision 1): no engine binary
	// asks for PlayerInfoManager002 or the plugin check (engine-boundary.md §6).
	{ "sourcesharp/game/server/shell_sharedappsystems.cpp", "kExposeServerDllSharedAppSystems" },
};

}

TEST( ServerFactorySources, EachObjectsFileDefinesItsRowOnce )
{
	ASSERT_FALSE( SrcRoot().empty() ) << "run from the farm's src directory";
	for ( const Site &s : kSites )
	{
		const std::string code = Code( Read( s.pszFile ) );
		ASSERT_FALSE( code.empty() ) << s.pszFile << " could not be read";
		EXPECT_EQ( 1u, CountOf( code, std::string( "const SourceSharpExposed " ) + s.pszRow + " = {" ) )
			<< s.pszFile << " does not define " << s.pszRow << " exactly once";
	}
}

TEST( ServerFactorySources, NoServerFileExposesThroughTheRegistry )
{
	for ( const Site &s : kSites )
	{
		std::string code = Code( Read( s.pszFile ) );
		// shell_movement.cpp is shared: the client keeps its EXPOSE_, behind CLIENT_DLL.
		const size_t clientArm = code.find( "#ifdef CLIENT_DLL\nEXPOSE_SINGLE_INTERFACE_GLOBALVAR" );
		if ( clientArm != std::string::npos )
			code.erase( clientArm, code.find( "#else", clientArm ) - clientArm );
		EXPECT_EQ( 0u, CountOf( code, "EXPOSE_SINGLE_INTERFACE" ) ) << s.pszFile;
		EXPECT_EQ( 0u, CountOf( code, "EXPOSE_INTERFACE" ) ) << s.pszFile;
	}
}

TEST( ServerFactorySources, TheTableListsEveryRowOnce )
{
	const std::string code = Code( Read( "sourcesharp/game/server/shell_factory.cpp" ) );
	ASSERT_FALSE( code.empty() );
	for ( const Site &s : kSites )
		EXPECT_EQ( 1u, CountOf( code, std::string( "&" ) + s.pszRow + "," ) ) << s.pszRow;
}
