//========= SourceSharp — no HTML panel is built at join =====================//
//
// Stock's info panel, CTextWindow (game_controls/vguitextwindow.cpp), builds a
// vgui::HTML at viewport Start, and that constructor's
// ISteamHTMLSurface::Init/CreateBrowser connects the game to Steam's web
// helper. When the helper dies, a reconnect can hit steamclient's
// Assert("Failed connecting to webhelper process") and Steam's crashhandler
// writes /tmp/dumps/assert_*.dmp (strace -k, 2026-10-05, main 1880e317).
//
// Main answered that by building the info panel on first use
// (deferred_viewport_panel.h). The client UI lane's viewport builds no stock
// panel at all: the MOTD, scoreboard, team menu and spectator screens are the
// managed UI's, and vgui_controls (where vgui::HTML lives) is off the link. So
// the intent -- nothing at join connects to the web helper -- is held here on
// the build graph instead of on a deferral:
//
//   * client.so compiles no stock viewport panel (nothing under
//     game/client/game_controls/), so nothing builds CTextWindow;
//   * client.so links no vgui_controls archive, so nothing can build a
//     vgui::HTML.
//
// Each absence is read through a reader shown to see the positive case first.
//
//=============================================================================//
#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <set>
#include <string>
#include <vector>

namespace
{
	// The client viewport this lane built: the reader must see it compiled, or
	// "absent" below is the answer of a reader that saw nothing.
	const char *const kViewport = "src/client/native/shell_viewport_client.cpp";

	const char *const kGameControls = "game/client/game_controls/";

	bool AnyUnder( const std::set< std::string > &compiled, const std::string &strPrefix, std::string &strHit )
	{
		for ( const std::string &s : compiled )
		{
			if ( s.compare( 0, strPrefix.size(), strPrefix ) == 0 )
			{
				strHit = s;
				return true;
			}
		}
		return false;
	}

	bool AnyToken( const std::vector< std::string > &tokens, const std::string &strNeedle, std::string &strHit )
	{
		for ( const std::string &t : tokens )
		{
			if ( t.find( strNeedle ) != std::string::npos )
			{
				strHit = t;
				return true;
			}
		}
		return false;
	}
}

TEST( NoHtmlPanelAtJoin, TheReaderSeesTheClientsOwnViewportCompiled )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, cmake_graph::RepoToFarm( kViewport ) ) )
		<< cmake_graph::kClient << " does not compile " << kViewport;
}

TEST( NoHtmlPanelAtJoin, NoStockViewportPanelIsCompiled )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kClient );
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, cmake_graph::RepoToFarm( kViewport ) ) );

	std::string strHit;
	EXPECT_FALSE( AnyUnder( compiled, kGameControls, strHit ) )
		<< cmake_graph::kClient << " compiles " << strHit
		<< ": a stock viewport panel is back (vguitextwindow.cpp's CTextWindow builds a vgui::HTML at Start)";
}

TEST( NoHtmlPanelAtJoin, TheLinkReaderSeesTheClientsLibraries )
{
	const std::vector< std::string > libs = cmake_graph::LinkLibraries( cmake_graph::kClient );
	std::string strHit;
	EXPECT_TRUE( AnyToken( libs, "tier1", strHit ) )
		<< "no tier1 on " << cmake_graph::kClient << "'s link line: the reader is not reading it";
}

TEST( NoHtmlPanelAtJoin, NoVguiControlsArchiveIsLinked )
{
	const std::vector< std::string > libs = cmake_graph::LinkLibraries( cmake_graph::kClient );
	std::string strHit;
	EXPECT_FALSE( AnyToken( libs, "vgui_controls", strHit ) )
		<< cmake_graph::kClient << " links " << strHit << ": vgui::HTML can be built again";
}
