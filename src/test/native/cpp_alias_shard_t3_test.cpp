//========= SourceSharp — shard T3's .cpp arms, read as source ===============//
//
// A SOURCE SCANNER, in cpp_alias_lane1_test / cpp_alias_shard2_test's idiom and
// for their reason: every claim here is about the TEXT of files that need a
// whole engine to run, so the text is what is read.
//
// WHAT SHARD T3 DID. Route step 5's `.cpp` half, shard T3 of lane T
// (docs/typedef-dryrun/route/s5_shards.json): 176 armed sites in twelve files.
// Three claims in it are load-bearing and NOTHING IN THE BUILD NOTICES ANY OF
// THEM ROTTING BACK, because they are all about the disarmed tree's text:
//
//   1. THE SUB-OBJECT ARGUMENT. collisionproperty.cpp and particle_property.cpp
//      lose 93 of those 176 sites to one arm each, and the arm is only
//      justified because CCollisionProperty and CParticleProperty are
//      CBaseEntity's OWN sub-objects — `CNetworkVarEmbedded` members reached
//      only through CollisionProp() / ParticleProp(). What must not drift is
//      WHAT IS OUTSIDE those arms: UpdateDirtySpatialPartitionEntities is
//      called by name from the new-style particle effects' frame (particlemgr.cpp's
//      UpdateNewEffects call, in shell_new_particles_client.cpp since that
//      file left the build), so guarding it would turn a compile error into a
//      LINK error, and
//      the CDirtySpatialPartitionEntityList read-lock pair is an
//      IPartitionQueryCallback the ENGINE calls and has to keep pairing. Both
//      are one careless brace move away from being swallowed.
//
//   2. EVERY GUARD MUST BE DEFEATABLE. survey_arm_alias.py's negative control
//      rewrites `#ifndef SS_CBASEENTITY_ALIAS` as a whole line and NOTHING
//      ELSE (GUARD_RE, `^([ \t]*)#ifndef SS_CBASEENTITY_ALIAS\b(?=[ \t]*$)`).
//      A compound `#if defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )`
//      compiles identically, guards identically, and `defeat` cannot reach it —
//      so the site stays hidden in the control run and the control quietly
//      stops being one. That spelling was written in this shard once and caught
//      before it landed; this is what stops it coming back.
//
//   3. TWO ARMS ARE COPIES OF AN ANSWER THAT LIVES SOMEWHERE ELSE, and a copy
//      with no compiler between it and its original is exactly what
//      docs/typedef-dryrun says to assert rather than assume. The server
//      CStandaloneMoveHelper::IsWorldEntity's armed arm must stay the line its
//      CLIENT TWIN already is.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, and this tree has shipped several. The fixtures at
// the bottom drive the guard-region walker over SYNTHETIC input where the
// answer is known, including the compound spelling it exists to reject, and
// assert that every real file was opened and is not trivial.
//
// Comments are stripped before any match, for the reason cpp_alias_shard2_test
// records: every arm in this shard carries prose NAMING the member it stopped
// calling, so a raw-text scan would read the name straight out of the sentence
// saying it is gone. The GUARD WALK, by contrast, runs on RAW text — it reads
// preprocessor directives, and stripping comments does not move those, while
// stripping them first would let a `//`-commented `#endif` disappear silently.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <cctype>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
std::string Trim( const std::string &strIn )
{
	size_t a = 0;
	while ( a < strIn.size() && ( strIn[ a ] == ' ' || strIn[ a ] == '\t' || strIn[ a ] == '\r' ) )
		a++;
	size_t b = strIn.size();
	while ( b > a && ( strIn[ b - 1 ] == ' ' || strIn[ b - 1 ] == '\t' || strIn[ b - 1 ] == '\r' ) )
		b--;
	return strIn.substr( a, b - a );
}

//=============================================================================
// THE GUARD WALK.
//
// Which arm of the alias each line of a file is in. Not proximity to a
// `#ifndef` — a scan that looked for the directive "near" a line would pass on
// a file where the `#endif` moved above it, which is precisely the drift claim
// 1 is about. This tracks the preprocessor nesting.
//
// Three answers, and the middle one is the one with teeth:
//
//   ARM_ALWAYS  compiled in both configurations
//   ARM_SDK     inside a live `#ifndef SS_CBASEENTITY_ALIAS` branch — dropped
//               when the alias is armed
//   ARM_ALIAS   inside the `#else` of one — compiled ONLY when it is armed
//
// Nesting is a stack, so a `#ifdef CLIENT_DLL` wrapped around an SS guard, or
// the other way round, is read correctly rather than by luck.
//=============================================================================
enum Arm_t
{
	ARM_ALWAYS,
	ARM_SDK,
	ARM_ALIAS,
};

struct Frame_t
{
	bool bIsAliasGuard;   // this #if is `#ifndef SS_CBASEENTITY_ALIAS`
	bool bInElse;         // we have passed its #else
};

// The EXACT spelling survey_arm_alias.py's negative control rewrites: optional
// leading whitespace, the directive, and nothing else on the line.
bool IsDefeatableGuardLine( const std::string &strLine )
{
	return Trim( strLine ) == "#ifndef SS_CBASEENTITY_ALIAS";
}

// Any OTHER preprocessor condition that mentions the macro. Every one of these
// is a guard `defeat` cannot reach.
bool IsUndefeatableGuardLine( const std::string &strLine )
{
	const std::string t = Trim( strLine );
	if ( t.size() < 3 || t[ 0 ] != '#' )
		return false;
	if ( IsDefeatableGuardLine( t ) )
		return false;
	// #ifdef / #ifndef / #if / #elif that names the macro.
	const bool bIsCondition =
		t.compare( 0, 4, "#if " ) == 0 || t.compare( 0, 4, "#if(" ) == 0 ||
		t.compare( 0, 6, "#ifdef" ) == 0 || t.compare( 0, 7, "#ifndef" ) == 0 ||
		t.compare( 0, 5, "#elif" ) == 0;
	if ( !bIsCondition )
		return false;
	// SS_CBASEENTITY_ALIAS_H is the alias header's own include guard and is not
	// one of these; require the name to end there.
	const size_t p = t.find( "SS_CBASEENTITY_ALIAS" );
	if ( p == std::string::npos )
		return false;
	const size_t after = p + sizeof( "SS_CBASEENTITY_ALIAS" ) - 1;
	if ( after < t.size() && ( t[ after ] == '_' || isalnum( t[ after ] ) ) )
		return false;
	return true;
}

void SplitLines( const std::string &strIn, std::vector< std::string > *pOut )
{
	std::string cur;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( strIn[ i ] == '\n' )
		{
			pOut->push_back( cur );
			cur.clear();
			continue;
		}
		cur += strIn[ i ];
	}
	pOut->push_back( cur );
}

// Per-line arm, for the RAW text of a file.
void WalkArms( const std::string &strRaw, std::vector< std::string > *pLines,
               std::vector< Arm_t > *pArms )
{
	pLines->clear();
	pArms->clear();
	SplitLines( strRaw, pLines );

	std::vector< Frame_t > stack;
	for ( size_t i = 0; i < pLines->size(); i++ )
	{
		const std::string t = Trim( ( *pLines )[ i ] );
		const bool bIsIf =
			t.compare( 0, 3, "#if" ) == 0;
		const bool bIsElse = ( t == "#else" ) || t.compare( 0, 5, "#elif" ) == 0;
		const bool bIsEndif = t.compare( 0, 6, "#endif" ) == 0;

		if ( bIsElse && !stack.empty() )
			stack.back().bInElse = true;

		if ( bIsEndif && !stack.empty() )
			stack.pop_back();

		// The arm of the DIRECTIVE line itself is whatever encloses it, which is
		// what the frame stack says after the pops above and before the push
		// below.
		Arm_t arm = ARM_ALWAYS;
		for ( size_t f = 0; f < stack.size(); f++ )
		{
			if ( !stack[ f ].bIsAliasGuard )
				continue;
			arm = stack[ f ].bInElse ? ARM_ALIAS : ARM_SDK;
			break;
		}
		pArms->push_back( arm );

		if ( bIsIf && !bIsElse && !bIsEndif )
		{
			Frame_t frame;
			frame.bIsAliasGuard = IsDefeatableGuardLine( t );
			frame.bInElse = false;
			stack.push_back( frame );
		}
	}
}

//-----------------------------------------------------------------------------
// THE FILES. Shard T3's twelve, named once.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE TWO FILES THE `residue` WAVE CLEARED, AND WHY THEIR ROWS STAY.
//
// The `residue` wave (74b071771..a46e8d645) deleted 5,282 lines of DEAD guarded
// arms out of 44 compiled stock TUs. Two of them are shard T3's, and every arm
// it took from them was a `#ifndef SS_CBASEENTITY_ALIAS` WITH NO `#else`:
//
//   game/shared/collisionproperty.cpp
//       74b071771:src/game/shared/collisionproperty.cpp:202  AddEntity's push
//       74b071771:src/game/shared/collisionproperty.cpp:240  the drain's
//                                                            client resolve
//       74b071771:src/game/shared/collisionproperty.cpp:288  the drain's body
//       74b071771:src/game/shared/collisionproperty.cpp:421  the WHOLE
//           out-of-line half of CCollisionProperty, closing at :1647 --
//           1,227 lines carrying DT_CollisionProperty, SetSolid,
//           UpdatePartition, GetCollisionOrigin (:816) and
//           GetCollisionAngles (:821)
//
//   game/client/particlemgr.cpp
//       74b071771:src/game/client/particlemgr.cpp:1434  the C_BaseEntityIterator
//           sweep in CParticleMgr::RemoveAllNewEffects
//
// THE ARMED CONFIGURATION IS SS_CBASEENTITY_ALIAS AND SOURCESHARP BOTH DEFINED
// and it is the only configuration this project builds, so not one line of any
// of those five regions was EVER COMPILED -- which is why the wave moved no
// symbol: 588 objects, identical DEFINED and UNDEFINED sets, 0 differing.
//
// THE ROWS STAY, AT ZERO. A file leaving kAllEdited is a claim nobody makes
// again; a row reading zero is a live assertion that fails the day a guard
// comes back into a file whose whole alias question is settled. The successor
// property -- "there are none, and their contents went with them" -- is
// asserted in TheResidueWaveTookTheDeadArmsWhole below, which is a STRONGER
// statement than the one the old rows made and is mutation-checked in both
// directions.
//-----------------------------------------------------------------------------
} // namespace


//=============================================================================
// CLAIM 2 FIRST, because it is the one that protects the measurement.
//
// PROVEN RED by rewriting collisionproperty.cpp's OnPreQuery guard as
// `#if defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )` — which is
// the spelling this shard actually wrote before catching it. The test named the
// file and the line.
//=============================================================================
//=============================================================================
// THE SUCCESSOR PROPERTY, and it is stronger than the rows it replaces.
//
// "Every entity ask in this file is inside the alias arm" is true of a file
// with no asks and no arms, so it passes VACUOUSLY now. What is actually true
// of collisionproperty.cpp and particlemgr.cpp after the `residue` wave is
// "THERE ARE NONE, AND THE BODIES WENT WITH THE GUARDS", and that is what this
// asserts -- each absence paired with a POSITIVE anchor out of the same file,
// so none of it can be green because the file failed to open.
//=============================================================================
//=============================================================================
// THE SAME CLAIM FOR THE THREE FILES `the-close` 1/N (f288a5e11) CLEARED.
//
// Written as its own fact rather than folded into the one above, because the
// two waves are two events: `residue` owned the stock tree and this one owned
// src/sourcesharp/**, and a single fact covering both could not say which wave
// a regression came from. Same shape either way -- every absence paired with a
// POSITIVE anchor out of the same file, so none of it can be green because the
// file failed to open.
//=============================================================================
//=============================================================================
// CLAIM 1 — the sub-object arms, and what must stay OUTSIDE them.
//
// PROVEN RED by moving collisionproperty.cpp's `#ifndef` above the
// UpdateDirtySpatialPartitionEntities definition: the first EXPECT below went
// red naming it, while the whole rest of the suite stayed green and the build
// stayed clean — which is the point, because that move is a link failure at
// step 6 and nothing else in this tree says so.
//=============================================================================
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. There are no SDK arms in this file any
// more, so "every SDK arm is guarded" is no longer a sentence about anything;
// the name is kept for identity, and what it checks is the successor.
//
// WHAT IT USED TO SAY: SourceSharpEntity_t::pBase is NULL for the life of an
// armed process, so every `pBase->` in this file must sit inside a `#ifndef
// SS_CBASEENTITY_ALIAS` arm -- counted line by line, twelve of them, rather
// than spot-checked, so a single unguarded one was a failure.
//
// WHAT HAPPENED: `the-close` 1/N (f288a5e11) deleted all twelve arms, and
// `the-close` 4/N established WHY they were safe to delete beyond the usual
// never-compiled argument: `IServerUnknown::GetBaseEntity()` is
// `mov $0x0,%eax; ret` in server.so, all eight implementations across the two
// DLLs, so pBase was a constant NULL and every one of the twelve was
// unreachable rather than merely unarmed.
//
// WHY THE SUCCESSOR IS STRONGER. The old rule could only catch a `pBase->`
// somebody wrote OUTSIDE an arm; it licensed any number of them inside one.
// The rule now is that the file contains none at all, in any arm, under any
// spelling -- which is a smaller set of permitted programs, and the clause that
// used to need a floor of ten to prove the scan was still matching needs no
// floor at all, because the presence half is asked of the successor instead.
//-----------------------------------------------------------------------------
//=============================================================================
// CLAIM 3 — the two arms that COPY an answer living somewhere else.
//
// PROVEN RED: the first by rewriting the server IsWorldEntity's armed arm as
// `handle.GetEntryIndex() == 1`, the second by putting `pModel` back into
// ClipRayToEntity. Each named its own file.
//=============================================================================
//=============================================================================
// The remaining per-site answers, each of which is a CHOICE that reads as an
// accident once the comment beside it is gone.
//=============================================================================

//=============================================================================
// THE FIXTURES — the scanner, checked against input whose answer is known.
//
// Without these, every claim above is "a string was or was not found in a file
// this test may never have opened", and this tree has shipped that check more
// than forty times.
//=============================================================================
TEST( CppAliasShardT3Fixture, TheGuardWalkAnswersKnownInputCorrectly )
{
	const std::string strFixture =
		"always_a\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"sdk_a\n"
		"#else\n"
		"alias_a\n"
		"#endif\n"
		"always_b\n"
		"#ifdef CLIENT_DLL\n"
		"always_c\n"
		"	#ifndef SS_CBASEENTITY_ALIAS\n"
		"sdk_b\n"
		"	#endif\n"
		"always_d\n"
		"#endif\n"
		"always_e\n";

	std::vector< std::string > lines;
	std::vector< Arm_t > arms;
	WalkArms( strFixture, &lines, &arms );

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );
		if ( t.empty() || t[ 0 ] == '#' )
			continue;
		Arm_t expect = ARM_ALWAYS;
		if ( t.compare( 0, 4, "sdk_" ) == 0 )
			expect = ARM_SDK;
		else if ( t.compare( 0, 6, "alias_" ) == 0 )
			expect = ARM_ALIAS;
		EXPECT_EQ( expect, arms[ i ] ) << "line " << ( i + 1 ) << " (" << t
		                               << ") landed in the wrong arm.";
	}

	// And the walk does not simply answer ARM_SDK for everything.
	int nAlways = 0, nSdk = 0, nAlias = 0;
	for ( size_t i = 0; i < arms.size(); i++ )
	{
		if ( arms[ i ] == ARM_ALWAYS ) nAlways++;
		else if ( arms[ i ] == ARM_SDK ) nSdk++;
		else nAlias++;
	}
	EXPECT_GT( nAlways, 0 );
	EXPECT_GT( nSdk, 0 );
	EXPECT_GT( nAlias, 0 );
}

TEST( CppAliasShardT3Fixture, TheDefeatabilityTestRejectsTheCompoundSpelling )
{
	// The exact spellings, because this is the rule the negative control stands
	// on and "roughly matches" is not good enough.
	EXPECT_TRUE( IsDefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_TRUE( IsDefeatableGuardLine( "\t#ifndef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_TRUE( IsDefeatableGuardLine( "    #ifndef SS_CBASEENTITY_ALIAS  " ) );

	EXPECT_FALSE( IsDefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS // why" ) );
	EXPECT_FALSE( IsDefeatableGuardLine( "#if !defined( SS_CBASEENTITY_ALIAS )" ) );

	// ...and those same non-matches are reported as UNDEFEATABLE, which is what
	// the whole-tree scan above fires on.
	EXPECT_TRUE( IsUndefeatableGuardLine(
		"#if defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )" ) );
	EXPECT_TRUE( IsUndefeatableGuardLine( "#if !defined( SS_CBASEENTITY_ALIAS )" ) );
	EXPECT_TRUE( IsUndefeatableGuardLine( "#ifdef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_TRUE( IsUndefeatableGuardLine( "	#elif !defined( SS_CBASEENTITY_ALIAS )" ) );

	// The bare spelling is not undefeatable, and the alias header's own include
	// guard is not one of these at all.
	EXPECT_FALSE( IsUndefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_FALSE( IsUndefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS_H" ) );
	EXPECT_FALSE( IsUndefeatableGuardLine( "#ifdef SS_CBASEENTITY_ALIAS_H" ) );
	EXPECT_FALSE( IsUndefeatableGuardLine( "int x = SS_CBASEENTITY_ALIAS;" ) );
}

