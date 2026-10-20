//====== SourceSharp — save/restore is out of both builds ====================//
//
// plan_shared.md Phase 2. game/shared/saverestore.cpp (3,551 lines) and
// game/shared/physics_saverestore.cpp (876) have left both DLLs' source
// lists (the `_csharp.vpc` files then; server_sourcesharp and
// client_sourcesharp in src/{server,client}/native/CMakeLists.txt now).
//
// WHY THEY STAY OUT HAS CHANGED, AND THE NEW REASON IS THE STRONGER ONE.
// The original was that this build refuses to save: the shell answered
// ShellGameDll_Unreachable for the whole IServerGameDLL save cluster, and the
// one instrumented run that forwarded to stock's machinery recorded SaveInit
// NumberOfEntities=473 against a NextEnt walk of 0 -- a save capturing 0 of 473
// entities (docs/plan-audit.md C77).
//
// The server now SAVES, and those files are still out. Stage 1 of the save shim
// (docs/managed-saverestore.md) answers eleven of the fifteen through
// sourcesharp/game/shared/shell_savegame.h -- which was two headers, the second
// being save_field_codec.h, until plan_shim.md §5 3e merged them and moved
// their 620 lines of prose to docs/managed-saverestore.md -- and which is under 700
// lines and encode the three field types the engine's own header actually uses.
// So the eviction is no longer "the feature is refused"; it is "the feature does
// not need 4,400 lines of stock encoder", which is the claim this file now has
// to keep true.
//
// FOUR THINGS COULD UNDO THAT AND THIS FILE ASSERTS ALL FOUR.
//
// 1. THE $File LINES COULD COME BACK. Both scripts are flattened, so adding a
//    file is adding a line and nothing anywhere would object. Asserted absent
//    by name, in both scripts.
//
// 2. THE GUARDS COULD GO. The eviction is held up by `#ifndef SOURCESHARP`
//    blocks in two stock files -- cdll_client_int.cpp's eight IBaseClientDLL
//    save methods and its two block-handler groups, and globalstate.cpp's two
//    functions and ONE DATAMAP FIELD. Delete any of them and the link fails,
//    which is a loud failure and not this test's job; what this test catches is
//    the quieter direction, a guard REMOVED ALONG WITH the code it guards, so
//    the feature stops existing without anybody deciding that it should.
//    (NO LONGER ASSERTED: cdll_client_int.cpp is Valve stock that neither
//    client.so nor server.so compiles, and globalstate.cpp is gone -- see
//    TheGlobalStateTranslationUnitIsGoneEntirely.)
//
// 3. THE SHELL COULD REACH BACK FOR THE EVICTED ENCODER. This is the check
//    that changed shape when the cluster started answering. It used to be that
//    all eleven refused; what has to hold now is that the ones which ANSWER do
//    it without naming CSave, CRestore or g_pGameSaveRestoreBlockSet, and that
//    the four which do NOT answer -- the level-TRANSITION methods, which need
//    the entity table stage 1 does not build -- still refuse out loud. A body
//    that reached for stock's encoder would make this whole eviction wrong
//    silently, and would not link, which is the loud half; a body that quietly
//    stopped refusing a transition is the quiet half and is what is asserted.
//
// 4. AND THE ONE THAT ALMOST HAPPENED. plan_shared.md §3c's cut list names
//    `saverestore_gamedll.cpp` as a "server-only CSave user" to be evicted with
//    the rest. IT IS NOT ONE. Its 241 lines are ParseKeyvalue and
//    ExtractKeyvalue -- the DATAMAP KEYVALUE READER, which is how a map's
//    keyvalues reach an entity's fields, called from baseentity_shared.cpp:521,
//    548 and 675. It references nothing in saverestore.cpp (0 symbols in
//    common, measured) and evicting it breaks entity spawning outright. It is
//    asserted PRESENT here, which is the unusual direction for an eviction test
//    and is the point: the next lane to read that cut list must fail this test
//    rather than the game.
//
//    The client's achievement_saverestore.cpp was asserted present here for the
//    same reason and NO LONGER IS. The measurement has not changed -- it still
//    shares 0 symbols with the save cluster, so it was never an enabling edit
//    for this phase -- but plan_entity.md step 7g evicted it on a different
//    argument entirely: this mod ships no achievements, so there is nothing for
//    its save block to save. The assertion is flipped rather than deleted, and
//    carries that reasoning, so that restoring the file is still a decision
//    somebody makes in front of it.
//
// COMMENT-STRIPPED before scanning, the same rule as the sibling eviction
// tests: this file's own subjects discuss the evicted files by name in their
// headers, and a scan that keeps comments reads the explanation as the
// violation.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <set>
#include <sstream>
#include <sys/stat.h>
#include <stdlib.h>   // realpath, for the absence check below
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

// WHAT EACH DLL COMPILES is its CMake target's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against the target's
// CMakeLists). A needle here is a path TAIL ("shared/saverestore.cpp"): it
// matches the file under game/shared/ and under sourcesharp/game/shared/ alike,
// so a relocation cannot hide a row from the absence checks below.
bool CompilesAnyNamed( const char *pszTarget, const char *pszTail )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( pszTarget );
	const std::string tail = std::string( "/" ) + pszTail;
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
		if ( it->size() >= tail.size() && it->compare( it->size() - tail.size(), tail.size(), tail ) == 0 )
			return true;
	return false;
}

// Each target's proof-of-read: the no-op manager, which the next TEST also
// requires in both. An absence answered by a reader that saw nothing is free.
const char *kProofOfRead = "sourcesharp/game/shared/shell_physsaverestore_absent.cpp";

// On disk at all. Deliberately NOT ReadStripped: that one EXPECTs the open to
// succeed, so asking it about a file that is SUPPOSED to be absent would fail
// the test for the very reason the test exists to confirm.
//
// IT ANSWERS WITH THE PATH IT RESOLVED, and that is not decoration. An absence
// check is the one shape that can answer confidently about a DIFFERENT TREE: a
// helper that walks upward looking for a repo marker leaves a git worktree and
// lands in the main checkout, where every tracked file still exists -- so a
// file this branch deleted reads back as "still there", with no error anywhere.
// (A worktree's .git is a FILE and not a directory, so the usual markers do not
// identify the root either.) SrcRoot() above cannot do that -- it tries ".",
// "src" and "../src" and nothing further -- but the next person to touch it has
// to be able to SEE which tree answered, so the path travels with the verdict.
bool Exists( const char *pszRel, std::string *pOutResolved = NULL )
{
	const char *pszRoot = SrcRoot();
	if ( pszRoot == NULL )
	{
		if ( pOutResolved )
			*pOutResolved = "<no src/ found from the working directory>";
		return false;
	}

	const std::string path = std::string( pszRoot ) + "/" + pszRel;

	if ( pOutResolved )
	{
		// The DIRECTORY is resolved rather than the file: realpath on a path
		// that is absent answers NULL, which is exactly the case this has to
		// report on.
		char szAbs[ 4096 ];
		const std::string dir = std::string( pszRoot );
		*pOutResolved = ( realpath( dir.c_str(), szAbs ) != NULL )
			? ( std::string( szAbs ) + "/" + pszRel )
			: ( path + " (could not resolve " + dir + ")" );
	}

	std::ifstream f( path.c_str() );
	return f.good();
}


} // namespace

TEST( SaveRestoreEvicted, NeitherProjectCompilesTheSaveMachinery )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kProofOfRead ) )
	    << "the reader is not reading server_sourcesharp's source list";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kProofOfRead ) )
	    << "the reader is not reading client_sourcesharp's source list";

	EXPECT_FALSE( CompilesAnyNamed( cmake_graph::kServer, "shared/saverestore.cpp" ) )
	    << "saverestore.cpp is back in the server project";
	EXPECT_FALSE( CompilesAnyNamed( cmake_graph::kClient, "shared/saverestore.cpp" ) )
	    << "saverestore.cpp is back in the client project";
	EXPECT_FALSE( CompilesAnyNamed( cmake_graph::kServer, "shared/physics_saverestore.cpp" ) )
	    << "physics_saverestore.cpp is back in the server project";
	EXPECT_FALSE( CompilesAnyNamed( cmake_graph::kClient, "shared/physics_saverestore.cpp" ) )
	    << "physics_saverestore.cpp is back in the client project";
}

TEST( SaveRestoreEvicted, TheNoOpPhysicsManagerIsInBothProjects )
{
	// The one thing that DID have to be replaced rather than deleted:
	// g_pPhysSaveRestoreManager's Associate/Forget paths are called by seven
	// surviving translation units every time a physics object is built or
	// destroyed. Without this file the link fails, so a missing line is loud --
	// but it must be in BOTH, and a line added to one script only would leave
	// the other side linking against nothing on its next full build.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kProofOfRead ) )
	    << "server_sourcesharp (src/server/native/CMakeLists.txt) does not compile " << kProofOfRead;
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kProofOfRead ) )
	    << "client_sourcesharp (src/client/native/CMakeLists.txt) does not compile " << kProofOfRead;
}

// RETIRED BY plan_shim.md PHASE 1d (lane 1d).
//
// The test here was `TheDatamapKeyvalueReaderIsStillBuilt`, and it asserted
// that server_csharp.vpc still names saverestore_gamedll.cpp. Its reason was
// that the file's ParseKeyvalue/ExtractKeyvalue are "called from
// baseentity_shared.cpp" -- which was true when it was written and is not true
// now. baseentity_shared.cpp is named 0 times in server_csharp.vpc and
// produces no object on either link line, so its calls are not calls in this
// build; tools/shim-census reports 0 objects importing saverestore_gamedll.cpp
// on both sides. The keyvalue parse that runs is managed:
// SourceSharp.Abi/ScriptedVars.cs and ScriptedEntity.cs ParseKeyvalue, reached
// through StandaloneEntity_ApplyMapKey.
//
// The assertion is gone rather than inverted because "the vpc does not name
// this file" is a claim every deleted row could make and none of them is worth
// a test. What the correction was really about -- that a row must not be
// evicted on the strength of its NAME -- is now recorded in the row's own
// deletion commit, with the census output that answered it.

TEST( SaveRestoreEvicted, TheClientAchievementBlockIsEvictedOnItsOwnArgument )
{
	// THIS ASSERTION WAS THE OTHER WAY ROUND, and the flip is the point rather
	// than an edit to get a suite green. What it used to say was that
	// achievement_saverestore.cpp shares 0 symbols with either file the save
	// phase evicted, so evicting it would not release anything -- and it ended
	// with the sentence this test is now the other side of:
	//
	//     It may be evictable on its own merits -- it registers a save block
	//     for a feature that cannot save -- and that is a decision with its own
	//     argument, not a side effect of this one.
	//
	// plan_entity.md step 7g IS that argument, made on its own grounds: this
	// mod ships no achievements, so achievementmgr.cpp and its siblings leave
	// the build, and a save block registered for them has nothing to save. The
	// measurement the old test rested on is untouched -- it still shares no
	// symbol with the save cluster, and 7g is not claiming otherwise.
	//
	// The assertion is KEPT rather than deleted so that putting the file back
	// is a decision somebody has to make here, in front of the reasoning, the
	// way `gib.cpp`'s eviction is kept in sdk_player_cast_test.cpp.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kProofOfRead ) )
	    << "the reader is not reading client_sourcesharp's source list";
	EXPECT_FALSE( CompilesAnyNamed( cmake_graph::kClient, "shared/achievement_saverestore.cpp" ) )
	    << "achievement_saverestore.cpp is in the client build again. Step 7g "
	       "removed it with the achievement manager it saves for; if the mod "
	       "now ships achievements this is right, and the sentence above is "
	       "what has to be rewritten -- do not just flip the assertion back.";
}

TEST( SaveRestoreEvicted, TheGlobalStateTranslationUnitIsGoneEntirely )
{
	// THIS TEST CHANGED SHAPE WITH ITS SUBJECT, and the earlier shapes are kept
	// here because each was right when it was written.
	//
	// FIRST it said `DEFINE_UTLVECTOR( m_list, FIELD_EMBEDDED )` in
	// game/server/globalstate.cpp had to be behind a SOURCESHARP guard, for a
	// reason that guarding the two obvious functions did not fix:
	//
	//     DEFINE_UTLVECTOR instantiates CUtlVectorDataOps<>, whose Save and
	//     Restore are VIRTUAL and are therefore kept alive by their own vtable
	//     through --gc-sections; their bodies reach CSave::DoWriteAll, which
	//     lives in game/shared/saverestore.cpp and is out of this build. A
	//     datamap field can drag a link dependency in through a template's
	//     vtable with no reachable code naming it.
	//
	// THEN, at plan_entity.md's Phase 4 exit, it said the shape was gone rather
	// than guarded: CGlobalState, its CUtlVector, its CUtlSymbolTable and both
	// datadescs had moved to SourceSharp.Server/GlobalStateTable.cs, and it read
	// the residue -- eleven forwarders and two ConCommands -- for the absence.
	//
	// NOW THERE IS NO RESIDUE AND NO FILE. plan_shim.md Phase 2c: the two
	// platform globals CWorld::Spawn declared are seeded by
	// GlobalStateTable.OnLevelInitPreEntity, `dump_globals` and
	// `server_game_time` are [ConCommand] classes in
	// SourceSharp.Server/ServerAdminCommands.cs, and the translation unit left
	// server_csharp.vpc with its last caller.
	//
	// WHICH ASSERTIONS WENT, AND WHY THEY ARE NOT CLAIMS ABOUT THIS BUILD ANY
	// MORE. Six read the file's text for the save cluster's spellings
	// (DEFINE_UTLVECTOR, BEGIN_SIMPLE_DATADESC, WriteFields, ReadFields,
	// saverestore.h) and two asserted that SaveGlobalState and
	// RestoreGlobalState were still DECLARED and still PRINTED a refusal. A
	// file that is not on disk cannot contain a datadesc, so the first six are
	// vacuously true of nothing. The last two mattered while the symbols
	// existed and something could call them; nothing imports either
	// (`tools/shim-census who-imports` over globalstate.cpp named exactly one
	// importer, world_adapter.o, and one symbol, GlobalEntity_Add), and the one
	// entry point that could reach them -- IServerGameDLL::SaveGlobalState --
	// is answered by ShellGameDll_Unreachable, which is where the printed
	// refusal now is and is asserted below rather than dropped.
	std::string strResolved;
	EXPECT_FALSE( Exists( "game/server/globalstate.cpp", &strResolved ) )
	    << "game/server/globalstate.cpp is back on disk. If that is deliberate, "
	       "the managed table (SourceSharp.Server/GlobalStateTable.cs) and this "
	       "file are now two tables, and the one the game reads is whichever "
	       "world_adapter.cpp happens to call.\nRESOLVED TO: " << strResolved
	    << "\nCheck that path names the tree you think it does: an absence "
	       "check that escaped a worktree reads the MAIN checkout, where the "
	       "file is still tracked, and answers \"still there\" without erroring.";

	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kProofOfRead ) )
	    << "the reader is not reading server_sourcesharp's source list";
	EXPECT_FALSE( CompilesAnyNamed( cmake_graph::kServer, "globalstate.cpp" ) )
	    << "server_sourcesharp (src/server/native/CMakeLists.txt) compiles globalstate.cpp again";

	// THE REFUSAL DID NOT VANISH WITH THE FILE, it moved to the only thing that
	// can still be asked for a save: the shell's IServerGameDLL.
	const std::string shell = ReadStripped( "sourcesharp/game/server/shell_gamedll.cpp" );
	ASSERT_FALSE( shell.empty() ) << "could not read shell_gamedll.cpp";
	EXPECT_TRUE( Has( shell, "SaveGlobalState" ) )
	    << "the shell no longer names SaveGlobalState at all, so a save request "
	       "reaches nothing and says nothing -- the silent no-op plan_entity.md "
	       "7 forbids";
	EXPECT_TRUE( Has( shell, "RestoreGlobalState" ) )
	    << "the shell no longer names RestoreGlobalState at all";
}

TEST( SaveRestoreEvicted, TheServersSaveClusterAnswersWithoutTheEvictedEncoder )
{
	const std::string shell =
	    ReadStripped( "sourcesharp/game/server/shell_gamedll.cpp" );

	// It answers -- through the managed SaveGameSession (plan_server_native_v2
	// T4c), not through stock.
	EXPECT_TRUE( Has( shell, "pArea->SaveWriteFields(" ) )
	    << "SaveWriteFields has to encode the engine's own header somehow, and "
	       "the managed codec is the somehow this eviction is predicated on";
	EXPECT_TRUE( Has( shell, "pArea->SaveReadFields(" ) );
	EXPECT_TRUE( Has( shell, "pArea->SaveInit(" ) );

	// And these are the three names from the evicted files. Any one of them
	// appearing here means the shell went back for game/shared/saverestore.cpp,
	// which would not link -- but a reader of this test should not have to
	// discover that from a linker error.
	EXPECT_FALSE( Has( shell, "CSave " ) );
	EXPECT_FALSE( Has( shell, "CRestore " ) );
	EXPECT_FALSE( Has( shell, "g_pGameSaveRestoreBlockSet" ) );
}

TEST( SaveRestoreEvicted, TheClientsSaveClusterForwardsToTheManagedSession )
{
	const std::string shell =
	    ReadStripped( "sourcesharp/game/client/shell_clientdll.cpp" );
	ASSERT_FALSE( shell.empty() ) << "could not read shell_clientdll.cpp";

	// The client state file's three byte-moving methods are one forward each
	// into IManagedSaveRestoreClient (the client save codec): the shared
	// managed SaveBlockSession decides the buffer and encodes the blocks.
	EXPECT_TRUE( Has( shell, "pArea->SaveInit( size, gpGlobals->curtime )" ) );
	EXPECT_TRUE( Has( shell, "pArea->SaveWriteFields( pSaveData, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount )" ) );
	EXPECT_TRUE( Has( shell, "pArea->SaveReadFields( pSaveData, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount )" ) );

	// And not through the native codec it replaced.
	EXPECT_FALSE( Has( shell, "ShellSave_" ) );
	EXPECT_FALSE( Has( shell, "CSave " ) );
	EXPECT_FALSE( Has( shell, "CRestore " ) );
}

TEST( SaveRestoreEvicted, TheFourTransitionRefusalsStillStand )
{
	const std::string shell =
	    ReadStripped( "sourcesharp/game/server/shell_gamedll.cpp" );

	// WHAT STAGE 1 DELIBERATELY DID NOT IMPLEMENT, asserted by name so that
	// "somebody made the save cluster consistent" cannot quietly take a
	// transition path from refusing to no-opping. Each of these needs the entity
	// table (docs/managed-saverestore.md 2d), which stage 1 leaves empty on
	// purpose, so an answer here would be a wrong answer rather than a partial
	// one -- and a wrong answer on a transition is a world that half-loads.
	const char *kStillRefused[] = {
		"IServerGameDLL::GetSaveComment",
		"IServerGameDLL::CreateEntityTransitionList",
		"IServerGameDLL::BuildAdjacentMapList",
	};

	for ( size_t i = 0; i < sizeof( kStillRefused ) / sizeof( kStillRefused[ 0 ] ); i++ )
	{
		const std::string strNeedle =
		    std::string( "ShellGameDll_Unreachable( \"" ) + kStillRefused[ i ] + "\" )";

		EXPECT_TRUE( Has( shell, strNeedle.c_str() ) )
		    << kStillRefused[ i ] << " no longer names itself in a refusal";
	}

	// The fourth, LevelInit's transition arm, is managed's since
	// plan_server_native_v2 T4e (MapLoadArms picks the arm), and refuses there.
	const std::string managed =
	    ReadStripped( "sourcesharp/managed/SourceSharp.Server/ServerGameDll.cs" );
	EXPECT_TRUE( Has( managed, "REFUSED: IServerGameDLL::LevelInit (transition) was called." ) )
	    << "IServerGameDLL::LevelInit (transition) no longer names itself in a refusal";
}

TEST( SaveRestoreEvicted, TheNoOpManagerKeepsNoTable )
{
	const std::string shim =
	    ReadStripped( "sourcesharp/game/shared/shell_physsaverestore_absent.cpp" );

	// A table nothing reads is a table that can disagree with reality without
	// anybody finding out, and this one would grow an entry per physics object
	// per level. The decision is that it stores nothing; this is what says so
	// if somebody "fixes" it by adding storage back.
	EXPECT_FALSE( Has( shim, "CUtlDict" ) );
	EXPECT_FALSE( Has( shim, "CUtlVector" ) );
	EXPECT_FALSE( Has( shim, "CUtlMap" ) );
}

TEST( SaveRestoreEvicted, TheFieldOpsAreAnObjectAndNotNull )
{
	const std::string shim =
	    ReadStripped( "sourcesharp/game/shared/shell_physsaverestore_absent.cpp" );

	// DEFINE_PHYSPTR bakes GetPhysObjSaveRestoreOps' return into a
	// typedescription_t at static-init (physics_saverestore.h:29), in whatever
	// TU carries the datamap. A NULL there is a null vtable pointer sitting in
	// a table that outlives every decision about whether saving happens.
	const size_t at = shim.find( "GetPhysObjSaveRestoreOps" );
	ASSERT_NE( at, std::string::npos );

	const std::string body = shim.substr( at );
	EXPECT_TRUE( Has( body, "return &s_Absent;" ) );
	EXPECT_FALSE( Has( body, "return NULL;" ) );
}
