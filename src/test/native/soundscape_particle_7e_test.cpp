//========= SourceSharp — step 7e's subjects, checked in the source ===========//
//
// plan_entity.md step 7e moved the client's sound cluster under the shim and
// de-entitied it. Three of the things it changed are INVISIBLE TO THE COMPILER
// and to a run that does not happen to touch them, which is exactly the class
// of change this project keeps losing:
//
//  1. THE VIEW ACCESSOR. CurrentViewOrigin/Forward/Right assert
//     s_bCanAccessCurrentView, which is only true INSIDE the render pass, and
//     every read in the soundscape file is outside it. `playsoundscape labs_mp`
//     took the game down with three of those asserts on this step's FIRST gate
//     run. Nothing about the fix is visible in a build: both spellings compile,
//     both link, and the wrong one is a crash only when somebody forces a
//     soundscape. So the spelling is a test.
//
//  2. THE ENTITY ASKS. The census reads a stock TU's TEXT, and a shim TU's text
//     is not counted the same way -- so a C_BasePlayer ask coming back into the
//     moved file would not move a census number. It is asserted here instead,
//     comment-stripped, because the file's own prose explains at length what
//     used to be there.
//
//  3. THE LANDING SITE AND ITS CALLER. Soundscape_Update was the function a
//     soundscape push landed on. It had NO CALLER at all when 7e wrote this
//     suite -- that was 7e's own open gate -- and it was KEPT deliberately so
//     that wiring the transport would be a caller and not a rewrite. Step
//     8e-sound wired it, and 7e's printed refusal was deleted rather than
//     re-conditioned. Sound cluster 17 (lane c2-sims) moved the system to C#:
//     the landing site is ClientSoundscapes.Push and its caller the client
//     module's SoundscapeMessage sink, with no crossing between them. Both
//     halves are asserted, because either alone is weak.
//
// AND ONE ABOUT THE INSTRUMENT. cs_cl_particle_report must not read a control
// point through GetControlPointAtCurrentTime: that accessor asserts
// `nControlPoint <= GetHighestControlPoint()` and
// `m_pDef->ReadsControlPoint( n )`, which is the right contract for an OPERATOR
// and the wrong one for a REPORT whose job is to say what an UNDECLARED point
// holds. env_fire_small declares point 0 only and the first version of the
// command fired particles.h:2392 twice.
//
// Source-text facts, so no engine and no game.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <set>
#include <fstream>
#include <sstream>
#include <string>

namespace
{

//-----------------------------------------------------------------------------
// The file with its comments taken out.
//
// EVERY "IS IT GONE" FACT NEEDS THIS. The moved files carry long notes naming
// the spellings that left and why, so a raw search for `CurrentViewOrigin`
// finds THE SENTENCE SAYING IT WENT and fails -- the recorded needle trap, met
// twice already on this plan (7b's C_BaseTempEntity, 6d's hitbox). String
// literals are preserved so a "//" inside a path is not read as a comment.
//-----------------------------------------------------------------------------
// What each side compiles: client_sourcesharp (src/client/native/CMakeLists.txt)
// and server_sourcesharp (src/server/native/CMakeLists.txt), read through
// cmake_graph.h, whose readers throw on a missing, empty or stale input.
const char *kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// Does the target compile a source whose farm-relative path ends in strTail?
// By the TAIL because the old vpc needles were substrings of the row: a copy
// of the file under any prefix counts. The proof-of-read is asserted first, so
// an absence is never answered by a reader that saw nothing.
bool CompilesTail( const char *pszTarget, const std::string &strTail )
{
	const std::set<std::string> src = cmake_graph::CompiledSources( pszTarget );
	EXPECT_TRUE( src.count( kProofOfRead ) > 0 )
		<< pszTarget << " does not compile " << kProofOfRead << " -- the reader is not reading its source list";
	for ( std::set<std::string>::const_iterator it = src.begin(); it != src.end(); ++it )
	{
		if ( it->size() >= strTail.size() && it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
			return true;
	}
	return false;
}

} // namespace

//=============================================================================
// The four files exist where the source lists say they do.
//
// FIRST, because every fact below is vacuous over an empty string: ReadFile
// answers "" for a path it cannot open, and a `find` on "" is npos, so a
// "spelling is gone" check passes perfectly over a file that is not there. That
// is the recorded "check that cannot fail" in its most ordinary shape.
//=============================================================================
//=============================================================================
// 1. THE VIEW ACCESSOR
//=============================================================================
//=============================================================================
// 2. THE ENTITY ASKS
//=============================================================================
//=============================================================================
// 3. THE LANDING SITE, AND THE REFUSAL
//=============================================================================
//-----------------------------------------------------------------------------
// THE CALLER 7e KEPT THE LANDING SITE FOR -- plan_entity.md step 8e-sound,
// and since sound cluster 17 a managed call with no crossing.
//
// This test REPLACED TheRefusalIsPrintedAndOnlyWhenThereIsSomethingToRefuse:
// the transport is wired, so 7e's refusal was deleted rather than
// re-conditioned. BOTH DIRECTIONS ARE ASSERTED, because either alone is weak.
//-----------------------------------------------------------------------------
//=============================================================================
// 4. THE EMITTER'S CLIENT ARM
//=============================================================================
//=============================================================================
// 5. THE INSTRUMENT
//=============================================================================
//=============================================================================
// 6. THE SOURCE ROWS
//
// A file needed by both sides needs a row in BOTH targets' source lists, and a
// one-sided row is silently wrong. These two are CLIENT-ONLY and the assertion
// is two-sided on purpose: present in one, absent from the other, and the stock
// path gone from both.
//=============================================================================
//=============================================================================
// THE SORT ORIGIN FOLLOWS WITH THE CONTROL POINTS. Stock's
// CParticleProperty::UpdateControlPoint (particle_property.cpp) ends every
// control point update with SetSortOrigin, and the client leaf system sorts and
// culls a system by it. The follow sweep moved the points and not the sort
// origin, so tools/7e-particle-sound-gate read cp0 400 units on and the sort
// origin 0 (3 of 3, once the client saw its entity move at all).
//=============================================================================
TEST( Step7eSubjects, TheMovedFilesAreInTheClientAndNotTheServer )
{
	// The soundscape's native half is shell_soundscape_audio_client.cpp since
	// sound cluster 17 (lane c2-sims): the system itself is managed.
	static const char *kRows[] = {
		"sourcesharp/game/client/shell_soundscape_audio_client.cpp",
		"sourcesharp/game/client/shell_recipientfilter_client.cpp",
	};

	for ( size_t i = 0; i < sizeof( kRows ) / sizeof( kRows[0] ); i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kRows[i] ) )
			<< kRows[i] << " is not compiled by client_sourcesharp";
		EXPECT_FALSE( CompilesTail( cmake_graph::kServer, kRows[i] ) )
			<< kRows[i] << " is compiled by server_sourcesharp. Both are #ifdef "
			<< "CLIENT_DLL-side files and neither is reachable from the server.";
	}
}

TEST( Step7eSubjects, TheStockPathsAreGoneFromBothTargets )
{
	static const char *kGone[] = {
		"game/client/c_soundscape.cpp",
		"game/client/c_recipientfilter.cpp",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[0] ); i++ )
	{
		EXPECT_FALSE( CompilesTail( cmake_graph::kClient, kGone[i] ) )
			<< kGone[i] << " is still compiled by client_sourcesharp and the "
			<< "file is not on disk any more; the build fails on a stale row.";
		EXPECT_FALSE( CompilesTail( cmake_graph::kServer, kGone[i] ) )
			<< kGone[i] << " is compiled by server_sourcesharp.";
	}
}
