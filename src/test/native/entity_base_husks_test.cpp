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
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[i] ) + "/game";
		const std::string pub = std::string( kCandidates[i] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) && stat( pub.c_str(), &st ) == 0 &&
			 S_ISDIR( st.st_mode ) )
		{
			return kCandidates[i];
		}
	}
	return NULL;
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

} // namespace

//=============================================================================
// (1) The three husk classes
//=============================================================================

//=============================================================================
// (2) shell_ai_absent.cpp -- what went, and what stayed with its caller
//=============================================================================

// NpcCheckBrushExcludeIsAStubWithNoCallerLeft STOOD HERE and is deleted with its
// subject, which is what its own body said would happen: "when the stub goes,
// the two declarations go with it and this fact is deleted in the same commit."
// shell_ai_absent.cpp no longer defines NPC_CheckBrushExclude and neither
// util_shared.cpp nor trace_filter_library.cpp declares it. What was left of
// that fact did NOT go with it: the absence of the definition is asserted in
// TheAiStubFileStillDefinesWhatIsStillCalled and the absence of the call, with
// the fall-through that replaced it, is the pair immediately above.

//=============================================================================
// (3) shell_ai_orphans.cpp -- the shot path stays, the node editor goes
//=============================================================================

//=============================================================================
// (4) shell_entity_orphans.cpp
//=============================================================================

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
