//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE VIEWPORT SYSTEM ORACLE (C7, vgui_game_viewport.cpp): stock's
// CViewportClientSystem, the game system whose Init lays the client mode out
// (g_pClientMode->Layout()) and whose every other stage is empty, run offline
// from the PRISTINE game/client/vgui_game_viewport.cpp over a recording client
// mode, and written down as the golden
// src/test/client/Fixtures/viewport-stock.tsv.
//
// THE SCRIPT: the system's name and per-frame answer, then each IGameSystem
// stage in the order the client walks them (Init, PostInit, the four level
// stages with the Steam context one between them, OnSave, OnRestore,
// SafeRemoveIfDesired, Shutdown), then Init once more. Each stage is an `S`
// row, followed by what stock did: `L` for a Layout call, `R` for a stage's
// answer.
//
// THE GOLDEN CARRIES ITS STIMULUS, so ViewportClientSystemOracleTests replays
// the same stages on the managed system and holds every row after each.
// Regenerating the golden to make this test green deletes the check (touch
// viewport_golden.regen beside this file, run, delete the marker, and say why
// in the commit).
//
//=============================================================================

#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>

#include "gtest/gtest.h"

#include "viewport_harness.h"

namespace
{
std::string g_Log;

void Log( const std::string &s )
{
	g_Log += s;
	g_Log += '\n';
}
} // namespace

void IClientMode::Layout()
{
	Log( "L\tLayout" );
}

static IClientMode s_ClientMode;
IClientMode *g_pClientMode = &s_ClientMode;

// igamesystem.cpp's, which the harness does not compile: the destructor only.
IGameSystem::~IGameSystem() {}

// The subject, included so its file-static system is this translation unit's.
#include "vgui_game_viewport.cpp"

namespace
{

std::string RunScript()
{
	g_Log.clear();
	IGameSystem *p = ViewportClientSystem();
	Log( std::string( "S\tName\nR\t" ) + p->Name() );
	Log( std::string( "S\tIsPerFrame\nR\t" ) + ( p->IsPerFrame() ? "1" : "0" ) );
	Log( "S\tInit" );
	Log( std::string( "R\t" ) + ( p->Init() ? "1" : "0" ) );
	Log( "S\tPostInit" );
	p->PostInit();
	Log( "S\tLevelInitPreEntity" );
	p->LevelInitPreEntity();
	Log( "S\tLevelInitPostEntity" );
	p->LevelInitPostEntity();
	Log( "S\tLevelShutdownPreClearSteamAPIContext" );
	p->LevelShutdownPreClearSteamAPIContext();
	Log( "S\tLevelShutdownPreEntity" );
	p->LevelShutdownPreEntity();
	Log( "S\tLevelShutdownPostEntity" );
	p->LevelShutdownPostEntity();
	Log( "S\tOnSave" );
	p->OnSave();
	Log( "S\tOnRestore" );
	p->OnRestore();
	Log( "S\tSafeRemoveIfDesired" );
	p->SafeRemoveIfDesired();
	Log( "S\tShutdown" );
	p->Shutdown();
	Log( "S\tInit" );
	Log( std::string( "R\t" ) + ( p->Init() ? "1" : "0" ) );
	return g_Log;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath() { return RepoFile( "src/test/client/Fixtures/viewport-stock.tsv" ); }
std::string MarkerPath() { return RepoFile( "src/test/oracle/native/viewport_golden.regen" ); }

} // namespace

TEST( ViewportOracle, StockProducesTheGolden )
{
	const std::string run = RunScript();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	std::ifstream in( GoldenPath() );
	ASSERT_TRUE( in.good() ) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();
	ASSERT_GT( golden.size(), 200u ) << "the golden is empty or truncated";
	EXPECT_EQ( run, golden ) << "Do NOT regenerate the golden to make this pass; see the file header.";
}

// Each arm the script exists to reach, found in the run.
TEST( ViewportOracle, TheScriptReachesEveryArm )
{
	const std::string run = RunScript();
	EXPECT_NE( run.find( "S\tName\nR\tCViewportClientSystem\n" ), std::string::npos ) << "the system names itself";
	EXPECT_NE( run.find( "S\tIsPerFrame\nR\t0\n" ), std::string::npos ) << "it has no per-frame half";
	EXPECT_NE( run.find( "S\tInit\nL\tLayout\nR\t1\n" ), std::string::npos ) << "Init lays the client mode out and succeeds";
	EXPECT_NE( run.find( "S\tLevelInitPreEntity\nS\tLevelInitPostEntity\n" ), std::string::npos ) << "a level stage does nothing";

	// Layout is asked only by Init: twice in the script, once per Init.
	size_t n = 0;
	for ( size_t at = run.find( "L\tLayout" ); at != std::string::npos; at = run.find( "L\tLayout", at + 1 ) )
		++n;
	EXPECT_EQ( n, 2u );
}
