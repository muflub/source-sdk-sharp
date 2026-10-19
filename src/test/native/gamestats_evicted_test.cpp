//====== SourceSharp — the telemetry uploader is out of both builds ==========//
//
// plan_shared.md Phase 4. game/shared/GameStats.cpp (1,626 lines) has left both
// `_csharp.vpc` file lists (now the server_sourcesharp and client_sourcesharp
// CMake source lists).
//
// WHAT IT WAS, because the name suggests something this mod might want and it is
// not that. It is Valve's TELEMETRY UPLOADER: a device and OS census, play-time
// buckets, an x360-era upload path, and a rolling frame-time window that only
// the upload ever read. Porting it would faithfully reproduce an uploader
// nothing receives.
//
// THE DECISION IS THE ONE plan_shared.md IS LEAST SURE OF, and it says so
// (Part 5, risk 1). What an eviction throws away is a working event fan-in --
// Event_PlayerKilled, Event_PlayerDisconnected, Event_AchievementProgress --
// that a port could have kept as managed hook points. THE CUT LIST IS IDENTICAL
// EITHER WAY: the same four call sites are rewired whether they end in a
// deletion or in a managed GameStatsEvents. That is what makes the phase safe
// to land first and argue later, and it is why the server rewire below is
// asserted by COUNT -- it is where a managed fan-in would be reattached, and a
// `Has` there passes with the wrong one of the file's two disconnect paths
// deleted (a mutation proved it).
//
// FOUR THINGS ARE ASSERTED HERE.
//
// 1. The $File lines are gone from both scripts.
// 2. The STOCK achievement reference left with its translation unit. (The
//    other stock reference, cdll_client_int.cpp's, is in a file neither DLL
//    compiles, and is no longer asserted.)
// 3. The two SHELL references are gone, because those are the port's own files
//    and a guard in one would be a guard around code this project wrote.
// 4. The client system row went with its accessor. It was the LAST row, and
//    that is asserted too: a row removed from the middle renumbers a walk order
//    that client_systems_table_test.cpp pins elsewhere.
//
// WHAT THIS EVICTION ALSO HAD TO MOVE, recorded here because the connection is
// not guessable: GameStats.cpp held CPointGamestatsCounter, which was
// AbiBothHierarchies' SDK-HIERARCHY FIXTURE -- the one spawnable stock
// CBaseEntity subclass the ABI gate compared its managed arm against. Losing it
// would have left that gate comparing an arm with itself, so the fixture was
// given a permanent home in sourcesharp/game/server/shell_abi_sdk_fixture.cpp.
//
// STEP 8a DELETED THE PROBE, so the fixture has no consumer and the "it must
// stay spawnable" requirement is gone with it. AND THE FILE IS NOW GONE:
// lane 5e-testonly deleted it after measuring that no .vpc named it and neither
// ninja graph carried an object for it (its vpc row went at fd5f3bc8d), and
// that its class and LINK_ENTITY_TO_CLASS had already been stripped by
// f288a5e11 -- so the sentence that used to stand here, "the file is still
// compiled; removing it is lane p4-exit's", was wrong on both halves.
// stock_serverclass_census_test.cpp now pins the OPPOSITE fact -- that nothing
// reaches for the classname any more.
//
// COMMENT-STRIPPED before scanning, the same rule as the sibling eviction
// tests: every one of these files now discusses GameStats.cpp by name in a
// comment, and a scan that keeps comments reads the explanation as the
// violation.
//
//=============================================================================//
#include <set>

#include <string>
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

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string ReadStripped( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + pszRel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return StripComments( ss.str() );
}

bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

// WHAT EACH SIDE COMPILES is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt). The old vpc check matched the path's
// TAIL (`shared\\GameStats.cpp`), so this does too: any compiled source whose
// farm-relative path ends in the needle counts, wherever it was moved to.
// The proof-of-read is the port's own shim, which both sides compile.
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

bool CompilesTail( const char *pszTarget, const std::string &strTail )
{
	const std::set< std::string > src = cmake_graph::CompiledSources( pszTarget );
	EXPECT_TRUE( src.count( kProofOfRead ) > 0 )
		<< pszTarget << " does not compile " << kProofOfRead
		<< " -- the reader is not reading its source list";
	for ( std::set< std::string >::const_iterator it = src.begin(); it != src.end(); ++it )
	{
		if ( it->size() >= strTail.size()
			&& it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
			return true;
	}
	return false;
}

} // namespace

TEST( GameStatsEvicted, NeitherProjectCompilesTheUploader )
{
	EXPECT_FALSE( CompilesTail( cmake_graph::kServer, "shared/GameStats.cpp" ) )
	    << "GameStats.cpp is back in the server project";
	EXPECT_FALSE( CompilesTail( cmake_graph::kClient, "shared/GameStats.cpp" ) )
	    << "GameStats.cpp is back in the client project";
}

TEST( GameStatsEvicted, TheAchievementStatHookLeftWithItsWholeTranslationUnit )
{
	// THIS USED TO BE HALF OF TheTwoStockReferencesAreGuardedAndNotDeleted, and
	// it asserted that achievementmgr.cpp's `Event_AchievementProgress` call
	// sat behind a `!defined( SOURCESHARP )` guard -- guarded rather than cut,
	// because it was somebody else's code and the argument for removing it was
	// this build's rather than upstream's.
	//
	// plan_entity.md step 7g makes an argument of its own and a stronger one:
	// this mod ships no achievements at all, so achievementmgr.cpp leaves the
	// build entirely. The reference cannot reach `gamestats` because the
	// translation unit holding it is not compiled, which is what the guard was
	// arranging for. The check moves to the build's source list, where that is
	// now the fact.
	//
	// It is asserted rather than dropped for the reason the guard was: a file
	// that comes back brings an undefined `gamestats` back with it, and this is
	// where somebody finds that out.
	EXPECT_FALSE( CompilesTail( cmake_graph::kClient, "shared/achievementmgr.cpp" ) )
	    << "achievementmgr.cpp is in the client build again. It calls "
	       "`gamestats->Event_AchievementProgress`, and `gamestats` has no "
	       "definition in this build -- step 7g removed the file, and putting "
	       "it back needs the guard this test used to police.";
	EXPECT_FALSE( CompilesTail( cmake_graph::kServer, "shared/achievementmgr.cpp" ) )
	    << "achievementmgr.cpp is in the server build";
}

TEST( GameStatsEvicted, TheTwoShellReferencesAreGoneRatherThanGuarded )
{
	// The port's OWN files, so there is nothing to preserve for upstream's
	// sake: a #ifndef SOURCESHARP in a sourcesharp/ file is a guard around code
	// this project wrote for a build it is the only build of.
	//
	// THE SERVER SITE IS WHERE A MANAGED FAN-IN WOULD REATTACH -- see this
	// file's header on why the eviction and the port share a cut list -- so it
	// is asserted by name rather than by counting.
	const std::string sv =
	    ReadStripped( "sourcesharp/game/server/shell_gameclients.cpp" );
	EXPECT_FALSE( Has( sv, "gamestats->" ) )
	    << "shell_gameclients.cpp still reaches the gamestats global";
	EXPECT_FALSE( Has( sv, "gamestats.h" ) )
	    << "shell_gameclients.cpp still includes gamestats.h";
	// IT WAS COUNTED, not merely found, and the count was ONE rather than two.
	//
	// It was two because there were TWO disconnect paths -- the standalone arm
	// and stock's SDK arm -- and only one of them had the stat event beside it.
	// `Has` passes with either deleted, which is how a mutation that removed
	// the REWIRED one survived this assertion until it was counted. That
	// reasoning is unchanged and is why this stays a count.
	//
	// plan_shim.md 5 3a (lane 3b-enginebind) deleted the SDK path. It was
	// `if ( player )` where player came from UTIL_SDKPlayerFromEdict, which has
	// one definition on the link line and whose every return statement is
	// `return NULL` -- so that copy of the call had never run, and the rules
	// have only ever been told about a disconnect by the arm that remains.
	//
	// THE CALL THIS FACT IS ABOUT IS THE SURVIVOR. The stat event
	// (`gamestats->Event_PlayerDisconnected( player );`) sat beside the SDK
	// copy, so what this fact guards -- that the eviction took the stat event
	// and nothing else -- is now asserted against the live path, which is a
	// stronger place for it to point than before.
	//
	// AND THE SURVIVOR WENT TOO, in plan_server_native_v2 T2. Its body was
	// empty (stock's is `if ( pClient ) { }` once SOURCESHARP's arm is taken),
	// so the rules were never told anything by it; managed hears of a
	// disconnect on its own path. What this fact still guards is that the
	// standalone arm, the path a departing client takes, survived the
	// eviction, and that the call cannot come back without its member.
	const size_t arm = sv.find(
	    "if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict ) )" );
	EXPECT_NE( arm, std::string::npos )
	    << "ClientDisconnect's standalone arm is gone";
	const std::string rules = ReadStripped( "sourcesharp/game/shared/igamerules.h" );
	ASSERT_NE( rules.find( "class IGameRules" ), std::string::npos )
	    << "igamerules.h was not read, so the absence below would prove nothing";
	EXPECT_FALSE( Has( rules, "ClientDisconnected" ) )
	    << "IGameRules declares ClientDisconnected again. Its only body was empty";

	const std::string cl =
	    ReadStripped( "sourcesharp/game/client/shell_clientsystems.cpp" );
	EXPECT_FALSE( Has( cl, "ClientSystem_GameStats" ) )
	    << "the client systems table still names an accessor whose definition "
	       "left with GameStats.cpp";
}

TEST( GameStatsEvicted, TheRowThatLeftWasTheLastOne )
{
	// A row removed from the MIDDLE renumbers the walk order that
	// client_systems_table_test.cpp pins -- init order forwards, shutdown
	// backwards. This one was last, so nothing above it moved, and that is a
	// fact about the edit rather than about the table.
	const std::string cl =
	    ReadStripped( "sourcesharp/game/client/shell_clientsystems.cpp" );

	const size_t table = cl.find( "kClientSystems[] =" );
	ASSERT_NE( table, std::string::npos );

	const size_t close = cl.find( "};", table );
	ASSERT_NE( close, std::string::npos );

	const std::string rows = cl.substr( table, close - table );
	const size_t last = rows.rfind( "ClientSystem_" );
	ASSERT_NE( last, std::string::npos );

	// CPhysicsSystem was the row above the one that left, so it is the last one
	// now. If a later phase appends below it this assertion moves -- which is
	// the point: appending to this table is a decision about walk order.
	EXPECT_TRUE( Has( rows.substr( last ), "ClientSystem_Physics" ) )
	    << "the last row of kClientSystems is no longer ClientSystem_Physics; "
	       "the GameStats row was removed from the end and something has been "
	       "added or removed since";
}
