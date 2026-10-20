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
const char *SourceRoot()
{
	static const char *kCandidates[] = { "game", "src/game", "../src/game" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

// pszDir is "/server/" for a Valve path, or "/" for one of ours reached as
// "../sourcesharp/...": a fresh farm has no game/server directory (no server
// file is a fork link since plan_server_native_v2 D3), and a path cannot pass
// through a directory that does not exist.
std::string ReadUnder( const char *pszDir, const char *pszLeaf )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += pszDir;
	strPath += pszLeaf;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

// "server/../x" is resolved by hand to "x", which names the same file without
// needing game/server to exist.
std::string ReadServerFile( const char *pszLeaf )
{
	if ( strncmp( pszLeaf, "../", 3 ) == 0 )
		return ReadUnder( "/", pszLeaf + 3 );
	return ReadUnder( "/server/", pszLeaf );
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos; i = hay.find( needle, i + needle.size() ) )
		++n;
	return n;
}

const std::string &PrecacheModelSource()
{
	static const std::string s = ReadServerFile( "../../sourcesharp/game/server/precache_model.cpp" );
	return s;
}

// SOURCESHARP: the two forwarders MOVED into the class body in baseentity.h,
// and a BaseEntityHeader() accessor here read them there. game/server/
// baseentity.h is compiled by nothing since plan_server_novalve.md P5 and left
// the farm in P6; Claim 2 now asserts its absence instead (see below).

// Whether game/server/<leaf> can be read at all. Claim 2's absence is asked of
// this, and it is anchored on a sibling the farm still links so a reader that
// sees nothing cannot satisfy it.
bool ServerFileReadable( const char *pszLeaf )
{
	return !ReadServerFile( pszLeaf ).empty();
}

// SOURCESHARP, the-close: where the two names live NOW. The class body in
// baseentity.h that held the forwarders was inside the dead
// `#ifndef SS_CBASEENTITY_ALIAS` arm and went with the other 452; baseentity.h
// includes this header instead, which is how the unqualified call sites still
// resolve.
const std::string &PrecacheModelHeader()
{
	static const std::string s = ReadServerFile( "../../sourcesharp/game/server/precache_model.h" );
	return s;
}

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
std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	for ( size_t i = 0; i < s.size(); )
	{
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '/' )
		{
			while ( i < s.size() && s[ i ] != '\n' )
				i++;
			continue;
		}
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < s.size() && !( s[ i ] == '*' && s[ i + 1 ] == '/' ) )
				i++;
			i = ( i + 1 < s.size() ) ? i + 2 : s.size();
			continue;
		}
		out += s[ i ];
		i++;
	}
	return out;
}

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

std::string Squeezed( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\r' && c != '\n' )
			out += c;
	}
	return out;
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
std::string BodyOf( const std::string &strSource, const char *pszQualifiedName )
{
	const std::string strNeedle( pszQualifiedName );

	size_t at = 0;
	for ( ;; )
	{
		at = strSource.find( strNeedle, at );
		if ( at == std::string::npos )
			return std::string();

		const size_t after = at + strNeedle.size();
		at = after;

		// The next non-space character must open the argument list, or the
		// needle matched a longer name that merely starts the same way --
		// PrecacheModel against PrecacheModelComponents, which is exactly the
		// collision this file has to survive.
		size_t p = after;
		while ( p < strSource.size() && ( strSource[ p ] == ' ' || strSource[ p ] == '\t' ) )
			p++;
		if ( p >= strSource.size() || strSource[ p ] != '(' )
			continue;

		const size_t open = strSource.find( '{', p );
		if ( open == std::string::npos )
			return std::string();

		const size_t semi = strSource.find( ';', p );
		if ( semi != std::string::npos && semi < open )
			continue; // a call, or a declaration

		int depth = 0;
		size_t i = open;
		for ( ; i < strSource.size(); i++ )
		{
			if ( strSource[ i ] == '{' ) depth++;
			else if ( strSource[ i ] == '}' )
			{
				depth--;
				if ( depth == 0 )
					break;
			}
		}
		if ( depth != 0 )
			return std::string();

		return Squeezed( strSource.substr( open + 1, i - open - 1 ) );
	}
}

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
TEST( PrecacheModelMove, NamesOnlyTheTwoStaticsDefinedOutsideBaseEntityObject )
{
	const std::string strCode = StripComments( PrecacheModelSource() );
	const std::set< std::string > names = CBaseEntityNamesIn( strCode );

	std::set< std::string > allowed;
	allowed.insert( "IsPrecacheAllowed" );
	allowed.insert( "PrecacheScriptSound" );

	for ( std::set< std::string >::const_iterator it = names.begin(); it != names.end(); ++it )
	{
		EXPECT_TRUE( allowed.count( *it ) != 0 )
			<< "precache_model.cpp reaches CBaseEntity::" << *it
			<< ", which is not one of the two statics defined outside baseentity.o. "
			<< "If that name IS defined in baseentity.o, this file has just rejoined "
			<< "the consumer set and shell_server.o and shell_standalone_base.o with it. "
			<< "Re-measure with: src/evict_consumers debug server baseentity.o";
	}
}

//-----------------------------------------------------------------------------
// No instance-level use of an entity, at all.
//
// The statics above are reached through the class name. A '->' or a '.' on an
// entity would be a different thing entirely: it would mean this file had
// acquired a CBaseEntity to talk to, which is precisely what moving it out of
// baseentity.cpp was for. The cheap mechanical proxy is that the file declares
// no CBaseEntity pointer or reference of any kind.
//-----------------------------------------------------------------------------
TEST( PrecacheModelMove, DeclaresNoEntityPointerOrReference )
{
	const std::string strCode = StripComments( PrecacheModelSource() );

	EXPECT_EQ( std::string::npos, strCode.find( "CBaseEntity *" ) )
		<< "precache_model.cpp declares a CBaseEntity pointer. Nothing here has an "
		<< "entity to point at, and acquiring one is how the move gets undone.";
	EXPECT_EQ( std::string::npos, strCode.find( "CBaseEntity&" ) )
		<< "precache_model.cpp declares a CBaseEntity reference.";
	EXPECT_EQ( std::string::npos, strCode.find( "CBaseEntity &" ) )
		<< "precache_model.cpp declares a CBaseEntity reference.";

	// CBaseEntity is not a name any anymore -- the alias header is deleted and
	// the entity type is spelled IGameEntity -- so the three needles above can
	// no longer be satisfied by new code, only by a revert. The same three in
	// the live spelling, so the fact still has something to catch.
	EXPECT_EQ( std::string::npos, strCode.find( "IGameEntity *" ) )
		<< "precache_model.cpp declares an entity pointer. Nothing here has an "
		<< "entity to point at, and acquiring one is how the move gets undone.";
	EXPECT_EQ( std::string::npos, strCode.find( "IGameEntity&" ) )
		<< "precache_model.cpp declares an entity reference.";
	EXPECT_EQ( std::string::npos, strCode.find( "IGameEntity &" ) )
		<< "precache_model.cpp declares an entity reference.";
}

//=============================================================================//
// Claim 2 -- what stayed behind is a forwarder and nothing else.
//=============================================================================//

// SOURCESHARP: THE FORWARDERS MOVED FROM baseentity.cpp INTO THE CLASS BODY IN
// baseentity.h, and these two tests followed them there.
//
// What this file pins is UNCHANGED and is still the whole point: each member is
// a bare forward onto the free function and holds no second implementation of a
// precache. The assertion is the same string it always was, because the body is
// the same body.
//
// WHY THE MOVE. plan_baseentity.md Phase 13 step 3 DELETES baseentity.cpp,
// because under `using CBaseEntity = IGameEntity;` an alias is not a class scope
// and `int CBaseEntity::PrecacheModel(...)` is a compile error rather than a
// definition. A forwarder that lives in the .cpp therefore has to be dealt with
// again later; one that lives in the class body is deleted by the same edit
// that deletes the class body, and in the meantime emits no symbol at all --
// two members off baseentity.o for no new free function. That is strictly
// better than a quarantine .cpp and needs no server_csharp.vpc edit.
//
// The counterpart assertion -- that neither is defined out of line in
// baseentity.cpp any more -- was culled when baseentity.cpp stopped being
// compiled and left the source tree.
// READ THE ASSERTIONS, NOT THE NAME. There is no member and no forwarder any
// more. `class CBaseEntity` in baseentity.h lived inside
// `#ifndef SS_CBASEENTITY_ALIAS` -- an arm the compiler never saw, because
// CBaseEntity WAS IGameEntity -- so the forwarder these two facts read was dead
// source, and the arm was deleted with the other 452. A fact reading it was a
// check that could not fail (R2).
//
// The claim SURVIVES, and is the same claim: there is exactly ONE precache
// implementation and the unqualified call sites reach it. What used to make
// that true was "the member is a bare forward"; what makes it true now is
// "there is no member, and baseentity.h includes the free function's header".
// Both halves are asserted, because "the member is gone" alone also passes over
// an empty header.
TEST( PrecacheModelMove, PrecacheModelMemberIsAPureForwarder )
{
	// P6: the header the member and the forwarder lived in is out of the farm.
	// That is the stronger form of "baseentity.h names PrecacheModel no more",
	// so the absence is the FILE's now, anchored on a sibling that IS read. The
	// other baseentity.h half -- that it includes precache_model.h -- is
	// retired: game/server/baseentity.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6.
	const std::string strDecls  = StripComments( PrecacheModelHeader() );
	ASSERT_NE( std::string::npos, strDecls.find( "PRECACHE_MODEL_H" ) )
		<< "precache_model.h did not load";
	ASSERT_TRUE( ServerFileReadable( "../shared/shareddefs.h" ) )
		<< "game/shared/shareddefs.h did not load, so the absence below would be "
		   "answered by a reader that sees nothing";

	// Absence half: no member, forwarder or otherwise -- no header to hold one.
	EXPECT_FALSE( ServerFileReadable( "baseentity.h" ) )
		<< "game/server/baseentity.h is readable from the farm again. It is where "
		   "the PrecacheModel member lived; two implementations of a precache is "
		   "the failure this pins: the SDK's 734 unqualified call sites would "
		   "take one and the port's own files the other.";

	// Live-successor half: the one declaration the call sites resolve to.
	EXPECT_EQ( 1u, CountOf( strDecls, "int Precache_Model( const char *name, bool bPreload = true );" ) )
		<< "precache_model.h no longer declares the free function the call "
		   "sites now resolve to.";
}

// READ THE ASSERTIONS, NOT THE NAME -- see the block above; same deletion, same
// two halves, the components entry point.
TEST( PrecacheModelMove, PrecacheModelComponentsMemberIsAPureForwarder )
{
	const std::string strDecls  = StripComments( PrecacheModelHeader() );
	ASSERT_NE( std::string::npos, strDecls.find( "PRECACHE_MODEL_H" ) )
		<< "precache_model.h did not load";
	ASSERT_TRUE( ServerFileReadable( "../shared/shareddefs.h" ) )
		<< "game/shared/shareddefs.h did not load";

	EXPECT_FALSE( ServerFileReadable( "baseentity.h" ) )
		<< "game/server/baseentity.h, which held the PrecacheModelComponents "
		   "member, is readable from the farm again.";
	EXPECT_EQ( 1u, CountOf( strDecls, "void Precache_ModelComponents( int nModelIndex );" ) )
		<< "precache_model.h no longer declares the component walk.";
}

//-----------------------------------------------------------------------------
// The bodies really are in the other file.
//
// Claim 2's two tests would also pass if the free functions did not exist at all
// and the forwarders called into nothing -- which does not link, but a test that
// depends on the linker to catch its own subject is not pinning anything.
//-----------------------------------------------------------------------------
TEST( PrecacheModelMove, TheFreeFunctionsAreDefinedInPrecacheModelCpp )
{
	const std::string strCode = StripComments( PrecacheModelSource() );

	EXPECT_NE( std::string( "" ), BodyOf( strCode, "int Precache_Model" ) )
		<< "Precache_Model has no definition in precache_model.cpp.";
	EXPECT_NE( std::string( "" ), BodyOf( strCode, "void Precache_ModelComponents" ) )
		<< "Precache_ModelComponents has no definition in precache_model.cpp.";
}

//-----------------------------------------------------------------------------
// plan_server_native_v2 T4e: stock's "warn on out of order precache" is
// managed's (LatePrecache). Precache_Model hands over the name and the
// window's flag -- a read, not a decision -- and neither asks the engine
// whether the model is precached nor prints: that would be the rule back.
//-----------------------------------------------------------------------------
TEST( PrecacheModelMove, TheLatePrecacheRuleIsManagedAndNativeHandsItTheFlag )
{
	// Squeezed: BodyOf strips every space, so the needles carry none.
	const std::string strBody = BodyOf( StripComments( PrecacheModelSource() ), "int Precache_Model" );
	ASSERT_NE( std::string( "" ), strBody ) << "Precache_Model has no definition";

	EXPECT_EQ( 1u, CountOf( strBody, "->ModelPrecacheCheck(name,EntityPrecache_IsAllowed());" ) )
		<< "Precache_Model does not hand managed the name and the window's flag";
	EXPECT_EQ( 0u, CountOf( strBody, "IsModelPrecached" ) )
		<< "Precache_Model asks the engine whether the model is precached: the rule is native again";
	EXPECT_EQ( 0u, CountOf( strBody, "DevMsg" ) )
		<< "Precache_Model prints the late-precache warning itself";
	EXPECT_EQ( 1u, CountOf( strBody, "engine->PrecacheModel(name,bPreload)" ) )
		<< "the control: the precache itself is still made here, read by the same body";
}

//=============================================================================//
// The scanner's own fixtures. Everything above is worthless if these fail.
//=============================================================================//

TEST( PrecacheModelMoveScanner, ThePrecacheModelFileWasReallyOpenedAndIsNotTrivial )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no game/ directory found from the working directory -- the scanner read "
		<< "nothing and every claim above passed vacuously";

	// 3000, from 8000, in plan_server_novalve.md P3: the model sounds cache
	// left the file (C#'s ModelComponents builds the list); ~5,000 bytes remain.
	EXPECT_GT( PrecacheModelSource().size(), (size_t)3000 )
		<< "precache_model.cpp came back too small to be the real file";
}

TEST( PrecacheModelMoveScanner, StripCommentsRemovesBothCommentForms )
{
	EXPECT_EQ( std::string( "a\nb" ), StripComments( "a// CBaseEntity::Nope\nb" ) );
	EXPECT_EQ( std::string( "ab" ), StripComments( "a/* CBaseEntity::Nope */b" ) );
	EXPECT_EQ( std::string( "a/b" ), StripComments( "a/b" ) );
}

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
TEST( PrecacheModelMove, ExtractorSeesExactlyWhatThisFileNames )
{
	const std::set< std::string > names =
		CBaseEntityNamesIn( StripComments( PrecacheModelSource() ) );

	EXPECT_EQ( (size_t)0, names.size() )
		<< "precache_model.cpp names " << names.size() << " CBaseEntity:: things. "
		<< "It must name NONE. It was two; IsPrecacheAllowed went when the shared "
		<< "quarantine was retired and PrecacheScriptSound went with the sound "
		<< "quarantine, so this file no longer reaches the class at all. ANY "
		<< "number here is a new class-scope reach, and the allowed-name "
		<< "assertion above is what argues about whether it may stay.";
	EXPECT_EQ( (size_t)0, names.count( "IsPrecacheAllowed" ) )
		<< "precache_model.cpp reaches CBaseEntity::IsPrecacheAllowed again. "
		<< "Nothing defines the class-scope name in this build.";
	EXPECT_EQ( (size_t)0, names.count( "PrecacheScriptSound" ) )
		<< "precache_model.cpp reaches CBaseEntity::PrecacheScriptSound again. "
		<< "Nothing defines the class-scope name in this build.";

	// The positive half, so a zero above cannot be satisfied by a file that
	// lost its precache calls: it reaches BOTH free functions instead.
	const std::string strCode = StripComments( PrecacheModelSource() );
	EXPECT_NE( std::string::npos, strCode.find( "EntityPrecache_IsAllowed" ) )
		<< "precache_model.cpp no longer asks whether precaching is allowed.";
	// The script-sound precache LEFT this file in plan_server_novalve.md P3:
	// a model's sounds are C#'s (ModelComponents), and the call that stays is
	// the one that hands them over.
	EXPECT_NE( std::string::npos, strCode.find( "pArea->ModelPrecacheComponents(" ) )
		<< "precache_model.cpp no longer hands a model's components to C#.";
}

//-----------------------------------------------------------------------------
// The body extractor's prefix collision, which is real here and was not
// hypothetical: "int CBaseEntity::PrecacheModel" is a prefix of nothing, but
// "void CBaseEntity::PrecacheModelComponents" and a hypothetical
// "PrecacheModel" needle differ only by what follows the name. The extractor
// requires '(' after the needle for exactly this reason.
//-----------------------------------------------------------------------------
TEST( PrecacheModelMoveScanner, BodyExtractorRequiresAnArgumentListAfterTheName )
{
	const std::string strFixture =
		"void Foo::PrecacheModelComponents( int n ) { components(); }\n"
		"int Foo::PrecacheModel( const char *p ) { return model(); }\n";

	EXPECT_EQ( std::string( "returnmodel();" ), BodyOf( strFixture, "int Foo::PrecacheModel" ) );
	EXPECT_EQ( std::string( "components();" ),
	           BodyOf( strFixture, "void Foo::PrecacheModelComponents" ) );
}

//-----------------------------------------------------------------------------
// A CALL before the DEFINITION must not end the search. This is the defect the
// sibling scanner found in itself on its first run; inheriting the code without
// inheriting the fixture would be inheriting the bug back.
//-----------------------------------------------------------------------------
TEST( PrecacheModelMoveScanner, BodyExtractorWalksPastCallsAndDeclarations )
{
	const std::string strFixture =
		"int Foo::Bar( int );\n"          // declaration
		"void other() { Foo::Bar( 1 ); }\n" // call
		"int Foo::Bar( int n ) { return n; }\n";

	EXPECT_EQ( std::string( "returnn;" ), BodyOf( strFixture, "int Foo::Bar" ) );
}

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
TEST( PrecacheModelMoveScanner, BodyExtractorConflatesNotFoundWithAnEmptyBody )
{
	EXPECT_EQ( std::string( "" ), BodyOf( "int Other::Thing() { return 1; }", "int Foo::Bar" ) );
	EXPECT_EQ( std::string( "" ), BodyOf( "int Foo::Bar() {}", "int Foo::Bar" ) );
}
