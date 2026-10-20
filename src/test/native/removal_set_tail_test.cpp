//========= SourceSharp — the step-3 removal set's tail, pinned ==============//
//
// A SOURCE SCANNER, like baseentity_shared_lift_test, physicslink_lift_test and
// soundemitter_lift_test, and for the same reason: the claim is about the TEXT
// of files that need a whole engine to run, so the text is what is read.
// buildcpptests links each test binary against gtest and nothing else, so
// calling the real PhysicsSweep_CheckEntity here is not available, and
// re-implementing it to "check the logic" would be a check that cannot fail --
// it would only compare a copy against itself.
//
// WHAT THIS COVERS. plan_baseentity.md Phase 13 step 3's removal set is the
// files DEFINING CBaseEntity members out of line; under
// `using CBaseEntity = IGameEntity;` an alias is not a class scope, so every
// such definition is a COMPILE error and can only be lifted, ported or deleted.
// The TAIL of that set is four files, and this pins all four at once:
//
//   game/server/physics_main.cpp                18   (was 19)
//   game/client/physics_main_client.cpp         15   (unchanged)
//   sourcesharp/game/server/shell_ai_orphans.cpp 1   (was 4)
//   game/server/subs.cpp                         2   (unchanged)
//
// There were also TWO quarantine files holding the lifted names' class-scope
// forms. Both are gone -- baseentity_sweep_forwarders.cpp and
// shell_ai_orphans_forwarders.cpp.
//
// ONLY shell_ai_orphans.cpp's (now shell_absent.cpp's) share of the tail is
// still asserted here. physics_main.cpp, physics_main_client.cpp, subs.cpp and
// the call sites in baseentity_shared.cpp are Valve stock that neither
// client.so nor server.so compiles, and the facts that read them were culled
// when the farm stopped carrying unused stock files.
//
// WHAT WAS LIFTED, AND WHAT WAS NOT. Three of shell_ai_orphans.cpp's four take
// no `this` -- TraceAttackToTriggers, CreateBubbleTrailTracer,
// UpdateShotStatistics -- and are now EntityShot_* at namespace scope in the
// same file, declared in shell_shot_path.h. physics_main.cpp's
// PhysicsCheckSweep was already nothing but `::PhysicsCheckSweep( this, ... )`
// forwarding to a file-static that took the entity explicitly; the static lost
// its `static` and became PhysicsSweep_CheckEntity.
//
// NEITHER SET OF CLASS-SCOPE NAMES SURVIVES. Both quarantine files have been
// retired, and the two retirements are NOT the same shape, which is the part
// worth reading:
//
//   PhysicsCheckSweep had no callers at all -- zero referring objects on the
//   link line and one source call site, physics_main.cpp's own, which already
//   named the free function. File AND declaration both went.
//
//   The orphans' three had two referring objects, baseentity_shared.o and
//   gamerules.o, so retiring them was four call-site rewrites: FireBullets'
//   three and anyentity.h's TraceAttackToTriggers wrapper. The DECLARATIONS
//   STAY in baseentity.h, because ai_basenpc.cpp still defines all three for
//   the builds that compile it -- this build just never names them. The
//   removal set counts out-of-line DEFINITIONS, and that is what left.
//
// EVERYTHING ELSE IN THE TAIL IS BLOCKED, and the reasons are the interesting
// part, because two of them are categories the plan did not have names for:
//
//   NO `this` BUT VIRTUAL. PerformCustomPhysics's whole body is `Assert(0)` on
//   both sides, so it takes no `this` and looks liftable. It is `virtual` on
//   BOTH sides (baseentity.h:1804, c_baseentity.h:1507), and a virtual name IS
//   a vtable slot: lifting it leaves every override without a base
//   implementation. Same for the client's StartTouch and EndTouch, whose
//   bodies are entirely commented out -- empty, virtual, and overridden.
//
//   NO `this` BUT TAKEN AS A POINTER-TO-MEMBER. This one the plan does not name
//   at all, and it is what blocks subs.cpp. CBaseEntity::SUB_DoNothing has an
//   EMPTY body and reads nothing, which is the textbook lift shape -- but it is
//   a THINK function, reached as `SetThink( &CDerived::SUB_DoNothing )` from
//   fourteen sites and registered in a datadesc by
//   `DEFINE_FUNCTION( SUB_DoNothing )`. A pointer-to-member cannot be formed
//   from a free function and DEFINE_FUNCTION requires a member, so lifting it
//   turns every one of those sites into a compile error. It has to be PORTED,
//   not lifted. SUB_Remove is the same shape and additionally reads m_iHealth
//   and calls UTIL_Remove( this ).
//
// The rest of physics_main.cpp is the movement core and reads the entity
// through its own `this` throughout; HandleShotImpactingGlass names `this`
// outright as the attacker of the round it refires behind glass.
//
// SIDE-INDEPENDENCE, AND WHY THE SCANNER READS TWO SCOPES. The client's
// out-of-line definitions are written `C_BaseEntity::` LITERALLY in
// physics_main_client.cpp -- unlike game/shared/ files, which say
// `CBaseEntity::` on both sides and rely on shared_classnames.h's
// `#define CBaseEntity C_BaseEntity`. A scanner looking only for
// "CBaseEntity::" finds NOTHING in the client file and its count passes for
// free at zero. That is exactly the fault plan_baseentity.md's printed
// measurement recipe has, and it fails toward "the set is empty". So every
// extractor here takes the scope as a parameter and is run over both.
//
// EVERY DETECTOR IS PROVEN ABLE TO ANSWER BOTH WAYS on hand-written fixtures at
// the bottom, because an empty-list assertion over a detector that finds
// nothing is the purest check that cannot fail.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <algorithm>
#include <fstream>
#include <set>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//
// Unlike the earlier lift tests this one must reach BOTH game/ and
// sourcesharp/, so it anchors on a directory holding both.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string strProbe( kCandidates[ i ] );
		strProbe += "/sourcesharp/game";

		DIR *pDir = opendir( strProbe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadSrcFile( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
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

// Phase 1b merged shell_ai_orphans.cpp into shell_absent.cpp, keeping only what
// an object on the server link line still imported. Of this file's subjects
// that was PostFrameNavigationSystem (gone in plan_server_native_min P1): the three EntityShot_*
// lifts and CTriggerTraceEnum were imported by no object at all and went.
// The name is kept so that the facts below read as the same questions asked of
// the same subject after a move.
const std::string &OrphansSource()
{
	static const std::string s = ReadSrcFile( "sourcesharp/game/server/shell_absent.cpp" );
	return s;
}

// game/server/anyentity.h -- the fourth, and the one that put the demand for
// CBaseEntity::TraceAttackToTriggers into gamerules.o.
const std::string &AnyEntityHeader()
{
	static const std::string s = ReadSrcFile( "sourcesharp/game/server/anyentity.h" );
	return s;
}

const std::string &ShotPathHeader()
{
	static const std::string s = ReadSrcFile( "sourcesharp/game/server/shell_shot_path.h" );
	return s;
}

// The control for the farm's game/ tree. game/server/ is empty since
// plan_server_native_min P1 deleted its last overlay headers
// (physics_sweep.h among them), so the control is a header the client still
// links from game/shared/.
const std::string &FarmControlHeader()
{
	static const std::string s = ReadSrcFile( "game/shared/shareddefs.h" );
	return s;
}

// ServerBaseEntityHeader() WAS HERE. game/server/baseentity.h is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6; its absence
// is pinned, anchored, in EveryFileWasReallyOpenedAndIsTheRightOne.

const std::string &ClientBaseEntityHeader()
{
	static const std::string s = ReadSrcFile( "game/client/c_baseentity.h" );
	return s;
}

//-----------------------------------------------------------------------------
// TWO MORE FILES, AND WHY THEY ARE HERE.
//
// Both entity headers lost their class bodies: the whole
// `class CBaseEntity { ... }` / `class C_BaseEntity { ... }` span was inside
// `#ifndef SS_CBASEENTITY_ALIAS`, an arm this build never compiled, and the
// close deleted it. Every "is it still declared virtual there" read below
// therefore had no subject left. The three files here are where those subjects
// went.
//
//   public/igameentity.h    the port's own entity interface, compiled. Touch,
//                           StartTouch and EndTouch are PURE virtual on it --
//                           the vtable-slot claim, in the file that now owns it.
//   game/server/world.h     live (mapentities.cpp and physics.cpp include it)
//                           and carrying plain `virtual T F( ... );` and plain
//                           non-virtual declarations, which is what the
//                           detector matches. It cannot match a pure virtual:
//                           `= 0;` is not the `;` it looks for.
//-----------------------------------------------------------------------------
const std::string &GameEntityInterfaceHeader()
{
	static const std::string s = ReadSrcFile( "public/igameentity.h" );
	return s;
}

const std::string &WorldHeader()
{
	static const std::string s = ReadSrcFile( "sourcesharp/game/server/ss_world.h" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a CBaseEntity:: name written in prose --
// which every file under test is full of, being about exactly that -- is not
// mistaken for code.
//
// Not a C++ lexer: it does not track string or character literals. It does not
// need to, because what is built on top of it is a DEFINITION detector, and a
// definition cannot occur inside a string literal.
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

size_t SkipParens( const std::string &strCode, size_t at )
{
	if ( at >= strCode.size() || strCode[ at ] != '(' )
		return std::string::npos;

	int depth = 0;
	for ( size_t i = at; i < strCode.size(); i++ )
	{
		if ( strCode[ i ] == '(' ) depth++;
		else if ( strCode[ i ] == ')' )
		{
			depth--;
			if ( depth == 0 )
				return i + 1;
		}
	}
	return std::string::npos;
}

size_t SkipSpace( const std::string &s, size_t i )
{
	while ( i < s.size() && ( s[ i ] == ' ' || s[ i ] == '\t' ||
	                          s[ i ] == '\r' || s[ i ] == '\n' ) )
		i++;
	return i;
}

// The two spellings an out-of-line definition can carry. game/shared/ files say
// the first on both sides and let shared_classnames.h rewrite it; the client's
// own files say the second literally.
const char *const kServerScope = "CBaseEntity::";
const char *const kClientScope = "C_BaseEntity::";

//-----------------------------------------------------------------------------
// Every member name DEFINED out of line as `<scope><Name>( ... ) {`.
//
// The discriminator between a definition and a call is what follows the
// argument list: a definition's next non-space character is '{' (allowing for a
// trailing `const`), a call's is ';' or ',' or ')' or an operator.
//-----------------------------------------------------------------------------
std::vector< std::string > MemberDefinitionsIn( const std::string &strSource, const char *pszScope )
{
	const std::string strCode = StripComments( strSource );
	const std::string kScope( pszScope );

	std::vector< std::string > names;

	for ( size_t pos = strCode.find( kScope ); pos != std::string::npos;
	      pos = strCode.find( kScope, pos + 1 ) )
	{
		// Must not be preceded by an identifier char, or a longer name ending
		// in the scope text would match.
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + kScope.size();
		const size_t nameStart = i;
		while ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			i++;
		if ( i == nameStart )
			continue;

		const std::string strName = strCode.substr( nameStart, i - nameStart );

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;

		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;

		i = SkipSpace( strCode, i );

		// allow a trailing `const`
		if ( strCode.compare( i, 5, "const" ) == 0 &&
		     ( i + 5 >= strCode.size() || !IsIdentChar( strCode[ i + 5 ] ) ) )
		{
			i = SkipSpace( strCode, i + 5 );
		}

		if ( i < strCode.size() && strCode[ i ] == '{' )
			names.push_back( strName );
	}

	return names;
}

// Both spellings at once, which is what a per-FILE count of the removal set
// means. A file uses one or the other, never both.
std::vector< std::string > MemberDefinitions( const std::string &strSource )
{
	std::vector< std::string > all = MemberDefinitionsIn( strSource, kServerScope );
	const std::vector< std::string > client = MemberDefinitionsIn( strSource, kClientScope );
	all.insert( all.end(), client.begin(), client.end() );
	return all;
}

//-----------------------------------------------------------------------------
// Every static DATA member DEFINED out of line as `<scope><Name>` NOT followed
// by an argument list -- i.e. followed by `=` (an initialiser), `;` (none) or
// `[` (an array).
//
// This is the failure no consumer count can see. A static data member of an
// alias does not fail to LINK; it stops the file PARSING, everywhere, at once,
// the moment step 4 writes `using CBaseEntity = IGameEntity;`. Strictly worse
// than the undefined symbols step 3 tracks, and `nm ... $2=="T"` is blind to it
// because T is TEXT symbols only.
//-----------------------------------------------------------------------------
std::vector< std::string > StaticDataMembersIn( const std::string &strSource, const char *pszScope )
{
	const std::string strCode = StripComments( strSource );
	const std::string kScope( pszScope );

	std::vector< std::string > names;

	for ( size_t pos = strCode.find( kScope ); pos != std::string::npos;
	      pos = strCode.find( kScope, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + kScope.size();
		const size_t nameStart = i;
		while ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			i++;
		if ( i == nameStart )
			continue;

		const std::string strName = strCode.substr( nameStart, i - nameStart );

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() )
			continue;

		// A definition needs a TYPE in front of it, on the same line, or this
		// is a read or a write of an existing one from inside a function.
		//
		// "Has an identifier char before it" is NOT enough on its own, and
		// baseentity_shared_lift_test's detector stops there -- which makes
		// `int n = CBaseEntity::m_nSeed;` and
		// `void Foo() { CBaseEntity::m_x = 1; }` both false positives, and is
		// why that test's negative fixture asserts about two named members
		// rather than about the list being empty. Requiring the lead to look
		// like a TYPE and nothing else -- no `=`, no brackets, no statement
		// punctuation -- rejects both, which lets the assertions here be
		// "the list is empty" instead.
		size_t lineStart = strCode.rfind( '\n', pos );
		lineStart = ( lineStart == std::string::npos ) ? 0 : lineStart + 1;
		const std::string strLead = strCode.substr( lineStart, pos - lineStart );

		bool bHasType = false;
		bool bLeadIsClean = true;
		for ( size_t k = 0; k < strLead.size(); k++ )
		{
			const char c = strLead[ k ];
			if ( IsIdentChar( c ) )
				bHasType = true;
			// Anything that can only appear in an expression or a statement,
			// never in the type of a namespace-scope definition.
			//
			// Deliberately does NOT include `,` `<` `>` `:`, which appear in
			// template and qualified types -- `CUtlMap< int, int >
			// CBaseEntity::sm_map;` is a static data member and must still be
			// caught. Erring toward a false POSITIVE is right here: this
			// detector exists to catch the parse-stopping failure, so a
			// spurious hit costs a look and a miss costs the whole build.
			if ( c == '=' || c == '(' || c == ')' ||
			     c == '{' || c == '}' || c == ';' )
			{
				bLeadIsClean = false;
			}
		}
		if ( !bHasType || !bLeadIsClean )
			continue;

		if ( strCode[ i ] == '=' || strCode[ i ] == ';' || strCode[ i ] == '[' )
			names.push_back( strName );
	}

	return names;
}

std::vector< std::string > StaticDataMembers( const std::string &strSource )
{
	std::vector< std::string > all = StaticDataMembersIn( strSource, kServerScope );
	const std::vector< std::string > client = StaticDataMembersIn( strSource, kClientScope );
	all.insert( all.end(), client.begin(), client.end() );
	return all;
}

//-----------------------------------------------------------------------------
// Is <name> declared `virtual` in this header? Looks at the declaration line
// only, which is where the keyword has to be: C++ requires it per declaration.
//-----------------------------------------------------------------------------
bool IsDeclaredVirtual( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		const size_t after = pos + strName.size();
		if ( after < strCode.size() && IsIdentChar( strCode[ after ] ) )
			continue;

		size_t i = SkipSpace( strCode, after );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;   // not a function declaration

		size_t lineStart = strCode.rfind( '\n', pos );
		lineStart = ( lineStart == std::string::npos ) ? 0 : lineStart + 1;
		const std::string strLine = strCode.substr( lineStart, pos - lineStart );

		if ( strLine.find( "virtual" ) != std::string::npos )
			return true;
	}
	return false;
}

// Is <name> declared at all in this header, as a function?
bool IsDeclaredAtAll( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		const size_t after = pos + strName.size();
		if ( after < strCode.size() && IsIdentChar( strCode[ after ] ) )
			continue;

		size_t i = SkipSpace( strCode, after );
		if ( i < strCode.size() && strCode[ i ] == '(' )
			return true;
	}
	return false;
}

// ZERO NOW, and it was 1 until the close deleted the dead guarded arms. The one
// that was left, HandleShotImpactingGlass, was not lifted and was not ported --
// it was GUARDED, inside `#ifndef SOURCESHARP`, because armed it has no caller:
// its only call site is game/shared/baseentity_shared.cpp:2119 and that file is
// in both removal sets. The close deleted the arm with the tree's other 452, so
// the file now defines no member of any entity class at all, which is the state
// shell_ai_orphans.cpp's own banner predicted -- "with this arm the object emits
// none, so gate 1's `nm` row reaches 0 on the server".
const size_t kOrphansMembers           = 0;    // was 4, then 1

struct Lifted_t
{
	const char *pszMember;
	const char *pszFree;
};

// shell_ai_orphans.cpp's three.
const Lifted_t kOrphanLifts[] =
{
	{ "TraceAttackToTriggers",   "EntityShot_TraceAttackToTriggers" },
	{ "CreateBubbleTrailTracer", "EntityShot_CreateBubbleTrail"     },
	{ "UpdateShotStatistics",    "EntityShot_UpdateStatistics"      },
};
const size_t kNumOrphanLifts = sizeof( kOrphanLifts ) / sizeof( kOrphanLifts[ 0 ] );

//=============================================================================
// ANCHORS. Every file is asserted to contain a string that is distinctive to
// IT, rather than merely to be non-empty or of some length -- a length check
// passes on the wrong file, and a line count rots on every unrelated edit.
//=============================================================================

struct Anchor_t
{
	const char *pszWhat;
	const std::string &( *pfnSource )();
	const char *pszAnchor;
};

const Anchor_t kAnchors[] =
{
	// physics_main.cpp, physics_main_client.cpp, subs.cpp, baseentity_shared.cpp
	// and NextBot/NextBot.h LEFT THIS LIST: they are Valve stock that neither
	// client.so nor server.so compiles, and the farm no longer carries them.
	// RE-ANCHORED (Phase 1b, then plan_server_native_min P1). Was
	// shell_ai_orphans.cpp on "CTriggerTraceEnum", then the deferred-navigation
	// hook; both went, so the anchor is a definition the file still holds.
	{ "sourcesharp/.../shell_absent.cpp",          OrphansSource,            "void UTIL_BloodSpray(" },
	{ "sourcesharp/game/server/anyentity.h",                   AnyEntityHeader,          "ANYENTITY_H" },
	// NEITHER QUARANTINE FILE IS HERE ANY MORE. baseentity_sweep_forwarders.cpp
	// and shell_ai_orphans_forwarders.cpp have both been retired.
	{ "sourcesharp/.../shell_shot_path.h",         ShotPathHeader,           "SHELL_SHOT_PATH_H" },
	{ "game/shared/shareddefs.h",                  FarmControlHeader,        "SHAREDDEFS_H" },
	// RE-ANCHORED, BOTH OF THEM. These used to be "PhysicsStepRunTimestep" and
	// "PerformCustomPhysics", two names from the class bodies that
	// `#ifndef SS_CBASEENTITY_ALIAS` kept out of this build and the close has
	// deleted. Each replacement is a LIVE line from the tail that SURVIVED the
	// class, at preprocessor depth 1 inside nothing but the include guard.
	// (game/server/baseentity.h's row went: it is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6.)
	{ "game/client/c_baseentity.h",                ClientBaseEntityHeader,   "EXTERN_RECV_TABLE(DT_BaseEntity);" },
	{ "public/igameentity.h",                      GameEntityInterfaceHeader, "abstract_class IGameEntity" },
	{ "sourcesharp/game/server/ss_world.h",                       WorldHeader,              "class CWorld" },
};
const size_t kNumAnchors = sizeof( kAnchors ) / sizeof( kAnchors[ 0 ] );

} // namespace

//=============================================================================
// THE FILES WERE REALLY READ.
//=============================================================================

TEST( RemovalSetTailFixture, EveryFileWasReallyOpenedAndIsTheRightOne )
{
	ASSERT_TRUE( SrcRoot() != NULL )
		<< "no src/ tree found from the working directory; every assertion "
		<< "below would pass vacuously on empty strings.";

	for ( size_t i = 0; i < kNumAnchors; i++ )
	{
		const std::string &s = kAnchors[ i ].pfnSource();

		EXPECT_FALSE( s.empty() ) << kAnchors[ i ].pszWhat << " did not load";
		EXPECT_NE( std::string::npos, s.find( kAnchors[ i ].pszAnchor ) )
			<< kAnchors[ i ].pszWhat << " loaded but does not contain its anchor \""
			<< kAnchors[ i ].pszAnchor << "\". Either the wrong file was opened, "
			<< "or the thing this test is about was renamed -- in which case fix "
			<< "the anchor deliberately rather than deleting it.";
	}

	// THE SERVER ENTITY HEADER IS GONE, which is the strong form of every
	// "not declared again in baseentity.h" row this file used to read it for.
	// game/server/baseentity.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6. Anchored on the farm
	// control header, which the loop above has just proved readable.
	EXPECT_NE( std::string::npos, FarmControlHeader().find( "SHAREDDEFS_H" ) )
		<< "game/shared/shareddefs.h did not read, so the absence below "
		<< "proves nothing about the farm.";
	EXPECT_TRUE( ReadSrcFile( "game/server/baseentity.h" ).empty() )
		<< "game/server/baseentity.h is readable from the farm again. Nothing "
		<< "compiles it; if it is back, the rows retired from this file need "
		<< "their subject read again.";
}

//=============================================================================
// THE COUNTS. The removal set's tail, per file.
//=============================================================================

TEST( RemovalSetTail, OrphansDefinesNoMemberAtAllAndStillDefinesTheThreeLifts )
{
	// WAS OrphansDefinesOnlyTheOneThatIsBlocked. The one was
	// HandleShotImpactingGlass, and it was only ever compiled by the builds
	// outside `#ifndef SOURCESHARP`; the close deleted that arm.
	//
	// A ZERO IS HALF A FACT, so the other half is here rather than only in
	// TheThreeLiftedOrphansAreServerOnly: the file still defines the three
	// FREE functions the lift produced. Without that, this test also passes
	// over a shell_ai_orphans.cpp that was emptied or deleted.
	const std::vector< std::string > defs = MemberDefinitions( OrphansSource() );

	EXPECT_EQ( kOrphansMembers, defs.size() )
		<< "shell_ai_orphans.cpp defines " << defs.size() << " entity-class "
		<< "members out of line; after the three lifts and the fourth's guarded "
		<< "arm going, it defines exactly " << kOrphansMembers << ". There is no "
		<< "entity class in this tree to define a member OF.";

	// THE SECOND HALF INVERTED IN PHASE 1B, and a zero is still half a fact.
	//
	// It used to require the three FREE functions to be PRESENT, because the
	// lift had moved the scope and not the body. All three are now gone: no
	// object on the server link line imported any of them, measured on a cold
	// build over the objects the ninja graph's link line names (with the
	// objects-on-disk count checked against it, so no evicted translation
	// unit's object could answer).
	//
	//   ./1b-census.sh sym server EntityShot_TraceAttackToTriggers  ->  0
	//   ./1b-census.sh sym server EntityShot_CreateBubbleTrail      ->  0
	//   ./1b-census.sh sym server EntityShot_UpdateStatistics       ->  0
	//
	// So the fact that keeps this from passing over an emptied file is no
	// longer "the three are here"; it is the anchor above, which requires
	// UTIL_BloodSpray, a definition the file still holds now that the
	// deferred-navigation hook (plan_server_native_min P1), RadiusDamage
	// (plan_server_native_v2 T2) and ss::SetPlayerSurface (T4g) are gone.
	//
	// BULLETS NO LONGER HIT TRIGGERS. plan_shim.md §10 records that as an open
	// question with the experiment that settles it: shoot a trigger_hurt with a
	// managed weapon on the live gate. If they must, the three come back WITH
	// callers, and this assertion inverts again.
	for ( size_t i = 0; i < kNumOrphanLifts; i++ )
	{
		EXPECT_EQ( std::string::npos,
		           StripComments( OrphansSource() ).find(
		               std::string( kOrphanLifts[ i ].pszFree ) + "(" ) )
			<< kOrphanLifts[ i ].pszFree << " is back in shell_absent.cpp. "
			<< "Phase 1b deleted all three because nothing on the server link "
			<< "line imported them; if a caller has returned, say which in the "
			<< "commit rather than restoring the bodies on their own.";
	}
}

//=============================================================================
// THE LIFTS LEFT THE CLASS SCOPE, AND LANDED IN THE QUARANTINE.
//=============================================================================

TEST( RemovalSetTail, OrphansNoLongerDefinesTheThree )
{
	const std::vector< std::string > defs = MemberDefinitions( OrphansSource() );
	const std::set< std::string > setDefs( defs.begin(), defs.end() );

	for ( size_t i = 0; i < kNumOrphanLifts; i++ )
	{
		EXPECT_EQ( 0u, setDefs.count( kOrphanLifts[ i ].pszMember ) )
			<< "shell_ai_orphans.cpp still defines CBaseEntity::"
			<< kOrphanLifts[ i ].pszMember << " out of line. It was lifted to "
			<< kOrphanLifts[ i ].pszFree << "; a class-scope definition here is "
			<< "a COMPILE error under `using CBaseEntity = IGameEntity;`.";
	}
}

//-----------------------------------------------------------------------------
// EveryForwarderIsOnlyAForward WAS HERE. Both quarantines it read are retired,
// so there is no forwarder left in the tail to hold to a plain forward and the
// assertion has nowhere to point. SweepQuarantineIsRetired and
// OrphanQuarantineIsRetired replaced it: the question is no longer "does the
// forwarder still only forward" but "did the name really go, and do the call
// sites really name the free function".
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The free functions are declared where they were said to be, and defined in
// the file the lift said kept the body.
//-----------------------------------------------------------------------------
TEST( RemovalSetTail, HeadersCarryEveryFreeFunction )
{
	// STILL TRUE, AND NOW IT IS A DANGLING DECLARATION, which is deliberate and
	// is the safe direction. Phase 1b deleted all three DEFINITIONS (0 importers
	// each); shell_shot_path.h still declares them, and game/server/anyentity.h
	// still has the inline CBaseEntity::TraceAttackToTriggers that calls the
	// first. Neither file is Phase 1b's to edit. Nothing calls that inline, which
	// is why the server links -- and the next translation unit that does call it
	// gets a link error naming the symbol, which is exactly the loud failure a
	// declaration left standing is for. See util.h's UTIL_RestartAmbientSounds
	// for the same disposition.
	for ( size_t i = 0; i < kNumOrphanLifts; i++ )
	{
		EXPECT_TRUE( IsDeclaredAtAll( ShotPathHeader(), kOrphanLifts[ i ].pszFree ) )
			<< "shell_shot_path.h does not declare " << kOrphanLifts[ i ].pszFree;
	}

	// physics_sweep.h's PhysicsSweep_CheckEntity declaration went with the
	// header (plan_server_native_min P1): it had no definition.
}

//=============================================================================
// THE STATIC DATA MEMBER. The failure no consumer count can see, because
// `nm ... $2=="T"` reads TEXT symbols only.
//=============================================================================

TEST( RemovalSetTail, NoFileInTheTailDefinesAStaticDataMember )
{
	const Anchor_t *pFiles = kAnchors;

	for ( size_t i = 0; i < kNumAnchors; i++ )
	{
		const std::vector< std::string > data = StaticDataMembers( pFiles[ i ].pfnSource() );

		std::string strFound;
		for ( size_t k = 0; k < data.size(); k++ )
		{
			if ( !strFound.empty() ) strFound += ", ";
			strFound += data[ k ];
		}

		EXPECT_TRUE( data.empty() )
			<< pFiles[ i ].pszWhat << " defines a static DATA member of "
			<< "CBaseEntity: " << strFound << ".\n"
			<< "That does not fail to LINK -- it stops the file PARSING, "
			<< "everywhere at once, the moment step 4 writes "
			<< "`using CBaseEntity = IGameEntity;`. It is strictly worse than "
			<< "the undefined symbols step 3 tracks, and no defined-symbol count "
			<< "can see it. The storage has to leave the class, the way "
			<< "m_bAllowPrecache did into entity_shared_statics.h.";
	}
}

//=============================================================================
// THE BLOCKED ONES, AND WHY. These are the categories that caught real lanes,
// so they are pinned as facts rather than left as prose in a commit message.
//=============================================================================

TEST( RemovalSetTail, TheEmptyTouchHandlersAreVirtualOnTheInterfaceThatOwnsThemNow )
{
	// WAS TheClientsEmptyTouchHandlersAreVirtualToo, which read `virtual` off
	// the two class bodies. Those went with `#ifndef SS_CBASEENTITY_ALIAS`.
	//
	// The claim is the same one and its subject moved to a file the build
	// compiles: StartTouch, Touch and EndTouch are PURE virtual on IGameEntity.
	// That is a stronger statement than the one this test used to make -- a
	// pure virtual is a slot every implementation MUST fill -- and it is the
	// reason these three still cannot be lifted, however empty their bodies in
	// physics_main_client.cpp look.
	//
	// Asserted as exact text rather than through IsDeclaredVirtual, because the
	// detector requires a `;` straight after the argument list and a pure
	// virtual has `= 0` in between. Using the detector here would report all
	// three absent, which is the direction that reads as a passing absence.
	const std::string strIface = Squeezed( StripComments( GameEntityInterfaceHeader() ) );

	const char *const kNames[] = { "StartTouch", "Touch", "EndTouch" };
	for ( size_t i = 0; i < 3; i++ )
	{
		const std::string strWanted =
			std::string( "virtualvoid" ) + kNames[ i ] + "(constToucher_t&toucher)=0;";
		EXPECT_NE( std::string::npos, strIface.find( strWanted ) )
			<< "IGameEntity no longer declares " << kNames[ i ] << " as a pure "
			<< "virtual. It is defined in physics_main_client.cpp with an empty "
			<< "body and would look liftable; a slot is why it is not.";

		EXPECT_FALSE( IsDeclaredAtAll( ClientBaseEntityHeader(), kNames[ i ] ) )
			<< "game/client/c_baseentity.h declares " << kNames[ i ] << " again.";
		// game/server/baseentity.h's row is retired: that header is compiled
		// by nothing since plan_server_novalve.md P5 and left the farm in P6.
	}
}

//-----------------------------------------------------------------------------
// HandleShotImpactingGlass is the fourth orphan and names `this` outright.
//-----------------------------------------------------------------------------
TEST( RemovalSetTail, HandleShotImpactingGlassWasGuardedOutAndWasNotLifted )
{
	// WAS HandleShotImpactingGlassStillNamesThis, which read
	// `info.m_pAttacker ? info.m_pAttacker : this` out of the body in
	// shell_ai_orphans.cpp -- the expression that names `this` and is the whole
	// reason this one was NOT lifted with the other three.
	//
	// That definition was inside `#ifndef SOURCESHARP` and the close deleted the
	// arm, so the body is not there to read. The FACT that mattered is the
	// refusal, and the refusal is checkable without the body: it was never
	// lifted, and nothing lifted it afterwards either. Lifting it would have
	// meant giving a free EntityShot_HandleShotImpactingGlass an IGameEntity
	// shooter plus free spellings of DoImpactEffect and FireBullets -- two
	// members, one of them virtual -- for a function nothing can reach.
	const std::string strOrphans = StripComments( OrphansSource() );

	EXPECT_EQ( std::string::npos, strOrphans.find( "EntityShot_HandleShotImpactingGlass" ) )
		<< "somebody lifted HandleShotImpactingGlass after all. Armed it has NO "
		<< "CALLER -- its only call site is game/shared/baseentity_shared.cpp, "
		<< "which is in both removal sets -- so a free spelling of it buys a "
		<< "free DoImpactEffect and a free FireBullets for nothing.";

	EXPECT_EQ( std::string::npos, strOrphans.find( "HandleShotImpactingGlass(" ) )
		<< "shell_ai_orphans.cpp defines or calls HandleShotImpactingGlass in "
		<< "code again.";

	// The live successor, and the reason the two absences above are not passing
	// over an empty file: the three that WERE lifted are still declared, and
	// the one that took this one's place in the shooting path is not among
	// them. shell_shot_path.h is the ruling, and it is a compiled header.
	for ( size_t i = 0; i < kNumOrphanLifts; i++ )
	{
		EXPECT_NE( std::string::npos,
		           StripComments( ShotPathHeader() ).find( kOrphanLifts[ i ].pszFree ) )
			<< kOrphanLifts[ i ].pszFree << " is gone from shell_shot_path.h, "
			<< "which is where the three lifts and the fourth's refusal are both "
			<< "recorded.";
	}
}

//-----------------------------------------------------------------------------
// The three lifted orphans are server-only: none is declared in the client
// header. This is the OTHER half of check-both-headers -- read server-only they
// look liftable AND ARE, but only because the client declares nothing.
//-----------------------------------------------------------------------------
TEST( RemovalSetTail, TheThreeLiftedOrphansAreServerOnly )
{
	// READ THE ASSERTIONS, NOT THE NAME. Server-only is still the claim. It used
	// to be read as "declared in baseentity.h, absent from c_baseentity.h";
	// baseentity.h's class body went with `#ifndef SS_CBASEENTITY_ALIAS`, so the
	// present half has no subject. The absent half is unchanged, and the
	// present half moves to the header that actually declares the lifted forms
	// -- sourcesharp/game/server/shell_shot_path.h, which is server-only by
	// construction: it lives under game/server and no client TU includes it.
	const std::string strShotPath = StripComments( ShotPathHeader() );

	for ( size_t i = 0; i < kNumOrphanLifts; i++ )
	{
		// game/server/baseentity.h's row is retired: that header is compiled
		// by nothing since plan_server_novalve.md P5 and left the farm in P6.
		EXPECT_FALSE( IsDeclaredAtAll( ClientBaseEntityHeader(), kOrphanLifts[ i ].pszMember ) )
			<< "c_baseentity.h has GROWN a declaration of "
			<< kOrphanLifts[ i ].pszMember << ". The lift was justified partly "
			<< "on these being server-only; if the client declares one now, "
			<< "check whether it is virtual there before trusting the lift.";

		EXPECT_TRUE( IsDeclaredAtAll( strShotPath, kOrphanLifts[ i ].pszFree ) )
			<< kOrphanLifts[ i ].pszFree << " is not declared in "
			<< "shell_shot_path.h. That is the server-only header the lifted "
			<< "forms live in, and without the declaration the two EXPECT_FALSEs "
			<< "above are passing over a function nobody can call.";

		EXPECT_FALSE( IsDeclaredVirtual( strShotPath, kOrphanLifts[ i ].pszFree ) )
			<< kOrphanLifts[ i ].pszFree << " is declared virtual in "
			<< "shell_shot_path.h. A virtual name IS a vtable slot, a free "
			<< "function is not one, and the lift is then wrong.";
	}
}

//=============================================================================
// THE DETECTORS ANSWER BOTH WAYS. Without these, every empty-list and
// zero-count assertion above passes for free.
//=============================================================================

TEST( RemovalSetTailFixture, ExtractorTellsADefinitionFromACall )
{
	const std::string strFixture =
		"void CBaseEntity::Defined( int a ) { Called(); }\n"
		"void Other() { CBaseEntity::NotDefined( 1 ); }\n"
		"bool CBaseEntity::ConstDefined() const { return true; }\n";

	const std::vector< std::string > defs = MemberDefinitionsIn( strFixture, kServerScope );
	const std::set< std::string > setDefs( defs.begin(), defs.end() );

	EXPECT_EQ( 2u, defs.size() );
	EXPECT_EQ( 1u, setDefs.count( "Defined" ) );
	EXPECT_EQ( 1u, setDefs.count( "ConstDefined" ) )
		<< "the extractor lost a definition with a trailing `const`";
	EXPECT_EQ( 0u, setDefs.count( "NotDefined" ) )
		<< "the extractor counted a CALL as a definition";
}

TEST( RemovalSetTailFixture, ExtractorHandlesTheClientScopeToo )
{
	const std::string strFixture =
		"void C_BaseEntity::ClientDefined( int a ) { }\n"
		"void Other() { C_BaseEntity::ClientCalled( 1 ); }\n";

	const std::vector< std::string > defs = MemberDefinitionsIn( strFixture, kClientScope );
	EXPECT_EQ( 1u, defs.size() );
	EXPECT_EQ( "ClientDefined", defs.empty() ? std::string() : defs[ 0 ] );

	// And the server scope must NOT match the underscored spelling.
	EXPECT_EQ( 0u, MemberDefinitionsIn( strFixture, kServerScope ).size() )
		<< "the server scope matched inside `C_BaseEntity::`, which would make "
		<< "the two counts overlap and double-count every client definition.";
}

TEST( RemovalSetTailFixture, ExtractorIgnoresProse )
{
	const std::string strFixture =
		"// CBaseEntity::MentionedInAComment( void ) { }\n"
		"/* void CBaseEntity::AlsoInABlockComment() { } */\n"
		"void CBaseEntity::Real() { }\n";

	const std::vector< std::string > defs = MemberDefinitionsIn( strFixture, kServerScope );
	EXPECT_EQ( 1u, defs.size() )
		<< "the extractor read a definition out of a comment. Every file under "
		<< "test is prose about exactly these names.";
	EXPECT_EQ( "Real", defs.empty() ? std::string() : defs[ 0 ] );
}

TEST( RemovalSetTailFixture, StaticDataDetectorSeesOneWhenThereIsOne )
{
	// Without this, NoFileInTheTailDefinesAStaticDataMember is the purest kind
	// of check that cannot fail: an empty-list assertion over a detector that
	// never finds anything.
	const std::string strPositive =
		"bool CBaseEntity::m_bAllowPrecache = false;\n"
		"int CBaseEntity::sm_nCount;\n"
		"const char *CBaseEntity::sm_szNames[4] = { 0 };\n";

	const std::vector< std::string > data = StaticDataMembersIn( strPositive, kServerScope );
	const std::set< std::string > setData( data.begin(), data.end() );

	EXPECT_EQ( 3u, data.size() );
	EXPECT_EQ( 1u, setData.count( "m_bAllowPrecache" ) );
	EXPECT_EQ( 1u, setData.count( "sm_nCount" ) )
		<< "the detector missed a static data member with no initialiser";
	EXPECT_EQ( 1u, setData.count( "sm_szNames" ) )
		<< "the detector missed a static data ARRAY member";
}

TEST( RemovalSetTailFixture, StaticDataDetectorIgnoresReadsWritesAndCalls )
{
	const std::string strNegative =
		"void CBaseEntity::SetAllowPrecache( bool allow )\n"
		"{\n"
		"    CBaseEntity::m_bAllowPrecache = allow;\n"      // a WRITE, no type on the line
		"    int n = CBaseEntity::m_nSeed;\n"               // a READ, but the line HAS a type
		"    CBaseEntity::SomeFunction( n );\n"             // a call
		"}\n"
		"void Foo() { CBaseEntity::m_bOnOneLine = true; }\n" // statement sharing the header's line
		"bool CBaseEntity::IsPrecacheAllowed() { return false; }\n";

	const std::vector< std::string > data = StaticDataMembersIn( strNegative, kServerScope );

	std::string strFound;
	for ( size_t i = 0; i < data.size(); i++ )
	{
		if ( !strFound.empty() ) strFound += ", ";
		strFound += data[ i ];
	}

	EXPECT_TRUE( data.empty() )
		<< "the static-data detector counted a read, a write or a function "
		<< "definition as a static data member: " << strFound << ".\n"
		<< "The last two lines are the ones baseentity_shared_lift_test's "
		<< "detector gets wrong -- `int n = CBaseEntity::m_nSeed;` and a "
		<< "statement sharing a line with a function header both have an "
		<< "identifier before the scope, so a lead-has-an-ident-char test "
		<< "accepts them. This one requires the lead to look like a TYPE.";
}

TEST( RemovalSetTailFixture, StaticDataDetectorStillSeesATemplatedType )
{
	// The lead filter must not reject a real static data member whose type
	// carries commas and angle brackets -- that would be a false NEGATIVE, and
	// this detector's whole job is to not miss one.
	const std::string strPositive =
		"CUtlMap< int, int > CBaseEntity::sm_map;\n"
		"CUtlVector< CBaseEntity * > CBaseEntity::sm_list;\n";

	const std::vector< std::string > data = StaticDataMembersIn( strPositive, kServerScope );
	const std::set< std::string > setData( data.begin(), data.end() );

	EXPECT_EQ( 1u, setData.count( "sm_map" ) )
		<< "the lead filter rejected a static data member with a templated type";
	EXPECT_EQ( 1u, setData.count( "sm_list" ) );
}

TEST( RemovalSetTailFixture, StaticDataDetectorIgnoresProse )
{
	const std::string strFixture =
		"// bool CBaseEntity::m_bAllowPrecache = false;\n"
		"/* int CBaseEntity::sm_nCount; */\n";

	EXPECT_TRUE( StaticDataMembersIn( strFixture, kServerScope ).empty() );
}

TEST( RemovalSetTailFixture, VirtualDetectorAnswersBothWays )
{
	const std::string strFixture =
		"class X {\n"
		"	virtual void			IsVirtual( int a );\n"
		"	void					NotVirtual( int a );\n"
		"	int						m_virtualLookingMember;\n"
		"};\n";

	EXPECT_TRUE(  IsDeclaredVirtual( strFixture, "IsVirtual" ) );
	EXPECT_FALSE( IsDeclaredVirtual( strFixture, "NotVirtual" ) )
		<< "the virtual detector answers TRUE for everything, so every "
		<< "EXPECT_TRUE above passes for free";
	EXPECT_FALSE( IsDeclaredVirtual( strFixture, "m_virtualLookingMember" ) )
		<< "the virtual detector matched a DATA member, not a function";

	EXPECT_TRUE(  IsDeclaredAtAll( strFixture, "NotVirtual" ) );
	EXPECT_FALSE( IsDeclaredAtAll( strFixture, "Absent" ) );
}

TEST( RemovalSetTailFixture, VirtualDetectorSeesTheRealHeadersBothWays )
{
	// RE-POINTED. This was proved against the two entity headers, whose class
	// bodies are gone, so a detector that answered FALSE for everything would
	// have satisfied it. The two replacements both have real declarations of
	// both kinds, and game/server/world.h is LIVE -- mapentities.cpp and
	// physics.cpp include it -- so what the detector is proved on is text the
	// compiler really sees.
	EXPECT_TRUE( IsDeclaredVirtual( WorldHeader(), "DecalTrace" ) )
		<< "the virtual detector cannot find `virtual void DecalTrace( ... );` "
		<< "in sourcesharp/game/server/ss_world.h, so every EXPECT_TRUE written on it passes "
		<< "for free.";
	EXPECT_FALSE( IsDeclaredVirtual( WorldHeader(), "ReportNetState" ) )
		<< "ReportNetState is declared plain in world.h; a detector calling it "
		<< "virtual is answering TRUE for everything.";

	EXPECT_TRUE( IsDeclaredAtAll( WorldHeader(), "ReportNetState" ) )
		<< "the declaration detector cannot see a declaration that is there, so "
		<< "every EXPECT_FALSE written on it passes for free.";
	EXPECT_FALSE( IsDeclaredAtAll( WorldHeader(), "NoSuchMemberAnywhere9d3f" ) );
}
