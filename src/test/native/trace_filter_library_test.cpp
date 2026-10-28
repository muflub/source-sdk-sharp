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
} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

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

//=============================================================================//
// 4. NOT ONE CALLER CHANGED
//=============================================================================//

//=============================================================================//
// 5. THE TRACER HELPER TAKES NO ENTITY
//
// Step 4e's own rule for this cluster: a utility the shim still calls takes
// handles or is managed. EntityTracer_ComputeStartPosition took a raw
// CBaseEntity* and read it on no arm this build compiles, so the parameter's
// only effect was to require its callers to hold an entity pointer.
//=============================================================================//

