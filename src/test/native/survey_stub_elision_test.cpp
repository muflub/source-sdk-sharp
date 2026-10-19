//========= SourceSharp — what the survey's own stub is allowed to hide ======//
//
// Phase 13's typedef survey compiles the whole tree with -DTYPEDEF_SURVEY and
// `using CBaseEntity = IGameEntity;`, and histograms what breaks. The counts
// that come out of that run are what every remaining lane is scoped by. THIS
// FILE PINS THE BOUNDARY THAT MAKES THOSE COUNTS MEAN WHAT THEY SAY.
//
// WHAT WAS WRONG, AND WHAT THIS FILE USED TO PIN. Both entity headers put
// their TYPEDEF_SURVEY stub in front of the class and then let the `#else` arm
// run to the LAST LINE OF THE FILE. Arming therefore deleted the whole header,
// and the header's tail is not the class. It also holds the free funnels, the
// classname predicate, the entity macros, the extern tables and the CHandle
// specialisations — every one of which SURVIVES the flip. Code calling them is
// code that is already correct, and the armed survey reported all of it as
// `'SDKEntityFromUnknown' was not declared in this scope`.
//
// Measured at c182f158, both sides, one run each with the old shape and the
// new one: the client's artefact was 39 unique sites across 19 files of 1,978
// — code with a funnel, a NULL check and a collideable fallback already in
// place. The server had the same fault and a much smaller effect, 9 sites of
// 2,691, because `GetContainingEntity` is separately forward-declared at
// baseentity.h:391, above the stub, and so already survived arming.
//
// THE RULE THIS FILE NOW PINS. Arming elides the entity class and every
// out-of-line definition of its MEMBERS — exactly the span Phase 13 deletes —
// and nothing else. Runs of member definitions in the tail carry their own
// `#ifndef TYPEDEF_SURVEY`.
//
// THE ONE EXCEPTION, and it is a judgement rather than a mechanic: a free
// function whose BODY dereferences the entity keeps its DECLARATION when armed
// and loses its body. FClassnameIs on both sides, ToBaseCombatCharacter on the
// server, and the `CHandle<CBaseCombatWeapon>::Get()` specialisation, whose
// eager instantiation of SourceSharp_CrossHandleToWeapon asks the entity for
// MyCombatWeaponPointer. That was not theory: with those bodies left visible,
// a single armed TU failed IN THE HEADER, which would have failed all 202
// server and all 328 client TUs and destroyed both the failing-TU count and
// the site histogram. A declaration is what lets a CALLER typecheck, and
// whether callers typecheck is the entire question the instrument asks.
//
// WHY A TEST AND NOT A COMMENT: the boundary has two failure modes and neither
// leaves a diff that says so. Move a member definition out of a guarded run
// and every armed TU fails in the header — the survey then reports ~100% and
// means nothing. Extend a guard over a free function, or push the arm's
// `#endif` back down to EOF, and the over-count comes back and every client
// figure silently changes meaning again. Either edit should have to argue with
// a red test first.
//
// SHAPE: source-scanning, like header_alias_test.cpp and
// alias_mechanics_test.cpp, and for the same reason — the entity headers drag
// in the whole game prelude and cannot be compiled standalone twice. Comments
// are stripped before any match (this file's own prose names every symbol it
// searches for), and each claim is anchored on a per-file STRING, never a line
// number and never a line count, so ordinary edits do not move it.
//
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAMES. THE INSTRUMENT SERVED ITS PURPOSE.
//-----------------------------------------------------------------------------
//
// The survey stub existed so that a run could DELETE the entity class and count
// what broke. The port then did the deletion for real. At this tip:
//
//   * `#ifdef TYPEDEF_SURVEY` appears in NEITHER entity header. The only
//     conditional on that macro left in game/ or public/ is
//     baseentity_shared.h:87.
//   * `class C_BaseEntity : public IClientEntity` and
//     `class CBaseEntity : public IServerEntity` are gone, and so is every
//     out-of-line definition of their members. `CBaseEntity` is not a name in
//     the compiled tree at all (the-close 3/N, 0bc727ed9).
//   * c_baseentity.h is 372 lines and baseentity.h is 632, where both were well
//     over a thousand. WHAT IS LEFT IS EXACTLY THE TAIL THIS FILE FOUGHT TO
//     KEEP: the free funnels, the classname predicate, the entity macros, the
//     extern tables and the CHandle specialisations.
//
// So every claim below is RE-POINTED rather than deleted, and each one is the
// same claim with the conditional taken out of it:
//
//   "the elided arm ends at the class"        -> there is no arm, the class is
//                                                gone, and the tail is now the
//                                                whole header
//   "no member definition survives arming"    -> no member definition exists
//   "the free helpers are visible when armed" -> the free helpers need no guard
//                                                at all: they sit at the
//                                                include guard's depth
//
// AND EVERY ABSENCE IS PAIRED WITH THE LIVE TEXT THAT MUST BE THERE INSTEAD,
// because an emptied or renamed header satisfies an absence on its own. The
// scanner self-tests at the bottom are kept exactly as they were and they are
// now doing MORE work, not less: they are the only reason "the sweep found zero
// member definitions" is a measurement rather than a scanner that has forgotten
// what one looks like.
//
// ONE FREE NAME IN THE OLD SURVIVOR LIST DID NOT SURVIVE. `GetContainingEntity`
// was deleted outright (the-close 5/N, cf0949d99): it bottoms out in
// `IServerUnknown::GetBaseEntity()`, and every implementation of that in both
// DLLs is `mov $0x0,%eax; ret`, so it was a constant NULL. Its successor is
// `GameEntityFromEntityHandle` in game/shared/util_shared.h, which asks the
// virtual `GetGameEntity()` and exists on BOTH sides. The fact that named it is
// re-pointed at that pair rather than dropped.
//
// P6: THE SERVER SIDE IS GONE FROM kSides. game/server/baseentity.h is compiled
// by nothing since plan_server_novalve.md P5 and left the farm in P6, so the
// facts about its text -- the surviving free helpers at depth 1, the macros, the
// line floors -- lost their subject and are retired. Its absences (no class, no
// stub, no member definition, no GetContainingEntity) have a stronger form, the
// header's own absence, asserted once and anchored in
// TheSurveyStubAndTheClassItElidedAreBothGone. The client side is unchanged.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "ss_ci_skip.h"

#include <sys/stat.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

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

// Strip // and /* */ comments, preserving line structure so an index into the
// returned vector still means the same line of the file. REQUIRED, not
// tidiness: both headers explain their own stub in prose that names
// TYPEDEF_SURVEY and SDKEntityFromUnknown, and a raw-text match would find
// those sentences and conclude the code was there.
std::vector< std::string > StripComments( const std::vector< std::string > &in )
{
	std::vector< std::string > out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		const std::string &s = in[ i ];
		std::string line;
		for ( size_t j = 0; j < s.size(); j++ )
		{
			if ( bInBlock )
			{
				if ( j + 1 < s.size() && s[ j ] == '*' && s[ j + 1 ] == '/' )
				{
					bInBlock = false;
					j++;
				}
				continue;
			}
			if ( j + 1 < s.size() && s[ j ] == '/' && s[ j + 1 ] == '*' )
			{
				bInBlock = true;
				j++;
				continue;
			}
			if ( j + 1 < s.size() && s[ j ] == '/' && s[ j + 1 ] == '/' )
				break;
			line += s[ j ];
		}
		out.push_back( line );
	}
	return out;
}

std::vector< std::string > ReadStripped( const std::string &strRelative )
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
	return StripComments( lines );
}

bool Has( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

bool StartsWith( const std::string &hay, const char *needle )
{
	const size_t n = strlen( needle );
	return hay.size() >= n && hay.compare( 0, n, needle ) == 0;
}

//-----------------------------------------------------------------------------
// WHICH LINES THE COMPILER SEES WITH -DTYPEDEF_SURVEY.
//
// The whole file turns on this, so it is a real evaluation of the conditional
// nesting rather than a search for one `#else`. A frame is ARMED-ONLY
// (`#ifdef TYPEDEF_SURVEY`), UNARMED-ONLY (`#ifndef TYPEDEF_SURVEY`) or
// NEUTRAL (everything else, both of whose arms may be visible); `#else` and
// `#elif` flip the first two and leave NEUTRAL alone. A line is visible when
// armed unless some enclosing frame is currently UNARMED-ONLY.
//
// Note what this deliberately does NOT do: it does not try to decide _DEBUG,
// SOURCESHARP or SIXENSE. Treating those as neutral is what makes the claims
// below independent of build configuration -- the boundary must hold in every
// one of them.
//-----------------------------------------------------------------------------
enum { kNeutral = 0, kArmedOnly = 1, kUnarmedOnly = 2 };

std::vector< char > VisibleWhenArmed( const std::vector< std::string > &lines )
{
	std::vector< char > out( lines.size(), 1 );
	std::vector< int > stack;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trimmed( lines[ i ] );

		if ( StartsWith( t, "#ifdef" ) || StartsWith( t, "#ifndef" )
			|| StartsWith( t, "#if" ) )
		{
			int st = kNeutral;
			if ( StartsWith( t, "#ifdef" ) && Has( t, "TYPEDEF_SURVEY" ) )
				st = kArmedOnly;
			else if ( StartsWith( t, "#ifndef" ) && Has( t, "TYPEDEF_SURVEY" ) )
				st = kUnarmedOnly;
			stack.push_back( st );
		}
		else if ( ( StartsWith( t, "#else" ) || StartsWith( t, "#elif" ) )
			&& !stack.empty() )
		{
			int &top = stack.back();
			if ( top == kArmedOnly )
				top = kUnarmedOnly;
			else if ( top == kUnarmedOnly )
				top = kArmedOnly;
		}
		else if ( StartsWith( t, "#endif" ) && !stack.empty() )
		{
			stack.pop_back();
		}

		bool bVisible = true;
		for ( size_t k = 0; k < stack.size(); k++ )
		{
			if ( stack[ k ] == kUnarmedOnly )
			{
				bVisible = false;
				break;
			}
		}
		out[ i ] = bVisible ? 1 : 0;
	}
	return out;
}

//-----------------------------------------------------------------------------
// The two sides, described rather than duplicated.
//
// The entity spelling is TWO strings on the client on purpose. Its tail used to
// write `C_BaseEntity::` almost everywhere and `CBaseEntity::` in a handful of
// definitions (BoundingRadius, IsPointSized, GetRenderColor), and a scanner
// carrying one spelling would silently miss those. BOTH spellings are now
// required to be ABSENT, and the second one is still carried for exactly the
// reason it was added: an absence measured with one spelling is an absence of
// one spelling. See TheTwoEntitySpellingsAreDistinct below.
//-----------------------------------------------------------------------------
struct Side
{
	const char *pszName;
	const char *pszHeader;
	const char *pszIncludeGuard;    // `#ifndef X` -- the ONE conditional the
	                                // whole header sits inside, which is what
	                                // makes "depth 1" mean "unconditional"
	const char *pszRetiredClassHead; // the class the stub elided; must be GONE
	const char *pszStubClassHead;    // the stub that stood in for it; also GONE
	const char *pszScope1;          // `Ent::` -- member definitions
	const char *pszScope2;          // second spelling, or NULL
	// Named member definitions that must NOT BE THERE. These were the anchors
	// for "found and elided"; the class is deleted, so they are the anchors for
	// "deleted with it". Kept as STRINGS and kept one per former guarded run,
	// so a class creeping back one run at a time is named rather than merely
	// changing a total.
	const char *pszDeletedMember[ 5 ];
	const char *pszSurvivor[ 4 ];   // free names that must be there, unguarded
	// The header is not empty: its NON-BLANK lines once comments are stripped
	// (directives included), so the floor reads the same whether the SDK tree
	// is the fork, whose copy carries our comments, or the comment-free
	// patched upstream (make sdk-upstream). A raw line count read 369 against
	// 230 for the same code and failed the second.
	int         nMinLines;
	int         nMinCodeLines;      // ...and its TAIL is what is left of it
};

const Side kSides[] = {
	{ "client", "game/client/c_baseentity.h",
	  "#ifndef C_BASEENTITY_H",
	  "class C_BaseEntity : public IClientEntity",
	  "class C_BaseEntity : public IGameEntity",
	  "C_BaseEntity::", "CBaseEntity::",
	  { "C_BaseEntity::CollisionProp",        // run 1, first
	    "C_BaseEntity::GetAbsVelocity",       // run 1, last
	    "C_BaseEntity::Instance",             // run 2, first
	    "CBaseEntity::BoundingRadius",        // run 2, the SERVER spelling
	    NULL },
	  { "SDKEntityFromUnknown", "SDKEntityFromRenderable", "FClassnameIs", NULL },
	  120, 80 },
	// The server row ({ "server", "game/server/baseentity.h", ... }) is
	// RETIRED: game/server/baseentity.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6. See the P6 note at
	// the head of the file for where its absences went.
};

// A line that DEFINES an out-of-line member of the entity class. Every one in
// either tail is written `inline <type> Ent::Name(`, which is what makes this
// a text question at all. The `#define` exclusion is load-bearing: the SetThink
// / SetTouch / SetUse / SetBlocked / SetMoveDone macros all contain `Ent::` in
// a static_cast and are NOT member definitions -- they survive arming, and a
// scanner that counted them would demand they be elided.
bool IsMemberDefinition( const std::string &t, const Side &side )
{
	if ( !StartsWith( t, "inline" ) )
		return false;
	if ( !Has( t, "(" ) )
		return false;
	const std::string head = t.substr( 0, t.find( '(' ) );
	if ( Has( head, side.pszScope1 ) )
		return true;
	return side.pszScope2 != NULL && Has( head, side.pszScope2 );
}

// Preprocessor nesting depth per line. The header's own include guard wraps
// everything, so depth 1 IS unconditional -- which is asserted rather than
// assumed by TheSurvivingFreeHelpersNeedNoGuardNow, which checks the outermost
// open directive is the guard before it reads any depth.
std::vector< int > ConditionalDepth( const std::vector< std::string > &lines )
{
	std::vector< int > out( lines.size(), 0 );
	int nDepth = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trimmed( lines[ i ] );
		if ( StartsWith( t, "#if" ) )
		{
			nDepth++;
			out[ i ] = nDepth;
			continue;
		}
		if ( StartsWith( t, "#endif" ) )
		{
			out[ i ] = nDepth;
			if ( nDepth > 0 )
				nDepth--;
			continue;
		}
		out[ i ] = nDepth;
	}
	return out;
}

// The first conditional directive in the file, so "everything is inside the
// include guard" is a measurement and not a habit.
std::string FirstConditional( const std::vector< std::string > &lines )
{
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trimmed( lines[ i ] );
		if ( StartsWith( t, "#if" ) )
			return t;
	}
	return std::string();
}

// Non-blank, non-directive lines. Comments are already stripped by the caller,
// so this is the header's actual CODE -- the tail the survey stub used to
// swallow, and now the whole of both files.
int CountNonBlankLines( const std::vector< std::string > &lines )
{
	int n = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( !Trimmed( lines[ i ] ).empty() )
			n++;
	}
	return n;
}

int CountCodeLines( const std::vector< std::string > &lines )
{
	int n = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trimmed( lines[ i ] );
		if ( t.empty() || StartsWith( t, "#" ) )
			continue;
		n++;
	}
	return n;
}

struct StubSpan
{
	size_t iIfdef;
	size_t iElse;
	size_t iEndif;
	bool   bFound;
};

StubSpan FindStubSpan( const std::vector< std::string > &lines )
{
	StubSpan span;
	span.bFound = false;
	span.iIfdef = span.iElse = span.iEndif = lines.size();

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( Trimmed( lines[ i ] ) == "#ifdef TYPEDEF_SURVEY" )
		{
			span.iIfdef = i;
			break;
		}
	}
	if ( span.iIfdef == lines.size() )
		return span;

	// Matched by PREPROCESSOR NESTING DEPTH, not by the `// TYPEDEF_SURVEY`
	// label on the #endif. Comments are stripped before this runs, so that
	// label is already gone -- the first cut of this scanner searched for it
	// and reported the intact header as malformed. Depth also survives any
	// conditional nested inside either arm.
	int nDepth = 1;
	for ( size_t i = span.iIfdef + 1; i < lines.size(); i++ )
	{
		const std::string t = Trimmed( lines[ i ] );
		if ( StartsWith( t, "#if" ) )
		{
			nDepth++;
		}
		else if ( StartsWith( t, "#endif" ) )
		{
			nDepth--;
			if ( nDepth == 0 )
			{
				span.iEndif = i;
				break;
			}
		}
		else if ( nDepth == 1 && span.iElse == lines.size()
			&& ( StartsWith( t, "#else" ) || StartsWith( t, "#elif" ) ) )
		{
			span.iElse = i;
		}
	}

	span.bFound = ( span.iElse < lines.size() && span.iEndif < lines.size() );
	return span;
}

} // namespace

//-----------------------------------------------------------------------------
// The claims.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was TheStubIsWellFormed and it
// required the `#ifdef TYPEDEF_SURVEY` / `#else` / `#endif` triple to be intact
// and the real class head to be present. Neither exists: the port deleted the
// entity class for real, which is the outcome the stub was an instrument for.
//
// So the polarity is inverted -- and paired, because "no stub" and "no class"
// are both true of a file that was emptied, renamed or moved. Each absence
// stands next to a live name from the tail that must still be there.
//-----------------------------------------------------------------------------
TEST( SurveyStubElision, TheSurveyStubAndTheClassItElidedAreBothGone )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	// THE SERVER SIDE, as the header's own absence. game/server/baseentity.h
	// is compiled by nothing since plan_server_novalve.md P5 and left the farm
	// in P6; that is the stronger form of "no CBaseEntity class, no stub, no
	// member definition, no GetContainingEntity in it". Anchored on a sibling
	// the farm still links, so a reader that sees nothing cannot satisfy it.
	ASSERT_FALSE( ReadStripped( "game/server/activator.h" ).empty() )
		<< "game/server/activator.h is missing or unreadable, so the absence "
		   "below would be answered by a reader that sees nothing";
	EXPECT_TRUE( ReadStripped( "game/server/baseentity.h" ).empty() )
		<< "game/server/baseentity.h is readable from the farm again. The "
		   "server row of kSides was retired when it left; put it back before "
		   "trusting any claim in this file about the server.";

	for ( size_t s = 0; s < sizeof( kSides ) / sizeof( kSides[ 0 ] ); s++ )
	{
		const Side &side = kSides[ s ];
		const std::vector< std::string > lines = ReadStripped( side.pszHeader );
		ASSERT_FALSE( lines.empty() )
			<< side.pszHeader << " is missing or unreadable";

		// ---- the live text FIRST, so nothing below can pass over an empty or
		// renamed file.
		for ( size_t h = 0; side.pszSurvivor[ h ] != NULL; h++ )
		{
			bool bThere = false;
			for ( size_t i = 0; i < lines.size() && !bThere; i++ )
				bThere = Has( lines[ i ], side.pszSurvivor[ h ] );
			ASSERT_TRUE( bThere )
				<< side.pszHeader << " no longer names " << side.pszSurvivor[ h ]
				<< ". This is the TAIL -- the part the survey stub was moved to "
				   "stop swallowing -- and if it is not here, every absence "
				   "below is an absence from the wrong file.";
		}
		EXPECT_EQ( side.pszIncludeGuard, FirstConditional( lines ) )
			<< side.pszHeader << "'s first conditional is no longer its own "
			   "include guard.";

		// ---- and now the absences.
		int nStub = 0, nSurveyConditional = 0, nRealHead = 0, nStubHead = 0;
		for ( size_t i = 0; i < lines.size(); i++ )
		{
			const std::string t = Trimmed( lines[ i ] );
			if ( t == "#ifdef TYPEDEF_SURVEY" )
				nStub++;
			if ( StartsWith( t, "#if" ) && Has( t, "TYPEDEF_SURVEY" ) )
				nSurveyConditional++;
			if ( Has( lines[ i ], side.pszRetiredClassHead ) )
				nRealHead++;
			if ( Has( lines[ i ], side.pszStubClassHead ) )
				nStubHead++;
		}

		EXPECT_EQ( 0, nStub ) << side.pszHeader
			<< " carries a `#ifdef TYPEDEF_SURVEY` stub again. The survey was an "
			   "instrument for deleting the entity class; the class is deleted, "
			   "so a stub standing in for it is standing in for nothing.";
		EXPECT_EQ( 0, nSurveyConditional ) << side.pszHeader
			<< " conditions on TYPEDEF_SURVEY. Nothing defines it in this tree "
			   "any more, so an `#ifndef` run on it is a dead arm and an "
			   "`#ifdef` arm is unreachable code.";
		EXPECT_EQ( 0, nRealHead ) << side.pszHeader << " declares \""
			<< side.pszRetiredClassHead << "\" again. That class was deleted in "
			   "the-close 3/N; its members are managed code now.";
		EXPECT_EQ( 0, nStubHead ) << side.pszHeader << " declares the survey's "
			   "stand-in class \"" << side.pszStubClassHead << "\".";
	}
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was
// TheElidedArmEndsAtTheClassAndNotAtTheEndOfTheFile, and it asserted that the
// elided arm's last line is the class's closing brace and that 200+ code lines
// follow the `#endif`.
//
// The whole point of that boundary was WHICH PART OF THE HEADER SURVIVES. The
// answer is now unconditional and complete: the class is gone, the arm is gone,
// and what survives is the entire file. c_baseentity.h went from well over a
// thousand lines to 372 and baseentity.h to 632, and what is left is precisely
// the tail the arm used to take with it.
//
// The floor moved with it. It has to: the old ">200 code lines AFTER the
// #endif" cannot be satisfied by a client header with 107 code lines in total,
// and quoting the old number would have been a check that cannot pass rather
// than one that cannot fail. The new floors are per side and are the same claim
// -- the tail is substantial and was not emptied along with the class.
//-----------------------------------------------------------------------------
TEST( SurveyStubElision, TheTailTheArmUsedToSwallowIsNowTheWholeHeader )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	for ( size_t s = 0; s < sizeof( kSides ) / sizeof( kSides[ 0 ] ); s++ )
	{
		const Side &side = kSides[ s ];
		const std::vector< std::string > lines = ReadStripped( side.pszHeader );
		ASSERT_FALSE( lines.empty() ) << side.pszHeader;

		// There is no arm to find. FindStubSpan is the same scanner that used to
		// locate it, so this is the absence measured by the instrument that
		// would see it if it were back, rather than by a fresh string search.
		const StubSpan span = FindStubSpan( lines );
		EXPECT_FALSE( span.bFound )
			<< side.pszHeader << " has a `#ifdef TYPEDEF_SURVEY` / `#else` / "
			   "`#endif` triple again at line " << ( span.iIfdef + 1 ) << ".";
		EXPECT_EQ( lines.size(), span.iIfdef )
			<< side.pszHeader << " opens a survey stub at line "
			<< ( span.iIfdef + 1 ) << ".";

		// The tail is still substantial, which is the half that catches the
		// header being gutted rather than merely un-stubbed.
		EXPECT_GE( CountNonBlankLines( lines ), side.nMinLines )
			<< side.pszHeader << " is only " << CountNonBlankLines( lines )
			<< " non-blank lines of code. The class was supposed to go; the free funnels, the "
			   "classname predicate, the entity macros, the extern tables and "
			   "the CHandle specialisations were supposed to stay.";
		EXPECT_GE( CountCodeLines( lines ), side.nMinCodeLines )
			<< side.pszHeader << " has only " << CountCodeLines( lines )
			<< " non-directive code lines.";

		// And nothing in it is conditional on arming any more: with
		// -DTYPEDEF_SURVEY every line of both headers is still visible. This is
		// the old claim's real content -- "arming elides only the class" -- at
		// the point where there is no class left for it to elide.
		const std::vector< char > vis = VisibleWhenArmed( lines );
		ASSERT_EQ( lines.size(), vis.size() );
		int nHidden = 0;
		std::string strFirstHidden;
		for ( size_t i = 0; i < lines.size(); i++ )
		{
			if ( vis[ i ] )
				continue;
			if ( !nHidden )
				strFirstHidden = Trimmed( lines[ i ] );
			nHidden++;
		}
		EXPECT_EQ( 0, nHidden )
			<< side.pszHeader << ": " << nHidden << " line(s) would be elided by "
			   "-DTYPEDEF_SURVEY, the first being \"" << strFirstHidden
			<< "\". Arming has nothing left to elide here; a line that it hides "
			   "is a guarded run that came back.";
	}
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was NoMemberDefinitionSurvivesArming
// and it required every out-of-line member of the entity class to be inside a
// `#ifndef TYPEDEF_SURVEY` run. There are no such members: the class is gone
// and its members are managed code.
//
// "Elided" therefore becomes "absent", which is a STRONGER claim and a much
// easier one to fake, so it is anchored three ways:
//
//   * the sweep must still be able to RECOGNISE an out-of-line definition. The
//     three fixture facts at the bottom of this file drive IsMemberDefinition
//     over the exact shapes it used to find, so "the sweep found zero" cannot
//     mean "the sweep has forgotten what one looks like".
//   * it must still be able to SEE `inline ... (` lines in these very files --
//     there are some, they are the surviving free funnels -- so a read that
//     silently returned nothing is red.
//   * both entity SCOPES must be absent from the code entirely, in both
//     spellings, so a member that came back under the other spelling is caught.
//-----------------------------------------------------------------------------
TEST( SurveyStubElision, NoOutOfLineEntityMemberDefinitionRemainsAtAll )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	for ( size_t s = 0; s < sizeof( kSides ) / sizeof( kSides[ 0 ] ); s++ )
	{
		const Side &side = kSides[ s ];
		const std::vector< std::string > lines = ReadStripped( side.pszHeader );
		ASSERT_FALSE( lines.empty() ) << side.pszHeader;

		// ---- the sweep can see `inline <...>(` lines in THIS file.
		int nInlineDefs = 0;
		for ( size_t i = 0; i < lines.size(); i++ )
		{
			const std::string t = Trimmed( lines[ i ] );
			if ( StartsWith( t, "inline" ) && Has( t, "(" ) )
				nInlineDefs++;
		}
		ASSERT_GT( nInlineDefs, 0 )
			<< side.pszHeader << " has no `inline ...(` line at all. Every "
			   "absence below is then an absence from a file this scan is not "
			   "reading, or from a shape it no longer recognises.";

		// ---- no member definition of the entity class, under either spelling.
		int nMembers = 0;
		std::string strReport;
		for ( size_t i = 0; i < lines.size(); i++ )
		{
			const std::string t = Trimmed( lines[ i ] );
			if ( !IsMemberDefinition( t, side ) )
				continue;
			nMembers++;
			strReport += "\n    " + std::string( side.pszHeader ) + ":"
			          + std::to_string( i + 1 ) + "  " + t;
		}
		EXPECT_EQ( 0, nMembers )
			<< side.pszHeader << " defines an out-of-line member of the entity "
			   "class. That class does not exist -- it was deleted, not stubbed "
			   "-- so this does not compile in any TU that includes this header."
			<< strReport;

		// ---- and the scope itself is not spelled in code anywhere.
		const char *pszScopes[ 2 ] = { side.pszScope1, side.pszScope2 };
		for ( size_t k = 0; k < 2; k++ )
		{
			if ( pszScopes[ k ] == NULL )
				continue;
			int nScope = 0;
			std::string strWhere;
			for ( size_t i = 0; i < lines.size(); i++ )
			{
				if ( !Has( lines[ i ], pszScopes[ k ] ) )
					continue;
				nScope++;
				strWhere += "\n    " + std::to_string( i + 1 ) + "  "
				          + Trimmed( lines[ i ] );
			}
			EXPECT_EQ( 0, nScope )
				<< side.pszHeader << " spells `" << pszScopes[ k ]
				<< "` in code. Comments are stripped before this runs and they "
				   "still say CBaseEntity on purpose (0bc727ed9); this is the "
				   "code." << strWhere;
		}

		// ---- each named member is gone by name, so a class creeping back one
		// run at a time is reported rather than merely changing a total.
		for ( size_t a = 0; side.pszDeletedMember[ a ] != NULL; a++ )
		{
			const char *pszGone = side.pszDeletedMember[ a ];
			bool bThere = false;
			size_t iWhere = 0;
			for ( size_t i = 0; i < lines.size() && !bThere; i++ )
			{
				if ( Has( lines[ i ], pszGone ) )
				{
					bThere = true;
					iWhere = i + 1;
				}
			}
			EXPECT_FALSE( bThere )
				<< side.pszHeader << ":" << iWhere << " names " << pszGone
				<< " again. It was one of the anchors for the guarded member "
				   "runs the survey stub elided, and those runs went with the "
				   "class.";
		}
	}
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was
// TheSurvivingFreeHelpersAreVisibleWhenArmed. The helpers did survive, and the
// claim is now the stronger one the survival was for: they need NO GUARD. Each
// sits at preprocessor depth 1 -- inside the header's own include guard and
// inside nothing else -- which is checked only after the file's outermost
// conditional is confirmed to BE that include guard.
//
// ONE NAME IN THE OLD LIST DID NOT SURVIVE, and it is asserted here rather than
// quietly dropped. `GetContainingEntity` is deleted (cf0949d99): it bottoms out
// in `IServerUnknown::GetBaseEntity()` and all eight implementations of that in
// the two DLLs are `mov $0x0,%eax; ret`, so it answered NULL for every edict
// ever passed to it. Its successor is `GameEntityFromEntityHandle` in
// game/shared/util_shared.h, which asks the virtual `GetGameEntity()` and exists
// on both sides -- so the absence is asserted WITH the replacement, because an
// absence on its own is also what a deleted header looks like.
//-----------------------------------------------------------------------------
TEST( SurveyStubElision, TheSurvivingFreeHelpersNeedNoGuardNow )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	for ( size_t s = 0; s < sizeof( kSides ) / sizeof( kSides[ 0 ] ); s++ )
	{
		const Side &side = kSides[ s ];
		const std::vector< std::string > lines = ReadStripped( side.pszHeader );
		ASSERT_FALSE( lines.empty() ) << side.pszHeader;

		ASSERT_EQ( side.pszIncludeGuard, FirstConditional( lines ) )
			<< side.pszHeader << "'s outermost conditional is not its include "
			   "guard, so depth 1 does not mean unconditional here and every "
			   "depth below is measuring something else.";

		const std::vector< int > depth = ConditionalDepth( lines );
		ASSERT_EQ( lines.size(), depth.size() );

		for ( size_t h = 0; side.pszSurvivor[ h ] != NULL; h++ )
		{
			const char *pszName = side.pszSurvivor[ h ];
			bool bFound = false;
			size_t iWhere = 0;
			for ( size_t i = 0; i < lines.size() && !bFound; i++ )
			{
				const std::string t = Trimmed( lines[ i ] );
				// A declaration or a definition: the name, then a paren, on a
				// line that is not a #define and not a call inside a body.
				if ( StartsWith( t, "#" ) )
					continue;
				if ( !Has( t, pszName ) || !Has( t, "(" ) )
					continue;
				if ( t.find( pszName ) > t.find( '(' ) )
					continue;
				if ( !Has( t, "*" ) && !StartsWith( t, "inline" )
					&& !StartsWith( t, "bool" ) )
					continue;
				bFound = true;
				iWhere = i;
			}

			ASSERT_TRUE( bFound )
				<< side.pszHeader << ": " << pszName
				<< " has no declaration or definition at all. It survives the "
				   "flip, so it has to be here; while it was hidden, every "
				   "CALLER of it was reported as `'" << pszName << "' was not "
				   "declared in this scope` and counted as flip work. That "
				   "artefact was 39 client sites.";

			EXPECT_EQ( 1, depth[ iWhere ] )
				<< side.pszHeader << ":" << ( iWhere + 1 ) << ": " << pszName
				<< " sits at conditional depth " << depth[ iWhere ]
				<< ", not 1. It survives the flip unconditionally; a guard round "
				   "it is a build in which its callers do not typecheck.";
		}
	}

	// The one that did not survive, and the one that replaced it. The
	// absence half read game/server/baseentity.h for GetContainingEntity; that
	// header is compiled by nothing since plan_server_novalve.md P5 and left
	// the farm in P6, and its own absence is asserted, anchored, in
	// TheSurveyStubAndTheClassItElidedAreBothGone.

	const std::vector< std::string > shared = ReadStripped( "game/shared/util_shared.h" );
	ASSERT_FALSE( shared.empty() ) << "could not read game/shared/util_shared.h";
	bool bSuccessor = false, bTrace = false;
	for ( size_t i = 0; i < shared.size(); i++ )
	{
		const std::string t = Trimmed( shared[ i ] );
		if ( StartsWith( t, "inline" ) && Has( t, "GameEntityFromEntityHandle(" ) )
			bSuccessor = true;
		if ( StartsWith( t, "inline" ) && Has( t, "TraceGameEntity(" ) )
			bTrace = true;
	}
	EXPECT_TRUE( bSuccessor )
		<< "game/shared/util_shared.h no longer defines GameEntityFromEntityHandle, "
		   "which is what replaced GetContainingEntity and EntityFromEntityHandle. "
		   "Without it the absence above is a deletion with nothing behind it.";
	EXPECT_TRUE( bTrace )
		<< "game/shared/util_shared.h no longer defines TraceGameEntity, which is "
		   "what replaced TraceEntity on both sides.";
}

// The entity macros survive arming too. They are not the class, and a use of
// one is a site the flip has to look at on its own terms rather than a name
// the instrument deleted.
TEST( SurveyStubElision, TheEntityMacrosSurviveArming )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	static const char *kMacros[] = { "SetThink", "SetContextThink" };

	for ( size_t s = 0; s < sizeof( kSides ) / sizeof( kSides[ 0 ] ); s++ )
	{
		const Side &side = kSides[ s ];
		const std::vector< std::string > lines = ReadStripped( side.pszHeader );
		ASSERT_FALSE( lines.empty() ) << side.pszHeader;
		const std::vector< char > vis = VisibleWhenArmed( lines );

		for ( size_t m = 0; m < sizeof( kMacros ) / sizeof( kMacros[ 0 ] ); m++ )
		{
			bool bFound = false;
			for ( size_t i = 0; i < lines.size(); i++ )
			{
				const std::string t = Trimmed( lines[ i ] );
				if ( !StartsWith( t, "#define " ) )
					continue;
				if ( t.compare( 8, strlen( kMacros[ m ] ), kMacros[ m ] ) != 0 )
					continue;
				bFound = true;
				EXPECT_TRUE( vis[ i ] )
					<< side.pszHeader << ":" << ( i + 1 ) << " defines "
					<< kMacros[ m ] << " where -DTYPEDEF_SURVEY cannot see it.";
			}
			EXPECT_TRUE( bFound )
				<< side.pszHeader << " no longer defines " << kMacros[ m ]
				<< "; this claim has gone vacuous.";
		}
	}
}

//-----------------------------------------------------------------------------
// Scanner self-tests — the half that stops the claims above from being
// unfalsifiable. Every one of these was watched failing before being kept.
//-----------------------------------------------------------------------------

TEST( SurveyStubElisionScanner, StripCommentsRemovesBothFormsAndKeepsCode )
{
	std::vector< std::string > in;
	in.push_back( "// inline SDKEntityFromUnknown( x ) is gone" );
	in.push_back( "int a; // trailing" );
	in.push_back( "/* inline FClassnameIs( y )" );
	in.push_back( "   still in the block */ int b;" );

	const std::vector< std::string > out = StripComments( in );

	ASSERT_EQ( 4u, out.size() ) << "line structure must survive stripping";
	EXPECT_EQ( "", Trimmed( out[ 0 ] ) );
	EXPECT_EQ( "int a;", Trimmed( out[ 1 ] ) );
	EXPECT_EQ( "", Trimmed( out[ 2 ] ) );
	EXPECT_EQ( "int b;", Trimmed( out[ 3 ] ) );

	// The point of stripping, stated as an assertion: prose naming a funnel
	// must not survive to be matched as a definition.
	for ( size_t i = 0; i < out.size(); i++ )
	{
		EXPECT_FALSE( Has( out[ i ], "SDKEntityFromUnknown" ) );
		EXPECT_FALSE( Has( out[ i ], "FClassnameIs" ) );
	}
}

// The scanner every claim above rests on, exercised on a fixture whose answer
// is written out by hand. Without this the conditional evaluation is a black
// box that could return "visible" for everything and pass four tests.
TEST( SurveyStubElisionScanner, VisibleWhenArmedEvaluatesTheConditionals )
{
	std::vector< std::string > in;
	in.push_back( "int always_a;" );          // 0  visible
	in.push_back( "#ifdef TYPEDEF_SURVEY" );  // 1
	in.push_back( "int armed_only;" );        // 2  visible
	in.push_back( "#else" );                  // 3
	in.push_back( "int unarmed_only;" );      // 4  HIDDEN
	in.push_back( "#endif" );                 // 5
	in.push_back( "#ifndef TYPEDEF_SURVEY" ); // 6
	in.push_back( "int guarded_run;" );       // 7  HIDDEN
	in.push_back( "#ifdef _DEBUG" );          // 8   nested neutral...
	in.push_back( "int nested_in_guard;" );   // 9  HIDDEN (outer still hides)
	in.push_back( "#endif" );                 // 10
	in.push_back( "#endif" );                 // 11
	in.push_back( "#ifdef SOURCESHARP" );     // 12  neutral
	in.push_back( "int neutral_arm;" );       // 13 visible
	in.push_back( "#else" );                  // 14
	in.push_back( "int neutral_else;" );      // 15 visible -- NEUTRAL never hides
	in.push_back( "#endif" );                 // 16
	in.push_back( "int always_b;" );          // 17 visible

	const std::vector< char > vis = VisibleWhenArmed( in );
	ASSERT_EQ( in.size(), vis.size() );

	EXPECT_TRUE( vis[ 0 ] )   << "code outside every conditional";
	EXPECT_TRUE( vis[ 2 ] )   << "the armed arm of #ifdef TYPEDEF_SURVEY";
	EXPECT_FALSE( vis[ 4 ] )  << "the #else of #ifdef TYPEDEF_SURVEY is elided";
	EXPECT_FALSE( vis[ 7 ] )  << "#ifndef TYPEDEF_SURVEY is a guarded run";
	EXPECT_FALSE( vis[ 9 ] )  << "a neutral conditional nested inside a guarded "
	                             "run does not un-hide it";
	EXPECT_TRUE( vis[ 13 ] )  << "a neutral conditional's first arm";
	EXPECT_TRUE( vis[ 15 ] )  << "a neutral conditional's #else";
	EXPECT_TRUE( vis[ 17 ] )  << "code after everything closes";
}

// IsMemberDefinition must separate a member definition from the macros that
// merely mention the same scope. Both shapes are in both headers.
TEST( SurveyStubElisionScanner, AMacroMentioningTheScopeIsNotAMemberDefinition )
{
	const Side &client = kSides[ 0 ];
	// The server SPELLING, as a fixture. kSides lost its server row in P6 (its
	// header left the farm), but this test is about the scanner, not that file,
	// and IsMemberDefinition reads only the two scope fields.
	Side server = Side();
	server.pszScope1 = "CBaseEntity::";
	server.pszScope2 = NULL;

	EXPECT_TRUE( IsMemberDefinition(
		"inline int C_BaseEntity::GetModelIndex( void ) const", client ) );
	EXPECT_TRUE( IsMemberDefinition(
		"inline float CBaseEntity::BoundingRadius() const", client ) )
		<< "the client writes a handful of its own members with the SERVER "
		   "spelling; a one-spelling scanner misses them";
	EXPECT_TRUE( IsMemberDefinition(
		"inline CBaseEntity *CBaseEntity::GetMoveParent( void )", server ) );

	EXPECT_FALSE( IsMemberDefinition(
		"#define SetThink( a ) ThinkSet( static_cast <void (CBaseEntity::*)(void)> (a), 0, NULL )",
		server ) )
		<< "SetThink is a macro that survives arming. A scanner that filed it "
		   "as a member definition would demand it be elided, which is the "
		   "over-count this file exists to prevent.";

	// THE FIXTURE THAT EARNS THE `inline` REQUIREMENT, and the reason it is
	// this line rather than the macro above: a `#define` puts its own name
	// before the first paren, so the scope never reaches the head and the
	// macro is rejected for a reason that has nothing to do with `inline`.
	// c_baseentity.h:166 is the shape that does reach it -- a CALL, fully
	// qualified, sitting in a macro continuation ABOVE the stub, and therefore
	// visible when armed. Drop the `inline` requirement and the sweep reports
	// it as a member definition that leaked.
	EXPECT_FALSE( IsMemberDefinition(
		"C_BaseEntity::CreatePredictedEntityByName( className, __FILE__, __LINE__ );",
		client ) )
		<< "a fully qualified CALL is not a definition; if this is filed as "
		   "one, NoMemberDefinitionSurvivesArming reports a leak that is not "
		   "there and the boundary looks broken when it is not.";
	EXPECT_FALSE( IsMemberDefinition(
		"inline C_BaseEntity *SDKEntityFromUnknown( IClientUnknown *pUnknown )",
		client ) )
		<< "the funnel names the entity as a RETURN TYPE, not as a scope";
	EXPECT_FALSE( IsMemberDefinition( "inline bool FClassnameIs( C_BaseEntity *p, const char *s )",
		client ) );
}

// The client writes `C_BaseEntity::` literally. A scanner carrying only the
// server's short spelling does not find nothing — it silently matches the
// wrong thing, or fails to match the right one. Pinned so the two spellings
// can never be assumed interchangeable.
TEST( SurveyStubElisionScanner, TheTwoEntitySpellingsAreDistinct )
{
	const std::string strClient = "inline int C_BaseEntity::GetModelIndex( void ) const";

	EXPECT_TRUE( Has( strClient, "C_BaseEntity::" ) );
	EXPECT_FALSE( Has( strClient, "CBaseEntity::" ) )
		<< "the server spelling must NOT match the client definition; if it "
		   "does, a shared scanner files client members under the wrong scope.";
}

// That the headers were really opened and really read — without this, a test
// that skipped or read an empty file would pass every claim above vacuously.
//
// THE TWO FLOORS MOVED AND THE ANCHOR CHANGED. This used to require >1000 lines
// and the real class head; the class is deleted and both headers are a third of
// what they were (372 and 632 lines). Quoting the old numbers would have made
// this a check that cannot PASS, which is no better than one that cannot fail.
// The anchor is now the include guard plus the first surviving free name --
// text that is in these files and in no others.
TEST( SurveyStubElisionScanner, TheHeadersWereReallyOpened )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	for ( size_t s = 0; s < sizeof( kSides ) / sizeof( kSides[ 0 ] ); s++ )
	{
		const Side &side = kSides[ s ];
		const std::vector< std::string > lines = ReadStripped( side.pszHeader );
		ASSERT_FALSE( lines.empty() )
			<< side.pszHeader << " is missing or unreadable";

		EXPECT_GE( CountNonBlankLines( lines ), side.nMinLines )
			<< side.pszHeader << " read as only " << CountNonBlankLines( lines )
			<< " non-blank lines of code. The entity class is gone from it, but its tail is not: "
			   "this is not that file.";
		EXPECT_GE( CountCodeLines( lines ), side.nMinCodeLines )
			<< side.pszHeader << " read as only " << CountCodeLines( lines )
			<< " code lines.";

		EXPECT_EQ( side.pszIncludeGuard, FirstConditional( lines ) )
			<< side.pszHeader << " does not open with its own include guard.";

		bool bSurvivor = false;
		for ( size_t i = 0; i < lines.size() && !bSurvivor; i++ )
			bSurvivor = Has( lines[ i ], side.pszSurvivor[ 0 ] );
		EXPECT_TRUE( bSurvivor )
			<< side.pszHeader << " does not name " << side.pszSurvivor[ 0 ]
			<< "; the read landed on the wrong file.";
	}
}
