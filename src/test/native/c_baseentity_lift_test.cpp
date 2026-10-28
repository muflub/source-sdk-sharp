//========= SourceSharp — c_baseentity.cpp's twelve lifted definitions =======//
//
// A SOURCE SCANNER, like baseentity_shared_lift_test, physicslink_lift_test,
// soundemitter_lift_test and baseentity_statics_test, and for the same reason:
// the claim is about the TEXT of three files that need a whole engine to run,
// so the text is what is read. buildcpptests links each test binary against
// gtest and nothing else.
//
// WHAT WAS DONE. plan_baseentity.md Phase 13 step 3 is the set of files that
// DEFINE CBaseEntity members out of line; under `using CBaseEntity =
// IGameEntity;` an alias is not a class scope, so each such definition is a
// compile error and can only be lifted, ported or deleted. game/client/
// c_baseentity.cpp is the largest of them: 269 such definitions in the release
// object at 19451ca6, measured with
//
//     nm --defined-only <obj> | awk '$2=="T"{print $3}'
//         | c++filt | grep -c '^C_?BaseEntity::'    (grep -E, or backslash the ?)
//
// TWELVE were lifted out of the class scope, taking that to 257.
//
//   SIX FRAME-LOOP STATICS, which c_baseentity_statics.h had listed as BLOCKED
//   since the lane that lifted its first three:
//
//       CalcAimEntPositions        -> EntityInterp_CalcAimEntPositions
//       ProcessTeleportList        -> EntityInterp_ProcessTeleportList
//       ProcessInterpolatedList    -> EntityInterp_ProcessInterpolatedList
//       CheckInterpolatedVarParanoidMeasurement
//                                  -> EntityInterp_CheckParanoidMeasurement
//       InterpolateServerEntities  -> EntityInterp_InterpolateServerEntities
//       AddVisibleEntities         -> EntityRender_AddVisibleEntities
//
//   FIVE ALLOCATION OPERATORS, which are a different shape and are guarded
//   differently below -- operator new, operator new[], their two
//   four-argument debug forms and operator delete, whose BODIES moved to
//   EntityMem_Alloc / EntityMem_AllocDbg / EntityMem_Free while the OPERATORS
//   stayed class members.
//
//   ONE STUB, IsFloating -> EntityWater_ClientIsFloating.
//
// WHAT MOVED UNDER THIS FILE, AND WHY FOUR ASSERTIONS BELOW CHANGED SHAPE.
// game/client/c_baseentity.h NO LONGER DECLARES AN ENTITY CLASS. The whole
// `class C_BaseEntity : public IClientEntity { ... }` span sat inside
// `#ifndef SS_CBASEENTITY_ALIAS` -- an arm the compiler never saw, because the
// alias header armed it -- and the close deleted all 2,233 lines of it with the
// rest of the tree's dead arms. `C_BaseEntity` is not a name any more either:
// the alias header is retired and the CODE tokens that spelled it read
// `IGameEntity`. The table below SAID SO IN ADVANCE, where it explains why
// EntityWater_ClientIsFloating could stay in c_baseentity.cpp -- "the class
// body is deleted by the same edit, so the demand dies with it".
//
// So the class-side facts -- the seven one-call forwarders, the four friend
// declarations, the six allocation operators, and the anchor that read the
// class's own opening line -- had their subject inside an arm the compiler
// never saw. NONE OF THEM IS DELETED. The anchor is re-pointed at a LIVE line;
// the other three are re-stated as the ABSENCE they now are, each PAIRED with
// the live successor that carries what the fact was protecting:
// c_baseentity_statics.h's declarations, the three-way home ruling, the
// memdbg bracket in the shim, and -- for the friend block -- the premise that
// made the fourth friend's deletion invisible in the first place, which is
// still true and is still checked nowhere else.
//
// WHY THIS NEEDS A GUARD, and not one of these produces a diagnostic:
//
//   1. THE FRIEND THAT NO BUILD CAN JUSTIFY. Four of the six statics reach
//      private state and are licensed by a friend block at the bottom of
//      C_BaseEntity. Three of those four break the build the moment their
//      friend is deleted -- and the FOURTH does not.
//      EntityInterp_CheckParanoidMeasurement's entire body sits behind
//      INTERPOLATEDVAR_PARANOID_MEASUREMENT, which interpolatedvar.h:27 has
//      commented out, so deleting its friend declaration goes green in every
//      configuration this tree builds and breaks the first time anyone turns
//      the define on. This test is the only thing in the tree that would
//      notice. It is the single highest-value assertion in this file.
//
//   2. Somebody puts logic in a forwarder. Then there are two implementations
//      of a frame-loop step: the SDK's call sites take the class-scope one,
//      the port's own files take the free one, and they drift silently in both
//      directions. Every forwarder below is pinned to a body that is exactly
//      one call and nothing else.
//
//   3. Somebody "finishes the job" on the allocation operators by lifting the
//      operators themselves. A free `operator new( size_t )` is not a lifted
//      C_BaseEntity::operator new -- it REPLACES THE GLOBAL ONE, so every
//      allocation in client.so starts going through an allocator that memsets,
//      and nothing about that is a compile error.
//
//   4. Somebody moves an EntityMem_ body to another .cpp. MEM_ALLOC_CREDIT()
//      records __FILE__, so that silently re-credits every entity allocation
//      to a different file, and the memdbgoff/memdbgon bracket that has to be
//      around a definition of operator-new machinery is left behind.
//
// PROVEN ABLE TO FAIL. The body reader is exercised on hand-written positives
// and negatives before any file is asserted clean -- without that, every "is
// no longer defined" assertion below passes for free, which is the shape of
// the thirty-two checks-that-cannot-fail found in this tree so far. Each file
// is also pinned by an ANCHOR string rather than a line
// count: a lane elsewhere had `lines.size() > 100` sitting four lines from a
// false alarm, and a file that fails to open reads as empty, which makes every
// absence assertion true at once.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
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

std::string ReadGameFile( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

const std::string &ClientHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity.h" );
	return s;
}

// interpolatedvar.h, for the one premise in this file that is not about any of
// the three files under test: whether the paranoid measurement is compiled.
const std::string &InterpolatedVarHeader()
{
	static const std::string s = ReadGameFile( "client/interpolatedvar.h" );
	return s;
}

const std::string &StaticsHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity_statics.h" );
	return s;
}

// THE CLIENT FLIP's home for the ones that outlive c_baseentity_statics.cpp too
// -- CLIENT_FLIP lane 2 (`c8-entitystatics`). That file is in the flip's own
// removal set, so the step-6 evacuation was not the last move for any of these.
const std::string &ShimSource()
{
	static const std::string s =
		ReadGameFile( "../sourcesharp/game/client/shell_entitystatics.cpp" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a name written in PROSE -- and all three
// files under test are full of exactly that, being about exactly this -- is not
// read as code. This file's own header block would fail nearly every assertion
// below without it.
//
// Not a C++ lexer: it does not track string or character literals, and does not
// need to, because what is built on it is a DEFINITION detector and a
// definition cannot occur inside a string literal. That matters for this file
// in particular: c_baseentity.cpp carries VPROF strings that read
// "C_BaseEntity::InterpolateServerEntities" and "C_BaseEntity::
// AddVisibleEntities", and both would be false positives for a plain substring
// search. Neither is followed by `(`-args-`{`, so neither is one here.
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

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) ||
	       ( c >= '0' && c <= '9' ) || c == '_';
}

size_t SkipSpace( const std::string &s, size_t at )
{
	while ( at < s.size() && ( s[ at ] == ' ' || s[ at ] == '\t' ||
	                           s[ at ] == '\r' || s[ at ] == '\n' ) )
		at++;
	return at;
}

//-----------------------------------------------------------------------------
// The body of one function DEFINED as `<name>(`, squeezed of whitespace. Empty
// if it is not defined in this text. Used both to prove a free function has a
// body and to prove a forwarder still only forwards.
//-----------------------------------------------------------------------------
std::string FunctionBody( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + strName.size();
		if ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			continue;   // a longer name that starts with this one

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;

		const size_t open = strCode.find( '{', i );
		const size_t stop = strCode.find( ';', i );
		if ( open == std::string::npos )
			continue;
		if ( stop != std::string::npos && stop < open )
			continue;   // a declaration, not a definition

		int depth = 0;
		for ( size_t k = open; k < strCode.size(); k++ )
		{
			if ( strCode[ k ] == '{' )
				depth++;
			else if ( strCode[ k ] == '}' )
			{
				depth--;
				if ( depth == 0 )
					return Squeezed( strCode.substr( open + 1, k - open - 1 ) );
			}
		}
	}
	return std::string();
}

bool Contains( const std::string &strHaystack, const char *pszNeedle )
{
	return strHaystack.find( pszNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// IS THERE A DEFINITION AT ALL -- as opposed to FunctionBody, which answers
// WHAT IS IN IT.
//
// FunctionBody returns the body's CONTENTS, so `FunctionBody( s, f ).empty()`
// is true both for "f is not defined here" and for "f is defined here with an
// EMPTY BODY". Every "must not be defined here" assertion built on it therefore
// passes on an empty stub -- which is the silent no-op plan_entity.md §7
// forbids and the exact thing such an assertion exists to catch. Found by
// mutation in baseentity_statics_test.cpp, which carries the same helper for
// the same reason; the two test binaries share no code today (they each carry
// their own StripComments, Squeezed and FunctionBody) and this follows that
// convention rather than starting a new one.
//
// A definition is `name(...)` followed by `{`; a declaration and a call are
// both followed by `;`.
bool DefinesFreeFunction( const std::string &strSource, const char *pszName )
{
	const std::string strCode = StripComments( strSource );
	const std::string strName( pszName );
	const std::string strNeedle = strName + "(";

	for ( size_t pos = strCode.find( strNeedle ); pos != std::string::npos;
	      pos = strCode.find( strNeedle, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + strName.size();
		int depth = 0;
		for ( ; i < strCode.size(); i++ )
		{
			if ( strCode[ i ] == '(' )
			{
				depth++;
			}
			else if ( strCode[ i ] == ')' )
			{
				depth--;
				if ( depth == 0 )
					break;
			}
		}

		for ( i++; i < strCode.size(); i++ )
		{
			const char c = strCode[ i ];
			if ( c == ' ' || c == '\t' || c == '\r' || c == '\n' )
				continue;
			if ( c == '{' )
				return true;
			break;
		}
	}

	return false;
}

bool ContainsInCode( const std::string &strHaystack, const char *pszNeedle )
{
	return Contains( StripComments( strHaystack ), pszNeedle );
}

//-----------------------------------------------------------------------------
// The twelve, by their class-scope names and their free names.
//-----------------------------------------------------------------------------
struct Lift_t
{
	const char *pszMember;      // what c_baseentity.cpp must NOT define any more
	const char *pszFree;        // what it must define instead
	const char *pszForwarder;   // the squeezed body the class member must have
};

const Lift_t kStatics[] =
{
	{ "CalcAimEntPositions",       "EntityInterp_CalcAimEntPositions",
	  "EntityInterp_CalcAimEntPositions();" },
	{ "ProcessTeleportList",       "EntityInterp_ProcessTeleportList",
	  "EntityInterp_ProcessTeleportList();" },
	{ "ProcessInterpolatedList",   "EntityInterp_ProcessInterpolatedList",
	  "EntityInterp_ProcessInterpolatedList();" },
	{ "CheckInterpolatedVarParanoidMeasurement",
	                               "EntityInterp_CheckParanoidMeasurement",
	  "EntityInterp_CheckParanoidMeasurement();" },
	{ "InterpolateServerEntities", "EntityInterp_InterpolateServerEntities",
	  "EntityInterp_InterpolateServerEntities();" },
	{ "AddVisibleEntities",        "EntityRender_AddVisibleEntities",
	  "EntityRender_AddVisibleEntities();" },
	{ "IsFloating",                "EntityWater_ClientIsFloating",
	  "returnEntityWater_ClientIsFloating();" },
};
const size_t kStaticCount = sizeof( kStatics ) / sizeof( kStatics[ 0 ] );

// The four that reach private state. Three of these break the build if their
// friend goes; the fourth does not, and is the reason this test exists.
const char *const kFriends[] =
{
	"friend void EntityInterp_CalcAimEntPositions();",
	"friend void EntityInterp_ProcessTeleportList();",
	"friend void EntityInterp_ProcessInterpolatedList();",
	"friend void EntityInterp_CheckParanoidMeasurement();",
};
const size_t kFriendCount = sizeof( kFriends ) / sizeof( kFriends[ 0 ] );

const char *const kMemHelpers[] =
{
	"EntityMem_Alloc",
	"EntityMem_Free",		// EntityMem_AllocDbg left 2026-10-25: nothing imported it
};
const size_t kMemHelperCount = sizeof( kMemHelpers ) / sizeof( kMemHelpers[ 0 ] );

} // namespace


//=============================================================================//
// 0. THE DETECTORS, ON HAND-WRITTEN POSITIVES AND NEGATIVES.
//
// Everything below asserts that names are ABSENT from a file. An absence
// assertion over a detector that finds nothing is true for free, and so is one
// over a file that failed to open. Both are closed here first.
//=============================================================================//

TEST( CBaseEntityLift, TheBodyReaderFindsABodyAndRejectsADeclaration )
{
	EXPECT_EQ( "returnEntityWater_ClientIsFloating();",
	           FunctionBody( "bool IsFloating() { return EntityWater_ClientIsFloating(); }",
	                         "IsFloating" ) );

	EXPECT_EQ( "", FunctionBody( "bool IsFloating();", "IsFloating" ) );

	// a longer name that merely starts with the one asked for
	EXPECT_EQ( "", FunctionBody( "void IsFloatingSomething() { x(); }", "IsFloating" ) );
}

TEST( CBaseEntityLift, TheTwoHeadersUnderTestWereActuallyRead )
{
	// A file that fails to open reads as the empty string, and every absence
	// assertion below is then true at once. An ANCHOR rather than a size: a
	// length threshold sits four lines from a false alarm the moment anyone
	// reformats.
	// RE-ANCHORED. This used to read "class C_BaseEntity : public IClientEntity",
	// which was the first line of a class body inside `#ifndef
	// SS_CBASEENTITY_ALIAS` -- an arm this build never compiled, and one the
	// close has now deleted. The replacement is a LIVE line: it sits at
	// preprocessor depth 1, inside nothing but the `#ifndef C_BASEENTITY_H`
	// include guard, and it is part of the tail that SURVIVES the class.
	ASSERT_TRUE( Contains( ClientHeader(), "EXTERN_RECV_TABLE(DT_BaseEntity);" ) )
		<< "game/client/c_baseentity.h did not read (anchor absent)";
	ASSERT_TRUE( Contains( StaticsHeader(), "C_BASEENTITY_STATICS_H" ) )
		<< "game/client/c_baseentity_statics.h did not read (anchor absent)";
}


//=============================================================================//
// 1. THE TWELVE ARE OUT OF THE CLASS SCOPE.
//=============================================================================//

// (The memdbgoff/memdbgon bracket around the three allocation helpers went
// with the client's stock headers, 2026-10-25: client.so defines
// NO_MALLOC_OVERRIDE, under which memdbgon.h is empty, so the bracket changed
// nothing it compiled.)

TEST( CBaseEntityLift, TheStaticsHeaderDeclaresEveryFreeFunction )
{
	for ( size_t i = 0; i < kStaticCount; i++ )
	{
		EXPECT_TRUE( ContainsInCode( StaticsHeader(), kStatics[ i ].pszFree ) )
			<< kStatics[ i ].pszFree << " is not declared in c_baseentity_statics.h; "
			<< "the forwarder in the class body would resolve through some other "
			<< "transitive path today and stop resolving tomorrow";
	}
	for ( size_t i = 0; i < kMemHelperCount; i++ )
	{
		EXPECT_TRUE( ContainsInCode( StaticsHeader(), kMemHelpers[ i ] ) )
			<< kMemHelpers[ i ] << " is not declared in c_baseentity_statics.h";
	}
}


//=============================================================================//
// 2. THE FORWARDERS STILL ONLY FORWARD.
//
// A forwarder that grows a body is two implementations of one frame-loop step:
// the SDK's call sites take the class-scope one, the port's own files take the
// free one, and they drift in both directions with nothing to say so.
//=============================================================================//

TEST( CBaseEntityLift, TheClientHeaderNoLongerDeclaresAnEntityClassAtAll )
{
	// THE LIVE SUCCESSOR the three re-stated facts below lean on. Asserted in
	// BOTH directions, because "the class is gone" on its own is equally true
	// of an empty file, the wrong file and a file that failed to open -- so the
	// tail that survived it is asserted present in the same breath, and every
	// one of those lines is LIVE: preprocessor depth 1, inside nothing but the
	// `#ifndef C_BASEENTITY_H` include guard.
	const std::string strSqueezed = Squeezed( StripComments( ClientHeader() ) );

	EXPECT_FALSE( strSqueezed.find( "classC_BaseEntity" ) != std::string::npos )
		<< "c_baseentity.h names C_BaseEntity in CODE again. The alias header is "
		<< "deleted; the occurrences left in this file are prose, which "
		<< "StripComments removes before this assertion sees them.";
	EXPECT_FALSE( strSqueezed.find( "classIGameEntity{" ) != std::string::npos )
		<< "c_baseentity.h has grown an IGameEntity class BODY. The interface is "
		<< "defined once, in public/igameentity.h.";

	const char *kLiveTail[] = {
		"typedef void (IGameEntity::*BASEPTR)(void);",
		"#define CREATE_PREDICTED_ENTITY( className )",
		"EXTERN_RECV_TABLE(DT_BaseEntity);",
		"bool FClassnameIs( IGameEntity *pEntity, const char *szClassname );",
		"inline IGameEntity *SDKEntityFromUnknown( IClientUnknown *pUnknown )",
	};
	for ( size_t i = 0; i < sizeof( kLiveTail ) / sizeof( kLiveTail[ 0 ] ); i++ )
	{
		EXPECT_TRUE( Contains( ClientHeader(), kLiveTail[ i ] ) )
			<< "c_baseentity.h lost \"" << kLiveTail[ i ] << "\". The class went "
			<< "with the dead arm; the TAIL is not the class.";
	}
}

TEST( CBaseEntityLift, NoForwarderIsLeftAndNoSecondImplementationTookItsPlace )
{
	// WAS EveryForwarderIsExactlyOneCall, which pinned each class member's body
	// to a bare call to its free function. Those forwarders were in the class
	// body and went with it.
	//
	// THE HAZARD IS UNCHANGED AND IS WHY THIS FACT IS NOT DROPPED: two
	// implementations of one frame-loop step, drifting in both directions with
	// nothing to say so. What has changed is which shape a second
	// implementation would take. There is no class scope left, so it would be a
	// FREE function -- and a free definition in a HEADER is worse than the
	// forwarder ever was, because every includer gets it.
	for ( size_t i = 0; i < kStaticCount; i++ )
	{
		EXPECT_EQ( "", FunctionBody( ClientHeader(), kStatics[ i ].pszMember ) )
			<< kStatics[ i ].pszMember << " is DEFINED in c_baseentity.h. There "
			<< "is no class here for it to be a member of, so this is a free "
			<< "function of the SDK's name sitting in a header.";

		EXPECT_FALSE( DefinesFreeFunction( ClientHeader(), kStatics[ i ].pszFree ) )
			<< kStatics[ i ].pszFree << " is DEFINED in c_baseentity.h. Its one "
			<< "home is ruled on below; a body in the header is a second "
			<< "implementation that every includer compiles.";
	}

	// ...and the declarations the forwarders used to reach are still there, so
	// the two EXPECT_FALSEs above are not passing over a file that lost them.
	for ( size_t i = 0; i < kStaticCount; i++ )
	{
		EXPECT_TRUE( ContainsInCode( StaticsHeader(), kStatics[ i ].pszFree ) )
			<< kStatics[ i ].pszFree << " is not named in c_baseentity_statics.h "
			<< "at all any more.";
	}
}


//=============================================================================//
// 3. THE FOUR FRIENDS.
//
// Three of these break the build the moment they go. The fourth --
// EntityInterp_CheckParanoidMeasurement -- does not, because its whole body is
// behind INTERPOLATEDVAR_PARANOID_MEASUREMENT, commented out at
// interpolatedvar.h:27. Deleting that one is green in every configuration this
// tree builds and breaks the first time anyone turns the define on. This
// assertion is the only thing that would notice.
//=============================================================================//

TEST( CBaseEntityLift, TheFourFriendsWentWithTheClassAndTheParanoidPREMISEIsStillLive )
{
	// WAS TheFourFriendDeclarationsSurvive, described above as "the single
	// highest-value assertion in this file". The friend block was at the bottom
	// of C_BaseEntity and went with the class; there is no private state left
	// for a friend to license, so the DECLARATIONS have no subject.
	//
	// THE PREMISE UNDER THEM DOES. What made that assertion the highest-value
	// one was never the four declarations -- it was that deleting the FOURTH
	// was invisible: EntityInterp_CheckParanoidMeasurement's entire body sits
	// behind INTERPOLATEDVAR_PARANOID_MEASUREMENT, which interpolatedvar.h has
	// COMMENTED OUT, so nothing this tree builds compiles it and no
	// configuration anyone runs would have noticed. That is still true, it is
	// still checked nowhere else in the tree, and it is what is asserted here
	// now: the define is still commented out, so the fourth is still the one
	// whose loss no build reports.
	const std::string strCode = Squeezed( StripComments( ClientHeader() ) );
	for ( size_t i = 0; i < kFriendCount; i++ )
	{
		const std::string strWanted = Squeezed( kFriends[ i ] );
		EXPECT_TRUE( strCode.find( strWanted ) == std::string::npos )
			<< kFriends[ i ] << " is back in c_baseentity.h. There is no class "
			<< "here to declare a friend of, so this does not compile -- and in "
			<< "a header every client TU includes.";
	}

	// The live successor. Note what is asserted: the COMMENTED-OUT spelling,
	// not the absence of the define. "No `#define INTERPOLATEDVAR_PARANOID_
	// MEASUREMENT`" is also true of a file that deleted the line, and this fact
	// is about the line being there and switched off -- which is what makes the
	// paranoid path something a future reader can turn back on.
	ASSERT_FALSE( InterpolatedVarHeader().empty() )
		<< "game/client/interpolatedvar.h did not read; the assertion below "
		<< "would be measuring an empty string.";
	EXPECT_TRUE( Contains( InterpolatedVarHeader(),
	                       "//#define INTERPOLATEDVAR_PARANOID_MEASUREMENT" ) )
		<< "interpolatedvar.h no longer has INTERPOLATEDVAR_PARANOID_MEASUREMENT "
		<< "commented out. If it is now DEFINED, the paranoid measurement path "
		<< "compiles for the first time and everything this file says about "
		<< "EntityInterp_CheckParanoidMeasurement being invisible is wrong. If "
		<< "the line was simply deleted, the switch is gone and so is the "
		<< "reason the fourth friend was special.";
	EXPECT_TRUE( Contains( InterpolatedVarHeader(),
	                       "#ifdef INTERPOLATEDVAR_PARANOID_MEASUREMENT" ) )
		<< "interpolatedvar.h has no `#ifdef INTERPOLATEDVAR_PARANOID_MEASUREMENT` "
		<< "arm left, so the switch above switches nothing.";
}


//=============================================================================//
// 4. THE ALLOCATION OPERATORS STAY CLASS MEMBERS.
//
// This is the one place where finishing the lift would be a serious bug and
// not a compile error. A free `operator new( size_t )` is not a lifted
// C_BaseEntity::operator new; it REPLACES THE GLOBAL ONE, so every allocation
// in client.so starts going through an allocator that memsets.
//=============================================================================//

TEST( CBaseEntityLift, TheHeaderDeclaresNoAllocationOperatorAndTheBodiesAreStillInTheShim )
{
	// WAS TheClassStillDeclaresItsOwnAllocationOperators. The six declarations
	// were class members and went with the class.
	//
	// THE BUG THIS GUARDS IS THE SAME ONE AND IT GOT EASIER TO WRITE, not
	// harder: a free `operator new( size_t )` is not a lifted
	// C_BaseEntity::operator new, it REPLACES THE GLOBAL ONE, so every
	// allocation in client.so starts going through an allocator that memsets --
	// and nothing about that is a compile error. With no class scope left,
	// ANY operator new written in this header is that free form, and every
	// client TU that includes c_baseentity.h gets it.
	const std::string strCode = Squeezed( StripComments( ClientHeader() ) );

	EXPECT_TRUE( strCode.find( "operatornew(" ) == std::string::npos )
		<< "c_baseentity.h declares or defines an operator new. There is no "
		<< "class here to scope it to, so it is a GLOBAL replacement in a "
		<< "header.";
	EXPECT_TRUE( strCode.find( "operatornew[](" ) == std::string::npos )
		<< "c_baseentity.h declares or defines an operator new[].";
	EXPECT_TRUE( strCode.find( "operatordelete(" ) == std::string::npos )
		<< "c_baseentity.h declares or defines an operator delete.";

	// The live successor, and the reason the absences above are not a deletion:
	// the three bodies the operators used to call are still defined in the
	// shim, and TheAllocatorSitsInsideTheMemdbgBracket pins them between the
	// memdbgoff/memdbgon pair that makes them mean what they say.
	for ( size_t i = 0; i < kMemHelperCount; i++ )
	{
		EXPECT_TRUE( DefinesFreeFunction( ShimSource(), kMemHelpers[ i ] ) )
			<< kMemHelpers[ i ] << " has no body in shell_entitystatics.cpp. The "
			<< "operators are gone from the header; if the bodies go too, the "
			<< "port's own world shell (world_adapter_client.cpp, c_world.cpp) "
			<< "loses its allocator with nothing saying so.";
	}
}
