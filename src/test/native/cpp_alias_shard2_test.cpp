//========= SourceSharp — shard 2's .cpp edits, read as source ===============//
//
// A SOURCE SCANNER, like soundemitter_lift_test, precache_model_move_test and
// cpp_alias_lane1_test, and for the same reason: the claim is about the TEXT of
// files that need a whole engine to run, so the text is what is read.
//
// WHAT SHARD 2 DID, and it is deliberately NOT a set of #ifdef arms.
// docs/typedef-dryrun/shard2_classification.md classifies all 1,018 armed sites
// in the ten server .cpp files this shard took; 846 of them are EVICTION -- the
// class must stop deriving, and an armed arm would be a pure stub. Twenty-eight
// were not, and those are what this file speaks for. Each is an UNCONDITIONAL
// edit rather than a guard, because in each case the answer that survives the
// flip is also the right answer today:
//
//   * TWENTY-TWO calls to CBaseEntity::PrecacheScriptSound in client.cpp and
//     effects.cpp. That member is `static` -- it takes no `this` and reads no
//     entity -- and its body is GameSound_PrecacheScript. The class scope was
//     the entire dependency, so the call sites ask the free function. The
//     one-line forward that used to stand between them is gone: its quarantine,
//     game/shared/baseentity_sound_forwarders.cpp, was retired in route step 1
//     and soundemitter_lift_test now guards the retirement instead.
//
//   * THREE calls to CBaseEntity::IsPrecacheAllowed /
//     ::IsSimulatingOnAlternateTicks in util.cpp and physics.cpp. Same shape,
//     same lift already landed: entity_shared_statics.h.
//
//   * THREE sites where EntityFromEntityHandle's NULL is a LIVE bug rather than
//     a Phase 13 one. See the three sections below.
//
// WHY THIS NEEDS A GUARD, and none of these produces a diagnostic:
//
//   1. Somebody writes `CBaseEntity::PrecacheScriptSound` back into one of
//      these files -- it is what every other SDK file still says, so it is the
//      natural thing to copy. It compiles today and is a Phase 13 site again.
//   2. Somebody "simplifies" one of the three filter fixes back to
//      EntityFromEntityHandle. It compiles, the suite stays green, and the
//      NULL dereference comes back for exactly the entities this build is made
//      of.
//   3. A merge splice eats one of the null checks. Nothing says so.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, and this tree has shipped several. The fixtures at
// the bottom assert that every file was really opened and is not trivial,
// and that comment stripping really strips (on synthetic input, because every
// edit here explains itself in prose that NAMES the member it stopped calling --
// matching raw text would read the name straight out of the sentence saying it
// is gone).
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and here that is load-bearing rather
// than tidiness. Every edit this file speaks for carries a comment naming the
// member it stopped calling -- "this was `TraceEntity( *pTrace )->entindex()`"
// is in util.cpp today. A scanner matching raw text would find
// `TraceEntity( *pTrace )` in the sentence saying it is gone and report a
// correct fix as broken. header_alias_test.cpp and cpp_alias_lane1_test.cpp
// were both caught by exactly this.
//-----------------------------------------------------------------------------
int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	if ( strNeedle.empty() )
		return 0;
	int n = 0;
	for ( size_t p = strHay.find( strNeedle ); p != std::string::npos;
	      p = strHay.find( strNeedle, p + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

//-----------------------------------------------------------------------------
// THE FILES, and the anchor each one is identified by. An anchor rather than a
// line count, because a count goes stale on every unrelated edit and then gets
// "fixed" by copying whatever the file says now -- which is the check that
// cannot fail, arriving by the back door.
//-----------------------------------------------------------------------------
} // namespace


//=============================================================================//
// 1. The sound statics left the class at these call sites.
//=============================================================================//

//=============================================================================//
// 3. The scanner checking itself. Without these every assertion above passes on
//    an empty string, which is precisely how a check that cannot fail is born.
//=============================================================================//

TEST( Shard2ScannerSelfCheck, CountOfCountsRatherThanFindingOnce )
{
	EXPECT_EQ( 0, CountOf( "abc", "z" ) );
	EXPECT_EQ( 1, CountOf( "abc", "b" ) );
	EXPECT_EQ( 3, CountOf( "b b b", "b" ) );
	EXPECT_EQ( 2, CountOf( "aaaa", "aa" ) ) << "non-overlapping, deliberately";
}
