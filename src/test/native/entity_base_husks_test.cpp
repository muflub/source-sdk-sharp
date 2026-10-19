//====== SourceSharp — the husks around the base entity, and the link glue =====//
//
// plan_entity.md step 4f. Two different deletions are pinned here, because they
// are the same mistake waiting in two places.
//
// (1) THREE SDK ENTITY CLASSES WHOSE REGISTRATION WAS ALREADY GUARDED AWAY.
//
//   game/server/cbase.cpp    CEventQueueSaveLoadProxy : CLogicalEntity
//   game/server/client.cpp   CPointClientCommand      : CPointEntity
//   game/server/client.cpp   CPointServerCommand      : CPointEntity
//
// Each carried `LINK_ENTITY_TO_CLASS` inside `#ifndef SOURCESHARP`, so no map
// and no console could create one in this build. They are the source row of
// entity_gates gate 1 -- `": public <entity>" decls` -- which read 12 on the
// server before this and reads 9 after.
//
// A DELETION IS NOT ALLOWED TO BE A CAPABILITY LOSS IN DISGUISE, which is the
// standing rule for Phase 4 ("removing a type must mean the behaviour is
// managed or genuinely gone, never invisible"). So each deletion is checked
// TOGETHER WITH what now carries it:
//
//   * point_clientcommand and point_servercommand are managed
//     ([ScriptedEntity] in SourceSharp.Server/Entities/Points), and
//     PointServerCommand.cs reads `sv_allow_point_servercommand` BY NAME -- so
//     the native ConVar registration in client.cpp is load-bearing and must
//     survive the class that used to be its only reader. That is the trap this
//     file exists for: deleting the class and its convar together would open
//     the most dangerous entity in the SDK with nothing failing.
//   * CEventQueueSaveLoadProxy handed over to
//     CEventQueue_SaveRestoreBlockHandler, which is still in cbase.cpp.
//
// (2) THE LINK-GLUE FILES, AND THE RULE THAT THEY DECAY.
//
//   sourcesharp/game/server/shell_ai_absent.cpp
//   sourcesharp/game/server/shell_ai_orphans.cpp
//   sourcesharp/game/server/shell_entity_orphans.cpp
//
// Every definition in those three exists only because something else in the
// build names it. That is a claim with an expiry date: when a stock TU leaves
// the vpc, its stubs become dead code that still looks purposeful. It had
// already expired badly -- shell_ai_absent.cpp defined 155 symbols and 15 were
// still referenced.
//
// The maintaining instrument is `tools/4a-owes`, which cannot run in a gtest
// (it needs the built objects). What CAN be checked offline, and is what this
// file checks, is the SHAPE the instrument produced:
//
//   * the definitions that went are gone, by family, so putting one back is red;
//   * each definition that STAYED is paired with the compiled file that names
//     it, and BOTH halves are asserted. That second half is the point: if
//     entitylist.cpp ever stops calling SceneManager_ClientActive, this test
//     goes red asking for the stub to go too, rather than quietly protecting a
//     stub that has become dead.
//
// THE STRIPPER IS LOAD-BEARING. Every name this file reasons about is spelled
// in the prose of the files it reads -- shell_entity_orphans.cpp's own header
// lists `CanOverrideEnvZoomOwner` and `PlayLockSounds` in its "GONE AT STEP 4f"
// table, and an unstripped scan would read that table as the violation. Same
// rule as client_orphan_evictions_test.cpp, and the same rule that made 6d's
// hitbox needle a check with no subject.
//
// ANCHORS, NOT LINE COUNTS. Every file is asserted to open and to contain a
// string that identifies it, because a source-scanning test whose subject was
// renamed reads an empty string and reports every absence invariant satisfied.
//
//=============================================================================//

#include <string>
#include <string.h>
#include <vector>
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

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return StripComments( all );
}

bool FileExists( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + strRelative ).c_str(), &st ) == 0;
}

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// PHASE 1B: THE THREE GLUE FILES ARE ONE FILE.
//
// shell_ai_absent.cpp, shell_ai_orphans.cpp and shell_entity_orphans.cpp were
// merged into shell_absent.cpp, keeping only the symbols an object on the
// server LINK LINE still imports -- seventeen of the forty-odd they defined
// between them. The three names below are kept as three aliases of one path so
// that the sections of this file stay legible as the three censuses they are,
// and so that a reader who arrives from the old file names finds the answer.
//
// Each still has its own anchor, and the three anchors are DIFFERENT: an alias
// that quietly became the wrong file is a red test rather than a vacuous pass,
// which is the same reason each file had one before.
//
// ANCHOR RE-POINTED by plan_shim.md 4a item 11, lane 5a-physresidue. It was
// `#define AI_ABSENT()`, and that macro went with the last of the three stubs
// that used it -- all three were kept for mapentities.o, which Phase 2c
// evicted. The replacement is GetRecentNPCSpeech's PARAMETER LIST rather than
// its name, because this file's anchor rule is that an anchor must not be
// implied by the assertion it guards: `GetRecentNPCSpeech` is a kKept needle
// below, and an anchor containing it would satisfy that needle for free.
// RE-POINTED AGAIN IN P5: GetRecentNPCSpeech went, and the AI section's one
// survivor is g_iszFuncBrushClassname, a kKept needle -- so the anchor is the
// rest of its line, its initialiser, which no other section has.
const File_t kAiAbsent     = { "sourcesharp/game/server/shell_absent.cpp",
	"= NULL_STRING;" };
// RE-POINTED IN plan_server_native_min P1: PostFrameNavigationSystem went
// with its hook, so the anchor was RadiusDamage's definition; that went in
// plan_server_native_v2 T2, and the anchor is ss::SetPlayerSurface's.
const File_t kAiOrphans    = { "sourcesharp/game/server/shell_absent.cpp",
	"void ss::SetPlayerSurface(" };
const File_t kEntOrphans   = { "sourcesharp/game/server/shell_absent.cpp",
	"void EffectsPrecache(" };
// kEntityList STOOD HERE and went with its only needle (4a item 11, lane
// 5a-physresidue): `game/server/entitylist.cpp` is a stock TU with no object on
// either link line, so nothing this file asserts can be founded on its text.
const File_t kGameClients  = { "sourcesharp/game/server/shell_gameclients.cpp",
	"CServerGameClients" };
// RELOCATED and SPLIT by plan_shim.md Phase 2c. The anchor moves with the file
// for the same reason it existed: MapEntity_ParseAllEntities went with the dead
// band, and MapEntity_ParseWorldOnly was what the armed map load called -- until
// plan_server_novalve.md P5 moved the text walk to C#; MapEntity_SpawnWorld is
// what the level step calls now.
const File_t kMapEntities  = { "sourcesharp/game/server/shell_mapparse.cpp",
	"MapEntity_SpawnWorld" };
// ANCHOR REPAIRED AT THE PHASE 2 MERGE. This row was `game/shared/util_shared.cpp`
// anchored on `CTraceFilterSimple`; Phase 2c deleted that file and the integrator
// re-pointed the PATH here without re-pointing the ANCHOR, which is the mistake
// the anchor rule exists to make loud -- and it was, immediately. The filter
// family is not in this file: it lives in trace_filter_library.cpp (kTraceFilterLib
// below), and lane 2-utilshared deleted nine of its classes' unreachable ARMS for
// having no live importer -- lane 3f then deleted the nine classes themselves,
// for the same reason one layer out. `UTIL_ShouldShowBlood` is an out-of-line definition this file really
// carries and is not the thing asserted about it, which is the rule. (The anchor was UTIL_IsLowViolence
// until plan_server_native_v2 T2 moved that, a client-only arm, to client_util_library.cpp.)
const File_t kUtilShared   = { "sourcesharp/game/shared/util_shared_library.cpp",
	"bool UTIL_ShouldShowBlood(" };
// plan_entity.md step 4e: the shared trace-filter family left util_shared.cpp
// for this shim. It is read here for one reason only -- it is the other TU that
// DECLARES NPC_CheckBrushExclude, so it is the other place a caller could
// reappear. Anchored on a filter body rather than on that declaration, per the
// rule above: an anchor equal to the assertion cannot catch a rename.
// Re-pointed in P5: the one body is spelled SS_SIMPLE_FILTER:: (the server's
// class is ss::CTraceFilterSimple, the client's util_shared.h's).
const File_t kTraceFilterLib = { "sourcesharp/game/shared/trace_filter_library.cpp",
	"SS_SIMPLE_FILTER::ShouldHitEntity" };
const File_t kShellMovement = { "sourcesharp/game/shared/shell_movement.cpp",
	"CShellGameMovement::WorldEffect" };
const File_t kWorldAdapter = { "sourcesharp/game/server/world_adapter.cpp",
	"CPrecacheRegister" };

std::string Load( const File_t &f )
{
	const std::string s = ReadStripped( f.pszRel );
	EXPECT_FALSE( s.empty() ) << "could not open " << f.pszRel
		<< " -- every absence invariant below would pass vacuously";
	EXPECT_NE( s.find( f.pszAnchor ), std::string::npos )
		<< f.pszRel << " does not contain its anchor \"" << f.pszAnchor
		<< "\"; the file this test reasons about has moved or been rewritten";
	return s;
}

// How many times a needle occurs, not whether it occurs. A presence needle is
// satisfied by ANY one of a set of sites that must all be there, which is how
// two of this file's own facts passed a mutation that removed one arm each.
size_t CountOf( const std::string &strHaystack, const char *pszNeedle )
{
	size_t n = 0;
	const size_t len = strlen( pszNeedle );
	if ( len == 0 )
		return 0;
	for ( size_t at = strHaystack.find( pszNeedle );
	      at != std::string::npos;
	      at = strHaystack.find( pszNeedle, at + len ) )
	{
		n++;
	}
	return n;
}

} // namespace

//=============================================================================
// (1) The three husk classes
//=============================================================================

TEST( EntityBaseHusks, BothPointCommandClassnamesAreManaged )
{
	// "The behaviour is managed" as a checked fact rather than a comment.
	const std::string strClientCmd =
		ReadStripped( "sourcesharp/managed/SourceSharp.Server/Entities/Points/PointClientCommand.cs" );
	const std::string strServerCmd =
		ReadStripped( "sourcesharp/managed/SourceSharp.Server/Entities/Points/PointServerCommand.cs" );

	ASSERT_FALSE( strClientCmd.empty() ) << "PointClientCommand.cs is missing";
	ASSERT_FALSE( strServerCmd.empty() ) << "PointServerCommand.cs is missing";

	EXPECT_NE( strClientCmd.find( "class PointClientCommand" ), std::string::npos );
	EXPECT_NE( strServerCmd.find( "class PointServerCommand" ), std::string::npos );
	EXPECT_NE( strClientCmd.find( "ScriptedEntity(EntityNames.PointClientcommand)" ),
	           std::string::npos )
		<< "PointClientCommand.cs no longer claims the point_clientcommand "
		   "classname, and the native class that used to hold it is deleted";
	EXPECT_NE( strServerCmd.find( "ScriptedEntity(EntityNames.PointServercommand)" ),
	           std::string::npos )
		<< "PointServerCommand.cs no longer claims the point_servercommand "
		   "classname, and the native class that used to hold it is deleted";
	EXPECT_NE( strServerCmd.find( "sv_allow_point_servercommand" ), std::string::npos )
		<< "the managed entity stopped reading the permission convar";
}

//=============================================================================
// (2) shell_ai_absent.cpp -- what went, and what stayed with its caller
//=============================================================================

TEST( EntityBaseHusks, TheAiStubFileHasNoBehaviourOrNavigatorStubsLeft )
{
	const std::string s = Load( kAiAbsent );
	static const char *kGoneFamilies[] = {
		"CAI_BehaviorBase::", "CAI_Navigator::", "CAI_Motor::", "CAI_MoveProbe::",
		"CAI_Squad::", "CAI_Expresser::", "CAI_BaseActor::", "CAI_NetworkEditTools::",
		"CAI_Hint::", "CAI_ScriptedSequence::", "NAI_Hull::",
	};
	for ( size_t i = 0; i < sizeof( kGoneFamilies ) / sizeof( kGoneFamilies[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGoneFamilies[ i ] ), std::string::npos )
			<< "shell_ai_absent.cpp defines " << kGoneFamilies[ i ]
			<< " again. Nothing on the server link line names that family: run "
			   "`tools/4a-owes shell_ai_absent` before adding a definition here.";
	}
}

TEST( EntityBaseHusks, TheAiStubFileStillDefinesWhatIsStillCalled )
{
	const std::string s = Load( kAiAbsent );
	static const char *kKept[] = {
		"g_iszFuncBrushClassname",
	};

	// THREE MORE LEFT IN P5 (plan_server_novalve.md): world_adapter stopped
	// calling the empty AI loaders (lane E 27), and `nm --undefined-only` over
	// every server object then named none of these, so they went. Asserted
	// absent, as the lists below are.
	static const char *kGoneInP5[] = {
		"CAI_NetworkManager::InitializeAINetworks",
		"CAI_SchedulesManager::LoadAllSchedules",
		"g_AI_SchedulesManager",
		"GetRecentNPCSpeech",
	};
	for ( size_t i = 0; i < sizeof( kGoneInP5 ) / sizeof( kGoneInP5[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGoneInP5[ i ] ), std::string::npos )
			<< "shell_absent.cpp defines " << kGoneInP5[ i ] << " again, and "
			   "no server object imported it when P5 deleted it.";
	}

	// FOUR MORE NAMES LEFT THAT LIST -- plan_shim.md 4a item 11, lane
	// 5a-physresidue -- and are asserted ABSENT below under the same rule as
	// the Phase 1b five: a definition belongs here only while something owes
	// it, so a definition coming back is a failure and not an improvement.
	//
	// Measured per MANGLED SYMBOL, over the objects the ninja graph's link
	// lines name (108 server, 253 client, 0 ghosts), by
	// `tools/shim-census --mtime-policy off who-imports <mangled>`:
	//
	//   _ZN8CNodeEnt5SpawnEPKc                            0 server  0 client
	//   _ZN12CAI_BaseNPC12TaskCompleteEb                  0 server  0 client
	//   _ZN7NWCEdit22RememberEntityPositionEP11IGameEntity 0 server 0 client
	//   _Z24SceneManager_ClientActiveP11CBasePlayer       0 server  0 client
	//
	// The first three are the "loud" stubs and went as a group with the
	// AI_ABSENT macro: mapentities.o was the sole importer of all three and
	// Phase 2c evicted it.
	//
	// SceneManager_ClientActive is the one worth reading twice, because the
	// reason it was kept was a file that is not in the build. Lane
	// 3b-enginebind deleted shell_gameclients.cpp's call, and what kept the
	// stub after that was EveryKeptAiStubStillHasTheCallerItWasKeptFor's needle
	// on `game/server/entitylist.cpp` -- a STOCK translation unit with no
	// object on either link line. That needle is deleted with this change and
	// the reason is in the fact's own body.
	static const char *kGoneIn4aItem11[] = {
		"CNodeEnt::Spawn", "CAI_BaseNPC::TaskComplete",
		"NWCEdit::RememberEntityPosition", "SceneManager_ClientActive",
	};
	for ( size_t i = 0; i < sizeof( kGoneIn4aItem11 ) / sizeof( kGoneIn4aItem11[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGoneIn4aItem11[ i ] ), std::string::npos )
			<< "shell_absent.cpp defines " << kGoneIn4aItem11[ i ] << " again. "
			   "Nothing on either link line imported it when 4a item 11 deleted "
			   "it; if a caller has returned, say which object imports it and "
			   "show the who-imports run that says so.";
	}

	// FIVE NAMES LEFT THIS LIST IN PHASE 1B and are asserted ABSENT below
	// rather than quietly dropped, which is this file's standing rule: a
	// definition belongs here only while something owes it, so a definition
	// coming back is a failure and not an improvement.
	//
	// Each read 0 importers on a cold build, over the objects the ninja graph's
	// link line names -- never over _vpc_/ninja/debug/objs/, which keeps the
	// objects of translation units the build has evicted and is what let this
	// file's three subjects drift to 90% dead in the first place:
	//
	//   CAI_BaseNPC::HasCondition     0    CNodeEnt::m_nNodeCount    0
	//   CAI_BaseNPC::NotifyPushMove   0    InstancedScriptedScene    0
	//   ai_LOS_mode                   0
	//
	// ai_LOS_mode is the one that is not simply gone: it is a ConVar, so it
	// registers itself from a static constructor and no importer count can see
	// it. It MOVED, to SourceSharp.Server/ConVars.cs, and
	// PolicyKnobDeclarationTests pins both halves -- the managed declaration and
	// the C++ one being gone. Its old note here said it had to stay native
	// because game/server/baseentity.cpp held an `extern ConVar` for it; that
	// file is no longer in server_csharp.vpc and there is no baseentity.o on the
	// link line, which is what made the move possible.
	static const char *kGoneInPhase1b[] = {
		"CAI_BaseNPC::HasCondition", "CAI_BaseNPC::NotifyPushMove",
		"CNodeEnt::m_nNodeCount", "InstancedScriptedScene", "ai_LOS_mode",
	};
	for ( size_t i = 0; i < sizeof( kGoneInPhase1b ) / sizeof( kGoneInPhase1b[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGoneInPhase1b[ i ] ), std::string::npos )
			<< "shell_absent.cpp defines " << kGoneInPhase1b[ i ] << " again. "
			   "Nothing on the server link line imported it when Phase 1b "
			   "deleted it; if a caller has returned, say which in the commit.";
	}
	for ( size_t i = 0; i < sizeof( kKept ) / sizeof( kKept[ 0 ] ); i++ )
	{
		EXPECT_NE( s.find( kKept[ i ] ), std::string::npos )
			<< kKept[ i ] << " left shell_ai_absent.cpp but something on the "
			   "server link line still names it";
	}

	// NPC_CheckBrushExclude WAS THE SIXTEENTH NAME ON THIS LIST and it is now
	// asserted ABSENT rather than dropped from the list. It is the one entry
	// whose caller went: its last call was game/shared/util_shared.cpp's trace
	// filter, inside `#ifndef SS_CBASEENTITY_ALIAS` and therefore never
	// compiled, and the close deleted the stub and both forward declarations
	// together. This file's own rule -- "a definition belongs here only while
	// something owes it" -- is what makes a definition coming back a failure and
	// not an improvement, so the rule is asserted rather than the list quietly
	// shortened.
	//
	// The LIVE SUCCESSOR half is in EveryKeptAiStubStillHasTheCallerItWasKeptFor:
	// util_shared.cpp's filter still answers that question, by falling through
	// to BaseClass::ShouldHitEntity.
	EXPECT_EQ( s.find( "bool NPC_CheckBrushExclude" ), std::string::npos )
		<< "shell_ai_absent.cpp defines NPC_CheckBrushExclude again. Nothing "
		   "calls it -- the last call was in a dead guarded arm and both forward "
		   "declarations are gone -- so a definition here is a stub with no "
		   "creditor, which is the drift step 4f removed 140 of.";
}

TEST( EntityBaseHusks, EveryKeptAiStubStillHasTheCallerItWasKeptFor )
{
	// The anti-vacuous half. A stub kept for a caller that has since left is
	// exactly the drift step 4f removed 140 definitions of; this goes red when
	// the caller goes, asking for the stub to follow it.
	//
	// EVERY NEEDLE HERE IS THE CALL AND NOT THE NAME, and that is the whole
	// difficulty. Written as the bare name this fact could not fail: three of
	// these four files carry a FORWARD DECLARATION of the function they call --
	// `void SceneManager_ClientActive( CBasePlayer *player );` sits at
	// entitylist.cpp:94 -- and a declaration survives the deletion of the call.
	// Found by mutation: deleting entitylist.cpp's call left the name-shaped
	// version of this fact green. The needles below carry the ARGUMENT, which a
	// declaration does not.
	const std::string strGameClients = Load( kGameClients );
	const std::string strMapEntities = Load( kMapEntities );
	const std::string strUtilShared  = Load( kUtilShared );

	// THE entitylist.cpp NEEDLE IS GONE, AND SO IS THE STUB IT KEPT ALIVE --
	// plan_shim.md 4a item 11, lane 5a-physresidue. It was
	//
	//     EXPECT_NE( strEntityList.find( "SceneManager_ClientActive( pPlayer )" ), npos )
	//
	// and it was the last thing holding SceneManager_ClientActive's stub in
	// shell_absent.cpp after lane 3b-enginebind deleted shell_gameclients.cpp's
	// call (the note that used to stand here explained that the stub had two
	// callers and only one had gone).
	//
	// IT WAS ASKING A FILE THAT IS NOT IN THE BUILD. `game/server/entitylist.cpp`
	// is a STOCK translation unit: it has no row in server_csharp.vpc and no
	// object on either link line -- `5a-linkline.sh server | grep entitylist`
	// is empty, and so is the client one. So the needle was reading source text
	// for a call that is never compiled, and answering "this stub still has a
	// caller" on the strength of it. effects_evicted_test states the general
	// form of this in its own anchor note: a stock TU is not evidence for this
	// port, because removing stock TUs is precisely what the port does.
	//
	// The measurement that replaced it is per-symbol and reads both link lines:
	// `_Z24SceneManager_ClientActiveP11CBasePlayer` has 0 importers on each.
	// Its absence is now asserted in TheAiStubFileStillDefinesWhatIsStillCalled.
	//
	// THE RULE THIS FACT ENFORCES IS UNCHANGED -- a stub kept for a caller that
	// has since left must follow it. What changed is that one of its needles
	// could not observe the caller leaving, because the caller was never there.
	// GetRecentNPCSpeech'S CALLER WENT WITH IT IN P5: it answered 0 on every
	// call, so GetBugReportInfo writes the empty report directly. Asserted
	// absent from the caller here and from the stub file above.
	EXPECT_EQ( strGameClients.find( "GetRecentNPCSpeech(" ), std::string::npos )
		<< "shell_gameclients.cpp calls GetRecentNPCSpeech again, which P5 "
		   "deleted with its stub";
	// THESE TWO FLIP, and the flip is the finding rather than a green-suite
	// edit. Both calls were inside the band plan_shim.md Phase 2c deleted from
	// the map parse -- NWCEdit::RememberEntityPosition in
	// RememberInitialEntityPositions, CNodeEnt in MapEntity_ParseAllEntities's
	// transient-spawn shortcut -- and every export in that band had 0 importers
	// on the debug link line, with a live counter reading 0 over two
	// dm_lockdown loads in one process.
	//
	// SO TWO STUBS IN shell_ai_absent.cpp HAVE LOST THEIR LAST IMPORTER. That
	// file keeps CNodeEnt::Spawn( const char * ) and NWCEdit's node-editor
	// entry points ONLY because the map parse named them; it does not any more.
	// Deleting them is Phase 1b's ledger and another lane's file, so this fact
	// RECORDS the change rather than making it -- which is the whole point of
	// asserting the absence instead of removing the lines.
	EXPECT_EQ( strMapEntities.find( "NWCEdit::RememberEntityPosition" ), std::string::npos )
		<< "the map parse calls NWCEdit::RememberEntityPosition again. Its caller "
		   "was RememberInitialEntityPositions, which had no caller of its own; "
		   "if it is back, the spawn-list band came back with it.";
	EXPECT_EQ( strMapEntities.find( "CNodeEnt" ), std::string::npos )
		<< "the map parse names CNodeEnt again. `nm -C server.so | grep -c "
		   "CNodeEnt` read 0 before this band went -- the class is not linked -- "
		   "so a mention here is a branch that can only ever answer NULL.";
	// REWRITTEN BY THE `residue` WAVE (74b071771..a46e8d645), AND THE PAIRING IT
	// ASSERTED NEVER EXISTED IN THE BUILT CONFIGURATION.
	//
	// The needle was "NPC_CheckBrushExclude( m_pEntity, pEntity )". At
	// 74b071771 that call was game/shared/util_shared.cpp:335, inside
	// `#ifndef SS_CBASEENTITY_ALIAS` (util_shared.cpp:333-340) nested in
	// `#ifndef CLIENT_DLL`. SS_CBASEENTITY_ALIAS is defined in every
	// configuration this tree builds, so the call WAS NEVER COMPILED, and the
	// wave deleted the dead arm around it. Checked rather than assumed:
	// `nm -u` over every object in
	// src/_vpc_/ninja/debug/objs/server_sourcesharp names NPC_CheckBrushExclude
	// in none of them, so util_shared.o has never carried an undefined
	// reference to it -- before the wave or after.
	//
	// THE DECLARATION HAS SINCE GONE TOO, AND THE COUNT IS NOW ZERO.
	//
	// The paragraph above described the state one commit earlier: no caller, but
	// a surviving forward declaration, so the honest pair was "exactly one
	// mention, and it is a declaration". The close finished the job -- it
	// deleted shell_ai_absent.cpp's stub and BOTH forward declarations in one
	// commit, which is what NpcCheckBrushExcludeIsAStubWithNoCallerLeft said in
	// as many words would happen when the stub went.
	//
	// So the pair is re-made at the new state, and it is still two-sided,
	// because "nothing names it" alone would pass over an empty or unreadable
	// util_shared.cpp:
	//
	//   absence  -- util_shared.cpp names NPC_CheckBrushExclude nowhere in code;
	//   presence -- and the FILTER THAT USED TO ASK IT STILL ANSWERS, by falling
	//               through to the base filter. Deleting the whole
	//               ShouldHitEntity override would satisfy the absence and
	//               silently change what every trace in the game collides with.
	//
	// Load() has already proved the file opened and matched its anchor, and
	// ReadStripped() has removed util_shared.cpp's own prose about the old call,
	// so neither half can be satisfied by a comment.
	EXPECT_EQ( CountOf( strUtilShared, "NPC_CheckBrushExclude" ), 0u )
		<< "the shim library names NPC_CheckBrushExclude in code. There is "
		   "no definition left for it to reach -- shell_ai_absent.cpp's stub went "
		   "with the declarations -- so a mention here is either a link error or "
		   "a call to a function nothing defines.";

	// THE PRESENCE HALF MOVED FILE, BY plan_shim.md Phase 2c, lane 2-utilshared,
	// AND MOVING IT IS THE POINT RATHER THAN A CONCESSION.
	//
	// The pair used to be "util_shared.cpp does not name the exclusion" and
	// "the filter that used to ask it still answers". That second clause was
	// anchored inside `CTraceFilterEntity::ShouldHitEntity`, and that class is
	// gone: it was file-local, declared in no header, constructed only by the
	// three UTIL_TraceEntity overloads, and the census over both link lines
	// found no importer of any of them.
	//
	// So the question "does a trace still collide with what it used to?" has a
	// different file to ask. `CTraceFilterSimple::ShouldHitEntity` in
	// sourcesharp/game/shared/trace_filter_library.cpp is what every trace in
	// the game runs through now -- CTraceFilterEntity only ever ADDED to it and
	// then fell through to it -- so the surviving question is that filter's own
	// ask, and it is asserted at its new address with the file proved read.
	//
	// ANCHORED, BECAUSE trace_filter_library.cpp HAD TEN FILTERS AND MANY
	// IDENTICAL FALL-THROUGH LINES. A bare find() over the file would be
	// satisfied by any of them and would stay green with the one that matters
	// deleted -- which is precisely the failure this clause exists to catch. So
	// the search starts at the collision-group ask that is unique to the right
	// method and looks only at what follows it.
	//
	// It has ONE now -- lane 3f deleted the nine with no caller on either link
	// line -- and the anchor stays exactly as it is. The reason it was written
	// is the reason it is kept: the identical fall-through lines are a property
	// of how these filters are written, not of how many there happen to be
	// today, and re-widening a search because the file got smaller is how the
	// next one of these goes quietly green.
	const std::string strTraceLib = Load( kTraceFilterLib );
	const size_t iAsk = strTraceLib.find( "pGameEntity->ShouldCollide( m_collisionGroup, contentsMask )" );
	ASSERT_NE( std::string::npos, iAsk )
		<< "CTraceFilterSimple::ShouldHitEntity no longer asks the entity "
		   "whether it should collide, so the anchor for the clause below has "
		   "moved. Re-point it rather than widening the search: an unanchored "
		   "search matches the wrong filter.";

	const std::string strAfterAsk = strTraceLib.substr( iAsk, 600 );
	EXPECT_NE( strAfterAsk.find( "g_pGameRules->ShouldCollide( m_collisionGroup," ),
	           std::string::npos )
		<< "the shared trace filter stopped asking the game rules about the "
		   "collision-group pair. That ask IS the successor to the NPC brush "
		   "exclusion: armed there is no NPC for the exclusion to be about, and "
		   "the group rules are what decides the same question for every entity "
		   "that is not one. Without this clause the absence above would be "
		   "satisfied by deleting the filter entirely. Text after the anchor "
		   "was: "
		<< strAfterAsk;
}

// NpcCheckBrushExcludeIsAStubWithNoCallerLeft STOOD HERE and is deleted with its
// subject, which is what its own body said would happen: "when the stub goes,
// the two declarations go with it and this fact is deleted in the same commit."
// shell_ai_absent.cpp no longer defines NPC_CheckBrushExclude and neither
// util_shared.cpp nor trace_filter_library.cpp declares it. What was left of
// that fact did NOT go with it: the absence of the definition is asserted in
// TheAiStubFileStillDefinesWhatIsStillCalled and the absence of the call, with
// the fall-through that replaced it, is the pair immediately above.

TEST( EntityBaseHusks, NeitherCNodeEntSpawnOverloadIsDefinedAnyMore )
{
	// THE PAIR IS NOW ABSENT ON BOTH HALVES, which is what this fact has been
	// waiting for since Phase 2c -- plan_shim.md 4a item 11, lane
	// 5a-physresidue.
	//
	// The fact used to REQUIRE `int CNodeEnt::Spawn( const char *pMapData )` to
	// be present, and said in its own failure message that its absence would be
	// "very likely CORRECT" but belonged in a ledger with a 0-importer proof
	// beside it rather than as a silent deletion -- "so this fact fails until
	// somebody writes that down". This is that, written down:
	//
	//   tools/shim-census --mtime-policy off who-imports _ZN8CNodeEnt5SpawnEPKc
	//     server 0 object files import it
	//     client 0 object files import it   (108 / 253 objects, 0 ghosts)
	//
	// Both overloads are therefore gone, for the same reason at different
	// times: the void one lost mapentities.o in Phase 1b, and the map-data one
	// lost it in Phase 2c when MapEntity_ParseAllEntities's transient-spawn
	// shortcut went.
	//
	// WHY A PER-OVERLOAD ASSERTION IS STILL WORTH TWO LINES rather than one on
	// the bare name: the ambiguity that produced this fact is still live. An
	// nm-derived keep list collapses both overloads to one name, and the two
	// halves have been on opposite sides of "kept" twice now. Keeping a needle
	// per overload is what makes a half coming back name itself.
	//
	// SOUNDNESS FOR A VIRTUAL. Spawn is virtual, so a dispatch through a
	// CNodeEnt vtable would emit no undefined symbol for it. No object on
	// either link line defines or references `vtable for CNodeEnt` or
	// `typeinfo for CNodeEnt`; the same nm sweep over CStandaloneEntity prints
	// eight rows, which is the control.
	const std::string s = Load( kAiAbsent );
	EXPECT_EQ( s.find( "int CNodeEnt::Spawn( const char *pMapData )" ), std::string::npos )
		<< "the map-data CNodeEnt::Spawn overload is back. Its last importer "
		   "was mapentities.o, which is not on either link line; if a caller "
		   "has returned, show the who-imports run that names it.";
	EXPECT_EQ( s.find( "void CNodeEnt::Spawn( void )" ), std::string::npos )
		<< "the void CNodeEnt::Spawn overload is back, and nothing imports "
		   "either overload now.";
}

//=============================================================================
// (3) shell_ai_orphans.cpp -- the shot path stays, the node editor goes
//=============================================================================

TEST( EntityBaseHusks, TheAiOrphansKeepTheShotPath )
{
	// THE SHOT PATH DID NOT SURVIVE PHASE 1B, and this fact records that rather
	// than being deleted, because it is a BEHAVIOUR DIFFERENCE from stock and
	// not a tidy-up.
	//
	// The three EntityShot_* functions were kept here on the argument that they
	// "are the shooting path, not AI: stubbing or dropping it makes shooting
	// THROUGH something stop working silently". That argument was about their
	// CALLERS, and the callers went. Measured on a cold build over the objects
	// the ninja graph's link line names:
	//
	//   EntityShot_TraceAttackToTriggers  0 importers
	//   EntityShot_CreateBubbleTrail      0 importers
	//   EntityShot_UpdateStatistics       0 importers
	//
	// The file's own note already said the last of those callers --
	// baseentity_shared.o and gamerules.o -- were what kept them; neither object
	// is on the link line now. CTriggerTraceEnum, the file-local enumerator
	// TraceAttackToTriggers drove, went with them.
	//
	// SO A BULLET NO LONGER TELLS A TRIGGER IT CROSSED IT. plan_shim.md §10
	// carries that as an open question with the experiment that settles it:
	// shoot a trigger_hurt with a managed weapon on the live gate. If the answer
	// is that it must, what comes back is the three AND a caller for each -- and
	// this assertion inverts again in the same commit.
	const std::string s = Load( kAiOrphans );
	static const char *kGoneWithTheirCallers[] = {
		"EntityShot_TraceAttackToTriggers", "EntityShot_CreateBubbleTrail",
		"EntityShot_UpdateStatistics", "CTriggerTraceEnum",
	};
	for ( size_t i = 0; i < sizeof( kGoneWithTheirCallers ) / sizeof( kGoneWithTheirCallers[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGoneWithTheirCallers[ i ] ), std::string::npos )
			<< kGoneWithTheirCallers[ i ] << " is back in shell_absent.cpp "
			<< "without this fact being rewritten. It was deleted because "
			   "nothing on the server link line imported it; bringing it back "
			   "without a caller is a stub with no creditor.";
	}

	// AND THE ONE THAT SURVIVED, UNTIL plan_server_native_min P1. The anchor
	// (ss::SetPlayerSurface) keeps the absences above from passing over an emptied
	// file. PostFrameNavigationSystem went with its hook, whose whole body was
	// a bool toggle nothing read, and its kServerSystems[] row went in the same
	// commit, so no null accessor reaches the game-system list.
	EXPECT_EQ( s.find( "PostFrameNavigationSystem" ), std::string::npos )
		<< "PostFrameNavigationSystem is back in shell_absent.cpp. Say which "
		   "reader needs the hook, and restore its kServerSystems[] row with it.";

	// THE FOURTH ONE IS THE EXCEPTION AND IT IS ASSERTED ABSENT.
	//
	// `CBaseEntity::HandleShotImpactingGlass` was on the list above until the
	// close, on the same "it is the shooting path, not AI" argument. It is not
	// a lift and never was: it names `this`, so it has to be a CBaseEntity
	// member, and the file's own banner said step 3 would have to answer for it
	// separately. Step 3 answered by removing its ONLY caller --
	// game/shared/baseentity_shared.cpp:2119, and that file is in BOTH removal
	// sets (git show fd5f3bc8d). Armed there is no caller, no `this` to be the
	// attacker of, and no CBaseEntity scope to define it in.
	//
	// AND IT IS GATE 1'S LAST STRONG SERVER SYMBOL. ROUTE_TO_DONE 1a names
	// exactly one `CBaseEntity::` definition of nm type T in a kept object, and
	// this was it. That is the LIVE SUCCESSOR this absence pairs with, and it is
	// measured rather than asserted here: entity_gates_test's
	// EveryGatedRowOnTodaysBinariesReadsZeroOnBothSides reads gate 1's nm row
	// off the built server.so and requires 0. If this definition came back that
	// row would go to 1, so the two facts fail together and neither can be
	// satisfied by editing the other's subject.
	EXPECT_EQ( s.find( "HandleShotImpactingGlass" ), std::string::npos )
		<< "shell_ai_orphans.cpp defines HandleShotImpactingGlass again. Nothing "
		   "calls it -- its only caller left with baseentity_shared.cpp -- and as "
		   "a CBaseEntity:: member it is a gate-1 symbol, so this is the port "
		   "coming apart rather than a capability being restored. The behaviour "
		   "was lost when the CALLER left, which is the flip's decision, recorded "
		   "in ROUTE_TO_DONE step 2.";
}

TEST( EntityBaseHusks, TheNodeEditorPickersAndTheirGeometryAreGone )
{
	const std::string s = Load( kAiOrphans );
	EXPECT_EQ( s.find( "FindPickerAINode" ), std::string::npos )
		<< "shell_ai_orphans.cpp defines FindPickerAINode again; its only "
		   "caller was gameinterface.cpp, which server_csharp.vpc does not name";
	EXPECT_EQ( s.find( "FindPickerAILink" ), std::string::npos )
		<< "shell_ai_orphans.cpp defines FindPickerAILink again";
	EXPECT_EQ( s.find( "PointOnLineNearestPoint" ), std::string::npos )
		<< "shell_ai_orphans.cpp defines PointOnLineNearestPoint again; nothing "
		   "on the server link line names it (util.cpp's "
		   "UTIL_PointOnLineNearestPoint is a different symbol)";
}

//=============================================================================
// (4) shell_entity_orphans.cpp
//=============================================================================

TEST( EntityBaseHusks, TheEntityOrphansDroppedEveryStubWhoseCallerLeft )
{
	const std::string s = Load( kEntOrphans );
	static const char *kGone[] = {
		"CanOverrideEnvZoomOwner", "RagdollMagnet_ForceFor", "WeaponManager_AmmoMod",
		"CEnvSoundscape::UpdateForPlayer", "soundscape_debug",
		"g_voteControllerGlobal", "CVoteController::TryCastVote",
		"PlayLockSounds", "UTIL_ItemCanBeTouchedByPlayer",
		"CBreakable::MaterialSound", "CBreakable::MaterialSoundRandom",
		// PHASE 4 EXIT. This one did not go because a file was deleted -- it
		// went because the three dynamic_cast< CBaseFilter * > sites that wanted
		// its typeinfo went. See TheFilterHierarchyCannotBeConstructed below for
		// the pair of facts that hold the two halves together.
		"CBaseFilter::PassesFilter", "CBaseFilter::PassesDamageFilter",
		"CBaseFilter::InputTestActivator", "BEGIN_DATADESC( CBaseFilter )",
	};
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGone[ i ] ), std::string::npos )
			<< "shell_entity_orphans.cpp defines " << kGone[ i ] << " again. "
			   "Nothing on the server link line names it: run "
			   "`tools/4a-owes shell_entity_orphans` before adding one back.";
	}
}

TEST( EntityBaseHusks, TheEntityOrphansKeepWhatIsStillNamed )
{
	const std::string s = Load( kEntOrphans );
	static const char *kKept[] = {
		"ss::SetPlayerSurface",
		// RadiusDamage left in plan_server_native_v2 T2: no managed caller.
		"UTIL_BloodSpray", "EffectsPrecache",
	};
	for ( size_t i = 0; i < sizeof( kKept ) / sizeof( kKept[ 0 ] ); i++ )
	{
		EXPECT_NE( s.find( kKept[ i ] ), std::string::npos )
			<< kKept[ i ] << " left shell_absent.cpp but something on "
			   "the server link line still names it";
	}

	// DrawMessageEntities LEFT IN plan_server_native_min P1: its message list
	// was always empty (the managed point_message draws itself) and its one
	// caller went with it.
	EXPECT_EQ( s.find( "DrawMessageEntities" ), std::string::npos )
		<< "shell_absent.cpp defines DrawMessageEntities again; say which "
		   "server object needs it.";

	// CEnvMicrophone::OnSoundPlayed LEFT IN P5: sound_emitter's calls had gone
	// and no server object imported it.
	EXPECT_EQ( s.find( "CEnvMicrophone::OnSoundPlayed" ), std::string::npos )
		<< "shell_absent.cpp defines CEnvMicrophone::OnSoundPlayed again, and "
		   "nothing on the server link line names it.";

	// THREE LEFT THIS LIST IN PHASE 1B, and each left for the reason this file
	// exists to check: the object that imported it is no longer on the link
	// line. Asserted absent rather than dropped.
	//
	//   GetSkyCameraList     0 -- CBaseEntity::GetEntitySkybox was its caller
	//   GetSpringAttachments 0 -- physics_debug_entity's constraint dump
	//   FindPickerEntity     0 -- ent_text's picker
	//
	// GetCurrentSkyCamera USED TO BE THE ONE OF THAT FAMILY THAT STAYED, on the
	// keep list above, with "shell_playerlocaldata.o still imports it" as the
	// reason. THAT IS NO LONGER TRUE and the note is kept because the pair
	// being on opposite lists is what needed explaining, and now the
	// explanation is that they are on the same list again.
	//
	// The importer was shell_playerlocaldata.cpp's ClientData_Update, which
	// asked for the camera and fell through to SourceSharp_FillSkyboxParams on
	// the NULL it always got. Lane 3b-enginebind deleted that function -- its
	// only caller sat behind a hardcoded NULL -- and its live twin,
	// StandaloneClientData_Update, calls SourceSharp_FillSkyboxParams directly
	// and never asks for a camera. Re-measured rather than inferred from the
	// source edit:
	//
	//   tools/shim-census --mtime-policy off who-imports _Z19GetCurrentSkyCamerav
	//     server 0 object files import it
	//     client 0 object files import it
	//
	// So both sky-camera accessors are now absent, each having outlived its
	// last importer by one phase.
	static const char *kGoneInPhase1b[] = {
		"GetSkyCameraList", "GetSpringAttachments", "FindPickerEntity",
		"GetCurrentSkyCamera",
	};
	for ( size_t i = 0; i < sizeof( kGoneInPhase1b ) / sizeof( kGoneInPhase1b[ 0 ] ); i++ )
	{
		EXPECT_EQ( s.find( kGoneInPhase1b[ i ] ), std::string::npos )
			<< "shell_absent.cpp defines " << kGoneInPhase1b[ i ] << " again. "
			   "Nothing on the server link line imported it when Phase 1b "
			   "deleted it.";
	}
}

TEST( EntityBaseHusks, OnlyTheHandleShapedSurfaceFunnelIsDefined )
{
	// gamemovement.cpp calls the pointer overload only in its `#else` arm,
	// which is the stock build's. The handle one is the live path.
	const std::string s = Load( kEntOrphans );
	EXPECT_NE( s.find( "SetPlayerSurface( const CBaseHandle &hPlayer" ), std::string::npos )
		<< "the handle-shaped surface funnel is gone; the movement loop calls it "
		   "every time the material under a player changes";
	EXPECT_EQ( s.find( "SetPlayerSurface( CBasePlayer *" ), std::string::npos )
		<< "the pointer-shaped overload is back; it has no caller in this build";

	// REPOINTED AT STEP 8f-igamemovement. The LIVE caller is the shim's
	// MoveEffect channel now -- SOURCESHARP_MOVE_EFFECT_SET_PLAYER_SURFACE, which
	// the managed loop reaches every time the material under a player changes.
	// gamemovement.cpp still has a call of its own in CategorizePosition, but
	// that is stock's body and nothing calls it unless
	// sourcesharp_movement_managed is 0, so a needle there would go on passing
	// after the file's last live caller had gone. Both are read, so neither can
	// be lost quietly.
	const std::string strShim = Load( kShellMovement );
	EXPECT_NE( strShim.find( "SetPlayerSurface( mv->m_nPlayerHandle" ), std::string::npos )
		<< "the managed movement loop's MoveEffect channel stopped calling the "
		   "handle-shaped funnel; the material under a player stops reaching "
		   "env_player_surface_trigger";
}

TEST( EntityBaseHusks, TheSelfRegisteringPrecacheIsKeptEvenThoughNothingNamesIt )
{
	// The instrument's blind spot, pinned so nobody deletes EffectsPrecache on
	// the strength of `4a-owes` naming nothing: it is on a list walked at level
	// load, and world_adapter.cpp is what walks it.
	const std::string s = Load( kEntOrphans );
	EXPECT_NE( s.find( "PRECACHE_REGISTER_FN( EffectsPrecache )" ), std::string::npos )
		<< "EffectsPrecache lost its registration. `tools/4a-owes` names it "
		   "nowhere BY DESIGN -- it is reached through CPrecacheRegister's list, "
		   "not by symbol -- so an owes-driven deletion would take six live "
		   "game sounds with it.";

	const std::string strWorld = Load( kWorldAdapter );
	EXPECT_NE( strWorld.find( "CPrecacheRegister" ), std::string::npos )
		<< "world_adapter.cpp no longer carries the precache list that "
		   "EffectsPrecache registers itself on";
}

TEST( EntityBaseHusks, TheGlueFileExistsAndIsOne )
{
	// A rename would make every absence assertion above read an empty string.
	//
	// WAS TheThreeGlueFilesExistAndAreShim. Phase 1b merged the three into one,
	// so the other half of this fact is the one that matters now: the three old
	// names must NOT be back, because a file that reappears takes its stubs with
	// it and no importer count was ever taken for them again.
	EXPECT_TRUE( FileExists( "sourcesharp/game/server/shell_absent.cpp" ) );

	EXPECT_FALSE( FileExists( "sourcesharp/game/server/shell_ai_absent.cpp" ) );
	EXPECT_FALSE( FileExists( "sourcesharp/game/server/shell_ai_orphans.cpp" ) );
	EXPECT_FALSE( FileExists( "sourcesharp/game/server/shell_entity_orphans.cpp" ) );
}

//=============================================================================
// (5) THE FILTER HIERARCHY, RETIRED AT THE PHASE 4 EXIT.
//
// CBaseFilter was the last SDK entity class the shim itself defined, and it was
// there for one job: to be the typeinfo three `dynamic_cast< CBaseFilter * >`
// sites tested against. Nothing constructed one -- over the 166 objects on the
// server link line exactly two named the class at all, this file's subject
// (which defined it) and baseentity.o (which wanted the typeinfo) -- so all
// three casts answered NULL and every branch they fed was dead.
//
// The deletion is checked TOGETHER WITH WHAT CARRIES IT, per this file's
// standing rule: a filter is resolved by NAME on the managed side, and both
// halves are asserted, so removing the managed path is as red as putting the
// SDK one back.
//
// THE NEEDLES ARE SPELLED TO SURVIVE THE SUBSTRING TRAP. `dynamic_cast` is the
// whole word rather than `CBaseFilter`, because both files' PROSE explains what
// used to be there and names the class repeatedly -- Load() strips comments, so
// the prose is invisible to these reads, and an unstripped scan would report
// the explanation as the violation. Same rule as (2) above.
//=============================================================================

// THE HEADER ITSELF IS GONE (after the server gc cull, 2026-10-01). Both facts
// that stood here -- it cast nothing, and it asked the managed side at both of
// EntityFilter_t's questions -- were about a header no compiled object
// included (ninja's deps log names it nowhere): EntityFilter_t had no user, and
// its inline Passes called StandaloneFilterAnswer, which nothing defined. A
// filter is resolved by name on the managed side and that is unchanged.
TEST( EntityBaseHusks, TheFilterHeaderIsGoneAndNothingIncludesIt )
{
	// Anchor: a sibling overlay header in the same directory is still read, so
	// the absence below is about the file and not about an unreadable tree.
	ASSERT_FALSE( ReadStripped( "game/shared/shareddefs.h" ).empty() )
		<< "game/shared/shareddefs.h is unreadable; the absence below would prove nothing";

	EXPECT_TRUE( ReadStripped( "game/server/entityfilter.h" ).empty() )
		<< "game/server/entityfilter.h is back; say which compiled object needs it.";
}

TEST( EntityBaseHusks, NoCompiledShimFileDefinesTheFilterBase )
{
	const std::string s = Load( kEntOrphans );
	EXPECT_EQ( s.find( "CBaseFilter::" ), std::string::npos )
		<< "shell_entity_orphans.cpp defines CBaseFilter again. Nothing on the "
		   "server link line names it: run `tools/4a-owes shell_entity_orphans` "
		   "before adding one back.";
	EXPECT_EQ( s.find( "#include \"filters.h\"" ), std::string::npos )
		<< "filters.h is included again; it declares CBaseFilter and this file "
		   "no longer defines any part of that class";
}
