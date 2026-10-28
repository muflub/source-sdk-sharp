//========= SourceSharp — an SDK-only animation entry point is a no-op ========//
//
// `SourceSharpEntity_t::Animating()` answers the SDK hierarchy's skeleton and
// NULL for everything else — `shell_standalone_base.cpp:511` is
// `pBase ? pBase->GetBaseAnimating() : NULL`, and `pBase` is NULL for every
// entity on the parallel hierarchy. `animating_projection_test.cpp` pins that
// NULL and pins the ban on writing the cast by hand.
//
// WHAT IT DOES NOT PIN, AND WHAT SHIPPED BECAUSE OF IT: an ABI entry point
// whose ONLY arm is the one `Animating()` answers. Such a function resolves the
// handle, gets NULL, returns, and says nothing — for the ordinary case in this
// mod rather than a rare one, because most of its entities are on the parallel
// hierarchy.
//
// `SourceSharp_EntitySetSequence` was exactly that. `ScriptedEntity.PlaySequence`
// (`SourceSharp.Abi/ScriptedEntity.cs:1738`) is
//
//     int sequence = ModelDescriptors.For( Handle ).LookupSequence( name );
//     if ( sequence < 0 ) return false;
//     Engine.EntitySetSequence( Handle, sequence );                 // SDK arm only
//     return true;
//
// so on a `[ScriptedEntity]` the lookup SUCCEEDED, the set did nothing, and the
// caller was told true. `PropDynamic.Play` then fires `OnAnimationBegun` and
// declines to print its own "model has no animation" warning, because as far as
// it can tell the animation started. A `prop_dynamic` with a `DefaultAnimation`
// or a `SetAnimation` input stands in its first frame, which is the symptom
// PropDynamic.cs:55's comment describes for the OTHER cause.
//
// plan_entity.md §7: "a behaviour that is refused is a ContractBaseline entry
// with a reason, never a silent no-op". This file is the shape check that says
// so at build time.
//
// THE SWEEP IS THE POINT, not the one function. Four of the five `.Animating()`
// sites in shell_scripted.cpp already fall through to
// `SourceSharp_StandaloneStudioHdrFor( ent.pStandalone, ... )`; one did not.
// A fact naming only that one would go green on the fix and never notice the
// sixth site somebody adds next.
//
// PROVEN RED. On the tree before the fix, EveryAnimationEntryPointAnswersBoth
// fails naming `SourceSharp_EntitySetSequence` at shell_scripted.cpp:1426.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

// An ABI entry point starts at column zero with a return type and the name.
// Deliberately NOT a brace-counting parser: the body we care about runs from one
// such line to the next, and a function that reaches `.Animating()` and never
// names the parallel hierarchy before the next entry point is the shape, however
// the braces inside it nest.
bool IsEntryPointStart( const std::string &line, std::string *pOutName )
{
	if ( line.empty() || line[ 0 ] == ' ' || line[ 0 ] == '\t' || line[ 0 ] == '#' )
		return false;

	const size_t nAt = line.find( "SourceSharp_" );
	if ( nAt == std::string::npos || nAt == 0 )
		return false;

	const size_t nOpen = line.find( '(', nAt );
	if ( nOpen == std::string::npos )
		return false;

	// A DECLARATION is not a definition, and shell_scripted.cpp carries several
	// at file scope. A definition's parameter list is followed by a body rather
	// than by a semicolon on the same line.
	if ( line.find( ';' ) != std::string::npos )
		return false;

	*pOutName = line.substr( nAt, nOpen - nAt );
	return true;
}

struct Entry
{
	std::string name;
	int         nLine;        // 1-based
	bool        bAsksSdkArm;
	bool        bAnswersParallel;
};

// A CALL ON the parallel hierarchy, not a MENTION of it.
//
// THIS DISTINCTION IS THE WHOLE TEST and it was got wrong first time. The rule
// read `line.find( "pStandalone" )`, which the arm's own GUARD satisfies:
//
//     if ( ent.pStandalone )
//         return;                      <- the arm deleted
//
// still contains the word, so deleting the body and keeping the `if` left the
// sweep green. The mutation harness caught it by mutating the SUBJECT rather
// than this file -- `a substring needle misses added text, so mutate by
// CHANGING as well as by deleting`.
//
// So the arm must be USED: `pStandalone->` calls a method on it and
// `pStandalone,` passes it to something (which is the shape the four worked
// examples in this file take -- `SourceSharp_StandaloneStudioHdrFor(
// ent.pStandalone, &studioHdr )`). `Block()` and `CStandaloneEntity` are the
// other two spellings of touching it. A bare `ent.pStandalone` inside an `if`
// is none of them.
bool NamesParallelArm( const std::string &line )
{
	return line.find( "pStandalone->" )    != std::string::npos
	    || line.find( "pStandalone," )     != std::string::npos
	    || line.find( "CStandaloneEntity" ) != std::string::npos
	    || line.find( "Block()" )          != std::string::npos;
}

// The parser proper, over lines that have already had their comment-only ones
// blanked. Split out from ScanEntryPoints so that the positive control can
// feed it TEXT: a control that names a function in the tree is a control the
// tree getting better makes vacuous, and this one has had to move twice for
// exactly that reason.
std::vector< Entry > ScanLines( const std::vector< std::string > &lines )
{
	std::vector< Entry > out;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		std::string name;
		if ( !IsEntryPointStart( lines[ i ], &name ) )
			continue;

		Entry e;
		e.name             = name;
		e.nLine            = (int)( i + 1 );
		e.bAsksSdkArm      = false;
		e.bAnswersParallel = false;

		for ( size_t j = i + 1; j < lines.size(); j++ )
		{
			std::string next;
			if ( IsEntryPointStart( lines[ j ], &next ) )
				break;

			if ( lines[ j ].find( ".Animating()" ) != std::string::npos )
				e.bAsksSdkArm = true;
			if ( NamesParallelArm( lines[ j ] ) )
				e.bAnswersParallel = true;
		}

		out.push_back( e );
	}

	return out;
}

// The same blanking CodeLines does, over a string. A comment-only line becomes
// an empty one rather than disappearing, so line numbers survive.
std::vector< Entry > ScanText( const std::string &szSource )
{
	std::vector< std::string > lines;
	std::string line;

	for ( size_t i = 0; i <= szSource.size(); i++ )
	{
		if ( i == szSource.size() || szSource[ i ] == '\n' )
		{
			const size_t nFirst = line.find_first_not_of( " \t" );

			if ( nFirst != std::string::npos && line.compare( nFirst, 2, "//" ) == 0 )
				lines.push_back( std::string() );
			else
				lines.push_back( line );

			line.clear();
			continue;
		}

		line += szSource[ i ];
	}

	return ScanLines( lines );
}

//=============================================================================//
// THE CHECKS THAT WOULD OTHERWISE NOT BE ABLE TO FAIL.
//
// Every fact below asserts something about a set read off disk. A rename, a
// move or an unexpected working directory empties that set, and an assertion
// over an empty set passes. These three make the emptiness the failure.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE PARSER'S POSITIVE CONTROL, and it does not read the tree.
//
// This control has now moved twice because its subject kept being fixed or
// deleted: it was SourceSharp_EntityLookupSequence, then
// SourceSharp_EntityLookupSequenceByActivity, and Phase 2d retired that one
// too. A control that names a function in the tree is a control that the tree
// getting better makes vacuous -- which is the opposite of what a control is
// for.
//
// So it feeds the parser text of its own. If the parser stops recognising an
// .Animating() arm or a parallel arm, THIS fails, whatever the tree contains.
//-----------------------------------------------------------------------------
TEST( AnimatingParallelArm, TheSweepSeesAnSdkArmWhenThereIsOneToSee )
{
	const std::string szSource =
		"int32_t SourceSharp_Fixture( uint32_t hEntity )\n"
		"{\n"
		"\tconst SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );\n"
		"\n"
		"\tif ( CBaseAnimating *pAnimating = ent.Animating() )\n"
		"\t\treturn pAnimating->GetSequence();\n"
		"\n"
		"\tif ( ent.pStandalone )\n"
		"\t\treturn ent.pStandalone->GetSequence();\n"
		"\n"
		"\treturn -1;\n"
		"}\n";

	const std::vector< Entry > entries = ScanText( szSource );

	ASSERT_EQ( 1u, entries.size() )
		<< "the entry-point parser did not find one definition in text that "
		   "has exactly one; every sweep over the tree is reading nothing.";

	EXPECT_EQ( "SourceSharp_Fixture", entries[ 0 ].name );

	EXPECT_TRUE( entries[ 0 ].bAsksSdkArm )
		<< "the parser no longer recognises an .Animating() arm, so the sweep's "
		   "silence over the tree means nothing.";

	EXPECT_TRUE( entries[ 0 ].bAnswersParallel )
		<< "the parser no longer recognises a parallel arm, so every entry "
		   "point would read as SDK-only and the sweep would fail on all of "
		   "them rather than none.";
}

TEST( AnimatingParallelArm, TheSweepNamesAnSdkOnlyEntryPoint )
{
	// The other direction: a function with the SDK arm and NO parallel arm is
	// exactly what the sweep exists to name, and the fixture proves it would.
	const std::string szSource =
		"int32_t SourceSharp_Fixture( uint32_t hEntity )\n"
		"{\n"
		"\tconst SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );\n"
		"\n"
		"\tif ( CBaseAnimating *pAnimating = ent.Animating() )\n"
		"\t\treturn pAnimating->GetSequence();\n"
		"\n"
		"\treturn -1;\n"
		"}\n";

	const std::vector< Entry > entries = ScanText( szSource );

	ASSERT_EQ( 1u, entries.size() );
	EXPECT_TRUE( entries[ 0 ].bAsksSdkArm );
	EXPECT_FALSE( entries[ 0 ].bAnswersParallel )
		<< "the parser reports a parallel arm in a function that has none, so "
		   "the sweep can never name anything.";
}

//=============================================================================//
// THE SWEEP.
//=============================================================================//

//=============================================================================//
// THE ONE THAT SHIPPED, named so a reader can find the bug from the test.
//=============================================================================//

//=============================================================================//
// THE TWO WORKED EXAMPLES ARE RETIRED WITH THEIR SUBJECTS -- plan_shim.md 2d.
//
// LookupSequenceIsTheWorkedExampleOfBothArms asserted that
// SourceSharp_EntityLookupSequence had lost its SDK arm and KEPT its parallel
// one -- the half the-close 1/N's deletion could have silently taken with it.
// LookupSequenceByActivityIsTheWorkedExampleOfBothArms was the parser's
// positive control and named the one function that still had both.
//
// Both subjects are RETIRED ABI slots now. The questions they answered --
// which sequence a label resolves to, which sequence an activity resolves to
// -- are one ModelDescriptor crossing and one managed rule, which answers for
// both hierarchies at once because it reads the MODEL rather than an entity.
// A fact that scans a file for a function that is not there is not a weaker
// claim about the build; it is no claim at all.
//
// WHAT EACH CLAIM BECAME, so neither is simply dropped:
//
//   * "it still answers the parallel hierarchy" is now structural rather than
//     asserted: there is no SDK arm to lose, because there is no entity type
//     in the crossing at all. ModelDescriptorTests pins what the rule answers.
//
//   * "the client and the server resolve the same index" was NOT what these
//     facts checked, and is the defect Phase 2d found: the two native bodies
//     DISAGREED, the client's falling through to a weighted random pick. One
//     managed body is the answer, and ModelDescriptorTests'
//     LookupSequenceDoesNotFallThroughToAnActivity is where that now lives.
//
//   * the parser's positive control moved UP, to
//     TheSweepSeesAnSdkArmWhenThereIsOneToSee, and out of the tree entirely --
//     it feeds the parser text of its own, so the tree getting better cannot
//     make it vacuous again. It had already moved once for that reason.
//=============================================================================//

} // namespace
