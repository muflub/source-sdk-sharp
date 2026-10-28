//========= SourceSharp — the precache statics really did leave the class =====//
//
// A SOURCE SCANNER, like collision_origin_identity_test and sdk_player_cast_test
// and for the same reason: the claim is about the TEXT of files that need a
// whole engine to run, so the text is what is read.
//
// WHAT WAS DONE. CBaseEntity::PrecacheModel and
// CBaseEntity::PrecacheModelComponents were static members of CBaseEntity that
// never touched an entity. Their bodies -- and the modelsounds.cache apparatus
// that serves them -- moved to sourcesharp/game/server/precache_model.cpp as the free
// functions Precache_Model and Precache_ModelComponents. The members stayed as
// one-line forwarders, because 734 call sites in 262 SDK files reach them
// unqualified from inside member functions.
//
// WHAT THAT BOUGHT, MEASURED. `src/evict_consumers debug server baseentity.o`
// went 3,614 refs / 71 consumers -> 3,601 / 68. shell_server.o and
// shell_standalone_base.o each had exactly ONE undefined reference to
// baseentity.o and it was CBaseEntity::PrecacheModel(char const*, bool), so a
// static with no entity in it was the entirety of their dependency on the class;
// both left the set. SoundEmitterSystem.o left too and was not predicted: it
// calls ClearModelSoundsCache(), a free function that happened to be DEFINED in
// baseentity.cpp, and that definition moved with the block it belonged to.
//
// WHY THIS NEEDS A GUARD AT ALL, AND IT IS NOT PEDANTRY. The property that
// bought those three files is not "the code is in a new file". It is that
// precache_model.cpp asks baseentity.o for NOTHING. That property is invisible
// at the call site, invisible in review, and produces no diagnostic when it is
// lost -- the build stays green and the three files quietly rejoin the consumer
// set, which nobody would notice until the next time somebody ran the probe and
// wondered why the number went backwards. One `pEnt->GetAbsOrigin()` added to
// this file for a plausible reason would do it.
//
// So two claims are pinned:
//
//   1. precache_model.cpp names CBaseEntity ONLY through the two statics that
//      are defined in OTHER objects -- IsPrecacheAllowed (baseentity_shared.o)
//      and PrecacheScriptSound (SoundEmitterSystem.o). NEITHER IS REACHED
//      THROUGH THE CLASS ANY MORE: route step 1 retired both quarantines, so
//      this file's calls are EntityPrecache_IsAllowed() and
//      GameSound_PrecacheScript() and it names ZERO CBaseEntity:: things. Any CBaseEntity::
//      name, and any instance-level use at all, goes red.
//
//   2. The two members left behind in baseentity.cpp are FORWARDERS and nothing
//      else -- a single call each, passing their arguments straight through. The
//      failure this guards is somebody "helpfully" reinstating logic in the
//      forwarder, which would make the free function a second implementation
//      that drifts from the one the SDK actually calls.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given -- this project has now found fifteen checks that
// could not fail. The fixtures below assert that precache_model.cpp was really
// opened, that a floor of bytes came back, that the specific bodies were really
// located, and that the CBaseEntity:: name extractor finds what it should in
// hand-written positive and negative fixtures.
//
// game/server/baseentity.cpp is no longer compiled and has left the source
// tree. The checks that read it -- no copy of the forwarders or the free
// functions left there, and the extractor's real-file control -- were culled
// with it. No compiled file names CBaseEntity:: in code any more, so the
// extractor's positive control is the hand-written fixture alone.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <ctype.h>
#include <dirent.h>

#include <fstream>
#include <set>
#include <cstring>
#include <string>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
// SOURCESHARP: the two forwarders MOVED into the class body in baseentity.h,
// and a BaseEntityHeader() accessor here read them there. game/server/
// baseentity.h is compiled by nothing since plan_server_novalve.md P5 and left
// the farm in P6; Claim 2 now asserts its absence instead (see below).

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a CBaseEntity:: name written in prose --
// this file's own subject matter, and precache_model.cpp is full of it -- is not
// mistaken for code.
//
// Not a C++ lexer: it does not track string or character literals. It does not
// need to, because the only thing built on top of it is a search for the token
// "CBaseEntity::", and a source file containing that inside a string literal
// would be a finding in itself rather than a false positive to be engineered
// around.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Every distinct name reached through "CBaseEntity::" in code (comments already
// stripped by the caller).
//-----------------------------------------------------------------------------
std::set< std::string > CBaseEntityNamesIn( const std::string &strCode )
{
	std::set< std::string > names;
	const std::string strNeedle( "CBaseEntity::" );

	size_t at = 0;
	for ( ;; )
	{
		at = strCode.find( strNeedle, at );
		if ( at == std::string::npos )
			break;

		size_t p = at + strNeedle.size();
		std::string strName;
		while ( p < strCode.size() &&
		        ( isalnum( (unsigned char)strCode[ p ] ) || strCode[ p ] == '_' ) )
		{
			strName += strCode[ p ];
			p++;
		}

		if ( !strName.empty() )
			names.insert( strName );
		at = p;
	}
	return names;
}

//-----------------------------------------------------------------------------
// The body of a named out-of-line definition, squeezed, braces excluded.
//
// Same shape as collision_origin_identity_test's extractor, and it inherits that
// file's hard-won correction: a rejected candidate ADVANCES the search rather
// than ending it, because a CALL to the member can appear in the file BEFORE the
// definition of it. Stopping at the first occurrence would report "not found"
// for a member that is defined ten lines later, and the test would pass while
// proving nothing.
//
// Returns the empty string when not found -- and ALSO when the body is found and
// is empty. That conflation is asserted, and its consequences reasoned about, in
// BodyExtractorConflatesNotFoundWithAnEmptyBody below.
//-----------------------------------------------------------------------------
} // namespace

//=============================================================================//
// Claim 1 -- precache_model.cpp asks baseentity.o for nothing.
//=============================================================================//

//-----------------------------------------------------------------------------
// The allowed names, and only those.
//
// IsPrecacheAllowed is defined in baseentity_shared.cpp -> baseentity_shared.o.
// PrecacheScriptSound is defined in sound_emitter.cpp -> SoundEmitterSystem.o.
// Neither lives in baseentity.o, so neither costs this file a place in the
// consumer set. A THIRD name almost certainly would, and this is the assertion
// that says so out loud.
//
// The set stays at two even though NEITHER is spelled through the class any
// more -- both quarantines were retired in route step 1 and both call sites now
// name a free function. This is a PERMISSION list, not an inventory: it says
// which two names would be tolerable if they came back (because neither lives
// in baseentity.o, so neither would cost this file a place in the consumer
// set), and the point of the assertion is the THIRD name that would. The exact
// inventory -- now zero -- is pinned separately by
// ExtractorSeesExactlyWhatThisFileNames below.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// No instance-level use of an entity, at all.
//
// The statics above are reached through the class name. A '->' or a '.' on an
// entity would be a different thing entirely: it would mean this file had
// acquired a CBaseEntity to talk to, which is precisely what moving it out of
// baseentity.cpp was for. The cheap mechanical proxy is that the file declares
// no CBaseEntity pointer or reference of any kind.
//-----------------------------------------------------------------------------
//=============================================================================//
// Claim 2 -- what stayed behind is a forwarder and nothing else.
//=============================================================================//

//-----------------------------------------------------------------------------
// The bodies really are in the other file.
//
// Claim 2's two tests would also pass if the free functions did not exist at all
// and the forwarders called into nothing -- which does not link, but a test that
// depends on the linker to catch its own subject is not pinning anything.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// plan_server_native_v2 T4e: stock's "warn on out of order precache" is
// managed's (LatePrecache). Precache_Model hands over the name and the
// window's flag -- a read, not a decision -- and neither asks the engine
// whether the model is precached nor prints: that would be the rule back.
//-----------------------------------------------------------------------------
//=============================================================================//
// The scanner's own fixtures. Everything above is worthless if these fail.
//=============================================================================//

TEST( PrecacheModelMoveScanner, NameExtractorFindsWhatItShould )
{
	const std::set< std::string > got =
		CBaseEntityNamesIn( "x = CBaseEntity::Alpha(); CBaseEntity::Beta_2; CFoo::Gamma();" );

	EXPECT_EQ( (size_t)2, got.size() );
	EXPECT_EQ( (size_t)1, got.count( "Alpha" ) );
	EXPECT_EQ( (size_t)1, got.count( "Beta_2" ) );
	EXPECT_EQ( (size_t)0, got.count( "Gamma" ) );
}

//-----------------------------------------------------------------------------
// ...and the exact inventory of THIS file, which is the number that moves.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The body extractor's prefix collision, which is real here and was not
// hypothetical: "int CBaseEntity::PrecacheModel" is a prefix of nothing, but
// "void CBaseEntity::PrecacheModelComponents" and a hypothetical
// "PrecacheModel" needle differ only by what follows the name. The extractor
// requires '(' after the needle for exactly this reason.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// A CALL before the DEFINITION must not end the search. This is the defect the
// sibling scanner found in itself on its first run; inheriting the code without
// inheriting the fixture would be inheriting the bug back.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// A LIMIT of the extractor, asserted rather than glossed over: "not found" and
// "found, and the body is empty" both come back as the empty string.
//
// That is only tolerable because of where the empty string is used. The
// "definitions are here" test asserts NON-empty, so it is strengthened by the
// ambiguity, not weakened. (The "not also in baseentity.cpp" test, which
// asserted empty and so could be fooled by an empty duplicate, was culled with
// that file.)
//-----------------------------------------------------------------------------
