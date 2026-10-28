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

std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
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

struct StubSpan
{
	size_t iIfdef;
	size_t iElse;
	size_t iEndif;
	bool   bFound;
};

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
//-----------------------------------------------------------------------------
// Scanner self-tests — the half that stops the claims above from being
// unfalsifiable. Every one of these was watched failing before being kept.
//-----------------------------------------------------------------------------

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

