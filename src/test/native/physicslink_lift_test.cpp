//========= SourceSharp — the eight physics statics really left the class ====//
//
// A SOURCE SCANNER, like soundemitter_lift_test and precache_model_move_test,
// and for the same reason: the claim is about the TEXT of files that need a
// whole engine to run, so the text is what is read. buildcpptests links each
// test binary against gtest and nothing else, so calling the real
// Physics_ClipVelocity here is not available; re-implementing it in the test to
// "check the maths" would be a check that cannot fail, since it would only
// compare a copy against itself.
//
// WHAT WAS DONE. game/shared/physics_main_shared.cpp defined FIFTY CBaseEntity
// members out of line. EIGHT of them take no `this`:
//
//   SEVEN are already declared `static`, declaration by declaration, in
//   game/server/baseentity.h (1181, 1186-1195) and game/client/c_baseentity.h
//   (964, 947-956) -- PhysicsRemoveTouchedList, PhysicsNotifyOtherOfUntouch,
//   PhysicsRemoveToucher, PhysicsNotifyOtherOfGroundRemoval,
//   PhysicsRemoveGround, PhysicsRemoveGroundList, GetTouchTrace. A static
//   member takes no `this`; the class scope was only ever being used as a
//   namespace.
//
//   The EIGHTH, PhysicsClipVelocity, is NOT declared static, but its body is
//   pure arithmetic over its four arguments and touches no member.
//
// Their bodies are now the free functions PhysicsLink_* / Physics_ClipVelocity
// declared in game/shared/physicslink.h and defined, UNMOVED, in
// physics_main_shared.cpp -- unmoved because all six link functions reach
// file-static state in that translation unit (the g_EdictTouchLinks /
// g_EntityGroundLinks pools, g_bCleanupDatObject, linksallocated,
// FreeTouchLink, FreeGroundLink; and, for GetTouchTrace, g_TouchTrace itself).
// THE EIGHT CLASS-SCOPE NAMES ARE GONE FROM THIS BUILD. They spent one phase
// as one-line forwarders quarantined together in
// game/shared/baseentity_physics_forwarders.cpp; that file is retired.
// rta_referrers.sh named the demand exactly -- SIX referring objects on the
// server link line and THREE on the client -- and all 22 call sites across
// eight files now name the free function. Two of the eight names,
// PhysicsRemoveGround and GetTouchTrace, had NO caller in this build at all.
//
// The DECLARATIONS stay in baseentity.h and c_baseentity.h. Two dozen SDK files
// outside this build reach CBaseEntity::GetTouchTrace() through the class, and
// server_csharp.vpc compiles none of them. The removal set counts out-of-line
// DEFINITIONS, and that is what left.
//
// The remaining FORTY-TWO take a `this` and stayed put.
//
// GetTouchTrace was the LAST one available: the shared-middle-tier lane put
// every non-virtual member of this file, of baseentity_shared.cpp and of
// sound_emitter.cpp through the compiler -- declaring each `static` and
// rebuilding, to a fixed point -- and this was the only body in
// physics_main_shared.cpp that came back needing no `this` and carrying no
// pointer-to-member. See baseentity_shared_lift_test for the other two files.
//
// Measured with the corrected pipeline from plan_baseentity.md Phase 13 note B
// (`nm --defined-only OBJ | awk '$2=="T"{print $3}' | c++filt |
//   grep -c '^C_\?BaseEntity::'`):
//
//   physics_main_shared.o             before 50/50   after 42/42
//   baseentity_physics_forwarders.o                  after  8/8   RETIRED 0/0
//
// 42 + 8 = 50 on both sides: the set of class-scope symbols the link is given
// is UNCHANGED. Nothing moved between translation units and nothing was
// deleted; what moved is the scope each definition is written in.
//
// WHY THIS NEEDS A GUARD, AND IT IS NOT PEDANTRY. Three ways this move rots,
// none of which produces a diagnostic:
//
//   1. Somebody adds logic to a forwarder. Then there are two implementations
//      of unlinking a ground contact: the SDK's unqualified call sites take the
//      forwarder's, the port's own files take the free function's, and they
//      drift. The build stays green in both directions.
//   2. Somebody writes an eighth static member definition back into
//      physics_main_shared.cpp, on the reasonable-looking grounds that all its
//      neighbours are there. Phase 13 step 4 then fails to compile on a file
//      that had been cleared, and the failure names a line nobody connects to
//      this move.
//   3. Somebody moves one of the forty-three NON-static members into the
//      quarantine file to tidy up. It would compile. It would also mean step 3
//      could no longer delete that file wholesale, which is the only reason it
//      exists.
//
// A fourth, specific to this lift: somebody drops the `static` from one of the
// six declarations in baseentity.h. The forwarder still compiles (as a member),
// the free function still compiles, and the PREMISE of the lift -- that these
// take no `this` -- has silently stopped being true. Pinned below.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given. The fixtures at the bottom assert that all four
// files were really opened and are not trivial, and that the definition
// extractor tells a DEFINITION from a CALL -- which matters here, because
// physics_main_shared.cpp contains both: it CALLS the lifted free functions
// from inside other members, and it DEFINES forty-three CBaseEntity members.
//
// SIDE-INDEPENDENCE. The scanner reads text, and the text says `CBaseEntity::`
// on both sides -- game/shared/shared_classnames.h does
// `#define CBaseEntity C_BaseEntity` for the client, which is why the client's
// symbols demangle to `C_BaseEntity::`. So these counts are per-FILE, not
// per-side, and they cover both link lines at once.
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
		if ( i + 1 < s.size() && s[i] == '/' && s[i + 1] == '/' )
		{
			while ( i < s.size() && s[i] != '\n' )
				i++;
			continue;
		}
		if ( i + 1 < s.size() && s[i] == '/' && s[i + 1] == '*' )
		{
			i += 2;
			while ( i + 1 < s.size() && !( s[i] == '*' && s[i + 1] == '/' ) )
				i++;
			i = ( i + 1 < s.size() ) ? i + 2 : s.size();
			continue;
		}
		out += s[i];
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
		const char c = s[i];
		if ( c != ' ' && c != '\t' && c != '\r' && c != '\n' )
			out += c;
	}
	return out;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) || ( c >= '0' && c <= '9' ) || c == '_';
}

//-----------------------------------------------------------------------------
// Skip a balanced parenthesised run starting at strCode[at] == '('. Returns the
// index just past the closing paren, or npos.
//-----------------------------------------------------------------------------
size_t SkipParens( const std::string &strCode, size_t at )
{
	if ( at >= strCode.size() || strCode[at] != '(' )
		return std::string::npos;

	int depth = 0;
	for ( size_t i = at; i < strCode.size(); i++ )
	{
		if ( strCode[i] == '(' )
			depth++;
		else if ( strCode[i] == ')' )
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
	while ( i < s.size() && ( s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' ) )
		i++;
	return i;
}

//-----------------------------------------------------------------------------
// Every member name DEFINED out of line as `CBaseEntity::<Name>( ... ) {`.
//
// The discriminator between a definition and a call is what follows the
// argument list: a definition's next non-space character is '{' (allowing for a
// trailing `const`), a call's is ';' or ',' or ')' or an operator.
//-----------------------------------------------------------------------------
std::vector<std::string> MemberDefinitions( const std::string &strSource )
{
	const std::string strCode = StripComments( strSource );
	const std::string kScope = "CBaseEntity::";

	std::vector<std::string> names;

	for ( size_t pos = strCode.find( kScope ); pos != std::string::npos; pos = strCode.find( kScope, pos + 1 ) )
	{
		// Must not be preceded by an identifier char, or "C_BaseEntity::"
		// and the like would match inside a longer name.
		if ( pos > 0 && IsIdentChar( strCode[pos - 1] ) )
			continue;

		size_t i = pos + kScope.size();
		const size_t nameStart = i;
		while ( i < strCode.size() && IsIdentChar( strCode[i] ) )
			i++;
		if ( i == nameStart )
			continue;

		const std::string strName = strCode.substr( nameStart, i - nameStart );

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[i] != '(' )
			continue;

		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;

		i = SkipSpace( strCode, i );

		// allow a trailing `const`
		if ( strCode.compare( i, 5, "const" ) == 0 && ( i + 5 >= strCode.size() || !IsIdentChar( strCode[i + 5] ) ) )
		{
			i = SkipSpace( strCode, i + 5 );
		}

		if ( i < strCode.size() && strCode[i] == '{' )
			names.push_back( strName );
	}

	return names;
}

//-----------------------------------------------------------------------------
// The body text of one out-of-line CBaseEntity member definition, squeezed of
// whitespace. Empty string if the member is not defined here.
//-----------------------------------------------------------------------------
std::string ForwarderBody( const std::string &strSource, const std::string &strMember )
{
	const std::string strCode = StripComments( strSource );
	const std::string kNeedle = "CBaseEntity::" + strMember;

	for ( size_t pos = strCode.find( kNeedle ); pos != std::string::npos; pos = strCode.find( kNeedle, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[pos - 1] ) )
			continue;

		size_t i = pos + kNeedle.size();
		if ( i < strCode.size() && IsIdentChar( strCode[i] ) )
			continue; // a longer name that merely starts the same way

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[i] != '(' )
			continue;

		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[i] != '{' )
			continue;

		int depth = 0;
		const size_t bodyStart = i + 1;
		for ( size_t j = i; j < strCode.size(); j++ )
		{
			if ( strCode[j] == '{' )
				depth++;
			else if ( strCode[j] == '}' )
			{
				depth--;
				if ( depth == 0 )
					return Squeezed( strCode.substr( bodyStart, j - bodyStart ) );
			}
		}
	}

	return std::string();
}

//-----------------------------------------------------------------------------
// Is `<Name>` declared `static` in this header? Looks for the declaration line
// and checks that `static` precedes the name on it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Is `<Name>` defined at NAMESPACE scope (i.e. `<Name>( ... ) {` with no
// `::` immediately before it)?
//-----------------------------------------------------------------------------
bool DefinedFree( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos; pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[pos - 1] ) )
			continue;
		if ( pos >= 2 && strCode.compare( pos - 2, 2, "::" ) == 0 )
			continue; // qualified -- that is the forwarder, not the free one

		size_t i = pos + strName.size();
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[i] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i < strCode.size() && strCode[i] == '{' )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// The seven, and the free function each one forwards to.
//-----------------------------------------------------------------------------
struct Lifted_t
{
	const char *pszMember;
	const char *pszFree;
	bool bDeclaredStatic; // six are; PhysicsClipVelocity is not
};

} // namespace

//=============================================================================
// The split: 42 stayed, 8 left.
//=============================================================================

//-----------------------------------------------------------------------------
// f-physics: WHAT MAKES THE TWO ZERO ROWS ABOVE MEAN "REFUSED" AND NOT "LOST".
//
// `TheCallersNameTheFreeFunctions` used to read 2 for each `physics.cpp`. Both
// are now 0, and its own failure message says why that number alone cannot be
// trusted: "FEWER means a call was LOST rather than rewritten -- for a link
// walker that means links never torn down, which no test downstream of here
// would notice." Lowering the expectation without this fact would retire
// exactly the check that message is about.
//
// So the zero is paid for here. Each bridge must:
//   * carry the refusal TYPE, not a bare `(void)` stub -- §7's "a reason, never
//     a silent no-op";
//   * name BOTH halves, start and end, because refusing one and keeping the
//     other is the leak the message describes; and
//   * still route the trigger path through PhysicsOwner_t, which is a different
//     mechanism and must not have gone with them.
//
// The three are checked separately so a partial revert is a specific failure.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SERVER'S CONTACT TOUCH, PORTED: both halves, both directions.
//
// Stock's PhysicsMarkEntitiesAsTouchingEventDriven links EACH entity to the
// other, and PhysicsLink_NotifyOtherOfUntouch takes both links down. A port
// that built the links without the teardown leaks touches that never end (no
// OnEndTouch); one that marked only one side leaves the other entity deaf.
//
// MANAGED SINCE plan_physics_managed B2.2: vphysics calls managed's own
// IPhysicsCollisionEvent table, which holds the pair (SolverHeldOverlaps) on
// StartTouch and releases it on the last EndTouch; the held pair is reported
// into every frame's overlap diff, which touches both entities. Native's
// DispatchStartTouch / DispatchEndTouch are deleted. Whole calls are the
// needles; the live gate's contact-touch step is the behaviour.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ...and the TRIGGER path, a different mechanism on the same set. Without
// this, dropping both touch mechanisms at once would pass every check above.
//-----------------------------------------------------------------------------
//=============================================================================
// "Every forwarder is a forward and nothing more" WAS HERE, reading the
// quarantine file. There are no forwarders left. What it defended -- that the
// SDK's unqualified call sites and the port's qualified ones cannot stop
// agreeing, silently and in both directions -- is now true by construction:
// there is one implementation and one way to reach it, which
// QuarantineIsRetired and TheCallersNameTheFreeFunctions pin instead.
//=============================================================================

//=============================================================================
// The free functions exist where they are supposed to.
//=============================================================================

//=============================================================================
// The PREMISE of the lift: seven of the eight really are declared `static`.
//
// If somebody drops the `static`, the forwarder still compiles as a member and
// the free function still compiles, and the reason these were safe to lift has
// silently stopped being true.
//=============================================================================

//=============================================================================
// THE SCANNER IS ITSELF CHECKED.
//
// Everything above is an assertion about strings. If the files never opened, or
// the extractor never matched anything, every one of those assertions that is
// phrased as "does not contain" passes for free.
//=============================================================================

TEST( PhysicsLinkLiftFixture, ExtractorTellsADefinitionFromACall )
{
	const std::string strFixture = "void CBaseEntity::RealDefinition( int a )\n"
								   "{\n"
								   "    CBaseEntity::NotADefinition( a );\n"
								   "    x = CBaseEntity::AlsoNotOne( a ) + 1;\n"
								   "}\n"
								   "bool CBaseEntity::ConstDefinition( void ) const\n"
								   "{\n"
								   "    return true;\n"
								   "}\n";

	const std::vector<std::string> defs = MemberDefinitions( strFixture );
	const std::set<std::string> setDefs( defs.begin(), defs.end() );

	EXPECT_EQ( 2u, defs.size() );
	EXPECT_EQ( 1u, setDefs.count( "RealDefinition" ) );
	EXPECT_EQ( 1u, setDefs.count( "ConstDefinition" ) ) // trailing const
		<< "a `const` member definition was missed; the real file has several";
	EXPECT_EQ( 0u, setDefs.count( "NotADefinition" ) ) << "a CALL was counted as a definition";
	EXPECT_EQ( 0u, setDefs.count( "AlsoNotOne" ) ) << "a call in an expression was counted as a definition";
}

TEST( PhysicsLinkLiftFixture, ExtractorIgnoresProse )
{
	const std::string strFixture = "// CBaseEntity::MentionedInAComment( void )\n"
								   "/* CBaseEntity::AlsoInAComment( void )\n"
								   "{\n"
								   "}\n"
								   "*/\n";

	EXPECT_TRUE( MemberDefinitions( strFixture ).empty() )
		<< "a name written in prose was counted; both files under test are "
		<< "full of exactly that, being about exactly this";
}

TEST( PhysicsLinkLiftFixture, ForwarderBodyExtractorWorksOnFixtures )
{
	const std::string strPositive = "void CBaseEntity::Thin( int a )\n"
									"{\n"
									"    Free_Thin( a );\n"
									"}\n";

	const std::string strGrown = "void CBaseEntity::Thick( int a )\n"
								 "{\n"
								 "    if ( a ) return;\n"
								 "    Free_Thick( a );\n"
								 "}\n";

	// Each body is bound to a local FIRST. Calling ForwarderBody() twice inside
	// one std::count() would take begin() and end() from two different
	// temporaries, which is undefined behaviour -- and did in fact miscount
	// before this was fixed.
	const std::string strThin = ForwarderBody( strPositive, "Thin" );
	const std::string strThick = ForwarderBody( strGrown, "Thick" );

	EXPECT_EQ( "Free_Thin(a);", strThin );
	EXPECT_EQ( 1, std::count( strThin.begin(), strThin.end(), ';' ) );

	// The negative the real test relies on: a grown body has >1 statement.
	EXPECT_EQ( 2, std::count( strThick.begin(), strThick.end(), ';' ) )
		<< "the >1-semicolon rule cannot catch a grown forwarder";

	EXPECT_TRUE( ForwarderBody( strPositive, "Absent" ).empty() );
}

TEST( PhysicsLinkLiftFixture, FreeDefinitionDetectorWorksOnFixtures )
{
	const std::string strFree = "void Free_Thing( int a )\n{\n}\n";
	const std::string strMember = "void CBaseEntity::Free_Thing( int a )\n{\n}\n";
	const std::string strDecl = "void Free_Thing( int a );\n";

	EXPECT_TRUE( DefinedFree( strFree, "Free_Thing" ) );
	EXPECT_FALSE( DefinedFree( strMember, "Free_Thing" ) ) << "a class-scope definition was read as a free one, so "
														   << "SharedFileDefinesEveryFreeFunction cannot fail";
	EXPECT_FALSE( DefinedFree( strDecl, "Free_Thing" ) ) << "a declaration was read as a definition";
}
