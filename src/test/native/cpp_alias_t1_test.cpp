//========= SourceSharp — lane T shard T1's .cpp arms, read as source ========//
//
// A SOURCE SCANNER, like cpp_alias_lane1_test and cpp_alias_shard2_test, and
// for the same reason those give: the claim is about the TEXT of files that
// need a whole engine to run, so the text is what is read.
//
// WHAT T1 DID. Route step 5's `.cpp` half, shard T1: 12 files carrying 208
// macro-resolved armed sites, 195 of them closed here. Unlike shard 2 — whose
// 846 eviction sites made an armed arm "a pure stub" — this shard is retype and
// CALLER-root work, so most of it IS `#ifndef SS_CBASEENTITY_ALIAS` arms, plus
// a number of unconditional retypes where the answer that survives the flip is
// also the right answer today.
//
// THE COMPILE-SIDE EVIDENCE is src/survey_run with the alias armed, which
// builds every TU with -DSS_CBASEENTITY_ALIAS; an armed arm that does not
// compile shows up there as a site, and this shard's 12 files measure 13 sites
// against a baseline of 208. That is a measurement run by hand, not a standing
// test, which is exactly why the invariants below are worth pinning: they are
// the half that can rot between surveys, and NOTHING in the ordinary build
// compiles an armed arm at all.
//
// WHAT ROT LOOKS LIKE HERE, which is what each test below is aimed at:
//
//   1. AN ARMED ARM IS "SIMPLIFIED" BACK. Somebody reads
//      `EntityPhysicsObject( pEntity )` and restores `pEntity->
//      VPhysicsGetObject()`, which is what every other SDK file still says and
//      so is the natural thing to copy. It compiles, the suite stays green, and
//      the site is back.
//   2. THE MOVE-TYPE INVERSION. EntityIsVPhysicsSimulated's armed arm asks
//      whether the solver has an object. Deriving it from the move-type neutral
//      instead — MOVETYPE_NONE, therefore "not simulated" — compiles, reads
//      naturally, and inverts the answer for EVERY entity on the parallel
//      hierarchy, which armed is the whole population. physicsowner.h gave that
//      question its own name to stop this and the reason deserves a guard.
//   3. A NULL TEST REWRITTEN INTO A WORLD TEST. This hierarchy's contract warns
//      about it by name: the world is CWorld, itself a CBaseEntity, so it
//      answers NON-NULL, and "never fix a NULL check into a world check".
//      CConstraintFloodList::IsWorldEntity's armed arm must stay a plain false.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "ss_ci_skip.h"

#include <dirent.h>

#include <fstream>
#include <iterator>
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
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		const std::string probe = std::string( kCandidates[ i ] ) + "/game/server";
		DIR *pDir = opendir( probe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and here it is load-bearing rather
// than tidiness: every arm this file speaks for carries a comment NAMING the
// member it stopped calling. "pEntity->VPhysicsGetObject()" appears in
// physics.cpp today inside the sentence explaining that it is gone, and
// "GetMoveType() != MOVETYPE_VPHYSICS -> true" appears inside the sentence
// explaining that it would be wrong. A scanner matching raw text would find
// both and report a correct fix as broken.
//
// header_alias_test.cpp and cpp_alias_lane1_test.cpp were each caught by
// exactly this, which is why the stripping is copied rather than reinvented.
//-----------------------------------------------------------------------------
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
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string CodeOf( const std::string &strRelative )
{
	return StripComments( ReadFile( strRelative ) );
}

int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	if ( strNeedle.empty() )
		return 0;
	int n = 0;
	for ( size_t p = strHay.find( strNeedle ); p != std::string::npos;
	      p = strHay.find( strNeedle, p + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

bool Has( const std::string &strHay, const std::string &strNeedle )
{
	return strHay.find( strNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// A preprocessor DIRECTIVE, not a substring of one.
//
// This exists because the first mutation run caught this file out. Counting
// "#endif" with CountOf also matches "#endifSOMETHING", so a mutation that
// RENAMED an #endif -- which is how a directive most plausibly stops being one
// -- left the count unchanged and the guard test green. That is the "needle
// located by substring where the mutation appends" shape this port has recorded
// before, and it is the reason a directive count needs its own function.
//
// A match requires the next character to be absent or non-identifier, so
// `#endif` and `#endif // note` count and `#endif_EATEN` does not.
//-----------------------------------------------------------------------------
bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
	    || ( c >= '0' && c <= '9' ) || c == '_';
}

int CountDirective( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t p = strHay.find( strNeedle ); p != std::string::npos;
	      p = strHay.find( strNeedle, p + strNeedle.size() ) )
	{
		const size_t after = p + strNeedle.size();
		if ( after >= strHay.size() || !IsIdentChar( strHay[ after ] ) )
			n++;
	}
	return n;
}

//-----------------------------------------------------------------------------
// The body of one function, by brace matching from the first `{` after a
// signature anchor. Returns empty when the anchor is not there, which the
// assertions below read as a FAILURE rather than a vacuous pass: an empty body
// contains none of the strings they require.
//-----------------------------------------------------------------------------
std::string BodyAfter( const std::string &strCode, const std::string &strAnchor )
{
	const size_t a = strCode.find( strAnchor );
	if ( a == std::string::npos )
		return std::string();

	const size_t open = strCode.find( '{', a );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < strCode.size(); i++ )
	{
		if ( strCode[ i ] == '{' )
			nDepth++;
		else if ( strCode[ i ] == '}' )
		{
			nDepth--;
			if ( nDepth == 0 )
				return strCode.substr( open, i - open + 1 );
		}
	}
	return std::string();
}

//-----------------------------------------------------------------------------
// Every conditional this shard opens must be closed, counted over the whole
// file rather than per arm.
//
// `#ifndef SS_CBASEENTITY_ALIAS`, `#ifdef SS_CBASEENTITY_ALIAS` and the two
// `#if !defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )` forms in
// physics_shared.cpp all open one conditional each, and each needs one `#endif`
// and at most one `#else`. This counts the opens and requires that many
// `#endif`s to follow the FIRST open — which is what a swallowed `#endif`
// breaks and what a swallowed `#else` leaves alone, so both tests are here.
//-----------------------------------------------------------------------------
int CountAliasOpens( const std::string &strCode )
{
	return CountDirective( strCode, "#ifndef SS_CBASEENTITY_ALIAS" )
	     + CountDirective( strCode, "#ifdef SS_CBASEENTITY_ALIAS" )
	     + CountOf( strCode, "!defined( SS_CBASEENTITY_ALIAS )" );
}

} // namespace


//=============================================================================//
// 1. The guards are structurally whole.
//=============================================================================//

//-----------------------------------------------------------------------------
// SERVERNETWORKPROPERTY.CPP IS GONE (P5 lane E), and this fact used to pin the
// answers its eight alias arms chose. Those answers were the neutrals a NULL
// outer gave: GetNetworkParent `return NULL;`, GetEntityHandle NULL, and so on.
// server.so never constructed the class (only CBaseEntity owned one), so the
// file went whole, together with shell_gameents.cpp's transmit walk, which
// cast other objects' networkables to it.
//
// The fact is now that it stays gone: no farm copy of the file, and no live
// line of the transmit step names the class. The positive half proves the
// directory was read. shell_gameents.cpp loads, and its always-transmit arm
// marks the edict itself and nothing else.
//-----------------------------------------------------------------------------
TEST( T1Guards, ServerNetworkPropertyIsGoneAndTheTransmitStepNamesItNowhere )
{
	const std::string ents = CodeOf( "sourcesharp/game/server/shell_gameents.cpp" );
	ASSERT_FALSE( ents.empty() ) << "shell_gameents.cpp did not load; the absence below would mean nothing";
	ASSERT_TRUE( Has( ents, "CServerGameEnts::CheckTransmit" ) );

	EXPECT_TRUE( ReadFile( "sourcesharp/game/server/ServerNetworkProperty.cpp" ).empty() )
		<< "ServerNetworkProperty.cpp is back in the server sources";
	EXPECT_EQ( 0, CountOf( ents, "CServerNetworkProperty" ) )
		<< "the transmit step names CServerNetworkProperty again; nothing in "
		   "server.so is one, so a cast to it reads another object's networkable";
	EXPECT_EQ( 0, CountOf( ents, "GetNetworkParent" ) );
	EXPECT_EQ( 1, CountOf( ents, "pInfo->m_pTransmitEdict->Set( iEdict );\n\t\t\tcontinue;" ) );
}


//-----------------------------------------------------------------------------
// The two physics.cpp files carry the bulk of the arms. A floor rather than an
// exact count, because adding a correct arm is a normal thing to do and this
// test must not punish it; what it catches is a collapse.
//-----------------------------------------------------------------------------
TEST( T1Guards, TheTwoPhysicsFilesStillCarryTheirArms )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	const std::string server = CodeOf( "sourcesharp/game/server/physics.cpp" );
	const std::string client = CodeOf( "game/client/physics.cpp" );

	ASSERT_FALSE( server.empty() );
	ASSERT_FALSE( client.empty() );

	// NEITHER FILE CARRIES AN ALIAS ARM ANY MORE, and the name of this test is
	// now the history of what it checks rather than a description of it. The
	// `residue` wave (74b071771..a46e8d645) deleted every one: twenty-one
	// regions in game/server/physics.cpp and eight in game/client/physics.cpp,
	// all of them `#ifndef SS_CBASEENTITY_ALIAS`. The pre-wave tip has them —
	// game/client/physics.cpp:356, :385, :411, :443, :526, :697, :1384, :1450
	// at 74b071771, for the client half.
	//
	// THEY WERE NEVER COMPILED. server_csharp.vpc and client_csharp.vpc build
	// these two TUs with SS_CBASEENTITY_ALIAS and SOURCESHARP both defined, and
	// that is the only configuration this project builds, so the `#ifndef` arm
	// is the side the preprocessor discards before the compiler sees it. What
	// the wave removed is text; what it left is the arm that was being
	// compiled all along. 588 objects before and after, identical DEFINED and
	// UNDEFINED symbol sets, 0 differing.
	//
	// SO THE SUCCESSOR FACT IS A PAIR, and it has to be a pair. "There are no
	// arms" alone is what an EMPTY FILE says too, and that is the rewrite this
	// wave invites and the one to design against. So the absence is asserted
	// WITH the armed bodies those arms used to select: if the deletion had
	// taken the armed side instead of the dead one, the second half goes red
	// while the first stays green.
	EXPECT_EQ( 0, CountAliasOpens( server ) );
	EXPECT_EQ( 0, CountAliasOpens( client ) );

	// The armed answer the server file's arms selected, now unconditional: the
	// solver-object relay igameentity.h names, and the simulated question
	// derived from it rather than from a move type.
	//
	// RE-POINTED (plan_physics_bones_native_zero 2.3): the helper that asked it
	// of `pEntity` left with its one caller, PhysForceEntityToSleep, which is
	// managed's PhysicsSolver.ForceEntityToSleep. The same relay is still how
	// the active-object loop asks an owner for its own object.
	EXPECT_TRUE( Has( server, "StandaloneEntity_PhysicsObject( owner.pGame )" ) );

	// THE SECOND ARMED BODY IS GONE WITH ITS WHOLE FUNCTION, WHICH IS NOT THE
	// FAILURE THIS PAIR WAS DESIGNED TO CATCH -- and telling those two apart is
	// the entire reason the clause is replaced rather than deleted.
	//
	// `EXPECT_TRUE( Has( server, "EntityPhysicsObject( pEntity ) != NULL" ) )`
	// stood here. It was EntityIsVPhysicsSimulated's armed body, and the pair
	// above exists so that a wave which deleted the ARMED side instead of the
	// dead `#ifndef` side would red here while "there are no arms" stayed
	// green. plan_shim.md §4a item 15 deleted neither side: it deleted the
	// FUNCTION, because its only two callers -- UpdatePenetrateEvents' debris
	// swap and CanResolvePenetrationWithNPC -- went with the penetration rule
	// set, measured unreachable live (19,302 pairs into
	// ShouldSolvePenetration, 0 past its owner guard).
	//
	// So the pair property is preserved by the clause that is left:
	// StandaloneEntity_PhysicsObject is the OTHER armed body this file's arms
	// selected, it is still here, and a wave that took armed sides instead of
	// dead ones still reds on it. And the absence is asserted rather than
	// assumed, so the helper cannot come back unnoticed either.
	EXPECT_EQ( 0, CountOf( server, "EntityPhysicsObject( pEntity ) != NULL" ) );

	// The armed answer the client file's arms selected: the object is asked for
	// its entity, never the entity for its object.
	//
	// TWO -> FIVE, AND THE RISE IS THE POINT. `the-close` 6/N (25406d807) put
	// the three remaining raw spellings in this file onto the same accessor:
	// `static_cast<IGameEntity *>(pObject->GetGameData())` and
	// `reinterpret_cast<IGameEntity *>(pActiveList[i]->GetGameData())` are gone
	// and `PhysicsOwnerOf( ... ).pBase` is what stands where they were. On the
	// client the resolved `pBase` is the same value the cast produced; on the
	// server the accessor also knows the world and the handle-keyed arm, which
	// is why the one spelling is the right one on both sides.
	EXPECT_EQ( 5, CountOf( client, "PhysicsOwnerOf( pObject ).pBase" ) );

	// ...and the cast it replaced is not reachable in this file any more. The
	// fork's copy still mentions `GetGameData()` in comments explaining the
	// removal; the comment-free patched upstream (make sdk-upstream) does not,
	// so the raw count is no longer asserted here. That the stripper is what
	// removes such a mention is StrippingIsWhatStopsTheProseFalsePositive's
	// fact, over a literal it controls.
	EXPECT_EQ( 0, CountOf( client, "GetGameData()" ) );

	// And nothing in either file mentions the macro at all now — not in an
	// `#endif` comment, not in a stray `#else`. A guard half-deleted by a merge
	// would leave one of these non-zero.
	EXPECT_EQ( 0, CountOf( server, "SS_CBASEENTITY_ALIAS" ) );
	EXPECT_EQ( 0, CountOf( client, "SS_CBASEENTITY_ALIAS" ) );
}


//=============================================================================//
// 2. The solver-object question goes through the helpers.
//=============================================================================//

//-----------------------------------------------------------------------------
// igameentity.h's THE TWELVE: "VPhysicsGetObject  Never crosses ... Game code
// that genuinely starts at an entity asks StandaloneEntity_PhysicsObject."
//
// physics.cpp asked it twenty-one times from a bare root pointer and now asks
// through two helpers. Restoring one bare call compiles, keeps the suite green,
// and is a Phase 13 site again -- which is failure mode 2 in this file's header.
//
// The three receivers that legitimately keep the raw spelling are named here so
// the count is exact rather than "few": pParent0 and pParent1 sit INSIDE an
// arm that the flip removes, and pCombat is a CBaseCombatCharacter, a derived
// class whose disposition is lane U's.
//-----------------------------------------------------------------------------
TEST( T1SolverObject, NoRootPointerAsksVPhysicsGetObjectDirectly )
{
	const std::string code = CodeOf( "sourcesharp/game/server/physics.cpp" );
	ASSERT_FALSE( code.empty() );

	// The helpers exist and each has both arms.
	// RE-POINTED, NOT LOWERED: `the-close` 3/N (0bc727ed9) retired the alias,
	// so the two helpers this file is about are spelled `IGameEntity` now. It
	// is a RENAME -- armed, `CBaseEntity` WAS `IGameEntity` -- so these are the
	// same two definitions, asked for under the name they have today.
	//
	// THEY LEFT WITH THEIR LAST CALLER (plan_physics_bones_native_zero 2.3):
	// PhysForceEntityToSleep, the only asker of the list helper, is managed's
	// PhysicsSolver.ForceEntityToSleep, and the list of one it asked for is
	// the object it is handed. Absent now, both of them; the presence half at
	// the end of this test is what keeps the zeros below from passing on an
	// emptied file.
	EXPECT_EQ( 0, CountOf( code, "EntityPhysicsObject( IGameEntity *pEntity )" ) );
	EXPECT_EQ( 0, CountOf( code, "EntityPhysicsObjectList(" ) );

	// AND THE RETIRED SPELLING NO LONGER DECLARES ANYTHING IN THIS FILE.
	//
	// The clause is on the DECLARATION shapes, not on the bare word, and that
	// is deliberate: `the-close` 3/N renamed CODE tokens only and left comments
	// and string literals alone on purpose, because a blind rename turned
	// sentences like "worldspawn is not a CBaseEntity" into nonsense. This file
	// still says CBaseEntity forty-odd times in prose and five times inside
	// printed §7 refusal messages -- the refusals are strings, so CodeOf does
	// not strip them -- and none of that is a defect. What WOULD be a defect is
	// a pointer, a reference or a cast spelled with the retired name, and there
	// is no spelling of one that does not contain one of these three needles.
	EXPECT_EQ( 0, CountOf( code, "CBaseEntity *" ) );
	EXPECT_EQ( 0, CountOf( code, "CBaseEntity*" ) );
	EXPECT_EQ( 0, CountOf( code, "C_BaseEntity" ) );
	EXPECT_TRUE( Has( code, "StandaloneEntity_PhysicsObject( owner.pGame )" ) );

	// ONE RAW ASK LEFT, NOT THREE, AND TWO OF THE THREE WERE DEAD SOURCE.
	//
	// pParent0 and pParent1 sat INSIDE `#ifndef SS_CBASEENTITY_ALIAS` — the
	// comment above still says so — which is why they were allowed to keep the
	// raw spelling in the first place. The `residue` wave
	// (74b071771..a46e8d645) deleted the arm they lived in, together with every
	// other alias arm in this file. That arm was never compiled: this project
	// builds game/server/physics.cpp only with SS_CBASEENTITY_ALIAS and
	// SOURCESHARP both defined, and armed the `#ifndef` side is discarded
	// before the compiler sees it. 588 objects before and after, identical
	// DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// pCombat was the one that was NOT inside an alias arm -- AND IT WAS NOT
	// LIVE CODE EITHER, which this clause used to say it was.
	//
	// plan_shim.md Phase 2b (lane 2-physics) deleted the file's three `#if 0`
	// blocks, and pCombat's ask was inside one of them: stock's
	// CalculatePhysicsStressDamage arm in UpdateDamageEvents, switched off by
	// Valve with a comment about dying from the impact rather than the stress.
	// So it was dead source of a SECOND kind -- not an arm this build discards,
	// but an arm NOBODY's build compiles -- and the sentence that stood here
	// ("it is live code and stays") was wrong about it. A comment-stripping
	// reader counts `#if 0` text as code, which is exactly how it survived a
	// scan that was looking for guarded arms.
	//
	// THE NUMBER GOES DOWN AND THE CLAIM GETS STRONGER. What this test is about
	// is that no ROOT POINTER asks VPhysicsGetObject directly; one surviving
	// occurrence was the weakest form of that, and zero is the strongest.
	EXPECT_EQ( 0, CountOf( code, "pParent0->VPhysicsGetObject()" ) );
	EXPECT_EQ( 0, CountOf( code, "pParent1->VPhysicsGetObject()" ) );
	EXPECT_EQ( 0, CountOf( code, "pCombat->VPhysicsGetObject()" ) );

	// And no CALL SITE asks it of a root pointer any more. These three
	// spellings were sites before this shard and must not come back.
	EXPECT_EQ( 0, CountOf( code, "pEntity0->VPhysicsGetObject()" ) );
	EXPECT_EQ( 0, CountOf( code, "pEntity1->VPhysicsGetObject()" ) );
	EXPECT_EQ( 0, CountOf( code, "pInflictor->VPhysicsGetObject()" ) );

	// THE TWO `pEntity->` SPELLINGS WERE THE HELPERS' OWN SDK ARMS, and they
	// went with every other dead arm in the same wave. The clause that used to
	// stand here said "EQ rather than LE on purpose: zero would mean the SDK
	// arm had been lost, which breaks the unarmed build silently" — and that
	// reasoning was about an UNARMED build this project does not produce. There
	// is no unarmed build of game/server/physics.cpp to break: the .vpc
	// compiles it armed, and the survey that compiles it any other way is a
	// measurement run by hand against a tree that no longer has to carry the
	// text.
	//
	// THE SUCCESSOR IS STRONGER THAN THE RULE IT REPLACES. The old rule was
	// "no ROOT POINTER asks VPhysicsGetObject directly", enforced by naming the
	// receivers one at a time -- a list that only catches spellings somebody
	// thought to write down. What is true now is arithmetic over the whole
	// file: `VPhysicsGetObject` does not occur in it AT ALL. Any new raw ask, by
	// any receiver name, under any spelling, moves this number.
	//
	// IT READ 1 UNTIL plan_shim.md PHASE 2b, and the one was pCombat's, inside
	// an `#if 0` -- see the block above for why that is a deletion rather than
	// a regression. A ZERO HERE IS NOT VACUOUS: the presence half below asserts
	// the file still answers the object-list question through its helper, so an
	// emptied or unopened file fails rather than passes.
	EXPECT_EQ( 0, CountOf( code, "pEntity->VPhysicsGetObject()" ) );
	EXPECT_EQ( 0, CountOf( code, "pEntity->VPhysicsGetObjectList" ) );
	EXPECT_EQ( 0, CountOf( code, "VPhysicsGetObject" ) );

	// ...and the PRESENCE half, which is what stops all of the above passing
	// over an emptied file. The list question left with its caller (above), so
	// the half is the question that is still asked here: whether an active
	// object is its owner's own, through the relay.
	EXPECT_TRUE( Has( code, "StandaloneEntity_PhysicsObject( owner.pGame ) == pObject" ) );
}


//=============================================================================//
// 3. The move-type inversion, which is the trap this shard came closest to.
//=============================================================================//

//-----------------------------------------------------------------------------
// physicsowner.h gives IsVPhysicsSimulated its own name rather than letting it
// be derived from the move type, and says why: "an object is in the registry
// BECAUSE its entity simulates, and nothing else puts it there."
//
// The neutral for a MISSING move type is MOVETYPE_NONE. Reading that as "not
// simulated" is the natural simplification, compiles, and inverts the answer
// for every entity on the parallel hierarchy. So the armed arm must ask the
// SOLVER, and this pins that it does.
//
// The mutation this is aimed at REPLACES the armed body, so the assertion is on
// what the body contains and on what it must not: a needle that only checked
// for the helper's NAME would survive a mutation that kept the name and changed
// the body, which is the "anchor renamed by appending" shape recorded in this
// port's ledger of checks that cannot fail.
//-----------------------------------------------------------------------------
TEST( T1MoveType, TheSimulatedQuestionAsksTheSolverNotTheMoveTypeNeutral )
{
	const std::string code = CodeOf( "sourcesharp/game/server/physics.cpp" );
	ASSERT_FALSE( code.empty() );

	// THE PRESENCE HALF IS RETIRED AND THE SUBJECT IS NAMED, because a
	// disappearing clause is how a weakened test hides.
	//
	// What stood here was `BodyAfter( code, "EntityIsVPhysicsSimulated( ... )" )`
	// plus `EXPECT_TRUE( Has( body, "EntityPhysicsObject( pEntity ) != NULL" ) )`
	// -- "the helper exists and asks the solver". plan_shim.md §4a item 15
	// deleted EntityIsVPhysicsSimulated: its only two callers were
	// UpdatePenetrateEvents' debris swap and CanResolvePenetrationWithNPC, and
	// both went with the penetration rule set, which was measured unreachable
	// (19,302 pairs reached ShouldSolvePenetration, 0 past its owner guard).
	// A helper with no callers cannot be asserted to ask the right question.
	//
	// THE ABSENCE HALF IS THE ONE THAT GUARDED THE BUG AND IT IS UNTOUCHED, and
	// it was already file-wide rather than body-scoped -- so what this test
	// catches is exactly what it caught before: anybody deriving "is it
	// simulated" from the move-type neutral, ANYWHERE in physics.cpp. The
	// body-scoped clauses go only because there is no body.

	// THE ARMED ARM IS THE WHOLE BODY NOW, and that is the fact this test
	// states. EntityIsVPhysicsSimulated was `#ifndef SS_CBASEENTITY_ALIAS` /
	// `#else` / `#endif` at 74b071771 with the move-type comparison on the
	// unarmed side; the `residue` wave (74b071771..a46e8d645) deleted that side
	// along with every other alias arm in game/server/physics.cpp. It was never
	// compiled — the .vpc defines SS_CBASEENTITY_ALIAS and SOURCESHARP for this
	// TU and no other configuration is built — so what the wave removed is the
	// text of the arm the preprocessor was already discarding.
	//
	// The armed arm asks whether the solver has an object for it, exactly as

	// THE ABSENCE HALF IS NOW THE WHOLE FILE, not just this body, and that is
	// strictly stronger than the clause it replaces. The trap this test exists
	// for is somebody deriving "is it simulated" from the move-type neutral —
	// MOVETYPE_NONE read as "not simulated", which inverts the answer for every
	// entity on the parallel hierarchy. With the SDK arm gone there is no
	// correct use of ANY move-type constant anywhere in this file, so the
	// question is asked of the file rather than of the body: a reintroduction
	// somewhere else in physics.cpp is the same defect and now fails here too.
	EXPECT_EQ( 0, CountOf( code, "MOVETYPE_VPHYSICS" ) );
	EXPECT_EQ( 0, CountOf( code, "MOVETYPE_NONE" ) );

	// THE TRAP SPELLING, NOT THE ACCESSOR. The body-scoped clause that stood
	// here was `CountOf( body, "GetMoveType()" ) == 0`, and widening THAT to
	// the file is wrong rather than stronger: `owner.GetMoveType()` is
	// PhysicsOwner_t's hierarchy-neutral accessor and WheelCollidesWith uses it
	// correctly at physics.cpp:1398. Asserting zero of those would have made
	// this test demand the removal of a call the port deliberately kept, and it
	// read 1 immediately.
	//
	// What the trap actually looks like is the RAW spelling -- asking an
	// IGameEntity * directly, `pEntity->GetMoveType()`, which is what returns
	// the MOVETYPE_NONE neutral for everything on the parallel hierarchy. That
	// is what is asserted absent, and file-wide, which is what the clause was
	// widened for in the first place.
	EXPECT_EQ( 0, CountOf( code, "->GetMoveType()" ) );

	// AND THE HELPER REALLY IS GONE, rather than the clauses above having
	// quietly stopped matching. Without this the whole test would pass on a
	// file that no longer contains its subject, which is the shape this port's
	// ledger of checks-that-cannot-fail is full of.
	EXPECT_EQ( 0, CountOf( code, "EntityIsVPhysicsSimulated" ) );
}


//=============================================================================//
// 4. A NULL test must not become a world test.
//=============================================================================//

//-----------------------------------------------------------------------------
// The hierarchy's contract warns about this by name, and ai_navigator.h's arm
// states the fact: "over here the world is CWorld ... so it is reached through
// the SDK arm and answers non-NULL; nothing that arrives on the parallel side
// is ever the world. Rewriting the NULL test into a world test would be the
// error this hierarchy's contract warns about."
//
// CConstraintFloodList::IsWorldEntity is this shard's one site where an armed
// arm answers a world question, and the answer has to be a plain false. A
// mutation that "helpfully" restores an IsWorld() call in the armed arm is what
// this catches.
//-----------------------------------------------------------------------------
TEST( T1WorldTest, IsWorldEntityLeftWithItsDeadFloodList )
{
	// CConstraintFloodList was physics_budget's neighbour in physics_debug.cpp
	// and had NO caller: DebugConstraints, the one walk that built it, went
	// with the SDK entity list. plan_physics_bones_native_zero 2.3 moved
	// physics_budget to managed (PhysicsBudget) and deleted the dead list with
	// it, so the armed world question this test held to a plain false has no
	// body left to grow an IsWorld() into.
	const std::string code = CodeOf( "sourcesharp/game/server/physics_debug.cpp" );
	ASSERT_FALSE( code.empty() );

	EXPECT_EQ( 0, CountOf( code, "CConstraintFloodList" ) );
	EXPECT_EQ( 0, CountOf( code, "IsWorldEntity(" ) );
	EXPECT_EQ( 0, CountOf( CodeOf( "sourcesharp/game/server/physics.cpp" ), "IsWorldEntity(" ) );

	// The presence half: the file is the one that held it, and still holds the
	// stock command it sat beside.
	EXPECT_TRUE( Has( code, "SS_NATIVE_COMMAND( physics_select )" ) );
}


//=============================================================================//
// 5. The two mechanisms whose halves used to disagree.
//=============================================================================//

//-----------------------------------------------------------------------------
// The FLUID throttle. The server's DeltaTimeSinceLastFluid was already keyed by
// handle (and since plan_physics_managed B2.2 is managed's PhysicsFluidEvents,
// keyed by EntityHandle); the client's still resolved the stored EHANDLE back to an entity,
// which answers NULL for everything outside the SDK -- so every such entity
// matched every other one's slot and one splash suppressed them all.
//
// fluidevent_t::hEntity is a CBaseHandle for exactly this reason, and the two
// signatures now agree.
//-----------------------------------------------------------------------------
TEST( T1FluidThrottle, TheClientKeysTheThrottleByHandle )
{
	const std::string client = CodeOf( "game/client/physics.cpp" );

	ASSERT_FALSE( client.empty() );

	EXPECT_TRUE( Has( client, "DeltaTimeSinceLastFluid( IHandleEntity *pEntity )" ) );

	// The client's declaration inside its own CCollisionEvent agrees with the
	// definition -- a mismatch there is a compile error, but the point is that
	// neither reverts to the entity spelling.
	EXPECT_EQ( 0, CountOf( client, "DeltaTimeSinceLastFluid( CBaseEntity *pEntity )" ) );
	EXPECT_EQ( 0, CountOf( client, "DeltaTimeSinceLastFluid( C_BaseEntity *pEntity )" ) );

	// The body compares handles rather than pointers.
	const std::string body = BodyAfter( client, "CCollisionEvent::DeltaTimeSinceLastFluid( IHandleEntity *pEntity )" );
	ASSERT_FALSE( body.empty() );
	EXPECT_TRUE( Has( body, "pEntity->GetRefEHandle()" ) );
	EXPECT_TRUE( Has( body, "m_fluidEvents[i].hEntity == handle" ) );
	EXPECT_EQ( 0, CountOf( body, "hEntity.Get()" ) );
}


//=============================================================================//
// 6. A handle read as a handle.
//=============================================================================//

//-----------------------------------------------------------------------------
// A sound's OWNER. `m_hOwner.Get()` is CHandle<CBaseEntity>::Get(), which
// resolves the stored EHANDLE to a CBaseEntity and answers NULL for an owner on
// the parallel hierarchy -- so a sound made by a managed entity reported no
// owner at all, and every listen filter testing "who made this" saw a world
// noise.
//
// shell_resolve.h: "the handle is the one number that never cared which side
// its owner lived on."
//
//=============================================================================
// RE-POINTED by plan_gamedll_after.md Phase A3's second pair, and the property
// is the same one -- what moved is where it lives.
//
// This used to read shell_sound.cpp's SourceSharp_SoundEntQuery, the engine
// slot that handed CSoundEnt's list to managed code. game/server/soundent.cpp
// has left server_csharp.vpc, the pool is SourceSharp.Server's SoundEnt, and
// that slot is gone with it -- so the fact's subject was deleted by the phase
// and it failed on a tree where nothing was wrong. This tree's recorded "a
// mutation that stops reaching its subject", met by a FACT rather than a
// mutation.
//
// THE SURVIVING SUBJECT IS THE OTHER DIRECTION. shell_soundent.cpp's
// ss::SoundEnt_InsertSound (CSoundEnt::InsertSound forwards to it since P5 lane
// SP gave it an R5 spelling) is where a native caller's owner now crosses, and the
// same question applies to it: is the owner read as a HANDLE, or resolved to a
// CBaseEntity first?
//
// AND THE THIRD EXPECTATION INVERTS, which is why this is a re-point and not a
// rename. Reading the list, the handle is already stored and
// SourceSharp_HandleFromAnyEntity must NOT appear -- calling it would mean
// something had been resolved and re-derived. WRITING the list, the caller has
// a pointer and the helper is exactly the right thing: it asks the
// IHandleEntity face, which both hierarchies answer. Its CBaseEntity-only
// sibling, SourceSharp_HandleFromEntity, is the spelling that would reintroduce
// the bug.
//-----------------------------------------------------------------------------
TEST( T1SoundOwner, TheOwnerHandleIsNotResolvedThroughAnEntityFirst )
{
	const std::string code = CodeOf( "sourcesharp/game/server/shell_soundent.cpp" );
	ASSERT_FALSE( code.empty() );

	const std::string body = BodyAfter( code, "void ss::SoundEnt_InsertSound(" );
	ASSERT_FALSE( body.empty() ) << "ss::SoundEnt_InsertSound is gone or unparseable";

	// The owner AND the target, which is two of them: stock stores both as
	// EHANDLEs and both are read by the same rule.
	EXPECT_EQ( 2, CountOf( body, "SourceSharp_HandleFromAnyEntity(" ) );

	// The three spellings that would put the bug back.
	EXPECT_EQ( 0, CountOf( body, "SourceSharp_HandleFromEntity(" ) );
	EXPECT_EQ( 0, CountOf( body, "->GetRefEHandle()" ) );
	EXPECT_EQ( 0, CountOf( body, ".Get()" ) );
}


//=============================================================================//
// 6b. A guarded-away DEFINITION must leave one behind, or the failure is a LINK
//     error that nothing in this shard's gates runs.
//=============================================================================//

//-----------------------------------------------------------------------------
// s5_guard_balance.py's rule 2 finds every region that swallows a function
// boundary -- the defect shape shard T2 landed twice -- and it correctly flags
// this shard's five WHOLE-DEFINITION guards too, because textually they are the
// same shape. What it cannot tell apart is the deliberate one from the mistake,
// and for the deliberate one the real exposure is different and worse:
//
//   a function with EXTERNAL LINKAGE whose only definition is inside a
//   `#ifndef` compiles cleanly in every TU, armed and unarmed, and fails at
//   LINK with an undefined symbol.
//
// Neither the unarmed build nor the armed survey can see that: the survey
// compiles objects and never links. This shard had exactly one --
// PhysGetMassCenterOverride, declared in physics.h:185 and called from
// physobj.cpp:524 and props.cpp:2620 -- and it now has an `#else` no-op.
//
// So: of the five regions the checker flags here, the two whose subject has
// external linkage must define it BOTH ways, and the two static ones must stay
// static, which is what makes their callers' guards sufficient.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// f-physics: "no configuration lacks a definition", checked as the rule rather
// than as one of its spellings.
//
// A definition wrapped in `#ifndef SS_CBASEENTITY_ALIAS` needs an `#else` arm,
// so it appears TWICE. A definition with no guard at all appears ONCE and
// serves both configurations. Both are correct; one definition inside a guard
// with no `#else` is the link error, and so is three.
//
// `strCode` has had its comments stripped by CodeOf, so prose naming the
// signature cannot satisfy this -- which matters in these two files, where the
// signature is discussed in comments as well as written.
//-----------------------------------------------------------------------------
void CheckEveryArmHasADefinition( const std::string &strCode, const std::string &strSignature )
{
	const int nDefs = CountOf( strCode, strSignature );
	ASSERT_LT( 0, nDefs ) << strSignature << " is not defined at all. If it is "
		<< "declared in a header and called from another translation unit, this "
		<< "compiles everywhere and fails at LINK -- which no compile-only "
		<< "survey and no unarmed build can see.";

	// Is the definition inside an alias guard? Look at the text immediately
	// before it: a guarded definition is written as the guard line and then the
	// signature, which is the shape both of this shard's cases use.
	const std::string strGuardedOpen = "#ifndef SS_CBASEENTITY_ALIAS\n" + strSignature;
	const bool bGuarded = ( CountOf( strCode, strGuardedOpen ) > 0 );

	if ( bGuarded )
	{
		EXPECT_EQ( 2, nDefs ) << strSignature << " is guarded by "
			<< "#ifndef SS_CBASEENTITY_ALIAS but has " << nDefs << " definition(s). "
			<< "A guarded definition needs an #else arm; one is the link error.";
	}
	else
	{
		EXPECT_EQ( 1, nDefs ) << strSignature << " is not guarded but has "
			<< nDefs << " definition(s). One unguarded definition serves both "
			<< "configurations; more than one is a redefinition.";
	}
}

TEST( T1GuardedDefinitions, AnExternallyLinkedGuardedFunctionIsDefinedBothWays )
{
	const std::string physics = CodeOf( "sourcesharp/game/server/physics.cpp" );
	const std::string mapents = CodeOf( "sourcesharp/game/server/shell_mapparse.cpp" );

	ASSERT_FALSE( physics.empty() );
	ASSERT_FALSE( mapents.empty() );

	// f-physics: THE INVARIANT IS "EVERY CONFIGURATION HAS A DEFINITION", AND
	// THE COUNT 2 WAS ONE WAY OF SPELLING IT, NOT THE INVARIANT ITSELF.
	//
	// PhysGetMassCenterOverride is now defined ONCE, unguarded: its body is a
	// declared §7 refusal, which is the same answer in both configurations, so
	// there is nothing for a guard to choose between. That satisfies this rule
	// more strongly than two arms did -- there is no arm that can lose its
	// definition -- and a flat `EXPECT_EQ( 2, ... )` would have called it a
	// regression.
	//
	// So the check is now the rule: a definition that IS guarded must appear
	// twice, and one that is NOT guarded must appear once. Either way no
	// configuration links without it, which is the link error this test exists
	// to stop.
	//
	// AND NOW IT IS GONE TOO (server gc cull, 2026-10-01): nothing called it,
	// server.so's --print-gc-sections link dropped it, and physics.cpp no longer
	// includes game/server/physics.h, the header that declared it (P5,
	// ss_physics.h). With no declaration and no caller there is no
	// configuration left that could fail to link without it; asserted absent,
	// the same treatment as the second subject below.
	ASSERT_TRUE( Has( physics, "PhysicsGameSystem" ) ) << "physics.cpp did not read";
	EXPECT_FALSE( Has( physics, "PhysGetMassCenterOverride" ) )
		<< "PhysGetMassCenterOverride is back in physics.cpp; if it has a caller "
		   "again, the guarded-definition rule applies to it once more.";
	// THE SECOND SUBJECT IS GENUINELY GONE, and the fact is the absence -- the
	// same treatment TheGuardedAwayConsoleHelpersStayStatic below gives its own
	// four. SetupParentsForSpawnList was a declared, externally-linked function
	// whose definition every configuration had to carry, which is what made it
	// worth checking here. plan_shim.md Phase 2c deleted it: 0 importers on the
	// debug link line, its only intra-file caller (SpawnAllEntities) went in the
	// same band, and a counter on that band read 0 over two dm_lockdown loads in
	// one process. Its declaration left game/server/mapentities.h with it, so
	// there is no configuration left that could fail to link without it.
	//
	// ASSERTED AS AN ABSENCE rather than deleted, because "no arm loses its
	// definition" is satisfied trivially by a function that does not exist, and
	// a reader needs to be able to tell that apart from a needle that stopped
	// matching.
	EXPECT_FALSE( Has( mapents, "SetupParentsForSpawnList" ) )
		<< "SetupParentsForSpawnList is back in the map parse. It is declared in "
		   "no header now, so if it has a caller again both halves came back and "
		   "the guarded-definition rule above applies to it once more.";

	// (Both were declared in headers, which is what made the count matter; the
	// header half went with the subjects.)
}

TEST( T1GuardedDefinitions, TheGuardedAwayConsoleHelpersStayStatic )
{
	// physics.cpp AND physics_debug.cpp: the console helpers moved into the
	// debug file with the diagnostics (2026-10-01), so the absences cover both
	// and the positive control below reads the debug half.
	const std::string host = CodeOf( "sourcesharp/game/server/physics.cpp" );
	const std::string debug = CodeOf( "sourcesharp/game/server/physics_debug.cpp" );
	ASSERT_FALSE( host.empty() );
	ASSERT_FALSE( debug.empty() );
	const std::string code = host + debug;

	// THE SUBJECT OF THIS FACT IS GENUINELY GONE, AND THE FACT IS NOW THE
	// ABSENCE — which is a DIFFERENT and stronger claim than the one that used
	// to stand here, not a weaker one.
	//
	// What stood here was: both helpers are `static`, so the definitions the
	// alias arm removes cannot be referenced from outside the file, and each
	// has exactly one caller, itself inside a guard. At 74b071771 that was
	// four things in game/server/physics.cpp — `static void
	// OutputVPhysicsDebugInfo( CBaseEntity *pEntity )` at :3207 inside
	// `#ifndef SS_CBASEENTITY_ALIAS` opened at :3206; `static void
	// DebugConstraints( CBaseEntity *pEntity )` at :3349 inside the guard
	// opened at :3348; CON_COMMAND(physics_constraints) at :3541 inside
	// `#ifndef SS_CBASEENTITY_ALIAS` at :3540; and
	// CON_COMMAND(physics_debug_entity) at :3550 inside `#ifndef SOURCESHARP`
	// at :3549.
	//
	// ALL FOUR WERE DEAD SOURCE. This project compiles game/server/physics.cpp
	// with SS_CBASEENTITY_ALIAS and SOURCESHARP BOTH DEFINED and with no other
	// configuration, so both kinds of `#ifndef` arm are discarded before the
	// compiler sees them. Neither command was ever registered in a build this
	// tree produces and neither helper was ever emitted. The `residue` wave
	// (74b071771..a46e8d645) deleted the text: 588 objects before and after,
	// identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// So "they stay static" is no longer expressible — there is nothing to be
	// static. The property that replaces it is the one `static` was BUYING:
	// nothing in this file names either helper. That is what these four
	// clauses say, and it holds against a caller being added as well as
	// against a definition coming back, which `static` never did.
	//
	// These are ADDITION-detecting checks. The mutation that breaks them adds
	// text rather than removing it, which is the opposite polarity from every
	// other fact in this file and is written down here because a substring
	// needle cannot catch an addition by accident.
	EXPECT_EQ( 0, CountOf( code, "OutputVPhysicsDebugInfo" ) );
	EXPECT_EQ( 0, CountOf( code, "DebugConstraints" ) );
	EXPECT_EQ( 0, CountOf( code, "physics_constraints" ) );
	EXPECT_EQ( 0, CountOf( code, "physics_debug_entity" ) );

	// THE POSITIVE CONTROL, and without it the four clauses above pass over any
	// file at all — including one this test failed to open, which is a shape
	// this tree has shipped more than forty times.
	//
	// PhysicsCommand is the route both deleted commands went through, and it is
	// STILL HERE with its refusal body, because `physics_select` still calls
	// it. So the machinery that made the deleted pair unreachable is present
	// and being exercised; what is absent is absent because it was removed, not
	// because the file is.
	EXPECT_TRUE( Has( debug,
		"void PhysicsCommand( const CCommand &args, void (*func)( IGameEntity *pEntity ) )" ) );
	EXPECT_EQ( 1, CountOf( debug, "PhysicsCommand( args, MarkVPhysicsDebug );" ) );
	EXPECT_TRUE( Has( debug, "SS_NATIVE_COMMAND( physics_select )" ) );
}


//=============================================================================//
// 7. Scanner self-checks. Without these the six sections above can all pass on
//    an empty string.
//=============================================================================//

TEST( T1ScannerSelfCheck, StrippingIsWhatStopsTheProseFalsePositive )
{
	// physics.cpp's comments quote the very spellings this file forbids. If
	// stripping regressed, the tests above would fail -- so this proves the
	// stripper is doing the work, by showing the raw text HAS what the stripped
	// text does not.
	//
	// RE-POINTED at physicsowner.h (plan_physics_bones_native_zero 2.3): the
	// prose in physics.cpp that quoted igameentity.h left with
	// EntityPhysicsObject. physicsowner.h names the member in a comment and
	// never in code, which is the same control.
	const std::string raw = ReadFile( "sourcesharp/game/shared/physicsowner.h" );
	const std::string code = CodeOf( "sourcesharp/game/shared/physicsowner.h" );

	ASSERT_FALSE( raw.empty() );
	ASSERT_FALSE( code.empty() );
	EXPECT_LT( code.size(), raw.size() );

	EXPECT_GT( CountOf( raw, "VPhysicsGetObject" ), CountOf( code, "VPhysicsGetObject" ) );
}

TEST( T1ScannerSelfCheck, BodyExtractorTakesOneBodyAndNotTheWholeFile )
{
	const std::string code = CodeOf( "sourcesharp/game/server/physics.cpp" );
	ASSERT_FALSE( code.empty() );

	// THE ANCHOR IS A CONTROL, NOT A SUBJECT, and it had to be re-pointed.
	//
	// This test proves the extractor takes ONE body rather than the whole file,
	// so it needs some function it knows is present. It used
	// EntityIsVPhysicsSimulated, which plan_shim.md §4a item 15 deleted --
	// and that would have reddened this test for a reason having nothing to do
	// with what it checks. A symbol with no callers can still be load-bearing
	// as a test's control; that is the fifth way this tree hides a live
	// dependency from a symbol census, after by-name reach, macro registration,
	// inline construction and vtable dispatch.
	//
	// PhysIsInCallback replaced it, and went with native's collision handler
	// (plan_physics_bones_native_zero 2.1: its in-callback count was that
	// handler's). PhysicsWorldOwner is the control now: physicsowner.h's
	// PhysicsOwnerOf calls it for every object, on both hierarchies.
	const std::string body =
		BodyAfter( code, "PhysicsWorldOwner( const IPhysicsObject *pObject )" );
	ASSERT_FALSE( body.empty() ) << "the scanner's control anchor is gone";

	// A body, not the file.
	EXPECT_LT( body.size(), code.size() / 4 );
	EXPECT_EQ( '{', body[ 0 ] );
	EXPECT_EQ( '}', body[ body.size() - 1 ] );

	// A missing anchor yields empty rather than something, which is what makes
	// the ASSERT_FALSE guards above real.
	EXPECT_TRUE( BodyAfter( code, "NoSuchFunctionAnchorT1(" ).empty() );
}

TEST( T1ScannerSelfCheck, CountOfCountsRatherThanFindingOnce )
{
	EXPECT_EQ( 0, CountOf( "abcabc", "z" ) );
	EXPECT_EQ( 2, CountOf( "abcabc", "abc" ) );
	EXPECT_EQ( 1, CountOf( "aaa", "aa" ) );
	EXPECT_EQ( 0, CountOf( "abc", "" ) );
}

//-----------------------------------------------------------------------------
// The directive counter, which the first mutation run proved was needed: a
// RENAMED #endif must not still count as one.
//-----------------------------------------------------------------------------
TEST( T1ScannerSelfCheck, ADirectiveCountIsNotASubstringCount )
{
	EXPECT_EQ( 1, CountDirective( "#endif\n", "#endif" ) );
	EXPECT_EQ( 1, CountDirective( "#endif // note\n", "#endif" ) );
	EXPECT_EQ( 1, CountDirective( "#endif", "#endif" ) );

	// The mutation that was silently survived before this existed.
	EXPECT_EQ( 0, CountDirective( "#endif_EATEN_BY_A_MERGE\n", "#endif" ) );
	EXPECT_EQ( 0, CountDirective( "#endifX", "#endif" ) );

	// ...and the substring counter genuinely does NOT distinguish them, which
	// is what makes the pair above evidence rather than decoration.
	EXPECT_EQ( 1, CountOf( "#endif_EATEN_BY_A_MERGE\n", "#endif" ) );
}
