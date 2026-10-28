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

template <int N>
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

const Expected kExpected[] = {
	{ "ServerGameDLL012", 0 },
	{ "ServerGameEnts001", 1 },
	{ "ServerGameClients005", 2 },
	{ "ServerGameClients004", 3 },
	{ "ServerGameClients003", 4 },
	{ "ServerGameTags001", 5 },
	{ "VServerDllSharedAppSystems001", 8 },
};

}

const SourceSharpExposed kExposeServerGameDll = { "ServerGameDLL012", StandIn<0> };
const SourceSharpExposed kExposeServerGameEnts = { "ServerGameEnts001", StandIn<1> };
const SourceSharpExposed kExposeServerGameClients = { "ServerGameClients005", StandIn<2> };
const SourceSharpExposed kExposeServerGameClients004 = { "ServerGameClients004", StandIn<3> };
const SourceSharpExposed kExposeServerGameClients003 = { "ServerGameClients003", StandIn<4> };
const SourceSharpExposed kExposeServerGameTags = { "ServerGameTags001", StandIn<5> };
const SourceSharpExposed kExposeServerDllSharedAppSystems = { "VServerDllSharedAppSystems001", StandIn<8> };

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
	std::set<std::string> names;
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

struct Site
{
	const char *pszFile;
	const char *pszRow;
};

}
