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
//-----------------------------------------------------------------------------
// The two physics.cpp files carry the bulk of the arms. A floor rather than an
// exact count, because adding a correct arm is a normal thing to do and this
// test must not punish it; what it catches is a collapse.
//-----------------------------------------------------------------------------
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
// TEST( T1FluidThrottle, TheClientKeysTheThrottleByHandle ) stood here and read
// fork game/client/physics.cpp's DeltaTimeSinceLastFluid. That file left
// client.so (2026-10-18); the client's throttle is managed's ClientFluidEvents,
// keyed by EntityHandle, and ClientFluidEventsTests' TwoOwnersAreThrottledApart
// and TheThrottleCountsFromTheOwnersFirstEntry hold the handle keying.

//=============================================================================//
// 6. A handle read as a handle.
//=============================================================================//

//-----------------------------------------------------------------------------
// TEST( T1SoundOwner, TheOwnerHandleIsNotResolvedThroughAnEntityFirst ) stood
// here and read shell_soundent.cpp's ss::SoundEnt_InsertSound, which handed a
// sound's owner and target to the managed pool as handles. That function went
// at plan_server_native_v2 T5 (no server object called it): sound hints enter
// the managed SoundEnt from managed code, where the owner is an EntityHandle
// by type.
//-----------------------------------------------------------------------------

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

//=============================================================================//
// 7. Scanner self-checks. Without these the six sections above can all pass on
//    an empty string.
//=============================================================================//

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
