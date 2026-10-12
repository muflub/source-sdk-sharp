//========= SourceSharp — step 3g, the rules callers ==========================//
//
// plan_entity.md Phase 3, paragraph 3g. Two things landed and this file is the
// gate on both.
//
//  1. CAST FAMILY 5 LEFT shell_gamerules.cpp (§3.16, "Downcasts inside the
//     hierarchy"). The three sites the plan names at shell_gamerules.cpp:1058-1067
//     were the whole of the file's ToBase* column in the census, and they were
//     all inside an `if` whose entire body was `#ifndef SOURCESHARP` — a
//     condition that ran on every victim of every explosion and then executed
//     an empty compound statement. §3.16 says the cast goes with its caller;
//     the caller here had already gone, so both did.
//
//  2. THE COLLISION-RULE BRIDGE IS DELETED (requests/3a.md §4). 3a stopped
//     CCollisionEvent::ShouldCollide calling g_pGameRules->ShouldCollide per
//     pair per step, and left CPhysicsHook::LevelInitPreEntity copying the
//     group matrix out of the rules object once per level until the
//     RegisterCollisionRules slot was retargeted. It is retargeted:
//     SourceSharp_RegisterCollisionRules writes the rows into the shell at the
//     moment C# states them. The bridge is gone, and 3a's own words are the
//     standard it must be gone by — "it must never become a per-pair fallback".
//
// A SOURCE TEST, and for the reason the neighbouring source tests give: what
// these facts check is a SHAPE. A re-added downcast compiles perfectly, and a
// re-added ask from inside the evaluator gives the right answer and costs a
// crossing per pair per step. Neither is visible to a test that only checks
// what the code computes.
//
// AND THE RAW-TEXT FACTS ARE RAW ON PURPOSE. tools/entity-census counts
// SPELLINGS over the compiled TU list and does NOT strip comments, so a comment
// that quotes the deleted call expressions leaves the census reading sites that
// are not there — the gate staying green across the deletion it exists to
// notice. Two facts below, one stripped and one raw, so that shape cannot come
// back as prose.
//
//============================================================================//

#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static std::string s_Root;
	if ( !s_Root.empty() )
		return s_Root.c_str();

	std::string prefix;
	for ( int i = 0; i < 8; i++ )
	{
		std::ifstream probe( ( prefix + "src/sourcesharp/game/shared/shell_gamerules.cpp" ).c_str() );
		if ( probe.good() )
		{
			s_Root = prefix + "src";
			return s_Root.c_str();
		}
		prefix += "../";
	}
	return NULL;
}

std::string ReadRaw( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + pszRel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

//-----------------------------------------------------------------------------
// The §3.16 family-5 helper names, as tools/entity-census-patterns spells them.
// A call site is the name followed by optional whitespace and an open paren,
// which is the census regex written out by hand because this test links no
// regex over the pattern file — TheNeedlesAreTheCensusFamily below checks the
// two lists against each other so a widened family is a red test rather than a
// hole.
//-----------------------------------------------------------------------------
const char *kToBaseNames[] =
{
	"ToBasePlayer",
	"ToBaseCombatCharacter",
	"ToBaseAnimating",
	"ToBaseCombatWeapon",
	"ToBaseFlex",
};
const size_t kToBaseCount = sizeof( kToBaseNames ) / sizeof( kToBaseNames[ 0 ] );

// "<name>" followed by optional whitespace and '(' — the call, not a mention.
int CountCallSites( const std::string &text, const char *pszName )
{
	const std::string name( pszName );
	int n = 0;
	for ( size_t p = text.find( name ); p != std::string::npos;
	      p = text.find( name, p + name.size() ) )
	{
		// Not a call if the name is a suffix of a longer identifier.
		if ( p > 0 )
		{
			const char c = text[ p - 1 ];
			if ( ( c >= 'A' && c <= 'Z' ) || ( c >= 'a' && c <= 'z' ) ||
			     ( c >= '0' && c <= '9' ) || c == '_' )
				continue;
		}
		size_t q = p + name.size();
		while ( q < text.size() && ( text[ q ] == ' ' || text[ q ] == '\t' ) )
			q++;
		if ( q < text.size() && text[ q ] == '(' )
			n++;
	}
	return n;
}

int CountEveryToBaseCallSite( const std::string &text, std::string *pWhich )
{
	int n = 0;
	for ( size_t i = 0; i < kToBaseCount; i++ )
	{
		const int k = CountCallSites( text, kToBaseNames[ i ] );
		if ( k > 0 && pWhich != NULL )
		{
			if ( !pWhich->empty() ) *pWhich += ", ";
			*pWhich += kToBaseNames[ i ];
		}
		n += k;
	}
	return n;
}

//-----------------------------------------------------------------------------
// The body of a function, from its opening brace to the matching close.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &text, const char *pszSignature )
{
	const size_t at = text.find( pszSignature );
	EXPECT_NE( std::string::npos, at ) << "cannot find " << pszSignature;
	if ( at == std::string::npos )
		return std::string();

	const size_t open = text.find( '{', at );
	EXPECT_NE( std::string::npos, open );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < text.size(); i++ )
	{
		if ( text[ i ] == '{' ) depth++;
		else if ( text[ i ] == '}' )
		{
			depth--;
			if ( depth == 0 )
				return text.substr( open, i - open + 1 );
		}
	}
	ADD_FAILURE() << "unbalanced braces after " << pszSignature;
	return std::string();
}

bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// WHAT THIS BUILD ACTUALLY COMPILES, as far as the SOURCESHARP switch decides
// it. Drops every `#else` arm of an `#ifdef SOURCESHARP`, and every arm of an
// `#ifndef SOURCESHARP`, counting nesting properly so an inner `#if _DEBUG`
// cannot be mistaken for the arm boundary.
//
// Needed because physics.cpp keeps stock's ninety-line ShouldCollide body below
// the arm 3a replaced (requests/3a.md §3 says why, and says the file leaves with
// both arms at the Phase 3 exit). A test that searched the raw text would be
// reading code this build does not compile, and would either fail for the wrong
// reason or -- worse -- be satisfied by the stock arm and miss an armed
// regression.
//-----------------------------------------------------------------------------
std::string ArmedOnly( const std::string &code )
{
	std::vector< bool > keepStack;   // is the arm we are currently inside kept?
	std::vector< int >  kindStack;   // 0 = not a SOURCESHARP switch, 1 = ifdef, 2 = ifndef
	std::string out;

	size_t i = 0;
	while ( i < code.size() )
	{
		size_t eol = code.find( '\n', i );
		if ( eol == std::string::npos ) eol = code.size();
		const std::string line = code.substr( i, eol - i );

		// Leading whitespace then '#'.
		size_t h = 0;
		while ( h < line.size() && ( line[ h ] == ' ' || line[ h ] == '\t' ) ) h++;
		const bool bDirective = ( h < line.size() && line[ h ] == '#' );
		const std::string d = bDirective ? line.substr( h ) : std::string();

		bool bKeepLine = true;
		for ( size_t k = 0; k < keepStack.size(); k++ )
			if ( !keepStack[ k ] ) bKeepLine = false;

		if ( bDirective && ( d.compare( 0, 6, "#ifdef" ) == 0 ||
		                     d.compare( 0, 7, "#ifndef" ) == 0 ||
		                     d.compare( 0, 3, "#if" ) == 0 ) )
		{
			int kind = 0;
			bool keep = true;
			if ( d.compare( 0, 6, "#ifdef" ) == 0 && Has( d, "SOURCESHARP" ) &&
			     !Has( d, "SS_CBASEENTITY_ALIAS" ) )
				kind = 1;
			else if ( d.compare( 0, 7, "#ifndef" ) == 0 && Has( d, "SOURCESHARP" ) &&
			          !Has( d, "SS_CBASEENTITY_ALIAS" ) )
			{
				kind = 2;
				keep = false;
			}
			kindStack.push_back( kind );
			keepStack.push_back( keep );
			bKeepLine = false;
		}
		else if ( bDirective && d.compare( 0, 5, "#else" ) == 0 && !keepStack.empty() )
		{
			if ( kindStack.back() == 1 )      keepStack.back() = false;
			else if ( kindStack.back() == 2 ) keepStack.back() = true;
			bKeepLine = false;
		}
		else if ( bDirective && d.compare( 0, 6, "#endif" ) == 0 && !keepStack.empty() )
		{
			keepStack.pop_back();
			kindStack.pop_back();
			bKeepLine = false;
		}

		if ( bKeepLine )
			out += line;
		out += '\n';

		i = eol + 1;
	}
	return out;
}

const char *kRulesCpp   = "sourcesharp/game/shared/shell_gamerules.cpp";
const char *kPhysicsCpp = "sourcesharp/game/server/physics.cpp";
const char *kPatterns   = "../tools/entity-census-patterns";

} // namespace


//=============================================================================//
// 1. CAST FAMILY 5 LEFT THE RULES SHIM
//=============================================================================//

//-----------------------------------------------------------------------------
// The code holds no downcast to a player, a combat character or an animating
// entity. This is the fact plan_entity.md's 3g gate names.
//-----------------------------------------------------------------------------
TEST( RulesCallers, NoDowncastToABaseTypeSurvivesInTheRulesShim )
{
	const std::string code = StripComments( ReadRaw( kRulesCpp ) );
	ASSERT_FALSE( code.empty() ) << "cannot read " << kRulesCpp;

	std::string which;
	const int n = CountEveryToBaseCallSite( code, &which );

	EXPECT_EQ( 0, n )
		<< kRulesCpp << " calls " << which << ". §3.16 family 5 says the shell "
		<< "is final and there is no subtype to cast to: 'is a player' is a flag "
		<< "in the engine block and the concrete pointer comes from the slot "
		<< "table. A new one here is a hole in the 3g gate.";
}

//-----------------------------------------------------------------------------
// AND NONE IN THE PROSE. tools/entity-census does not strip comments, so a
// comment that quotes the deleted call expressions puts the sites back into the
// census after the code was deleted. The comment left in place of the hook says
// so and describes the two casts instead of spelling them; this is that promise
// as a fact.
//
// It is a SEPARATE fact from the one above deliberately: the one above is what
// the port cares about, this one is what the INSTRUMENT reads, and a run where
// they disagree is exactly the failure mode this file's header describes.
//-----------------------------------------------------------------------------
TEST( RulesCallers, NoDowncastSurvivesInTheProseEither )
{
	const std::string raw = ReadRaw( kRulesCpp );
	ASSERT_FALSE( raw.empty() ) << "cannot read " << kRulesCpp;

	std::string which;
	const int n = CountEveryToBaseCallSite( raw, &which );

	EXPECT_EQ( 0, n )
		<< kRulesCpp << " SPELLS " << which << " somewhere the compiler ignores "
		<< "and tools/entity-census does not. The census counts spellings over "
		<< "raw text, so this file would report that many ToBase sites with none "
		<< "of them in the code. Describe the cast; do not quote the call.";
}

//-----------------------------------------------------------------------------
// The needles above ARE §3.16's family. tools/entity-census-patterns is the one
// list the gate is as wide as; if a sixth helper is added there and not here,
// this test would keep passing over a spelling the census counts.
//-----------------------------------------------------------------------------
TEST( RulesCallers, TheNeedlesAreTheCensusFamily )
{
	const std::string patterns = ReadRaw( kPatterns );
	ASSERT_FALSE( patterns.empty() ) << "cannot read tools/entity-census-patterns";

	const size_t at = patterns.find( "ToBaseX\t" );
	ASSERT_NE( std::string::npos, at ) << "the census no longer has a ToBaseX family";

	const size_t eol = patterns.find( '\n', at );
	const std::string line = patterns.substr( at, eol - at );

	for ( size_t i = 0; i < kToBaseCount; i++ )
	{
		// The regex spells the family as To(BasePlayer|BaseCombatCharacter|...),
		// so the shared "To" is factored out. Look for the tail.
		const std::string tail = std::string( kToBaseNames[ i ] ).substr( 2 );
		EXPECT_TRUE( Has( line, tail.c_str() ) )
			<< kToBaseNames[ i ] << " is checked here but is not in the census "
			<< "family any more: " << line;
	}

	// And the other way: every '|'-separated alternative in the regex is one of
	// this test's needles. A widened family with no matching needle here is a
	// spelling the census counts and this file does not.
	const size_t open = line.find( "To(" );
	ASSERT_NE( std::string::npos, open ) << "unexpected ToBaseX regex shape: " << line;
	const size_t close = line.find( ')', open );
	ASSERT_NE( std::string::npos, close );

	std::string alts = line.substr( open + 3, close - open - 3 );
	alts += "|";
	std::string one;
	int nSeen = 0;
	for ( size_t i = 0; i < alts.size(); i++ )
	{
		if ( alts[ i ] != '|' ) { one += alts[ i ]; continue; }

		bool bKnown = false;
		for ( size_t k = 0; k < kToBaseCount; k++ )
			if ( std::string( "To" ) + one == kToBaseNames[ k ] )
				bKnown = true;

		EXPECT_TRUE( bKnown )
			<< "the census counts To" << one << " and this test does not look "
			<< "for it. Add it to kToBaseNames.";
		nSeen++;
		one.clear();
	}
	EXPECT_EQ( (int)kToBaseCount, nSeen )
		<< "the census family and this test's needle list are different sizes";
}


//=============================================================================//
// 2. THE COLLISION-RULE BRIDGE IS DELETED, AND THE SLOT IS RETARGETED
//=============================================================================//

//-----------------------------------------------------------------------------
// requests/3a.md §4's block is gone from CPhysicsHook::LevelInitPreEntity.
//-----------------------------------------------------------------------------
TEST( RulesCallers, TheLevelInitSeedIsGone )
{
	const std::string code = StripComments( ReadRaw( kPhysicsCpp ) );
	ASSERT_FALSE( code.empty() ) << "cannot read " << kPhysicsCpp;

	const std::string body = BodyOf( code, "void CPhysicsHook::LevelInitPreEntity()" );
	ASSERT_FALSE( body.empty() );

	EXPECT_FALSE( Has( body, "g_pGameRules->ShouldCollide" ) )
		<< "LevelInitPreEntity still copies the group matrix out of the game "
		<< "rules object. 3a left that as a bridge for 3g to delete; the rows "
		<< "are written by SourceSharp_RegisterCollisionRules now.";

	EXPECT_FALSE( Has( body, "PhysicsShell_SetCollisionRules" ) )
		<< "LevelInitPreEntity still writes the shell's rule table. The table is "
		<< "stated once by managed code, not seeded per level.";
}

//-----------------------------------------------------------------------------
// AND NOWHERE ELSE IN THE FILE. The bridge's failure mode was never "it stays
// in LevelInitPreEntity" — it was "it moves to the evaluation path", where it
// gives the same answer and costs a virtual call per pair per step. §7 decides
// there is no path from ShouldCollide back out to anything.
//-----------------------------------------------------------------------------
TEST( RulesCallers, NothingInPhysicsCppAsksTheRulesWhetherTwoGroupsCollide )
{
	const std::string armed = ArmedOnly( StripComments( ReadRaw( kPhysicsCpp ) ) );
	ASSERT_FALSE( armed.empty() ) << "cannot read " << kPhysicsCpp;

	EXPECT_FALSE( Has( armed, "g_pGameRules->ShouldCollide" ) )
		<< kPhysicsCpp << " asks the game rules whether two collision groups "
		<< "collide on a path this build compiles. 3a moved that question into "
		<< "the shell's own table so it is a memory read; an ask here is the "
		<< "per-pair fallback §7 refuses.";
}

//-----------------------------------------------------------------------------
// THE CHECK-THAT-CANNOT-FAIL GUARD FOR THE FACT ABOVE -- REWRITTEN BY THE
// `residue` WAVE (74b071771..a46e8d645), AND RENAMED BECAUSE IT NOW ASSERTS
// THE OPPOSITE OF WHAT IT USED TO.
//
// This was `TheStockArmStillHasTheCallTheArmedArmMustNot`, and it asserted that
// game/server/physics.cpp still contained `g_pGameRules->ShouldCollide` in SOME
// arm -- so that NothingInPhysicsCppAsksTheRulesWhetherTwoGroupsCollide was a
// measurement rather than a search that finds nothing. Its own failure message
// said "delete both facts together when stock's arm goes".
//
// Stock's arm has now gone. At 74b071771 the call was physics.cpp:1890, inside
// the `#else` of the `#ifdef SOURCESHARP` that opens CCollisionEvent::
// ShouldCollide_2 (physics.cpp:1598 `#ifdef SOURCESHARP`, :1682 `#else`) --
// that is, inside the NOT-SOURCESHARP arm, which this build has never compiled;
// requests/3a.md §3 kept it only so the two arms could be read side by side.
// The wave deleted the whole stock arm.
//
// BOTH FACTS ARE KEPT, not deleted, because the guard's JOB survives its
// subject: something still has to prove that the absence above is a real search.
// What has changed is where the proof comes from. It used to come from the
// stock arm; the stock arm cannot supply it any more, so it comes from a
// HAND-WRITTEN FIXTURE, which is this file's standing method for an absence
// with no live positive. Three things are asserted, and they are deliberately
// not all in the same direction:
//
//   ABSENCE  -- the call really is gone from every arm, which is the successor
//               property and a STRONGER statement than the one this fact used
//               to make (residue brief rule 4);
//   PRESENCE -- physics.cpp is still the right haystack: ShouldCollide_2 is
//               still there and its armed body still evaluates the table, so
//               the absence is not being read off a file that lost the
//               function;
//   PRESENCE -- ArmedOnly still strips a stock arm and still keeps an armed
//               one, proved on a fixture that contains the needle in both
//               shapes of the switch. Without this, ArmedOnly could start
//               returning the empty string and every armed-arm fact in this
//               file would pass.
//-----------------------------------------------------------------------------
TEST( RulesCallers, TheStockArmIsGoneAndTheArmedAbsenceRestsOnAFixtureNow )
{
	const std::string all = StripComments( ReadRaw( kPhysicsCpp ) );
	ASSERT_FALSE( all.empty() ) << "cannot read " << kPhysicsCpp;

	// ABSENCE. Note this is over the WHOLE file and not just the armed arms:
	// the point is that there is no arm left to read it out of. Comment-stripped,
	// because physics.cpp's surviving prose at :1458 says "asked of the shell's
	// table rather than of g_pGameRules->ShouldCollide" -- the raw file still
	// spells the needle, in exactly the way this project's stripper exists for.
	EXPECT_FALSE( Has( all, "g_pGameRules->ShouldCollide" ) )
		<< kPhysicsCpp << " asks the game rules whether two collision groups "
		<< "collide again, in some arm. The rule table is stated once by managed "
		<< "code and evaluated over the engine block; an ask here is the "
		<< "per-pair fallback plan_entity.md §7 refuses.";

	// PRESENCE, the haystack. ShouldCollide_2 left with native's collision
	// handler (plan_physics_bones_native_zero 2.1: managed's PhysicsSolver
	// answers vphysics, over a group matrix stated once), and the report's
	// evaluation against the world left with the report (2.3: managed's
	// PhysicsSolver.AgainstTheWorld). physics.cpp evaluates no rule at all
	// now; it is still the file, because it still answers which object is
	// the world's.
	EXPECT_FALSE( Has( all, "PhysicsShell().ShouldCollide(" ) )
		<< kPhysicsCpp << " evaluates the native rule table again; the rules are managed's";
	const std::string body = BodyOf( all, "CWorld *PhysicsWorldOwner( const IPhysicsObject *pObject )" );
	ASSERT_FALSE( body.empty() )
		<< "PhysicsWorldOwner is not in " << kPhysicsCpp
		<< " under that signature any more, so the absence above is about a file "
		<< "that no longer contains the subject and cannot fail";
	EXPECT_TRUE( Has( body, "g_PhysWorldObject" ) )
		<< "PhysicsWorldOwner no longer answers by identity, so this section is "
		<< "about the wrong code.";

	// PRESENCE, the instrument. A fixture rather than the tree, because the
	// tree no longer has a stock arm anywhere in this file to exercise it with.
	// Both shapes of the switch, because ArmedOnly treats them separately:
	// `#ifdef SOURCESHARP` keeps the first arm, `#ifndef SOURCESHARP` drops it.
	static const char *const kSwitchFixture =
		"#ifdef SOURCESHARP // armed arm\n"
		"\tconst int nAnswer = PhysicsShell().ShouldCollide( side0, side1, facts, &nReason );\n"
		"#else // stock arm\n"
		"\tif ( !g_pGameRules->ShouldCollide( owner0.GetCollisionGroup(), owner1.GetCollisionGroup() ) )\n"
		"\t\treturn 0;\n"
		"#endif\n"
		"#ifndef SOURCESHARP\n"
		"\tif ( !g_pGameRules->ShouldCollide( 1, 2 ) )\n"
		"\t\treturn 0;\n"
		"#endif\n";

	const std::string strRawFixture( kSwitchFixture );
	const std::string strArmedFixture = ArmedOnly( strRawFixture );

	EXPECT_TRUE( Has( strRawFixture, "g_pGameRules->ShouldCollide" ) )
		<< "the fixture itself has stopped containing the needle, so the strip "
		<< "below has nothing to remove and proves nothing";
	EXPECT_FALSE( Has( strArmedFixture, "g_pGameRules->ShouldCollide" ) )
		<< "ArmedOnly no longer drops the stock arm of a SOURCESHARP switch. "
		<< "NothingInPhysicsCppAsksTheRulesWhetherTwoGroupsCollide reads its "
		<< "output, so with this broken that fact passes on text this build "
		<< "does not compile.";
	EXPECT_TRUE( Has( strArmedFixture, "PhysicsShell().ShouldCollide( side0, side1, facts, &nReason )" ) )
		<< "ArmedOnly is dropping the ARMED arm too -- or returning nothing at "
		<< "all -- which makes every armed-arm fact in this file vacuous";
}

//-----------------------------------------------------------------------------
// THE SHELL'S COPY IS GONE, AND THE GAME RULES' IS ALL THE SLOT WRITES.
//
// Four facts stood here about the registration's push into physics_shell.h's
// rule table: that it wrote the table, read-modify-wrote it so the nine
// clauses beyond the rows survived, tied the two row arrays at compile time,
// and did it server-only. plan_physics_bones_native_zero 2.3 deleted the
// table -- its last reader was the SS-PHYSWORLD report, which is managed's,
// and the rules vphysics asks are managed's PhysicsSolverRules over the matrix
// CollisionRules builds. So the successor is the absence of every one of
// those writes, beside the presence of the one write that is left: the rows
// the trace filters' ShouldCollide reads.
//-----------------------------------------------------------------------------
TEST( RulesCallers, TheRegistrationWritesOnlyTheGameRulesMatrix )
{
	const std::string raw = ReadRaw( kRulesCpp );
	ASSERT_FALSE( raw.empty() ) << "cannot read " << kRulesCpp;
	const std::string code = StripComments( raw );

	const std::string body = BodyOf(
		code, "void SourceSharp_RegisterCollisionRules( const SourceSharpCollisionRules *pRules )" );
	ASSERT_FALSE( body.empty() );

	// PRESENCE: the matrix still lands where CGameRules::ShouldCollide reads it.
	EXPECT_TRUE( Has( body, "s_CollisionRules = *pRules;" ) )
		<< "the registration no longer stores the matrix the trace filters ask";

	// ABSENCE: no shell table to write, read or include.
	EXPECT_FALSE( Has( body, "PhysicsShell_SetCollisionRules" ) );
	EXPECT_FALSE( Has( body, "PhysicsShell().Rules()" ) );
	EXPECT_FALSE( Has( body, "physRules" ) );
	EXPECT_FALSE( Has( code, "#include \"physics_shell.h\"" ) )
		<< kRulesCpp << " includes the physics shell again; nothing in it is this file's";
}
