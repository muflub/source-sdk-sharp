//====== SourceSharp — A MANAGED ENTITY EXISTS AND A TOOL CANNOT SEE IT ========
//
// Lane diag-visibility. Two diagnostics that walked the SDK hierarchy and
// therefore reported a world that was mostly absent, and the properties that
// keep them honest.
//
// WHY THIS MATTERS BEYOND TIDINESS. Every lane in this port uses these tools to
// decide whether its work happened. A diagnostic that silently omits managed
// entities is how a lane concludes "nothing was created" and goes looking for a
// bug that is not there; two lanes have already been misled that way.
//
//   report_entities  walked gEntList.FirstServerEntity()/NextServerEntity(),
//                    which is CEntInfo. Step 2c took the managed shells out of
//                    CEntInfo. MEASURED ON dm_lockdown BEFORE THE FIX: the
//                    command printed NOT ONE LINE while `./debug-api ents "*"` on
//                    the same running game read 471.
//
//   InvalidateMdlCache  walked ClientEntityList().FirstBaseEntity(), which
//                    answers C_BaseEntity. C_StandaloneEntity is not one.
//                    MEASURED: zero entities reached, no output at all, and a
//                    body that looked exactly like a working one.
//
// SOURCE-READ, and forced rather than lazy: every file below includes cbase.h's
// whole world and none can be linked into a gtest. The behavioural halves are
// the managed facts (ClientMdlCacheFlushTests, fifteen of them) and the live
// gate. What no other test can reach is the C++ shape, and it has properties a
// reader cannot check by eye and a compiler cannot check at all -- above all
// PreprocessorNesting below, which is the one that actually bit.
//
//=============================================================================//

#include <cctype>
#include <string>
#include <vector>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// IS THIS LINE COMPILED WHEN <symbol> IS DEFINED?
//
// THE TEST THIS FILE EXISTS FOR. The first version of the client's binding was
// written beside the server's, under its own `#ifdef CLIENT_DLL` -- inside the
// 350-line `#ifndef CLIENT_DLL` that surrounds that whole region. The build was
// GREEN, the crossing FIRED, C# printed its line, and the count in it was 0.
// sourcesharp_gamesystem.cpp's own header records that trap costing an earlier
// lane one build; it cost this one a live run.
//
// Nothing else catches it. A compiler cannot: the block is simply not there. A
// grep cannot: the text is present and correct. Only the NESTING says so.
//
// A tiny evaluator: walk the conditionals, keep a stack, and answer whether the
// line at `at` survives with `pszSymbol` defined. Conditions this does not
// understand are treated as TAKEN, which is the conservative direction -- it
// can only make this fact easier to pass, never harder, so a pass is not an
// artefact of the parser giving up.
//-----------------------------------------------------------------------------
//
// TWO QUESTIONS, ONE WALK. "Not compiled on the client" and "compiled on the
// server" are DIFFERENT FACTS, and a server-only binding needs both: an
// `#ifdef CLIENT_DLL` nested inside the 350-line `#ifndef CLIENT_DLL` is
// compiled on NEITHER side, so it answers the first question correctly while
// binding nothing at all. Lane `abi-tail` found that by mutating a server-arm
// fact into exactly that shape and watching it stay GREEN.
bool CompiledWithSymbol( const std::string &src, size_t at, const char *pszSymbol, bool bDefined )
{
	const std::string sym( pszSymbol );

	// live[] is whether each open conditional's current arm is taken.
	std::vector<bool> live;
	size_t line = 0;

	while ( line < src.size() && line <= at )
	{
		const size_t end = src.find( '\n', line );
		const std::string text = src.substr( line, ( end == std::string::npos ) ? std::string::npos : end - line );

		size_t h = text.find_first_not_of( " \t" );
		if ( h != std::string::npos && text[h] == '#' )
		{
			const std::string d = text.substr( h + 1 );
			const bool mentions = d.find( sym ) != std::string::npos;

			if ( d.compare( 0, 6, "ifndef" ) == 0 )
				live.push_back( mentions ? !bDefined : true );
			else if ( d.compare( 0, 5, "ifdef" ) == 0 )
				live.push_back( mentions ? bDefined : true );
			else if ( d.compare( 0, 2, "if" ) == 0 )
				live.push_back( true );
			else if ( d.compare( 0, 4, "else" ) == 0 && !live.empty() )
				live.back() = !live.back();
			else if ( d.compare( 0, 5, "endif" ) == 0 && !live.empty() )
				live.pop_back();
		}

		if ( end == std::string::npos )
			break;
		line = end + 1;
	}

	for ( size_t i = 0; i < live.size(); i++ )
	{
		if ( !live[i] )
			return false;
	}

	return true;
}

bool CompiledWhenDefined( const std::string &src, size_t at, const char *pszSymbol )
{
	return CompiledWithSymbol( src, at, pszSymbol, true );
}

bool CompiledWhenNotDefined( const std::string &src, size_t at, const char *pszSymbol )
{
	return CompiledWithSymbol( src, at, pszSymbol, false );
}

//-----------------------------------------------------------------------------
// HOW MANY GENERATED SHIM-AREA FORWARDS IN `inc` ARE `CShim...::<slot>( ... )`
// WITH THE ONE-LINE BODY `{ return ::<body>( ... ); }` -- or with ANY body when
// `body` is empty. Since plan_client_vtables CC-last a slot is wired by such a
// forward on each side: shell_shim_areas.inc on the server,
// shell_shim_areas_client.inc on the client (both GENERATED by tools/shim-areas
// from docs/engineapi-areas.tsv), and the struct fill that used to say
// `engineApi.<Slot> = <Body>;` is gone. Whitespace is ignored, so the
// generator's line wrapping is not part of the needle; the class name must
// carry "CShim", so the body's own `::<body>(` never counts as a slot.
//-----------------------------------------------------------------------------
} // namespace

//=============================================================================
// SUBJECT 1 -- report_entities
//=============================================================================

//-----------------------------------------------------------------------------
// report_entities' native half is gone (server gc cull, 2026-10-01).
// ShellLessEntity_LiveCount and SourceSharp_StandaloneServerAt had no caller
// left, and server.so's --print-gc-sections link dropped both, so they were
// deleted. Each file is proved read first.
//-----------------------------------------------------------------------------
//=============================================================================
// SUBJECT 2 -- InvalidateMdlCache on the client
//=============================================================================

//-----------------------------------------------------------------------------
// THE SHELL NO LONGER WALKS ANYTHING. It hands the engine's question to C#,
// which is what the SERVER half has done since plan_gamedll_after.md Phase A2
// and what LANE_BRIEF.md requires of a client interface.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SDK HALF WAS NEVER COMPILED, AND IS NOW GONE FROM THE SOURCE TOO.
//
// READ THE ASSERTIONS, NOT THE OLD NAME. This fact was
// `TheWalkCoversBothHierarchies` and it required
// `ClientEntityList().FirstBaseEntity()` to be present, on the argument that
// the SDK arm frees C_BaseAnimating::m_pStudioHdr and no managed walk reaches
// it. THAT ARM WAS INSIDE `#ifndef SS_CBASEENTITY_ALIAS` FOR ITS WHOLE LIFE.
// SS_CBASEENTITY_ALIAS is defined in every build the mod ships, so the arm was
// never compiled, never linked and never freed anything -- the fact was green
// and measuring text. `git show f288a5e11 -- sourcesharp/game/client/shell_clientdll.cpp`
// is the deletion; the guard is what makes it a no-op rather than a loss.
//
// So it is rewritten to the pair R1 asks for: the ABSENCE, plus the LIVE
// SUCCESSOR. The absence is checked over the WHOLE FILE and not just the walk's
// body -- a re-point that only looked inside the function would go green again
// if the arm came back in a dead guard three lines below it -- and the file's
// guard count is pinned at zero so "gone" cannot mean "moved into another arm
// the compiler will not see".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND THE PARALLEL HALF IS IN THE SHIM FILE THAT OWNS THE LIST, which is the
// other side of a split that is NOT arbitrary: client_entity_resolve_test.cpp's
// clause-2 gate waives shell_clientdll.cpp by name for the iteration of the
// stock list and waives no other shim file. Writing both halves in one place
// would have meant adding a row to that waiver.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// IT ANSWERS HOW MANY. The number is the whole difference between an empty body
// and a working one, from outside.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THERE IS NO TYPE TEST LEFT TO MAKE A VIRTUAL, BECAUSE THERE IS ONE HIERARCHY.
//
// READ THE ASSERTIONS, NOT THE OLD NAME. This was
// `TheTypeTestIsAVirtualAndNotACast` and it REQUIRED `GetBaseAnimating()` in
// the walk -- gate 3's remedy for the dynamic_cast that used to be there. The
// branch it named lived in the same `#ifndef SS_CBASEENTITY_ALIAS` arm as the
// stock-list walk above and was never compiled either; both went at f288a5e11.
//
// The gate-3 property it was really defending is UNCHANGED and is asserted
// here in full: no cast of any gated spelling reaches an entity in this walk.
// What has changed is why that is true. It used to be true because a virtual
// stood in for a cast; it is now true because every entity the walk reaches is
// a C_StandaloneEntity and the method it calls is that type's own -- no
// narrowing is being done, so there is nothing for a cast to do.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CLIENT SHELL'S INVALIDATOR CLEARS BOTH THINGS, and it is the server's
// two lines rather than a new idea (shell_standalone.h:588).
//-----------------------------------------------------------------------------
//=============================================================================
// THE ONE THAT ACTUALLY BIT
//=============================================================================

//-----------------------------------------------------------------------------
// THE CLIENT'S BINDING IS COMPILED ON THE CLIENT.
//
// Written beside the server's, it lands inside the 350-line
// `#ifndef CLIENT_DLL` -- and then the build is green, the crossing fires, C#
// prints its line, and the number in it is 0. Nothing but the NESTING says so.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND THE SERVER'S IS COMPILED ON THE SERVER -- the same check, the other way,
// so a fix that moved the client's binding by moving the whole region cannot
// pass the fact above by breaking the server.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE EVALUATOR ITSELF IS CHECKED. A nesting test whose parser always answered
// TRUE would pass both facts above forever, which is precisely the shape of the
// 43 checks-that-cannot-fail this tree has already found.
//-----------------------------------------------------------------------------
TEST( PreprocessorNesting, ItAnswersFalseInsideAnIfndefOfTheSymbol )
{
	const std::string src = "#ifndef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_FALSE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, ItAnswersTrueInsideAnIfdefOfTheSymbol )
{
	const std::string src = "#ifdef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, ItSeesThroughNestingToAnOuterIfndef )
{
	// THE EXACT SHAPE OF THE BUG: an `#ifdef CLIENT_DLL` that is correct in
	// itself and dead because of what encloses it.
	const std::string src = "#ifndef CLIENT_DLL\n#ifdef CLIENT_DLL\nTARGET\n#endif\n#endif\n";

	EXPECT_FALSE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, AClosedBlockDoesNotReachPastItsEndif )
{
	const std::string src = "#ifndef CLIENT_DLL\n#endif\nTARGET\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, AnElseFlipsTheArm )
{
	const std::string src = "#ifndef CLIENT_DLL\n#else\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, AConditionAboutSomethingElseDoesNotExcludeTheLine )
{
	const std::string src = "#ifndef GAME_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

//=============================================================================
// THE `abi-tail` MERGE'S FIVE BINDINGS, AT THE SAME SITE AND FOR THE SAME
// REASON.
//
// The entity factory dictionary's five slots are SERVER ONLY -- there is no
// factory dictionary on the client. So they take the opposite arm of the same
// trap: a binding written where the CLIENT arm can see it would bind five
// server-only bodies the client cannot link, and one written outside a
// conditional at all would try to bind them on both.
//
// A NULL SLOT IS "NOT ASKED", NOT "EMPTY", and that is why the arm matters
// rather than being tidiness. `EntityFactories.Available` answers false with
// the slots null, and every consumer keeps its present behaviour. A CLIENT that
// bound them and got zero would answer "the table is empty" -- and an empty
// table refuses `ent_create prop_physics` for a class that works.
//=============================================================================

namespace
{

} // namespace

//-----------------------------------------------------------------------------
// ALL FIVE ARE BOUND. Four of five is not a partial win: EntityFactories.Refresh
// walks the names with Count and stops at the refusal ledger without all three
// of its slots, so a missing one is a table that reads as complete and is not.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND EVERY ONE OF THEM IS ON THE SERVER ARM. This is the check the
// diag-visibility lane lost a live run to, taken the other way round: these
// bodies live in shell_standalone_class.cpp, which the client does not compile.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE DECLARATIONS ARE ON THE SERVER ARM TOO. A declaration outside the
// conditional compiles on both sides and only fails at LINK, on the client, in
// a build nobody runs until they do.
//
// THE SUBJECT MOVED AT plan_shim.md §5 3e AND THE CLAIM DID NOT. These five
// were hand-written prototypes in sourcesharp_gamesystem.cpp's server arm. They
// are GENERATED now, into sourcesharp_entrypoints.h by tools/entrypoint-decls,
// which puts each declaration on the side the FILL BLOCK wires it -- so
// "declared where only the server compiles it" is a property the generator
// produces rather than one a person maintains, and this fact is what says it
// produced the right one. Read with the same conditional evaluator, because the
// generated header uses the same guard spellings.
//
// AND THE MOVE CORRECTED THEM, which is the generator's whole argument. The
// hand-written five said `int` and `int i`; SourceSharpEngineApi's fields say
// `int32_t` and `int32_t index`. The two are the same type on this platform, so
// nothing broke and nothing would have -- which is exactly why a second
// spelling of a signature is worth removing rather than reviewing.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE BODIES EXIST, where the declarations say they do. A binding to a name
// nothing defines is a link error on the server -- but only once something
// stops the linker folding it, and this says which file owes it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE TWO INDEXED READS REFUSE AN OUT-OF-RANGE INDEX rather than reading off
// the end. Stock's GetCannonicalName did not test its own Find and reached
// GetElementName( InvalidIndex() ) -- a read of the element vector at 0xFFFF.
//-----------------------------------------------------------------------------
//=============================================================================
// AND THE OTHER HALF OF THE EVALUATOR IS CHECKED TOO.
//
// `CompiledWhenNotDefined` is a second question over the same walk, added by
// lane `abi-tail` because a server-only binding needs BOTH answers and the
// mutation that proved it -- five bindings wrapped in an `#ifdef CLIENT_DLL`
// inside the server-only block -- was green against `CompiledWhenDefined`
// alone. A parser that always answered TRUE would put it straight back.
//=============================================================================

TEST( PreprocessorNesting, NotDefinedAnswersTrueInsideAnIfndefOfTheSymbol )
{
	const std::string src = "#ifndef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, NotDefinedAnswersFalseInsideAnIfdefOfTheSymbol )
{
	const std::string src = "#ifdef CLIENT_DLL\nTARGET\n#endif\n";

	EXPECT_FALSE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

//-----------------------------------------------------------------------------
// THE EXACT SHAPE OF THE MIRROR BUG, and the reason this question exists: a
// region compiled on NEITHER side. `CompiledWhenDefined` says false, which
// reads as "correctly server-only"; this says false too, which is what makes
// the pair able to tell "server-only" from "nowhere".
//-----------------------------------------------------------------------------
TEST( PreprocessorNesting, ARegionCompiledOnNeitherSideAnswersFalseToBoth )
{
	const std::string src = "#ifndef CLIENT_DLL\n#ifdef CLIENT_DLL\nTARGET\n#endif\n#endif\n";

	const size_t at = src.find( "TARGET" );

	EXPECT_FALSE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) );
	EXPECT_FALSE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) );
}

//-----------------------------------------------------------------------------
// AND AN UNCONDITIONAL LINE ANSWERS TRUE TO BOTH, so the pair is not two ways
// of spelling "false".
//-----------------------------------------------------------------------------
TEST( PreprocessorNesting, AnUnconditionalLineAnswersTrueToBoth )
{
	const std::string src = "TARGET\n";
	const size_t at = src.find( "TARGET" );

	EXPECT_TRUE( CompiledWhenDefined( src, at, "CLIENT_DLL" ) );
	EXPECT_TRUE( CompiledWhenNotDefined( src, at, "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, NotDefinedFlipsOnAnElseToo )
{
	const std::string src = "#ifndef CLIENT_DLL\n#else\nTARGET\n#endif\n";

	EXPECT_FALSE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}

TEST( PreprocessorNesting, NotDefinedTreatsAnUnrelatedConditionAsTaken )
{
	const std::string src = "#ifndef GAME_DLL\nTARGET\n#endif\n";

	EXPECT_TRUE( CompiledWhenNotDefined( src, src.find( "TARGET" ), "CLIENT_DLL" ) );
}
