//========= SourceSharp — the trace filter library, and its two link lines ====//
//
// plan_entity.md Phase 4, step 4e (Utilities). The step's gate is the shim's
// cross-edge count into game/shared/util_shared.cpp and the trace ABI facts,
// and what this file pins is the MOVE that produced both: the shared
// trace-filter family and the ConVar the trace inlines read left stock's
// util_shared.cpp for sourcesharp/game/shared/trace_filter_library.cpp.
//
// WHY A MOVE NEEDS A TEST OF ITS OWN. What each filter DOES is pinned already,
// by engine_core_retype_test's SharedTraceFilters section and by
// cpp_alias_t5_test's guard tables; both were repointed at the new address with
// this change and neither would notice the failure this file is about. Three
// failures, all of which compile and two of which link:
//
//   1. A COPY LEFT BEHIND. Two definitions of one filter, one in each file,
//      and the linker picks stock's for stock's callers -- so the shim's arms
//      are what the tests read and stock's are what the game runs. The
//      repointed tests pass; the game uses the other body.
//
//   2. A ONE-SIDED SOURCE ROW. This file is compiled into server.so AND
//      client.so, and util_shared.cpp was compiled into both too. A row added to
//      only one of the two source lists gives that side two definitions and the other
//      side none -- and the side with none fails at LINK, which is loud, while
//      the side with two silently keeps stock's. The project has a recorded
//      lesson exactly here: a shared file read on one side compiles green and
//      breaks the other at a later step.
//
//   3. THE ConVar AND THE FILTER SPLIT UP. util_shared.h's UTIL_TraceLine,
//      UTIL_TraceHull and UTIL_TraceRay are INLINE: each constructs
//      CTraceFilterSimple and reads r_visualizetraces, so every translation
//      unit that traces at all references both symbols. Moving one and leaving
//      the other puts the cross-edge straight back where it was, and nothing
//      about the build looks different.
//
// The declarations are deliberately NOT moved -- they stay in
// game/shared/util_shared.h -- so that no caller on either side had to change.
// That is asserted too, because a move that quietly rewrote a hundred call
// sites would be a different change wearing this one's commit message.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <algorithm>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// WHETHER THE PATH IS THERE AT ALL, which ReadFile above cannot answer: it
// EXPECTs the open, so asking it about a file that is supposed to be gone fails
// on the open instead of reporting the absence. Added at plan_shim.md Phase 2c,
// lane 2-utilshared, where game/shared/util_shared.cpp stopped existing and
// several facts here changed from "this needle is not in that file" to "that
// file is not in the tree" -- the stronger claim, and the one that cannot be
// satisfied vacuously.
bool FileExists( const char *pPath )
{
	std::ifstream f( pPath );
	return f.good();
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *const kLibrary   = "sourcesharp/game/shared/trace_filter_library.cpp";
const char *const kStatics   = "sourcesharp/game/shared/entity_shared_statics.cpp";
const char *const kUtilShrC  = "game/shared/util_shared.cpp";
// Phase 2c, lane 2-utilshared: util_shared.cpp is deleted and its residue
// went here, so this is the one file a second r_visualizetraces could
// plausibly land in.
const char *const kUtilSharedLib = "sourcesharp/game/shared/util_shared_library.cpp";
const char *const kUtilShrH  = "game/shared/util_shared.h";
// What each side compiles is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt), farm-relative paths.
const char *const kLibrarySource = "sourcesharp/game/shared/trace_filter_library.cpp";
const char *const kStaticsSource = "sourcesharp/game/shared/entity_shared_statics.cpp";

// Compiled into the target, and listed EXACTLY ONCE in its CMakeLists (the old
// vpc check was a count of 1; a doubled row is a list somebody edited twice).
void ExpectCompiledOnce( const char *pszTarget, const char *pszSource )
{
	EXPECT_TRUE( cmake_graph::Compiles( pszTarget, pszSource ) )
		<< pszTarget << " (" << cmake_graph::ListFile( pszTarget ) << ") does not compile "
		<< pszSource;
	const std::vector< std::string > listed = cmake_graph::ListedSources( pszTarget );
	EXPECT_EQ( 1, (int)std::count( listed.begin(), listed.end(), std::string( pszSource ) ) )
		<< pszSource << " is not listed exactly once in " << cmake_graph::ListFile( pszTarget );
}

// The out-of-line definitions that moved. Spelled as they appear at the start of
// a definition, so that the DECLARATION in util_shared.h -- which stays -- does
// not match: a declaration ends in `;` and carries no `::`.
const char *const kMovedDefinitions[] = {
	"bool PassServerEntityFilter(",
	"bool StandardFilterRules(",
	// One body for both sides since P5: SS_SIMPLE_FILTER is util_shared.h's
	// CTraceFilterSimple on the client and server.so's ss::CTraceFilterSimple
	// on the server (TheSimpleFilterIsOneBodyNamedPerSide).
	"\nSS_SIMPLE_FILTER::CTraceFilterSimple(",
	"bool SS_SIMPLE_FILTER::ShouldHitEntity(",
};
const size_t kNumMoved = sizeof( kMovedDefinitions ) / sizeof( kMovedDefinitions[ 0 ] );

//-----------------------------------------------------------------------------
// THE OTHER NINE FILTERS' DEFINITIONS ARE GONE -- plan_shim.md §5 3f, lane 3f.
//
// This list used to hold sixteen definitions and asserted each was present
// exactly once. Twelve of those sixteen belonged to nine classes with no caller
// anywhere on either link line, and they are deleted.
//
// THE ROWS ARE REWRITTEN RATHER THAN DROPPED, and the claim they make now is
// strictly stronger than the one they made before. "This definition is here
// exactly once" is satisfied by a file nobody links; the pair below is
//
//   (a) the definition is in NEITHER of the two files that could hold it --
//       not the shim library, and not the stock file, whose absence is itself
//       asserted rather than assumed; and
//   (b) the DECLARATION is still in util_shared.h.
//
// (b) is what stops (a) degenerating into "everything is gone, so everything
// passes" -- if somebody deletes the declarations too, every (a) still holds
// and (b) fails. It is also the substantive claim: the declarations stay
// because files this build does not compile (ai_basenpc.cpp, beam_shared.cpp,
// the nav mesh, the tf tree) still name these classes, and a lane porting one
// of them wants them to still be there.
//
// HOW THE DEADNESS WAS ESTABLISHED, because "the census said 0" was not enough
// here and plan_shim.md §7 says so: a per-function census sees only out-of-line
// symbols, so a filter constructed inline from a header reads as unused when it
// is not. `nm -C` over the objects the GRAPH names on both link lines finds no
// object but this one's own that emits OR REFERENCES `vtable for CTraceFilter*`
// for any of the nine -- undefined (U) rows included, which are the rows an
// inline construction would have left.
//-----------------------------------------------------------------------------
const char *const kDeletedDefinitions[] = {
	"bool CTraceFilterOnlyNPCsAndPlayer::ShouldHitEntity(",
	"bool CTraceFilterNoNPCsOrPlayer::ShouldHitEntity(",
	"CTraceFilterSkipTwoEntities::CTraceFilterSkipTwoEntities(",
	"bool CTraceFilterSkipTwoEntities::ShouldHitEntity(",
	"CTraceFilterSimpleList::CTraceFilterSimpleList(",
	"bool CTraceFilterSimpleList::ShouldHitEntity(",
	"CTraceFilterLOS::CTraceFilterLOS(",
	"bool CTraceFilterLOS::ShouldHitEntity(",
	"bool CTraceFilterSkipClassname::ShouldHitEntity(",
	"bool CTraceFilterSkipTwoClassnames::ShouldHitEntity(",
	"bool CTraceFilterSimpleClassnameList::ShouldHitEntity(",
	"bool CTraceFilterChain::ShouldHitEntity(",
};
const size_t kNumDeleted = sizeof( kDeletedDefinitions ) / sizeof( kDeletedDefinitions[ 0 ] );

// The declaration half of the pair: every class whose definition went, spelled
// as util_shared.h spells it.
const char *const kDeletedButStillDeclared[] = {
	"class CTraceFilterSkipTwoEntities : public CTraceFilterSimple",
	"class CTraceFilterSimpleList : public CTraceFilterSimple",
	"class CTraceFilterOnlyNPCsAndPlayer : public CTraceFilterSimple",
	"class CTraceFilterNoNPCsOrPlayer : public CTraceFilterSimple",
	"class CTraceFilterLOS : public CTraceFilterSkipTwoEntities",
	"class CTraceFilterSkipClassname : public CTraceFilterSimple",
	"class CTraceFilterSkipTwoClassnames : public CTraceFilterSkipClassname",
	"class CTraceFilterSimpleClassnameList : public CTraceFilterSimple",
	"class CTraceFilterChain : public CTraceFilter",
};
const size_t kNumDeletedDeclared =
	sizeof( kDeletedButStillDeclared ) / sizeof( kDeletedButStillDeclared[ 0 ] );

// What util_shared.h must still declare, so that no caller changed.
const char *const kStillDeclared[] = {
	"bool PassServerEntityFilter( const IHandleEntity *pTouch, const IHandleEntity *pPass );",
	"bool StandardFilterRules( IHandleEntity *pHandleEntity, int fContentsMask );",
	"class CTraceFilterSimple : public CTraceFilter",
	"class CTraceFilterSkipTwoEntities : public CTraceFilterSimple",
	"class CTraceFilterLOS : public CTraceFilterSkipTwoEntities",
	"class CTraceFilterChain : public CTraceFilter",
	"extern ConVar r_visualizetraces;",
};
const size_t kNumDeclared = sizeof( kStillDeclared ) / sizeof( kStillDeclared[ 0 ] );

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

TEST( TraceFilterLibrary, EveryMovedDefinitionIsInTheShimFile )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not defined exactly once in "
			<< kLibrary;
	}
}

TEST( TraceFilterLibrary, TheNineDeadFiltersAreDefinedInNeitherFile )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load -- without this "
		<< "every absence below is satisfied by a file that is not there";

	// The only other file that could hold them is stock's, and it does not
	// exist. Asserted, not assumed: an absence over a missing file is vacuous
	// and this test would otherwise be two of those.
	EXPECT_FALSE( FileExists( kUtilShrC ) )
		<< kUtilShrC << " exists again and could define them a second time.";

	for ( size_t i = 0; i < kNumDeleted; i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, kDeletedDefinitions[ i ] ) )
			<< kDeletedDefinitions[ i ] << " is back in " << kLibrary
			<< ". Nine filter classes have no caller on either link line; if "
			   "one has grown a caller, say so in the plan rather than here.";
	}
}

TEST( TraceFilterLibrary, TheNineDeadFiltersAreStillDeclaredForUnportedCallers )
{
	const std::string h = ReadFile( kUtilShrH );
	ASSERT_FALSE( h.empty() ) << kUtilShrH << " did not load";

	// This is the half that stops the absence above degenerating. Delete the
	// declarations as well and every EXPECT_EQ( 0u, ... ) still passes while
	// this one fails.
	for ( size_t i = 0; i < kNumDeletedDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( h, kDeletedButStillDeclared[ i ] ) )
			<< "util_shared.h no longer declares: "
			<< kDeletedButStillDeclared[ i ]
			<< ". Files this build does not compile still name it.";
	}
}

// game/shared/util_shared.cpp DOES NOT EXIST. plan_shim.md Phase 2c, lane
// 2-utilshared, deleted it: 1,031 lines had zero importers on either link line,
// and the two definitions that did not (`hl2_episodic` and `UTIL_BloodDrips`)
// moved into sourcesharp/game/shared/util_shared_library.cpp, which is the file
// this test already reads. Both vpc rows went with it.
//
// ss::CTraceFilterSimple (P5 lane E's server spelling of the same filter) is
// gone with the server's native trace filter; one body of rules is left.
TEST( TraceFilterLibrary, TheSimpleFilterIsOneBodyAndTheClientsOnly )
{
	// plan_server_native_v2 T4f: the server's half (ss::CTraceFilterSimple, its
	// shape clause and ownership rule) went to managed TraceFilterRules, and the
	// one body left is the client's, under util_shared.h's name.
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	EXPECT_EQ( 1u, CountOf( lib, "#define SS_SIMPLE_FILTER CTraceFilterSimple\n" ) )
		<< "the client no longer compiles the simple filter as util_shared.h's class";
	EXPECT_EQ( 0u, CountOf( lib, "#define SS_SIMPLE_FILTER ss::CTraceFilterSimple" ) )
		<< "the server's native filter is back; its rule is managed's (TraceFilterRules)";

	const size_t at = lib.find( "bool SS_SIMPLE_FILTER::ShouldHitEntity(" );
	ASSERT_NE( std::string::npos, at );
	const std::string body = lib.substr( at, lib.find( "\n}\n", at ) - at );
	EXPECT_EQ( 1u, CountOf( body, "if ( !StandardFilterRules( pHandleEntity, contentsMask ) )" ) )
		<< "the one body no longer applies the standard rules itself";
	EXPECT_EQ( 0u, CountOf( lib, "::CTraceFilterSimple legacy(" ) )
		<< "a forwarding second filter is back; the rules exist once";
}

// AN ABSENCE OVER A FILE THAT IS NOT THERE IS VACUOUS, which is what every
// "did not load" assertion in this file exists to refuse -- so the absence is
// asserted as an absence of the FILE, and the successor file is proved read in
// the same breath.
TEST( TraceFilterLibrary, TheStockFileIsGoneSoNothingCanBeLeftBehindInIt )
{
	EXPECT_FALSE( FileExists( kUtilShrC ) )
		<< kUtilShrC << " exists again. Two bodies for one filter link, and the "
		<< "one the game runs is not the one the other tests read.";

	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load -- without this "
		<< "the absence above is satisfied by two missing files rather than one";
	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not in " << kLibrary << " either.";
	}
}

//=============================================================================//
// 2. BOTH LINK LINES COMPILE IT
//
// The shared-file lesson: a change to a file both sides compile that is checked
// on one side only compiles green there and breaks the other later.
//=============================================================================//

TEST( TraceFilterLibrary, TheServerNoLongerCompilesIt )
{
	// plan_server_native_v2 T4f: every server trace runs the managed rule over
	// libss_fwd_server's ITraceFilter, so nothing in server.so imports this TU.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kLibrarySource ) )
		<< kLibrarySource << " is back in server_sourcesharp; the server's trace "
		   "filter is managed's (TraceFilterRules) and this TU is the client's";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kLibrarySource ) )
		<< "and the client still compiles it, which is what makes the absence above a move";
}

TEST( TraceFilterLibrary, TheClientCompilesIt )
{
	ExpectCompiledOnce( cmake_graph::kClient, kLibrarySource );
	if ( ::testing::Test::HasFailure() )
		ADD_FAILURE() << "the client does not compile the trace filter library -- so every "
		   "client trace links against nothing, or against a definition this step deleted.";
}

TEST( TraceFilterLibrary, BothSidesCompileTheStaticsLibraryToo )
{
	ExpectCompiledOnce( cmake_graph::kServer, kStaticsSource );
	ExpectCompiledOnce( cmake_graph::kClient, kStaticsSource );
	if ( ::testing::Test::HasFailure() )
		ADD_FAILURE() << "entity_shared_statics.cpp holds EntitySim_IsSimulatingOnAlternateTicks "
		   "and the prediction-seed setter, and the client calls both.";
}

//=============================================================================//
// 3. THE ConVar TRAVELLED WITH THE FILTER IT IS READ BESIDE
//=============================================================================//

// plan_server_novalve.md P2.3b: ONLY THE CLIENT'S. The server reads the
// engine's own r_visualizetraces through SourceSharp_EngineVisualizeTraces(),
// which a server-only compile definition substitutes for the name in stock
// util_shared.h's extern and its inline reads.
TEST( TraceFilterLibrary, TheVisualiseConVarIsDefinedInTheLibraryForTheClient )
{
	// The client's own ConVar. The server's reader of the engine's
	// (SourceSharp_EngineVisualizeTraces) left with the server's native traces
	// at plan_server_native_v2 T4f.
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";
	EXPECT_EQ( 1u, CountOf( lib, "ConVar r_visualizetraces( \"r_visualizetraces\"" ) );
	EXPECT_EQ( 0u, CountOf( lib, "SourceSharp_EngineVisualizeTraces" ) );
}

static size_t EntriesCarrying( const std::string &strTarget, const std::string &strNeedle )
{
	const std::vector< cmake_graph::CompileEntry > entries = cmake_graph::TargetEntries( strTarget );
	size_t n = 0;
	for ( size_t i = 0; i < entries.size(); i++ )
		n += entries[ i ].strCommand.find( strNeedle ) != std::string::npos;
	return n;
}

TEST( TraceFilterLibrary, NoTuOnEitherSideTakesTheServersVisualiseMacro )
{
	// The macro turned util_shared.h's `extern ConVar r_visualizetraces` into a
	// read of the engine's for server TUs. No server TU traces through those
	// inlines any more (plan_server_native_v2 T4f), so the macro and the
	// function it named are gone; the client declares its own ConVar.
	ASSERT_GT( cmake_graph::TargetEntries( cmake_graph::kServer ).size(), 0u );
	EXPECT_EQ( 0u, EntriesCarrying( cmake_graph::kServer, "-Dr_visualizetraces=" ) )
		<< "the server's r_visualizetraces macro is back with nothing to name";
	EXPECT_EQ( 0u, EntriesCarrying( cmake_graph::kClient, "-Dr_visualizetraces=" ) )
		<< "the client declares its own r_visualizetraces and must not take a macro";
}

TEST( TraceFilterLibrary, TheVisualiseConVarIsDefinedNowhereElse )
{
	// The old spelling of this fact read util_shared.cpp for the absence. That
	// file is gone (Phase 2c, lane 2-utilshared), and an absence over a missing
	// file is vacuous -- so the absence is taken over the file that INHERITED
	// util_shared.cpp's residue, which is the one place a second definition
	// could plausibly land, and that file is proved read first.
	const std::string lib = ReadFile( kUtilSharedLib );
	ASSERT_FALSE( lib.empty() ) << kUtilSharedLib << " did not load";
	EXPECT_EQ( 0u, CountOf( lib, "ConVar r_visualizetraces( \"r_visualizetraces\"" ) )
		<< "r_visualizetraces is defined in " << kUtilSharedLib << " as well. "
		   "The three UTIL_Trace* inlines in util_shared.h read it AND construct "
		   "CTraceFilterSimple, so a second definition is a second setting under "
		   "one name and nothing reports which one a reader got.";
	EXPECT_FALSE( FileExists( kUtilShrC ) )
		<< kUtilShrC << " exists again and could define it a third time.";
}

//=============================================================================//
// 4. NOT ONE CALLER CHANGED
//=============================================================================//

TEST( TraceFilterLibrary, UtilSharedHeaderStillDeclaresEverythingThatMoved )
{
	const std::string h = ReadFile( kUtilShrH );
	ASSERT_FALSE( h.empty() ) << kUtilShrH << " did not load";

	for ( size_t i = 0; i < kNumDeclared; i++ )
	{
		EXPECT_EQ( 1u, CountOf( h, kStillDeclared[ i ] ) )
			<< "util_shared.h no longer declares: " << kStillDeclared[ i ];
	}
}

//=============================================================================//
// 5. THE TRACER HELPER TAKES NO ENTITY
//
// Step 4e's own rule for this cluster: a utility the shim still calls takes
// handles or is managed. EntityTracer_ComputeStartPosition took a raw
// CBaseEntity* and read it on no arm this build compiles, so the parameter's
// only effect was to require its callers to hold an entity pointer.
//=============================================================================//

TEST( TraceFilterLibrary, TheTracerStartHelperTakesNoShooter )
{
	const std::string h = ReadFile( "sourcesharp/game/shared/entity_shared_statics.h" );
	ASSERT_FALSE( h.empty() );

	EXPECT_EQ( 1u, CountOf( h,
		"void EntityTracer_ComputeStartPosition( const Vector &vecShotSrc, Vector *pVecTracerStart );" ) )
		<< "the declared shape changed";

	// The definition is the client's (plan_server_native_v2 T2, 2026-10-18): it
	// was #ifdef CLIENT_DLL in the shared entity_shared_statics.cpp.
	EXPECT_EQ( 1u, CountOf( ReadFile( "sourcesharp/game/client/client_util_library.cpp" ),
		"void EntityTracer_ComputeStartPosition( const Vector &vecShotSrc, Vector *pVecTracerStart )" ) )
		<< "the definition and the declaration disagree, or the definition moved";
	EXPECT_EQ( 0u, CountOf( ReadFile( kStatics ),
		"void EntityTracer_ComputeStartPosition(" ) )
		<< "the shared entity_shared_statics.cpp defines the client's tracer helper again";
}
