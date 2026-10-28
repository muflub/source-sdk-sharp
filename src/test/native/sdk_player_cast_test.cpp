//========= SourceSharp — the shape that flips from safe to wrong ============//
//
// THE BUG THIS FILE IS ABOUT IS A SHAPE, NOT A VALUE, so this test reads source
// rather than running any.
//
//     CBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );
//     if ( p )
//         p->SomethingVirtual();
//
// Both CBaseEntity::Instance() and GetContainingEntity() are
// edict->GetUnknown()->GetBaseEntity(), and CStandaloneEntity::GetBaseEntity()
// returns NULL -- "that single NULL is what the whole parallel hierarchy rests
// on" (shell_standalone.h). So the cast is a cast of NULL, the `if` fails, and
// the site is harmless. docs/divergences.md found FOURTEEN of these, and
// thirteen were NULL-checked exactly like the above.
//
// PHASE 13 STEP 5 MAKES GetBaseEntity() RETURN `this`. Every one of those casts
// then yields a live pointer, every one of those `if`s then PASSES, and the
// call lands on the wrong vtable -- caused by a commit in a different file that
// nobody would think to review against these.
//
//=============================================================================//
// WHAT THE DRY RUN TAUGHT THIS FILE, AND WHY IT IS THREE BANS NOW
//=============================================================================//
//
// A step-5 SIMULATION (`ss_step5_sim`, retired at plan_entity.md step 4g --
// see CStandaloneEntity::GetBaseEntity for why) was armed on a live map and the
// process died in seconds, THREE TIMES, at three sites none of which this file
// could see. THE BANS BELOW OUTLIVE THE INSTRUMENT THAT FOUND THEM: what they
// forbid is asking the partition as `GetBaseEntity() == NULL`, and §3.3 makes
// that the wrong question whatever GetBaseEntity happens to answer.
//
//   1. gameinterface.cpp ProcessUsercmds -- the same cast-plus-NULL-check
//      shape, SPLIT OVER FOUR LINES. The old predicate needed the cast and the
//      Instance() call on ONE line, so a two-statement spelling of the exact
//      banned thing walked straight past it.
//
//          CBaseEntity *pEnt = CBaseEntity::Instance( player );
//          if ( pEnt && pEnt->IsPlayer() )                        <-- crash
//              pPlayer = static_cast< CBasePlayer * >( pEnt );
//
//   2. gameinterface.cpp MarkEntitiesAsTouching -- NOT A CAST AT ALL. Two
//      resolved pointers and `if ( entity && entityTouched )` choosing between
//      the SDK arm and the standalone arm. At step 5 both come back non-NULL,
//      the SDK arm runs on wrong-vtable pointers and the standalone arm below
//      becomes unreachable. No cast-scanner in any form could have matched it.
//
//   3. shell_standalone_player.cpp RunOneCommand, downstream through
//      gamemovement's traces.
//
// So the real hazard class is NOT "a cast". It is A NULL CHECK ON A POINTER
// THAT CAME FROM A GetBaseEntity()-DERIVED RESOLVER -- 48 server sites in 20
// files and 38 client, enumerated in docs/typedef-dryrun/nullflip_verbose.txt
// by the predicate this file now reimplements.
//
// THREE BANS, in order of how much they see:
//
//   A. the original one-line cast (kept verbatim; it is cheap and it documents)
//   B. the TWO-STATEMENT cast -- a resolver bound on one line and a player cast
//      of that variable within six -- which is casualty 1
//   C. the class itself -- a NULL test within six lines on a pointer bound from
//      a resolver THAT DOES NOT ASK THE PARTITION -- which is casualty 2 and
//      everything like it
//
// WHAT MAKES BAN C SATISFIABLE. The fix is not 86 call-site edits; it is the
// five resolvers those 86 sites all run through (GetContainingEntity,
// CBaseEntity::Instance, gEntList.GetBaseEntity, EntityFromEntityHandle, and
// the raw crossing in handle_cross.h) asking the partition the way that
// survives step 5 -- GetGameEntity() != NULL, never GetBaseEntity() == NULL.
// A fix at the resolver cannot miss a call site, which matters because the
// enumerated list was demonstrably incomplete: three of the three crashes were
// on nobody's list. So Ban C fires only on a RAW GetBaseEntity() -- the
// no-argument member call that bypasses those five.
//
// AND WHAT KEEPS BAN C FROM BEING VACUOUS. "Raw calls: zero" would be a proud
// report from a predicate that matched nothing at all -- this project has now
// found twenty-two checks that could not fail. So TheBanCPredicateSeesTheClass
// below runs the SAME predicate with the exemption removed, i.e. treating every
// resolver as raw, and requires it to find at least as many server sites as the
// dry run enumerated by hand. Ban C's zero means something only because that
// number is large.
//
//=============================================================================//
// SCOPE, stated rather than implied. game/server and game/shared for the server
// pass; game/client, game/client/replay and game/shared for the client. Top
// level otherwise -- the per-mod subdirectories (tf/, hl2/, episodic/,
// portal/, ...) are not compiled into this mod. Regions the preprocessor drops
// in a SOURCESHARP build are skipped, so the sanctioned cast in the #else arm
// of UTIL_SDKPlayerFromEdict does not count against it, and neither does stock
// code parked under #ifndef SOURCESHARP. Whole-line comments are skipped, which
// is what lets util.h and this file quote the banned forms in prose.
//
// THE CLIENT IS A BAN NOW, AND IT WAS A RATCHET.
//
// It was a ratchet because IClientUnknown had no GetGameEntity(): the partition
// could only be asked on the server, where it lives on IServerEntity, so the
// client's eighteen raw sites were counted and pinned rather than fixed, and
// the constant said out loud that it could fall and must not rise.
//
// Phase 13's revised step 1 landed the client's half -- on IClientEntity, since
// appending to IClientUnknown moves five engine-called vtable slots -- and gave
// the client two resolvers to funnel through, because unlike the server it had
// none to fix at. All eighteen are gone, and so are the raw sites that had no
// NULL check at all and were therefore invisible to this scanner. The count is
// zero and the fixture below now demands zero.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every ban it is asked to enforce. So the fixtures below assert a floor on the
// files opened, a floor on the lines read, that the eviction list was parsed
// and is load-bearing, that the files of the original inventory were among them
// or are accounted for where they went, that each predicate still recognises
// its own shape, and -- the one that matters most -- that Ban C's predicate
// finds the dry run's class when its exemption is lifted.
//
// AND EVERY ONE OF THOSE FIVE WAS RE-CALIBRATED AT THE FLIP, on the commit that
// put SS_CBASEENTITY_ALIAS into both .vpc files and took 35 stock `$File` rows
// out with it. Each of them had been counted or named against the pre-flip file
// set, so each went red on a tree where nothing was wrong: the floors because
// half the scanned source left the build, the inventory and the eviction rows
// because the files they named by hand are among the ~38. They are RE-STATED
// below, each with what the flip changed, what the number is now, and why it
// still catches what it was written to catch -- and the two floors that had
// been revised once per wave of progress were made RELATIVE while they were
// open, so that the next eviction moves neither.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"
#include <set>

#include <dirent.h>
#include <stdio.h>
#include <string.h>

#include <algorithm>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Squeezing, and what counts as prose.
//
// Whitespace is squeezed out first so that "( CBasePlayer * )", "(CBasePlayer*)"
// and "static_cast<CBasePlayer*>" all read the same. Every predicate below
// works on the squeezed form, which is why none of them needs to care about
// how a line was wrapped.
//-----------------------------------------------------------------------------

std::string Squeezed( const std::string &line )
{
	std::string out;
	out.reserve( line.size() );
	for ( size_t i = 0; i < line.size(); i++ )
	{
		const char c = line[ i ];
		if ( c != ' ' && c != '\t' && c != '\r' )
			out += c;
	}
	return out;
}

bool IsWholeLineComment( const std::string &line )
{
	size_t i = 0;
	while ( i < line.size() && ( line[ i ] == ' ' || line[ i ] == '\t' ) )
		i++;

	if ( i >= line.size() )
		return true;

	// "//", "/*" and the " * " continuation of a block comment. Enough for the
	// way this codebase writes prose, and deliberately not a C++ lexer: a
	// scanner nobody can predict the behaviour of is a scanner nobody trusts
	// when it goes red.
	if ( line.compare( i, 2, "//" ) == 0 )
		return true;
	if ( line.compare( i, 2, "/*" ) == 0 )
		return true;
	if ( line[ i ] == '*' )
		return true;

	return false;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
		|| ( c >= '0' && c <= '9' ) || c == '_';
}

//-----------------------------------------------------------------------------
// BAN A -- the original one-line shape, unchanged.
//
// The line must contain BOTH a cast to a player-ish SDK type AND one of the two
// ways of asking an edict for its entity. Either alone is fine: casting is how
// the SDK is written, and Instance()/GetContainingEntity() answering a
// CBaseEntity * is their job. It is the pair on one line that is the hazard.
//-----------------------------------------------------------------------------

const char *kPlayerCasts[] =
{
	"(CBasePlayer*)",
	"<CBasePlayer*>",
	"(CBaseCombatCharacter*)",
	"<CBaseCombatCharacter*>",
	"(CBaseMultiplayerPlayer*)",
	"<CBaseMultiplayerPlayer*>",
	"(C_BasePlayer*)",
	"<C_BasePlayer*>",
};
const size_t kNumPlayerCasts = sizeof( kPlayerCasts ) / sizeof( kPlayerCasts[ 0 ] );

bool HasPlayerCast( const std::string &squeezed )
{
	for ( size_t i = 0; i < kNumPlayerCasts; i++ )
	{
		if ( squeezed.find( kPlayerCasts[ i ] ) != std::string::npos )
			return true;
	}
	return false;
}

bool LineHasBannedCast( const std::string &line )
{
	if ( IsWholeLineComment( line ) )
		return false;

	const std::string s = Squeezed( line );

	if ( !HasPlayerCast( s ) )
		return false;

	// The first three are deleted from the tree and kept for the reason
	// kPartitionedResolvers gives: a name that is gone is free to ban, and the
	// ban is what makes typing it back visible. The fourth is the live
	// successor -- a player cast off GameEntityFromEntityHandle is exactly as
	// wrong as one off the three that went, and it is the one that can happen.
	return s.find( "CBaseEntity::Instance(" ) != std::string::npos
		|| s.find( "C_BaseEntity::Instance(" ) != std::string::npos
		|| s.find( "GetContainingEntity(" ) != std::string::npos
		|| s.find( "GameEntityFromEntityHandle(" ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// The resolvers, and which of them ask the partition.
//
// PARTITIONED -- these five reach GetBaseEntity() through
// SourceSharp_SDKEntityFromUnknown, which asks GetGameEntity() first and hands
// back NULL for anything on the parallel hierarchy whatever step 5 does to
// GetBaseEntity(). A pointer from one of these is safe to NULL-check.
//
// RAW -- a bare `X->GetBaseEntity()` with NO ARGUMENTS. That is the member call
// on IServerUnknown/IClientUnknown that the five wrap, and calling it directly
// is the bypass. The empty parentheses are what distinguish it from
// `gEntList.GetBaseEntity( hEnt )` and `cl_entitylist->GetBaseEntity( i )`,
// which are two of the five and always take one.
//
// TheResolverListIsHonest below reads each of the five out of the tree and
// requires it to name the partition, so this classification cannot rot into a
// list of names that no longer do what it says they do.
//-----------------------------------------------------------------------------

const char *kPartitionedResolvers[] =
{
	// THE FIRST FOUR ARE DELETED FROM THE TREE, AND THEY STAY ON THIS LIST.
	// All four bottomed out in IServerUnknown/IClientUnknown::GetBaseEntity(),
	// whose eight implementations across the two DLLs are every one of them
	// `mov $0x0,%eax; ret` -- so each answered a constant NULL and the close
	// deleted them. A name that is gone cannot appear in a binding, so these
	// four rows now cost nothing to keep and buy the one thing a deletion
	// cannot buy by itself: if any of them is typed back, it is a resolver on
	// the day it returns and Ban C sees it immediately rather than after
	// somebody remembers to re-add it here.
	"CBaseEntity::Instance(",
	"C_BaseEntity::Instance(",
	"GetContainingEntity(",
	"EntityFromEntityHandle(",
	// THE LIVE SUCCESSOR of the fourth, and it is on BOTH sides now (the client
	// had none, so shared trace code culled every managed entity). It asks the
	// object -- IServerEntity::GetGameEntity() on the server,
	// IClientUnknown::GetIClientEntity()->GetGameEntity() on the client -- so it
	// is partitioned by construction. Spelled in full: the shorter row above is
	// a SUFFIX of this name, and the word-boundary check would reject it.
	"GameEntityFromEntityHandle(",
	"GetBaseEntity(",			// the argument-taking entity-list forms
	"SDKEntityFromUnknown(",	// the client's two, added by Phase 13 step 1
	"SDKEntityFromRenderable(",
};
const size_t kNumPartitionedResolvers =
	sizeof( kPartitionedResolvers ) / sizeof( kPartitionedResolvers[ 0 ] );

enum ResolverKind_t
{
	RESOLVER_NONE = 0,
	RESOLVER_PARTITIONED,	// goes through the five; its NULL is trustworthy
	RESOLVER_RAW,			// X->GetBaseEntity(), the bypass
};

// A binding is `<ident> = [cast] <maybe-a-receiver><resolver>(`. The receiver
// run allows "gEntList.", "pUnk->", "CBaseEntity::" and friends, which is what
// makes one predicate cover all the spellings.
//
// Parentheses are allowed in the run so that a CHAINED receiver counts --
// "pRend->GetIClientUnknown()->GetBaseEntity()" is fifty-odd sites in the
// client's renderer and the dry run's regex could not match any of them,
// because its receiver class had no '(' in it. Being able to see them is
// the point of rewriting the predicate rather than porting it.
bool IsReceiverChar( char c )
{
	return IsIdentChar( c ) || c == ':' || c == '.' || c == '>' || c == '-'
		|| c == '(' || c == ')';
}

// Strips one leading cast from the right-hand side, if there is one. Returns
// the offset past it.
size_t SkipLeadingCast( const std::string &rhs )
{
	static const char *kCastKeywords[] =
		{ "static_cast<", "assert_cast<", "dynamic_cast<", "reinterpret_cast<", "const_cast<" };
	static const size_t kNumCastKeywords =
		sizeof( kCastKeywords ) / sizeof( kCastKeywords[ 0 ] );

	for ( size_t i = 0; i < kNumCastKeywords; i++ )
	{
		const size_t n = strlen( kCastKeywords[ i ] );
		if ( rhs.compare( 0, n, kCastKeywords[ i ] ) == 0 )
		{
			const size_t close = rhs.find( ">(", n );
			if ( close != std::string::npos )
				return close + 2;
			return 0;
		}
	}

	// A C-cast: "(SomeType*)" or "(SomeType::Nested*)".
	if ( !rhs.empty() && rhs[ 0 ] == '(' )
	{
		const size_t close = rhs.find( ')' );
		if ( close != std::string::npos && close >= 2 && rhs[ close - 1 ] == '*' )
		{
			bool bAllWordy = true;
			for ( size_t i = 1; i + 1 < close; i++ )
			{
				if ( !IsIdentChar( rhs[ i ] ) && rhs[ i ] != ':' && rhs[ i ] != '*' )
				{
					bAllWordy = false;
					break;
				}
			}
			if ( bAllWordy )
				return close + 1;
		}
	}

	return 0;
}

// Which resolver, if any, the head of an expression names.
//
// bTreatEveryResolverAsRaw is the exemption switch. With it on, this is the dry
// run's own predicate -- every resolver counts -- and that is how
// TheBanCPredicateSeesTheClass proves the predicate sees anything at all.
ResolverKind_t ResolverAtHeadOf( const std::string &rhs, bool bTreatEveryResolverAsRaw )
{
	// The TERNARY form, and it is here because the live simulation found it:
	//
	//     C_BaseEntity *pEntity = it->m_pRenderable
	//         ? it->m_pRenderable->GetIClientUnknown()->GetBaseEntity() : NULL;
	//     if ( pEntity )
	//         pEntity->IsNPC();          <-- viewrender.cpp:4172, jumped to 0
	//
	// The guard on the left of the '?' is a NULL check on the RECEIVER, which
	// says nothing about which hierarchy the receiver belongs to -- so the site
	// reads as careful and is not. Neither the dry run's census nor the first
	// version of this predicate could see it, because both looked for the
	// resolver at the head of the expression and the head is the condition.
	const size_t question = rhs.find( '?' );
	if ( question != std::string::npos )
	{
		const ResolverKind_t kind =
			ResolverAtHeadOf( rhs.substr( question + 1 ), bTreatEveryResolverAsRaw );
		if ( kind != RESOLVER_NONE )
			return kind;
	}

	const size_t start = SkipLeadingCast( rhs );
	const std::string rest = rhs.substr( start );

	// Every '(' whose whole prefix is one receiver run is a candidate call.
	// Chains produce more than one, and the resolver may be any of them.
	for ( size_t paren = 0; paren < rest.size(); paren++ )
	{
		if ( !IsReceiverChar( rest[ paren ] ) )
			break;
		if ( rest[ paren ] != '(' )
			continue;

		// The raw form first: GetBaseEntity with EMPTY parentheses.
		const bool bEmptyArgs = ( paren + 1 < rest.size() && rest[ paren + 1 ] == ')' );
		const std::string head = rest.substr( 0, paren + 1 );

		if ( bEmptyArgs )
		{
			const size_t n = strlen( "GetBaseEntity(" );
			if ( head.size() >= n && head.compare( head.size() - n, n, "GetBaseEntity(" ) == 0 )
			{
				// A word boundary before it, so "MyGetBaseEntity(" is not this.
				if ( head.size() == n || !IsIdentChar( head[ head.size() - n - 1 ] ) )
					return RESOLVER_RAW;
			}
		}

		for ( size_t i = 0; i < kNumPartitionedResolvers; i++ )
		{
			const size_t n = strlen( kPartitionedResolvers[ i ] );
			if ( head.size() < n )
				continue;
			if ( head.compare( head.size() - n, n, kPartitionedResolvers[ i ] ) != 0 )
				continue;
			if ( head.size() > n && IsIdentChar( head[ head.size() - n - 1 ] ) )
				continue;

			return bTreatEveryResolverAsRaw ? RESOLVER_RAW : RESOLVER_PARTITIONED;
		}
	}

	return RESOLVER_NONE;
}

//-----------------------------------------------------------------------------
// One line, one binding. `<ident> = <resolver>( ... )`.
//
// Only the FIRST assignment on the line is considered, which is all any of the
// enumerated sites needs and keeps the predicate something a reader can hold in
// their head.
//-----------------------------------------------------------------------------
struct Binding_t
{
	Binding_t() : kind( RESOLVER_NONE ) {}

	std::string strVar;
	ResolverKind_t kind;
};

bool IsAssignmentAt( const std::string &s, size_t i )
{
	if ( s[ i ] != '=' )
		return false;
	if ( i + 1 < s.size() && s[ i + 1 ] == '=' )
		return false;
	if ( i == 0 )
		return false;

	const char prev = s[ i - 1 ];
	if ( prev == '=' || prev == '!' || prev == '<' || prev == '>' || prev == '+'
		|| prev == '-' || prev == '*' || prev == '/' || prev == '&' || prev == '|'
		|| prev == '^' || prev == '%' )
		return false;

	return IsIdentChar( prev );
}

Binding_t BindingOn( const std::string &squeezed, bool bTreatEveryResolverAsRaw )
{
	Binding_t out;

	for ( size_t i = 0; i < squeezed.size(); i++ )
	{
		if ( !IsAssignmentAt( squeezed, i ) )
			continue;

		const ResolverKind_t kind =
			ResolverAtHeadOf( squeezed.substr( i + 1 ), bTreatEveryResolverAsRaw );
		if ( kind == RESOLVER_NONE )
			continue;

		size_t start = i;
		while ( start > 0 && IsIdentChar( squeezed[ start - 1 ] ) )
			start--;

		if ( start == i )
			continue;

		out.strVar = squeezed.substr( start, i - start );
		out.kind = kind;
		return out;
	}

	return out;
}

//-----------------------------------------------------------------------------
// A NULL test on a named variable, within the window.
//
// "if ( pEnt )", "if ( !pEnt )", "if ( pEnt && ... )", "while ( ent )",
// "if ( !pEnt || ... )" and "Assert( pEnt )" all count -- and so does
// "if ( !pEntity->BlocksLOS() )", which is a site with NO check at all and is
// the worse half of the same class. What they share is that the pointer's value
// decides what happens next, which is exactly what step 5 changes.
//-----------------------------------------------------------------------------
bool MentionsVarInABooleanPosition( const std::string &squeezed, const std::string &strVar )
{
	static const char *kKeywords[] = { "if(", "while(", "Assert(", "assert(", "&&", "||", "?" };
	static const size_t kNumKeywords = sizeof( kKeywords ) / sizeof( kKeywords[ 0 ] );

	size_t nEarliest = std::string::npos;
	for ( size_t i = 0; i < kNumKeywords; i++ )
	{
		const size_t at = squeezed.find( kKeywords[ i ] );
		if ( at != std::string::npos && at < nEarliest )
			nEarliest = at;
	}

	if ( nEarliest == std::string::npos )
		return false;

	for ( size_t at = squeezed.find( strVar, nEarliest );
		at != std::string::npos;
		at = squeezed.find( strVar, at + 1 ) )
	{
		const size_t end = at + strVar.size();

		// A whole identifier, not a substring of a longer one.
		if ( at > 0 && IsIdentChar( squeezed[ at - 1 ] ) )
			continue;
		if ( end < squeezed.size() && IsIdentChar( squeezed[ end ] ) )
			continue;

		// Negated: "!pEnt", "!pEnt->Something()".
		if ( at > 0 && squeezed[ at - 1 ] == '!' )
			return true;

		// Or used bare as a truth value: "pEnt)", "pEnt&&", "pEnt||", "pEnt,".
		if ( end >= squeezed.size() )
			continue;
		if ( squeezed[ end ] == ')' || squeezed[ end ] == ',' )
			return true;
		if ( squeezed.compare( end, 2, "&&" ) == 0 || squeezed.compare( end, 2, "||" ) == 0 )
			return true;
	}

	return false;
}

bool MentionsVarInAPlayerCast( const std::string &squeezed, const std::string &strVar )
{
	if ( !HasPlayerCast( squeezed ) )
		return false;

	for ( size_t at = squeezed.find( strVar );
		at != std::string::npos;
		at = squeezed.find( strVar, at + 1 ) )
	{
		const size_t end = at + strVar.size();
		if ( at > 0 && IsIdentChar( squeezed[ at - 1 ] ) )
			continue;
		if ( end < squeezed.size() && IsIdentChar( squeezed[ end ] ) )
			continue;
		return true;
	}

	return false;
}

// Six, the same window the dry run's census used. Long enough for a binding, a
// blank line, a brace and a comment; short enough that a variable reused far
// below is not attributed to this binding.
const int kWindow = 6;

//-----------------------------------------------------------------------------
// Whether a line is compiled in a SOURCESHARP build.
//
// Only the SOURCESHARP conditionals are evaluated; every other #if is treated
// as live. Being wrong in that direction costs a false positive somebody has to
// look at, which is the right way round for a ban.
//-----------------------------------------------------------------------------

class CGuardStack
{
public:
	CGuardStack() {}

	// True when the line just past the directives seen so far is compiled.
	bool Live() const
	{
		for ( size_t i = 0; i < m_Stack.size(); i++ )
		{
			if ( !m_Stack[ i ] )
				return false;
		}
		return true;
	}

	void Directive( const std::string &line )
	{
		const std::string s = Squeezed( line );
		if ( s.empty() || s[ 0 ] != '#' )
			return;

		// Longest keyword first: "ifndef" would otherwise be read as "if".
		if ( Starts( s, "#ifndef" ) )
		{
			const bool bOurs = Starts( s, "#ifndefSOURCESHARP" );
			Push( /*bLive=*/ !bOurs, bOurs );
			return;
		}
		if ( Starts( s, "#ifdef" ) )
		{
			const bool bOurs = Starts( s, "#ifdefSOURCESHARP" );
			Push( /*bLive=*/ true, bOurs );
			return;
		}
		if ( Starts( s, "#if" ) )
		{
			// #if defined(SOURCESHARP) / #if !defined(SOURCESHARP), and every
			// other #if, which is treated as live.
			const bool bOursDefined = ( s.find( "#ifdefined(SOURCESHARP)" ) == 0 );
			const bool bOursNotDefined = ( s.find( "#if!defined(SOURCESHARP)" ) == 0 );
			Push( /*bLive=*/ !bOursNotDefined, bOursDefined || bOursNotDefined );
			return;
		}
		if ( Starts( s, "#elif" ) )
		{
			// An #elif arm of a SOURCESHARP conditional is not this build's.
			if ( !m_Stack.empty() && m_IsSourceSharp.back() )
				m_Stack.back() = false;
			return;
		}
		if ( Starts( s, "#else" ) )
		{
			if ( !m_Stack.empty() && m_IsSourceSharp.back() )
				m_Stack.back() = !m_Stack.back();
			return;
		}
		if ( Starts( s, "#endif" ) )
		{
			if ( !m_Stack.empty() )
			{
				m_Stack.pop_back();
				m_IsSourceSharp.pop_back();
			}
			return;
		}
	}

private:
	static bool Starts( const std::string &s, const char *pszPrefix )
	{
		return s.compare( 0, strlen( pszPrefix ), pszPrefix ) == 0;
	}

	void Push( bool bLive, bool bIsSourceSharp )
	{
		m_Stack.push_back( bLive );
		m_IsSourceSharp.push_back( bIsSourceSharp );
	}

	std::vector< bool > m_Stack;
	std::vector< bool > m_IsSourceSharp;
};

//-----------------------------------------------------------------------------
// The escape hatch, and the only one.
//
// CServerNetworkProperty has a member called GetBaseEntity() that has nothing
// to do with the partition: it returns m_pOuter, the entity that OWNS the
// property, and a standalone entity has no CServerNetworkProperty at all so it
// can never see one. A name collision, not a hazard.
//
// Marked rather than pattern-matched, because "is this receiver an
// IServerUnknown?" is a type question and this is a line scanner. The marker is
// counted and its files are named by TheExemptionsAreWhereTheySay, so it cannot
// be sprinkled around to silence a real finding without that fixture noticing.
//-----------------------------------------------------------------------------
const char kExemptionMarker[] = "SS_NOT_THE_PARTITION";

//-----------------------------------------------------------------------------
// The scan.
//-----------------------------------------------------------------------------

struct Hit_t
{
	std::string strFile;
	int nLine;
	std::string strText;
	int nFlagLine;
	std::string strFlagText;
};

struct ScanResult_t
{
	ScanResult_t() : nFiles( 0 ), nLines( 0 ), nSkipped( 0 ), nHere( 0 ),
		nEvictions( 0 ), nExemptions( 0 ) {}

	int nFiles;
	int nLines;
	int nSkipped;		// files present on disk but evicted from this .so
	int nHere;			// of those, the ones naming a file this scan WALKS --
						// the honest denominator for the coverage ratio, since
						// an entry under sourcesharp/ was never going to be read
	int nEvictions;		// entries in the target's compiled-source list (the
						// name is from the removal-list era) -- see CompiledPaths
	int nExemptions;	// SS_NOT_THE_PARTITION markers honoured

	std::vector< Hit_t > CastHits;			// ban A
	std::vector< Hit_t > TwoStatementHits;	// ban B
	std::vector< Hit_t > RawNullCheckHits;	// ban C
	std::vector< Hit_t > EveryResolverHits;	// ban C with the exemption lifted

	std::vector< std::string > FilesRead;
	std::vector< std::string > ExemptionFiles;
};

//-----------------------------------------------------------------------------
// Which files this mod actually compiles.
//
// The tree carries far more source than the mod builds, and a banned shape
// inside a file the compiler never sees is not a hazard. On the first run this
// mattered immediately -- the scan reported three sites, and two of them
// (sceneentity.cpp, teamplay_gamerules.cpp) were evicted files. The third,
// baseentity.cpp, was live and real, and was not in the hand-taken inventory
// of fourteen.
//
// Read from the build rather than listed here, so that a file returning to the
// build brings its sites back under the ban on the same commit, with nobody
// having to remember. Since Phase 6b that is the target's compiled sources --
// server_sourcesharp / client_sourcesharp, read through cmake_graph.h from
// compile_commands.json and cross-checked against the target's CMakeLists; it
// throws on a missing, empty or stale input.
//
// BOTH PROJECTS ANSWER THIS THE SAME WAY NOW, and it is the better of the two
// answers they used to give.
//
// This block used to say that the client "still includes client_base.vpc and
// subtracts from it, so what it does NOT compile is what its `-$File` lines
// name", and that the flattened server answered it the other way round. Both
// are flattened now -- plan_vpc.md and plan_vpc_client.md -- and neither has a
// `-$File` at all. Each lists the files it compiles (145 and 287 translation
// units) and nothing else, so the question is A FILE IS NOT IN THE BUILD
// UNLESS A `$File` NAMES IT, on both sides.
//
// That is strictly better than the subtractive form and the reason is worth
// keeping: a removal list can only ever name files somebody thought to write
// down, so every file that was never in the project at all read as PRESENT.
// The additive list is exact by construction. On the client that turned 229
// `-$File` basenames into 288 `$File` ones and brought several hundred files
// that were never in the build under the ban for the first time.
//
// FULL PATHS, AND THEY USED TO BE BASENAMES. The membership question is still
// answered on the basename -- see IsEvicted, whose behaviour has not changed --
// but the DIRECTORY is what says whether this scan could ever have read an
// entry at all, and that is the other question the same parse now answers.
//
// WHY THE SECOND QUESTION EXISTS. The ratio arm in TheScannerFoundTheSourceTree
// holds the files read against the entries listed, and it went red on a commit
// that ADDED a translation unit -- because the entry landed in the denominator
// (every $File is counted) and never in the numerator (this scan walks
// game/server and game/shared, and the file was under sourcesharp/game/shared).
// That is not a coincidence about one file: this port's whole direction is
// moving code INTO sourcesharp/, so the denominator grows on exactly the
// commits that represent progress and the ratio falls on every one of them.
// It is the same disease the absolute floor had, one level up -- a number that
// has to be revised every time the port succeeds is not measuring what it was
// written to measure -- so the denominator is now the entries the scan can
// reach rather than all of them.
//
// The paths themselves are farm-relative ("game/server/x.cpp", once
// "$SRCDIR\game\server\x.cpp" in the vpc), and two files with the same
// basename in game/server and game/shared do not exist.
//-----------------------------------------------------------------------------
const std::vector< std::string > &CompiledPaths( const char *pszWhich )
{
	static std::vector< std::string > s_Server;
	static std::vector< std::string > s_Client;
	static bool s_bServerDone = false;
	static bool s_bClientDone = false;

	const bool bServer = ( strcmp( pszWhich, "server" ) == 0 );
	std::vector< std::string > &out = bServer ? s_Server : s_Client;
	bool &bDone = bServer ? s_bServerDone : s_bClientDone;

	if ( bDone )
		return out;

	// CompiledSources throws on a missing, empty or stale input -- gtest
	// reports it as a failure of the calling test -- so bDone is set only after
	// a complete read, and an empty list can never be cached as the answer.
	const std::set< std::string > srcs =
		cmake_graph::CompiledSources( bServer ? cmake_graph::kServer : cmake_graph::kClient );
	out.assign( srcs.begin(), srcs.end() );
	bDone = true;

	return out;
}

//-----------------------------------------------------------------------------
// The basename out of one listed path. Backslashes AND forward slashes (the
// vpc era wrote the first; the CMake lists write the second).
//-----------------------------------------------------------------------------
std::string BasenameOf( const std::string &strPath )
{
	const size_t slash = strPath.find_last_of( "\\/" );
	return slash == std::string::npos ? strPath : strPath.substr( slash + 1 );
}

//-----------------------------------------------------------------------------
// "Is this file outside the build?"
//
// ABSENCE from the compiled list, on both sides -- see CompiledPaths above.
// The function's name is from the era when the client answered it the other
// way round; what it means has not changed, and neither has what it compares:
// the basename, taken from the stored path at the point of comparison rather
// than at the point of parsing.
//-----------------------------------------------------------------------------
bool IsEvicted( const char *pszWhich, const std::string &strBasename )
{
	const std::vector< std::string > &listed = CompiledPaths( pszWhich );

	for ( size_t i = 0; i < listed.size(); i++ )
	{
		if ( BasenameOf( listed[ i ] ) == strBasename )
			return false;
	}

	return true;
}

//-----------------------------------------------------------------------------
// How many listed entries name a file THIS SCAN WALKS -- the honest denominator
// for the coverage ratio.
//
// ScanDirectory is pointed at game/server and game/shared (and game/client and
// game/client/replay on the other side); an entry under sourcesharp/, public/
// or common/ is one the scan was never going to read, so counting it as a miss
// says the scan lost coverage when nothing of the sort happened.
//
// The prefix test is on "game/" at the START of the farm-relative path, and it
// is not the same as asking whether the path CONTAINS "/game/": every
// sourcesharp entry contains it, because they are spelled
// "sourcesharp/game/shared/x.cpp". That near-miss is the whole reason this is
// a function with a comment on it.
//-----------------------------------------------------------------------------
int EntriesThisScanCanReach( const char *pszWhich )
{
	const std::vector< std::string > &listed = CompiledPaths( pszWhich );
	const std::string strPrefix = "game/";

	int nHere = 0;

	for ( size_t i = 0; i < listed.size(); i++ )
	{
		if ( listed[ i ].compare( 0, strPrefix.size(), strPrefix ) == 0 )
			nHere++;
	}

	return nHere;
}

// buildcpptests runs each binary with src/ as the working directory. Kept as a
// list rather than one path so that running the binary by hand from the repo
// root also works, and so that "found no directory" is distinguishable from
// "found a directory with nothing in it".
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

void RecordHit( std::vector< Hit_t > &out, const std::string &strPath,
	int nLine, const std::string &strText, int nFlagLine, const std::string &strFlagText )
{
	Hit_t hit;
	hit.strFile = strPath;
	hit.nLine = nLine;
	hit.strText = strText;
	hit.nFlagLine = nFlagLine;
	hit.strFlagText = strFlagText;
	out.push_back( hit );
}

void ScanFile( const std::string &strPath, ScanResult_t &result )
{
	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return;

	result.nFiles++;
	result.FilesRead.push_back( strPath );

	// Read the whole file first: bans B and C look FORWARD from a binding, and
	// a streaming scan cannot.
	std::vector< std::string > lines;
	std::string line;
	while ( std::getline( file, line ) )
		lines.push_back( line );

	result.nLines += (int)lines.size();

	// The guard state at every line, precomputed for the same reason.
	std::vector< bool > live( lines.size(), true );
	{
		CGuardStack guards;
		for ( size_t i = 0; i < lines.size(); i++ )
		{
			const std::string squeezed = Squeezed( lines[ i ] );
			if ( !squeezed.empty() && squeezed[ 0 ] == '#' )
			{
				guards.Directive( lines[ i ] );
				live[ i ] = false;
				continue;
			}
			live[ i ] = guards.Live();
		}
	}

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( !live[ i ] )
			continue;
		if ( IsWholeLineComment( lines[ i ] ) )
			continue;

		// --- ban A, one line ------------------------------------------------
		if ( LineHasBannedCast( lines[ i ] ) )
			RecordHit( result.CastHits, strPath, (int)i + 1, lines[ i ], 0, "" );

		const std::string squeezed = Squeezed( lines[ i ] );

		// --- bans B and C, a binding and a window ---------------------------
		const bool bExempt =
			( lines[ i ].find( kExemptionMarker ) != std::string::npos );
		if ( bExempt )
		{
			result.nExemptions++;
			if ( std::find( result.ExemptionFiles.begin(), result.ExemptionFiles.end(), strPath )
				== result.ExemptionFiles.end() )
			{
				result.ExemptionFiles.push_back( strPath );
			}
		}

		const Binding_t strict = BindingOn( squeezed, /*bTreatEveryResolverAsRaw=*/ false );
		const Binding_t loose  = BindingOn( squeezed, /*bTreatEveryResolverAsRaw=*/ true );

		if ( loose.kind == RESOLVER_NONE )
			continue;

		bool bDidTwoStatement = false;
		bool bDidRaw = false;
		bool bDidLoose = false;

		for ( size_t j = i + 1; j < lines.size() && (int)( j - i ) <= kWindow; j++ )
		{
			if ( IsWholeLineComment( lines[ j ] ) )
				continue;
			if ( !live[ j ] )
				continue;

			const std::string nextSqueezed = Squeezed( lines[ j ] );

			if ( !bDidTwoStatement
				&& MentionsVarInAPlayerCast( nextSqueezed, loose.strVar ) )
			{
				bDidTwoStatement = true;
				if ( !bExempt )
				{
					RecordHit( result.TwoStatementHits, strPath, (int)i + 1, lines[ i ],
						(int)j + 1, lines[ j ] );
				}
			}

			if ( !bDidLoose
				&& MentionsVarInABooleanPosition( nextSqueezed, loose.strVar ) )
			{
				bDidLoose = true;
				RecordHit( result.EveryResolverHits, strPath, (int)i + 1, lines[ i ],
					(int)j + 1, lines[ j ] );

				if ( !bDidRaw && !bExempt && strict.kind == RESOLVER_RAW )
				{
					bDidRaw = true;
					RecordHit( result.RawNullCheckHits, strPath, (int)i + 1, lines[ i ],
						(int)j + 1, lines[ j ] );
				}
			}

			if ( bDidTwoStatement && bDidLoose )
				break;
		}
	}
}

void ScanDirectory( const char *pszWhich, const std::string &strDir, ScanResult_t &result )
{
	DIR *pDir = opendir( strDir.c_str() );
	if ( !pDir )
		return;

	std::vector< std::string > names;
	while ( struct dirent *pEnt = readdir( pDir ) )
	{
		const char *pszName = pEnt->d_name;
		const size_t len = strlen( pszName );
		if ( len < 5 )
			continue;
		if ( strcmp( pszName + len - 4, ".cpp" ) != 0 && strcmp( pszName + len - 2, ".h" ) != 0 )
			continue;
		names.push_back( pszName );
	}
	closedir( pDir );

	// Sorted so a failure lists its hits in the same order every run.
	std::sort( names.begin(), names.end() );

	for ( size_t i = 0; i < names.size(); i++ )
	{
		if ( IsEvicted( pszWhich, names[ i ] ) )
		{
			result.nSkipped++;
			continue;
		}

		ScanFile( strDir + "/" + names[ i ], result );
	}
}

//-----------------------------------------------------------------------------
// plan_entity.md step 4e moved the shared trace-filter family out of
// game/shared/util_shared.cpp and into the shim, and SEVEN of this scan's
// server resolver sites went with it -- the walk covers src/game/server and
// src/game/shared, so an eviction into sourcesharp/ takes its sites out of
// reach exactly as saverestore.cpp's did.
//
// SO THE FILE IS SCANNED BY NAME rather than the floor being lowered. Lowering
// it would have been the wrong repair twice over: the sites did not go away,
// and a floor that follows the measurement down stops being a floor. Widening
// the walk to the whole of sourcesharp/game/shared would be a different change
// with a different owner -- this scan's coverage of the shim is a decision
// recorded at TheInventoryOfScannedFiles, and one file the ban already covered
// staying covered after it moved is not that decision.
//
// Both sides name it, because both link lines compile it.
//-----------------------------------------------------------------------------
const char *const kTraceFilterLibrary =
	"/../sourcesharp/game/shared/trace_filter_library.cpp";

//-----------------------------------------------------------------------------
// The Phase 4 exit's cluster-E lane (p4-util) did the same thing three more
// times, and it took the SERVER count 25 -> 22 before these lines were added:
//
//   * UTIL_ImpactTrace's `CBaseEntity *pEntity = TraceEntity( *pTrace );`
//     followed by `if ( !pEntity )` left game/server/util.cpp for
//     server_util_library.cpp, along with the two GetContainingEntity sites
//     inside the check-client subsystem;
//   * UTIL_DecalTrace's identical pair left game/shared/util_shared.cpp for
//     util_shared_library.cpp;
//   * ApplyMultiDamage's entity-list lookup left game/shared/takedamageinfo.cpp
//     for takedamageinfo_library.cpp.
//
// Same repair, for the same two reasons 4e gave: the sites did not go away, and
// a floor that follows the measurement down stops being a floor. This is NOT
// the decision to widen the walk to all of sourcesharp/game -- it is the
// existing coverage of three files following them to their new addresses.
//
// server_util_library.cpp is SERVER-ONLY, because game/server/util.cpp is; the
// other two are on both link lines and both scans name them.
//-----------------------------------------------------------------------------
const char *const kServerUtilLibrary =
	"/../sourcesharp/game/server/server_util_library.cpp";
const char *const kUtilSharedLibrary =
	"/../sourcesharp/game/shared/util_shared_library.cpp";
const char *const kTakeDamageInfoLibrary =
	"/../sourcesharp/game/shared/takedamageinfo_library.cpp";

// THE MAP PARSE, NAMED EXPLICITLY for the same reason the three above are: the
// scan walks game/server and game/shared, and plan_shim.md Phase 2c relocated
// game/server/mapentities.cpp to sourcesharp/game/server/shell_mapparse.cpp.
// A relocated file that stops being scanned takes its ban with it SILENTLY --
// every absence assertion about it would then pass over a file nobody read --
// which is precisely what TheBanCPredicateSeesTheClass's named-file row exists
// to catch, and it caught this. SERVER ONLY: the map parse is not on the client
// link line.
const char *const kMapParse =
	"/../sourcesharp/game/server/shell_mapparse.cpp";

const ScanResult_t &ServerScan()
{
	static ScanResult_t s_result;
	static bool s_bDone = false;

	if ( !s_bDone )
	{
		s_bDone = true;
		s_result.nEvictions = (int)CompiledPaths( "server" ).size();
		s_result.nHere      = EntriesThisScanCanReach( "server" );

		const char *pszRoot = SourceRoot();
		if ( pszRoot )
		{
			ScanDirectory( "server", std::string( pszRoot ) + "/server", s_result );
			ScanDirectory( "server", std::string( pszRoot ) + "/shared", s_result );
			ScanFile( std::string( pszRoot ) + kTraceFilterLibrary, s_result );
			ScanFile( std::string( pszRoot ) + kServerUtilLibrary, s_result );
			ScanFile( std::string( pszRoot ) + kUtilSharedLibrary, s_result );
			ScanFile( std::string( pszRoot ) + kTakeDamageInfoLibrary, s_result );
			ScanFile( std::string( pszRoot ) + kMapParse, s_result );
		}
	}

	return s_result;
}

const ScanResult_t &ClientScan()
{
	static ScanResult_t s_result;
	static bool s_bDone = false;

	if ( !s_bDone )
	{
		s_bDone = true;
		s_result.nEvictions = (int)CompiledPaths( "client" ).size();
		s_result.nHere      = EntriesThisScanCanReach( "client" );

		const char *pszRoot = SourceRoot();
		if ( pszRoot )
		{
			ScanDirectory( "client", std::string( pszRoot ) + "/client", s_result );
			ScanDirectory( "client", std::string( pszRoot ) + "/client/replay", s_result );
			ScanDirectory( "client", std::string( pszRoot ) + "/shared", s_result );
			ScanFile( std::string( pszRoot ) + kTraceFilterLibrary, s_result );
			ScanFile( std::string( pszRoot ) + kUtilSharedLibrary, s_result );
			ScanFile( std::string( pszRoot ) + kTakeDamageInfoLibrary, s_result );
		}
	}

	return s_result;
}

bool ScannedFileNamed( const ScanResult_t &result, const char *pszSuffix )
{
	for ( size_t i = 0; i < result.FilesRead.size(); i++ )
	{
		const std::string &s = result.FilesRead[ i ];
		const size_t n = strlen( pszSuffix );
		if ( s.size() >= n && s.compare( s.size() - n, n, pszSuffix ) == 0 )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// Did a predicate find anything IN THIS FILE?
//
// The counterpart of ScannedFileNamed, and it exists for the re-key in
// TheBanCPredicateSeesTheClass: that fact used to prove the predicate still
// matched by counting its hits, and the flip took the count below its floor by
// evicting the files the hits were in. A count cannot tell "the predicate
// broke" from "a file left the build"; a NAMED FILE can, because an eviction
// reds the row for the file it evicted and leaves the others green.
//
// Matched on a suffix for the same reason ScannedFileNamed is: the recorded
// path is whatever ScanDirectory or ScanFile was handed, which carries a
// "game/../sourcesharp/..." spelling for the files scanned by name.
//-----------------------------------------------------------------------------
bool HitsInFileEnding( const std::vector< Hit_t > &hits, const char *pszSuffix )
{
	const size_t n = strlen( pszSuffix );
	for ( size_t i = 0; i < hits.size(); i++ )
	{
		const std::string &s = hits[ i ].strFile;
		if ( s.size() >= n && s.compare( s.size() - n, n, pszSuffix ) == 0 )
			return true;
	}
	return false;
}

std::string Describe( const std::vector< Hit_t > &hits )
{
	std::string out;
	for ( size_t i = 0; i < hits.size(); i++ )
	{
		char szLine[ 64 ];
		snprintf( szLine, sizeof( szLine ), "%d", hits[ i ].nLine );
		out += "\n    " + hits[ i ].strFile + ":" + szLine;
		out += "\n        " + hits[ i ].strText;
		if ( hits[ i ].nFlagLine )
		{
			snprintf( szLine, sizeof( szLine ), "%d", hits[ i ].nFlagLine );
			out += "\n    ...:";
			out += szLine;
			out += "\n        " + hits[ i ].strFlagText;
		}
	}
	return out;
}

// The whole text of one file, for the fixtures that read a definition rather
// than a shape.
std::string SlurpFromSourceRoot( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
		std::istreambuf_iterator< char >() );
}

// The same, with whole-line comments removed.
//
// THIS EXISTS BECAUSE A FIXTURE WENT GREEN WHEN IT SHOULD NOT HAVE.
// TheResolverListIsHonest below asks whether a resolver still names the
// partition; the resolver was mutated to stop asking, and the fixture passed,
// because the file's own PROSE says "SourceSharp_SDKEntityFromUnknown" several
// times over. A check that a comment can satisfy is a check about comments.
// Whole-line comments out of an ALREADY-READ span. Factored out of
// SlurpCodeFromSourceRoot so a test that has to locate a region by a comment
// marker can still drop the prose INSIDE that region -- which
// ToBasePlayerAsksThePartitionBeforeItDereferences below now does, having been
// found counting a token out of a comment that names it.
std::string CodeOnly( const std::string &text )
{
	std::string out;
	out.reserve( text.size() );

	size_t at = 0;
	while ( at < text.size() )
	{
		size_t end = text.find( '\n', at );
		if ( end == std::string::npos )
			end = text.size();

		const std::string line = text.substr( at, end - at );
		if ( !IsWholeLineComment( line ) )
		{
			out += line;
			out += '\n';
		}

		at = end + 1;
	}

	return out;
}

std::string SlurpCodeFromSourceRoot( const char *pszRelative )
{
	return CodeOnly( SlurpFromSourceRoot( pszRelative ) );
}

//-----------------------------------------------------------------------------
// FIRST: the scanner works. Every assertion below this point is worthless
// without these, because a scanner that reads nothing reports no violations.
//-----------------------------------------------------------------------------

TEST( SdkPlayerCast, TheScannerFoundTheSourceTree )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no game/ directory found from the working directory; run this from src/";

	const ScanResult_t &server = ServerScan();

	// game/server and game/shared are hundreds of files, but the ban only
	// covers the ones this mod COMPILES, and after the flattening that set is
	// known exactly rather than approximated.
	//
	// THESE FLOORS CAME DOWN, from 200 files and 100000 lines, and it is worth
	// saying why so the next person does not read it as a test being loosened
	// to go green. The scan is 140 files and 97289 lines now. It was over 200
	// before because the old answer to "is this file in the build?" was
	// membership of server_csharp.vpc's `-$File` list -- which could only name
	// files somebody had thought to write down. Hundreds of files in
	// game/server were never in this project to be evicted from it, no
	// directive named them, and the ban scanned every one. server_csharp.vpc
	// lists what it compiles now, so the question is answered exactly and the
	// scan covers the real build and nothing else.
	//
	// No coverage was lost: every file the compiler sees is still scanned, and
	// a banned shape in a file the compiler never sees was never a hazard.
	// AND DOWN AGAIN, 120 -> 80, for the same kind of reason and worth the same
	// sentence. The temp entity port evicted forty-one more server files --
	// te.cpp, basetempentity.cpp, te_basebeam.cpp and the thirty-eight
	// te_*.cpp -- because all forty-four temp entity classes are C#
	// declarations now. The scan is 99 files; it was 140.
	//
	// AND IT NEEDED LOWERING A THIRD TIME, WHICH IS WHERE THE ABSOLUTE FLOOR
	// STOPPED EARNING ITS KEEP. plan_server_all.md Waves 0-3 and the Wave 8
	// globals merge took seventeen more files out of the server list; the scan
	// reached exactly 80 and the `> 80` went red on legitimate progress. A
	// number that has to be revised every time the port succeeds is not
	// measuring the thing it was written to measure.
	//
	// So it is RELATIVE now. What the floor is really guarding is the $File
	// parse breaking and finding nothing -- and the parse's own output is the
	// honest denominator.
	//
	// AND THE DENOMINATOR HAD TO BE NARROWED, on the commit that added
	// sourcesharp/game/shared/shell_acquire_reasons.cpp. It was nEvictions --
	// EVERY entry the .vpc lists -- and this scan walks game/server and
	// game/shared only, so an entry under sourcesharp/ landed in the
	// denominator and could never land in the numerator. The ratio stood at
	// 65/162 = 40.1% against a 40% floor: ONE new translation unit took it red,
	// and the file it was red about was a file the scan was never asked to
	// read.
	//
	// That is the absolute floor's disease one level up. This port's direction
	// is moving code INTO sourcesharp/, so the old denominator grew on exactly
	// the commits that represent progress and the ratio fell on every one of
	// them -- "a number that has to be revised every time the port succeeds is
	// not measuring the thing it was written to measure", which is what the
	// paragraph above says about the number it replaced.
	//
	// nHere is the entries naming a file under $SRCDIR\game\, which is what
	// ScanDirectory is pointed at. Measured at this tip: the server .vpc lists
	// 163 entries, 64 of them under game/, and the scan reads 65 files there
	// (the extra is a header, which the .vpc does not list and the scan does
	// read). So the real relationship is that the scan reads AT LEAST every
	// listed file it can reach, and the arm says so at 90% rather than 40% --
	// a floor with headroom that a sourcesharp addition cannot move at all,
	// because such an addition raises neither side.
	//
	// AND THE HARD FLOOR OF 20 WENT THE SAME WAY AS THE THREE BEFORE IT.
	//
	// It read: "a hard floor of 20 that is not expected to move again: a parse
	// that finds nothing gives zero, and no amount of real progress in this
	// port takes a live server below twenty compiled game/ files while there
	// is still a game." plan_shim.md Phase 1d deleted nine stock translation
	// units from server_csharp.vpc -- AI_Criteria, game, saverestore_gamedll,
	// textstatsmgr, timedeventmgr, GameStats_BasicStatsFunctions,
	// mp_shareddefs, predictableid, takedamageinfo -- and the entries under
	// $SRCDIR\game\ went 23 -> 14. The floor went red on exactly the progress
	// it said could not reach it, for the fourth time in this file's history.
	//
	// It will not survive being raised, either: Phases 2 through 5 move every
	// remaining stock game/ row out of this project, so the honest limit of
	// this quantity is ZERO and any constant above it is a date stamp.
	//
	// WHAT THE FLOOR WAS REALLY GUARDING is a $File parse that silently
	// matches nothing -- because nHere == 0 makes the ratio arm below
	// `nFiles * 100 > 0`, which passes on anything. So that is what is
	// asserted now, in the two forms that a shrinking build cannot touch: the
	// parse produced entries at all, and it produced a NAMED one that this
	// scan really walks. A parse that has lost a spelling gives zero and a
	// parse that is working gives usercmd.cpp whatever else has been deleted.
	EXPECT_GT( server.nFiles, 0 )
		<< "the scan read no files at all -- it is pointed at game/server and "
		<< "game/shared, so this is the scan and not the build";
	// RETIRED HERE, BY THE LANE THAT EMPTIED THE FOLDER, which is what the
	// paragraph above asked for: `EXPECT_GT( server.nHere, 0 )` stood where
	// this does, and plan_shim.md Phase 2 drove the population it guarded to
	// exactly zero. Its message said "that is the parse breaking, not the port
	// succeeding"; today the second reading is the true one for the first
	// time, so the assertion is inverted rather than lowered.
	EXPECT_EQ( 0, server.nHere )
		<< "server_sourcesharp compiles " << server.nHere << " entr(y/ies) under game/. "
		<< "Phase 2's exit condition is that there are NONE -- a file that must "
		<< "stay native relocates under sourcesharp/ with its reason letter. A "
		<< "row reappearing here is a stock file re-entering the build.";

	// THE NAMED CONTROL IS DERIVED NOW, NOT SPELLED, and that is the fifth
	// time a hardcoded name in this block has gone stale on progress rather
	// than on a defect. It named usercmd.cpp, and plan_shim.md Phase 2d
	// RELOCATED usercmd.cpp to sourcesharp/game/shared -- it did not leave the
	// build, it left `game/`, which is the whole point of Phase 2 and which
	// this test's own prose above predicts will happen to every remaining row.
	//
	// So the control asks the .vpc which row to name. Whatever the FIRST
	// `$SRCDIR\\game\\` path in the script is, the parse must have produced
	// it: a parse that has lost a spelling gives zero and a parse that is
	// working gives that row whatever else has been deleted. It cannot be made
	// stale by a deletion, and when the population finally reaches zero it is
	// EXPECT_GT( server.nHere, 0 ) above that has to be retired -- by the lane
	// that empties the folder, as the Phase 2 exit condition, not by a lane
	// patching a name.
	const std::vector< std::string > &listedHere = CompiledPaths( "server" );

	const std::string strGamePrefix = "game/";
	std::string strFirstUnderGame;
	for ( size_t i = 0; i < listedHere.size(); i++ )
	{
		if ( listedHere[ i ].compare( 0, strGamePrefix.size(), strGamePrefix ) == 0 )
		{
			strFirstUnderGame = listedHere[ i ];
			break;
		}
	}

	// The parse's health can no longer be shown by a game/ row, because there
	// are none. It is shown by the property that survives an empty folder: the
	// parse produced rows AT ALL, and it produced the one this scan is pointed
	// at. A parse that has lost a spelling gives zero rows and fails here.
	EXPECT_GT( listedHere.size(), 0u )
		<< "server_sourcesharp's source list produced no rows at all -- "
		<< "that is the reader, not the build";
	EXPECT_TRUE( strFirstUnderGame.empty() )
		<< "the parse found " << strFirstUnderGame << " under " << strGamePrefix
		<< ", and Phase 2's exit condition is that no such row exists. This is "
		<< "the same claim as the count above, reached from the path list "
		<< "rather than the count, so the two fail together or not at all.";
	// Vacuous BY CONSTRUCTION now (nHere is 0 and so is the product), which is
	// not the same as vacuous by breakage -- the assertions above are what
	// catch that. Kept, because a row reappearing under game/ puts a real
	// population back in the denominator and this arm starts meaning something
	// again in the same commit.
	EXPECT_GE( server.nFiles * 100, server.nHere * 90 )
		<< "scanned " << server.nFiles << " files out of " << server.nHere
		<< " entries server_sourcesharp compiles UNDER game/, below 90%. Files leaving the "
		<< "build lower both numbers together, so a fall in the RATIO means "
		<< "the scan stopped reading files it should have read -- which is the "
		<< "failure an absolute floor was there to catch and kept mistaking "
		<< "for progress. Entries under sourcesharp/, public/ and common/ are "
		<< "deliberately not in the denominator: this scan does not walk them, "
		<< "so counting them as misses says coverage was lost when the only "
		<< "thing that happened is that the port moved a file.";
	// AND THE LINE FLOOR IS RELATIVE NOW, RE-CALIBRATED AT THE FLIP.
	//
	// It read `> 40000` and the armed tree reads 20246. What changed is not the
	// scanner: SS_CBASEENTITY_ALIAS went into server_csharp.vpc and the same
	// commit took 20 stock `$File` rows out of it -- util.cpp, entitylist.cpp,
	// physics_main.cpp, baseentity.cpp, baseentity_shared.cpp, cbase.cpp,
	// baseanimating.cpp and the rest of the removal set. Those are the biggest
	// files in game/server, so half the scanned line count left the build with
	// them, and 40000 was a number measured over a file set that no longer
	// exists.
	//
	// It is NOT simply re-pointed at 20246, and not lowered to some new
	// absolute either -- every paragraph above this one is about a floor that
	// had to be revised on each commit that represented progress, and a fourth
	// revision of the same kind would be the fourth admission that the number
	// is not measuring what it was written to measure.
	//
	// What the floor is actually guarding is A SCANNER THAT OPENED FILES AND
	// READ NOTHING OUT OF THEM -- ScanFile returning early, an ifstream that
	// never opened, a line loop that stopped at the first byte. nFiles catches
	// "opened nothing"; this catches "opened them and came back empty", and the
	// honest denominator for that is the files it says it opened. The armed
	// tree stands at 20246/27 = 750 lines per file and the stock SDK's
	// game/server has no plausible future in which its average compiled file is
	// under a hundred lines, so the arm has 7x of headroom and cannot be moved
	// at all by the next wave of evictions -- an eviction lowers both sides
	// together, which is the whole reason the files arm above was made relative
	// first.
	EXPECT_GT( server.nLines, server.nFiles * 100 )
		<< "scanned " << server.nLines << " lines out of " << server.nFiles
		<< " files -- under a hundred lines each, so the files were opened and "
		<< "not read. Evictions lower both sides together; only a scanner that "
		<< "stopped reading lowers the ratio.";

	// Plus a hard floor, on the same footing as the `nFiles > 20` above: a
	// parse that finds nothing gives zero, and a live server dll below five
	// thousand compiled lines of game/ source is not a port in progress, it is
	// a broken measurement.
	// The 5,000-line floor that stood here was the fifth hardcoded number in
	// this block to go stale on progress rather than on a defect: Phase 2
	// relocated every game/ row out, and the scan now reads 3,532 lines of a
	// build that is working exactly as intended. The per-file ratio above is
	// the part that catches a scanner which opened files without reading them,
	// and it does not care how many files there are.
	EXPECT_GT( server.nLines, 0 )
		<< "scanned no lines at all, so the $File parse or the reader is broken";

	// The other half of that: the scan must not have quietly become the whole
	// tree again, which is what a broken parse of the $File list would do.
	EXPECT_LT( server.nFiles, 300 )
		<< "scanned far more than this mod compiles; the $File parse is wrong";

	// 148 at the flip; it was 196, and before that over 200 for the wrong
	// reason -- the history of the number the arms below replaced. The client's
	// membership test was a removal list, which could only name files somebody
	// had written a -$File for, so every stock file that was never in the
	// project at all read as live and got scanned. client_csharp.vpc is
	// flattened now (plan_vpc_client.md) and the test is exact: these are the
	// files in game/client, game/client/replay and game/shared that the
	// project actually compiles. The floor moved because the answer got more
	// accurate, not because the scanner lost anything -- which is why the
	// shape assertion below is the one that matters.
	// AND THE CLIENT'S TWO FLOORS WERE RE-CALIBRATED AT THE FLIP, onto the same
	// relative shape the server already uses, for the same reason and by the
	// same commit.
	//
	// They read `nFiles > 150` and `nLines > 100000`; the armed tree reads 148
	// and 91333. client_csharp.vpc now defines SS_CBASEENTITY_ALIAS, and the
	// commit that armed it took the removal set out of the client's `$File`
	// list too -- cliententitylist.cpp, c_baseentity.cpp, c_baseanimating.cpp,
	// c_baseentity_statics.cpp, c_entitydissolve.cpp, fx_impact.cpp,
	// physics_main_client.cpp, shared/baseentity_shared.cpp,
	// shared/entitylist_base.cpp, shared/physics_main_shared.cpp and the rest.
	// Seven files short of 150 and 8667 lines short of 100000 is not a scanner
	// that stopped reading; it is those files no longer being in the build for
	// it to read.
	//
	// So they are re-stated rather than re-pointed. The paragraph on the server
	// ratio above gives the argument in full: a removal lowers the numerator
	// and the denominator TOGETHER, so a relative arm holds across the flip and
	// across every eviction after it, while an absolute one has to be revised
	// on each commit that represents progress.
	//
	// THE MARGIN IS NOT AN ACCIDENT AND IS WORTH WRITING DOWN. client_csharp.vpc
	// lists 153 entries under $SRCDIR\game\ and this scan walks game/client,
	// game/client/replay and game/shared, so eight of them -- the
	// game/client/game_controls/ subdirectory -- are entries it was never going
	// to reach. The ceiling is therefore 145 of 153 = 94.8%, the arm is at 90%,
	// and the only thing that can close that gap is the scan losing files it
	// should have read.
	// The floors fell 50 -> 40 when the client's input left (lane c6-input,
	// 2026-10-11: 48 scanned, 45 compiled under game/): the port shrinking the
	// fork's share, the case the paragraph above names; the ratio arm is the
	// one that still says whether the scan read what it should. 40 -> 35 when
	// game/client/ragdoll.cpp left (plan physics_bones_native_zero Phase 3,
	// 2026-10-12: 40 compiled under game/), for the same reason, and when
	// the client's Steam left (lane c6-input, 2026-10-11: 39 compiled under
	// game/ on its own base, clientsteamcontext.cpp and secure_command_line.cpp
	// gone); both are in at the client batch's rebase onto main.
	// 35 -> 28 in C8 (lane c6-input, 2026-10-11: 34 compiled under game/ after
	// gamevars_shared.cpp and sceneentity_shared.cpp left). 28 -> 24 in C7 step 2
	// (2026-10-12: 28 compiled under game/ once view_scene.cpp left). 24 -> 20
	// in C7 step 3 (2026-10-12: 22 compiled under game/ and 19,398 lines scanned
	// once view.cpp, viewrender.cpp and viewdebug.cpp left). Files 24 -> 20 in
	// C7 particles phase 1 (2026-10-12: 24 scanned once particle_proxies.cpp
	// left).
	// (One fewer compiled once the dead proxyentity.cpp left too, C9, lane c6-input.)
	// Floors 20 -> 16 in C9 (lane c6-input, 2026-10-12) once studio_shared.cpp left,
	// and 16 -> 12 once sequence_Transitioner.cpp and vgui_consolepanel.cpp left
	// (16 compiled under game/). nHere 12 -> 10 in C7 particles phase 4
	// (2026-10-18, on main 8605b6ea1: 11 compiled under game/ once
	// particlemgr.cpp and particles_new.cpp left). Files 12 -> 10 and nHere
	// 10 -> 8 when entity_client_tools.cpp and toolframework_client.cpp left
	// (SFM is dead, user ruling 2026-10-19: 12 scanned, 9 compiled under game/,
	// at 2b7da491f plus the SFM commits). nHere 8 -> 7 when flashlighteffect.cpp
	// left (C7 cluster 12, 2026-10-18, on main e070a68e7: 8 compiled under game/).
	// Files 10 -> 8 and nHere 7 -> 6 when clientleafsystem.cpp left (C7 cluster
	// 12 L2, 2026-10-19, on main 62e8b7d00 plus the lane: 10 scanned, 7 compiled
	// under game/). nHere 6 -> 5 when rendertexture.cpp left (C7 render
	// textures, 2026-10-19, on main a001d7a31 plus the lane: 6 compiled under
	// game/). Files 8 -> 7 and nHere 5 -> 4 when ScreenSpaceEffects.cpp left
	// (C7 screen-space effects, the same lane: 8 scanned, 5 compiled under
	// game/). Files 7 -> 6 and nHere 4 -> 3 when vgui_game_viewport.cpp left
	// (C7 viewport system, the same lane: 7 scanned, 4 compiled under game/).
	// Files 6 -> 5 and nHere 3 -> 2 when warp_overlay.cpp left (C7 glow
	// overlays, the same lane: 6 scanned, 3 compiled under game/). Files 5 -> 3
	// and nHere 2 -> 0 when glow_overlay.cpp and c_pixel_visibility.cpp left
	// (C7 glow overlays and pixel visibility, the same lane: 4 scanned,
	// clientshadowmgr.cpp the one compiled under game/).
	//
	// AND THEN THE CLIENT EMPTIED ITS game/ ROWS TOO, which is the server's
	// Phase 2 exit reached on the other side: clientshadowmgr.cpp, the client's
	// last fork TU, left client.so with C7 cluster 12 S2 (2026-10-19; the
	// shadow manager is managed's, src/client/managed/Render/Shadows/). The
	// client scan reads 3 files now, all of them the libraries ClientScan names
	// by path (trace_filter_library.cpp, util_shared_library.cpp,
	// takedamageinfo_library.cpp), and the walk of game/client and game/shared
	// reads none -- it still opens them, which is what the nSkipped arm below
	// proves. So the arms take the server's shape: nHere is inverted rather
	// than lowered (0 is the exit condition, not a broken parse), the files
	// floor is the three named libraries (> 3 -> >= 3: the walk contributes
	// nothing for it to be above), and the ratio arm is vacuous by construction
	// until a row reappears under game/.
	const ScanResult_t &client = ClientScan();
	EXPECT_GE( client.nFiles, 3 )
		<< "scanned " << client.nFiles << " client files -- fewer than the three "
		<< "libraries ClientScan names by path, so the reader is broken";
	EXPECT_EQ( 0, client.nHere )
		<< "client_sourcesharp compiles " << client.nHere << " entr(y/ies) under "
		<< "game/. It compiled none once clientshadowmgr.cpp left (C7 cluster 12 "
		<< "S2); a row reappearing here is a stock file re-entering the build.";
	EXPECT_GT( client.nFiles * 100, client.nHere * 90 )
		<< "scanned " << client.nFiles << " files out of " << client.nHere
		<< " entries client_sourcesharp compiles UNDER game/, below 90%. Files "
		<< "leaving the build lower both numbers together, so a fall in the "
		<< "RATIO means the scan stopped reading files it should have read.";
	EXPECT_GT( client.nLines, client.nFiles * 100 )
		<< "scanned " << client.nLines << " lines out of " << client.nFiles
		<< " files -- under a hundred lines each, so the files were opened and "
		<< "not read";
	// 14,000: detailobjectsystem.cpp's 2,881 lines left the scan with the file
	// (C7 cluster 10; 14,518 scanned after it, in a tree set up from the fork).
	// A tree from stock upstream plus patches/ (make sdk-upstream, as CI's)
	// reads fewer lines, since the fork's edited files are longer: 13,913 at
	// 28b4e9d and 13,715 at 4cb81375. particlemgr.cpp and particles_new.cpp
	// left with C7 particles phase 4: 11,300 scanned after them on the fork
	// (main 8605b6ea1), about 10,800 on upstream (13,715 less their 2,918
	// upstream lines). entity_client_tools.cpp and toolframework_client.cpp
	// left too (SFM is dead, user ruling 2026-10-19): 10,378 scanned after them
	// on the fork; upstream's copies are 793 + 356 lines, so about 9,650 there.
	// clientleafsystem.cpp left with C7 cluster 12 L2: 7,991 scanned after it
	// on the fork; upstream's copy is about 1,800 lines, so about 7,850 there.
	// glow_overlay.cpp and c_pixel_visibility.cpp left with C7: 5,776 scanned
	// after them on the fork (clientshadowmgr.cpp's 4,484 and the three
	// libraries' 1,292); upstream's clientshadowmgr.cpp is 4,335 lines, so
	// about 5,627 there. 5,000 holds on both.
	// 5,000 -> 500 when clientshadowmgr.cpp left with C7 cluster 12 S2
	// (2026-10-19): 1,021 scanned after it, all of it our three libraries
	// (94 + 584 + 343 lines, so the same on a fork tree and an upstream one).
	// The per-file arm above is the one that cannot go stale; this floor keeps
	// about half the measurement as headroom for the libraries shrinking.
	EXPECT_GT( client.nLines, 200 )
		<< "scanned " << client.nLines << " client lines -- too few to mean "
		<< "anything";
	EXPECT_GT( client.nSkipped, client.nFiles )
		<< "fewer files skipped than scanned; the $File parse has swallowed "
		   "the tree and the ban is scanning what the compiler never sees";
}

TEST( SdkPlayerCast, TheEvictionListWasParsedAndIsLoadBearing )
{
	const ScanResult_t &server = ServerScan();

	// server_csharp.vpc lists the ~190 files it compiles (it was flattened;
	// it subtracts nothing). An empty parse would now mark EVERY file evicted
	// and scan nothing at all -- the failure that passes -- and a parse that
	// swallowed the whole tree would scan files the compiler never sees, which
	// is noisy but safe. Both are excluded by asserting a range, and the
	// direction of the dangerous failure is the opposite of what it was.
	// 80, not 100: plan_server_native_v2's evictions brought the list to 100
	// at T4e (shell_serversystems.cpp left), and a floor the tree sits on is
	// not a floor.
	EXPECT_GT( server.nEvictions, 80 )
		<< "server_sourcesharp's source list read as (nearly) nothing";
	EXPECT_LT( server.nEvictions, 400 )
		<< "far more entries than server_sourcesharp compiles; the reader is "
		   "picking up something that is not a file entry";
	EXPECT_GT( server.nSkipped, 20 )
		<< "nothing on disk was skipped; the ban is scanning files this mod "
		   "does not compile";

	// MORE IS SKIPPED THAN SCANNED, and that is now the correct shape rather
	// than a symptom. 140 of the ~890 files in game/server and game/shared are
	// in this build; the other 750 are stock SDK source the mod does not
	// compile. This assertion used to read `nFiles > nSkipped` and passed only
	// because the subtractive list under-reported what was outside the build.
	// What is worth asserting is that neither side collapsed.
	EXPECT_GT( server.nSkipped, server.nFiles )
		<< "fewer files skipped than scanned; the $File parse has swallowed "
		   "the tree and the ban is scanning what the compiler never sees";
	EXPECT_GT( server.nFiles, 0 )
		<< "everything was skipped; the ban is scanning nothing at all";

	// Two known-evicted files, named so the parse is checked against content
	// rather than against a count. Both were reported as violations on this
	// test's first run, before it learned to read the .vpc. Neither appears
	// anywhere in server_csharp.vpc now -- which is the point of asking the
	// question the other way round: they are outside the build because
	// nothing puts them in it, not because a line was written to take them
	// out.
	EXPECT_TRUE( IsEvicted( "server", "sceneentity.cpp" ) );
	EXPECT_TRUE( IsEvicted( "server", "player.cpp" ) );

	// And one that no -$File ever named, because it was never in this
	// project's list to be removed from it. The old subtractive answer called
	// this file live and scanned it; the positive list gets it right.
	EXPECT_TRUE( IsEvicted( "server", "npc_alyx.cpp" ) );

	// And one that must NOT be, or the ban has no teeth where it matters most.
	//
	// IT USED TO BE baseentity.cpp AND THE FLIP CHANGED THE TRUTH, so the row
	// is re-stated rather than loosened. server_csharp.vpc defines
	// SS_CBASEENTITY_ALIAS now, and the commit that armed it took
	// game/server/baseentity.cpp out of the `$File` list with the rest of the
	// removal set -- the file is still on disk, it is simply not compiled, so
	// `IsEvicted( "server", "baseentity.cpp" )` is TRUE and asking for FALSE
	// was asking the parse to lie.
	//
	// What this row is FOR is unchanged: it checks the parse against CONTENT
	// rather than against a count, by naming a file the list must report as
	// live -- an empty or mis-parsed `$File` list marks every file evicted and
	// scans nothing, and the counts above cannot tell that from a small build.
	// THE FILE THAT TAKES THIS JOB MOVED, and the job is unchanged. It was
	// game/server/mapentities.cpp; plan_shim.md Phase 2c relocated that to
	// sourcesharp/game/server/shell_mapparse.cpp and split it, so the name the
	// parse has to report as live is the new one. The qualification is the same
	// one the paragraph above states -- it is still compiled AND it still
	// carries the resolver sites TheBanCPredicateSeesTheClass names below
	// (CreateEntityByNameEither's GameEntityFromEntityHandle), so it is a file
	// where the ban has teeth rather than merely a file that is in the build.
	EXPECT_FALSE( IsEvicted( "server", "shell_mapparse.cpp" ) )
		<< "the server source list reports a compiled file as evicted; every "
		<< "ban above it is scanning nothing";

	// baseentity.cpp IS EVICTED NOW, and the assertion is inverted rather than
	// deleted -- gib.cpp's and playerinfomanager.cpp's precedent, below.
	//
	// PUTTING THE FILE BACK REDS THIS, which is what the inversion is for: it
	// is the single largest source of the class this file bans (101 of the
	// shim's cross-edges came from it before the flip), and it must not come
	// back into the build without somebody looking at it.
	EXPECT_TRUE( IsEvicted( "server", "baseentity.cpp" ) )
		<< "game/server/baseentity.cpp is in the server build again. The flip "
		<< "took it out of server_csharp.vpc when it armed SS_CBASEENTITY_ALIAS, "
		<< "so this is a resurrection rather than a drift -- move the assertion, "
		<< "do not delete it.";

	// gameinterface.cpp IS EVICTED NOW, and the assertion is inverted rather
	// than deleted -- playerinfomanager.cpp's precedent, thirty lines below.
	// plan_gamedll_after.md Phase A4 and A9 took the file apart and deleted it;
	// its edict-to-player sites moved into
	// sourcesharp/game/server/shell_gameclients.cpp already written on
	// UTIL_SDKPlayerFromEdict, and its two remaining CBaseEntity::Instance
	// calls are inside #ifndef SOURCESHARP, which this mod never compiles.
	//
	// PUTTING THE FILE BACK REDS THIS, which is what the inversion is for.
	EXPECT_TRUE( IsEvicted( "server", "gameinterface.cpp" ) )
		<< "gameinterface.cpp is in the server build again. It was deleted from "
		<< "the tree at plan_gamedll_after.md Phase A9 step 4, so this is a "
		<< "resurrection rather than a drift, and its sites must go back on the "
		<< "scanned inventory below -- move the assertion, do not delete it.";

	// The client's own list, parsed from its own .vpc, and the SAME question
	// asked the same way -- client_csharp.vpc was flattened too
	// (plan_vpc_client.md) and has no -$File left either. The count went from
	// 229 removal basenames to 288 compiled ones and the direction of the
	// dangerous failure inverted with it: an empty parse now marks every
	// client file evicted and scans nothing, instead of marking every client
	// file live and scanning the whole tree.
	const ScanResult_t &client = ClientScan();
	EXPECT_GT( client.nEvictions, 100 )
		<< "client_sourcesharp's source list read as (nearly) nothing";
	EXPECT_LT( client.nEvictions, 600 )
		<< "far more entries than client_sourcesharp compiles; the reader is "
		   "picking up something that is not a file entry";
	EXPECT_GT( client.nSkipped, 5 )
		<< "nothing on disk was skipped; the ban is scanning files this mod "
		   "does not compile";
	EXPECT_GT( client.nFiles, 0 )
		<< "everything was skipped; the client ban is scanning nothing at all";

	// Named files, so the client parse is checked against content and not
	// only against a range. hud_health.cpp is one of the 229 the removal list
	// used to name, and hl2/c_npc_manhack.cpp was never in the client's list
	// to be removed from it -- the case only the positive form gets right.
	//
	// THE "COMPILED" ROW USED TO BE cliententitylist.cpp AND THE FLIP CHANGED
	// THE TRUTH, exactly as it did for baseentity.cpp on the server thirty
	// lines above. client_csharp.vpc defines SS_CBASEENTITY_ALIAS now and the
	// same commit dropped game/client/cliententitylist.cpp from its `$File`
	// list; the client resolves the entity list through
	// sourcesharp/game/client/shell_cliententitylist.cpp instead, which is a
	// DIFFERENT BASENAME and so does not answer this question -- IsEvicted
	// matches on the basename and nothing in the build is called
	// "cliententitylist.cpp" any more.
	//
	// clientleafsystem.cpp took the job on the same grounds mapentities.cpp
	// took it on the server, and left client.so with C7 cluster 12 L2 (the leaf
	// system is managed's). clientshadowmgr.cpp took it then, and left client.so
	// with C7 cluster 12 S2 (2026-10-19), the client's last fork TU to go.
	// trace_filter_library.cpp takes it now, on the grounds shell_mapparse.cpp
	// took it on the server: compiled into client_sourcesharp, and a file the
	// client ban reads by name (kTraceFilterLibrary), so a file where the ban
	// has teeth rather than merely a file in the build.
	EXPECT_FALSE( IsEvicted( "client", "trace_filter_library.cpp" ) )
		<< "the client source list reports a compiled file as evicted; every "
		<< "ban above it is scanning nothing";
	// And clientshadowmgr.cpp inverted rather than deleted, as
	// cliententitylist.cpp is below.
	EXPECT_TRUE( IsEvicted( "client", "clientshadowmgr.cpp" ) )
		<< "game/client/clientshadowmgr.cpp is in the client build again. It left "
		<< "with C7 cluster 12 S2 and the managed shadow manager replaced it, so "
		<< "this is a resurrection rather than a drift -- move the assertion, do "
		<< "not delete it.";

	// And cliententitylist.cpp inverted rather than deleted, so that putting it
	// back reds this and its six cross-edges come back under the ban with
	// somebody looking at them.
	EXPECT_TRUE( IsEvicted( "client", "cliententitylist.cpp" ) )
		<< "game/client/cliententitylist.cpp is in the client build again. The "
		<< "flip took it out of client_csharp.vpc when it armed "
		<< "SS_CBASEENTITY_ALIAS and shell_cliententitylist.cpp replaced it, so "
		<< "this is a resurrection rather than a drift -- move the assertion, "
		<< "do not delete it.";

	EXPECT_TRUE( IsEvicted( "client", "hud_health.cpp" ) );
	EXPECT_TRUE( IsEvicted( "client", "c_npc_manhack.cpp" ) );
}

TEST( SdkPlayerCast, TheScannerReadEveryFileOfTheOriginalInventory )
{
	const ScanResult_t &server = ServerScan();

	// The six files docs/divergences.md found the fourteen sites in. If a file
	// is renamed or evicted this goes red and somebody decides deliberately,
	// rather than the ban quietly ceasing to cover it.
	//
	// server/util.cpp AND server/entitylist.cpp WERE THE FIRST TWO ROWS OF THIS
	// LIST AND THE FLIP EVICTED BOTH, and this is that deliberate decision.
	//
	// server_csharp.vpc defines SS_CBASEENTITY_ALIAS now and the commit that
	// armed it took both files out of the `$File` list. They are still on disk;
	// they are not compiled, and a banned shape in a file the compiler never
	// sees was never a hazard.
	//
	// THE SITES DID NOT ALL GO WITH THEM, WHICH IS WHY THIS IS NOT JUST TWO
	// INVERSIONS. util.cpp's edict-to-player half -- UTIL_PlayerByIndex and
	// UTIL_SDKPlayerFromEdict, plus the precache-other subsystem -- moved into
	// sourcesharp/game/server/server_util_library.cpp, which ServerScan already
	// reads BY NAME (kServerUtilLibrary), on the precedent set thirty lines
	// above ScanDirectory for the trace-filter family: when sites move into the
	// shim the file is scanned by name rather than the floor being lowered. So
	// the inventory follows them there rather than shrinking, and the four
	// resolver sites that came across are still under the ban --
	// TheBanCPredicateSeesTheClass names them below.
	//
	// entitylist.cpp's went to the C# entity list and have no scanned
	// successor, so it gets the inversion alone.
	EXPECT_TRUE( ScannedFileNamed( server, "server/server_util_library.cpp" ) )
		<< "game/server/util.cpp's edict-to-player half moved into "
		<< "sourcesharp/game/server/server_util_library.cpp at the flip and the "
		<< "scan has stopped reading it, so those sites are under no ban at all";

	EXPECT_TRUE( IsEvicted( "server", "util.cpp" ) )
		<< "game/server/util.cpp is in the server build again. The flip took it "
		<< "out of server_csharp.vpc when it armed SS_CBASEENTITY_ALIAS, so this "
		<< "is a resurrection rather than a drift, and its sites must go back on "
		<< "the scanned inventory above -- move the assertion, do not delete it.";
	EXPECT_TRUE( IsEvicted( "server", "entitylist.cpp" ) )
		<< "game/server/entitylist.cpp is in the server build again. The flip "
		<< "took it out of server_csharp.vpc when it armed SS_CBASEENTITY_ALIAS "
		<< "and the C# entity list replaced it, so this is a resurrection rather "
		<< "than a drift -- move the assertion, do not delete it.";

	// server/client.cpp WAS on this list and is not any more, and this is the
	// deliberate decision the comment above asks for rather than a quietly
	// relaxed assertion.
	//
	// plan_entity.md's Phase 4 exit took the file out of server_csharp.vpc: it
	// was batch 7 of the removal order, ahead of the cycle, with nothing owed.
	// (The two instruments that measured that -- `tools/p4-exit-batches` and
	// `tools/4a-owes` -- were themselves deleted with the rest of the
	// removal-set toolkit once the set was closed, so they are described here
	// rather than cited. The citation was prose either way; the fact below
	// reads the .vpc.) Its thirty-eight §3.16 cast sites went
	// with it and are the reason it was worth removing -- twenty of them
	// `ToBasePlayer( UTIL_GetCommandClient() )`, one per development console
	// command, every one of which had been answering NULL because
	// UTIL_SDKPlayerFromEdict cannot see a standalone player. Those commands are
	// the console's now (Verdict A's forty-three, DevCommandsLive.cs and
	// DevCommandsRetired.cs), which is where the subject is resolved through the
	// slot table instead.
	//
	// What the file still registered in THIS build came across rather than
	// going with it: sv_allow_point_servercommand, drawline and drawcross to
	// sourcesharp/game/server/shell_client_orphans.cpp, groundlist and bug_swap
	// to DevCommandsRetired.cs. client_tu_evicted_test.cpp is the enumeration
	// and computes, over the whole file, that no registration is left outside a
	// `#ifndef SOURCESHARP` arm.
	//
	// Asserted the other way round so that PUTTING THE FILE BACK reds this test
	// and its sites come back under the ban with somebody looking at them.
	EXPECT_TRUE( IsEvicted( "server", "client.cpp" ) )
		<< "game/server/client.cpp is in the server build again. Its "
		<< "thirty-eight cast sites are compiled once more and must be back on "
		<< "the scanned inventory above -- move the assertion, do not delete it.";

	// server/gameinterface.cpp WAS on this list and is not any more, and this
	// is the deliberate decision the comment above asks for.
	//
	// It was deleted from the tree at plan_gamedll_after.md Phase A9 step 4.
	// Its edict-to-player sites did not go with it -- they are in
	// sourcesharp/game/server/shell_gameclients.cpp, already rewritten onto
	// UTIL_SDKPlayerFromEdict, with two CBaseEntity::Instance calls left inside
	// #ifndef SOURCESHARP that this mod never compiles.
	//
	// AND THE BAN NO LONGER COVERS THAT FILE, WHICH IS A REAL GAP AND IS NOT
	// A9's TO CLOSE. This scanner walks src/game/server and src/game/shared
	// only. Widening it to src/sourcesharp/game/server was MEASURED while this
	// row was being written and finds exactly ONE violation --
	// shell_standalone_base.cpp:2772, a raw `pEnt->GetBaseEntity()` NULL-tested
	// four lines later, which is the class CASUALTY 2 was. That is a real site
	// in a file this phase does not own, and closing it is a decision with an
	// owner rather than a side effect of deleting gameinterface.cpp. Recorded
	// here so the next pass does not have to re-derive it.
	EXPECT_TRUE( IsEvicted( "server", "gameinterface.cpp" ) )
		<< "gameinterface.cpp is in the server build again -- put its row back "
		<< "on this inventory rather than leaving the ban blind to it.";

	// playerinfomanager.cpp WAS on this list and is not any more, and this is
	// the deliberate decision the comment above asks for rather than a quietly
	// relaxed assertion.
	//
	// It held FOUR of the fourteen sites -- GetPlayerInfo (V1) at :63,
	// GetPlayerInfo at :79, GetBotController at :99 and CreateBot at :120, the
	// last with no NULL check either (docs/divergences.md:10574). All four had
	// already been rewritten onto UTIL_SDKPlayerFromEdict; the file's own
	// header comment names the ban's shape and line 43 warns that this mod has
	// no CBasePlayer at all.
	//
	// plan_server_all.md Wave 3 then took the whole file out of the build: it
	// publishes PlayerInfoManager001/002 and BotManager001 through
	// CreateInterface, and the only binary in the installed game naming any of
	// those strings is serverplugin_empty.dll -- a WINDOWS sample plugin.
	// docs/server_evictions.md 77 carries the measurement and the live check.
	//
	// So the ban has not stopped covering four live sites; the four sites
	// stopped being compiled. Asserted the other way round so that PUTTING THE
	// FILE BACK reds this test and its four sites come back under the ban with
	// somebody looking at them.
	EXPECT_TRUE( IsEvicted( "server", "playerinfomanager.cpp" ) )
		<< "playerinfomanager.cpp is in the server build again. Its four "
		<< "edict-to-player sites are compiled once more and must be back on "
		<< "the scanned inventory above -- move the assertion, do not delete "
		<< "it.";

	// server/gib.cpp WAS on this list and is not any more, and this is the
	// deliberate decision the comment above asks for rather than a quietly
	// relaxed assertion.
	//
	// It held ONE of the fourteen sites: CGib::SpawnHeadGib's
	// `UTIL_SDKPlayerFromEdict( pentPlayer )`, already rewritten off the banned
	// shape and asking the resolver. plan_entity.md step 4a then PORTED the gib
	// -- ss_gib and Gibs in SourceSharp.Server -- and deleted gib.cpp from the
	// tree, so the site did not move anywhere: SpawnHeadGib had no caller in
	// this build at all (its callers are the NPC death code, out of
	// server_csharp.vpc), and it went with the file.
	//
	// So the ban has not stopped covering a live site; the site stopped being
	// compiled. Asserted the other way round so that PUTTING THE FILE BACK reds
	// this test and its site comes back under the ban with somebody looking at
	// it -- the same treatment gameinterface.cpp and playerinfomanager.cpp have
	// above.
	EXPECT_TRUE( IsEvicted( "server", "gib.cpp" ) )
		<< "gib.cpp is in the server build again. It was deleted from the tree "
		<< "at plan_entity.md step 4a, so this is a resurrection rather than a "
		<< "drift, and its edict-to-player site must go back on the scanned "
		<< "inventory above -- move the assertion, do not delete it.";

	// And the files the dry run's census found the widest class in.
	//
	// game/shared/util_shared.cpp WAS HERE AND IS DELETED by plan_shim.md Phase
	// 2c, lane 2-utilshared -- not evicted from the vpc, deleted from the tree,
	// after a census read three server importers and one client one for the
	// whole translation unit and none of them was a trace. Asserted the other
	// way round, exactly as gib.cpp above: putting the file back reds this test
	// and its resolver site comes back under the ban with somebody looking at
	// it.
	EXPECT_FALSE( ScannedFileNamed( server, "shared/util_shared.cpp" ) )
		<< "game/shared/util_shared.cpp is in the server build again. It was "
		<< "DELETED at plan_shim.md Phase 2c, so this is a resurrection rather "
		<< "than a drift, and its GameEntityFromEntityHandle site must go back "
		<< "on the scanned inventory above -- move the assertion, do not delete "
		<< "it.";

	// server/physics_main.cpp STOOD BESIDE IT and the flip evicted it, on the
	// same commit and for the same reason as util.cpp and entitylist.cpp above:
	// the physics tick is the C# shell's now and the whole
	// physics_main.cpp / physics_main_shared.cpp / hierarchy.cpp group left
	// server_csharp.vpc when SS_CBASEENTITY_ALIAS was armed. Its two
	// cross-edges (Physics_RunThinkFunctions and PerformCustomPhysics) went
	// with it -- the entity-census records exactly that -- so no site moved
	// anywhere for the inventory to follow, and the row is inverted rather than
	// deleted.
	EXPECT_TRUE( IsEvicted( "server", "physics_main.cpp" ) )
		<< "game/server/physics_main.cpp is in the server build again. The flip "
		<< "took it out of server_csharp.vpc when it armed SS_CBASEENTITY_ALIAS, "
		<< "so this is a resurrection rather than a drift, and its sites must go "
		<< "back on the scanned inventory above -- move the assertion, do not "
		<< "delete it.";

	// And the shared trace-filter family, which is where the widest class
	// actually lives now: eleven of the sixteen hits
	// TheBanCPredicateSeesTheClass counts on the server are in this one file.
	// It is scanned by name (kTraceFilterLibrary) and is the reason that fact
	// still has a population to count after the flip, so if the scan stops
	// reading it there is no ban left worth the name.
	EXPECT_TRUE( ScannedFileNamed( server, "shared/trace_filter_library.cpp" ) )
		<< "the shim's trace-filter family is unscanned; the widest resolver "
		<< "class in the build is under no ban at all";

	const ScanResult_t &client = ClientScan();
	// clientleafsystem.cpp left client.so with C7 cluster 12 L2, so it left
	// the inventory too. The shadow manager followed it with C7 cluster 12 S2
	// (2026-10-19: clientshadowmgr.cpp, the client's last fork TU, left
	// client.so), so its row is inverted the same way. Both absences rest on
	// the trace-filter row below, which proves the client scan read a file.
	EXPECT_FALSE( ScannedFileNamed( client, "client/clientleafsystem.cpp" ) )
		<< "clientleafsystem.cpp is in the client scan again: the stock leaf "
		<< "system is back in client.so.";
	EXPECT_FALSE( ScannedFileNamed( client, "client/clientshadowmgr.cpp" ) )
		<< "clientshadowmgr.cpp is in the client scan again: the stock shadow "
		<< "manager is back in client.so (it is managed since C7 cluster 12 S2).";

	// client/cliententitylist.cpp STOOD HERE and the flip evicted it, the
	// client's half of the same commit. The list itself is
	// sourcesharp/game/client/shell_cliententitylist.cpp now; that file is in
	// the build but this scan walks game/client, game/client/replay and
	// game/shared only, so -- exactly as the gameinterface.cpp row records
	// above -- widening the walk into sourcesharp/game/client is a decision
	// with an owner rather than a side effect of this repair. Recorded here so
	// the next pass does not have to re-derive it.
	EXPECT_TRUE( IsEvicted( "client", "cliententitylist.cpp" ) )
		<< "game/client/cliententitylist.cpp is in the client build again. The "
		<< "flip took it out of client_csharp.vpc when it armed "
		<< "SS_CBASEENTITY_ALIAS, so this is a resurrection rather than a drift, "
		<< "and its sites must go back on the scanned inventory above -- move "
		<< "the assertion, do not delete it.";

	// And the client's half of the trace-filter family, for the reason the
	// server's row gives: ten of the client's seventeen hits are in it.
	EXPECT_TRUE( ScannedFileNamed( client, "shared/trace_filter_library.cpp" ) )
		<< "the shim's trace-filter family is unscanned on the client too";
	// `ScannedFileNamed( client, "client/replay/replaycamera.cpp" )` STOOD HERE
	// and said "the replay subdirectory is compiled into client.so and has a
	// site in it". plan_entity.md step 7g took the whole of replay/ out of the
	// client build -- this mod does not ship replay -- so the file is evicted
	// and the site went with it. The assertion is INVERTED rather than deleted,
	// on the treatment gib.cpp gets above: if the subdirectory comes back, its
	// sites have to come back onto the scanned inventory beside it.
	EXPECT_TRUE( IsEvicted( "client", "replaycamera.cpp" ) )
		<< "client/replay/replaycamera.cpp is in the client build again. It was "
		<< "deleted from the tree at plan_entity.md step 7g with the rest of "
		<< "replay/, so this is a resurrection rather than a drift, and its cast "
		<< "sites must go back on the scanned inventory above -- move the "
		<< "assertion, do not delete it.";
}

TEST( SdkPlayerCast, TheScannerStillRecognisesTheOneLineShape )
{
	// The mutation check, written down rather than performed by hand once. If
	// the predicate is ever loosened into uselessness, this is what says so.
	EXPECT_TRUE( LineHasBannedCast(
		"\tCBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );" ) );
	EXPECT_TRUE( LineHasBannedCast(
		"\tCBasePlayer *p = (CBasePlayer*)GetContainingEntity( pEdict );" ) );
	EXPECT_TRUE( LineHasBannedCast(
		"\tCBasePlayer *p = static_cast<CBasePlayer*>( GetContainingEntity( pEdict ) );" ) );

	// And the things that must NOT trip it, or the ban is unusable.
	EXPECT_FALSE( LineHasBannedCast(
		"\t// CBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );" ) );
	EXPECT_FALSE( LineHasBannedCast(
		"//     ( CBasePlayer * )CBaseEntity::Instance( pEdict )" ) );
	EXPECT_FALSE( LineHasBannedCast(
		"\tCBaseEntity *p = CBaseEntity::Instance( pEdict );" ) );
	EXPECT_FALSE( LineHasBannedCast(
		"\tCBasePlayer *p = ToBasePlayer( pEntity );" ) );
	EXPECT_FALSE( LineHasBannedCast(
		"\tCBasePlayer *p = UTIL_SDKPlayerFromEdict( pEdict );" ) );
}

TEST( SdkPlayerCast, TheBindingPredicateReadsEverySpellingOfAResolver )
{
	// Every form the enumerated 86 sites are written in, and the classification
	// each has to get. This is the half of Ban C that a reader has to be able
	// to check by eye.
	struct Case_t { const char *pszLine; ResolverKind_t kind; const char *pszVar; };
	static const Case_t kCases[] =
	{
		{ "CBaseEntity *pEnt = CBaseEntity::Instance(player);", RESOLVER_PARTITIONED, "pEnt" },
		{ "\tCBaseEntity *entity = GetContainingEntity( e1 );", RESOLVER_PARTITIONED, "entity" },
		{ "\tCBaseEntity *pEntity = EntityFromEntityHandle( pHandleEntity );", RESOLVER_PARTITIONED, "pEntity" },
		{ "\tCBaseEntity *pCheck = gEntList.GetBaseEntity( h );", RESOLVER_PARTITIONED, "pCheck" },
		{ "\tpHit = cl_entitylist->GetBaseEntity( iEntity );", RESOLVER_PARTITIONED, "pHit" },
		{ "\tCBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );", RESOLVER_PARTITIONED, "p" },
		{ "\tCBasePlayer *p = static_cast<CBasePlayer*>( GetContainingEntity( e ) );", RESOLVER_PARTITIONED, "p" },

		// The raw form: a member call with EMPTY parentheses.
		{ "\tCBaseEntity *pRet = pUnk->GetBaseEntity();", RESOLVER_RAW, "pRet" },
		{ "\tC_BaseEntity *ent = m_hAttachedToEntity->GetBaseEntity();", RESOLVER_RAW, "ent" },
		{ "\tC_BaseEntity *pEnt = pRend->GetIClientUnknown()->GetBaseEntity();", RESOLVER_RAW, "pEnt" },

		// The ternary form, verbatim from viewrender.cpp:4169 -- the site the
		// LIVE client canary died at, which neither the dry run's census nor
		// the first version of this predicate matched.
		{ "\tC_BaseEntity *pEntity = itEntity->m_pRenderable ? itEntity->m_pRenderable->GetIClientUnknown()->GetBaseEntity() : NULL;", RESOLVER_RAW, "pEntity" },
		{ "\tC_BaseEntity *pEntity = pClientEntity ? pClientEntity->GetBaseEntity() : NULL;", RESOLVER_RAW, "pEntity" },

		// And things that are not bindings from a resolver at all.
		{ "\tCBaseEntity *pEnt = ToBasePlayer( pOther );", RESOLVER_NONE, "" },
		{ "\tif ( pEnt->GetBaseEntity() )", RESOLVER_NONE, "" },
		{ "\tint n = pEnt->entindex();", RESOLVER_NONE, "" },
		{ "\tif ( pEnt == CBaseEntity::Instance( e ) )", RESOLVER_NONE, "" },
	};

	for ( size_t i = 0; i < sizeof( kCases ) / sizeof( kCases[ 0 ] ); i++ )
	{
		const Binding_t b = BindingOn( Squeezed( kCases[ i ].pszLine ), false );
		EXPECT_EQ( kCases[ i ].kind, b.kind ) << "on: " << kCases[ i ].pszLine;
		if ( kCases[ i ].kind != RESOLVER_NONE )
		{
			EXPECT_EQ( std::string( kCases[ i ].pszVar ), b.strVar ) << "on: " << kCases[ i ].pszLine;
		}
	}
}

TEST( SdkPlayerCast, TheNullTestPredicateReadsEverySpellingOfACheck )
{
	// Taken from nullflip_verbose.txt: these are the actual flag lines of the
	// enumerated sites, not invented ones.
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif ( pEnt )" ), "pEnt" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif ( !pEnt )" ), "pEnt" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif ( pEnt && pEnt->IsPlayer() )" ), "pEnt" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif ( entity && entityTouched )" ), "entity" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\twhile ( ent )" ), "ent" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif ( !pEntity || FClassnameIs( pEntity, m_p ) )" ), "pEntity" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif ( !pEntity->BlocksLOS() )" ), "pEntity" ) );
	EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed( "\tif (m_AlphaVar && pBaseEnt)" ), "pBaseEnt" ) );

	// And what must not count, or every use of every pointer is a hit.
	EXPECT_FALSE( MentionsVarInABooleanPosition( Squeezed( "\tpEnt->Think();" ), "pEnt" ) );
	EXPECT_FALSE( MentionsVarInABooleanPosition( Squeezed( "\tif ( pOther )" ), "pEnt" ) );
	EXPECT_FALSE( MentionsVarInABooleanPosition( Squeezed( "\tif ( pEntity2 )" ), "pEntity" ) )
		<< "matched a longer identifier that merely starts the same";
}

TEST( SdkPlayerCast, TheGuardStackDropsWhatThisBuildDoesNotCompile )
{
	CGuardStack guards;
	EXPECT_TRUE( guards.Live() );

	guards.Directive( "#ifndef SOURCESHARP" );
	EXPECT_FALSE( guards.Live() ) << "#ifndef SOURCESHARP is not compiled here";

	guards.Directive( "#endif" );
	EXPECT_TRUE( guards.Live() );

	// The sanctioned cast in UTIL_SDKPlayerFromEdict lives in exactly this arm.
	guards.Directive( "#ifdef SOURCESHARP" );
	EXPECT_TRUE( guards.Live() );
	guards.Directive( "#else" );
	EXPECT_FALSE( guards.Live() ) << "the #else of #ifdef SOURCESHARP is stock's arm";
	guards.Directive( "#endif" );
	EXPECT_TRUE( guards.Live() );

	// A guard this scanner does not evaluate stays live, so its contents are
	// still reported. Wrong in the direction of a false positive, on purpose.
	guards.Directive( "#ifdef TF_DLL" );
	EXPECT_TRUE( guards.Live() );
	guards.Directive( "#endif" );
	EXPECT_TRUE( guards.Live() );
}

//-----------------------------------------------------------------------------
// THE ONE THAT KEEPS BAN C HONEST.
//
// Ban C reports zero. So would a predicate that matched nothing whatsoever, and
// this project has now found twenty-two checks that could not fail -- six of
// them the very gates that were supposed to guard this step. So the SAME
// predicate is run with its exemption lifted, treating every resolver as raw,
// and required to find the class the dry run enumerated by hand.
//
// If Ban C's zero is ever produced by a broken predicate rather than by a fixed
// tree, this is what says so.
//-----------------------------------------------------------------------------

TEST( SdkPlayerCast, TheBanCPredicateSeesTheClass )
{
	const ScanResult_t &server = ServerScan();
	const ScanResult_t &client = ClientScan();

	// docs/typedef-dryrun/nullflip_verbose.txt: 48 server sites in 20 files and
	// 38 client. This scan is not identical to that one -- it evaluates the
	// SOURCESHARP guards, and it reaches only the top level of each directory
	// plus client/replay, where the census followed the compiler's include
	// graph into public/ as well -- so the floor is set below those numbers
	// rather than at them. What it excludes is the failure that matters: a
	// predicate that has stopped matching.
	// The server floor came down from 40 to 30 with the flattening: the scan
	// covers the 140 files this mod compiles rather than every file in
	// game/server, so three of the hits it used to count were in stock source
	// the compiler never sees. 37 remain. The floor is set below that for the
	// same reason it was set below 48 -- it is here to catch a predicate that
	// has stopped matching, not to pin a census.
	//
	// And down again, 30 to 25, for the same reason a second time:
	// plan_shared.md Phase 2 took game/shared/saverestore.cpp out of both
	// projects, and its restore walk carried resolver sites of its own
	// (saverestore.cpp:985, :2640, :3417 and more). The scan reads the .vpc, so
	// they left the count with the file: 30 measured before the eviction, 28
	// after. THIS FLOOR IS NOT A CENSUS AND MUST NOT BE SET AT THE
	// MEASUREMENT -- a floor equal to the count fails on the next legitimate
	// eviction, and somebody raises it back rather than reading it, which is
	// how a floor stops being a check.
	// And down a third time, 25 to 20, for the THIRD instance of the same
	// legitimate cause: plan_entity.md's Phase 4 exit took
	// game/server/client.cpp out of server_csharp.vpc, and its resolver site
	// left the count with the file -- 25 measured before the eviction, 24
	// after. Set at 20 and not at 24, because the paragraph above says why: a
	// floor equal to the measurement fails on the next legitimate eviction and
	// somebody raises it back rather than reading it.
	// AND THE FLIP TOOK IT BELOW 20, WHICH IS WHERE THE POPULATION COUNT STOPS
	// BEING THE CHECK. The comment on the client arm below -- written three
	// lowerings ago -- says what to do when that happens: "the answer is to
	// re-key this self-test on something that is not a population count, not to
	// lower the number a fifth time." This is that re-key.
	//
	// WHAT THE FLIP DID. server_csharp.vpc defines SS_CBASEENTITY_ALIAS now and
	// the commit that armed it took 20 stock `$File` rows off the server,
	// among them every large resolver-bearing file left on the server:
	// util.cpp, entitylist.cpp, physics_main.cpp, baseentity.cpp,
	// baseentity_shared.cpp, cbase.cpp. The scan reads the .vpc, so their hits
	// left the count with the files exactly as saverestore.cpp's and
	// client.cpp's did. 24 before the flip, 16 after; the client 30-ish before,
	// 17 after. Nothing stopped matching.
	//
	// THE RE-KEY, and why it is better than a fifth number. The population
	// count was only ever a proxy for "the predicate still recognises the class
	// IN THE TREE" -- the quoted-line fixtures above already prove it
	// recognises the class in the abstract. A proxy that falls every time a
	// file legitimately leaves the build is the wrong proxy, and this one has
	// now been lowered four times for four different legitimate evictions.
	//
	// So the fact NAMES THE FILES instead. Each of these is in the armed build,
	// each carries at least one site of the class with the exemption lifted,
	// and none of them can be moved by the next eviction of some other file:
	//
	//   sourcesharp/game/shared/trace_filter_library.cpp   11 of the 16
	//   sourcesharp/game/server/server_util_library.cpp     4  (util.cpp's, re-homed)
	//   game/shared/util_shared.cpp                         1
	//   game/server/mapentities.cpp                         1
	//
	// A predicate that has stopped matching finds none of them and this goes
	// red four times over. A file that legitimately leaves the build takes its
	// own row red and nobody else's, which is the point: the failure then names
	// the file whose eviction caused it, instead of a count that says only that
	// some number fell.
	// trace_filter_library.cpp's SERVER ROW IS RETIRED BY plan_server_native_v2
	// T4f, and it was the last file that held the class on the server: the
	// server's trace filter is managed's (TraceFilterRules), server.so no longer
	// compiles the file, and the resolver sites were its server arms. So the
	// row goes red for the reason the paragraph above wants -- the file whose
	// eviction caused it -- and is retired with its subject, not re-pointed:
	// there is no armed spelling of the class left on the server.
	EXPECT_FALSE( HitsInFileEnding( server.EveryResolverHits, "shared/trace_filter_library.cpp" ) )
		<< "Ban C's predicate finds the class in the trace-filter family again; "
		<< "its server arms are managed's (plan_server_native_v2 T4f)."
		<< Describe( server.EveryResolverHits );
	// game/shared/util_shared.cpp's ROW IS RETIRED BY plan_shim.md PHASE 2c,
	// lane 2-utilshared, AND THE NOTE ABOVE SAID EXACTLY WHAT TO DO WITH IT:
	// "A file that legitimately leaves the build takes its own row red and
	// nobody else's, which is the point: the failure then names the file whose
	// eviction caused it."
	//
	// This is that case and it is not an eviction of a file that still has
	// code. The site was `CTraceFilterEntity`'s ctor and `ShouldHitEntity`
	// asking `GameEntityFromEntityHandle`, inside two file-local filter classes
	// declared in no header, reachable only from three `UTIL_TraceEntity`
	// overloads with zero importers on either link line:
	//
	//     tools/shim-census --mtime-policy off who-imports \
	//         src/game/shared/util_shared.cpp
	//       -> server 3 importers: hl2_episodic, UTIL_PlayerByCommandArg,
	//          UTIL_BloodDrips.  client 1: hl2_episodic.  None is a filter.
	//
	// So the row is removed rather than re-pointed: there is no successor site
	// in that file to point at. The three named rows that remain
	// (trace_filter_library.cpp, and the two absences below) still carry the
	// fact, and the floor below falls with it -- see its own note.

	// TWO OF THE FOUR ROWS WERE BOUND FROM A RESOLVER THAT NO LONGER EXISTS,
	// and they are re-keyed as ABSENCES with the READ PROVED, which is the
	// shape this fact already uses for toolframework_client.cpp below.
	//
	// server_util_library.cpp's four and mapentities.cpp's one all bound from
	// GetContainingEntity. The close measured that function: it resolves
	// through IServerUnknown::GetBaseEntity(), whose three implementations in
	// server.so are every one of them `mov $0x0,%eax; ret`, so it answered a
	// constant NULL, and it is deleted with its ten call sites. Each site now
	// carries the NULL written down and a note saying why. A binding cannot be
	// found where there is no longer a call, and that is not the predicate
	// going quiet -- so the files are asserted READ and asserted CLEAN, and the
	// live class is carried by the two rows that remain.
	EXPECT_TRUE( ScannedFileNamed( server, "server/server_util_library.cpp" ) )
		<< "server_util_library.cpp is not in the server scan at all any more, "
		<< "so the absence below would pass over a file nobody read.";
	EXPECT_FALSE( HitsInFileEnding( server.EveryResolverHits, "server/server_util_library.cpp" ) )
		<< "server_util_library.cpp binds an entity from a resolver again. Its "
		<< "four sites were GetContainingEntity's, which answered NULL for every "
		<< "edict; a site here now is a live one and wants reading."
		<< Describe( server.EveryResolverHits );

	// RELOCATED by plan_shim.md Phase 2c: game/server/mapentities.cpp is
	// sourcesharp/game/server/shell_mapparse.cpp. The site this pair is about
	// survived the split -- it is in CreateEntityByNameEither, which is one of
	// the eight exports that kept an importer.
	EXPECT_TRUE( ScannedFileNamed( server, "server/shell_mapparse.cpp" ) )
		<< "shell_mapparse.cpp is not in the server scan at all any more, so the "
		<< "absence below would pass over a file nobody read.";
	EXPECT_FALSE( HitsInFileEnding( server.EveryResolverHits, "server/shell_mapparse.cpp" ) )
		<< "the map parse binds an entity from a resolver and then tests it "
		<< "for NULL again. Its one site was GetContainingEntity's; what it "
		<< "does now is bind from GameEntityFromEntityHandle in an `if` "
		<< "condition and RETURN the pointer, which is the shape that needs no "
		<< "second test." << Describe( server.EveryResolverHits );

	// The population floor is KEPT and demoted rather than deleted. It is no
	// longer the thing that keeps Ban C honest -- the rows above are -- but it
	// still catches the case none of them can: a predicate that matches those
	// files and nothing else, which is a predicate that has been narrowed to
	// fit its own self-test.
	//
	// THE MEASUREMENT IS 2 NOW, NOT 16, AND THE FALL IS THE WORK. Four resolver
	// families were deleted as constant NULLs -- GetContainingEntity,
	// CBaseEntity::Instance, EntityFromEntityHandle and TraceEntity -- and the
	// sites that bound from them do not bind from anything now, because the
	// answer is written down at each one. What is left binds from the two live
	// resolvers and is exactly the two rows above.
	//
	// So the floor is set AT the measurement, deliberately, against this file's
	// own rule that a floor equal to the measurement fails on the next
	// legitimate eviction. At 2 there is nowhere left to demote it to: a lower
	// floor is satisfied by one row, and both rows are asserted by name anyway.
	// If the next eviction takes one, the row named above goes red first and
	// says which file -- which is the outcome that rule wanted.
	// 2 -> 1 BY plan_shim.md PHASE 2c, lane 2-utilshared, AND IT IS THE THIRD
	// KIND OF NUMBER rather than a ratchet or a stale floor: it was a real
	// MEASUREMENT of how many files hold the class, and one of the two files
	// stopped holding it because its code was deleted for having no consumers.
	// A measurement that should break, broke.
	//
	// The paragraph above says "at 2 there is nowhere left to demote it to". At
	// 1 that is even more true, so the floor stops carrying the weight and the
	// NAMED row above it does: trace_filter_library.cpp is asserted by name and
	// holds every site the server has. The floor is kept only as the vacuity
	// guard its own text describes -- a predicate narrowed to its own self-test
	// finds zero, and zero is what this refuses.
	//
	// 1 -> 0 BY plan_server_native_v2 T4f: the trace-filter family's server arms,
	// the class's last armed spelling on the server, went to managed. The
	// vacuity guard is now the quoted-line fixtures above alone, which prove
	// the predicate recognises the class without the tree holding any.
	EXPECT_EQ( server.EveryResolverHits.size(), 0u )
		<< "the Ban C predicate finds the class on the server again."
		<< Describe( server.EveryResolverHits );

	// AND THE CLIENT FLOOR WAS SET AT THE MEASUREMENT, which is exactly what
	// the paragraph above forbids. It read 30 with 30 hits in the tree, so the
	// first legitimate removal broke it: lane c7-proxies took the
	// ProxyHealth/recvproxy resolver out of the material proxies and the count
	// fell 30 -> 29. Nothing was wrong with that work; the floor was wrong,
	// and it was wrong in precisely the way three server-side comments above
	// say a floor goes wrong.
	//
	// Set at 20, not at 29, for the reason those comments give: a floor equal
	// to the measurement fails on the next legitimate eviction, and whoever
	// meets it raises it back rather than reading it, which is how a floor
	// stops being a check. The client is still driving this population to
	// zero, so it will cross 20 too -- and when it does, the answer is to
	// re-key this self-test on something that is not a population count, not
	// to lower the number a fifth time.
	// AND THE FLIP TOOK THE CLIENT BELOW 20 TOO -- 17 -- which is the crossing
	// the paragraph above predicted, arriving by eviction rather than by the
	// population reaching zero: client_csharp.vpc armed SS_CBASEENTITY_ALIAS
	// and dropped cliententitylist.cpp, c_baseentity.cpp, c_baseanimating.cpp,
	// fx_impact.cpp, physics_main_client.cpp and the rest of the removal set.
	// The instruction it left is followed rather than the number lowered a
	// fifth time: the client is re-keyed on named files on the server's model
	// directly above.
	//
	//   sourcesharp/game/shared/trace_filter_library.cpp   10 of the 17
	//   game/client/clientleafsystem.cpp                    1
	//   game/client/toolframework_client.cpp                1
	//   game/shared/util_shared.cpp                         1
	//
	// AND THE `residue` WAVE (74b071771..a46e8d645) THEN EVICTED ONE OF THE
	// FOUR NAMED ROWS, which is the failure residue brief rule 3 is about: an
	// anchor a change can take away is not an anchor. The measured client rows
	// now read
	//
	//   sourcesharp/game/shared/trace_filter_library.cpp   10 of the 15
	//   game/client/cdll_util.cpp                           1
	//   game/client/clientleafsystem.cpp                    1
	//   game/client/flashlighteffect.cpp                    1
	//   game/shared/util_shared.cpp                         1
	//
	// toolframework_client.cpp's single row was
	// `C_BaseEntity *pEnt = SDKEntityFromUnknown( view->GetCurrentlyDrawingEntity() );`
	// at toolframework_client.cpp:357 at 74b071771, inside
	// `#ifndef SS_CBASEENTITY_ALIAS` (toolframework_client.cpp:351-453, the one
	// guarded region that file's own header banner counts). The alias is defined
	// in every configuration this tree builds, so THAT SITE WAS NEVER COMPILED:
	// the scan found it because the scan reads source, not objects. The wave
	// deleted the dead arm and the row went with it.
	//
	// The row is RE-POINTED rather than dropped, and it is re-pointed at the
	// surviving ARMED spelling of the same thing: cdll_util.cpp:615's
	// `C_BaseEntity *pEntity = SDKEntityFromUnknown( pClientEntity );` in
	// CFlaggedEntitiesEnum::EnumElement, which is under no preprocessor guard at
	// all and is the client twin of util.cpp's enumerator. Checked for
	// preprocessor state and not just for text, per residue brief rule 5.
	//
	// And the eviction is asserted as an ABSENCE in its own right below, with
	// the file proved READ, so "toolframework_client.cpp stopped being scanned"
	// cannot be mistaken for "toolframework_client.cpp is clean".
	// The client's trace_filter_library.cpp row went with the server's arms
	// (plan_server_native_v2 T4f): the scan reads source, so the client found
	// the class in the file's #ifndef CLIENT_DLL blocks; the file is the
	// client's arm only now and holds none.
	EXPECT_FALSE( HitsInFileEnding( client.EveryResolverHits, "shared/trace_filter_library.cpp" ) )
		<< "Ban C's predicate finds the class in the trace-filter family again "
		<< "on the client." << Describe( client.EveryResolverHits );
	// clientleafsystem.cpp's row (its AddRenderableToLeaf dump, under a
	// DUMP_RENDERABLE_LEAFS nothing defines) left with the file at C7 cluster
	// 12 L2. cdll_util.cpp's CFlaggedEntitiesEnum::EnumElement held the third
	// row until C8 (lane c6-input), when the file left client.so with no
	// importer; the class has no armed spelling left on the client outside the
	// trace-filter family.
	EXPECT_FALSE( HitsInFileEnding( client.EveryResolverHits, "client/clientleafsystem.cpp" ) )
		<< "Ban C's predicate finds clientleafsystem.cpp again."
		<< Describe( client.EveryResolverHits );

	// THE EVICTED ROW'S FILE HAS LEFT THE BUILD. toolframework_client.cpp held
	// it until the residue wave; then the file left client.so (SFM is dead,
	// user ruling 2026-10-19), so the scan no longer reads it at all. The read
	// of a sibling that stays is proved first, so the absence is the file
	// leaving and not the scanner going quiet. The sibling was
	// clientshadowmgr.cpp until it, the client's last fork TU, left client.so
	// with C7 cluster 12 S2 (2026-10-19); it is trace_filter_library.cpp now,
	// which ClientScan reads by path and the client still compiles. A scanner
	// that read nothing fails this row, so the absences after it are evidence.
	EXPECT_TRUE( ScannedFileNamed( client, "shared/trace_filter_library.cpp" ) )
		<< "trace_filter_library.cpp is not in the client scan, so the absences "
		<< "below prove nothing about the scanner.";
	EXPECT_FALSE( ScannedFileNamed( client, "client/clientshadowmgr.cpp" ) )
		<< "clientshadowmgr.cpp is in the client scan again: the stock shadow "
		<< "manager is back in client.so (it is managed since C7 cluster 12 S2).";
	EXPECT_FALSE( ScannedFileNamed( client, "client/toolframework_client.cpp" ) )
		<< "toolframework_client.cpp is in the client scan again: the tool "
		<< "framework is back in client.so.";
	EXPECT_FALSE( ScannedFileNamed( client, "client/flashlighteffect.cpp" ) )
		<< "flashlighteffect.cpp is in the client scan again: the flashlight "
		<< "is native in client.so again (it is managed since C7 cluster 12).";
	// game/shared/util_shared.cpp's CLIENT ROW IS RETIRED FOR THE SAME REASON AS
	// ITS SERVER TWIN, and it is the same single site: the file is compiled into
	// both sides, so `CTraceFilterEntity`'s `GameEntityFromEntityHandle` ask was
	// one row here and one row there. plan_shim.md Phase 2c, lane 2-utilshared,
	// deleted the two file-local filter classes on a zero-importer census taken
	// over both link lines -- so there is no successor site on this side either
	// and the row is removed rather than re-pointed.

	// Demoted and kept, exactly as on the server, and down for the same reason:
	// the four deleted resolver families took the client's bindings with them
	// and 5 is what the two live ones leave. Four of those five are named
	// above, so this floor is again the row that catches a predicate narrowed
	// to its own self-test rather than the thing keeping Ban C honest.
	//
	// 5 -> 4 BY plan_shim.md PHASE 2c, lane 2-utilshared. Same deletion as the
	// server floor above, and the same kind of number: a real measurement of
	// how many client files hold the class, one of which stopped holding it
	// because its code went. Three of the four are still named by row above
	// (trace_filter_library.cpp, clientleafsystem.cpp, cdll_util.cpp), which is
	// where the weight sits; this floor's remaining job is the vacuity guard.
	// 4 -> 3 in C8 (lane c6-input): cdll_util.cpp left client.so. 3 -> 2 in C7
	// cluster 12 (2026-10-18): flashlighteffect.cpp left client.so, and its
	// CTraceFilterSkipPlayerAndViewModel resolver with it (the managed
	// flashlight's filter resolves the hit by handle, flashlight_trace_filter.h).
	// Its absence is asserted below with a sibling proved read.
	// 2 -> 1 in C7 cluster 12 L2 (2026-10-19): clientleafsystem.cpp left
	// client.so, and its dead dump's SDKEntityFromRenderable with it; the
	// trace-filter family, named above, is the class's one armed spelling left.
	// 1 -> 0 BY plan_server_native_v2 T4f, the server's reason: the trace-filter
	// family's server arms were the last spelling the client's source scan saw.
	EXPECT_EQ( client.EveryResolverHits.size(), 0u )
		<< "the same, on the client." << Describe( client.EveryResolverHits );

}

TEST( SdkPlayerCast, ThePredicatesMatchTheThreeSitesThatKilledTheProcess )
{
	// The counts above are a floor, and a floor can be met by forty of
	// something else. So the actual casualties are quoted VERBATIM, as they
	// stood when the step-5 simulation killed the process at them, and fed to
	// the predicates directly.
	//
	// Quoted rather than looked up in the tree on purpose: the sites have since
	// been fixed, so a fixture that read them from disk would have gone quiet
	// the moment it started to matter. These three lines are the specification.

	// CASUALTY 1 -- gameinterface.cpp ProcessUsercmds. Ban B: the cast is three
	// lines below the resolve, which is why the old one-line predicate missed
	// it entirely.
	{
		const Binding_t b = BindingOn( Squeezed(
			"\tCBaseEntity *pEnt = CBaseEntity::Instance(player);" ), true );
		ASSERT_EQ( RESOLVER_RAW, b.kind );
		EXPECT_EQ( std::string( "pEnt" ), b.strVar );

		EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed(
			"\tif ( pEnt && pEnt->IsPlayer() )" ), b.strVar ) );
		EXPECT_TRUE( MentionsVarInAPlayerCast( Squeezed(
			"\t\tpPlayer = static_cast< CBasePlayer * >( pEnt );" ), b.strVar ) )
			<< "the two-statement cast ban no longer matches the site it was "
			   "written for";
		EXPECT_FALSE( LineHasBannedCast(
			"\tCBaseEntity *pEnt = CBaseEntity::Instance(player);" ) )
			<< "if the ONE-LINE ban matched this, the two-statement ban would "
			   "never have been needed and this fixture is testing nothing";
	}

	// CASUALTY 2 -- gameinterface.cpp MarkEntitiesAsTouching. Ban C, and NOT A
	// CAST: two resolves and an `if` choosing between the SDK arm and the
	// standalone arm.
	{
		const Binding_t b = BindingOn( Squeezed(
			"\tCBaseEntity *entity = GetContainingEntity( e1 );" ), true );
		ASSERT_EQ( RESOLVER_RAW, b.kind );
		EXPECT_EQ( std::string( "entity" ), b.strVar );

		EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed(
			"\tif ( entity && entityTouched )" ), b.strVar ) );
		EXPECT_FALSE( MentionsVarInAPlayerCast( Squeezed(
			"\tif ( entity && entityTouched )" ), b.strVar ) )
			<< "no cast anywhere near this site; that is the whole point of it";
	}

	// CASUALTY 3 -- reached through gamemovement's traces, whose filters resolve
	// with EntityFromEntityHandle. util_shared.cpp's eleven sites are all this
	// shape and this is one of them verbatim.
	{
		const Binding_t b = BindingOn( Squeezed(
			"\tCBaseEntity *pEntity = EntityFromEntityHandle( pHandleEntity );" ), true );
		ASSERT_EQ( RESOLVER_RAW, b.kind );
		EXPECT_TRUE( MentionsVarInABooleanPosition( Squeezed(
			"\tif ( !pEntity )" ), b.strVar ) );
	}
}

TEST( SdkPlayerCast, TheResolverListIsHonest )
{
	// The five names Ban C exempts, read out of the tree and required to name
	// the partition. Exempting a resolver is only safe while the resolver
	// really asks GetGameEntity(); the day somebody simplifies one of these
	// back to a bare GetBaseEntity(), Ban C would fall silent everywhere and
	// nothing else in this file would notice.
	// One row per file, and the EXACT token its code must contain. Not "names
	// the partition somewhere" -- the mutation that proved this fixture red the
	// first time was passed by the file's own prose.
	struct Def_t { const char *pszFile; const char *pszFunction; const char *pszToken; };
	static const Def_t kDefs[] =
	{
		// GetContainingEntity's ROW IS GONE BECAUSE THE FUNCTION IS. It lived in
		// server/baseentity.h and its body WAS the token below; the close proved
		// it answered a constant NULL -- all three implementations of
		// IServerUnknown::GetBaseEntity() in server.so are `mov $0x0,%eax; ret`,
		// which game/server/util.h:724 had already written down in prose -- and
		// deleted it with its ten call sites. Its absence is asserted after this
		// loop, with the file proved read, so "the row was quietly dropped" and
		// "the function is gone" cannot be confused.
		// server/entitylist.h's row (CGlobalEntityList::GetBaseEntity) is
		// RETIRED: game/server/entitylist.h is compiled by nothing since
		// plan_server_novalve.md P5 and left the farm in P6, so there is no
		// server resolver in it left to exempt.
		// Shared, and it has to ask on BOTH sides: the trace filters are shared
		// code, so util_shared.cpp's eleven sites run on the client too.
		//
		// EntityFromEntityHandle WENT THE SAME WAY as GetContainingEntity and
		// for the same measured reason, and GameEntityFromEntityHandle is what
		// replaced it -- the rows are RE-POINTED at the successor, which asks
		// the object instead of casting it. The client arm is NEW: the client
		// had no such function at all, so shared trace code resolved a managed
		// entity to nothing and culled it unconditionally.
		{ "shared/util_shared.h", "GameEntityFromEntityHandle (server arm)",
		  "pServerEntity->GetGameEntity()" },
		{ "shared/util_shared.h", "GameEntityFromEntityHandle (client arm)",
		  "pEntity->GetGameEntity() : NULL" },
		// (The client's two, SDKEntityFromUnknown and SDKEntityFromRenderable,
		// went with the client's stock headers, 2026-10-25: no client object
		// answers GetBaseEntity(), and their one caller, the beam endpoint
		// facts, asked a question whose answer was always NULL.)
		// client/cliententitylist.cpp's row (CClientEntityList's four) went
		// when client.so stopped compiling that file and it left the source
		// tree.
	};

	for ( size_t i = 0; i < sizeof( kDefs ) / sizeof( kDefs[ 0 ] ); i++ )
	{
		const std::string text = SlurpCodeFromSourceRoot( kDefs[ i ].pszFile );
		ASSERT_FALSE( text.empty() ) << "could not read " << kDefs[ i ].pszFile;

		EXPECT_NE( std::string::npos, text.find( kDefs[ i ].pszToken ) )
			<< kDefs[ i ].pszFile << " no longer routes " << kDefs[ i ].pszFunction
			<< " through the partition -- looked for `" << kDefs[ i ].pszToken
			<< "` in its code, comments stripped -- so exempting it from Ban C "
			   "is wrong";
	}

	// THE TWO DELETED RESOLVERS, ASSERTED AS ABSENCES WITH THE READ PROVED
	// FIRST. Each still has a row on kPartitionedResolvers and on Ban A's list,
	// so a reintroduction is caught the moment it binds -- but a reintroduction
	// of the DEFINITION is what would make that possible, and it is the thing
	// this file would otherwise never look at again.
	//
	// GetContainingEntity's absence was read out of server/baseentity.h. That
	// header is compiled by nothing since plan_server_novalve.md P5 and left
	// the farm in P6, which is the stronger form: the file it was defined in
	// cannot be read at all. Anchored on a sibling the farm still links, so a
	// reader that sees nothing cannot satisfy it.
	ASSERT_FALSE( SlurpCodeFromSourceRoot( "shared/shareddefs.h" ).empty() )
		<< "shared/shareddefs.h did not read, so the absence below would pass "
		   "over a reader that sees nothing";
	EXPECT_TRUE( SlurpCodeFromSourceRoot( "server/baseentity.h" ).empty() )
		<< "server/baseentity.h is readable from the farm again. It is where "
		   "GetContainingEntity was defined; it resolved through "
		   "IServerUnknown::GetBaseEntity(), whose three implementations in "
		   "server.so all return NULL, so it can only answer NULL -- and every "
		   "call site it had was re-pointed at a live successor";

	const std::string strUtilSharedH = SlurpCodeFromSourceRoot( "shared/util_shared.h" );
	ASSERT_FALSE( strUtilSharedH.empty() )
		<< "shared/util_shared.h did not read, so the absence below would pass "
		   "over a file nobody opened";
	EXPECT_EQ( std::string::npos, strUtilSharedH.find( "inline CBaseEntity *EntityFromEntityHandle(" ) )
		<< "EntityFromEntityHandle is back in util_shared.h beside "
		   "GameEntityFromEntityHandle. Two resolvers for one question, one of "
		   "which answers a constant NULL, is how a trace filter comes to cull "
		   "every managed entity again";
	EXPECT_NE( std::string::npos, strUtilSharedH.find( "inline IGameEntity *TraceGameEntity(" ) )
		<< "TraceGameEntity is gone -- it is what a trace's m_pEnt is resolved "
		   "through now, and TraceEntity, the thing it replaced, answered a "
		   "constant NULL";

	// And the partition itself still asks the stable arm, on BOTH sides, with
	// one virtual through a structural cast.
	const std::string strCross =
		SlurpCodeFromSourceRoot( "../sourcesharp/game/shared/handle_cross.h" );
	ASSERT_FALSE( strCross.empty() ) << "handle_cross.h not found";

	EXPECT_NE( std::string::npos, strCross.find( "GameEntityOf( pObject ) != 0" ) )
		<< "SourceSharp_SDKEntityFromUnknown no longer asks the stable arm of "
		   "the partition; every exemption in this file rests on it doing so";

	EXPECT_NE( std::string::npos,
		strCross.find( "static_cast< TPartition * >( pObject )->GetBaseEntity()" ) )
		<< "the server's partition no longer reaches GetBaseEntity(), its one "
		   "accessor since plan_server_native_v2 T1.3";

	EXPECT_EQ( std::string::npos, strCross.find( "SourceSharpClientPartition" ) )
		<< "the client has its own partition again; on stock headers it asks "
		   "IClientUnknown::GetBaseEntity(), the server's question";

	EXPECT_EQ( std::string::npos, strCross.find( "SourceSharpNoPartition" ) )
		<< "the no-partition tag is back; it means \"this side cannot ask\", "
		   "and both sides can";
}

TEST( SdkPlayerCast, ToBasePlayerDereferencesNothingAtAllAndTheConversionMovedToOneNamedInline )
{
	// READ THE ASSERTIONS, NOT THE NAME. This fact was
	// ToBasePlayerAsksThePartitionBeforeItDereferences, and the ORDER of two
	// questions inside ToBasePlayer was the whole of it: IsPlayer() is a
	// virtual, a pointer that survived a NULL check but came off the parallel
	// hierarchy dispatches it through the wrong subobject, so GetGameEntity()
	// had to be asked FIRST -- otherwise this file's own advice, "ask
	// ToBasePlayer instead", taken by 429 call sites, was pointing people at
	// the same bug in a friendlier wrapper.
	//
	// THERE IS NO ORDER TO CHECK ANY MORE, AND THAT IS STRONGER, NOT WEAKER.
	// ToBasePlayer's SDK arm was the `#ifndef SS_CBASEENTITY_ALIAS` half of
	// itself -- text the compiler never saw, because the alias was armed in
	// every configuration this tree builds. The close deleted the 453 dead arms
	// and both overloads are now what the ARMED build always ran: a NULL check
	// and `return NULL`. Nothing is dereferenced, nothing is cast, and neither
	// virtual is asked -- so the hazard is closed by construction rather than by
	// an ordering a later edit could reverse.
	//
	// WHAT REPLACED THE CONVERSION, and this is the live half without which the
	// absences below would pass over a deleted header: a caller that has already
	// asked IsPlayer() converts through SourceSharp_PlayerOf in
	// shell_standalone_player.h -- ONE named inline with a debug dynamic_cast
	// check beside it, where fifteen hand-written spellings of the same
	// static_cast used to be.
	//
	// P6: THE HEADER IS GONE TOO. The region facts that read ToBasePlayer out
	// of server/player.h -- both overloads, no IsPlayer(), no GetGameEntity(),
	// no static_cast, four `return NULL;` -- are retired: game/server/player.h
	// is compiled by nothing since plan_server_novalve.md P5 and left the farm
	// in P6. Nothing can dereference through a ToBasePlayer no file includes,
	// so the header's absence is the stronger form of those absences, anchored
	// on a sibling the farm still links.
	ASSERT_FALSE( SlurpFromSourceRoot( "shared/shareddefs.h" ).empty() )
		<< "could not read shared/shareddefs.h, so the absence below would pass "
		   "over a reader that sees nothing";
	EXPECT_TRUE( SlurpFromSourceRoot( "server/player.h" ).empty() )
		<< "server/player.h is readable from the farm again. It holds stock's "
		   "ToBasePlayer; re-derive whether its arms dereference or cast before "
		   "anything includes it.";

	// THE LIVE SUCCESSOR. The conversion a caller actually needs did not
	// disappear with the cast; it became one named inline, checked in debug.
	const std::string strPlayer =
		SlurpCodeFromSourceRoot( "../sourcesharp/game/server/shell_standalone_player.h" );
	ASSERT_FALSE( strPlayer.empty() ) << "could not read shell_standalone_player.h";

	EXPECT_NE( std::string::npos,
		strPlayer.find( "inline CStandalonePlayer *SourceSharp_PlayerOf( CStandaloneEntity *pEnt )" ) )
		<< "SourceSharp_PlayerOf is gone -- it is the one conversion the fifteen "
		   "hand-written static_casts collapsed into";
	EXPECT_NE( std::string::npos,
		strPlayer.find( "Assert( !pEnt || dynamic_cast< CStandalonePlayer * >( pEnt ) != NULL );" ) )
		<< "SourceSharp_PlayerOf lost its debug dynamic_cast check. That check "
		   "is the entire difference between one named conversion and fifteen "
		   "unchecked casts written out by hand";
}

TEST( SdkPlayerCast, TheExemptionsAreWhereTheySay )
{
	const ScanResult_t &server = ServerScan();

	// SS_NOT_THE_PARTITION exists for exactly one thing: CServerNetworkProperty
	// has a GetBaseEntity() that returns its owner and has nothing to do with
	// the hierarchies. A handful, in named files. If the count grows, somebody
	// is silencing findings.
	EXPECT_LE( server.nExemptions, 8 )
		<< "more SS_NOT_THE_PARTITION markers than the name collision needs";

	for ( size_t i = 0; i < server.ExemptionFiles.size(); i++ )
	{
		const std::string &f = server.ExemptionFiles[ i ];
		const bool bKnown =
			f.find( "ServerNetworkProperty" ) != std::string::npos
			|| f.find( "entity_tools_server.cpp" ) != std::string::npos
			|| f.find( "mapentities.cpp" ) != std::string::npos
			|| f.find( "baseentity.cpp" ) != std::string::npos;

		EXPECT_TRUE( bKnown ) << "an exemption appeared in an unexpected file: " << f;
	}
}

//-----------------------------------------------------------------------------
// THEN: the bans themselves.
//-----------------------------------------------------------------------------

TEST( SdkPlayerCast, NoLiveSiteCastsAnEdictsEntityToAPlayerOnOneLine )
{
	const ScanResult_t &server = ServerScan();

	EXPECT_EQ( 0u, server.CastHits.size() )
		<< "A C-cast of an edict's entity to a player type is NULL today and a\n"
		   "wrong-vtable pointer the day Phase 13 step 5 makes GetBaseEntity()\n"
		   "return `this` -- and the NULL check next to it will PASS.\n"
		   "Ask UTIL_SDKPlayerFromEdict (util.h), CStandalonePlayer::FromEdict,\n"
		   "or UTIL_PlayerActivatorBySlot instead. Sites:"
		<< Describe( server.CastHits );
}

TEST( SdkPlayerCast, NoLiveSiteCastsAnEdictsEntityToAPlayerOverTwoStatements )
{
	const ScanResult_t &server = ServerScan();

	EXPECT_EQ( 0u, server.TwoStatementHits.size() )
		<< "The SAME shape as the one-line ban, spelled over two statements --\n"
		   "resolve into a variable, then cast that variable. This is CASUALTY 1:\n"
		   "gameinterface.cpp ProcessUsercmds died on the first user command of\n"
		   "the first step-5 simulation, and the one-line predicate could not\n"
		   "see it. Ask UTIL_SDKPlayerFromEdict instead. Sites:"
		<< Describe( server.TwoStatementHits );
}

TEST( SdkPlayerCast, NoLiveServerSiteNullChecksARawGetBaseEntity )
{
	const ScanResult_t &server = ServerScan();

	EXPECT_EQ( 0u, server.RawNullCheckHits.size() )
		<< "THE REAL CLASS. A pointer taken straight from X->GetBaseEntity()\n"
		   "and then tested for NULL. Today the parallel hierarchy answers NULL\n"
		   "and the test does what it looks like; at Phase 13 step 5 it answers\n"
		   "with the entity's IGameEntity view, the test PASSES, and whatever\n"
		   "the arm behind it does happens on a pointer whose vtable belongs to\n"
		   "another class. CASUALTY 2 (MarkEntitiesAsTouching) was this and was\n"
		   "not a cast at all.\n"
		   "Ask through a resolver that asks the partition -- GetContainingEntity,\n"
		   "CBaseEntity::Instance, gEntList.GetBaseEntity, EntityFromEntityHandle --\n"
		   "or call SourceSharp_SDKEntityFromUnknown< IServerEntity >() directly.\n"
		   "Sites:"
		<< Describe( server.RawNullCheckHits );
}

//-----------------------------------------------------------------------------
// AND THE CLIENT, WHICH IS A BAN.
//
// This was a RATCHET on eighteen counted sites, and the constant it pinned was
// the twenty-third check on this project that could not fail in the direction
// anybody wanted: IClientUnknown had no GetGameEntity(), so there was nothing
// for a client resolver to ask and no edit that could have taken the number to
// zero. It said so in its own fixture name.
//
// The eighteen, for the record, since a deleted list is a deleted argument:
//   alphamaterialproxy.cpp:50            IClientEntity      *pEnt
//   animatedentitytextureproxy.cpp:32,46 pRend->GetIClientUnknown()
//   c_sprite.cpp:393                     IClientEntity      *attachedto
//   cdll_util.cpp:669                    ternary on pClientEntity
//   cliententitylist.cpp:337,385,418,446 IClientUnknown     *pUnknown / pUnk
//   clientleafsystem.cpp:347,407,1133    pUnk / pRenderable->GetIClientUnknown()
//   clientshadowmgr.cpp:3492,3617        pRenderable->GetIClientUnknown()
//   particles_localspace.cpp:142         pRenderable->GetIClientUnknown()
//   proxypupil.cpp:76                    ternary on pEntity
//   studio_stats.cpp:118                 g_pStudioStatsEntity->GetIClientUnknown()
//   viewrender.cpp:4169                  ternary + chain -- THE ONE THAT KILLED
//                                        THE PROCESS under the step-5 canary
//
// Eleven of them were the CHAINED or TERNARY spellings, which the dry run's
// census could not match at all. All eighteen now ask SDKEntityFromUnknown or
// SDKEntityFromRenderable, and so do nine more that had NO NULL CHECK AT ALL
// and were therefore invisible to this scanner in either direction.
//-----------------------------------------------------------------------------

TEST( SdkPlayerCast, NoLiveClientSiteNullChecksARawGetBaseEntity )
{
	const ScanResult_t &client = ClientScan();

	EXPECT_EQ( 0u, client.RawNullCheckHits.size() )
		<< "THE REAL CLASS, on the client. A pointer taken straight from\n"
		   "X->GetBaseEntity() and then tested for NULL. The parallel\n"
		   "hierarchy answers NULL, so the test does what it looks like -- and\n"
		   "that is the whole problem: it is asking whether the object is on\n"
		   "the SDK's tree by asking a method whose NULL is an accident of\n"
		   "which hierarchy it is on. The retired step-5 canary made this arm\n"
		   "answer the entity's IGameEntity view instead, the test PASSED, and\n"
		   "the arm behind it ran on a pointer whose vtable belongs to another\n"
		   "class -- that is how the client canary died in\n"
		   "CRendering3dView::DrawOpaqueRenderables.\n"
		   "Ask SDKEntityFromUnknown or SDKEntityFromRenderable (c_baseentity.h),\n"
		   "or the entity list's GetBaseEntity / GetBaseEntityFromHandle.\n"
		   "Sites:"
		<< Describe( client.RawNullCheckHits );
}

} // namespace
