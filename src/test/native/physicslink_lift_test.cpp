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

const std::string &PhysicsLinkHeader()
{
	static const std::string s = ReadGameFile( "../sourcesharp/game/shared/physicslink.h" );
	return s;
}

const std::string &ServerBaseEntityHeader()
{
	static const std::string s = ReadGameFile( "server/baseentity.h" );
	return s;
}

const std::string &ClientBaseEntityHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity.h" );
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

//-----------------------------------------------------------------------------
// Skip a balanced parenthesised run starting at strCode[at] == '('. Returns the
// index just past the closing paren, or npos.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// Every member name DEFINED out of line as `CBaseEntity::<Name>( ... ) {`.
//
// The discriminator between a definition and a call is what follows the
// argument list: a definition's next non-space character is '{' (allowing for a
// trailing `const`), a call's is ';' or ',' or ')' or an operator.
//-----------------------------------------------------------------------------
std::vector< std::string > MemberDefinitions( const std::string &strSource )
{
	const std::string strCode = StripComments( strSource );
	const std::string kScope = "CBaseEntity::";

	std::vector< std::string > names;

	for ( size_t pos = strCode.find( kScope ); pos != std::string::npos;
	      pos = strCode.find( kScope, pos + 1 ) )
	{
		// Must not be preceded by an identifier char, or "C_BaseEntity::"
		// and the like would match inside a longer name.
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
// The body text of one out-of-line CBaseEntity member definition, squeezed of
// whitespace. Empty string if the member is not defined here.
//-----------------------------------------------------------------------------
std::string ForwarderBody( const std::string &strSource, const std::string &strMember )
{
	const std::string strCode = StripComments( strSource );
	const std::string kNeedle = "CBaseEntity::" + strMember;

	for ( size_t pos = strCode.find( kNeedle ); pos != std::string::npos;
	      pos = strCode.find( kNeedle, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + kNeedle.size();
		if ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			continue;   // a longer name that merely starts the same way

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;

		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '{' )
			continue;

		int depth = 0;
		const size_t bodyStart = i + 1;
		for ( size_t j = i; j < strCode.size(); j++ )
		{
			if ( strCode[ j ] == '{' ) depth++;
			else if ( strCode[ j ] == '}' )
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
bool DeclaredStatic( const std::string &strHeader, const std::string &strName )
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

		// must be a declaration: an argument list then a ';'
		size_t i = SkipSpace( strCode, after );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != ';' )
			continue;

		// walk back to the start of the line and look for `static`
		size_t lineStart = strCode.rfind( '\n', pos );
		lineStart = ( lineStart == std::string::npos ) ? 0 : lineStart + 1;
		const std::string strLine = strCode.substr( lineStart, pos - lineStart );
		if ( strLine.find( "static" ) != std::string::npos )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// Is `<Name>` defined at NAMESPACE scope (i.e. `<Name>( ... ) {` with no
// `::` immediately before it)?
//-----------------------------------------------------------------------------
bool DefinedFree( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		if ( pos >= 2 && strCode.compare( pos - 2, 2, "::" ) == 0 )
			continue;   // qualified -- that is the forwarder, not the free one

		size_t i = pos + strName.size();
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i < strCode.size() && strCode[ i ] == '{' )
			return true;
	}
	return false;
}

bool DeclaredInHeader( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		size_t i = pos + strName.size();
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i < strCode.size() && strCode[ i ] == ';' )
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
	bool        bDeclaredStatic;   // six are; PhysicsClipVelocity is not
};

const Lifted_t kLifted[] =
{
	{ "PhysicsRemoveTouchedList",          "PhysicsLink_RemoveTouchedList",          true  },
	{ "PhysicsNotifyOtherOfUntouch",       "PhysicsLink_NotifyOtherOfUntouch",       true  },
	{ "PhysicsRemoveToucher",              "PhysicsLink_RemoveToucher",              true  },
	{ "PhysicsNotifyOtherOfGroundRemoval", "PhysicsLink_NotifyOtherOfGroundRemoval", true  },
	{ "PhysicsRemoveGround",               "PhysicsLink_RemoveGround",               true  },
	{ "PhysicsRemoveGroundList",           "PhysicsLink_RemoveGroundList",           true  },
	{ "PhysicsClipVelocity",               "Physics_ClipVelocity",                   false },

	// The EIGHTH, added by the shared-middle-tier lane. Static on both
	// sides like the first six; see physicslink.h for why it is lifted
	// rather than deleted despite having no caller on either link line.
	{ "GetTouchTrace",                     "Physics_GetTouchTrace",                  true  },
};

const size_t kNumLifted = sizeof( kLifted ) / sizeof( kLifted[ 0 ] );

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
TEST( PhysicsLinkLift, TheTwoPhysicsBridgesRefuseTheContactTouchlinkOutLoud )
{
	// A plain occurrence counter, NOT CountCallsTo: a refusal is DECLARED
	// (`static CPhysicsBridgeRefusal s_Foo( ... )`), so the type name is
	// followed by an identifier rather than by `(`, and the call counter reads
	// 0 for a file with two of them. Stated because it read 0 here first.
	struct Local
	{
		static size_t Occurrences( const std::string &hay, const std::string &needle )
		{
			size_t n = 0;
			for ( size_t at = hay.find( needle ); at != std::string::npos;
			      at = hay.find( needle, at + needle.size() ) )
			{
				n++;
			}
			return n;
		}
	};

	// The CLIENT bridge is gone: fork game/client/physics.cpp left client.so
	// (2026-10-18) and its touch slots are managed's, which answer as stock's
	// client did for an owner-less pair (ClientPhysicsCollisionEventsTests.
	// TheFiveSlotsStockLeftEmptyReachNoSink). The server's half stays.
	const char *const kBridges[] = { "../sourcesharp/game/server/physics.cpp" };

	for ( size_t i = 0; i < sizeof( kBridges ) / sizeof( kBridges[ 0 ] ); i++ )
	{
		const std::string strSrc = ReadGameFile( kBridges[ i ] );
		ASSERT_FALSE( strSrc.empty() ) << kBridges[ i ] << " did not load";

		// Comments are stripped so the prose ABOUT the refusal cannot satisfy
		// the check for the refusal -- `.vpc prose is load-bearing` in reverse,
		// and these two files are almost entirely prose.
		const std::string strCode = StripComments( strSrc );

		// NAMED, NOT COUNTED IN BULK -- and it is the DECLARATION that is
		// counted, not the name: each refusal's name also appears in its three
		// uses (.Reached, .What, .Refusal), so a bare name count reads 4 for one
		// refusal. Stated because it read 4 here first. `server/physics.cpp` declares several
		// refusals for unrelated paths -- the shadow list, the debug overlay,
		// the solver -- so a count of ALL CPhysicsBridgeRefusal declarations
		// would rise as other work lands and would say nothing about THESE two.
		// The two contact-touchlink refusals are named individually, which is
		// also what makes a partial revert point at the half that went.
		// THE SERVER NO LONGER REFUSES (physics stock parity E1, 2026-10-01):
		// contact touch is ported (managed's since plan_physics_managed B2.2), and
		// TheServerHoldsAContactTouchAndReleasesIt below pins that build and
		// teardown went TOGETHER. The client still refuses both.
		const bool bServer = true;

		EXPECT_EQ( bServer ? 0u : 1u, Local::Occurrences( strCode, "CPhysicsBridgeRefusal s_RefuseContactTouch(" ) )
			<< kBridges[ i ] << ( bServer
				? " refuses DispatchStartTouch again, but the server ports contact touch."
				: " has no DispatchStartTouch refusal. Refusing the "
				  "teardown while keeping the build leaks touch links that are never "
				  "torn down -- the bug this test's other message says nothing "
				  "downstream would notice." );

		EXPECT_EQ( bServer ? 0u : 1u, Local::Occurrences( strCode, "CPhysicsBridgeRefusal s_RefuseContactUntouch(" ) )
			<< kBridges[ i ] << ( bServer
				? " refuses DispatchEndTouch again, but the server ports contact touch."
				: " has no DispatchEndTouch refusal. Refusing the "
				  "build while keeping the teardown walks a list nothing fills." );

		// AND EACH ONE PRINTS. This was `find("SOURCESHARP_PHYSICS_REFUSAL_FMT")`
		// and was a check that could not fail: with several refusals in a file,
		// silencing ONE leaves the others' prints behind and a first-hit search
		// still says yes. Caught by mutating the server's DispatchStartTouch
		// print to a DevMsg -- the test stayed green.
		//
		// The invariant is one print per refusal, file-wide: every refusal this
		// file declares must reach the shared format exactly once. Counting the
		// two together is what makes silencing any single one visible.
		EXPECT_EQ( Local::Occurrences( strCode, "CPhysicsBridgeRefusal" ),
		           Local::Occurrences( strCode, "SOURCESHARP_PHYSICS_REFUSAL_FMT" ) )
			<< kBridges[ i ] << " declares "
			<< Local::Occurrences( strCode, "CPhysicsBridgeRefusal" ) << " refusals but "
			<< "prints through the shared format "
			<< Local::Occurrences( strCode, "SOURCESHARP_PHYSICS_REFUSAL_FMT" ) << " times. "
			<< "A refusal that never prints is a silent no-op wearing a type -- the "
			<< "thing plan_entity.md §7 forbids -- and it is invisible in a file that "
			<< "still has another one printing.";
	}
}

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
TEST( PhysicsLinkLift, TheServerHoldsAContactTouchAndReleasesIt )
{
	const std::string strServer  = StripComments( ReadGameFile( "../sourcesharp/game/server/physics.cpp" ) );
	const std::string strBinding = StripComments( ReadGameFile(
		"../sourcesharp/managed/SourceSharp.Server.Interop/PhysicsCollisionEventBinding.cs" ) );
	ASSERT_FALSE( strServer.empty() );
	ASSERT_FALSE( strBinding.empty() );

	EXPECT_EQ( std::string::npos, strServer.find( "owner0.StartTouch( owner1 );" ) )
		<< "native's contact touch dispatch is back in physics.cpp";

	const char *const kNeedles[] = {
		"solverHeld.Add(first, second);", "solverHeld.Remove(first, second);",
	};
	for ( const char *pszNeedle : kNeedles )
	{
		EXPECT_NE( std::string::npos, strBinding.find( pszNeedle ) )
			<< "the collision event binding no longer has `" << pszNeedle << "`: contact "
			<< "touch must be held, and its teardown must go with its build.";
	}
}

//-----------------------------------------------------------------------------
// ...and the TRIGGER path, a different mechanism on the same set. Without
// this, dropping both touch mechanisms at once would pass every check above.
//-----------------------------------------------------------------------------
TEST( PhysicsLinkLift, TheTriggerTouchPathIsHeldToo )
{
	const std::string strBinding = StripComments( ReadGameFile(
		"../sourcesharp/managed/SourceSharp.Server.Interop/PhysicsCollisionEventBinding.cs" ) );
	ASSERT_FALSE( strBinding.empty() );

	EXPECT_NE( std::string::npos, strBinding.find( "solverHeld.Add(trigger, entity);" ) )
		<< "entering a physics trigger no longer holds the pair";
	EXPECT_NE( std::string::npos, strBinding.find( "solverHeld.Remove(trigger, entity);" ) )
		<< "leaving a physics trigger no longer releases the pair";
}

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

TEST( PhysicsLinkLift, HeaderDeclaresEveryFreeFunction )
{
	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		EXPECT_TRUE( DeclaredInHeader( PhysicsLinkHeader(), kLifted[ i ].pszFree ) )
			<< "physicslink.h does not declare " << kLifted[ i ].pszFree;
	}
}

//=============================================================================
// The PREMISE of the lift: seven of the eight really are declared `static`.
//
// If somebody drops the `static`, the forwarder still compiles as a member and
// the free function still compiles, and the reason these were safe to lift has
// silently stopped being true.
//=============================================================================

TEST( PhysicsLinkLift, TheStaticOnesLeftBothHeadersAndTheirFreeFormsTakeTheEntityExplicitly )
{
	// WAS TheStaticOnesAreStillDeclaredStaticOnBothSides, which read the
	// `static` off each member's declaration in the two entity headers. Those
	// declarations were inside the class bodies, inside
	// `#ifndef SS_CBASEENTITY_ALIAS` -- an arm this build never compiled -- and
	// the close deleted it with the tree's other 452 dead arms. Reading a
	// premise off text the compiler never sees is the check-that-cannot-fail
	// shape, so this is re-pointed rather than dropped.
	//
	// THE PREMISE IS THE SAME CLAIM SPELLED A DIFFERENT WAY. `static` on the
	// member meant "this function takes no `this`" -- which is what made it
	// liftable. The lifted form says it outright: every one of the seven static
	// ones takes its entity as an EXPLICIT parameter, `IGameEntity *`, and
	// physicslink.h is a header the build really compiles. If a free form ever
	// stops taking the entity, either it has started reading a global or the
	// lift has been undone, and neither produces a diagnostic.
	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		if ( !kLifted[ i ].bDeclaredStatic )
			continue;

		// The server header's half read game/server/baseentity.h, which is
		// compiled by nothing since plan_server_novalve.md P5 and left the farm
		// in P6; AllFilesWereReallyOpened pins that absence instead.

		EXPECT_FALSE( DeclaredStatic( ClientBaseEntityHeader(), kLifted[ i ].pszMember ) )
			<< "game/client/c_baseentity.h declares " << kLifted[ i ].pszMember
			<< " static again.";

		// The live successor, in the header physicslink.h's own callers include.
		EXPECT_TRUE( DeclaredInHeader( PhysicsLinkHeader(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is not declared in physicslink.h at all, "
			<< "so the assertion above is passing over its absence.";

		EXPECT_FALSE( DeclaredStatic( PhysicsLinkHeader(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is declared `static` in physicslink.h. "
			<< "At namespace scope in a header that is internal linkage -- one "
			<< "copy per including TU -- which is the opposite of what `static` "
			<< "bought on the member it replaced.";
	}

	// Physics_GetTouchTrace is the exception among the seven and is named here
	// rather than filtered out silently: it reads the file-scope g_TouchTrace,
	// so it takes no entity either. Its own entry in physicslink.h says why.
	const std::string strLink = Squeezed( StripComments( PhysicsLinkHeader() ) );
	const char *const kTakeAnEntity[] = {
		"voidPhysicsLink_RemoveTouchedList(IGameEntity*ent);",
		"voidPhysicsLink_NotifyOtherOfUntouch(IGameEntity*ent,IGameEntity*other);",
		"voidPhysicsLink_RemoveToucher(IGameEntity*other,touchlink_t*link);",
		"voidPhysicsLink_NotifyOtherOfGroundRemoval(IGameEntity*ent,IGameEntity*other);",
		"voidPhysicsLink_RemoveGround(IGameEntity*other,groundlink_t*link);",
		"voidPhysicsLink_RemoveGroundList(IGameEntity*ent);",
	};
	for ( size_t i = 0; i < sizeof( kTakeAnEntity ) / sizeof( kTakeAnEntity[ 0 ] ); i++ )
	{
		EXPECT_NE( std::string::npos, strLink.find( kTakeAnEntity[ i ] ) )
			<< "physicslink.h no longer declares \"" << kTakeAnEntity[ i ]
			<< "\". Note the spelling of the entity type: `IGameEntity`, because "
			<< "the alias these used to be written through is deleted.";
	}
}

TEST( PhysicsLinkLift, ClipVelocityIsTheOneThatWasNeverStatic )
{
	// READ THE ASSERTIONS, NOT THE NAME. The name records the asymmetry and the
	// asymmetry has not changed: PhysicsClipVelocity lifts because its body is
	// PURE ARITHMETIC, not because it was declared static. What changed is
	// where that is readable -- the two `static`-free declarations this used to
	// read were in class bodies inside `#ifndef SS_CBASEENTITY_ALIAS`, which
	// the compiler never saw and the close deleted.
	//
	// The lifted form shows the asymmetry better than the absent `static` did:
	// Physics_ClipVelocity takes no entity of any kind, while the six link
	// functions above it all take an `IGameEntity *`. Vectors in, vector out.
	// (The server header's half left with game/server/baseentity.h: compiled by
	// nothing since plan_server_novalve.md P5, out of the farm since P6.)
	EXPECT_FALSE( DeclaredStatic( ClientBaseEntityHeader(), "PhysicsClipVelocity" ) );

	const std::string strLink = Squeezed( StripComments( PhysicsLinkHeader() ) );
	EXPECT_NE( std::string::npos, strLink.find(
		"intPhysics_ClipVelocity(constVector&in,constVector&normal,Vector&out,floatoverbounce);" ) )
		<< "Physics_ClipVelocity's declaration in physicslink.h is not the "
		<< "arithmetic-only one any more. If it has acquired an entity "
		<< "parameter, the reason this one was a lift at all has stopped being "
		<< "true and nothing else in the tree would say so.";

	EXPECT_EQ( std::string::npos, strLink.find( "Physics_ClipVelocity(IGameEntity" ) )
		<< "Physics_ClipVelocity now takes an entity.";
}

//=============================================================================
// THE SCANNER IS ITSELF CHECKED.
//
// Everything above is an assertion about strings. If the files never opened, or
// the extractor never matched anything, every one of those assertions that is
// phrased as "does not contain" passes for free.
//=============================================================================

TEST( PhysicsLinkLiftFixture, AllFilesWereReallyOpened )
{
	// FLOOR PLUS ANCHOR, the shape the two entity headers below already use and
	// the server vpc took next (05237b9c8). Both subjects here are files the
	// port is actively moving code OUT of -- that is what this suite measures --
	// so a byte threshold over either one reddens on a correct tree eventually,
	// saying "did not load" about a file that loaded perfectly. Measured, not
	// guessed: baseentity_shared.cpp is 81,361 bytes and physics_main_shared.cpp
	// 59,986, both against floors of 50,000, and the lift is still taking bodies
	// out of them. The floor drops to a value only an unopened or truncated read
	// can miss; the anchor identifies WHICH file was read, which a size cannot.
	// (physics_main_shared.cpp's row left with the file: neither DLL compiles
	// it, and the checks that read it were culled 2026-09-27.)
	EXPECT_GT( PhysicsLinkHeader().size(),        200u ) << "physicslink.h did not load";
	EXPECT_NE( PhysicsLinkHeader().find( "PHYSICSLINK_H" ), std::string::npos )
		<< "physicslink.h is not physicslink.h.";
	// ANCHORS, NOT SIZES, for the two entity headers. Both lost their class
	// bodies -- 2,693 lines from the server's and 2,233 from the client's --
	// when the close deleted `#ifndef SS_CBASEENTITY_ALIAS`, so the
	// 50,000-byte thresholds that stood here reported "did not load" about
	// files that had loaded perfectly. A size cannot tell a truncated file from
	// a shortened one in any case. Each anchor is a LIVE line from the tail
	// that SURVIVED the class, at preprocessor depth 1.
	EXPECT_GT( ClientBaseEntityHeader().size(), 5000u ) << "client/c_baseentity.h did not load";
	EXPECT_NE( std::string::npos,
	           ClientBaseEntityHeader().find( "EXTERN_RECV_TABLE(DT_BaseEntity);" ) )
		<< "client/c_baseentity.h is not the file this suite means to be reading.";

	// THE SERVER HEADER IS AN ANCHORED ABSENCE NOW. game/server/baseentity.h is
	// compiled by nothing since plan_server_novalve.md P5 and left the farm in
	// P6, which is the stronger form of the "declares it static again" facts
	// above that used to read it. ReadGameFile answers empty both for a missing
	// file and for no tree at all, so a sibling in the same directory must read
	// first.
	EXPECT_NE( std::string::npos,
	           ReadGameFile( "shared/shareddefs.h" ).find( "#ifndef SHAREDDEFS_H" ) )
		<< "game/shared/shareddefs.h did not read, so the absence below is not "
		<< "evidence.";
	EXPECT_TRUE( ServerBaseEntityHeader().empty() )
		<< "game/server/baseentity.h is readable from the farm again. Nothing has "
		<< "compiled it since plan_server_novalve.md P5.";
}

TEST( PhysicsLinkLiftFixture, ExtractorTellsADefinitionFromACall )
{
	const std::string strFixture =
		"void CBaseEntity::RealDefinition( int a )\n"
		"{\n"
		"    CBaseEntity::NotADefinition( a );\n"
		"    x = CBaseEntity::AlsoNotOne( a ) + 1;\n"
		"}\n"
		"bool CBaseEntity::ConstDefinition( void ) const\n"
		"{\n"
		"    return true;\n"
		"}\n";

	const std::vector< std::string > defs = MemberDefinitions( strFixture );
	const std::set< std::string > setDefs( defs.begin(), defs.end() );

	EXPECT_EQ( 2u, defs.size() );
	EXPECT_EQ( 1u, setDefs.count( "RealDefinition" ) );
	EXPECT_EQ( 1u, setDefs.count( "ConstDefinition" ) )   // trailing const
		<< "a `const` member definition was missed; the real file has several";
	EXPECT_EQ( 0u, setDefs.count( "NotADefinition" ) )
		<< "a CALL was counted as a definition";
	EXPECT_EQ( 0u, setDefs.count( "AlsoNotOne" ) )
		<< "a call in an expression was counted as a definition";
}

TEST( PhysicsLinkLiftFixture, ExtractorIgnoresProse )
{
	const std::string strFixture =
		"// CBaseEntity::MentionedInAComment( void )\n"
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
	const std::string strPositive =
		"void CBaseEntity::Thin( int a )\n"
		"{\n"
		"    Free_Thin( a );\n"
		"}\n";

	const std::string strGrown =
		"void CBaseEntity::Thick( int a )\n"
		"{\n"
		"    if ( a ) return;\n"
		"    Free_Thick( a );\n"
		"}\n";

	// Each body is bound to a local FIRST. Calling ForwarderBody() twice inside
	// one std::count() would take begin() and end() from two different
	// temporaries, which is undefined behaviour -- and did in fact miscount
	// before this was fixed.
	const std::string strThin  = ForwarderBody( strPositive, "Thin" );
	const std::string strThick = ForwarderBody( strGrown, "Thick" );

	EXPECT_EQ( "Free_Thin(a);", strThin );
	EXPECT_EQ( 1, std::count( strThin.begin(), strThin.end(), ';' ) );

	// The negative the real test relies on: a grown body has >1 statement.
	EXPECT_EQ( 2, std::count( strThick.begin(), strThick.end(), ';' ) )
		<< "the >1-semicolon rule cannot catch a grown forwarder";

	EXPECT_TRUE( ForwarderBody( strPositive, "Absent" ).empty() );
}

TEST( PhysicsLinkLiftFixture, StaticDetectorWorksOnFixtures )
{
	const std::string strFixture =
		"class CBaseEntity\n"
		"{\n"
		"    static void   IsStatic( CBaseEntity *ent );\n"
		"    void          IsNotStatic( CBaseEntity *ent );\n"
		"};\n";

	EXPECT_TRUE ( DeclaredStatic( strFixture, "IsStatic" ) );
	EXPECT_FALSE( DeclaredStatic( strFixture, "IsNotStatic" ) )
		<< "the static detector answers true for a non-static declaration, so "
		<< "TheStaticOnesAreStillDeclaredStaticOnBothSides cannot fail";
	EXPECT_FALSE( DeclaredStatic( strFixture, "NotPresentAtAll" ) );
}

TEST( PhysicsLinkLiftFixture, FreeDefinitionDetectorWorksOnFixtures )
{
	const std::string strFree   = "void Free_Thing( int a )\n{\n}\n";
	const std::string strMember = "void CBaseEntity::Free_Thing( int a )\n{\n}\n";
	const std::string strDecl   = "void Free_Thing( int a );\n";

	EXPECT_TRUE ( DefinedFree( strFree, "Free_Thing" ) );
	EXPECT_FALSE( DefinedFree( strMember, "Free_Thing" ) )
		<< "a class-scope definition was read as a free one, so "
		<< "SharedFileDefinesEveryFreeFunction cannot fail";
	EXPECT_FALSE( DefinedFree( strDecl, "Free_Thing" ) )
		<< "a declaration was read as a definition";
}
