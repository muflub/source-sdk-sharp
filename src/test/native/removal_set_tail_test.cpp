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
// ServerBaseEntityHeader() WAS HERE. game/server/baseentity.h is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6; its absence
// is pinned, anchored, in EveryFileWasReallyOpenedAndIsTheRightOne.

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

    // was 4, then 1

struct Lifted_t
{
	const char *pszMember;
	const char *pszFree;
};

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

} // namespace

//=============================================================================
// THE FILES WERE REALLY READ.
//=============================================================================

//=============================================================================
// THE COUNTS. The removal set's tail, per file.
//=============================================================================

//=============================================================================
// THE LIFTS LEFT THE CLASS SCOPE, AND LANDED IN THE QUARANTINE.
//=============================================================================

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
//=============================================================================
// THE STATIC DATA MEMBER. The failure no consumer count can see, because
// `nm ... $2=="T"` reads TEXT symbols only.
//=============================================================================

//=============================================================================
// THE BLOCKED ONES, AND WHY. These are the categories that caught real lanes,
// so they are pinned as facts rather than left as prose in a commit message.
//=============================================================================

//-----------------------------------------------------------------------------
// HandleShotImpactingGlass is the fourth orphan and names `this` outright.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The three lifted orphans are server-only: none is declared in the client
// header. This is the OTHER half of check-both-headers -- read server-only they
// look liftable AND ARE, but only because the client declares nothing.
//-----------------------------------------------------------------------------
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

