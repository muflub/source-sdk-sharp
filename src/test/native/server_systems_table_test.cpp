//====== SourceSharp — the named list's native half, after it went managed =====//
//
// plan_gamedll.md Part 3a and Part 9a, Phase 7; plan_server_native_v2 T4e.
//
// kServerSystems[] -- the server's game systems in the order they run, walked
// forwards for init and backwards for shutdown -- was a C++ table in
// shell_serversystems.cpp. Since T4e the ORDER is managed's (ServerSystems.cs)
// and its facts are ServerSystemsTests: physics before the managed row on the
// way in and after it on the way out, the string pool last, the managed row's
// stages inside the model-cache lock. What is left native is one step per
// stage of each of the two native systems, and the facts here are about that
// half:
//
//   1. There is no native table and no native walk left to drive a system a
//      second time, or to stop driving one silently.
//   2. Each IShimLevel system step is ONE stage of ONE system, under the
//      model-cache lock its walk took.
//   3. The world's own precache hands managed the pre-entity edge -- stock's
//      placement for the pass, and the landmine Phase 7 found.
//   4. The managed side's server edge bodies: the pre-entity one is left, the
//      four the table's row called went with it.
//
// SOURCE-READ RATHER THAN LINKED, as before: shell_level.cpp includes the
// engine's world, and the behavioural half of this gate is a running server.
//
//=============================================================================//

#include <string>
#include <cstring>
#include <fstream>
#include <sys/stat.h>

#include "gtest/gtest.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

// The file, or empty. bExpect: a file that must be there; a missing one is a
// failure, never a vacuous pass.
std::string Read( const char *pszRel, bool bExpect = true )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	if ( bExpect )
		EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
			<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

// Comments are stripped FIRST and always. Every needle below would otherwise
// match the paragraph explaining what it is for.
std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

const char *kTableFile    = "sourcesharp/game/server/shell_serversystems.cpp";
const char *kLevel        = "sourcesharp/game/server/shell_level.cpp";
const char *kFrame        = "sourcesharp/game/server/shell_frame.cpp";
const char *kGameSystem   = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
const char *kWorldAdapter = "sourcesharp/game/server/world_adapter.cpp";
const char *kShellGameDll = "sourcesharp/game/server/shell_gamedll.cpp";

// The body of one function in a file, brace-matched from its signature.
std::string BodyOf( const std::string &file, const char *pszSignature )
{
	const size_t at = file.find( pszSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = file.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	size_t depth = 0;
	for ( size_t i = open; i < file.size(); i++ )
	{
		if ( file[ i ] == '{' ) depth++;
		else if ( file[ i ] == '}' ) { depth--; if ( depth == 0 ) return file.substr( open, i - open + 1 ); }
	}
	return std::string();
}

int CountOf( const std::string &haystack, const std::string &needle )
{
	int n = 0;
	for ( size_t at = haystack.find( needle ); at != std::string::npos;
	      at = haystack.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

} // namespace

//=============================================================================
// 1. NO NATIVE TABLE, NO NATIVE WALK
//=============================================================================

TEST( ServerSystemsTable, TheNativeTableAndItsWalksAreGone )
{
	// The control: the files the walks would be in are read.
	const std::string level = StripComments( Read( kLevel ) );
	const std::string frame = StripComments( Read( kFrame ) );
	const std::string shell = StripComments( Read( kShellGameDll ) );
	ASSERT_GT( level.size(), 1000u );
	ASSERT_GT( frame.size(), 1000u );
	ASSERT_GT( shell.size(), 1000u );

	EXPECT_TRUE( Read( kTableFile, false ).empty() )
		<< kTableFile << " is back: the order is managed's (ServerSystems.cs)";

	const char *kWalks[] = {
		"SourceSharp_ServerSystemsInit(", "SourceSharp_ServerSystemsPostInit(",
		"SourceSharp_ServerSystemsShutdown(", "SourceSharp_LevelInitPreEntityAllSystems(",
		"SourceSharp_ServerSystemsLevelInitPostEntity(", "SourceSharp_ServerSystemsLevelShutdownPreEntity(",
		"SourceSharp_ServerSystemsLevelShutdownPostEntity(", "SourceSharp_ServerSystemsFramePreThink(",
		"SourceSharp_ServerSystemsFramePostThink(", "SourceSharp_ServerSystemsPreClientUpdate(",
	};
	for ( const char *pszWalk : kWalks )
	{
		EXPECT_EQ( 0, CountOf( level, pszWalk ) + CountOf( frame, pszWalk ) + CountOf( shell, pszWalk ) )
			<< pszWalk << " is called again: a native walk beside managed's order "
			   "drives a system twice";
	}
}

TEST( ServerSystemsTable, TheShellNeverWalksStocksAnonymousList )
{
	// igamesystem.cpp is off the server's link line; nothing that drives a
	// stage may name stock's list.
	const char *kFiles[] = { kLevel, kFrame, kShellGameDll, kWorldAdapter };
	for ( const char *pszFile : kFiles )
	{
		const std::string file = StripComments( Read( pszFile ) );
		ASSERT_GT( file.size(), 1000u ) << pszFile;
		EXPECT_EQ( CountOf( file, "IGameSystem::" ), 0 ) << pszFile << " walks stock's IGameSystem list";
	}
}

//=============================================================================
// 2. ONE STAGE OF ONE SYSTEM PER STEP
//=============================================================================

TEST( ServerSystemsTable, EachSystemStepIsOneStageOfOneSystemUnderTheLock )
{
	const std::string level = StripComments( Read( kLevel ) );

	struct Step_t { const char *m_pszSlot; const char *m_pszCall; };
	const Step_t kSteps[] = {
		{ "virtual int32_t PhysicsInit()",                     "PhysicsGameSystem()->Init()" },
		{ "virtual void PhysicsLevelInitPreEntity()",          "PhysicsGameSystem()->LevelInitPreEntity()" },
		{ "virtual void PhysicsLevelInitPostEntity()",         "PhysicsGameSystem()->LevelInitPostEntity()" },
		{ "virtual void PhysicsLevelShutdownPreEntity()",      "PhysicsGameSystem()->LevelShutdownPreEntity()" },
		{ "virtual void PhysicsLevelShutdownPostEntity()",     "PhysicsGameSystem()->LevelShutdownPostEntity()" },
		{ "virtual void StringPoolLevelShutdownPostEntity()",  "GameStringSystem()->LevelShutdownPostEntity()" },
	};

	for ( const Step_t &step : kSteps )
	{
		const std::string body = BodyOf( level, step.m_pszSlot );
		ASSERT_FALSE( body.empty() ) << step.m_pszSlot << " has no body in shell_level.cpp";
		EXPECT_EQ( 1, CountOf( body, step.m_pszCall ) ) << step.m_pszSlot << " does not run " << step.m_pszCall;
		EXPECT_EQ( 1, CountOf( body, "->" ) ) << step.m_pszSlot << " runs more than one stage";
		EXPECT_EQ( 1, CountOf( body, "MDLCACHE_CRITICAL_SECTION();" ) )
			<< step.m_pszSlot << " runs outside the model-cache lock its walk took";
	}
}

TEST( ServerSystemsTable, TheManagedRowsLockIsTheModelCachesOwn )
{
	const std::string frame = StripComments( Read( kFrame ) );

	const std::string begin = BodyOf( frame, "virtual void BeginModelCacheLock()" );
	const std::string end = BodyOf( frame, "virtual void EndModelCacheLock()" );
	ASSERT_FALSE( begin.empty() );
	ASSERT_FALSE( end.empty() );
	EXPECT_EQ( 1, CountOf( begin, "mdlcache->BeginLock();" ) );
	EXPECT_EQ( 1, CountOf( end, "mdlcache->EndLock();" ) );
}

//=============================================================================
// 3. THE WORLD'S PRE-ENTITY PASS
//=============================================================================

TEST( ServerSystemsTable, TheWorldsPreEntityPassGoesToTheManagedEdgeAndNotToStock )
{
	const std::string world = StripComments( Read( kWorldAdapter ) );

	// THE LANDMINE. Stock calls LevelInitPreEntityAllSystems from inside the
	// WORLD ENTITY's precache, not from LevelInit. The pass is managed's now
	// (ServerSystems.LevelInitPreEntity: CPhysicsHook's stage, which creates
	// physenv, then the managed participants), so the world hands managed the
	// edge, once.
	EXPECT_EQ( world.find( "IGameSystem::LevelInitPreEntityAllSystems" ), std::string::npos )
		<< "world_adapter must not call stock's pass directly";
	EXPECT_EQ( CountOf( world, "SourceSharp_ManagedLevelInitPreEntity(" ), 1 )
		<< "exactly one pre-entity pass, through the managed edge";
}

TEST( ServerSystemsTable, TheWorldsPrecacheKeepsOnlyItsNativeHalfInOrder )
{
	// plan_server_native_v2 T4e: the rest of stock's CWorld::Precache is
	// ServerGameDll.MapLoadPreEntity's (the convars, the window, the pass, the
	// world's precache list). What native keeps is what native state holds,
	// in stock's order around the managed edge.
	const std::string world = StripComments( Read( kWorldAdapter ) );
	const std::string body = BodyOf( world, "void CWorld::Precache( void )" );
	ASSERT_FALSE( body.empty() ) << "CWorld::Precache has no body";

	const char *kOrder[] = {
		"InstallGameRules();", "ss::SoundEnt_Init();",
		"SourceSharp_ManagedLevelInitPreEntity(", "SENTENCEG_Init();", "W_Precache();",
		// AllocPooledString("func_brush") stood last; its global had no reader
		// and went in plan_server_native_v2 T6.
	};
	size_t at = 0;
	for ( const char *pszStep : kOrder )
	{
		const size_t next = body.find( pszStep, at );
		EXPECT_NE( next, std::string::npos ) << pszStep << " is missing or out of order";
		if ( next != std::string::npos )
			at = next;
	}

	// And what moved stays moved: the convar writes, the window's opening,
	// the gibs, the player model, the precache register and the no-op rules
	// calls.
	const char *kGone[] = {
		"ConVarRef", "EntityPrecache_SetAllowed(", "Precache_Model(", "ClientGamePrecache(",
		"CPrecacheRegister", "g_pGameRules->", "ServerPrecacheScriptSound(",
	};
	for ( const char *pszGone : kGone )
		EXPECT_EQ( std::string::npos, body.find( pszGone ) ) << pszGone << " is back in CWorld::Precache";
}

TEST( ServerSystemsTable, TheReloadIsServicedFromThinkAlone )
{
	// docs/gamedll-ledger.md owner rule 2: the ONE service point on the shell
	// path is IServerGameDLL::Think, because that is the only per-frame entry
	// that runs with no level loaded.
	const std::string shell = StripComments( Read( kShellGameDll ) );
	EXPECT_EQ( CountOf( shell, "SourceSharp_ServiceReloadRequest()" ), 1 )
		<< "exactly one service call on the shell";
}

//=============================================================================
// 4. THE MANAGED EDGE BODIES
//=============================================================================

TEST( ServerSystemsTable, ThePreEntityEdgeIsTheServersOneManagedEdgeBody )
{
	const std::string file = StripComments( Read( kGameSystem ) );
	ASSERT_GT( file.size(), 1000u );

	EXPECT_EQ( 1, CountOf( file, "void SourceSharp_ManagedLevelInitPreEntity( const char *pszMapName )" ) )
		<< "the pre-entity edge has one body";

	// The four the table's row called. ServerSystems calls those edges itself
	// now; a body back here is a second driver.
	const char *kGone[] = {
		"void SourceSharp_ManagedLevelInit(", "void SourceSharp_ManagedLevelShutdownPreEntity(",
		"void SourceSharp_ManagedLevelShutdown(", "void SourceSharp_ManagedFramePreThink(",
		"void SourceSharp_ManagedFrameThink(",
	};
	for ( const char *pszBody : kGone )
		EXPECT_EQ( 0, CountOf( file, pszBody ) ) << pszBody << " is back";
}
