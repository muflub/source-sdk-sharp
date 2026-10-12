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

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
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

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return StripComments( all );
}

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

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

// The viewport is the shim's since plan_client_novalve C3 (lane ui): the
// fork's baseviewport.cpp left client_sourcesharp, and the viewport is
// shell_viewport_client.cpp's raw panel, which builds no panel at all.
const File_t kViewport = { "sourcesharp/game/client/shell_viewport_client.cpp",
	"void CSourceSharpViewport::ShowPanel( const char *pName, bool state )" };

std::string Load( const File_t &f )
{
	const std::string s = ReadStripped( f.pszRel );
	EXPECT_FALSE( s.empty() ) << "could not open " << f.pszRel
		<< " — every absence invariant below would pass vacuously";
	EXPECT_NE( s.find( f.pszAnchor ), std::string::npos )
		<< f.pszRel << " does not contain its anchor \"" << f.pszAnchor
		<< "\"; the file this test reasons about has moved or been rewritten";
	return s;
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
TEST( CommentaryViewerEvicted, TheOnlyNewOfTheViewerIsInsideTheSourceSharpGuard )
{
	// READ THE ASSERTIONS, NOT THE NAME. There is no `new` here any more and no
	// guard for one to be inside of. The name is kept so the fact keeps its
	// identity across the rewrite; what it asserts now is the SUCCESSOR of what
	// it asserted before, and the successor is the stronger claim.
	//
	// WHAT REMOVED THE SUBJECT, named: the `residue` wave,
	// 74b071771..a46e8d645. At 74b071771 this file carried a
	// `#ifndef SOURCESHARP` region at
	// game/client/game_controls/baseviewport.cpp:342-357 holding the
	// PANEL_COMMENTARY_MODELVIEWER branch and its `new`, and a second
	// `#ifndef SOURCESHARP` at :42-44 holding the `#include` of the viewer`s
	// header. Both targets that build this tree define SOURCESHARP
	// (their target_compile_definitions), so NEITHER region was ever
	// compiled -- the wave deleted 19 lines here and the client`s object files
	// came out identical.
	//
	// THE SUCCESSOR PROPERTY. The eviction used to be CONDITIONAL: correct only
	// while a guard held, and one careless `#ifndef` edit away from an undefined
	// reference. It is now UNCONDITIONAL. baseviewport.cpp does not name the
	// viewer in ANY arm, so there is no guard to take off and no arm to lift the
	// `new` out of.
	//
	// That is an ABSENCE, which is why the PRESENCE half below is not optional:
	// an absence over a file that failed to open, or whose CreatePanelByName had
	// been gutted, passes for the wrong reason.
	const std::string flat = NoWhitespace( Load( kViewport ) );

	// PRESENCE. Since C3 the viewport's CreatePanelByName builds nothing, and
	// the fork's baseviewport.cpp, which built every stock panel, is out of the
	// client: the strongest form of "nothing here names the viewer".
	AssertReadsTarget( cmake_graph::kClient );
	if ( HasFatalFailure() )
		return;
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/game_controls/baseviewport.cpp" ) )
		<< "the fork's baseviewport.cpp is back in the client: it builds every stock "
		   "viewport panel by name, and this fact now reads the shim's viewport";

	// Every panel by name goes to the managed UI: that line is the viewport's
	// whole answer, and without it the absences below are about some other file.
	ASSERT_NE( flat.find( "pUi->ViewportPanel(pName,state?1:0);" ), std::string::npos )
		<< "the viewport no longer hands a panel by name to the managed UI";
	EXPECT_EQ( flat.find( "CreatePanelByName" ), std::string::npos )
		<< "the viewport builds panels by name again; every stock panel is a managed screen";

	// ABSENCE, in all three spellings the reference could come back through.
	EXPECT_EQ( flat.find( "CCommentaryModelViewer" ), std::string::npos )
		<< "baseviewport.cpp names the commentary model viewer again. The three "
		   "files it lives in are compiled by nothing in client_sourcesharp, so nothing "
		   "compiles them, and a reference from here is an undefined symbol at the "
		   "next link -- guard or no guard";

	EXPECT_EQ( flat.find( "PANEL_COMMENTARY_MODELVIEWER" ), std::string::npos )
		<< "the PANEL_COMMENTARY_MODELVIEWER branch is back in CreatePanelByName. "
		   "Even without a `new`, a branch for a panel no build can construct is how "
		   "the reference this eviction removed gets written back";

	EXPECT_EQ( flat.find( "commentary_modelviewer.h" ), std::string::npos )
		<< "baseviewport.cpp includes the viewer`s header again. That include was "
		   "the other half of what the residue wave deleted here, and an include of a "
		   "header whose .cpp is out of the build is exactly how the `new` comes back "
		   "without anybody noticing";
}

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

TEST( CommentaryViewerEvicted, NoPanelIsCreatedByNameExceptFromTheEvictedFileItself )
{
	const std::string flat = NoWhitespace( Load( kViewport ) );

	// CreateDefaultPanels asks for five panels by name and this is not one of
	// them: the only CreatePanelByName( PANEL_COMMENTARY_MODELVIEWER ) in the
	// tree was in commentary_modelviewer.cpp, behind the console command.
	EXPECT_EQ( flat.find( "CreatePanelByName(PANEL_COMMENTARY_MODELVIEWER)" ),
	           std::string::npos )
		<< "baseviewport.cpp now creates the commentary viewer by name itself, so "
		   "it is no longer reachable only from a console command — which is one "
		   "of the two legs this eviction stands on";

	// And no panel can be found by name, so nothing reaches one: the viewport's
	// FindPanelByName (its header) answers NULL.
	const std::string header = NoWhitespace( ReadStripped( "sourcesharp/game/client/shell_viewport_client.h" ) );
	ASSERT_FALSE( header.empty() ) << "cannot read the viewport's header";
	EXPECT_NE( header.find( "virtualIViewPortPanel*FindPanelByName(constchar*szPanelName)OVERRIDE{returnNULL;}" ),
	           std::string::npos )
		<< "the viewport finds a panel by name again; it holds none";
}
