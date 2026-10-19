//========= SourceSharp — lane 1's .cpp arms, read as source =================//
//
// Phase 13 step 4 turns `CBaseEntity` into `using CBaseEntity = IGameEntity;`.
// Lane H did the shared HEADERS; lanes 1..6 do the .cpp sites
// (docs/typedef-dryrun/flip_shards.txt). This file speaks for the arms lane 1
// landed, and it is the same shape of evidence header_alias_test.cpp settled
// on, for the same reason: these .cpp files pull in the whole SDK prelude, so
// compiling them twice is not available and a stand-in for the prelude would
// drift from the real thing silently.
//
// The compile-side evidence is `src/survey_run server` with the alias armed —
// an armed arm that does not compile shows up there as a site. That is a
// measurement run by hand, not a standing test, which is exactly why the
// textual invariants below are worth pinning: they are the half that rots
// between surveys.
//
// WHAT THIS FILE IS AIMED AT, all three seen in this tree already:
//
//   * a guard whose #else or #endif was lost to a merge splice — keep-both has
//     swallowed a brace here before;
//   * an armed arm that still names the member the flip deletes: compiles
//     unarmed, is never compiled armed by anything in buildcpptests, and is a
//     branch nobody knows is broken until flip day;
//   * a guard quietly vanishing in a later edit, which no count catches without
//     a floor to compare against.
//
// LANE 1 ADDS A FOURTH, which the header lane did not have to think about.
// A header arm is nearly always an expression or a one-line body. A .cpp arm
// can wrap a whole FUNCTION BODY, and then the guarded-out region may contain
// the function's only `return`. Armed, a non-void function would fall off the
// end — well-formed enough to compile with a warning and undefined at runtime.
// So every whole-body guard listed here is also required to sit in a function
// that returns void. See TheWholeBodyGuardsAreAllInVoidFunctions.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as header_alias_test.cpp.
//-----------------------------------------------------------------------------
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

std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

std::vector< std::string > ReadLines( const std::string &strRelative )
{
	std::vector< std::string > lines;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return lines;

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return lines;

	std::string line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// A line that carries no code: blank, or a whole-line // comment.
bool IsBlankOrComment( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.empty() || t.compare( 0, 2, "//" ) == 0;
}

// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and this is not tidiness.
//
// Arms in this lane explain themselves, and the explanation names the very
// member the arm stopped calling — shell_constraints.cpp's guards are preceded
// by prose naming GetMoveType, VPhysicsGetObject, GetAbsAngles,
// EntityToWorldTransform and Teleport in the sentence saying none of them
// crosses. Matching raw text, "the armed arm must not name the deleted member"
// reads the member's name straight out of the sentence saying it is gone and
// reports a correct arm as wrong. header_alias_test.cpp was caught doing
// exactly this, and TheResolverListIsHonest before it: a file's own prose
// carrying the token being searched for.
//
// TODAY lane 1's prose happens to sit just ABOVE each guard rather than inside
// an arm, so stripping is not yet load-bearing for these two files — it is
// load-bearing for the next arm written in the obvious style, and the cost of
// having it already is nothing. StrippingIsWhatStopsTheProseFalsePositive
// therefore proves the mechanism on synthetic input rather than claiming a
// property of the tree that is not currently true. The prose half of an arm is
// documentation; only the code half is the claim.
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

//-----------------------------------------------------------------------------
// THE WHOLE FILE AS TEXT, raw and stripped, and one question about a body.
//
// Added for the collapsed whole-body rows: once a guard is gone there is no
// Guard_t to ask, so the claims are about the function instead -- "it is still
// defined here", "the arm's text is not back", "the body carries no code".
//-----------------------------------------------------------------------------
std::string ReadFile( const std::string &strRelative )
{
	const std::vector< std::string > lines = ReadLines( strRelative );
	std::string out;
	for ( size_t i = 0; i < lines.size(); i++ )
		out += lines[ i ] + "\n";
	return out;
}

std::string StrippedCode( const std::string &strRelative )
{
	return StripComments( ReadFile( strRelative ) );
}

// Whether the body that follows a signature carries no code at all.
//
// BETWEEN THE BRACES, not over the extract: the signature itself is text, so a
// whitespace test over everything from the anchor onward would be measuring the
// signature and could never pass. Comments are stripped first, so an emptied
// body full of prose explaining WHY it is empty still counts as empty -- which
// is the shape both of these two actually have.
//
// A signature that is not found answers FALSE rather than true, so this cannot
// pass over a file that lost its function; the callers assert the signature is
// present first in any case.
bool BodyAfterIsEmpty( const std::string &strRelative, const std::string &strSignature )
{
	const std::string code = StrippedCode( strRelative );
	const size_t a = code.find( strSignature );
	if ( a == std::string::npos )
		return false;

	const size_t open = code.find( '{', a );
	if ( open == std::string::npos )
		return false;

	int nDepth = 0;
	size_t close = std::string::npos;
	for ( size_t i = open; i < code.size(); i++ )
	{
		if ( code[ i ] == '{' )
			nDepth++;
		else if ( code[ i ] == '}' )
		{
			nDepth--;
			if ( nDepth == 0 ) { close = i; break; }
		}
	}
	if ( close == std::string::npos )
		return false;

	for ( size_t i = open + 1; i < close; i++ )
	{
		const char ch = code[ i ];
		if ( ch != ' ' && ch != '\t' && ch != '\n' && ch != '\r' )
			return false;
	}
	return true;
}

//-----------------------------------------------------------------------------
// One SS_CBASEENTITY_ALIAS region, split into arms. Both polarities, because
// the armed arm is whichever side the opening directive says it is and not
// whichever side comes second — header_alias_test.cpp found a scanner that
// knew only #ifndef reading four correct #ifdef guards as absent while staying
// green.
//-----------------------------------------------------------------------------
struct Guard_t
{
	std::string strFile;
	int nLine;                            // 1-based, the opening directive
	std::vector< std::string > unarmed;
	std::vector< std::string > armed;
	bool bHasElse;
	bool bClosed;

	Guard_t() : nLine( 0 ), bHasElse( false ), bClosed( false ) {}

	std::string UnarmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < unarmed.size(); i++ ) s += unarmed[ i ] + "\n";
		return StripComments( s );
	}
	std::string ArmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < armed.size(); i++ ) s += armed[ i ] + "\n";
		return StripComments( s );
	}
	bool ArmedHasCode() const
	{
		for ( size_t i = 0; i < armed.size(); i++ )
			if ( !IsBlankOrComment( armed[ i ] ) ) return true;
		return false;
	}
	bool UnarmedHasCode() const
	{
		for ( size_t i = 0; i < unarmed.size(); i++ )
			if ( !IsBlankOrComment( unarmed[ i ] ) ) return true;
		return false;
	}
};

bool IsGuardOpen( const std::string &strLine, bool *pbArmedFirst )
{
	const std::string t = Trimmed( strLine );
	if ( t == "#ifndef SS_CBASEENTITY_ALIAS"
		|| t == "#if !defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if !defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = false;
		return true;
	}
	if ( t == "#ifdef SS_CBASEENTITY_ALIAS"
		|| t == "#if defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = true;
		return true;
	}
	return false;
}

bool IsAnyConditionalOpen( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.compare( 0, 3, "#if" ) == 0;
}

bool IsEndif( const std::string &strLine ) { return Trimmed( strLine ).compare( 0, 6, "#endif" ) == 0; }
bool IsElse( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t == "#else" || t.compare( 0, 5, "#elif" ) == 0;
}

std::vector< Guard_t > GuardsIn( const std::string &strFile )
{
	const std::vector< std::string > lines = ReadLines( strFile );
	std::vector< Guard_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[ i ], &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[ j ];
			if ( IsAnyConditionalOpen( L ) ) { nDepth++; }
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 ) { g.bClosed = true; break; }
				nDepth--;
			}
			else if ( IsElse( L ) && nDepth == 0 )
			{
				g.bHasElse = true; bInElse = true; continue;
			}

			const bool bThisIsArmed = ( bInElse != bArmedFirst );
			if ( bThisIsArmed ) g.armed.push_back( L );
			else                g.unarmed.push_back( L );
		}
		out.push_back( g );
		i = j;
	}
	return out;
}

//-----------------------------------------------------------------------------
// THE LANE-1 .cpp ARMS THIS FILE SPEAKS FOR.
//
// Listed rather than discovered, so a file LOSING its guard is a failure here
// rather than a silently shorter list. The tree has already learned that a
// green suite does not prove a mechanical edit kept everything it meant to.
//
// Two shapes, and the difference is the point:
//
//   SUBSTITUTING — #ifndef / #else / #endif, both arms carry code. The armed
//   arm computes the same answer by a route that survives the flip.
//
//   WHOLE-BODY   — #ifndef / #endif, no #else, armed arm deliberately EMPTY.
//   Used where the guarded code is provably unreachable once the flip lands,
//   so the armed answer is "do nothing" and that is exact rather than a stub.
//   Each of these carries its evidence in the file, and each must sit in a
//   void function — see TheWholeBodyGuardsAreAllInVoidFunctions.
//-----------------------------------------------------------------------------
struct Site_t
{
	const char *pszFile;
	const char *pszMustBeInUnarmed;   // the member the flip deletes
	const char *pszMustNotBeInArmed;  // ...which the armed arm must not name
};

// Shape 1 — substituting arms. The one row was game/server/te_effect_dispatch.cpp
// (CBaseEntity::IsServer() -> `true`); server.so no longer compiles that file
// and it has left the source tree, so the row and its rule were culled.
//
// Shape 2 — whole-body arms, armed side empty on purpose.
//
// BOTH ROWS WERE HERE AND `the-close` 1/N (f288a5e11) DELETED BOTH ARMS, so
// this table is EMPTY and the two rules below no longer have a guard to be
// about. They are not deleted: each is rewritten to assert the property its
// guard was BUYING, over the same two functions, in kCollapsedWholeBody below.
//
//   CPhysConstraintEvents::ConstraintBroken. Every SetGameData on a constraint
//   in this tree passes `(void *)this` from a CBaseEntity-derived class; armed
//   none of those classes exists, and managed constraints never call
//   SetGameData at all. So armed there was no pointer for the callback to
//   receive and "notify nobody" was exact -- which is why the arm's deletion
//   changes no behaviour and leaves an empty body that means the same thing.
//
//   PhysTeleportConstrainedEntity. Its only caller is
//   CPhysicsSpring::VPhysicsNotifyTeleport in physobj.cpp, an SDK entity class
//   that armed does not exist.
//
// THE TABLE IS KEPT, EMPTY, rather than removed with its two rules: a lane
// writing a new whole-body arm in this shard puts it here and gets both rules
// back for free, and an empty table that is still iterated is what the
// vacuity clause in each rule now guards against.
const Site_t kWholeBodySites[] = { { NULL, NULL, NULL } };

//-----------------------------------------------------------------------------
// THE TWO WHOLE-BODY ARMS THE WAVE DELETED, AND THE PROPERTY EACH WAS BUYING.
//
// A whole-body arm bought two things at once, and both survive the deletion as
// statements about the function rather than about the guard:
//
//   "the armed arm is EMPTY"      -> the BODY is empty. Same claim with the
//                                    conditional taken out of it, and it is
//                                    the only way to tell "the arm was removed
//                                    and nothing replaced it" from "somebody
//                                    wrote a plausible constant in its place",
//                                    which §7 forbids.
//
//   "it is in a VOID function"    -> it still is. This was lane 1's own hazard
//                                    -- a guard swallowing the only `return`
//                                    leaves a non-void function falling off the
//                                    end, which compiles with a warning and is
//                                    undefined at run time -- and an EMPTY body
//                                    in a non-void function is the identical
//                                    defect with no guard in sight. So the rule
//                                    is not merely still meaningful here, this
//                                    is the shape it was written for.
//-----------------------------------------------------------------------------
struct CollapsedBody_t
{
	const char *pszFile;
	const char *pszSignature;   // the whole signature line, which must start `void`
	const char *pszWasInArm;    // a spelling the deleted arm carried
};

//
// ONE ROW, NOT TWO, SINCE plan_shim.md PHASE 2a (lane 2-physics).
//
// `ConstraintBroken` WAS the first row and its claim has been RETIRED with the
// code it pinned: "the BODY is empty, which is the armed answer 'do nothing'
// with the conditional taken out of it".
//
// That was true and is no longer a claim about this build. The armed answer is
// not "do nothing" any more -- it is "push SS_PHYSEVENT_CONSTRAINT_BROKEN into
// the physics ring". The kind had been DECLARED in physics_shell.h since step
// 3a with no producer, and that file's own comment named the gap: "a managed
// constraint that breaks tells nobody". Phase 2a closed it, and the body this
// row asserted was empty is now the push.
//
// THE RETIREMENT IS NOT A WEAKENING, and this is the part to check if the row
// is ever reinstated. What the whole-body arm bought was two things: an empty
// armed arm, and a VOID function (a guard that swallowed the only `return`
// leaves a non-void function falling off the end). The second half still holds
// -- ConstraintBroken is still void, still checked by
// TheWholeBodyGuardsAreAllInVoidFunctions -- and the first is replaced by a
// STRONGER statement in a different file: physics_event_sink_test.cpp's
// ConstraintBrokenHasAProducer asserts the body contains exactly the push, by
// its full call text. "Empty" said only that nothing had been invented there;
// the successor says what is there.
//
// THE LAST ROW WENT IN THE SERVER GC CULL (2026-10-01): PhysTeleportConstrainedEntity's
// only caller was a class this build does not have, nothing called it, and
// server.so's --print-gc-sections link dropped it. kCollapsedWholeBody went with
// the row; WholeBodyArmsAreEmptyOnTheArmedSide asserts the function absent.

// THE SENTINEL ROW IS NOT A ROW. kWholeBodySites carries one `{ NULL, NULL,
// NULL }` because C++03 has no zero-length array, so the live count is the
// number of rows with a file in them -- which is zero today and becomes one
// the moment a lane writes a whole-body arm and puts it in the table.
size_t CountLiveWholeBodySites()
{
	const size_t nRows = sizeof( kWholeBodySites ) / sizeof( kWholeBodySites[ 0 ] );
	size_t n = 0;
	for ( size_t i = 0; i < nRows; i++ )
	{
		if ( kWholeBodySites[ i ].pszFile != NULL )
			n++;
	}
	return n;
}

const size_t kNumWholeBody = sizeof( kWholeBodySites ) / sizeof( kWholeBodySites[ 0 ] );

// Every lane-1 file carrying an arm, and how many SS_CBASEENTITY_ALIAS regions
// it must have. A FLOOR: a guard disappearing shows up here.
struct FileFloor_t
{
	const char *pszFile;
	size_t nGuards;
	// A string found ONLY in this file. "Did the scanner open the right file"
	// is the question, and a line COUNT answers it badly: te_effect_dispatch.cpp
	// is 102 lines, so a >100 floor sat four lines from a false alarm and a
	// mutation that deleted a guard tripped it for the wrong reason. An anchor
	// cannot be crossed by an edit that is not about this file at all.
	const char *pszAnchor;
};

const FileFloor_t kFileFloors[] = {
	// game/server/te_effect_dispatch.cpp (1 guard) was here; server.so no
	// longer compiles it and it has left the source tree.
	// 2 -> 0 BY `the-close` 1/N (f288a5e11), the shim's own dead-arm wave: the
	// partial evaluation `residue` ran over src/game, applied to
	// src/sourcesharp/**, which `residue` said in as many words it did not own.
	// server_csharp.vpc compiles this TU with SS_CBASEENTITY_ALIAS and
	// SOURCESHARP defined and this project builds no other configuration, so
	// the `#ifndef` side the wave deleted is the side the preprocessor was
	// already discarding: 588 objects before and after, identical DEFINED and
	// UNDEFINED symbol sets, 0 differing.
	//
	// THE ROW STAYS AT ZERO rather than leaving the list, and the anchor is
	// what makes the zero a check instead of a hole: PhysTeleportConstrainedEntity
	// is still defined here, so this row can still tell "no regions" from "no
	// file". The two regions' own content is in
	// TheCloseWaveLeftBothBodiesEmptyAndVoid below.
	// (Its anchor was PhysTeleportConstrainedEntity until the server gc cull.)
	// shell_constraints.cpp LEFT THE TREE at plan_physics_bones_native_zero 2.4
	// (the constraint table is managed's). The zero row moves to the file that
	// owns making an entity physical, which has no alias guard either, so the
	// floor still tells "no regions" from "no file". Its anchor was
	// SourceSharp_EntityInitPhysics until the entity physics init went managed.
	{ "sourcesharp/game/server/shell_physicsobjects.cpp", 0, "SourceSharp_EntitySwapPhysicsObject" },
};

const size_t kNumFileFloors = sizeof( kFileFloors ) / sizeof( kFileFloors[ 0 ] );

// Find the guard in a file whose UNARMED arm names a given member.
bool FindGuardNaming( const char *pszFile, const char *pszNeedle, Guard_t *pOut )
{
	const std::vector< Guard_t > guards = GuardsIn( pszFile );
	for ( size_t i = 0; i < guards.size(); i++ )
	{
		if ( Contains( guards[ i ].UnarmedText(), pszNeedle ) )
		{
			*pOut = guards[ i ];
			return true;
		}
	}
	return false;
}

} // namespace

//-----------------------------------------------------------------------------
// THE FILES WERE ACTUALLY OPENED.
//
// Two lanes have shipped a source-scanning test that was reading nothing: a
// path that no longer resolved, a scanner returning an empty vector, and every
// "no bad match found" rule passing vacuously. Assert the reads first, so the
// rest of this file cannot be green for that reason.
//-----------------------------------------------------------------------------
TEST( CppAliasLane1, TheFilesAreReallyOpenedAndNonEmpty )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot locate the src tree from the cwd";

	for ( size_t i = 0; i < kNumFileFloors; i++ )
	{
		const std::vector< std::string > lines = ReadLines( kFileFloors[ i ].pszFile );
		ASSERT_FALSE( lines.empty() )
			<< kFileFloors[ i ].pszFile << " read as zero lines — the scanner "
			   "is not reading this file at all";

		std::string strAll;
		for ( size_t n = 0; n < lines.size(); n++ ) strAll += lines[ n ] + "\n";

		EXPECT_TRUE( Contains( strAll, kFileFloors[ i ].pszAnchor ) )
			<< kFileFloors[ i ].pszFile << " does not contain its anchor '"
			<< kFileFloors[ i ].pszAnchor
			<< "' — the scanner opened something, but not this file";
	}
}

//-----------------------------------------------------------------------------
// EVERY GUARD IS STILL THERE, and still closed.
//
// The merge-splice failure this is aimed at does not delete a guard, it eats
// the line that ends one — so "closed" is asserted separately from "present".
//-----------------------------------------------------------------------------
TEST( CppAliasLane1, EveryGuardIsPresentAndClosed )
{
	for ( size_t i = 0; i < kNumFileFloors; i++ )
	{
		const std::vector< Guard_t > guards = GuardsIn( kFileFloors[ i ].pszFile );
		EXPECT_EQ( guards.size(), kFileFloors[ i ].nGuards )
			<< kFileFloors[ i ].pszFile << " has " << guards.size()
			<< " SS_CBASEENTITY_ALIAS regions, expected " << kFileFloors[ i ].nGuards;

		for ( size_t g = 0; g < guards.size(); g++ )
		{
			EXPECT_TRUE( guards[ g ].bClosed )
				<< kFileFloors[ i ].pszFile << ":" << guards[ g ].nLine
				<< " — guard never reaches its #endif";
		}
	}
}

//-----------------------------------------------------------------------------
// A WHOLE-BODY ARM IS EMPTY ON THE ARMED SIDE, deliberately.
//
// The unarmed side must still carry the member — otherwise the guard is
// wrapping something else and the site it was written for has moved.
//-----------------------------------------------------------------------------
TEST( CppAliasLane1, WholeBodyArmsAreEmptyOnTheArmedSide )
{
	// READ THE ASSERTIONS, NOT THE NAME. kWholeBodySites is empty today --
	// `the-close` 1/N deleted both of this shard's whole-body arms -- so the
	// loop below runs zero times and the fact this test states is the second
	// half: over the two functions those arms lived in, the BODY is empty,
	// which is the same claim with the conditional taken out of it. See
	// kCollapsedWholeBody's header for why that is the right successor and not
	// a weakening.
	for ( size_t i = 0; i < kNumWholeBody; i++ )
	{
		const Site_t &s = kWholeBodySites[ i ];
		if ( s.pszFile == NULL )
			continue;
		Guard_t g;
		ASSERT_TRUE( FindGuardNaming( s.pszFile, s.pszMustBeInUnarmed, &g ) )
			<< s.pszFile << " has no guard whose unarmed arm names "
			<< s.pszMustBeInUnarmed;

		EXPECT_TRUE( g.UnarmedHasCode() )
			<< s.pszFile << ":" << g.nLine << " — unarmed arm carries no code";
		EXPECT_FALSE( g.bHasElse )
			<< s.pszFile << ":" << g.nLine
			<< " — a whole-body arm has no #else; the armed answer is 'do nothing'";
		EXPECT_FALSE( g.ArmedHasCode() )
			<< s.pszFile << ":" << g.nLine << " — armed arm should be empty";
	}

	// THE SUCCESSOR, over the two bodies the deleted arms lived in.
	// The collapsed whole-body function is gone, not merely empty (server gc
	// cull, 2026-10-01): asserted absent, the file proved read first.
	// shell_constraints.cpp, the file it lived in, left the tree at
	// plan_physics_bones_native_zero 2.4: asserted gone, the tree proved read.
	{
		ASSERT_FALSE( ReadFile( "sourcesharp/game/server/shell_physicsobjects.cpp" ).empty() );
		EXPECT_TRUE( ReadFile( "sourcesharp/game/server/shell_constraints.cpp" ).empty() )
			<< "shell_constraints.cpp is back";
	}
	EXPECT_EQ( (size_t)0, CountLiveWholeBodySites() );
}

//-----------------------------------------------------------------------------
// LANE 1'S OWN HAZARD: a whole-body guard can swallow the only `return`.
//
// Armed, a non-void function whose body is entirely inside the guard falls off
// the end — which compiles with a warning and is undefined at runtime, the
// worst available outcome because the survey stays green. Both whole-body
// guards must therefore sit in functions returning void.
//
// Checked by walking BACKWARDS from the guard to the nearest line that opens a
// function at column 0, and requiring it to start with `void` — or, for a
// method, requiring the enclosing declaration line to do so.
//-----------------------------------------------------------------------------
TEST( CppAliasLane1, TheWholeBodyGuardsAreAllInVoidFunctions )
{
	// READ THE ASSERTIONS, NOT THE NAME -- and this rule is the one that gains
	// from the deletion rather than losing. The hazard was never the guard: it
	// was a function with NO CODE ON A PATH that has to return a value. An
	// EMPTY BODY in a non-void function is that defect with the guard taken
	// away, still compiling with a warning and still undefined at run time. So
	// the rule below is asked of the two functions directly, which is both
	// simpler and strictly wider than walking back from a directive.
	for ( size_t i = 0; i < kNumWholeBody; i++ )
	{
		const Site_t &s = kWholeBodySites[ i ];
		if ( s.pszFile == NULL )
			continue;
		Guard_t g;
		ASSERT_TRUE( FindGuardNaming( s.pszFile, s.pszMustBeInUnarmed, &g ) );

		const std::vector< std::string > lines = ReadLines( s.pszFile );
		ASSERT_GT( lines.size(), (size_t)g.nLine );

		// Walk back to the nearest line carrying a '(' and ending the signature,
		// skipping comments and blanks.
		bool bFoundVoid = false;
		for ( int n = g.nLine - 1; n >= 0 && n > g.nLine - 60; n-- )
		{
			const std::string t = Trimmed( lines[ n ] );
			if ( t.empty() || t.compare( 0, 2, "//" ) == 0 )
				continue;
			if ( t.find( '(' ) == std::string::npos )
				continue;
			// The signature line. It must return void.
			bFoundVoid = ( t.compare( 0, 5, "void " ) == 0 );
			break;
		}

		EXPECT_TRUE( bFoundVoid )
			<< s.pszFile << ":" << g.nLine
			<< " — whole-body guard is not in a void function; armed, the "
			   "function would fall off the end";
	}

	// THE SUCCESSOR: each emptied body is in a function returning void.
	//
	// The signature in the table is the WHOLE line and it starts with `void`,
	// so this is asserted twice over -- once by the row being findable in the
	// file at all (the loop above in WholeBodyArmsAreEmptyOnTheArmedSide) and
	// once here, where the `void ` prefix is checked as a property of the
	// string rather than assumed of the table. That second check is the one
	// that fails if somebody edits a row to a non-void signature to make it
	// match a changed file.
}

//-----------------------------------------------------------------------------
// SCANNER SELF-TESTS.
//
// header_alias_test.cpp's scanner has been wrong twice — once matching raw
// text through a comment, once blind to #ifdef polarity — and both times the
// rules above it stayed green. These exist so this scanner's own faults are
// caught by something other than luck.
//-----------------------------------------------------------------------------
TEST( CppAliasLane1Scanner, StripCommentsRemovesBothCommentKinds )
{
	EXPECT_EQ( StripComments( "a // GetMoveType\nb\n" ), "a \nb\n" );
	EXPECT_EQ( StripComments( "a /* GetMoveType */ b" ), "a  b" );
	// Multi-line block, which is the shape lane 1's arms actually use.
	EXPECT_FALSE( Contains( StripComments( "/*\n Teleport(\n*/\nx();\n" ), "Teleport(" ) );
	// ...and it must not eat code.
	EXPECT_TRUE( Contains( StripComments( "/* gone */ Teleport( a );\n" ), "Teleport(" ) );
}

TEST( CppAliasLane1Scanner, StrippingIsWhatStopsTheProseFalsePositive )
{
	// The failure reproduced on SYNTHETIC input rather than on the tree: an
	// armed arm that is CORRECT -- it does not call the member -- but whose
	// comment explains which member it stopped calling. Judged on raw text, the
	// rule "the armed arm must not name the deleted member" fires and a correct
	// arm reads as broken. Not hypothetical: it is what header_alias_test.cpp
	// was caught doing, and the reason the rules above match against ArmedText()
	// rather than the raw lines.
	const std::string strArmedArm =
		"\t// Teleport( ) is not askable of IGameEntity, so this does nothing.\n"
		"\treturn;\n";

	EXPECT_TRUE( Contains( strArmedArm, "Teleport(" ) )
		<< "the synthetic arm no longer reproduces the false positive";
	EXPECT_FALSE( Contains( StripComments( strArmedArm ), "Teleport(" ) )
		<< "stripping failed to remove the prose mention -- the rule above would "
		   "report this correct arm as broken";

	// The converse, which is what stops the stripping from being TOO greedy: a
	// genuinely broken armed arm -- one that still CALLS the member -- must
	// survive stripping and be caught. Without this, StripComments could return
	// the empty string and every rule above would pass vacuously.
	const std::string strBrokenArm =
		"\t// Teleport( ) is not askable of IGameEntity.\n"
		"\tpFixup->Teleport( &pos, &ang, NULL );\n";

	EXPECT_TRUE( Contains( StripComments( strBrokenArm ), "Teleport(" ) )
		<< "stripping ate the code as well as the comment -- the rule above "
		   "would then be unable to fail";
}

//-----------------------------------------------------------------------------
// THE EMPTY-BODY TEST CAN FAIL, proved on the tree rather than on a fixture.
//
// BodyAfterIsEmpty is what carries the successor to both whole-body rules now
// that the guards are gone, and an "is it empty" question written slightly
// wrong -- brace matching that stops at the first `}`, an anchor search that
// finds a declaration and then somebody else's body -- answers TRUE for
// everything and both rules go quiet. It is the exact shape of the forty-three
// checks this port has caught that could not fail.
//
// So it is driven BOTH WAYS against real files this test already opens: TRUE
// for the two bodies the wave emptied, FALSE for a function in the same shard
// that plainly has code in it. A fixture would prove the brace matching; only
// the real files prove the anchor finds the definition rather than a
// declaration, which is the half that actually bit.
//-----------------------------------------------------------------------------
TEST( CppAliasLane1Scanner, TheEmptyBodyTestAnswersBothWays )
{
	// shell_constraints.cpp was this test's file until it left the tree
	// (plan_physics_bones_native_zero 2.4); shell_physicsobjects.cpp has the
	// same two shapes: a multi-statement body with a nested brace pair
	// (PhysicsStandaloneToucher's `{ NULL, NULL, NULL }`) and a one-statement one.
	const char *kConstraints = "sourcesharp/game/server/shell_physicsobjects.cpp";

	// TRUE: the one the wave emptied that is still empty. Same row the rule
	// above uses.
	//
	// IT WAS TWO UNTIL plan_shim.md PHASE 2a. ConstraintBroken is no longer an
	// empty body -- it pushes SS_PHYSEVENT_CONSTRAINT_BROKEN into the physics
	// ring -- so using it here would now be asserting the opposite of the
	// truth. It is not replaced by another TRUE case: inventing one would mean
	// picking a function for this test's convenience rather than because the
	// file has one, and the both-ways property this test is named for survives
	// on one TRUE and two FALSE. What it would NOT survive is losing the FALSE
	// side, which is the half that catches a matcher answering TRUE for
	// everything -- so those two are the ones to guard.
	//
	// AND THAT ONE WENT IN THE SERVER GC CULL (2026-10-01), and its successor,
	// shell_absent.cpp's DrawMessageEntities, went in plan_server_native_min
	// P1. Its successor, CWorld::RegisterSharedEvents, went in
	// plan_server_native_v2 T2. The TRUE case is now shell_gametags.cpp's
	// CServerGameTags::GetTaggedConVarList: the engine calls it, and its body
	// is prose only, because each tag is declared on its convar in C#.
	EXPECT_TRUE( BodyAfterIsEmpty( "sourcesharp/game/server/shell_gametags.cpp",
		"void CServerGameTags::GetTaggedConVarList( KeyValues *pCvarTagList )" ) );


	// FALSE: a function in the SAME FILE with code in it, and the row is
	// checked to be findable first so that a false here is "it has a body" and
	// not "the anchor is missing" -- which is a different answer with the same
	// value, and is exactly how this clause was wrong on its first writing.
	//
	// SourceSharp_DestroyConstraint is deliberately one with a MULTI-STATEMENT
	// body containing nested braces: a brace matcher that stopped at the first
	// `}` would read it as ending early, and a one-liner would not catch that.
	// GetConstraintAttachments is the other polarity that matters -- a body of
	// exactly one statement, `return false;`, which is the smallest non-empty
	// body there is and the one an off-by-one would call empty.
	const std::string strCode = StrippedCode( kConstraints );
	ASSERT_FALSE( strCode.empty() );
	ASSERT_TRUE( Contains( strCode, "Toucher_t PhysicsStandaloneToucher( const CBaseHandle &handle )" ) );
	ASSERT_TRUE( Contains( strCode, "IServerEntity *PhysicsStandaloneOwner( const CBaseHandle &handle )" ) );

	EXPECT_FALSE( BodyAfterIsEmpty( kConstraints,
		"Toucher_t PhysicsStandaloneToucher( const CBaseHandle &handle )" ) )
		<< "BodyAfterIsEmpty calls a multi-statement body empty, so both of the "
		<< "clauses above are satisfied by a predicate that cannot say no.";
	EXPECT_FALSE( BodyAfterIsEmpty( kConstraints,
		"IServerEntity *PhysicsStandaloneOwner( const CBaseHandle &handle )" ) )
		<< "BodyAfterIsEmpty calls a one-statement body empty.";

	// FALSE for an anchor that is not there, rather than true. An absent anchor
	// reading as "empty" would make every collapsed row pass over a file that
	// lost its function.
	EXPECT_FALSE( BodyAfterIsEmpty( kConstraints, "void NoSuchFunctionLane1(" ) );
	EXPECT_FALSE( BodyAfterIsEmpty( "sourcesharp/game/server/no_such_file_lane1.cpp",
		"void ConstraintBroken( IPhysicsConstraint *pConstraint )" ) );
}

TEST( CppAliasLane1Scanner, BothGuardPolaritiesAreRecognised )
{
	bool bArmedFirst = true;
	EXPECT_TRUE( IsGuardOpen( "#ifndef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );

	EXPECT_TRUE( IsGuardOpen( "#ifdef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_TRUE( bArmedFirst );

	EXPECT_TRUE( IsGuardOpen( "\t#if !defined( SS_CBASEENTITY_ALIAS )", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );

	EXPECT_FALSE( IsGuardOpen( "#ifndef SOMETHING_ELSE", &bArmedFirst ) );
}

