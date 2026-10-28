//===== SourceSharp — the commentary model viewer leaves the client build =====//
//
// A VIEWER FOR A SUBSYSTEM THAT IS NOT IN THE BUILD. `commentary_showmodelviewer`
// opens an HL2 commentary authoring panel; both halves of the commentary system
// it exists to author for are excluded BY NAME already —
// `-$File CommentarySystem.cpp` in server_csharp.vpc, `-$File
// c_point_commentary_node.cpp` in client_csharp.vpc — so that command was the
// last commentary code in either DLL. Three files go:
//
//   game/client/commentary_modelviewer.cpp        CCommentaryModelViewer
//   game/client/game_controls/basemodelpanel.cpp  CModelPanel, CModelPanelModel
//   game/client/game_controls/basemodel_panel.cpp CBaseModelPanel
//
// WHY A VGUI PANEL WAS A C_BaseEntity CONSUMER AT ALL, which is the fact worth
// carrying away: `basemodelpanel.h:24` is `class CModelPanelModel : public
// C_BaseFlex`. A panel that previews a model does it by making an ENTITY and
// drawing it, so `basemodelpanel.o` carries a whole C_BaseFlex vtable — 120 of
// c_baseentity.o's symbol-needs, every one of them vtable-bound, none reachable
// by any retype. That is why this is in the eviction lane and not the retype one.
//
// WHAT THE LINK ALREADY AGREES ABOUT. `./evict_probe release client
// commentary_modelviewer.o basemodelpanel.o basemodel_panel.o` answered exit 1
// with exactly ONE undefined reference —
// `CCommentaryModelViewer::CCommentaryModelViewer(IViewPort*)`, baseviewport.cpp's
// `new` inside CreatePanelByName — and exit 0 in both configurations once that
// `new` went inside a `#ifndef SOURCESHARP`. Nothing else in the client named
// any of the three files' symbols, and a compiler would have said so.
//
// WHAT THE LINK CANNOT SEE, which is everything below:
//
// THE GUARD IS GONE AND SO IS THE REFERENCE -- READ THIS BEFORE THE PARAGRAPHS
// BELOW IT, WHICH ARE KEPT FOR THEIR REASONING AND NOT FOR THEIR TENSE.
//
// The `residue` wave (74b071771..a46e8d645) deleted the `#ifndef SOURCESHARP`
// region at game/client/game_controls/baseviewport.cpp:342-357, and the guarded
// `#include` at :42-44, as they stood at 74b071771. Both are dead source: every
// project that builds this tree defines SOURCESHARP (both targets'
// target_compile_definitions), so the compiler never saw either region and the
// client`s objects were unchanged by their removal.
//
// So the eviction no longer RESTS ON A GUARD. It rests on there being no
// reference: baseviewport.cpp does not name the class, the branch, or the
// header. Where the paragraph below says "both directions are asserted", the
// direction that is asserted now is that nothing here names the viewer at all,
// and TheOnlyNewOfTheViewerIsInsideTheSourceSharpGuard says why in place.
//
//   * THE GUARD WAS THE WHOLE EVICTION, WHILE THERE WAS ONE. Taking the
//     `#ifndef SOURCESHARP` off baseviewport.cpp made the build FAIL, loudly — but put the `new` back
//     OUTSIDE the guard while leaving the files excluded and it fails in a way
//     nobody reading client code would connect to a vpc line. Worse, the reverse
//     edit — un-excluding the three files and leaving the guard — compiles,
//     links, and silently reinstates 120 symbol-needs and a C_BaseFlex vtable
//     for a panel nothing can open. Both directions are asserted.
//
//   * GUARDING A CALLER IS NORMALLY THE WRONG WAY OUT. server_csharp.vpc said so
//     itself: three files that "were one call site apiece" got their function
//     LIFTED rather than their caller guarded, because "guarding would have
//     traded a file out of the build for behaviour out of the game". It is the
//     right way out here only because the behaviour was already gone, and that
//     is a claim with two legs, both asserted: the commentary system is out of
//     BOTH builds, and `CreateDefaultPanels` never asks for this panel — so the
//     only way to reach it was to type the console command.
//
//   * RETURNING NULL IS A SHAPE CreatePanelByName ALREADY HAD. Every other
//     branch in it is inside a `#ifndef`, and `AddNewPanel` answers a NULL panel
//     with a DevMsg rather than a fault. If that check goes, the guard stops
//     being harmless.
//
// WHAT IS DELIBERATELY GIVEN UP, said plainly rather than buried: the
// `commentary_showmodelviewer` console command. It loaded
// `Resource/UI/CommentaryModelViewer.res`, which mod_sharp does not ship.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose spells
// `#ifndef SOURCESHARP`, `CModelPanel` and `commentary_showmodelviewer` in the
// paragraphs explaining where they may and may not be. Same rule as
// effects_evicted_test.cpp and client_orphan_evictions_test.cpp.
//
// ANCHORS, NOT LINE COUNTS.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// WHAT EACH TARGET COMPILES is its source list, read through cmake_graph.h
// (compile_commands.json cross-checked against the target's CMakeLists). The
// proof-of-read is the PORT'S OWN shim, which no eviction can take and which
// this test does not otherwise assert about -- an anchor equal to the assertion
// cannot catch anything. (The vpc-era anchors moved off the `-$File` row when
// client_csharp.vpc was flattened, and off baseentity_shared.cpp at the flip.)
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// A reader that sees nothing cannot then answer "not compiled" for free.
void AssertReadsTarget( const char *pTarget )
{
	ASSERT_TRUE( cmake_graph::Compiles( pTarget, kProofOfRead ) )
		<< pTarget << " does not compile " << kProofOfRead
		<< " -- the reader is not reading that target's source list";
}

} // namespace

//-----------------------------------------------------------------------------
// The exclusion.
//-----------------------------------------------------------------------------
TEST( CommentaryViewerEvicted, TheClientExcludesTheViewerAndBothModelPanels )
{
	AssertReadsTarget( cmake_graph::kClient );
	if ( HasFatalFailure() )
		return;

	static const char *kPaths[] = {
		"game/client/commentary_modelviewer.cpp",
		"game/client/game_controls/basemodelpanel.cpp",
		"game/client/game_controls/basemodel_panel.cpp",
	};

	for ( size_t i = 0; i < sizeof( kPaths ) / sizeof( kPaths[ 0 ] ); i++ )
	{
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kPaths[ i ] ) )
			<< kPaths[ i ] << " is back in the client build. This one compiles and "
			   "links either way — the guard in baseviewport.cpp does not need the "
			   "file gone — so what comes back is a C_BaseFlex vtable and 120 "
			   "symbol-needs for a panel nothing can open, with no diagnostic";
	}
}

//-----------------------------------------------------------------------------
// The guard, which IS the eviction: it is the one reference the linker had.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The two legs under "the behaviour was already gone".
//-----------------------------------------------------------------------------
TEST( CommentaryViewerEvicted, TheCommentarySystemIsOutOfBothBuilds )
{
	AssertReadsTarget( cmake_graph::kServer );
	AssertReadsTarget( cmake_graph::kClient );
	if ( HasFatalFailure() )
		return;

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/server/CommentarySystem.cpp" ) )
		<< "the server's commentary system is back. The viewer this evicts exists "
		   "to author content for it, so its return is the one event that makes "
		   "this eviction worth re-taking";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/c_point_commentary_node.cpp" ) )
		<< "the client's commentary node is back — the other half of the same "
		   "system";
}

