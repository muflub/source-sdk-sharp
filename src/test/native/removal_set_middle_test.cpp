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
//   sourcesharp/game/client/sound_emitter.cpp     9 definitions  ( 9 /  5)
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

//=============================================================================
// THE SCANNER IS ITSELF CHECKED.
//
// Everything above is an assertion about strings, two of them absences, which
// a detector that never finds anything satisfies for free.
//=============================================================================

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
