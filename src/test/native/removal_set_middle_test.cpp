//========= SourceSharp — the step-3 removal set's shared middle tier ========//
//
// A SOURCE SCANNER, like removal_set_tail_test, physicslink_lift_test and
// baseentity_shared_lift_test, and for the same reason: the claim is about the
// TEXT of files that need a whole engine to run, so the text is what is read.
//
// WHAT THIS COVERS. plan_baseentity.md Phase 13 step 3's removal set is the
// files DEFINING CBaseEntity members out of line; under
// `using CBaseEntity = IGameEntity;` an alias is not a class scope, so every
// such definition is a COMPILE error and can only be lifted, ported or
// deleted. The MIDDLE of that set is three files in game/shared/, and every
// change in them lands in BOTH server.so and client.so:
//
//   game/shared/baseentity_shared.cpp     78 definitions  (77 server / 78 client)
//   game/shared/physics_main_shared.cpp   42 definitions  (42 / 42)
//   sourcesharp/game/shared/sound_emitter.cpp     9 definitions  ( 9 /  5)
//
// ONE HUNDRED AND TWENTY-NINE definitions were classified, each of them by
// asking the COMPILER rather than by reading: every non-virtual member was
// declared `static` in both entity headers and the objects rebuilt, to a fixed
// point. A static member function has no `this`, so a body that still compiles
// never needed the entity. The answer was FOUR bodies out of 129:
//
//   GetTouchTrace              LIFTED  (physicslink_lift_test pins it)
//   ScriptSoundDuration        BLOCKED by a pointer-to-member
//   VScriptPrecacheScriptSound BLOCKED by a pointer-to-member
//   SetPlayerSimulated \       BLOCKED: clean only because the body is
//   UnsetPlayerSimulated/      compiled out, and the compiled-out arm reaches
//                              state that is PRIVATE on the client
//
// Everything else takes a real `this`, or is virtual, and is a PORT rather
// than a lift. This file pinned the four verdicts and, more importantly, the
// six places the two headers disagree -- because that is what a future lane
// reading one header will get wrong. Those facts read the removal set's own
// files, which the build no longer compiles, and went with them; what is left
// is the ImpactTrace default-argument fact below, over headers the build does
// compile.
//
//=============================================================================//
//
// THE HAZARD THIS FILE EXISTS FOR. These are game/shared/ files: one text,
// two configurations, two different classes. A member can be `virtual` on one
// side and plain on the other, and lifting it then
//
//     COMPILES GREEN IN BOTH CONFIGURATIONS
//
// while leaving every override on the virtual side with no base
// implementation. Nothing in a build catches it. There are SIX such members
// across this lane's files, found by reading both headers for all 129:
//
//   ShouldDrawUnderwaterBulletBubbles  plain server / VIRTUAL client
//   HandleShotImpactingWater           plain server / VIRTUAL client
//   DispatchTraceAttack                plain server / VIRTUAL client
//   IsCurrentlyTouching                plain server / VIRTUAL client
//   ResolveFlyCollisionCustom          VIRTUAL server / plain client
//   SUB_Remove                         plain server / VIRTUAL client
//
// and two more where the disagreement is not about `virtual` at all:
//
//   IsToolRecording        declared on the CLIENT ONLY, and its definition in
//                          baseentity_shared.cpp sits inside `#ifdef
//                          CLIENT_DLL` -- which is why the same file yields 77
//                          symbols on one link line and 78 on the other.
//   ImpactTrace            its third parameter has a DEFAULT ARGUMENT on the
//                          server (`= NULL`) and none on the client. The
//                          quietest kind of all: the short call compiles on
//                          one side and not the other, and no build says so,
//                          because every two-argument call in the tree goes
//                          through the server-only UTIL_ImpactTrace wrapper.
//   m_bIsPlayerSimulated   PUBLIC on the server, PRIVATE on the client. A data
//                          member, so no `virtual` scan would ever have found
//                          it, and it is the whole reason SetPlayerSimulated
//                          and UnsetPlayerSimulated cannot be lifted: their
//                          bodies are empty under SOURCESHARP, so the compiler
//                          says "no `this` needed", but the `#ifndef
//                          SOURCESHARP` arm they still carry writes that
//                          member -- legal from a free function on the server,
//                          not on the client.
//
// SIDE-INDEPENDENCE OF THE SCANNER. All three files under test say
// `CBaseEntity::` on both sides: game/shared/shared_classnames.h:17 does
// `#define CBaseEntity C_BaseEntity` for the client, which is why the client's
// symbols demangle to `C_BaseEntity::`. So the definition counts here are
// per-FILE and cover both link lines at once -- unlike the client-only files
// removal_set_tail_test reads, which are written `C_BaseEntity::` literally
// and need the scope passed in.
//
// THE DETECTOR IS PROVEN ABLE TO ANSWER EVERY WAY on hand-written fixtures at
// the bottom. An absence assertion passes for free over a detector that never
// finds anything.
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

const std::string &ServerHeader()
{
	static const std::string s = ReadGameFile( "server/baseentity.h" );
	return s;
}

const std::string &ClientHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity.h" );
	return s;
}

const std::string &ServerUtilHeader()
{
	static const std::string s = ReadGameFile( "server/util.h" );
	return s;
}

const std::string &ClientUtilHeader()
{
	static const std::string s = ReadGameFile( "client/cdll_util.h" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a member name written in prose -- which
// every file under test is full of, being about exactly that -- is not
// mistaken for code.
//
// Not a C++ lexer: it does not track string or character literals. What is
// built on top of it is a DEFINITION and DECLARATION detector, and neither can
// occur inside a string literal.
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

size_t SkipSpace( const std::string &s, size_t i )
{
	while ( i < s.size() && ( s[ i ] == ' ' || s[ i ] == '\t' ||
	                          s[ i ] == '\r' || s[ i ] == '\n' ) )
		i++;
	return i;
}

//-----------------------------------------------------------------------------
// Skip a balanced parenthesised run starting at strCode[at] == '('.
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

//-----------------------------------------------------------------------------
// The line `pos` sits on, comments already stripped, and where on that line
// `pos` fell.
//
// THE COLUMN IS RETURNED RATHER THAN SEARCHED FOR AGAIN, and the difference is
// not cosmetic: the first fixture run of this file failed because the caller
// did `strLine.find( strName )` and got the WRONG occurrence. On
//
//     virtual bool CallsAPlainOne( CBaseEntity *p ) const { return p->APlainOne(); }
//
// searching for "APlainOne" finds it inside "CallsAPlainOne" -- a substring
// match with an identifier character in front of it, at a column with no `(`
// before it and no `->` immediately before it. Every guard passed and the line
// carried `virtual`, so a plain member was reported virtual. That is precisely
// the IsStandable shape this detector exists to reject, reproduced inside the
// detector itself.
//-----------------------------------------------------------------------------
std::string LineAt( const std::string &strCode, size_t pos, size_t *pColumn )
{
	size_t start = strCode.rfind( '\n', pos );
	start = ( start == std::string::npos ) ? 0 : start + 1;
	size_t end = strCode.find( '\n', pos );
	if ( end == std::string::npos )
		end = strCode.size();
	if ( pColumn )
		*pColumn = pos - start;
	return strCode.substr( start, end - start );
}

//-----------------------------------------------------------------------------
// How many parameters of `<Name>`'s declaration carry a DEFAULT ARGUMENT.
//
// The fourth way a shared member can differ between the sides, after
// `virtual`, `const` and existence -- and the quietest. A default that exists
// on only one side is invisible to every build: the side that has it accepts
// the short call, and the side that does not never sees one, because every
// call site that omits the argument is in a file compiled for one side only.
// It becomes a compile error the day a game/shared/ file makes the short call.
//
// Counted rather than compared as text, because the two headers space their
// declarations differently and always have.
//-----------------------------------------------------------------------------
int DefaultArgumentCount( const std::string &strHeader, const std::string &strName )
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
			continue;
		const size_t open = i;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;

		size_t j = SkipSpace( strCode, i );
		if ( strCode.compare( j, 5, "const" ) == 0 &&
		     ( j + 5 >= strCode.size() || !IsIdentChar( strCode[ j + 5 ] ) ) )
		{
			j = SkipSpace( strCode, j + 5 );
		}
		if ( j >= strCode.size() || strCode[ j ] != ';' )
			continue;             // a call or a definition, not a declaration

		size_t nameOnLine = 0;
		const std::string strLine = LineAt( strCode, pos, &nameOnLine );
		if ( strLine.substr( 0, nameOnLine ).find( '(' ) != std::string::npos )
			continue;

		int n = 0;
		for ( size_t k = open; k + 1 < i; k++ )
		{
			// an `=` inside the parameter list, and not `==` `!=` `<=` `>=`
			if ( strCode[ k ] != '=' )
				continue;
			if ( strCode[ k + 1 ] == '=' )
				continue;
			if ( k > open && ( strCode[ k - 1 ] == '=' || strCode[ k - 1 ] == '!' ||
			                   strCode[ k - 1 ] == '<' || strCode[ k - 1 ] == '>' ) )
				continue;
			n++;
		}
		return n;
	}
	return -1;                    // not declared here
}

} // namespace

//=============================================================================
// The NINTH disagreement, and the quietest of them all.
//=============================================================================

TEST( RemovalSetMiddle, ImpactTraceHasADefaultArgumentOnTheServerOnly )
{
	// baseentity.h:1078   ... const char *pCustomImpactName = NULL );
	// c_baseentity.h:869  ... const char *pCustomImpactName );
	//
	// So `ImpactTrace( pTrace, iDamageType )` compiles on the server and NOT
	// on the client, and nothing in either build says so: every two-argument
	// call in the tree goes through UTIL_ImpactTrace (util.cpp:1708), which
	// carries its own default and is server-only. The day a game/shared/ file
	// makes the short call directly, the client stops building on a line that
	// looks correct and a header that looks the same.
	//
	// ImpactTrace is virtual on both sides, so it is BLOCKED from lifting for
	// an unrelated reason. This is recorded because the NEXT member somebody
	// looks at might not be.
	// READ THE ASSERTIONS, NOT THE NAME -- AND THE TWO SIDES NOW AGREE, which
	// is the outcome the old failure message invited: "If the two sides now
	// agree, that is a real improvement and this test should say so instead."
	// It is not agreement anybody negotiated. Both member declarations were in
	// class bodies inside `#ifndef SS_CBASEENTITY_ALIAS` and the close deleted
	// them, so neither header gives ImpactTrace a default now, because neither
	// header declares ImpactTrace.
	//
	// The asymmetry mattered because a shared TU could write the short call.
	// The only two-argument call surface left is the FREE funnel, and it is the
	// one thing that has to agree across the two sides -- so that is what is
	// asserted: UTIL_ImpactTrace carries the default on BOTH, in headers the
	// build compiles (server_util_library.cpp includes util.h,
	// client_util_library.cpp includes cdll_util.h).
	// -1, not 0: DefaultArgumentCount answers -1 for "not declared in this
	// file" and 0 for "declared here with no default". The distinction is the
	// whole of what changed, so it is asserted rather than flattened -- a 0
	// here would mean ImpactTrace is still declared and merely lost its
	// default, which is a different event with different consequences.
	//
	// THE SERVER HALVES ARE RETIRED. game/server/baseentity.h and
	// game/server/util.h are compiled by nothing since plan_server_novalve.md
	// P5 and left the farm in P6 (AllFilesWereReallyOpened pins both absences),
	// so the server no longer has a header-level ImpactTrace to disagree with,
	// and util.h's UTIL_ImpactTrace default lost its subject. The client halves
	// below are the side the old asymmetry broke, and they stand.
	EXPECT_EQ( -1, DefaultArgumentCount( ClientHeader(), "ImpactTrace" ) )
		<< "game/client/c_baseentity.h declares ImpactTrace again.";

	EXPECT_EQ( 1, DefaultArgumentCount( ClientUtilHeader(), "UTIL_ImpactTrace" ) )
		<< "game/client/cdll_util.h no longer gives UTIL_ImpactTrace's third "
		<< "parameter a default. THIS is the side the old asymmetry broke: a "
		<< "game/shared file making the short call compiled on the server and "
		<< "not on the client, on a line that looks correct and a header that "
		<< "looks the same.";
}

//=============================================================================
// THE SCANNER IS ITSELF CHECKED.
//
// Everything above is an assertion about strings, two of them absences, which
// a detector that never finds anything satisfies for free.
//=============================================================================

TEST( RemovalSetMiddleFixture, AllFilesWereReallyOpened )
{
	// RE-ANCHORED when the farm stopped carrying stock files neither DLL
	// compiles. This also read baseentity_shared.cpp, physics_main_shared.cpp,
	// sound_emitter.cpp, subs.cpp, baseentity.cpp and baseentity_sound_members.cpp;
	// the facts that read those went with them. What is left is the two client
	// headers ImpactTraceHasADefaultArgumentOnTheServerOnly reads, one of which
	// it asserts an ABSENCE over -- so an unread header must be red here.
	//
	// ANCHORS, NOT SIZES, for the two entity headers: both lost their class
	// bodies when the close deleted `#ifndef SS_CBASEENTITY_ALIAS`, so the
	// 50,000-byte thresholds reported "did not load" about files that loaded
	// perfectly. Each anchor is a LIVE line from the tail that survived.
	EXPECT_GT( ClientHeader().size(),            5000u ) << "client/c_baseentity.h did not load";
	EXPECT_NE( std::string::npos,
	           ClientHeader().find( "EXTERN_RECV_TABLE(DT_BaseEntity);" ) )
		<< "client/c_baseentity.h is not the file this suite means to read.";

	EXPECT_GT( ClientUtilHeader().size(),        2000u ) << "client/cdll_util.h did not load";

	// THE TWO SERVER HEADERS ARE ANCHORED ABSENCES NOW. game/server/baseentity.h
	// and game/server/util.h are compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6. ReadGameFile answers
	// empty for a missing file and for no tree alike, so a sibling in the same
	// directory must read first.
	EXPECT_NE( std::string::npos,
	           ReadGameFile( "shared/shareddefs.h" ).find( "#ifndef SHAREDDEFS_H" ) )
		<< "game/shared/shareddefs.h did not read, so the absences below are "
		<< "not evidence.";
	EXPECT_TRUE( ServerHeader().empty() )
		<< "game/server/baseentity.h is readable from the farm again.";
	EXPECT_TRUE( ServerUtilHeader().empty() )
		<< "game/server/util.h is readable from the farm again.";
}

TEST( RemovalSetMiddleFixture, DefaultArgumentCounterAnswersEveryWay )
{
	const std::string strFixture =
		"class CBaseEntity\n"
		"{\n"
		"\tvirtual void NoDefaults( int a, int b );\n"
		"\tvirtual void OneDefault( int a, int b = 0 );\n"
		"\tvirtual void TwoDefaults( int a = 1, const char *b = NULL );\n"
		"\tvirtual void DefaultIsAComparison( int a, bool b = ( 1 == 1 ) );\n"
		"\tbool InlineBody( int a ) const { return a == 0 && a != 1; }\n"
		"};\n";

	EXPECT_EQ( 0,  DefaultArgumentCount( strFixture, "NoDefaults" ) );
	EXPECT_EQ( 1,  DefaultArgumentCount( strFixture, "OneDefault" ) );
	EXPECT_EQ( 2,  DefaultArgumentCount( strFixture, "TwoDefaults" ) );
	EXPECT_EQ( -1, DefaultArgumentCount( strFixture, "NotDeclaredHere" ) )
		<< "an absent member must be distinguishable from one with no "
		<< "defaults: 0 and not-there are different answers, and the real "
		<< "assertion above expects a 0 from one side.";
	EXPECT_EQ( 1,  DefaultArgumentCount( strFixture, "DefaultIsAComparison" ) )
		<< "the `==` inside a default EXPRESSION was counted as a second "
		<< "default";
	EXPECT_EQ( -1, DefaultArgumentCount( strFixture, "InlineBody" ) )
		<< "an inline body is not a declaration and must not be read as one -- "
		<< "its `==` and `!=` are not defaults, and neither is anything else "
		<< "in it";
}
